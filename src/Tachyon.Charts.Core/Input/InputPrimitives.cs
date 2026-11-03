using System.Numerics;

namespace Tachyon.Charts.Core.Input;

/// <summary>Keyboard modifiers carried by a platform-neutral input event.</summary>
[Flags]
public enum InputModifiers
{
    /// <summary>No modifier is pressed.</summary>
    None = 0,
    /// <summary>The Shift key is pressed.</summary>
    Shift = 1 << 0,
    /// <summary>The Control key is pressed.</summary>
    Control = 1 << 1,
    /// <summary>The Alt or Option key is pressed.</summary>
    Alt = 1 << 2,
    /// <summary>The Windows or Command key is pressed.</summary>
    Meta = 1 << 3,
}

/// <summary>Pointer buttons currently pressed.</summary>
[Flags]
public enum PointerButtons
{
    /// <summary>No button is pressed.</summary>
    None = 0,
    /// <summary>The primary button is pressed.</summary>
    Primary = 1 << 0,
    /// <summary>The secondary button is pressed.</summary>
    Secondary = 1 << 1,
    /// <summary>The middle button is pressed.</summary>
    Middle = 1 << 2,
    /// <summary>The first extended button is pressed.</summary>
    X1 = 1 << 3,
    /// <summary>The second extended button is pressed.</summary>
    X2 = 1 << 4,
}

/// <summary>A button whose state changed in a pointer event.</summary>
public enum PointerButton
{
    /// <summary>No button changed.</summary>
    None,
    /// <summary>The primary button changed.</summary>
    Primary,
    /// <summary>The secondary button changed.</summary>
    Secondary,
    /// <summary>The middle button changed.</summary>
    Middle,
    /// <summary>The first extended button changed.</summary>
    X1,
    /// <summary>The second extended button changed.</summary>
    X2,
}

/// <summary>The physical source of a pointer event.</summary>
public enum PointerDeviceKind
{
    /// <summary>The platform did not identify the device.</summary>
    Unknown,
    /// <summary>A mouse or mouse-like device.</summary>
    Mouse,
    /// <summary>A touch contact.</summary>
    Touch,
    /// <summary>A pen or stylus.</summary>
    Pen,
}

/// <summary>The phase of a pointer event.</summary>
public enum PointerEventKind
{
    /// <summary>A pointer entered the chart.</summary>
    Entered,
    /// <summary>A pointer moved.</summary>
    Moved,
    /// <summary>A pointer button was pressed.</summary>
    Pressed,
    /// <summary>A pointer button was released.</summary>
    Released,
    /// <summary>A pointer left the chart.</summary>
    Exited,
    /// <summary>The platform canceled the pointer sequence.</summary>
    Canceled,
}

/// <summary>A platform-neutral pointer event in device-independent units.</summary>
/// <param name="Kind">The event phase.</param>
/// <param name="PointerId">The stable identifier for this pointer sequence.</param>
/// <param name="Position">The position relative to the chart.</param>
/// <param name="Buttons">The buttons held after the event.</param>
/// <param name="ChangedButton">The button changed by this event, if any.</param>
/// <param name="Modifiers">The keyboard modifiers.</param>
/// <param name="Timestamp">A monotonic timestamp with an arbitrary origin.</param>
/// <param name="DeviceKind">The physical pointer source.</param>
/// <param name="ClickCount">The platform click count, or zero when not applicable.</param>
public readonly record struct PointerInputEvent(
    PointerEventKind Kind,
    long PointerId,
    Vector2 Position,
    PointerButtons Buttons = PointerButtons.None,
    PointerButton ChangedButton = PointerButton.None,
    InputModifiers Modifiers = InputModifiers.None,
    TimeSpan Timestamp = default,
    PointerDeviceKind DeviceKind = PointerDeviceKind.Unknown,
    int ClickCount = 0);

/// <summary>A platform-neutral wheel event in device-independent units.</summary>
/// <param name="Position">The position relative to the chart.</param>
/// <param name="Delta">The normalized horizontal and vertical wheel delta; positive is right and up.</param>
/// <param name="Modifiers">The keyboard modifiers.</param>
/// <param name="Timestamp">A monotonic timestamp with an arbitrary origin.</param>
public readonly record struct WheelInputEvent(
    Vector2 Position,
    Vector2 Delta,
    InputModifiers Modifiers = InputModifiers.None,
    TimeSpan Timestamp = default);

/// <summary>The phase of a key event.</summary>
public enum KeyEventKind
{
    /// <summary>The key was pressed.</summary>
    Pressed,
    /// <summary>The key was released.</summary>
    Released,
}

/// <summary>A platform-neutral key used by chart interaction.</summary>
public enum ChartKey
{
    /// <summary>No key.</summary>
    None,
    /// <summary>Backspace.</summary>
    Backspace,
    /// <summary>Tab.</summary>
    Tab,
    /// <summary>Enter or Return.</summary>
    Enter,
    /// <summary>Escape.</summary>
    Escape,
    /// <summary>Space.</summary>
    Space,
    /// <summary>Page Up.</summary>
    PageUp,
    /// <summary>Page Down.</summary>
    PageDown,
    /// <summary>End.</summary>
    End,
    /// <summary>Home.</summary>
    Home,
    /// <summary>Left Arrow.</summary>
    Left,
    /// <summary>Up Arrow.</summary>
    Up,
    /// <summary>Right Arrow.</summary>
    Right,
    /// <summary>Down Arrow.</summary>
    Down,
    /// <summary>Insert.</summary>
    Insert,
    /// <summary>Delete.</summary>
    Delete,
    /// <summary>The plus or keypad-add key.</summary>
    Add,
    /// <summary>The minus or keypad-subtract key.</summary>
    Subtract,
    /// <summary>The digit 0.</summary>
    D0,
    /// <summary>The digit 1.</summary>
    D1,
    /// <summary>The digit 2.</summary>
    D2,
    /// <summary>The digit 3.</summary>
    D3,
    /// <summary>The digit 4.</summary>
    D4,
    /// <summary>The digit 5.</summary>
    D5,
    /// <summary>The digit 6.</summary>
    D6,
    /// <summary>The digit 7.</summary>
    D7,
    /// <summary>The digit 8.</summary>
    D8,
    /// <summary>The digit 9.</summary>
    D9,
    /// <summary>The letter A.</summary>
    A,
    /// <summary>The letter B.</summary>
    B,
    /// <summary>The letter C.</summary>
    C,
    /// <summary>The letter D.</summary>
    D,
    /// <summary>The letter E.</summary>
    E,
    /// <summary>The letter F.</summary>
    F,
    /// <summary>The letter G.</summary>
    G,
    /// <summary>The letter H.</summary>
    H,
    /// <summary>The letter I.</summary>
    I,
    /// <summary>The letter J.</summary>
    J,
    /// <summary>The letter K.</summary>
    K,
    /// <summary>The letter L.</summary>
    L,
    /// <summary>The letter M.</summary>
    M,
    /// <summary>The letter N.</summary>
    N,
    /// <summary>The letter O.</summary>
    O,
    /// <summary>The letter P.</summary>
    P,
    /// <summary>The letter Q.</summary>
    Q,
    /// <summary>The letter R.</summary>
    R,
    /// <summary>The letter S.</summary>
    S,
    /// <summary>The letter T.</summary>
    T,
    /// <summary>The letter U.</summary>
    U,
    /// <summary>The letter V.</summary>
    V,
    /// <summary>The letter W.</summary>
    W,
    /// <summary>The letter X.</summary>
    X,
    /// <summary>The letter Y.</summary>
    Y,
    /// <summary>The letter Z.</summary>
    Z,
    /// <summary>The function key F12.</summary>
    F12,
}

/// <summary>A platform-neutral key event.</summary>
/// <param name="Kind">The event phase.</param>
/// <param name="Key">The logical key.</param>
/// <param name="Modifiers">The keyboard modifiers after the event.</param>
/// <param name="IsRepeat">Whether this is an automatic repeat.</param>
/// <param name="Timestamp">A monotonic timestamp with an arbitrary origin.</param>
public readonly record struct KeyInputEvent(
    KeyEventKind Kind,
    ChartKey Key,
    InputModifiers Modifiers = InputModifiers.None,
    bool IsRepeat = false,
    TimeSpan Timestamp = default);

/// <summary>A configurable logical-key binding.</summary>
/// <param name="Key">The logical key.</param>
/// <param name="Modifiers">The exact modifier combination.</param>
/// <param name="Kind">The key phase that activates the binding.</param>
public readonly record struct KeyBinding(
    ChartKey Key,
    InputModifiers Modifiers = InputModifiers.None,
    KeyEventKind Kind = KeyEventKind.Pressed)
{
    /// <summary>Tests whether an event activates this binding.</summary>
    /// <param name="input">The event.</param>
    /// <returns><see langword="true"/> when key, modifiers and phase match.</returns>
    public bool Matches(in KeyInputEvent input) =>
        input.Key == Key && input.Modifiers == Modifiers && input.Kind == Kind;
}

/// <summary>The kind of a synthesized gesture.</summary>
public enum GestureKind
{
    /// <summary>Translation, such as two-finger scrolling.</summary>
    Pan,
    /// <summary>Scaling around a center point.</summary>
    Pinch,
}

/// <summary>The phase of a synthesized gesture.</summary>
public enum GesturePhase
{
    /// <summary>The gesture began.</summary>
    Started,
    /// <summary>The gesture changed.</summary>
    Changed,
    /// <summary>The gesture completed normally.</summary>
    Completed,
    /// <summary>The platform canceled the gesture.</summary>
    Canceled,
}

/// <summary>A platform-neutral pan or pinch event in device-independent units.</summary>
/// <param name="Kind">The gesture kind.</param>
/// <param name="Phase">The gesture phase.</param>
/// <param name="Position">The current gesture center relative to the chart.</param>
/// <param name="Delta">The translation since the preceding event.</param>
/// <param name="Scale">The scale factor since the preceding event; one means unchanged.</param>
/// <param name="Modifiers">The keyboard modifiers.</param>
/// <param name="Timestamp">A monotonic timestamp with an arbitrary origin.</param>
public readonly record struct GestureInputEvent(
    GestureKind Kind,
    GesturePhase Phase,
    Vector2 Position,
    Vector2 Delta,
    float Scale = 1,
    InputModifiers Modifiers = InputModifiers.None,
    TimeSpan Timestamp = default);
