using System.Numerics;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Writes the labels of the pies in 3D, flat over the picture, as a pie in 2D does: on the slice when it is wide
/// enough, otherwise beside the pie with a line to its edge, spread so that no two labels overlap.
/// </summary>
/// <param name="scene">The scene.</param>
internal sealed class PieLabels3D(Scene3D scene) : ChartOverlay
{
    private const double Inset = 4;
    private const double InsideShare = 0.06;
    private const double Gap = 14;

    private readonly List<Outside> _left = [];
    private readonly List<Outside> _right = [];
    private readonly List<DipRect> _placed = [];

    /// <inheritdoc />
    protected override long ExternalVersion
    {
        get
        {
            var version = scene.Camera.Version + scene.Version + ChartText.Version;
            foreach (var pie in scene.Pies)
            {
                version += pie.Version;
            }

            return version;
        }
    }

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        var pane = canvas.Layout.Panes[0];
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var line = canvas.GetLineHeight(canvas.LabelSize);
        foreach (var pie in scene.Pies)
        {
            _left.Clear();
            _right.Clear();
            _placed.Clear();
            var (minimum, maximum) = Extent(pie, pane, matrix);
            var center = ToScreen(pane, matrix, new Point3D(0, 0, pie.Height / 2));
            for (var i = 0; i < pie.Values.Count; i++)
            {
                if (pie.LabelText(i) is not { Length: > 0 } text || pie.LabelPoint(i) is not { } inside || pie.TopPoint(i, 1) is not { } edge)
                {
                    continue;
                }

                var width = canvas.MeasureWidth(text, canvas.LabelSize);
                // On the slice when it is wide enough and the label covers none placed before; slices seen flat at the back may not.
                var place = ToScreen(pane, matrix, inside);
                var area = Area(width, line, place.X - (width / 2), place.Y);
                if (pie.GetShare(i) >= InsideShare && !_placed.Exists(other => Overlap(other, area)))
                {
                    _placed.Add(area);
                    Label(canvas, text, width, line, place.X - (width / 2), place.Y);
                    continue;
                }

                var anchor = ToScreen(pane, matrix, edge);
                (anchor.X < center.X ? _left : _right).Add(new Outside(text, width, anchor, anchor.Y));
            }

            Spread(canvas, _left, pane, line, minimum - Gap, isLeft: true);
            Spread(canvas, _right, pane, line, maximum + Gap, isLeft: false);
        }
    }

    private static bool Overlap(DipRect a, DipRect b) => a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static DipRect Area(double width, double line, double left, double middle) =>
        new(Math.Round(left - Inset), Math.Round(middle - (line / 2) - (Inset / 2)), width + (2 * Inset), line + Inset);

    private static void Label(OverlayCanvas canvas, string text, double width, double line, double left, double middle)
    {
        var area = Area(width, line, left, middle);
        canvas.FillRectangle(area, canvas.BackgroundColor with { A = 190 });
        canvas.DrawText(text, Math.Round(area.X + Inset), Math.Round(area.Y + (Inset / 2) + (line * 0.8)), canvas.LabelSize, canvas.TextColor);
    }

    // The left and right edge of the pie on the screen, from points around its rim.
    private (double Minimum, double Maximum) Extent(Pie3D pie, DipRect pane, Matrix3D matrix)
    {
        var (minimum, maximum) = (double.MaxValue, double.MinValue);
        var reach = 1 + pie.ExplodeDistance;
        for (var step = 0; step < 72; step++)
        {
            var angle = step * Math.PI / 36;
            foreach (var z in (ReadOnlySpan<double>)[-pie.Height / 2, pie.Height / 2])
            {
                var point = ToScreen(pane, matrix, new Point3D(Math.Cos(angle) * reach, Math.Sin(angle) * reach, z));
                (minimum, maximum) = (Math.Min(minimum, point.X), Math.Max(maximum, point.X));
            }
        }

        return (minimum, maximum);
    }

    // The labels of one side top to bottom, at least one line apart, then back up from the bottom, each with its line.
    private static void Spread(OverlayCanvas canvas, List<Outside> labels, DipRect pane, double line, double column, bool isLeft)
    {
        if (labels.Count == 0)
        {
            return;
        }

        labels.Sort(static (a, b) => a.Y.CompareTo(b.Y));
        var step = line + Inset + 2;
        var y = new double[labels.Count];
        for (var i = 0; i < y.Length; i++)
        {
            y[i] = Math.Max(labels[i].Y, i == 0 ? pane.Y + step : y[i - 1] + step);
        }

        for (var i = y.Length - 1; i >= 0; i--)
        {
            y[i] = Math.Min(y[i], i == y.Length - 1 ? pane.Bottom - step : y[i + 1] - step);
        }

        var color = canvas.TextColor with { A = 150 };
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            canvas.DrawLine(label.Anchor, new Vector2((float)column, (float)y[i]), 1, color);
            Label(canvas, label.Text, label.Width, line, isLeft ? column - Inset - label.Width : column + Inset, y[i]);
        }
    }

    // From data units through the box of the axes onto the screen.
    private Vector2 ToScreen(DipRect pane, Matrix3D matrix, Point3D point)
    {
        var box = new Point3D(AxisBox3D.ToBox(scene.XAxis, point.X), AxisBox3D.ToBox(scene.YAxis, point.Y), AxisBox3D.ToBox(scene.ZAxis, point.Z));
        var (x, y, _) = matrix.Project(box);
        return new Vector2((float)(pane.X + ((x + 1) / 2 * pane.Width)), (float)(pane.Y + ((1 - y) / 2 * pane.Height)));
    }

    private readonly record struct Outside(string Text, double Width, Vector2 Anchor, double Y);
}
