using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Rows of a spectrogram for the examples.</summary>
public static class SpectrogramSignal
{
    /// <summary>Appends a row of the signal.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="index">The number of the row.</param>
    /// <param name="row">A buffer for one row.</param>
    /// <param name="withPauses">Whether the signal has pauses of missing rows.</param>
    public static void AppendSignal(SpectrogramData data, long index, Span<float> row, bool withPauses = true)
    {
        var center = (row.Length / 2.0) + ((row.Length / 2.5) * Math.Sin(index * 0.03));
        for (var i = 0; i < row.Length; i++)
        {
            var distance = (i - center) / 6;
            var overtone = (i - (center / 2)) / 10;
            row[i] = withPauses && index % 100 is >= 60 and < 66 ? float.NaN : (float)(Math.Exp(-distance * distance) + (0.4 * Math.Exp(-overtone * overtone)) + (0.05 * Math.Sin(i * 0.9)));
        }

        data.AppendRow(row);
    }
}
