using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Direct access to reserved space of a <see cref="DataSource{TX, TY}"/>.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// Disposing publishes all reserved points, unless <see cref="Commit"/> or <see cref="Cancel"/>
/// decided otherwise. The owning thread must dispose it, other writers wait until then.
/// </remarks>
public ref struct DataWriter<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private DataSource<TX, TY>? _source;

    internal DataWriter(DataSource<TX, TY> source, Span<TX> x, Span<TY> y)
    {
        _source = source;
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the space for the X values.
    /// </summary>
    public Span<TX> X { get; private set; }

    /// <summary>
    /// Gets the space for the Y values.
    /// </summary>
    public Span<TY> Y { get; private set; }

    /// <summary>
    /// Publishes only the first points, for sources that delivered fewer than reserved.
    /// </summary>
    /// <param name="count">The number of points to publish.</param>
    /// <exception cref="ArgumentOutOfRangeException">The count is out of range.</exception>
    public void Commit(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, X.Length);

        X = X[..count];
        Y = Y[..count];
        Dispose();
    }

    /// <summary>
    /// Discards the reserved points.
    /// </summary>
    public void Cancel()
    {
        var source = _source;
        _source = null;
        source?.CancelWrite();
    }

    /// <summary>
    /// Publishes the reserved points.
    /// </summary>
    public void Dispose()
    {
        var source = _source;
        _source = null;
        source?.EndWrite(X, Y);
    }
}
