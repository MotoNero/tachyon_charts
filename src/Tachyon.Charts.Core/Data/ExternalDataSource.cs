using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Creates data sources over existing memory.
/// </summary>
public static class ExternalDataSource
{
    /// <summary>
    /// Shows arrays without copying them.
    /// </summary>
    /// <typeparam name="TX">The X type.</typeparam>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <param name="isSortedX">Whether X is sorted, or <see langword="null"/> to detect it. A given value is trusted.</param>
    /// <returns>The data source.</returns>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public static ExternalDataSource<TX, TY> Create<TX, TY>(TX[] x, TY[] y, bool? isSortedX = null)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY> => new(x, y, isSortedX);

    /// <summary>
    /// Shows memory without copying it.
    /// </summary>
    /// <typeparam name="TX">The X type.</typeparam>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <param name="isSortedX">Whether X is sorted, or <see langword="null"/> to detect it. A given value is trusted.</param>
    /// <returns>The data source.</returns>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public static ExternalDataSource<TX, TY> Create<TX, TY>(ReadOnlyMemory<TX> x, ReadOnlyMemory<TY> y, bool? isSortedX = null)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY> => new(x, y, isSortedX);
}

/// <summary>
/// A data source over memory owned by the application.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// The metadata comes from one scan on first use, so that creating the source stays free of cost. If the
/// application changes the memory, it calls <see cref="Invalidate"/>.
/// </remarks>
public sealed class ExternalDataSource<TX, TY> : IDataSource<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private readonly ReadOnlyMemory<TX> _x;
    private readonly ReadOnlyMemory<TY> _y;
    private readonly bool? _isSortedX;
    private readonly Lock _scanGate = new();
    private DataMetadata? _metadata;
    private long _version;

    internal ExternalDataSource(ReadOnlyMemory<TX> x, ReadOnlyMemory<TY> y, bool? isSortedX)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("X and Y must have the same length.", nameof(y));
        }

        _x = x;
        _y = y;
        _isSortedX = isSortedX;
    }

    /// <inheritdoc />
    public long Version => Volatile.Read(ref _version);

    /// <inheritdoc />
    public DataMetadata Metadata
    {
        get
        {
            using var scope = _scanGate.EnterScope();
            _metadata ??= Scan();
            return _metadata.Value;
        }
    }

    /// <inheritdoc />
    public DataSnapshot<TX, TY> GetSnapshot()
    {
        var metadata = Metadata;
        var version = Version;
        return new DataSnapshot<TX, TY>(_x, _y, version, metadata, version);
    }

    /// <summary>
    /// Announces that the application changed the memory.
    /// </summary>
    public void Invalidate()
    {
        using var scope = _scanGate.EnterScope();
        _metadata = null;
        Interlocked.Increment(ref _version);
    }

    private DataMetadata Scan()
    {
        var scanner = new MetadataScanner<TX, TY>();
        scanner.Add(_x.Span, _y.Span);

        var metadata = scanner.ToMetadata(_x.Length);
        if (_isSortedX is { } isSortedX)
        {
            metadata = metadata with { IsSortedX = isSortedX, UniformXStep = isSortedX ? metadata.UniformXStep : null };
        }

        return metadata;
    }
}
