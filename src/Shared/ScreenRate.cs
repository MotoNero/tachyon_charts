using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if AVALONIA
namespace Tachyon.Charts.Avalonia;
#else
namespace Tachyon.Charts.Wpf;
#endif

/// <summary>
/// Finds how often the screens show a new picture, so that charts draw as many frames as the fastest screen can show.
/// On Windows it asks every display attached to the desktop; elsewhere, or when the answer is missing, it is 60.
/// </summary>
internal static partial class ScreenRate
{
    /// <summary>The rate when the screens do not tell.</summary>
    public const int Fallback = 60;

    private const int CurrentSettings = -1;
    private const uint AttachedToDesktop = 1;

    /// <summary>Gets the highest refresh rate of all displays, in frames per second.</summary>
    /// <returns>The rate, at least 30 and at most 500.</returns>
    public static int Highest()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Fallback;
        }

        var highest = 0;
        try
        {
            var device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            for (uint index = 0; EnumDisplayDevicesW(null, index, ref device, 0); index++)
            {
                if ((device.StateFlags & AttachedToDesktop) != 0)
                {
                    var mode = new DisplayMode { Size = (ushort)Marshal.SizeOf<DisplayMode>() };
                    if (EnumDisplaySettingsW(NameOf(device), CurrentSettings, ref mode))
                    {
                        highest = Math.Max(highest, (int)mode.DisplayFrequency);
                    }
                }

                device = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
            }
        }
        catch (EntryPointNotFoundException)
        {
            return Fallback;
        }
        catch (DllNotFoundException)
        {
            return Fallback;
        }

        // Some drivers answer 0 or 1 for "the default of the hardware".
        return highest is >= 30 and <= 500 ? highest : Fallback;
    }

    private static string NameOf(in DisplayDevice device)
    {
        ReadOnlySpan<ushort> name = device.DeviceName;
        var length = name.IndexOf((ushort)0);
        return new string(MemoryMarshal.Cast<ushort, char>(length < 0 ? name : name[..length]));
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettingsW(string device, int modeNumber, ref DisplayMode mode);

    [InlineArray(32)]
    private struct Chars32
    {
        private ushort _first;
    }

    [InlineArray(128)]
    private struct Chars128
    {
        private ushort _first;
    }

    // DISPLAY_DEVICEW, with characters as numbers so that the structure needs no marshalling.
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayDevice
    {
        public uint Size;
        public Chars32 DeviceName;
        public Chars128 DeviceString;
        public uint StateFlags;
        public Chars128 DeviceId;
        public Chars128 DeviceKey;
    }

    // DEVMODEW in its form for displays.
    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayMode
    {
        public Chars32 DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TrueTypeOption;
        public short Collate;
        public Chars32 FormName;
        public ushort LogPixels;
        public uint BitsPerPixel;
        public uint PixelsWidth;
        public uint PixelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }
}
