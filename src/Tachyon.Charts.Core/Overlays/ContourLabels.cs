using System.Numerics;
using System.Runtime.CompilerServices;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>
/// Writes the values of contour lines along the lines. The places come from a coarse marching squares pass over the
/// values, made once per change of data or levels; each frame takes at most one place per level and tile of the
/// screen, upright along the line, and leaves out labels that would touch one already placed. A plate in the
/// color of the background hides the line under the text.
/// </summary>
internal static class ContourLabels
{
    private const int MostCells = 96;
    private const double Tile = 240;
    private const int MostLabels = 120;
    private const float Size = 11;
    private const float SameLevelDistance = 200;

    private static readonly ConditionalWeakTable<ContourSeries, Cache> _caches = [];

    // Runs every frame: an index loop, because an enumerator would allocate.
    public static bool Any(ChartSeries[] series)
    {
        for (var i = 0; i < series.Length; i++)
        {
            if (series[i] is ContourSeries { ShowsLabels: true })
            {
                return true;
            }
        }

        return false;
    }

    public static void Draw(OverlayCanvas canvas, ChartLayoutResult layout, ChartSeries[] series)
    {
        for (var i = 0; i < series.Length; i++)
        {
            if (series[i] is ContourSeries { ShowsLabels: true } contours)
            {
                DrawOne(canvas, layout, contours);
            }
        }
    }

    /// <summary>Finds the places on the lines: the middle of each crossing of a coarse cell, with its direction.</summary>
    internal static List<Place> FindPlaces(ContourSeries series)
    {
        var data = series.Data;
        var values = data.Values;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Max(data.Width, data.Height) / (double)MostCells));
        var places = new List<Place>();
        Span<Vector2> crossings = stackalloc Vector2[4];
        foreach (var level in series.LevelsInUse)
        {
            for (var row = 0; row + step < data.Height; row += step)
            {
                for (var column = 0; column + step < data.Width; column += step)
                {
                    // The corners of the coarse cell, clockwise from the top left.
                    var a = values[(row * data.Width) + column];
                    var b = values[(row * data.Width) + column + step];
                    var c = values[((row + step) * data.Width) + column + step];
                    var d = values[((row + step) * data.Width) + column];
                    if (float.IsNaN(a) || float.IsNaN(b) || float.IsNaN(c) || float.IsNaN(d))
                    {
                        continue;
                    }

                    var count = 0;
                    Cross(a, b, level, new Vector2(column, row), new Vector2(column + step, row), crossings, ref count);
                    Cross(b, c, level, new Vector2(column + step, row), new Vector2(column + step, row + step), crossings, ref count);
                    Cross(c, d, level, new Vector2(column + step, row + step), new Vector2(column, row + step), crossings, ref count);
                    Cross(d, a, level, new Vector2(column, row + step), new Vector2(column, row), crossings, ref count);
                    if (count == 2)
                    {
                        places.Add(new Place(level, (crossings[0] + crossings[1]) / 2, crossings[1] - crossings[0]));
                    }
                }
            }
        }

        return places;
    }

    /// <summary>Turns an angle so that text never stands on its head.</summary>
    internal static float Upright(float angle)
    {
        while (angle > MathF.PI / 2)
        {
            angle -= MathF.PI;
        }

        while (angle < -MathF.PI / 2)
        {
            angle += MathF.PI;
        }

        return angle;
    }

    private static void Cross(float from, float to, float level, Vector2 start, Vector2 end, Span<Vector2> crossings, ref int count)
    {
        if ((from < level) == (to < level) || count >= crossings.Length)
        {
            return;
        }

        var share = (level - from) / (to - from);
        crossings[count++] = start + ((end - start) * share);
    }

    private static void DrawOne(OverlayCanvas canvas, ChartLayoutResult layout, ContourSeries series)
    {
        var cache = _caches.GetOrCreateValue(series);
        var levels = series.LevelsInUse;
        if (cache.Version != series.Data.Version || cache.Levels != levels)
        {
            cache.Places = FindPlaces(series);
            cache.Version = series.Data.Version;
            cache.Levels = levels;
        }

        if (cache.Language != ChartText.Version)
        {
            cache.Texts.Clear();
            cache.Language = ChartText.Version;
        }

        var pane = layout.Panes[0];
        cache.Taken.Clear();
        cache.Placed.Clear();
        var line = canvas.GetLineHeight(Size);
        foreach (var place in cache.Places)
        {
            if (cache.Placed.Count >= MostLabels)
            {
                break;
            }

            var (x, y) = series.GridPoint(place.Grid.X, place.Grid.Y);
            var (x2, y2) = series.GridPoint(place.Grid.X + place.Direction.X, place.Grid.Y + place.Direction.Y);
            if (!canvas.TryGetPosition(series.XAxis, x, out var left) || !canvas.TryGetPosition(series.YAxis, y, out var top)
                || !canvas.TryGetPosition(series.XAxis, x2, out var right) || !canvas.TryGetPosition(series.YAxis, y2, out var bottom))
            {
                continue;
            }

            if (!cache.Texts.TryGetValue(place.Level, out var text))
            {
                text = place.Level.ToString("G4", ChartText.Culture);
                cache.Texts[place.Level] = text;
            }

            var half = (canvas.MeasureWidth(text, Size) / 2) + 2;
            var center = new Vector2((float)left, (float)top);
            if (center.X < pane.X + half || center.X > pane.Right - half || center.Y < pane.Y + half || center.Y > pane.Bottom - half)
            {
                continue;
            }

            // One label per level and tile, and none that would touch another.
            var tile = ((long)Math.Floor(center.X / Tile) * 1_000_003) + ((long)Math.Floor(center.Y / Tile) * 7919) + BitConverter.SingleToInt32Bits(place.Level);
            if (Touches(cache.Placed, center, half, place.Level) || !cache.Taken.Add(tile))
            {
                continue;
            }

            cache.Placed.Add((center, half, place.Level));
            var angle = Upright(MathF.Atan2((float)(bottom - top), (float)(right - left)));
            Plate(canvas, center, angle, half, (line / 2) + 1);
            canvas.DrawTurnedText(text, center, angle, Size, canvas.TextColor);
        }
    }

    // Labels never touch, and labels of the same line keep a wide distance.
    private static bool Touches(List<(Vector2 Center, float Radius, float Level)> placed, Vector2 center, float radius, float level)
    {
        foreach (var (other, size, otherLevel) in placed)
        {
            var distance = Vector2.Distance(other, center);
            if (distance < radius + size || (otherLevel == level && distance < SameLevelDistance))
            {
                return true;
            }
        }

        return false;
    }

    private static void Plate(OverlayCanvas canvas, Vector2 center, float angle, float halfWidth, float halfHeight)
    {
        var along = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var across = new Vector2(-along.Y, along.X);
        var a = center - (along * halfWidth) - (across * halfHeight);
        var b = center + (along * halfWidth) - (across * halfHeight);
        var c = center + (along * halfWidth) + (across * halfHeight);
        var d = center - (along * halfWidth) + (across * halfHeight);
        var color = canvas.BackgroundColor with { A = 170 };
        canvas.FillTriangle(a, b, c, color);
        canvas.FillTriangle(a, c, d, color);
    }

    /// <summary>A place on a line: its level, the point in grid units and the direction of the line there.</summary>
    internal readonly record struct Place(float Level, Vector2 Grid, Vector2 Direction);

    private sealed class Cache
    {
        public long Version { get; set; } = -1;

        public float[]? Levels { get; set; }

        public List<Place> Places { get; set; } = [];

        public HashSet<long> Taken { get; } = [];

        public Dictionary<float, string> Texts { get; } = [];

        public long Language { get; set; } = -1;

        public List<(Vector2 Center, float Radius, float Level)> Placed { get; } = [];
    }
}
