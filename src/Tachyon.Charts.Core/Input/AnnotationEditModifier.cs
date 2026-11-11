using System.Numerics;
using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;

namespace Tachyon.Charts.Core.Input;

/// <summary>
/// Edits annotations marked as editable with the mouse: a drag on a handle (an edge of a band or a box, a corner, an
/// end of a line or an arrow) moves that handle alone, a drag elsewhere on the annotation moves it whole. The handles
/// show while the pointer is over the annotation. Each drag is one step of the history, so Ctrl+Z takes it back and
/// Ctrl+Y makes it again. Delete removes the last touched annotation.
/// </summary>
public sealed class AnnotationEditModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above panning, the rubber band and the gestures of instruments.</summary>
    public const int DefaultPriority = 45;

    private static readonly KeyBinding _undoKey = new(ChartKey.Z, InputModifiers.Control);
    private static readonly KeyBinding _redoKey = new(ChartKey.Y, InputModifiers.Control);

    private Annotation? _active;
    private Annotation? _selected;
    private Annotation? _hot;
    private object? _before;
    private Vector2 _last;
    private long _pointerId;
    private int _handle;
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

        if (Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind is PointerEventKind.Moved or PointerEventKind.Exited)
        {
            Hover(interaction, input.Kind == PointerEventKind.Exited ? null : Find(interaction, input.Position));
            return ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != PointerButton.Primary
            || Find(interaction, input.Position) is not { } annotation)
        {
            return ModifierResult.Ignored;
        }

        _active = annotation;
        _selected = annotation;
        _pointerId = input.PointerId;
        _last = input.Position;
        _handle = annotation.HandleAt(input.Position, Tolerance);
        _isResizing = _handle < 0 && IsAtCorner(annotation, input.Position);
        _before = annotation.Capture();
        Hover(interaction, annotation);
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (Interaction is not { } interaction || input.Kind != KeyEventKind.Pressed)
        {
            return ModifierResult.Ignored;
        }

        var isUndo = _undoKey.Matches(input);
        if (isUndo || _redoKey.Matches(input))
        {
            interaction.Dispatch(() =>
            {
                if (isUndo)
                {
                    interaction.History.Undo(interaction.Chart);
                }
                else
                {
                    interaction.History.Redo(interaction.Chart);
                }
            });
            return ModifierResult.Handled;
        }

        if (input.Key != ChartKey.Delete || _selected is not { } annotation)
        {
            return ModifierResult.Ignored;
        }

        _selected = null;
        interaction.Dispatch(() => interaction.Chart.RemoveOverlay(annotation));
        Removed?.Invoke(annotation);
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => Finish();

    private static double ToCoordinate(ChartInteraction interaction, AxisBase? axis, double pixels) =>
        axis is null ? pixels : interaction.ToAxisDistance(axis, pixels);

    private Annotation? Find(ChartInteraction interaction, Vector2 position)
    {
        var overlays = interaction.Chart.Overlays;
        for (var i = overlays.Count - 1; i >= 0; i--)
        {
            if (overlays[i] is Annotation { IsEditable: true } annotation
                && (annotation.HandleAt(position, Tolerance) >= 0 || annotation.Contains(position, Tolerance)))
            {
                return annotation;
            }
        }

        return null;
    }

    private void Hover(ChartInteraction interaction, Annotation? annotation)
    {
        if (ReferenceEquals(annotation, _hot))
        {
            return;
        }

        var previous = _hot;
        _hot = annotation;
        interaction.Dispatch(() =>
        {
            if (previous is not null)
            {
                previous.IsHot = false;
            }

            if (annotation is not null)
            {
                annotation.IsHot = true;
            }
        });
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
                Finish();
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void Finish()
    {
        if (_active is not { } annotation)
        {
            return;
        }

        _active = null;
        var before = _before;
        _before = null;
        if (Interaction is not { } interaction)
        {
            return;
        }

        interaction.Dispatch(() =>
        {
            if (before is not null && annotation.Capture() is { } after && !Equals(before, after))
            {
                interaction.History.Record(new AnnotationChange(annotation, before, after));
            }

            annotation.NotifyEditFinished();
        });
    }

    private void Drag(Vector2 position)
    {
        var delta = position - _last;
        _last = position;
        if (_active is not { } annotation || Interaction is not { } interaction)
        {
            return;
        }

        var handle = _handle;
        var isResizing = _isResizing;
        interaction.Dispatch(() =>
        {
            // Each axis takes the move along its own direction, also when the X axis runs down.
            var deltaX = ToCoordinate(interaction, annotation.XAxis, annotation.XAxis is { IsHorizontal: false } ? delta.Y : delta.X);
            var deltaY = ToCoordinate(interaction, annotation.YAxis, annotation.YAxis is { IsHorizontal: true } ? delta.X : delta.Y);
            if (handle >= 0)
            {
                annotation.MoveHandle(handle, deltaX, deltaY);
            }
            else if (!isResizing || !annotation.Resize(deltaX, deltaY))
            {
                annotation.Move(deltaX, deltaY);
            }

            annotation.NotifyEdited();
        });
    }

    /// <summary>One drag of an annotation, as a step of the history.</summary>
    private sealed class AnnotationChange(Annotation annotation, object before, object after) : IChartEdit
    {
        public void Undo() => Apply(before);

        public void Redo() => Apply(after);

        private void Apply(object state)
        {
            annotation.Restore(state);
            annotation.NotifyEdited();
            annotation.NotifyEditFinished();
        }
    }
}
