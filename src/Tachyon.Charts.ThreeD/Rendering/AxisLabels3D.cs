using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Writes the numbers and titles of the axes. Text is drawn flat, so that it stays readable from every side.
/// This runs per frame while the view turns, so it keeps its lists instead of creating new ones.
/// </summary>
/// <param name="scene">The scene.</param>
internal sealed class AxisLabels3D(Scene3D scene) : ChartOverlay
{
    private const double LabelReach = 0.14;
    private const double TitleReach = 0.5;
    private const double LabelGap = 6;
    private const double ZOffset = 10;
    private const double HighestUpAxisView = 62;
    private const double ScaleWidth = 14;
    private const double ScaleHeight = 220;
    private const double ScaleMargin = 56;
    private const int ScaleSteps = 64;

    private readonly AxisText _x = new();
    private readonly AxisText _y = new();
    private readonly AxisText _z = new();

    /// <inheritdoc />
    protected override long ExternalVersion => scene.Camera.Version + ChartText.Version;

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        if (!scene.ShowsAxes)
        {
            return;
        }

        var pane = canvas.Layout.Panes[0];
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var walls = AxisBox3D.SelectWalls(scene.Camera.Eye);
        var view = new Projector(matrix, pane);
        _x.Update(scene.XAxis);
        _y.Update(scene.YAxis);
        DrawFloorAxis(canvas, _x, scene.XAxis, AxisBox3D.XEdge(walls), view);
        DrawFloorAxis(canvas, _y, scene.YAxis, AxisBox3D.YEdge(walls), view);

        // Seen from above the axis points at the eye, and its numbers would fall on one spot.
        if (Math.Abs(scene.Camera.Elevation) <= HighestUpAxisView)
        {
            _z.Update(scene.ZAxis);
            DrawUpAxis(canvas, AxisBox3D.ZEdge(walls, matrix), view);
        }

        if (scene.Antennas.Count > 0)
        {
            DrawAntenna(canvas, scene.Antennas[0], pane, view);
            return;
        }

        // The first surface whose colors come from a color scale shows it; a loop, as this runs every frame.
        foreach (var mesh in scene.Meshes)
        {
            if (mesh.ColorScale is { } scale)
            {
                DrawScale(canvas, scale, pane);
                return;
            }
        }
    }

    private static void DrawScale(OverlayCanvas canvas, ColorScale3D scale, DipRect pane)
    {
        var (x, top) = (pane.X + pane.Width - ScaleMargin, pane.Y + ((pane.Height - ScaleHeight) / 2));
        var step = ScaleHeight / ScaleSteps;
        for (var i = 0; i < ScaleSteps; i++)
        {
            canvas.FillRectangle(new DipRect(x, top + (i * step), ScaleWidth, step + 0.5), scale.Colormap.Sample(1 - ((i + 0.5) / ScaleSteps)));
        }

        var height = canvas.GetLineHeight(canvas.LabelSize);
        var middle = x + (ScaleWidth / 2);
        canvas.DrawText(scale.HighText, Math.Round(middle - (canvas.MeasureWidth(scale.HighText, canvas.LabelSize) / 2)), Math.Round(top - (height / 2)), canvas.LabelSize, canvas.TextColor);
        canvas.DrawText(scale.LowText, Math.Round(middle - (canvas.MeasureWidth(scale.LowText, canvas.LabelSize) / 2)), Math.Round(top + ScaleHeight + height), canvas.LabelSize, canvas.TextColor);
        if (scale.Title is { Length: > 0 } title)
        {
            canvas.DrawText(title, Math.Round(Math.Min(middle - (canvas.MeasureWidth(title, canvas.LabelSize) / 2), pane.X + pane.Width - canvas.MeasureWidth(title, canvas.LabelSize))), Math.Round(top - (height * 1.7)), canvas.LabelSize, canvas.TextColor);
        }
    }

    private void DrawAntenna(OverlayCanvas canvas, AntennaPattern3D antenna, DipRect pane, in Projector view)
    {
        if (!double.IsFinite(antenna.MainLobe.Gain))
        {
            return;
        }

        var (x, top) = (pane.X + pane.Width - ScaleMargin, pane.Y + ((pane.Height - ScaleHeight) / 2));
        var step = ScaleHeight / ScaleSteps;
        for (var i = 0; i < ScaleSteps; i++)
        {
            var color = antenna.Colormap.Sample(1 - ((i + 0.5) / ScaleSteps));
            canvas.FillRectangle(new DipRect(x, top + (i * step), ScaleWidth, step + 0.5), color);
        }

        var peak = antenna.PeakText;
        var floor = antenna.FloorText;
        var height = canvas.GetLineHeight(canvas.LabelSize);
        canvas.DrawText(peak, Math.Round(x + (ScaleWidth / 2) - (canvas.MeasureWidth(peak, canvas.LabelSize) / 2)), Math.Round(top - (height / 2)), canvas.LabelSize, canvas.TextColor);
        canvas.DrawText(floor, Math.Round(x + (ScaleWidth / 2) - (canvas.MeasureWidth(floor, canvas.LabelSize) / 2)), Math.Round(top + ScaleHeight + height), canvas.LabelSize, canvas.TextColor);

        var tip = antenna.MarkTip;
        var (tipX, tipY) = view.ToDips(new Point3D(AxisBox3D.ToBox(scene.XAxis, tip.X), AxisBox3D.ToBox(scene.YAxis, tip.Y), AxisBox3D.ToBox(scene.ZAxis, tip.Z)));
        canvas.DrawText(antenna.LobeText, Math.Round(tipX + LabelGap), Math.Round(tipY), canvas.LabelSize, canvas.TextColor);
    }

    private static Point3D Along(in LabelEdge edge, double place, double reach) =>
        edge.Start + ((edge.End - edge.Start) * ((place + 1) / 2)) + (edge.Outward * reach);

    private static void DrawFloorAxis(OverlayCanvas canvas, AxisText text, AxisBase axis, in LabelEdge edge, in Projector view)
    {
        var height = canvas.GetLineHeight(canvas.LabelSize);
        var (a, b) = (view.ToDips(edge.Start), view.ToDips(edge.End));
        var isWide = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
        for (var i = 0; i < text.Count; i++)
        {
            var place = view.ToDips(Along(edge, text.Places[i], LabelReach));
            text.Dips[i] = place;
            var width = canvas.MeasureWidth(text.Labels[i], canvas.LabelSize);
            text.Spans[i] = isWide ? (place.X - (width / 2), place.X + (width / 2)) : (place.Y - height, place.Y);
        }

        AxisBox3D.KeepApart(text.Spans.AsSpan(0, text.Count), LabelGap, text.Shown);
        for (var i = 0; i < text.Count; i++)
        {
            if (text.Shown[i])
            {
                var width = canvas.MeasureWidth(text.Labels[i], canvas.LabelSize);
                canvas.DrawText(text.Labels[i], Math.Round(text.Dips[i].X - (width / 2)), Math.Round(text.Dips[i].Y + (height / 3)), canvas.LabelSize, canvas.TextColor);
            }
        }

        if (axis.Title is { } title)
        {
            var place = view.ToDips(Along(edge, 0, TitleReach));
            canvas.DrawText(title, Math.Round(place.X - (canvas.MeasureWidth(title, canvas.LabelSize) / 2)), Math.Round(place.Y), canvas.LabelSize, canvas.TextColor);
        }
    }

    private void DrawUpAxis(OverlayCanvas canvas, in LabelEdge edge, in Projector view)
    {
        var text = _z;
        var height = canvas.GetLineHeight(canvas.LabelSize);
        for (var i = 0; i < text.Count; i++)
        {
            var place = view.ToDips(Along(edge, text.Places[i], 0));
            text.Dips[i] = place;
            text.Spans[i] = (place.Y - height, place.Y);
        }

        AxisBox3D.KeepApart(text.Spans.AsSpan(0, text.Count), LabelGap, text.Shown);
        for (var i = 0; i < text.Count; i++)
        {
            if (text.Shown[i])
            {
                var width = canvas.MeasureWidth(text.Labels[i], canvas.LabelSize);
                canvas.DrawText(text.Labels[i], Math.Round(text.Dips[i].X - width - ZOffset), Math.Round(text.Dips[i].Y + (height / 3)), canvas.LabelSize, canvas.TextColor);
            }
        }

        if (scene.ZAxis.Title is { } title)
        {
            var place = view.ToDips(edge.End);
            canvas.DrawText(title, Math.Round(place.X - (canvas.MeasureWidth(title, canvas.LabelSize) / 2)), Math.Round(place.Y - height), canvas.LabelSize, canvas.TextColor);
        }
    }

    private readonly struct Projector(Matrix3D matrix, DipRect pane)
    {
        private readonly Matrix3D _matrix = matrix;

        public (double X, double Y) ToDips(Point3D point)
        {
            var (x, y, _) = _matrix.Project(point);
            return (pane.X + ((x + 1) / 2 * pane.Width), pane.Y + ((1 - y) / 2 * pane.Height));
        }
    }

    /// <summary>The ticks of an axis with room for their places. It only changes with the range.</summary>
    private sealed class AxisText
    {
        private DoubleRange _range = DoubleRange.Empty;

        public int Count { get; private set; }

        public string[] Labels { get; private set; } = [];

        public double[] Places { get; private set; } = [];

        public (double X, double Y)[] Dips { get; private set; } = [];

        public (double Start, double End)[] Spans { get; private set; } = [];

        public bool[] Shown { get; private set; } = [];

        public void Update(AxisBase axis)
        {
            if (axis.VisibleRange == _range)
            {
                return;
            }

            _range = axis.VisibleRange;
            var ticks = AxisBox3D.GetTicks(axis);
            Count = ticks.Count;
            Labels = new string[Count];
            Places = new double[Count];
            Dips = new (double, double)[Count];
            Spans = new (double, double)[Count];
            Shown = new bool[Count];
            for (var i = 0; i < Count; i++)
            {
                Labels[i] = ticks[i].Label;
                Places[i] = AxisBox3D.ToBox(axis, ticks[i].Value);
            }
        }
    }
}
