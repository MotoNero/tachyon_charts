using System.Runtime.InteropServices;

namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>Tells whether the computer runs on mains or battery, for measurement reports.</summary>
public static class PowerInfo
{
    /// <summary>Describes the power source.</summary>
    /// <returns>The text for a report.</returns>
    public static string Describe()
    {
        if (!OperatingSystem.IsWindows() || !GetSystemPowerStatus(out var status))
        {
            return "Strom unbekannt";
        }

        return status.AcLineStatus switch
        {
            0 => "Akku",
            1 => "Netz",
            _ => "Strom unbekannt",
        };
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
