using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Resampling;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A line through the points of a data source. NaN and ±∞ interrupt the line.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// A <see cref="FifoDataSource{TX, TY}"/> needs X values that never decrease. It keeps its points in a GPU
/// buffer that only receives new data, except with <see cref="DashPattern"/>, which is built on the CPU
/// from a copy of the ring. Dispose the series on the thread of the device after detaching the chart.
/// </remarks>
public sealed class LineSeries<TX, TY> : ChartSeries, IDisposable
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
{
    private const double HighlightGrowth = 1.5;

    private readonly IDataSource<TX, TY> _source;
    private readonly FifoLinePath<TX, TY>? _fifoPath;
    private readonly SnapshotLinePath<TX, TY> _snapshotPath;
    private readonly SeriesHitTester<TX, TY> _hitTester;
    private long _appearanceVersion;
    private Rgba8 _color = new(80, 170, 240, 255);
    private double? _glowRadius;
    private double _glowIntensity = 0.6;
    private double _thickness = 1.5;
    private IReadOnlyList<float>? _dashPattern;
    private bool _isStep;
    private bool _isSpline;
    private ResamplingMode _resampling;

    /// <summary>
    /// Initializes a new instance of the <see cref="LineSeries{TX, TY}"/> class.
    /// </summary>
    /// <param name="source">The data.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public LineSeries(IDataSource<TX, TY> source, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _snapshotPath = new SnapshotLinePath<TX, TY>(source);
        _hitTester = new SeriesHitTester<TX, TY>(source);
        _fifoPath = source is FifoDataSource<TX, TY> fifo ? new FifoLinePath<TX, TY>(fifo) : null;
    }

    /// <summary>
    /// Gets or sets the line color.
    /// </summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets the thickness in device-independent units.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double Thickness
    {
        get => _thickness;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The thickness must be positive and finite.");
            }

            _thickness = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets the lengths of dashes and spaces in turn, in device-independent units, or
    /// <see langword="null"/> for a solid line. Columns of a reduced line stay solid.
    /// </summary>
    /// <exception cref="ArgumentException">The list is empty, or a length is not positive and finite.</exception>
    public IReadOnlyList<float>? DashPattern
    {
        get => _dashPattern;
        set
        {
            if (value is not null && (value.Count == 0 || value.Any(length => !float.IsFinite(length) || length <= 0)))
            {
                throw new ArgumentException("A pattern needs positive, finite lengths.", nameof(value));
            }

            _dashPattern = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the line keeps each value until the next point. Columns of
    /// a reduced line stay as they are.
    /// </summary>
    public bool IsStep
    {
        get => _isStep;
        set
        {
            _isStep = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the line runs as a smooth curve through its points. The
    /// curve stays between neighboring values. Only lines that show all their points are smoothed, and
    /// <see cref="IsStep"/> takes precedence. A ring buffer is not smoothed.
    /// </summary>
    public bool IsSpline
    {
        get => _isSpline;
        set
        {
            _isSpline = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets how the line reduces its points. The default is <see cref="ResamplingMode.Auto"/>.
    /// </summary>
    public ResamplingMode Resampling
    {
        get => _resampling;
        set
        {
            _resampling = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the reach of a glow in device-independent units, zero for none, or <see langword="null"/> for the glow of the theme.</summary>
    public double? GlowRadius
    {
        get => _glowRadius;
        set
        {
            _glowRadius = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the strength of a glow of its own, from 0 to 1.</summary>
    public double GlowIntensity
    {
        get => _glowIntensity;
        set
        {
            _glowIntensity = value;
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    private TX XOrigin => XAxis is DateTimeAxis time ? TX.CreateTruncating(time.OriginTicks) : TX.Zero;

    // The shader maps linearly, so a nonlinear axis needs pixels from the CPU.
    private LinePath ActivePath => _fifoPath is not null && _dashPattern is null && XAxis.Scaling is null && YAxis.Scaling is null ? _fifoPath : _snapshotPath;

    /// <inheritdoc />
    protected internal override long Version => _source.Version + Volatile.Read(ref _appearanceVersion);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => ActivePath.DrawnPointCount;

    /// <inheritdoc />
    protected internal override long SourcePointCount => _source is FifoDataSource<TX, TY> fifo ? fifo.Count : _source.Metadata.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        _snapshotPath.Dispose();
        _fifoPath?.Dispose();
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources() => Dispose();

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y) =>
        _hitTester.TryFind(frame, XOrigin, pixel, maximumDistance, mode, out index, out x, out y);

    /// <inheritdoc />
    protected internal override void CollectPoints(in SeriesFrame frame, in SelectionArea area, List<long> indices) =>
        _hitTester.CollectPoints(frame, XOrigin, area, indices);

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y) =>
        _hitTester.TryReadPoint(XOrigin, index, out x, out y);

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y) =>
        ActivePath.TryGetDataRange(XAxis, out x, out y);

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var options = new LineOptions(_isStep, _resampling, _dashPattern, Volatile.Read(ref _appearanceVersion), _isSpline);
        ActivePath.Update(device, frame, XAxis, YAxis, options);
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        var thickness = IsHighlighted ? _thickness + HighlightGrowth : _thickness;
        var style = new PolylineStyle((float)(thickness * frame.DpiScale), _color)
        {
            Clip = frame.Bounds,
            GlowRadius = (float)((_glowRadius ?? frame.GlowRadius) * frame.DpiScale),
            GlowIntensity = (float)(_glowRadius is null ? frame.GlowIntensity : _glowIntensity),
        };
        ActivePath.Draw(commands, frame, XAxis, style);
    }

    private void OnAppearanceChanged() => Interlocked.Increment(ref _appearanceVersion);
}

/// <summary>
/// What the paths need to know about a line.
/// </summary>
internal readonly record struct LineOptions(bool IsStep, ResamplingMode Resampling, IReadOnlyList<float>? DashPattern, long Version, bool IsSpline = false);

/// <summary>
/// The vertices of a line on the GPU.
/// </summary>
internal abstract class LinePath : IDisposable
{
    public abstract int DrawnPointCount { get; }

    protected IGpuBuffer? Buffer { get; private set; }

    public abstract bool TryGetDataRange(AxisBase xAxis, out DoubleRange x, out DoubleRange y);

    public abstract void Update(IRenderDevice device, in SeriesFrame frame, AxisBase xAxis, AxisBase yAxis, in LineOptions options);

    public abstract void Draw(ICommandList commands, in SeriesFrame frame, AxisBase xAxis, in PolylineStyle style);

    public void Dispose()
    {
        Buffer?.Dispose();
        Buffer = null;
        OnBufferReleased();
    }

    /// <summary>Forgets what the released buffer held.</summary>
    protected abstract void OnBufferReleased();

    /// <summary>
    /// Gets the value zero of an axis in the units of the data: time axes count from their origin.
    /// </summary>
    protected static TX GetOrigin<TX>(AxisBase axis)
        where TX : unmanaged, INumber<TX> =>
        axis is DateTimeAxis time ? TX.CreateTruncating(time.OriginTicks) : TX.Zero;

    protected void EnsureBuffer(IRenderDevice device, long byteCount, bool keepSize)
    {
        if (Buffer is null || Buffer.SizeInBytes < byteCount || (keepSize && Buffer.SizeInBytes != byteCount))
        {
            Buffer?.Dispose();
            var size = keepSize ? byteCount : Math.Max(byteCount, 4096);
            Buffer = device.CreateBuffer(new BufferDesc(size, BufferUsage.Vertex));
        }
    }
}
