using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.ThreeD.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD;

/// <summary>Shows the markers of the 3D chart in the 2D chart of a cut, with the same numbers.</summary>
/// <param name="feed">The cut.</param>
/// <param name="xAxis">The axis along the cut.</param>
/// <param name="yAxis">The axis of the values.</param>
public sealed class CutMarkerOverlay(CutCurveFeed feed, AxisBase xAxis, AxisBase yAxis) : ChartOverlay
{
    private const float Size = 6;

    /// <inheritdoc />
    protected override long ExternalVersion => feed.MarkerVersion;

    /// <summary>Creates the overlay for the first axes of a chart.</summary>
    /// <param name="feed">The cut.</param>
    /// <param name="chart">The 2D chart.</param>
    /// <returns>The overlay.</returns>
    public static CutMarkerOverlay For(CutCurveFeed feed, ChartSurface chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        return new CutMarkerOverlay(feed, chart.HorizontalAxes[0], chart.Panes[0].Axes[0]);
    }

    /// <inheritdoc />
    protected override void Draw(OverlayCanvas canvas)
    {
        var markers = feed.Markers;
        for (var i = 0; i < markers.Count; i++)
        {
            if (canvas.TryGetPosition(xAxis, markers[i].Along, out var x) && canvas.TryGetPosition(yAxis, markers[i].Value, out var y))
            {
                var point = new Vector2((float)x, (float)y);
                canvas.FillTriangle(point, point + new Vector2(-Size, -Size * 1.5f), point + new Vector2(Size, -Size * 1.5f), SpectrumLinks.MarkerColor);
                var name = SpectrumLinks.Name(markers[i].Number);
                canvas.DrawText(name, Math.Round(x - 3), Math.Round(y - (Size * 2)), canvas.LabelSize, SpectrumLinks.MarkerColor);
            }
        }
    }
}

/// <summary>
/// Sets and removes markers in the 2D chart of a cut, and the 3D chart shows them at once: Ctrl+click sets a marker or
/// removes the one under the pointer, Delete removes the newest.
/// </summary>
/// <param name="feed">The cut.</param>
public sealed class CutMarkerModifier(CutCurveFeed feed) : ChartModifier(DefaultPriority)
{
    /// <summary>The priority, before navigation.</summary>
    public const int DefaultPriority = 26;

    private const float Reach = 10;
    private const float ClickReach = 4;

    private Vector2? _pressed;

    /// <inheritdoc />
    protected override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary && (input.Modifiers & InputModifiers.Control) != 0)
        {
            _pressed = input.Position;
            return ModifierResult.Capture;
        }

        if (_pressed is not { } pressed)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            _pressed = null;
            if (Vector2.Distance(pressed, input.Position) <= ClickReach)
            {
                Toggle(input.Position);
            }

            return ModifierResult.Release;
        }

        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (input.Kind != KeyEventKind.Pressed || input.Key != ChartKey.Delete || feed.Markers.Count == 0)
        {
            return ModifierResult.Ignored;
        }

        feed.RemoveNewestMarker();
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected override void OnCaptureLost(InputCapture capture) => _pressed = null;

    private void Toggle(Vector2 position)
    {
        if (Interaction?.Chart is not { CurrentLayout: { Panes.Count: > 0 } layout } chart)
        {
            return;
        }

        var pane = layout.Panes[0];
        var transform = chart.HorizontalAxes[0].CreateTransform(pane.X, pane.Width);
        var along = transform.ToData(position.X);
        var reach = Math.Abs(transform.ToData(position.X + Reach) - along);
        if (!feed.RemoveMarkerNear(along, reach))
        {
            feed.AddMarker(along);
        }

        chart.Invalidate();
    }
}
