using System.Numerics;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Core.Instruments;

/// <summary>Moves the marker of a Smith chart to the point of the curve next to the pointer.</summary>
public sealed class SmithMarkerModifier() : ChartModifier(ZoomPanModifier.DefaultPriority)
{
    private bool _isDragging;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary)
        {
            _isDragging = true;
            Move(input.Position);
            return ModifierResult.Capture;
        }

        if (!_isDragging)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            Move(input.Position);
            return ModifierResult.Handled;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            _isDragging = false;
            return ModifierResult.Release;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _isDragging = false;

    private void Move(Vector2 position)
    {
        if (Interaction?.Chart is not { CurrentLayout: { } layout } chart)
        {
            return;
        }

        var series = chart.Series.OfType<SmithSeries>().FirstOrDefault();
        var marker = chart.Overlays.OfType<SmithMarkerOverlay>().FirstOrDefault();
        if (series is null || marker is null)
        {
            return;
        }

        var pane = layout.Panes[0];
        var projection = SmithProjection.Fit(pane.X, pane.Y, pane.Width, pane.Height);
        var best = -1;
        var distance = float.PositiveInfinity;
        for (var i = 0; i < series.Count; i++)
        {
            var current = Vector2.DistanceSquared(projection.ToPoint(series.GetReflection(i)), position);
            if (current < distance)
            {
                distance = current;
                best = i;
            }
        }

        if (best >= 0)
        {
            marker.Index = best;
        }
    }
}

/// <summary>
/// Operates parallel coordinates: dragging an axis at its name moves it, dragging along an axis keeps
/// only the rows of that range, a double click anywhere shows all rows again.
/// </summary>
public sealed class ParallelAxesModifier() : ChartModifier(ZoomPanModifier.DefaultPriority)
{
    private const double HeaderHeight = 34;
    private const double Reach = 14;

    private int _place = -1;
    private bool _isMoving;
    private float _startY;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart is not { CurrentLayout: { } layout } chart || chart.Series.OfType<ParallelCoordinatesSeries>().FirstOrDefault() is not { } series)
        {
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        var width = pane.Width / (series.Order.Count - 1);
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary)
        {
            // Anywhere in the plot: nobody hits a thin axis twice in a row.
            if (input.ClickCount >= 2)
            {
                series.ClearFilters();
                _place = -1;
                return ModifierResult.Handled;
            }

            var place = (int)Math.Round((input.Position.X - pane.X) / width);
            if (place < 0 || place >= series.Order.Count || Math.Abs(pane.X + (place * width) - input.Position.X) > Reach)
            {
                return ModifierResult.Ignored;
            }

            _place = place;
            _isMoving = input.Position.Y <= pane.Y + HeaderHeight;
            _startY = input.Position.Y;
            return ModifierResult.Capture;
        }

        if (_place < 0)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            return ModifierResult.Handled;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            if (_isMoving)
            {
                series.MoveAxis(_place, Math.Clamp((int)Math.Round((input.Position.X - pane.X) / width), 0, series.Order.Count - 1));
            }
            else if (Math.Abs(input.Position.Y - _startY) > 2)
            {
                var column = series.Order[_place];
                var range = series.GetRange(column);
                double ToValue(float y) => range.Max - ((y - pane.Y) / pane.Height * (range.Max - range.Min));
                var first = ToValue(_startY);
                var second = ToValue(input.Position.Y);
                series.SetFilter(column, new DoubleRange(Math.Min(first, second), Math.Max(first, second)));
            }

            _place = -1;
            return ModifierResult.Release;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _place = -1;
}

/// <summary>
/// Moves the markers of a network analyzer: dragging a marker moves it, Ctrl and a click adds one or
/// removes the one at the pointer. All other input stays free for zooming and panning.
/// </summary>
/// <param name="display">The state.</param>
public sealed class NetworkMarkerModifier(NetworkDisplay display) : ChartModifier(SelectionModifier.DefaultPriority)
{
    private const double Reach = 10;

    private int _marker = -1;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart is not { CurrentLayout: { } layout } chart)
        {
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        var range = chart.Series[0].XAxis.VisibleRange;
        var perUnit = (range.Max - range.Min) / pane.Width;
        var frequency = range.Min + ((input.Position.X - pane.X) * perUnit);
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary)
        {
            return OnPressed(frequency, Reach * perUnit, (input.Modifiers & InputModifiers.Control) != 0);
        }

        if (_marker < 0)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            display.MoveMarker(_marker, Math.Clamp(frequency, range.Min, range.Max));
            return ModifierResult.Handled;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            _marker = -1;
            return ModifierResult.Release;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _marker = -1;

    private ModifierResult OnPressed(double frequency, double reach, bool isToggle)
    {
        var nearest = -1;
        var distance = reach;
        for (var i = 0; i < display.Markers.Count; i++)
        {
            if (Math.Abs(display.Markers[i] - frequency) <= distance)
            {
                distance = Math.Abs(display.Markers[i] - frequency);
                nearest = i;
            }
        }

        if (isToggle)
        {
            if (nearest >= 0)
            {
                display.RemoveMarker(nearest);
            }
            else
            {
                display.AddMarker(frequency);
            }

            return ModifierResult.Handled;
        }

        if (nearest < 0)
        {
            return ModifierResult.Ignored;
        }

        _marker = nearest;
        return ModifierResult.Capture;
    }
}

/// <summary>
/// Operates a spectrum analyzer: the wheel zooms the frequency around the pointer, at the left edge it
/// changes the level per division, with Ctrl the reference level. Dragging moves the frequency, a click
/// sets the marker, a double click returns it to the peak. Ctrl and a click adds a further marker or
/// removes the one at the pointer. Dragging the mask line moves the mask up or down.
/// </summary>
/// <param name="display">The state.</param>
public sealed class SpectrumModifier(SpectrumDisplay display) : ChartModifier(ZoomPanModifier.DefaultPriority)
{
    private const float EdgeWidth = 60;
    private const float ClickDistance = 4;
    private const float MarkerReach = 10;
    private const double ZoomPerStep = 1.25;
    private const double MaskReach = 6;

    private bool _isDragging;
    private bool _hasMoved;
    private bool _isAdding;
    private Vector2 _start;
    private double _startFrequency;
    private double _stopFrequency;
    private FrequencyMask? _movedMask;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart.CurrentLayout is not { } layout)
        {
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary)
        {
            return Press(input, pane);
        }

        if (!_isDragging)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            Move(input, pane);
            return ModifierResult.Handled;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            _isDragging = false;
            if (!_hasMoved)
            {
                PlaceMarker((input.Position.X - pane.X) / pane.Width);
            }

            return ModifierResult.Release;
        }

        return ModifierResult.Ignored;
    }

    private ModifierResult Press(in PointerInputEvent input, DipRect pane)
    {
        if (input.ClickCount == 2)
        {
            display.MarkerFrequency = null;
            return ModifierResult.Handled;
        }

        _isDragging = true;
        _hasMoved = false;
        _movedMask = IsOnMask(display, pane, input.Position) ? display.Mask : null;
        _isAdding = (input.Modifiers & InputModifiers.Control) != 0;
        _start = input.Position;
        _startFrequency = display.Start;
        _stopFrequency = display.Stop;
        return ModifierResult.Capture;
    }

    // Dragging the mask moves it up or down; dragging elsewhere moves the frequencies.
    private void Move(in PointerInputEvent input, DipRect pane)
    {
        _hasMoved |= Vector2.Distance(input.Position, _start) > ClickDistance;
        if (_hasMoved && _movedMask is { } mask)
        {
            var span = display.PerDivision * ScopeDisplay.VerticalDivisions;
            display.Mask = mask.Shift((_start.Y - input.Position.Y) / pane.Height * span);
        }
        else if (_hasMoved)
        {
            var shift = (_start.X - input.Position.X) / pane.Width * (_stopFrequency - _startFrequency);
            display.SetView(_startFrequency + shift, _stopFrequency + shift, display.ReferenceLevel, display.PerDivision);
        }
    }

    /// <summary>Finds whether a point lies on the line of the mask.</summary>
    /// <param name="display">The display.</param>
    /// <param name="pane">The area of the spectrum.</param>
    /// <param name="position">The point.</param>
    /// <returns>Whether the point is within a few pixels of the mask.</returns>
    internal static bool IsOnMask(SpectrumDisplay display, DipRect pane, Vector2 position)
    {
        if (display.Mask is not { } mask || pane.Width <= 0 || pane.Height <= 0)
        {
            return false;
        }

        var frequency = display.Start + ((position.X - pane.X) / pane.Width * (display.Stop - display.Start));
        var limit = mask.GetLimit(frequency);
        var span = display.PerDivision * ScopeDisplay.VerticalDivisions;
        var y = pane.Y + ((display.ReferenceLevel - limit) / span * pane.Height);
        return double.IsFinite(y) && Math.Abs(y - position.Y) <= MaskReach;
    }

    private void PlaceMarker(double share)
    {
        var span = display.Stop - display.Start;
        var frequency = display.Start + (share * span);
        if (!_isAdding)
        {
            display.MarkerFrequency = frequency;
        }
        else if (!display.RemoveMarker(frequency, MarkerReach * span / (Interaction?.Chart.CurrentLayout?.Panes[0].Width ?? 1)))
        {
            display.AddMarker(frequency);
        }
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (Interaction?.Chart.CurrentLayout is not { } layout || input.Delta.Y == 0)
        {
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        var isLarger = input.Delta.Y < 0;
        if ((input.Modifiers & InputModifiers.Control) != 0)
        {
            display.SetView(display.Start, display.Stop, display.ReferenceLevel + (isLarger ? -display.PerDivision : display.PerDivision), display.PerDivision);
        }
        else if (input.Position.X <= pane.X + EdgeWidth)
        {
            display.SetView(display.Start, display.Stop, display.ReferenceLevel, ScopeFormat.Step(display.PerDivision, isLarger ? 1 : -1));
        }
        else
        {
            var share = (input.Position.X - pane.X) / pane.Width;
            var center = display.Start + (share * (display.Stop - display.Start));
            var span = (display.Stop - display.Start) * (isLarger ? ZoomPerStep : 1 / ZoomPerStep);
            display.SetView(center - (share * span), center + ((1 - share) * span), display.ReferenceLevel, display.PerDivision);
        }

        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _isDragging = false;
}
