using System.Numerics;
using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;

namespace Tachyon.Charts.Core.Input;

/// <summary>
/// Moves annotations marked as editable, changes their size at the bottom right corner, and removes the
/// last touched one with Delete.
/// </summary>
public sealed class AnnotationEditModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above panning and the rubber band.</summary>
    public const int DefaultPriority = 25;

    private Annotation? _active;
    private Annotation? _selected;
    private Vector2 _last;
    private long _pointerId;
    private bool _isResizing;

    /// <summary>Initializes a new instance of the <see cref="AnnotationEditModifier"/> class.</summary>
    public AnnotationEditModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets or sets how near a position must be, in device-independent units.</summary>
    public double Tolerance { get; set; } = 5;

    /// <summary>Occurs on the UI thread when Delete removed an annotation.</summary>
    public event Action<Annotation>? Removed;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (_active is not null)
        {
            return input.PointerId == _pointerId ? ContinueDrag(input) : ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != PointerButton.Primary
            || Interaction is not { } interaction
            || Find(interaction, input.Position) is not { } annotation)
        {
            return ModifierResult.Ignored;
        }

        _active = annotation;
        _selected = annotation;
        _pointerId = input.PointerId;
        _last = input.Position;
        _isResizing = IsAtCorner(annotation, input.Position);
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (input.Key != ChartKey.Delete || input.Kind != KeyEventKind.Pressed || _selected is not { } annotation || Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        _selected = null;
        interaction.Dispatch(() => interaction.Chart.RemoveOverlay(annotation));
        Removed?.Invoke(annotation);
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _active = null;

    private static double ToCoordinate(ChartInteraction interaction, AxisBase? axis, double pixels) =>
        axis is null ? pixels : interaction.ToAxisDistance(axis, pixels);

    private Annotation? Find(ChartInteraction interaction, Vector2 position)
    {
        var overlays = interaction.Chart.Overlays;
        for (var i = overlays.Count - 1; i >= 0; i--)
        {
            if (overlays[i] is Annotation { IsEditable: true } annotation && annotation.Contains(position, Tolerance))
            {
                return annotation;
            }
        }

        return null;
    }

    private bool IsAtCorner(Annotation annotation, Vector2 position) =>
        annotation.LastBounds is { } bounds
        && Vector2.Distance(position, new Vector2((float)bounds.Right, (float)bounds.Bottom)) <= Tolerance;

    private ModifierResult ContinueDrag(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                Drag(input.Position);
                return ModifierResult.Handled;
            case PointerEventKind.Released:
            case PointerEventKind.Canceled:
                _active = null;
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void Drag(Vector2 position)
    {
        var delta = position - _last;
        _last = position;
        if (_active is not { } annotation || Interaction is not { } interaction)
        {
            return;
        }

        var isResizing = _isResizing;
        interaction.Dispatch(() =>
        {
            var deltaX = ToCoordinate(interaction, annotation.XAxis, delta.X);
            var deltaY = ToCoordinate(interaction, annotation.YAxis, delta.Y);
            if (!isResizing || !annotation.Resize(deltaX, deltaY))
            {
                annotation.Move(deltaX, deltaY);
            }

            annotation.NotifyEdited();
        });
    }
}
