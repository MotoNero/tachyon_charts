using System.Numerics;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// The extremes of a column by range, as a line over a column far larger than memory asks for them: a
/// <see cref="ColumnPyramid{T}"/> built over a file, or a <see cref="Data.ColumnRecorder{T}"/> that grows as it records.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
public interface IColumnPyramid<T>
    where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    /// <summary>Gets a value indicating whether every value is covered.</summary>
    bool IsComplete { get; }

    /// <summary>Gets how far the extremes are known, from 0 to 1.</summary>
    double Progress { get; }

    /// <summary>Gets the number of pieces known so far, which grows while the extremes are built.</summary>
    int BuiltPieces { get; }

    /// <summary>Gets the extremes of all values, once complete.</summary>
    MinMax<T> Total { get; }

    /// <summary>Finds the extremes of a range exactly.</summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes; empty ones (min above max) for a range without finite values.</returns>
    MinMax<T> Query(long start, long end);

    /// <summary>Finds the extremes of the whole blocks that touch a range, without reading values.</summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes.</returns>
    MinMax<T> QueryBlocks(long start, long end);

    /// <summary>Finds the extremes of what is known so far of a range.</summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    /// <returns>The extremes; empty when nothing of the range is known yet.</returns>
    MinMax<T> QueryAvailable(long start, long end);
}
