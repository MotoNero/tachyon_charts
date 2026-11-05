using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>Remembers what lies under the pointer.</summary>
/// <param name="scene">The scene.</param>
/// <param name="picker">The search.</param>
internal sealed class PointerPick3D(Scene3D scene, ScenePicker picker)
{
    private const double Reach = PickReach.Pixels;

    private long _version;

    /// <summary>Gets what lies under the pointer.</summary>
    public Pick3D? Current { get; private set; }

    /// <summary>Gets a number that changes with <see cref="Current"/>.</summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>Looks at a place.</summary>
    /// <param name="chart">The chart.</param>
    /// <param name="position">The place of the pointer.</param>
    public void Move(ChartSurface chart, Vector2 position)
    {
        Current = chart.CurrentLayout is { Panes.Count: > 0 } layout ? Find(layout.Panes[0], position) : null;
        Interlocked.Increment(ref _version);
        scene.Group?.ShowCursor(Current?.Position.X);
    }

    /// <summary>Hides the value.</summary>
    public void Hide()
    {
        if (Current is not null)
        {
            Current = null;
            Interlocked.Increment(ref _version);
            scene.Group?.ShowCursor(null);
        }
    }

    /// <summary>Finds what lies at a place of the pane.</summary>
    /// <param name="pane">The pane.</param>
    /// <param name="position">The place.</param>
    /// <returns>The hit, if any.</returns>
    public Pick3D? Find(DipRect pane, Vector2 position)
    {
        var (x, y) = (((position.X - pane.X) / pane.Width * 2) - 1, 1 - ((position.Y - pane.Y) / pane.Height * 2));
        var ray = scene.Camera.CreateRay(pane.Width / pane.Height, x, y);
        var beside = scene.Camera.CreateRay(pane.Width / pane.Height, x + (2 * Reach / pane.Width), y);
        return picker.Find(ray, new PickReach((beside.Start - ray.Start).Length, (beside.Direction - ray.Direction).Length));
    }
}

/// <summary>Shows the value under the pointer and the markers, flat over the picture.</summary>
/// <param name="scene">The scene.</param>
/// <param name="pointer">What lies under the pointer.</param>
internal sealed class PickLabels3D(Scene3D scene, PointerPick3D pointer) : ChartOverlay
{
    private const double Gap = 10;
    private const double Inset = 6;
    private const float Dot = 3.5f;

    private readonly string[] _rows = new string[4];
    private readonly System.Numerics.Vector4[] _table = new System.Numerics.Vector4[64];
    private readonly List<TransferPoint3D> _points = [];
    private readonly List<double> _frequencies = [];

    /// <inheritdoc />
    protected override long ExternalVersion => pointer.Version + scene.Camera.Version + scene.MarkerVersion + ChartText.Version + scene.Version + SpectrumLinks.Stamp(scene);

    /// <summary>Writes the rows of the label.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="pick">The hit.</param>
    /// <param name="rows">Receives three rows.</param>
    internal static void WriteRows(Scene3D scene, in Pick3D pick, string[] rows)
    {
        if (pick.Source is AntennaPattern3D antenna)
        {
            var p = pick.Position;
            var azimuth = double.RadiansToDegrees(Math.Atan2(p.Y, p.X));
            var elevation = double.RadiansToDegrees(Math.Asin(Math.Clamp(p.Length > 0 ? p.Z / p.Length : 0, -1, 1)));
            rows[0] = ChartText.Get(ChartTextKey.Azimuth) + ": " + azimuth.ToString("0.0", ChartText.Culture) + "°";
            rows[1] = ChartText.Get(ChartTextKey.Elevation) + ": " + elevation.ToString("0.0", ChartText.Culture) + "°";
            rows[2] = ChartText.Get(ChartTextKey.Gain) + ": " + (antenna.Floor + p.Length).ToString("0.00", ChartText.Culture) + " dB";
            if (rows.Length > 3)
            {
                rows[3] = string.Empty;
            }

            return;
        }

        rows[0] = Row(scene.XAxis, "X", pick.Position.X);
        rows[1] = Row(scene.YAxis, "Y", pick.Position.Y);
        rows[2] = Row(scene.ZAxis, "Z", pick.Position.Z);
        if (rows.Length > 3)
        {
            rows[3] = pick.Value is { } value ? ChartText.Get(ChartTextKey.Value) + ": " + value.ToString("G6", ChartText.Culture) : string.Empty;
        }
    }

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        var pane = canvas.Layout.Panes[0];
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var markers = scene.Markers;
        for (var i = 0; i < markers.Count; i++)
        {
            DrawPick(canvas, pane, matrix, markers[i]);
            DrawName(canvas, pane, matrix, markers[i].Box, i + 1, canvas.AccentColor);
        }

        DrawSpectrumMarkers(canvas, pane, matrix);

        if (pointer.Current is { } pick)
        {
            DrawPick(canvas, pane, matrix, pick);
        }

        foreach (var cloud in scene.Clouds)
        {
            if (cloud.ShowsProgress)
            {
                DrawProgress(canvas, pane, cloud.Progress);
                break;
            }
        }

        foreach (var volume in scene.Volumes)
        {
            if (volume.Mode == VolumeMode.Surface)
            {
                DrawLevel(canvas, TransferGrip.Area(pane), volume);
                break;
            }

            if (volume.Transfer is { } transfer)
            {
                DrawEditor(canvas, TransferGrip.Area(pane), transfer);
                break;
            }
        }
    }

    // A colored bar across the value range with the slider at the value of the surface.
    private static void DrawLevel(OverlayCanvas canvas, DipRect area, Volume3D volume)
    {
        const int Columns = 64;
        const double Height = 14;
        var bar = new DipRect(area.X, area.Y + ((area.Height - Height) / 2), area.Width, Height);
        var width = bar.Width / Columns;
        for (var i = 0; i < Columns; i++)
        {
            canvas.FillRectangle(new DipRect(bar.X + (i * width), bar.Y, width + 0.5, bar.Height), volume.Colormap.Sample((i + 0.5) / Columns));
        }

        canvas.DrawRectangle(bar, canvas.TextColor);
        var x = (float)(bar.X + (volume.IsoShare * bar.Width));
        canvas.DrawLine(new Vector2(x, (float)bar.Y - 6), new Vector2(x, (float)(bar.Y + bar.Height + 6)), 4, canvas.TextColor);
        var text = volume.IsoValue.ToString("G6", ChartText.Culture);
        canvas.DrawText(text, Math.Round(x - (canvas.MeasureWidth(text, canvas.LabelSize) / 2)), Math.Round(bar.Y + bar.Height + 22), canvas.LabelSize, canvas.TextColor);
    }

    private static void DrawProgress(OverlayCanvas canvas, DipRect pane, double progress)
    {
        const double Width = 220;
        const double Height = 4;
        const double Top = 44;
        var (x, y) = (pane.X + ((pane.Width - Width) / 2), pane.Y + Top);
        var text = ChartText.Get(ChartTextKey.Loading) + " " + ((int)(progress * 100)).ToString(ChartText.Culture) + " %";
        canvas.DrawText(text, Math.Round(x), Math.Round(y - 6), canvas.LabelSize, canvas.TextColor);
        canvas.FillRectangle(new DipRect(x, y, Width, Height), canvas.BackgroundColor);
        canvas.DrawRectangle(new DipRect(x, y, Width, Height), canvas.TextColor);
        canvas.FillRectangle(new DipRect(x, y, Width * progress, Height), canvas.AccentColor);
    }

    private void DrawEditor(OverlayCanvas canvas, DipRect area, TransferFunction3D transfer)
    {
        const float Handle = 4;
        var (table, points) = (_table, _points);
        transfer.WriteTable(table);
        transfer.ReadPoints(points);
        canvas.FillRectangle(area, canvas.BackgroundColor);
        var width = area.Width / table.Length;
        for (var i = 0; i < table.Length; i++)
        {
            var (color, height) = (table[i], area.Height * table[i].W);
            var shade = new Tachyon.Charts.Rendering.Rgba8((byte)(color.X * 255), (byte)(color.Y * 255), (byte)(color.Z * 255), 255);
            canvas.FillRectangle(new DipRect(area.X + (i * width), area.Y + area.Height - height, width + 0.5, height), shade);
        }

        canvas.DrawRectangle(area, canvas.TextColor);
        for (var i = 0; i < points.Count; i++)
        {
            var place = TransferGrip.ToDips(area, points[i]);
            canvas.DrawLine(place - new Vector2(Handle, 0), place + new Vector2(Handle, 0), 2 * Handle, canvas.TextColor);
            if (i > 0)
            {
                canvas.DrawLine(TransferGrip.ToDips(area, points[i - 1]), place, 1.5f, canvas.TextColor);
            }
        }
    }

    private static string Row(AxisBase axis, string name, double value)
    {
        if (axis is not NumericAxis numeric)
        {
            return (axis.Title ?? name) + ": " + axis.FormatCursorValue(value);
        }

        var format = numeric.LabelFormat;
        var unit = format.Unit.Length > 0 ? " " + format.Unit : string.Empty;
        return (axis.Title ?? name) + ": " + value.ToString("G6", format.Culture ?? ChartText.Culture) + unit;
    }

    // The markers of a spectrum behind a waterfall carry the numbers of the 2D chart at the top of their posts.
    private void DrawSpectrumMarkers(OverlayCanvas canvas, DipRect pane, Matrix3D matrix)
    {
        var map = new BoxMap(scene);
        foreach (var waterfall in scene.Waterfalls)
        {
            if (waterfall.Display is not { } display)
            {
                continue;
            }

            SpectrumLinks.ReadMarkers(display, _frequencies);
            for (var i = 0; i < _frequencies.Count; i++)
            {
                var box = map.ToBox(SpectrumLinks.Top(waterfall, display, _frequencies[i]));
                if (Math.Abs(box.X) <= 1)
                {
                    DrawName(canvas, pane, matrix, box with { Z = Math.Clamp(box.Z, -1, 1) }, i + 1, SpectrumLinks.MarkerColor);
                }
            }
        }
    }

    private static void DrawName(OverlayCanvas canvas, DipRect pane, Matrix3D matrix, Point3D box, int number, Tachyon.Charts.Rendering.Rgba8 color)
    {
        var (x, y, _) = matrix.Project(box);
        var (dipX, dipY) = (pane.X + ((x + 1) / 2 * pane.Width), pane.Y + ((1 - y) / 2 * pane.Height));
        canvas.DrawText(SpectrumLinks.Name(number), Math.Round(dipX - 10), Math.Round(dipY - 6), canvas.LabelSize, color);
    }

    private void DrawPick(OverlayCanvas canvas, DipRect pane, Matrix3D matrix, in Pick3D pick)
    {
        var (x, y, _) = matrix.Project(pick.Box);
        var (dipX, dipY) = (pane.X + ((x + 1) / 2 * pane.Width), pane.Y + ((1 - y) / 2 * pane.Height));
        var center = new Vector2((float)dipX, (float)dipY);
        canvas.DrawLine(center - new Vector2(Dot, 0), center + new Vector2(Dot, 0), Dot, canvas.AccentColor);
        canvas.DrawLine(center - new Vector2(0, Dot), center + new Vector2(0, Dot), Dot, canvas.AccentColor);

        WriteRows(scene, pick, _rows);
        var height = canvas.GetLineHeight(canvas.LabelSize);
        var count = _rows[3].Length > 0 ? 4 : 3;
        var width = 0.0;
        for (var i = 0; i < count; i++)
        {
            width = Math.Max(width, canvas.MeasureWidth(_rows[i], canvas.LabelSize));
        }

        width += 2 * Inset;
        var total = (count * height) + (2 * Inset);
        var left = Math.Min(dipX + Gap, pane.X + pane.Width - width);
        var top = Math.Max(Math.Min(dipY - total - Gap, pane.Y + pane.Height - total), pane.Y);
        var area = new DipRect(Math.Round(left), Math.Round(top), width, total);
        canvas.FillRectangle(area, canvas.BackgroundColor);
        canvas.DrawRectangle(area, canvas.AccentColor);
        for (var i = 0; i < count; i++)
        {
            canvas.DrawText(_rows[i], Math.Round(area.X + Inset), Math.Round(area.Y + Inset + ((i + 0.8) * height)), canvas.LabelSize, canvas.TextColor);
        }
    }
}
