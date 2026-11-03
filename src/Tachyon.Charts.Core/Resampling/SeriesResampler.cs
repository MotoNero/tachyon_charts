using System.Numerics;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Resampling;

/// <summary>
/// How a series reduces its points for drawing.
/// </summary>
public enum ResamplingMode
{
    /// <summary>
    /// Min/max for sorted data with more points than the columns can show, otherwise all points.
    /// </summary>
    Auto,

    /// <summary>
    /// All visible points.
    /// </summary>
    None,

    /// <summary>
    /// The extremes per pixel column. Peaks stay visible. Needs sorted X.
    /// </summary>
    MinMax,

    /// <summary>
    /// A shape-preserving selection, see <see cref="Lttb"/>. Needs sorted X.
    /// </summary>
    Lttb,
}

/// <summary>
/// The outcome of a resampling.
/// </summary>
/// <param name="Mode">The mode used, never <see cref="ResamplingMode.Auto"/>.</param>
/// <param name="Start">The first visible index.</param>
/// <param name="End">The index behind the last visible one.</param>
/// <param name="SelectedCount">The number of indices written with <see cref="ResamplingMode.Lttb"/>.</param>
public readonly record struct ResampleResult(ResamplingMode Mode, int Start, int End, int SelectedCount);

/// <summary>
/// Resamples an append-only data source and keeps its pyramid up to date in the background. Until the
/// pyramid catches up, queries scan the remaining values, with the same result.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// A <see cref="FifoDataSource{TX, TY}"/> overwrites points, so it gets no pyramid and every query scans.
/// </remarks>
public sealed class SeriesResampler<TX, TY> : IThreadPoolWorkItem
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
{
    private readonly IDataSource<TX, TY> _source;
    private readonly int _factor;
    private readonly bool _usesPyramid;
    private readonly Lock _updateGate = new();
    private volatile EpochPyramid _pyramid;
    private long _builtVersion = -1;
    private int _isScheduled;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesResampler{TX, TY}"/> class.
    /// </summary>
    /// <param name="source">The data source.</param>
    /// <param name="factor">The growth of the pyramid per level.</param>
    /// <exception cref="ArgumentOutOfRangeException">The factor is out of range.</exception>
    public SeriesResampler(IDataSource<TX, TY> source, int factor = LodPyramid<TY>.DefaultFactor)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _factor = factor;
        _usesPyramid = source is not FifoDataSource<TX, TY>;
        _pyramid = new EpochPyramid(new LodPyramid<TY>(factor), 0);
    }

    /// <summary>
    /// Gets the memory of the pyramid in bytes.
    /// </summary>
    public long PyramidByteCount => _pyramid.Pyramid.ByteCount;

    /// <summary>
    /// Resamples the visible range.
    /// </summary>
    /// <param name="visible">The visible X range, not empty.</param>
    /// <param name="mode">The wanted mode. Without sorted X, all modes fall back to <see cref="ResamplingMode.None"/>.</param>
    /// <param name="minima">Receives the minimum per column for <see cref="ResamplingMode.MinMax"/>. Its length is the number of columns.</param>
    /// <param name="maxima">Receives the maximum per column, as long as <paramref name="minima"/>.</param>
    /// <param name="indices">Receives the indices for <see cref="ResamplingMode.Lttb"/>, with the target count as length.</param>
    /// <returns>What was done.</returns>
    /// <exception cref="ArgumentException">The range is empty or a span does not fit.</exception>
    public ResampleResult Resample(DoubleRange visible, ResamplingMode mode, Span<TY> minima, Span<TY> maxima, Span<int> indices)
    {
        var snapshot = _source.GetSnapshot();
        ScheduleUpdate(snapshot.Version);

        var x = snapshot.X.Span;
        var y = snapshot.Y.Span;
        if (!snapshot.Metadata.IsSortedX)
        {
            return new ResampleResult(ResamplingMode.None, 0, x.Length, 0);
        }

        var start = MinMaxDecimator.LowerBound(x, visible.Min, 0, 0);
        var end = MinMaxDecimator.UpperBound(x, visible.Max);
        if (mode == ResamplingMode.Auto)
        {
            mode = end - start > minima.Length * 2L ? ResamplingMode.MinMax : ResamplingMode.None;
        }

        switch (mode)
        {
            case ResamplingMode.MinMax:
                var current = _pyramid;
                var pyramid = _usesPyramid && current.Epoch == snapshot.ContentEpoch ? current.Pyramid : null;
                MinMaxDecimator.Decimate(x, y, pyramid, visible, minima, maxima);
                return new ResampleResult(mode, start, end, 0);

            case ResamplingMode.Lttb:
                var selected = Lttb.Select(x[start..end], y[start..end], indices);
                for (var i = 0; i < selected; i++)
                {
                    indices[i] += start;
                }

                return new ResampleResult(mode, start, end, selected);

            default:
                return new ResampleResult(ResamplingMode.None, start, end, 0);
        }
    }

    /// <summary>
    /// Brings the pyramid up to date on the calling thread.
    /// </summary>
    public void UpdateNow()
    {
        if (_usesPyramid)
        {
            Update();
        }
    }

    /// <inheritdoc />
    void IThreadPoolWorkItem.Execute()
    {
        Update();
        Volatile.Write(ref _isScheduled, 0);

        // A change after the last check would otherwise wait for the next query.
        if (_source.Version != Volatile.Read(ref _builtVersion))
        {
            ScheduleUpdate(_source.Version);
        }
    }

    private void ScheduleUpdate(long version)
    {
        if (_usesPyramid
            && version != Volatile.Read(ref _builtVersion)
            && Interlocked.CompareExchange(ref _isScheduled, 1, 0) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }
    }

    private void Update()
    {
        using (_updateGate.EnterScope())
        {
            var snapshot = _source.GetSnapshot();
            var current = _pyramid;
            if (current.Epoch != snapshot.ContentEpoch)
            {
                current = new EpochPyramid(new LodPyramid<TY>(_factor), snapshot.ContentEpoch);
                _pyramid = current;
            }

            if (snapshot.Metadata.IsSortedX)
            {
                current.Pyramid.Update(snapshot.Y.Span);
            }

            Volatile.Write(ref _builtVersion, snapshot.Version);
        }
    }

    private sealed record EpochPyramid(LodPyramid<TY> Pyramid, long Epoch);
}
