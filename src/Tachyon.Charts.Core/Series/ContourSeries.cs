using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// Isolines of a grid, optionally with filled bands between them. The samples sit in the middle of the
/// cells of a heatmap with the same layout. A device with compute shaders finds the lines itself,
/// any other uses <see cref="MarchingSquares"/>.
/// </summary>
public sealed class ContourSeries : RasterSeries
{
    private const int DefaultLevelCount = 8;
    private const int PointsPerSegment = 3;

    private readonly ColormapTexture _bandTexture = new();
    private double _xStart;
    private double _xStep = 1;
    private double _yStart;
    private double _yStep = 1;
    private float[]? _levels;
    private bool _isFilled;
    private bool _showsLines = true;
    private bool _showsLabels;
    private float[] _levelsInUse = [];
    private Rgba8 _lineColor = new(240, 240, 240, 255);
    private double _lineThickness = 1.5;
    private long _layoutVersion;

    private IGpuTexture? _values;
    private IGpuBuffer? _segments;
    private long _segmentCount;
    private long _builtVersion = -1;
    private long _uploadedVersion = -1;
    private long _bandVersion = -1;
    private IGpuTexture? _bands;
    private PixelRect _bounds;
    private Vector2 _sectionOrigin;
    private Vector2 _sectionSize;
    private Vector2[] _points = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ContourSeries"/> class.
    /// </summary>
    /// <param name="data">The samples.</param>
    /// <param name="xAxis">The horizontal axis, linear.</param>
    /// <param name="yAxis">The vertical axis, linear.</param>
    /// <param name="colorAxis">The value scale.</param>
    /// <exception cref="ArgumentException">An axis is not linear: the GPU maps the lines linearly.</exception>
    public ContourSeries(HeatmapData data, AxisBase xAxis, AxisBase yAxis, ColorAxis colorAxis)
        : base(xAxis, yAxis, colorAxis)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        if (XAxis.Scaling is not null || YAxis.Scaling is not null)
        {
            throw new ArgumentException("A contour needs linear axes.", nameof(xAxis));
        }
    }

    /// <summary>Gets the samples.</summary>
    public HeatmapData Data { get; }

    /// <summary>Gets the number of segments of the last frame.</summary>
    public long SegmentCount => Interlocked.Read(ref _segmentCount);

    /// <summary>Gets or sets the levels, or <see langword="null"/> for equal steps through the values.</summary>
    /// <exception cref="ArgumentException">A level is not finite.</exception>
    public IReadOnlyList<double>? Levels
    {
        get => _levels?.Select(level => (double)level).ToArray(); // NOSONAR: public property, a copy protects the levels
        set
        {
            if (value is not null && value.Any(level => !double.IsFinite(level)))
            {
                throw new ArgumentException("The levels must be finite.", nameof(value));
            }

            _levels = value?.Order().Select(level => (float)level).ToArray();
            Touch();
        }
    }

    /// <summary>Gets or sets a value indicating whether the bands between the lines are filled.</summary>
    public bool IsFilled
    {
        get => _isFilled;
        set
        {
            _isFilled = value;
            Touch();
        }
    }

    /// <summary>Gets or sets a value indicating whether the lines are drawn.</summary>
    public bool ShowsLines
    {
        get => _showsLines;
        set
        {
            _showsLines = value;
            Touch();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the lines carry their values: along the line, upright, on a plate
    /// that hides the line beneath, and never on top of each other.
    /// </summary>
    public bool ShowsLabels
    {
        get => _showsLabels;
        set
        {
            _showsLabels = value;
            Touch();
        }
    }

    /// <summary>Gets the levels of the last frame, for the labels.</summary>
    internal float[] LevelsInUse => _levelsInUse;

    /// <summary>Gets the place of a grid point in values of the axes: the middle of its cell.</summary>
    internal (double X, double Y) GridPoint(double column, double row) => (_xStart + ((column + 0.5) * _xStep), _yStart + ((row + 0.5) * _yStep));

    /// <summary>Gets or sets the color of the lines.</summary>
    public Rgba8 LineColor
    {
        get => _lineColor;
        set
        {
            _lineColor = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the thickness of the lines in device-independent units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double LineThickness
    {
        get => _lineThickness;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The thickness must be positive.");
            }

            _lineThickness = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart
    {
        get => _xStart;
        set
        {
            _xStart = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the width of a column.</summary>
    public double XStep
    {
        get => _xStep;
        set
        {
            _xStep = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the lower edge of the first row.</summary>
    public double YStart
    {
        get => _yStart;
        set
        {
            _yStart = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the height of a row.</summary>
    public double YStep
    {
        get => _yStep;
        set
        {
            _yStep = value;
            Touch();
        }
    }

    /// <inheritdoc />
    protected internal override long Version => Data.Version + Volatile.Read(ref _layoutVersion);

    private DoubleRange XRange => new(_xStart, _xStart + (_xStep * Data.Width));

    private DoubleRange YRange => new(_yStart, _yStart + (_yStep * Data.Height));

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = XRange;
        y = YRange;
        return true;
    }

    /// <inheritdoc />
    protected internal override bool TryGetValueRange(out DoubleRange range)
    {
        range = Data.ValueRange;
        return !range.IsEmpty;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        base.Prepare(device, frame);
        var version = Version;
        var levels = _levels ?? CreateLevels();
        _levelsInUse = levels;
        var values = Data.Values;
        if (_values is null)
        {
            _values = device.CreateTexture(new TextureDesc(new PixelSize(Data.Width, Data.Height), TextureFormat.R32Float));
            _uploadedVersion = -1;
        }

        if (_uploadedVersion != Data.Version)
        {
            _values.Write(MemoryMarshal.AsBytes(values.AsSpan()));
            _uploadedVersion = Data.Version;
        }

        if (_builtVersion != version)
        {
            BuildSegments(device, values, levels);
            _builtVersion = version;
        }

        if (_isFilled)
        {
            UpdateBands(device, levels);
            (_bounds, _sectionOrigin, _sectionSize) = Place(frame, XRange, YRange, Vector2.One);
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_isFilled && _values is not null && _bands is not null && _bounds.Width > 0 && _bounds.Height > 0)
        {
            // The bands are values of the scale, so the range is the one the table was made for.
            var style = ColorAxis.CreateRasterStyle() with { SectionOrigin = _sectionOrigin, SectionSize = _sectionSize, IsInterpolated = true };
            commands.DrawRaster(_values, _bands, _bounds, style);
        }

        if (_showsLines && _segments is not null && _segmentCount > 0)
        {
            var offset = new Vector2((float)frame.X.ToPixel(_xStart + (_xStep / 2)), (float)frame.Y.ToPixel(_yStart + (_yStep / 2)));
            var scale = new Vector2((float)(frame.X.PixelsPerUnit * _xStep), (float)(frame.Y.PixelsPerUnit * _yStep));
            var width = (float)(_lineThickness * frame.DpiScale);
            commands.DrawPolyline(_segments, 0, (int)(_segmentCount * PointsPerSegment), new PolylineStyle(width, _lineColor) { Scale = scale, Offset = offset, Clip = frame.Bounds });
        }
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        _bandTexture.Dispose();
        _values?.Dispose();
        _segments?.Dispose();
        _values = null;
        _segments = null;
        _bands = null;
        _uploadedVersion = -1;
        _builtVersion = -1;
        _bandVersion = -1;
    }

    private float[] CreateLevels()
    {
        var range = Data.ValueRange;
        if (range.IsEmpty)
        {
            return [];
        }

        var levels = new float[DefaultLevelCount];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = (float)(range.Min + ((range.Max - range.Min) * (i + 1) / (levels.Length + 1)));
        }

        return levels;
    }

    private void BuildSegments(IRenderDevice device, float[] values, float[] levels)
    {
        if (device.Compute is { } compute)
        {
            var capacity = EnsureBuffer(device, Math.Max(_segments is null ? 0 : _segments.SizeInBytes / IComputeOperations.ContourSegmentSize, 4L * (Data.Width + Data.Height) * Math.Max(levels.Length, 1)), BufferUsage.Vertex | BufferUsage.Storage);
            var count = compute.GenerateContours(_values!, levels, _segments!);
            if (count > capacity)
            {
                // The shader counts on when the buffer is full, so the second run fits.
                EnsureBuffer(device, count, BufferUsage.Vertex | BufferUsage.Storage);
                compute.GenerateContours(_values!, levels, _segments!);
            }

            Interlocked.Exchange(ref _segmentCount, count);
            return;
        }

        var segments = new List<ContourSegment>();
        MarchingSquares.Generate(values, Data.Width, Data.Height, levels, segments);
        Interlocked.Exchange(ref _segmentCount, segments.Count);
        if (segments.Count == 0)
        {
            return;
        }

        EnsureBuffer(device, segments.Count, BufferUsage.Vertex);
        var pointCount = segments.Count * PointsPerSegment;
        if (_points.Length < pointCount)
        {
            _points = new Vector2[pointCount];
        }

        for (var i = 0; i < segments.Count; i++)
        {
            _points[i * PointsPerSegment] = segments[i].Start;
            _points[(i * PointsPerSegment) + 1] = segments[i].End;
            _points[(i * PointsPerSegment) + 2] = new Vector2(PolylineStyle.Gap);
        }

        _segments!.Write<Vector2>(0, _points.AsSpan(0, pointCount));
    }

    private long EnsureBuffer(IRenderDevice device, long segments, BufferUsage usage)
    {
        var size = segments * IComputeOperations.ContourSegmentSize;
        if (_segments is null || _segments.SizeInBytes < size)
        {
            _segments?.Dispose();
            _segments = device.CreateBuffer(new BufferDesc(size, usage));
        }

        return _segments.SizeInBytes / IComputeOperations.ContourSegmentSize;
    }

    private void UpdateBands(IRenderDevice device, float[] levels)
    {
        var version = ColorAxis.Version + Volatile.Read(ref _layoutVersion) + Data.Version;
        if (_bandVersion != version)
        {
            _bands = _bandTexture.Update(device, CreateBandMap(levels));
            _bandVersion = version;
        }
    }

    // A map of flat bands. Each band has the color of its middle on the scale of the axis.
    private Colormap CreateBandMap(float[] levels)
    {
        var scaling = ColorAxis.Scaling;
        var range = ColorAxis.VisibleRange;
        var low = scaling?.Forward(range.Min) ?? range.Min;
        var high = scaling?.Forward(range.Max) ?? range.Max;
        var positions = new double[levels.Length + 2];
        positions[^1] = 1;
        for (var i = 0; i < levels.Length; i++)
        {
            var level = scaling?.Forward(levels[i]) ?? levels[i];
            positions[i + 1] = Math.Clamp((level - low) / (high - low), 0, 1);
        }

        var entries = new Rgba8[Colormap.EntryCount];
        var band = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            var position = i / (double)(entries.Length - 1);
            while (band + 2 < positions.Length && position >= positions[band + 1])
            {
                band++;
            }

            entries[i] = ColorAxis.Colormap.Sample((positions[band] + positions[band + 1]) / 2);
        }

        return Colormap.FromEntries("Bands", entries);
    }

    private void Touch() => Interlocked.Increment(ref _layoutVersion);
}
