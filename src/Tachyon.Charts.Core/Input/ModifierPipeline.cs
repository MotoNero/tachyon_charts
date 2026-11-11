namespace Tachyon.Charts.Core.Input;

/// <summary>
/// Dispatches platform-neutral input to modifiers in priority order and owns their input captures.
/// </summary>
/// <remarks>This class is intended to be used from the UI thread of a chart host.</remarks>
public sealed class ModifierPipeline
{
    private readonly List<ChartModifier> _modifiers = [];
    private readonly Dictionary<long, ChartModifier> _pointerCaptures = [];
    private ChartModifier? _keyboardCapture;
    private ChartModifier? _gestureCapture;
    private ChartInteraction? _interaction;

    /// <summary>Gets or sets the connection to the chart, which all modifiers of this pipeline use.</summary>
    public ChartInteraction? Interaction
    {
        get => _interaction;
        set
        {
            _interaction = value;
            if (value is not null)
            {
                value.Chart.Interaction = value;
            }

            foreach (var modifier in _modifiers)
            {
                modifier.Interaction = value;
            }
        }
    }

    /// <summary>Gets the modifiers in dispatch order.</summary>
    public IReadOnlyList<ChartModifier> Modifiers => _modifiers;

    /// <summary>Adds a modifier, preserving insertion order among equal priorities.</summary>
    /// <param name="modifier">The modifier.</param>
    /// <exception cref="ArgumentException">The modifier is already in this pipeline.</exception>
    public void Add(ChartModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        if (_modifiers.Contains(modifier))
        {
            throw new ArgumentException("The modifier is already in this pipeline.", nameof(modifier));
        }

        var index = 0;
        while (index < _modifiers.Count && _modifiers[index].Priority >= modifier.Priority)
        {
            index++;
        }

        _modifiers.Insert(index, modifier);
        modifier.Interaction = _interaction;
    }

    /// <summary>Removes a modifier and cancels any input it captured.</summary>
    /// <param name="modifier">The modifier.</param>
    /// <returns>Whether the modifier was present.</returns>
    public bool Remove(ChartModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        if (!_modifiers.Remove(modifier))
        {
            return false;
        }

        ReleaseCaptures(modifier, notify: true);
        modifier.Interaction = null;
        return true;
    }

    /// <summary>Removes all modifiers and cancels all captures.</summary>
    public void Clear()
    {
        CancelAllCaptures();
        foreach (var modifier in _modifiers)
        {
            modifier.Interaction = null;
        }

        _modifiers.Clear();
    }

    /// <summary>Gets whether the specified pointer is captured.</summary>
    /// <param name="pointerId">The pointer identifier.</param>
    /// <returns>Whether a modifier owns the pointer.</returns>
    public bool IsPointerCaptured(long pointerId) => _pointerCaptures.ContainsKey(pointerId);

    /// <summary>Cancels every current capture, for example when the host loses focus.</summary>
    public void CancelAllCaptures()
    {
        if (_pointerCaptures.Count != 0)
        {
            var captures = _pointerCaptures.ToArray();
            _pointerCaptures.Clear();
            for (var i = 0; i < captures.Length; i++)
            {
                captures[i].Value.OnCaptureLost(new InputCapture(InputCaptureKind.Pointer, captures[i].Key));
            }
        }

        if (_keyboardCapture is { } keyboard)
        {
            _keyboardCapture = null;
            keyboard.OnCaptureLost(new InputCapture(InputCaptureKind.Keyboard));
        }

        if (_gestureCapture is { } gesture)
        {
            _gestureCapture = null;
            gesture.OnCaptureLost(new InputCapture(InputCaptureKind.Gesture));
        }
    }

    /// <summary>Dispatches a pointer event.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>Whether a modifier handled the event.</returns>
    public bool Process(in PointerInputEvent input)
    {
        if (TryForwardPointer(input))
        {
            return true;
        }

        for (var i = 0; i < _modifiers.Count; i++)
        {
            var modifier = _modifiers[i];
            if (!modifier.IsEnabled)
            {
                continue;
            }

            var result = modifier.OnPointer(input);
            if (result == ModifierResult.Ignored)
            {
                continue;
            }

            if (result == ModifierResult.Capture && !IsPointerEnd(input.Kind))
            {
                _pointerCaptures.Add(input.PointerId, modifier);
            }

            return true;
        }

        return false;
    }

    /// <summary>Dispatches a wheel event.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>Whether a modifier handled the event.</returns>
    /// <exception cref="InvalidOperationException">A modifier attempts to capture wheel input.</exception>
    public bool Process(in WheelInputEvent input)
    {
        for (var i = 0; i < _modifiers.Count; i++)
        {
            var modifier = _modifiers[i];
            if (!modifier.IsEnabled)
            {
                continue;
            }

            var result = modifier.OnWheel(input);
            if (result is ModifierResult.Capture or ModifierResult.Release)
            {
                throw new InvalidOperationException("Wheel input cannot be captured or released.");
            }

            if (result == ModifierResult.Handled)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Dispatches a key event.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>Whether a modifier handled the event.</returns>
    public bool Process(in KeyInputEvent input)
    {
        if (_keyboardCapture is { } captured)
        {
            if (!captured.IsEnabled)
            {
                _keyboardCapture = null;
                captured.OnCaptureLost(new InputCapture(InputCaptureKind.Keyboard));
            }
            else
            {
                var result = captured.OnKey(input);
                if (result == ModifierResult.Release)
                {
                    _keyboardCapture = null;
                }

                return true;
            }
        }

        for (var i = 0; i < _modifiers.Count; i++)
        {
            var modifier = _modifiers[i];
            if (!modifier.IsEnabled)
            {
                continue;
            }

            var result = modifier.OnKey(input);
            if (result == ModifierResult.Ignored)
            {
                continue;
            }

            if (result == ModifierResult.Capture)
            {
                _keyboardCapture = modifier;
            }

            return true;
        }

        return false;
    }

    /// <summary>Dispatches a synthesized gesture event.</summary>
    /// <param name="input">The input event.</param>
    /// <returns>Whether a modifier handled the event.</returns>
    public bool Process(in GestureInputEvent input)
    {
        if (TryForwardGesture(input))
        {
            return true;
        }

        for (var i = 0; i < _modifiers.Count; i++)
        {
            var modifier = _modifiers[i];
            if (!modifier.IsEnabled)
            {
                continue;
            }

            var result = modifier.OnGesture(input);
            if (result == ModifierResult.Ignored)
            {
                continue;
            }

            if (result == ModifierResult.Capture && !IsGestureEnd(input.Phase))
            {
                _gestureCapture = modifier;
            }

            return true;
        }

        return false;
    }

    private bool TryForwardPointer(in PointerInputEvent input)
    {
        if (!_pointerCaptures.TryGetValue(input.PointerId, out var captured))
        {
            return false;
        }

        if (!captured.IsEnabled)
        {
            _pointerCaptures.Remove(input.PointerId);
            captured.OnCaptureLost(new InputCapture(InputCaptureKind.Pointer, input.PointerId));
            return false;
        }

        var result = captured.OnPointer(input);
        if (result == ModifierResult.Release || IsPointerEnd(input.Kind))
        {
            _pointerCaptures.Remove(input.PointerId);
        }

        return true;
    }

    private bool TryForwardGesture(in GestureInputEvent input)
    {
        if (_gestureCapture is not { } captured)
        {
            return false;
        }

        if (!captured.IsEnabled)
        {
            _gestureCapture = null;
            captured.OnCaptureLost(new InputCapture(InputCaptureKind.Gesture));
            return false;
        }

        var result = captured.OnGesture(input);
        if (result == ModifierResult.Release || IsGestureEnd(input.Phase))
        {
            _gestureCapture = null;
        }

        return true;
    }

    private static bool IsPointerEnd(PointerEventKind kind) =>
        kind is PointerEventKind.Released or PointerEventKind.Canceled;

    private static bool IsGestureEnd(GesturePhase phase) =>
        phase is GesturePhase.Completed or GesturePhase.Canceled;

    private void ReleaseCaptures(ChartModifier modifier, bool notify)
    {
        if (_pointerCaptures.Count != 0)
        {
            var captures = _pointerCaptures.Where(pair => ReferenceEquals(pair.Value, modifier)).ToArray();
            for (var i = 0; i < captures.Length; i++)
            {
                _pointerCaptures.Remove(captures[i].Key);
                if (notify)
                {
                    modifier.OnCaptureLost(new InputCapture(InputCaptureKind.Pointer, captures[i].Key));
                }
            }
        }

        if (ReferenceEquals(_keyboardCapture, modifier))
        {
            _keyboardCapture = null;
            if (notify)
            {
                modifier.OnCaptureLost(new InputCapture(InputCaptureKind.Keyboard));
            }
        }

        if (ReferenceEquals(_gestureCapture, modifier))
        {
            _gestureCapture = null;
            if (notify)
            {
                modifier.OnCaptureLost(new InputCapture(InputCaptureKind.Gesture));
            }
        }
    }
}
