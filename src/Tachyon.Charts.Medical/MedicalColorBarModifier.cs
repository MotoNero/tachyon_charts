using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;

namespace Tachyon.Charts.Medical;

/// <summary>
/// Makes the grayscale bar beside a medical image set the window: the wheel widens or narrows it around its centre,
/// dragging moves it as if the bar were pulled, and a double click goes back to the window the image came with. The bar
/// alone would only change its own scale, while the picture keeps the window of the display.
/// </summary>
internal sealed class MedicalColorBarModifier(Func<MedicalWindow> read, Action<MedicalWindow> write, MedicalWindow initial) : ChartModifier(55)
{
    private const double ZoomStep = 1.15;

    private AxisPlacement _bar;
    private MedicalWindow _startWindow;
    private Vector2 _start;
    private long _pointerId;
    private bool _isDragging;

    /// <summary>Gets the window after a turn of the wheel: one step narrows it by about 13 %.</summary>
    /// <param name="window">The window before.</param>
    /// <param name="steps">The steps of the wheel, up positive.</param>
    /// <returns>The window after.</returns>
    public static MedicalWindow Zoom(MedicalWindow window, double steps) =>
        new("Custom", window.Center, Math.Max(1e-6, window.Width * Math.Pow(ZoomStep, -steps)));

    /// <summary>Gets the window after pulling the bar: the value under the pointer stays under it.</summary>
    /// <param name="window">The window when the pull started.</param>
    /// <param name="bar">The place of the bar.</param>
    /// <param name="from">Where the pull started.</param>
    /// <param name="to">Where the pointer is now.</param>
    /// <returns>The window after.</returns>
    public static MedicalWindow Pull(MedicalWindow window, AxisPlacement bar, Vector2 from, Vector2 to)
    {
        var (moved, length) = bar.Axis.IsHorizontal ? (to.X - from.X, bar.Bounds.Width) : (from.Y - to.Y, bar.Bounds.Height);
        var shift = length > 0 ? moved / length * window.Width : 0;
        return new MedicalWindow("Custom", window.Center - shift, window.Width);
    }

    /// <inheritdoc />
    protected override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (input.Delta.Y == 0 || !HitsBar(input.Position, out _))
        {
            return ModifierResult.Ignored;
        }

        write(Zoom(read(), input.Delta.Y));
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (!_isDragging)
        {
            if (input.Kind != PointerEventKind.Pressed || input.ChangedButton != PointerButton.Primary || !HitsBar(input.Position, out var bar))
            {
                return ModifierResult.Ignored;
            }

            if (input.ClickCount >= 2)
            {
                write(initial);
                return ModifierResult.Handled;
            }

            (_isDragging, _pointerId, _start, _bar, _startWindow) = (true, input.PointerId, input.Position, bar, read());
            return ModifierResult.Capture;
        }

        if (input.PointerId != _pointerId)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind is PointerEventKind.Released or PointerEventKind.Canceled)
        {
            _isDragging = false;
            return ModifierResult.Release;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            write(Pull(_startWindow, _bar, _start, input.Position));
        }

        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected override void OnCaptureLost(InputCapture capture) => _isDragging = false;

    private bool HitsBar(Vector2 position, out AxisPlacement bar)
    {
        bar = default;
        return Interaction is { } interaction && interaction.TryHitAxis(position, out bar) && bar.Axis is ColorAxis;
    }
}
