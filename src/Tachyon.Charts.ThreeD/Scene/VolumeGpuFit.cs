using System.Globalization;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>
/// How a volume fits on the graphics card. A volume that does not fit at full size first keeps its resolution with
/// 16-bit floats, about 11 bits per value, and else shows every second, third … value along each axis, so that it still
/// shows instead of failing. Values read on the CPU, such as measurements, always use the full volume.
/// </summary>
/// <param name="Reduction">One for every value, two for every second value along each axis, and so on.</param>
/// <param name="IsExact">Whether every value reaches the GPU exactly, as 8 or 32 bits.</param>
/// <param name="Bytes">The bytes of the volume on the graphics card.</param>
/// <param name="FullBytes">The bytes the volume would take at full resolution and exact.</param>
/// <param name="Budget">The bytes a volume may take: the given limit, or a share of the memory of the graphics card.</param>
/// <param name="GraphicsMemory">The memory of the graphics card, or 0 where the system does not tell.</param>
public sealed record VolumeGpuFit(int Reduction, bool IsExact, long Bytes, long FullBytes, long Budget, long GraphicsMemory)
{
    /// <summary>Gets a value indicating whether the volume shows fewer values than it has.</summary>
    public bool IsReduced => Reduction > 1;

    /// <summary>Gets a sentence on how the volume fits, for a status line or a log.</summary>
    public string Message
    {
        get
        {
            var memory = GraphicsMemory > 0 ? string.Create(CultureInfo.InvariantCulture, $" (graphics memory {Size(GraphicsMemory)})") : string.Empty;
            if (IsReduced)
            {
                return string.Create(CultureInfo.InvariantCulture, $"Volume reduced to every {Ordinal(Reduction)} value: {Size(FullBytes)} exceed the budget of {Size(Budget)}{memory}, now {Size(Bytes)}.");
            }

            return IsExact
                ? string.Create(CultureInfo.InvariantCulture, $"Volume at full resolution, exact: {Size(Bytes)}{memory}.")
                : string.Create(CultureInfo.InvariantCulture, $"Volume at full resolution as 16-bit floats: {Size(FullBytes)} exact exceed the budget of {Size(Budget)}{memory}, now {Size(Bytes)}.");
        }
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1_000_000_000 => (bytes / 1e9).ToString("0.0", CultureInfo.InvariantCulture) + " GB",
        >= 1_000_000 => (bytes / 1e6).ToString("0", CultureInfo.InvariantCulture) + " MB",
        _ => (bytes / 1e3).ToString("0", CultureInfo.InvariantCulture) + " KB",
    };

    private static string Ordinal(int value) => value switch
    {
        2 => "2nd",
        3 => "3rd",
        _ => value.ToString(CultureInfo.InvariantCulture) + "th",
    };
}
