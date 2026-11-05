using System.Numerics;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// What is known about the content of a data source without scanning it.
/// </summary>
/// <param name="Count">The number of points.</param>
/// <param name="IsSortedX">Whether X never decreases. An empty source counts as sorted.</param>
/// <param name="UniformXStep">The constant positive X distance, or <see langword="null"/> if there is none.</param>
/// <param name="XRange">The range of the finite X values.</param>
/// <param name="YRange">The range of the finite Y values.</param>
public readonly record struct DataMetadata(int Count, bool IsSortedX, double? UniformXStep, DoubleRange XRange, DoubleRange YRange);

/// <summary>
/// A data source independent of its value types.
/// </summary>
/// <remarks>Members are safe to use from any thread.</remarks>
public interface IDataSource
{
    /// <summary>
    /// Gets the version, which grows with every change.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Gets the metadata of the current content.
    /// </summary>
    DataMetadata Metadata { get; }
}

/// <summary>
/// A data source with its value types.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
public interface IDataSource<TX, TY> : IDataSource
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    /// <summary>
    /// Takes a consistent view of the current content, without blocking writers.
    /// </summary>
    /// <returns>The snapshot.</returns>
    DataSnapshot<TX, TY> GetSnapshot();

    /// <summary>Reads the newest point without copying the data. Safe from any thread.</summary>
    /// <param name="y">Receives the value of the newest point.</param>
    /// <returns>Whether there is a point.</returns>
    bool TryGetLatest(out TY y)
    {
        var snapshot = GetSnapshot();
        y = snapshot.Y.Length > 0 ? snapshot.Y.Span[^1] : default;
        return snapshot.Y.Length > 0;
    }
}

/// <summary>
/// A consistent view of a data source. Later appends do not change it.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <param name="X">The X values.</param>
/// <param name="Y">The Y values, as many as <paramref name="X"/>.</param>
/// <param name="Version">The version of this content.</param>
/// <param name="Metadata">The metadata of this content.</param>
/// <param name="ContentEpoch">Changes when existing points changed, not when points were appended, so caches of older points stay valid within an epoch.</param>
public readonly record struct DataSnapshot<TX, TY>(ReadOnlyMemory<TX> X, ReadOnlyMemory<TY> Y, long Version, DataMetadata Metadata, long ContentEpoch)
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>;
