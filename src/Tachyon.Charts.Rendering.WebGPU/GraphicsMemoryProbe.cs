using System.Runtime.InteropServices;

namespace Tachyon.Charts.Rendering.WebGPU;

/// <summary>
/// Finds the memory of a graphics card, which WebGPU does not report. On Windows the DXGI adapter with the same vendor
/// and device number tells its dedicated memory; a card that shares the system memory gets half of the shared part.
/// Elsewhere the answer is 0, unknown.
/// </summary>
internal static unsafe class GraphicsMemoryProbe
{
    // Below this a card counts as one that shares the system memory.
    private const long SmallDedicated = 512L << 20;

    private static readonly Guid _factory1 = new(0x770aae78, 0xf26f, 0x4dba, 0xa8, 0x29, 0x25, 0x3c, 0x83, 0xd1, 0xb3, 0x87);

    /// <summary>Finds the memory of a card.</summary>
    /// <param name="vendor">The vendor number.</param>
    /// <param name="device">The device number.</param>
    /// <returns>The bytes, or 0 when unknown.</returns>
    public static long Find(uint vendor, uint device)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        try
        {
            return FindOnWindows(vendor, device);
        }
        catch (DllNotFoundException)
        {
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>The memory to count with, from what an adapter tells.</summary>
    /// <param name="dedicated">The memory of its own.</param>
    /// <param name="shared">The system memory it may use.</param>
    /// <returns>The bytes.</returns>
    internal static long Usable(long dedicated, long shared) => dedicated >= SmallDedicated ? dedicated : dedicated + (shared / 2);

    // unsafe: DXGI is a COM interface, reached through its table of methods.
    private static long FindOnWindows(uint vendor, uint device)
    {
        var id = _factory1;
        if (CreateDXGIFactory1(&id, out var factory) < 0 || factory == 0)
        {
            return 0;
        }

        try
        {
            var table = *(nint**)factory;
            var enumerate = (delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)table[12]; // IDXGIFactory1::EnumAdapters1
            for (uint i = 0; ; i++)
            {
                nint adapter;
                if (enumerate(factory, i, &adapter) < 0)
                {
                    return 0;
                }

                try
                {
                    var describe = (delegate* unmanaged[Stdcall]<nint, AdapterDescription*, int>)(*(nint**)adapter)[10]; // IDXGIAdapter1::GetDesc1
                    AdapterDescription description;
                    if (describe(adapter, &description) >= 0 && description.VendorId == vendor && description.DeviceId == device)
                    {
                        return Usable((long)description.DedicatedVideoMemory, (long)description.SharedSystemMemory);
                    }
                }
                finally
                {
                    Release(adapter);
                }
            }
        }
        finally
        {
            Release(factory);
        }
    }

    private static void Release(nint unknown) => ((delegate* unmanaged[Stdcall]<nint, uint>)(*(nint**)unknown)[2])(unknown);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(Guid* id, out nint factory);

    /// <summary>DXGI_ADAPTER_DESC1.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AdapterDescription
    {
        public fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }
}
