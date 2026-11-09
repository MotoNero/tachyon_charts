using System.Numerics;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// The values of the objects that ask for them (<c>ShowsValues</c>), flat over the picture above their points: the
/// height of a column, a surface or a measured point. Large data are sampled evenly, and a label that would cover one
/// drawn before is left out.
/// </summary>
/// <param name="scene">The scene.</param>
internal sealed class ValueLabels3D(Scene3D scene) : ChartOverlay
{
    // About this many places per object are looked at; more would cover each other anyway.
    private const int Budget = 600;
    // At most three decimals and thousands grouped: 913, 0.5, 1,250.25; tiny values read as 0.
    private const string ValueFormat = "#,0.###";
    private const float Padding = 3;
    private const float Distance = 4;

    private readonly List<(Point3D Place, double Value)> _labels = [];
    private readonly List<DipRect> _placed = [];
    private readonly char[] _text = new char[48];

    /// <inheritdoc />
    protected override long ExternalVersion => scene.Camera.Version + scene.Version + ChartText.Version;

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        _labels.Clear();
        Collect();
        if (_labels.Count == 0)
        {
            return;
        }

        _placed.Clear();
        var pane = canvas.Layout.Panes[0];
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var line = canvas.GetLineHeight(canvas.LabelSize);
        var back = canvas.BackgroundColor with { A = 190 };
        foreach (var (place, value) in _labels)
        {
            var box = new Point3D(AxisBox3D.ToBox(scene.XAxis, place.X), AxisBox3D.ToBox(scene.YAxis, place.Y), AxisBox3D.ToBox(scene.ZAxis, place.Z));
            var (x, y, depth) = matrix.Project(box);
            if (Math.Abs(x) > 1 || Math.Abs(y) > 1 || depth is < -1 or > 1 || !value.TryFormat(_text, out var written, ValueFormat, ChartText.Culture))
            {
                continue;
            }

            var point = new Vector2((float)(pane.X + ((x + 1) / 2 * pane.Width)), (float)(pane.Y + ((1 - y) / 2 * pane.Height)));
            var text = _text.AsSpan(0, written);
            var width = canvas.MeasureWidth(text, canvas.LabelSize) + (2 * Padding);
            var area = new DipRect(point.X - (width / 2), point.Y - Distance - line - Padding, width, line + Padding);
            if (Covers(area))
            {
                continue;
            }

            _placed.Add(area);
            canvas.FillRectangle(area, back);
            canvas.DrawText(text, Math.Round(area.X + Padding), Math.Round(area.Y + (Padding / 2) + (line * 0.8)), canvas.LabelSize, canvas.TextColor);
        }
    }

    private static int Stride(long count) => (int)Math.Max(1, Math.Ceiling((double)count / Budget));

    private static int GridStride(int columns, int rows) => (int)Math.Max(1, Math.Ceiling(Math.Sqrt((double)columns * rows / Budget)));

    private void Collect()
    {
        for (var n = 0; n < scene.Bars.Count; n++)
        {
            var bars = scene.Bars[n];
            if (bars.ShowsValues)
            {
                // The top of each column, in the middle of its cell.
                var (data, step) = (bars.Data, GridStride(bars.Data.Width, bars.Data.Height));
                for (var row = 0; row < data.Height; row += step)
                {
                    for (var column = 0; column < data.Width; column += step)
                    {
                        var value = data[column, row];
                        if (float.IsFinite(value))
                        {
                            var place = new Point3D(bars.X.Min + ((column + 0.5) * bars.X.Length / data.Width), bars.Y.Min + ((row + 0.5) * bars.Y.Length / data.Height), value);
                            _labels.Add((place, value));
                        }
                    }
                }
            }
        }

        for (var n = 0; n < scene.Surfaces.Count; n++)
        {
            var surface = scene.Surfaces[n];
            if (surface.ShowsValues)
            {
                // The corners of the grid, evenly thinned.
                var (data, step) = (surface.Data, GridStride(surface.Data.Width, surface.Data.Height));
                for (var row = 0; row < data.Height; row += step)
                {
                    for (var column = 0; column < data.Width; column += step)
                    {
                        var value = data[column, row];
                        if (float.IsFinite(value))
                        {
                            var place = new Point3D(surface.X.Min + (column * surface.X.Length / Math.Max(data.Width - 1, 1)), surface.Y.Min + (row * surface.Y.Length / Math.Max(data.Height - 1, 1)), value);
                            _labels.Add((place, value));
                        }
                    }
                }
            }
        }

        for (var n = 0; n < scene.Lines.Count; n++)
        {
            var line = scene.Lines[n];
            if (line.ShowsValues)
            {
                AddPoints(line.Points);
            }
        }

        for (var n = 0; n < scene.Scatters.Count; n++)
        {
            var scatter = scene.Scatters[n];
            if (scatter.ShowsValues)
            {
                AddPoints(scatter.Points);
            }
        }

        for (var n = 0; n < scene.Bubbles.Count; n++)
        {
            var bubbles = scene.Bubbles[n];
            if (bubbles.ShowsValues)
            {
                AddArrays(bubbles.X, bubbles.Y, bubbles.Z);
            }
        }

        for (var n = 0; n < scene.Maps.Count; n++)
        {
            var map = scene.Maps[n];
            if (map.ShowsValues)
            {
                AddArrays(map.X, map.Y, map.Z);
            }
        }

        for (var n = 0; n < scene.Clouds.Count; n++)
        {
            var cloud = scene.Clouds[n];
            if (cloud.ShowsValues)
            {
                AddArrays(cloud.X, cloud.Y, cloud.Z);
            }
        }
    }

    private void AddPoints(Points3D points)
    {
        var count = points.Count;
        for (var i = 0; i < count; i += Stride(count))
        {
            var point = points.Read(i);
            if (double.IsFinite(point.Z))
            {
                _labels.Add((point, point.Z));
            }
        }
    }

    private void AddArrays(double[] x, double[] y, double[] z)
    {
        var count = Math.Min(x.Length, Math.Min(y.Length, z.Length));
        for (var i = 0; i < count; i += Stride(count))
        {
            if (double.IsFinite(z[i]))
            {
                _labels.Add((new Point3D(x[i], y[i], z[i]), z[i]));
            }
        }
    }

    private bool Covers(in DipRect area)
    {
        for (var n = 0; n < _placed.Count; n++)
        {
            var other = _placed[n];
            if (area.X < other.X + other.Width && other.X < area.X + area.Width && area.Y < other.Y + other.Height && other.Y < area.Y + area.Height)
            {
                return true;
            }
        }

        return false;
    }
}
