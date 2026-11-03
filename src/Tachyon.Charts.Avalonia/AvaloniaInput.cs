using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Tachyon.Charts.Core.Input;

namespace Tachyon.Charts.Avalonia;

/// <summary>
/// Translates native input into the types of the engine. Holds no chart logic.
/// </summary>
internal static class AvaloniaInput
{
    public static InputModifiers ToModifiers(KeyModifiers keys)
    {
        var modifiers = InputModifiers.None;
        modifiers |= (keys & KeyModifiers.Shift) != 0 ? InputModifiers.Shift : 0;
        modifiers |= (keys & KeyModifiers.Control) != 0 ? InputModifiers.Control : 0;
        modifiers |= (keys & KeyModifiers.Alt) != 0 ? InputModifiers.Alt : 0;
        modifiers |= (keys & KeyModifiers.Meta) != 0 ? InputModifiers.Meta : 0;
        return modifiers;
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

    public static PointerButton ToButton(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => PointerButton.Primary,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => PointerButton.Secondary,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => PointerButton.Middle,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => PointerButton.X1,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => PointerButton.X2,
        _ => PointerButton.None,
    };

    public static PointerButtons ToButtons(PointerPointProperties properties)
    {
        var buttons = PointerButtons.None;
        buttons |= properties.IsLeftButtonPressed ? PointerButtons.Primary : 0;
        buttons |= properties.IsRightButtonPressed ? PointerButtons.Secondary : 0;
        buttons |= properties.IsMiddleButtonPressed ? PointerButtons.Middle : 0;
        buttons |= properties.IsXButton1Pressed ? PointerButtons.X1 : 0;
        buttons |= properties.IsXButton2Pressed ? PointerButtons.X2 : 0;
        return buttons;
    }
}

/// <summary>
/// A native child window for the swapchain. Avalonia places a holder window of its own between this
/// window and the main window, where mouse messages would end. So this window takes them and hands
/// them to the main window, which keeps delivering all input to the chart.
/// </summary>
internal sealed partial class ChartWindowHost : NativeControlHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int GwlpWndProc = -4;
    private const uint GaRoot = 2;
    private const uint WmNcHitTest = 0x0084;
    private const uint WmMouseFirst = 0x0200;
    private const uint WmMouseLast = 0x020D;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmLeftButtonDoubleClick = 0x0203;
    private const uint WmRightButtonDoubleClick = 0x0206;
    private const uint WmMiddleButtonDoubleClick = 0x0209;
    private const uint WmXButtonDoubleClick = 0x020D;
    private const nint HtClient = 1;

    // A field, because native code calls it for as long as the window lives.
    private readonly WindowProcedure _procedure;
    private nint _original;

    public ChartWindowHost()
    {
        _procedure = OnMessage;
    }

    private delegate nint WindowProcedure(nint window, uint message, nint wParam, nint lParam);

    public nint Handle { get; private set; }

    /// <summary>Tests whether the cursor is over this window, which the main window cannot know.</summary>
    /// <returns>Whether it is.</returns>
    public bool ContainsCursor() =>
        Handle != 0 && GetCursorPos(out var point) && GetWindowRect(Handle, out var bounds)
        && point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom;

    /// <inheritdoc />
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
        {
            return base.CreateNativeControlCore(parent);
        }

        Handle = CreateWindowExW(0, "static", string.Empty, WsChild | WsVisible, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
        _original = SetWindowLongPtrW(Handle, GwlpWndProc, Marshal.GetFunctionPointerForDelegate(_procedure));
        return new PlatformHandle(Handle, "HWND");
    }

    /// <inheritdoc />
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (OperatingSystem.IsWindows())
        {
            DestroyWindow(control.Handle);
            Handle = 0;
        }
    }

    private nint OnMessage(nint window, uint message, nint wParam, nint lParam)
    {
        // A static window would pass the mouse on to the holder.
        if (message == WmNcHitTest)
        {
            return HtClient;
        }

        // The wheel carries screen positions and finds its way by itself.
        if (message is >= WmMouseFirst and <= WmMouseLast and not WmMouseWheel)
        {
            var root = GetAncestor(window, GaRoot);
            var point = new NativePoint((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
            MapWindowPoints(window, root, ref point, 1);

            // Static windows report the second click as a message of its own, which the main window
            // does not know. The button-down message sits two below it.
            var forwarded = message is WmLeftButtonDoubleClick or WmRightButtonDoubleClick or WmMiddleButtonDoubleClick or WmXButtonDoubleClick
                ? message - 2
                : message;
            return SendMessageW(root, forwarded, wParam, (nint)((point.X & 0xFFFF) | ((point.Y & 0xFFFF) << 16)));
        }

        return CallWindowProcW(_original, window, message, wParam, lParam);
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateWindowExW(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint window, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint CallWindowProcW(nint procedure, nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageW(nint window, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial nint GetAncestor(nint window, uint flags);

    [LibraryImport("user32.dll")]
    private static partial int MapWindowPoints(nint from, nint to, ref NativePoint points, uint count);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out NativeRect bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
