using System.Numerics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Tachyon.Charts.Core.Input;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// The child window a swapchain renders into. Sizes come from WM_SIZE, because that is the physical size.
/// </summary>
internal sealed partial class ChartWindowHost : HwndHost
{
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;
    private const int WmSize = 0x0005;
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;
    private const int WmHitTest = 0x0084;
    private const int WmGetDialogCode = 0x0087;
    private const int WmCaptureChanged = 0x0215;
    private const int HitClient = 1;
    private const int DialogWantsAllKeys = 0x0004;
    private const float ClickDistance = 4;
    private const int TmeLeave = 0x00000002;

    private readonly ClickCounter _clicks = new(TimeSpan.FromMilliseconds(GetDoubleClickTime()), ClickDistance);
    private readonly DateTime _start = DateTime.UtcNow;
    private bool _isTrackingLeave;
    private int _heldButtons;
    private nint _window;

    public event Action<int, int>? PhysicalSizeChanged;

    public event Action<double>? ScaleChanged;

    public event Action<PointerInputEvent>? Pointer;

    public event Action<WheelInputEvent>? Wheel;

    public event Action<KeyInputEvent>? KeyInput;

    public event Action? CaptureLost;

    public nint Window => _window;

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _window = CreateWindowExW(0, "static", string.Empty, WsChild | WsVisible, 0, 0, 1, 1, hwndParent.Handle, 0, 0, 0);
        return new HandleRef(this, _window);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        DestroyWindow(hwnd.Handle);
        _window = 0;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ScaleChanged?.Invoke(newDpi.DpiScaleX);
    }

    protected override nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        var scale = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        switch (msg)
        {
            case WmSize:
                PhysicalSizeChanged?.Invoke((int)(lParam & 0xFFFF), (int)((lParam >> 16) & 0xFFFF));
                break;

            // The swapchain covers the window, GDI must not paint over it.
            case WmEraseBackground:
                handled = true;
                return 1;

            case WmPaint:
                ValidateRect(hwnd, 0);
                handled = true;
                return 0;

            // Static controls are transparent for the mouse by default.
            case WmHitTest:
                handled = true;
                return HitClient;

            case WmGetDialogCode:
                handled = true;
                return DialogWantsAllKeys;

            case WpfInput.WmMouseLeave:
                _isTrackingLeave = false;
                Pointer?.Invoke(new PointerInputEvent(PointerEventKind.Exited, 1, default, Modifiers: Modifiers, Timestamp: Timestamp, DeviceKind: PointerDeviceKind.Mouse));
                break;

            case WpfInput.WmMouseWheel:
                OnWheel(hwnd, wParam, lParam, scale);
                handled = true;
                return 0;

            case WmCaptureChanged:
                if (lParam != hwnd)
                {
                    _heldButtons = 0;
                    CaptureLost?.Invoke();
                }

                break;

            case WpfInput.WmKeyDown or WpfInput.WmKeyUp or WpfInput.WmSysKeyDown or WpfInput.WmSysKeyUp:
                if (WpfInput.TryTranslateKey(msg, wParam, lParam, Modifiers, Timestamp, out var key))
                {
                    KeyInput?.Invoke(key);
                }

                break;

            default:
                OnMouse(hwnd, msg, wParam, lParam, scale);
                break;
        }

        return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
    }

    private static InputModifiers Modifiers => WpfInput.ToModifiers(Keyboard.Modifiers);

    private TimeSpan Timestamp => DateTime.UtcNow - _start;

    private void OnMouse(nint hwnd, int msg, nint wParam, nint lParam, double scale)
    {
        var clicks = 0;
        if (WpfInput.IsButtonPress(msg))
        {
            clicks = _clicks.Click(new Vector2((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)), Timestamp);
        }

        if (!WpfInput.TryTranslateMouse(msg, wParam, lParam, scale, Modifiers, clicks, Timestamp, out var input))
        {
            return;
        }

        if (!_isTrackingLeave)
        {
            var tracking = new TrackMouseEventData { Size = Marshal.SizeOf<TrackMouseEventData>(), Flags = TmeLeave, Window = hwnd };
            _isTrackingLeave = TrackMouseEvent(ref tracking);
        }

        // Dragging over the edge of the window must keep reaching the chart.
        if (input.Kind == PointerEventKind.Pressed)
        {
            SetFocus(hwnd);
            if (_heldButtons++ == 0)
            {
                SetCapture(hwnd);
            }
        }

        Pointer?.Invoke(input);

        // After the event: releasing reports a lost capture at once, which would cancel a running drag.
        if (input.Kind == PointerEventKind.Released && _heldButtons > 0 && --_heldButtons == 0)
        {
            ReleaseCapture();
        }
    }

    private void OnWheel(nint hwnd, nint wParam, nint lParam, double scale)
    {
        var point = new NativePoint((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF));
        ScreenToClient(hwnd, ref point);
        Wheel?.Invoke(WpfInput.TranslateWheel(wParam, new Vector2(point.X, point.Y), scale, Modifiers, Timestamp));
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ValidateRect(nint window, nint rectangle);

    [LibraryImport("user32.dll")]
    private static partial nint SetFocus(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint SetCapture(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ScreenToClient(nint window, ref NativePoint point);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(ref TrackMouseEventData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData
    {
        public int Size;
        public int Flags;
        public nint Window;
        public int HoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}
