namespace Tachyon.Charts.Core.Input;

/// <summary>The result of dispatching one input event to a modifier.</summary>
public enum ModifierResult
{
    /// <summary>The modifier ignored the event, so dispatch may continue.</summary>
    Ignored,
    /// <summary>The modifier handled the event without changing capture.</summary>
    Handled,
    /// <summary>The modifier handled the event and requests exclusive capture of its input sequence.</summary>
    Capture,
    /// <summary>The modifier handled the event and releases its capture.</summary>
    Release,
}

/// <summary>The kind of input sequence captured by a modifier.</summary>
public enum InputCaptureKind
{
    /// <summary>A pointer sequence.</summary>
    Pointer,
    /// <summary>The keyboard.</summary>
    Keyboard,
    /// <summary>Synthesized gestures.</summary>
    Gesture,
}

/// <summary>Identifies a capture that ended outside the normal input sequence.</summary>
/// <param name="Kind">The captured input source.</param>
/// <param name="PointerId">The pointer identifier, or zero for non-pointer capture.</param>
public readonly record struct InputCapture(InputCaptureKind Kind, long PointerId = 0);

/// <summary>
/// Base class for a platform-neutral chart interaction. Higher priorities receive uncaptured input first.
/// </summary>
public abstract class ChartModifier
{
    private ChartInteraction? _interaction;

    /// <summary>Initializes a modifier.</summary>
    /// <param name="priority">The dispatch priority. Higher values run first.</param>
    protected ChartModifier(int priority = 0)
    {
        Priority = priority;
    }

    /// <summary>Gets the dispatch priority. Higher values run first.</summary>
    public int Priority { get; }

    /// <summary>Gets the connection to the chart, or <see langword="null"/> outside of a connected pipeline.</summary>
    public ChartInteraction? Interaction
    {
        get => _interaction;
        internal set
        {
            if (_interaction is { } old)
            {
                OnDetached(old);
            }

            _interaction = value;
            if (value is not null)
            {
                OnAttached(value);
            }
        }
    }

    /// <summary>Gets or sets whether this modifier participates in dispatch.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Connects the modifier to a chart, for example to add an overlay.</summary>
    /// <param name="interaction">The connection.</param>
    protected internal virtual void OnAttached(ChartInteraction interaction)
    {
    }

    /// <summary>Disconnects the modifier from a chart.</summary>
    /// <param name="interaction">The connection.</param>
    protected internal virtual void OnDetached(ChartInteraction interaction)
    {
    }

    /// <summary>Handles pointer input.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>The dispatch result.</returns>
    protected internal virtual ModifierResult OnPointer(in PointerInputEvent input) => ModifierResult.Ignored;

    /// <summary>Handles wheel input.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>The dispatch result. Wheel input cannot be captured.</returns>
    protected internal virtual ModifierResult OnWheel(in WheelInputEvent input) => ModifierResult.Ignored;

    /// <summary>Handles keyboard input.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>The dispatch result.</returns>
    protected internal virtual ModifierResult OnKey(in KeyInputEvent input) => ModifierResult.Ignored;

    /// <summary>Handles synthesized pan or pinch input.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>The dispatch result.</returns>
    protected internal virtual ModifierResult OnGesture(in GestureInputEvent input) => ModifierResult.Ignored;

    /// <summary>Notifies the modifier that capture ended because it was canceled, disabled or removed.</summary>
    /// <param name="capture">The capture that ended.</param>
    protected internal virtual void OnCaptureLost(InputCapture capture)
    {
    }
}
