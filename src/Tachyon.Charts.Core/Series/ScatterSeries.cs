using System.Numerics;
using System.Runtime.CompilerServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>How a scatter series shows its points.</summary>
public enum ScatterMode
{
    /// <summary>Markers, and a density map if there are too many visible points per pixel.</summary>
    Auto,

    /// <summary>Always markers.</summary>
    Markers,

    /// <summary>Always a density map.</summary>
    Density,
}

/// <summary>
/// Draws one marker for every finite point in a data source.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// With a <see cref="DensityAxis"/>, a plot full of points switches from markers to a density map.
/// /// Point colors and sizes are optional. When supplied, their lengths must equal the data count. The
/// caller owns their memory and must assign the property again after changing its contents. Dispose
/// the series on the device thread after detaching it from the chart.
/// </remarks>
public sealed class ScatterSeries<TX, TY> : DataSeries<TX, TY>, IDisposable
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private const int DensitySampleCount = 65536;
    private const double DensityReturn = 0.8;

    private IGpuBuffer? _buffer;
    private IRenderDevice? _device;
    private MarkerInstance[] _instances = [];
    private int _count;
    private long _uploadedDataVersion = -1;
    private long _uploadedInstanceVersion = -1;
    private long _instanceVersion;
    private Rgba8 _color = new(80, 170, 240, 255);
    private Rgba8 _borderColor = new(20, 50, 70, 255);
    private double _size = 7;
    private double _borderThickness = 1;
    private MarkerShape _shape;
    private ReadOnlyMemory<Rgba8> _pointColors;
    private ReadOnlyMemory<float> _pointSizes;
    private readonly DensityRaster _density = new();
    private ColorAxis? _densityAxis;
    private ScatterMode _mode;
    private double _densityThreshold = 1;
    private bool _isDensityActive;
    private SeriesFrame _estimateFrame;
    private long _estimateVersion = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScatterSeries{TX, TY}"/> class.
    /// </summary>
    /// <param name="source">The point coordinates.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public ScatterSeries(IDataSource<TX, TY> source, AxisBase xAxis, AxisBase yAxis)
        : base(source, xAxis, yAxis)
    {
    }

    /// <summary>Gets or sets the scale of the density map, or <see langword="null"/> to always draw markers. It belongs to a pane of the chart.</summary>
    public ColorAxis? DensityAxis
    {
        get => _densityAxis;
        set
        {
            _densityAxis = value;
            OnInstanceChanged();
        }
    }

    /// <summary>Gets or sets how the points are shown.</summary>
    public ScatterMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _estimateVersion = -1;
            OnInstanceChanged();
        }
    }

    /// <summary>Gets or sets the visible points per pixel from which <see cref="ScatterMode.Auto"/> shows the density.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double DensityThreshold
    {
        get => _densityThreshold;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold must be positive.");
            }

            _densityThreshold = value;
            _estimateVersion = -1;
            OnInstanceChanged();
        }
    }

    /// <summary>Gets a value indicating whether the last frame showed the density.</summary>
    public bool IsDensityActive => Volatile.Read(ref _isDensityActive);

    /// <inheritdoc />
    public override ColorAxis? ColorAxis => _densityAxis;

    /// <summary>Gets or sets the fill color used when <see cref="PointColors"/> is empty.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            OnInstanceChanged();
        }
    }

    /// <summary>Gets or sets the marker diameter in device-independent units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double Size
    {
        get => _size;
        set
        {
            ValidatePositive(value, nameof(value));
            _size = value;
            OnInstanceChanged();
        }
    }

    /// <summary>Gets or sets the marker shape.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The shape is unknown.</exception>
    public MarkerShape Shape
    {
        get => _shape;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The marker shape is unknown.");
            }

            _shape = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the inward border thickness in device-independent units. Zero disables it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not finite.</exception>
    public double BorderThickness
    {
        get => _borderThickness;
        set
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The border thickness must be finite and non-negative.");
            }

            _borderThickness = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the border color.</summary>
    public Rgba8 BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets optional per-point fill colors. An empty memory uses <see cref="Color"/>.
    /// </summary>
    public ReadOnlyMemory<Rgba8> PointColors
    {
        get => _pointColors;
        set
        {
            _pointColors = value;
            OnInstanceChanged();
        }
    }

    /// <summary>
    /// Gets or sets optional per-point diameters in device-independent units. An empty memory uses <see cref="Size"/>.
    /// </summary>
    public ReadOnlyMemory<float> PointSizes
    {
        get => _pointSizes;
        set
        {
            _pointSizes = value;
            OnInstanceChanged();
        }
    }

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _isDensityActive ? _density.Count : _count;

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _device = null;
        _density.Dispose();
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources() => Dispose();

    /// <inheritdoc />
    protected internal override bool TryGetValueRange(out DoubleRange range)
    {
        // Pixels without a point stay below the range, which lets a transparent color show the plot.
        range = new DoubleRange(1, Math.Max(_density.Maximum, 2));
        return _isDensityActive && _density.Maximum > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var metadata = Source.Metadata;
        var offset = double.CreateTruncating(GetOrigin());
        x = metadata.XRange.IsEmpty ? metadata.XRange : new DoubleRange(metadata.XRange.Min - offset, metadata.XRange.Max - offset);
        y = metadata.YRange;
        return metadata.Count > 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        if (_densityAxis is { } densityAxis && ChooseDensity(frame))
        {
            Volatile.Write(ref _isDensityActive, true);
            if (!_density.HasPoints(Snapshot.Version))
            {
                TakeDensityPoints(device);
            }

            _density.Update(device, frame);
            _density.UpdateTable(device, densityAxis);
            return;
        }

        Volatile.Write(ref _isDensityActive, false);
        var dataVersion = Snapshot.Version;
        var instanceVersion = Volatile.Read(ref _instanceVersion);
        if (_device == device && _uploadedDataVersion == dataVersion && _uploadedInstanceVersion == instanceVersion)
        {
            return;
        }

        if (_device != device)
        {
            _buffer?.Dispose();
            _buffer = null;
            _device = device;
        }

        var x = Snapshot.X.Span;
        var y = Snapshot.Y.Span;
        ValidateStyleData(x.Length);
        if (_instances.Length < x.Length)
        {
            _instances = new MarkerInstance[x.Length];
        }

        FillInstances(x, y);
        UploadInstances(device);
        _uploadedDataVersion = dataVersion;
        _uploadedInstanceVersion = instanceVersion;
    }

    private void FillInstances(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        var colors = _pointColors.Span;
        var sizes = _pointSizes.Span;
        var origin = GetOrigin();
        _count = 0;
        for (var i = 0; i < x.Length; i++)
        {
            // The GPU maps linearly, so a nonlinear scale is part of the stored position.
            var relativeX = double.CreateTruncating(x[i] - origin);
            var valueY = double.CreateTruncating(y[i]);
            relativeX = XAxis.Scaling?.Forward(relativeX) ?? relativeX;
            valueY = YAxis.Scaling?.Forward(valueY) ?? valueY;
            var size = sizes.IsEmpty ? (float)_size : sizes[i];
            if (!float.IsFinite(size) || size <= 0)
            {
                throw new InvalidOperationException($"Point size {i} must be positive and finite.");
            }

            if (!double.IsFinite(relativeX) || !double.IsFinite(valueY)
                || relativeX is < -float.MaxValue or > float.MaxValue
                || valueY is < -float.MaxValue or > float.MaxValue)
            {
                continue;
            }

            _instances[_count++] = new MarkerInstance(
                new Vector2((float)relativeX, (float)valueY),
                size,
                colors.IsEmpty ? _color : colors[i]);
        }
    }

    private void UploadInstances(IRenderDevice device)
    {
        if (_count > 0)
        {
            var byteCount = (long)_count * Unsafe.SizeOf<MarkerInstance>();
            if (_buffer is null || _buffer.SizeInBytes < byteCount)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(byteCount, BufferUsage.Vertex));
            }

            _buffer.Write<MarkerInstance>(0, _instances.AsSpan(0, _count));
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_isDensityActive)
        {
            _density.Draw(commands, frame, _densityAxis!);
            return;
        }

        if (_buffer is null || _count == 0)
        {
            return;
        }

        var xOffset = frame.X.ToPixelLinear(0);
        var yOffset = frame.Y.ToPixelLinear(0);
        var style = new MarkerStyle(_shape, (float)(_borderThickness * frame.DpiScale), _borderColor)
        {
            Scale = new Vector2((float)frame.X.PixelsPerUnit, (float)frame.Y.PixelsPerUnit),
            Offset = new Vector2((float)xOffset, (float)yOffset),
            SizeScale = (float)frame.DpiScale,
            Clip = frame.Bounds,
        };
        commands.DrawMarkers(_buffer, 0, _count, style);
    }

    // A sample tells the visible density without touching all points. Between the two limits the mode
    // stays, so that a view at the threshold does not flicker.
    private bool ChooseDensity(in SeriesFrame frame)
    {
        if (_mode != ScatterMode.Auto)
        {
            return _mode == ScatterMode.Density;
        }

        if (_estimateVersion == Snapshot.Version && _estimateFrame == frame)
        {
            return _isDensityActive;
        }

        var x = Snapshot.X.Span;
        var y = Snapshot.Y.Span;
        var stride = Math.Max(x.Length / DensitySampleCount, 1);
        var origin = GetOrigin();
        var bounds = frame.Bounds;
        var inside = 0L;
        for (var i = 0; i < x.Length; i += stride)
        {
            var pixelX = frame.X.ToPixel(double.CreateTruncating(x[i] - origin));
            var pixelY = frame.Y.ToPixel(double.CreateTruncating(y[i]));
            if (pixelX >= bounds.X && pixelY >= bounds.Y && pixelX < bounds.X + bounds.Width && pixelY < bounds.Y + bounds.Height)
            {
                inside++;
            }
        }

        var density = inside * (double)stride / Math.Max(bounds.Width * (double)bounds.Height, 1);
        _estimateVersion = Snapshot.Version;
        _estimateFrame = frame;
        return density >= _densityThreshold * (_isDensityActive ? DensityReturn : 1);
    }

    private void TakeDensityPoints(IRenderDevice device)
    {
        var x = Snapshot.X.Span;
        var y = Snapshot.Y.Span;
        var origin = GetOrigin();
        var points = _density.RentPoints(x.Length);
        var count = 0;
        for (var i = 0; i < x.Length; i++)
        {
            var valueX = double.CreateTruncating(x[i] - origin);
            var valueY = double.CreateTruncating(y[i]);
            valueX = XAxis.Scaling?.Forward(valueX) ?? valueX;
            valueY = YAxis.Scaling?.Forward(valueY) ?? valueY;
            if (double.IsFinite(valueX) && double.IsFinite(valueY))
            {
                points[count++] = new Vector2((float)valueX, (float)valueY);
            }
        }

        _density.CommitPoints(device, count, Snapshot.Version);
    }

    private static void ValidatePositive(double value, string parameter)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameter, value, "The marker size must be positive and finite.");
        }
    }

    private void ValidateStyleData(int count)
    {
        if (!_pointColors.IsEmpty && _pointColors.Length != count)
        {
            throw new InvalidOperationException("PointColors must be empty or have one entry per point.");
        }

        if (!_pointSizes.IsEmpty && _pointSizes.Length != count)
        {
            throw new InvalidOperationException("PointSizes must be empty or have one entry per point.");
        }
    }

    private void OnInstanceChanged()
    {
        Interlocked.Increment(ref _instanceVersion);
        OnAppearanceChanged();
    }

    private TX GetOrigin() =>
        XAxis is DateTimeAxis time ? TX.CreateTruncating(time.OriginTicks) : TX.Zero;
}
