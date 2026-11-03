using System.Numerics;
using System.Windows.Input;
using Tachyon.Charts.Core.Input;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// Translates native input into the types of the engine. Holds no chart logic.
/// </summary>
internal static class WpfInput
{
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int WmMouseMove = 0x0200;
    public const int WmLeftButtonDown = 0x0201;
    public const int WmLeftButtonUp = 0x0202;
    public const int WmLeftButtonDoubleClick = 0x0203;
    public const int WmRightButtonDown = 0x0204;
    public const int WmRightButtonUp = 0x0205;
    public const int WmRightButtonDoubleClick = 0x0206;
    public const int WmMiddleButtonDown = 0x0207;
    public const int WmMiddleButtonUp = 0x0208;
    public const int WmMiddleButtonDoubleClick = 0x0209;
    public const int WmMouseWheel = 0x020A;
    public const int WmMouseLeave = 0x02A3;
    public const int WmXButtonDown = 0x020B;
    public const int WmXButtonUp = 0x020C;
    public const int WmXButtonDoubleClick = 0x020D;

    private const double WheelStepsPerNotch = 120;
    private const long MousePointerId = 1;
    private const int KeyRepeatBit = 1 << 30;

    private const int MkLeft = 0x0001;
    private const int MkRight = 0x0002;
    private const int MkMiddle = 0x0010;
    private const int MkX1 = 0x0020;
    private const int MkX2 = 0x0040;

    /// <summary>Tests whether a message presses a button. A quick second press arrives as double click.</summary>
    /// <param name="message">The window message.</param>
    /// <returns>Whether a button went down.</returns>
    public static bool IsButtonPress(int message) => message is WmLeftButtonDown or WmLeftButtonDoubleClick
        or WmRightButtonDown or WmRightButtonDoubleClick
        or WmMiddleButtonDown or WmMiddleButtonDoubleClick
        or WmXButtonDown or WmXButtonDoubleClick;

    public static InputModifiers ToModifiers(ModifierKeys keys)
    {
        var modifiers = InputModifiers.None;
        modifiers |= (keys & ModifierKeys.Shift) != 0 ? InputModifiers.Shift : 0;
        modifiers |= (keys & ModifierKeys.Control) != 0 ? InputModifiers.Control : 0;
        modifiers |= (keys & ModifierKeys.Alt) != 0 ? InputModifiers.Alt : 0;
        modifiers |= (keys & ModifierKeys.Windows) != 0 ? InputModifiers.Meta : 0;
        return modifiers;
    }

    public static ModifierKeys ToModifierKeys(InputModifiers modifiers)
    {
        var keys = ModifierKeys.None;
        keys |= (modifiers & InputModifiers.Shift) != 0 ? ModifierKeys.Shift : 0;
        keys |= (modifiers & InputModifiers.Control) != 0 ? ModifierKeys.Control : 0;
        keys |= (modifiers & InputModifiers.Alt) != 0 ? ModifierKeys.Alt : 0;
        keys |= (modifiers & InputModifiers.Meta) != 0 ? ModifierKeys.Windows : 0;
        return keys;
    }

    public static ChartKey ToChartKey(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ChartKey.A + (key - Key.A),
        >= Key.D0 and <= Key.D9 => ChartKey.D0 + (key - Key.D0),
        Key.Back => ChartKey.Backspace,
        Key.Tab => ChartKey.Tab,
        Key.Enter => ChartKey.Enter,
        Key.Escape => ChartKey.Escape,
        Key.Space => ChartKey.Space,
        Key.PageUp => ChartKey.PageUp,
        Key.PageDown => ChartKey.PageDown,
        Key.End => ChartKey.End,
        Key.Home => ChartKey.Home,
        Key.Left => ChartKey.Left,
        Key.Up => ChartKey.Up,
        Key.Right => ChartKey.Right,
        Key.Down => ChartKey.Down,
        Key.Insert => ChartKey.Insert,
        Key.Delete => ChartKey.Delete,
        Key.Add or Key.OemPlus => ChartKey.Add,
        Key.Subtract or Key.OemMinus => ChartKey.Subtract,
        Key.F12 => ChartKey.F12,
        _ => ChartKey.None,
    };

    public static PointerButton ToButton(MouseButton button) => button switch
    {
        MouseButton.Left => PointerButton.Primary,
        MouseButton.Right => PointerButton.Secondary,
        MouseButton.Middle => PointerButton.Middle,
        MouseButton.XButton1 => PointerButton.X1,
        MouseButton.XButton2 => PointerButton.X2,
        _ => PointerButton.None,
    };

    public static PointerButtons ToButtons(MouseDevice mouse)
    {
        var buttons = PointerButtons.None;
        buttons |= mouse.LeftButton == MouseButtonState.Pressed ? PointerButtons.Primary : 0;
        buttons |= mouse.RightButton == MouseButtonState.Pressed ? PointerButtons.Secondary : 0;
        buttons |= mouse.MiddleButton == MouseButtonState.Pressed ? PointerButtons.Middle : 0;
        buttons |= mouse.XButton1 == MouseButtonState.Pressed ? PointerButtons.X1 : 0;
        buttons |= mouse.XButton2 == MouseButtonState.Pressed ? PointerButtons.X2 : 0;
        return buttons;
    }

    /// <summary>Translates a mouse message other than the wheel.</summary>
    /// <param name="message">The window message.</param>
    /// <param name="wParam">Its first parameter.</param>
    /// <param name="lParam">Its second parameter, the position in physical pixels.</param>
    /// <param name="scale">Physical pixels per device-independent unit.</param>
    /// <param name="modifiers">The keyboard modifiers.</param>
    /// <param name="clickCount">The number of quick clicks at this place.</param>
    /// <param name="timestamp">The time of the event.</param>
    /// <param name="input">Receives the event.</param>
    /// <returns>Whether the message is a mouse event.</returns>
    public static bool TryTranslateMouse(int message, nint wParam, nint lParam, double scale, InputModifiers modifiers, int clickCount, TimeSpan timestamp, out PointerInputEvent input)
    {
        var (kind, button) = message switch
        {
            WmMouseMove => (PointerEventKind.Moved, PointerButton.None),
            WmLeftButtonDown or WmLeftButtonDoubleClick => (PointerEventKind.Pressed, PointerButton.Primary),
            WmLeftButtonUp => (PointerEventKind.Released, PointerButton.Primary),
            WmRightButtonDown or WmRightButtonDoubleClick => (PointerEventKind.Pressed, PointerButton.Secondary),
            WmRightButtonUp => (PointerEventKind.Released, PointerButton.Secondary),
            WmMiddleButtonDown or WmMiddleButtonDoubleClick => (PointerEventKind.Pressed, PointerButton.Middle),
            WmMiddleButtonUp => (PointerEventKind.Released, PointerButton.Middle),
            WmXButtonDown or WmXButtonDoubleClick => (PointerEventKind.Pressed, HighWord(wParam) == 1 ? PointerButton.X1 : PointerButton.X2),
            WmXButtonUp => (PointerEventKind.Released, HighWord(wParam) == 1 ? PointerButton.X1 : PointerButton.X2),
            _ => (PointerEventKind.Canceled, PointerButton.None),
        };

        if (kind == PointerEventKind.Canceled)
        {
            input = default;
            return false;
        }

        input = new PointerInputEvent(
            kind,
            MousePointerId,
            ToDips(lParam, scale),
            ToButtons((int)wParam),
            button,
            modifiers,
            timestamp,
            PointerDeviceKind.Mouse,
            kind == PointerEventKind.Pressed ? clickCount : 0);
        return true;
    }

    /// <summary>Translates a wheel message.</summary>
    /// <param name="wParam">Its first parameter, with the distance in the high word.</param>
    /// <param name="client">The position in physical pixels relative to the window.</param>
    /// <param name="scale">Physical pixels per device-independent unit.</param>
    /// <param name="modifiers">The keyboard modifiers.</param>
    /// <param name="timestamp">The time of the event.</param>
    /// <returns>The event, one step per notch.</returns>
    public static WheelInputEvent TranslateWheel(nint wParam, Vector2 client, double scale, InputModifiers modifiers, TimeSpan timestamp) =>
        new(client / (float)scale, new Vector2(0, (float)(HighWord(wParam) / WheelStepsPerNotch)), modifiers, timestamp);

    /// <summary>Translates a key message.</summary>
    /// <param name="message">The window message.</param>
    /// <param name="wParam">The virtual key.</param>
    /// <param name="lParam">The key flags.</param>
    /// <param name="modifiers">The keyboard modifiers.</param>
    /// <param name="timestamp">The time of the event.</param>
    /// <param name="input">Receives the event.</param>
    /// <returns>Whether the message is a key the engine knows.</returns>
    public static bool TryTranslateKey(int message, nint wParam, nint lParam, InputModifiers modifiers, TimeSpan timestamp, out KeyInputEvent input)
    {
        var key = ToChartKey(KeyInterop.KeyFromVirtualKey((int)wParam));
        var isDown = message is WmKeyDown or WmSysKeyDown;
        input = new KeyInputEvent(isDown ? KeyEventKind.Pressed : KeyEventKind.Released, key, modifiers, isDown && (lParam & KeyRepeatBit) != 0, timestamp);
        return key != ChartKey.None && message is WmKeyDown or WmKeyUp or WmSysKeyDown or WmSysKeyUp;
    }

    private static Vector2 ToDips(nint lParam, double scale) =>
        new((float)((short)(lParam & 0xFFFF) / scale), (float)((short)((lParam >> 16) & 0xFFFF) / scale));

    private static short HighWord(nint value) => (short)((value >> 16) & 0xFFFF);

    private static PointerButtons ToButtons(int flags)
    {
        var buttons = PointerButtons.None;
        buttons |= (flags & MkLeft) != 0 ? PointerButtons.Primary : 0;
        buttons |= (flags & MkRight) != 0 ? PointerButtons.Secondary : 0;
        buttons |= (flags & MkMiddle) != 0 ? PointerButtons.Middle : 0;
        buttons |= (flags & MkX1) != 0 ? PointerButtons.X1 : 0;
        buttons |= (flags & MkX2) != 0 ? PointerButtons.X2 : 0;
        return buttons;
    }
}
