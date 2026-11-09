using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A grid of values, shown as colors. Cells have equal sizes, or edges of their own.
/// </summary>
/// <remarks>
/// With equal cells on linear axes, the GPU picks the cells from a texture of all values, so that
/// moving the view uploads nothing. Other grids and axes are resampled to the pixels of the plot on
/// the CPU whenever the view or the data changes.
/// </remarks>
public sealed class HeatmapSeries : RasterSeries
{
    private readonly long _cellCount;

    private double _xStart;
    private double _xStep = 1;
    private double _yStart;
    private double _yStep = 1;
    private double[]? _xEdges;
    private double[]? _yEdges;
    private long _layoutVersion;

    private IGpuTexture? _valueTexture;
    private long _uploadedVersion = -1;
    private IGpuTexture? _pixelTexture;
    private float[] _pixels = [];
    private int[] _columns = [];
    private int[] _rows = [];
    private SeriesFrame _pixelFrame;
    private long _pixelVersion = -1;

    private float[] _values = [];
    private float[] _masked = [];
    private Polygon[]? _mask;
    private long _maskVersion;
    private long _valueVersion = -1;
    private IGpuTexture? _texture;
    private PixelRect _bounds;
    private Vector2 _sectionOrigin;
    private Vector2 _sectionSize;

    /// <summary>Gets or sets a value indicating whether adjacent cell colors are blended.</summary>
    public bool IsInterpolated { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeatmapSeries"/> class.
    /// </summary>
    /// <param name="data">The grid.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    /// <param name="colorAxis">The value scale.</param>
    public HeatmapSeries(HeatmapData data, AxisBase xAxis, AxisBase yAxis, ColorAxis colorAxis)
        : base(xAxis, yAxis, colorAxis)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        _cellCount = (long)data.Width * data.Height;
    }

    /// <summary>Gets the grid.</summary>
    public HeatmapData Data { get; }

    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart
    {
        get => _xStart;
        set => SetLayout(ref _xStart, value);
    }

    /// <summary>Gets or sets the width of a column.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double XStep
    {
        get => _xStep;
        set => SetStep(ref _xStep, value);
    }

    /// <summary>Gets or sets the lower edge of the first row.</summary>
    public double YStart
    {
        get => _yStart;
        set => SetLayout(ref _yStart, value);
    }

    /// <summary>Gets or sets the height of a row.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double YStep
    {
        get => _yStep;
        set => SetStep(ref _yStep, value);
    }

    /// <summary>Gets or sets the edges of the columns, one more than columns and rising, or <see langword="null"/> for equal columns.</summary>
    /// <exception cref="ArgumentException">The edges do not fit the grid or do not rise.</exception>
    public IReadOnlyList<double>? XEdges
    {
        get => _xEdges;
        set => SetEdges(ref _xEdges, value, Data.Width);
    }

    /// <summary>Gets or sets the edges of the rows, one more than rows and rising, or <see langword="null"/> for equal rows.</summary>
    /// <exception cref="ArgumentException">The edges do not fit the grid or do not rise.</exception>
    public IReadOnlyList<double>? YEdges
    {
        get => _yEdges;
        set => SetEdges(ref _yEdges, value, Data.Height);
    }

    /// <inheritdoc />
    protected internal override long Version => Data.Version + Volatile.Read(ref _layoutVersion);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => (int)Math.Min(_cellCount, int.MaxValue);

    /// <inheritdoc />
    protected internal override long SourcePointCount => _cellCount;

    private DoubleRange XRange => _xEdges is { } edges ? new DoubleRange(edges[0], edges[^1]) : new DoubleRange(_xStart, _xStart + (_xStep * Data.Width));

    private DoubleRange YRange => _yEdges is { } edges ? new DoubleRange(edges[0], edges[^1]) : new DoubleRange(_yStart, _yStart + (_yStep * Data.Height));

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

    /// <summary>
    /// Gets or sets polygons that cut the heatmap out: only cells whose middle lies inside them show, the rest
    /// stays clear, for example a measurement over the shape of a region. <see langword="null"/> shows all cells.
    /// Cells without a value inside the polygons stay clear as well.
    /// </summary>
    public IReadOnlyList<Polygon>? Mask
    {
        get => _mask;
        set
        {
            _mask = value is null ? null : [.. value];
            Interlocked.Increment(ref _maskVersion);
            Interlocked.Increment(ref _layoutVersion);
        }
    }

    /// <inheritdoc />
    protected internal override void TakeSnapshot()
    {
        // Data and mask together: a new mask needs a new upload as well.
        var wanted = (Data.Version * 1_000_003) + Interlocked.Read(ref _maskVersion);
        if (_valueVersion != wanted)
        {
            // Read in this order: a newer grid with the old version only leads to one more upload.
            _valueVersion = wanted;
            _values = _mask is { } mask ? Masked(Data.Values, mask) : Data.Values;
        }
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        base.Prepare(device, frame);
        var limit = device.Capabilities.MaxTextureDimension;
        if (_xEdges is null && _yEdges is null && frame.X.IsLinear && frame.Y.IsLinear && Data.Width <= limit && Data.Height <= limit)
        {
            PrepareTexture(device, frame);
        }
        else
        {
            PrepareResampled(device, frame);
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is not null && Table is not null && _bounds.Width > 0 && _bounds.Height > 0)
        {
            var style = ColorAxis.CreateRasterStyle() with
            {
                SectionOrigin = _sectionOrigin,
                SectionSize = _sectionSize,
                IsInterpolated = IsInterpolated,
            };
            if (_mask is not null)
            {
                style = style with { MissingColor = default };
            }

            commands.DrawRaster(_texture, Table, _bounds, style);
        }
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        index = -1;
        x = 0;
        y = 0;
        var column = FindCell(frame.X.ToData(pixel.X), _xEdges, _xStart, _xStep, Data.Width);
        var row = FindCell(frame.Y.ToData(pixel.Y), _yEdges, _yStart, _yStep, Data.Height);
        var values = _values;
        var cell = ((long)row * Data.Width) + column;
        if (column < 0 || row < 0 || float.IsNaN(Data[column, row]) || (_mask is not null && cell < values.Length && float.IsNaN(values[cell])))
        {
            return false;
        }

        index = ((long)row * Data.Width) + column;
        x = Center(column, _xEdges, _xStart, _xStep);
        y = Center(row, _yEdges, _yStart, _yStep);
        return true;
    }

    /// <inheritdoc />
    protected internal override void CollectValueLabels(DoubleRange x, DoubleRange y, List<ValueLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        // Every cell when few, otherwise an even grid of them, so that labels spread over the whole map.
        var (columns, rows) = (Data.Width, Data.Height);
        var step = (int)Math.Max(1, Math.Ceiling(Math.Sqrt((double)columns * rows / 600)));
        for (var row = step / 2; row < rows && labels.Count < Overlays.ValueLabelMarks.Limit; row += step)
        {
            for (var column = step / 2; column < columns; column += step)
            {
                var i = ((long)row * columns) + column;
                if (TryReadPoint(i, out var px, out var py) && x.Contains(px) && y.Contains(py) && TryReadValue(i, out var value))
                {
                    labels.Add(new ValueLabel(px, py, value, ValueLabelPlace.Center));
                }
            }
        }
    }

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y)
    {
        x = 0;
        y = 0;
        if (index < 0 || index >= _cellCount)
        {
            return false;
        }

        x = Center((int)(index % Data.Width), _xEdges, _xStart, _xStep);
        y = Center((int)(index / Data.Width), _yEdges, _yStart, _yStep);
        return true;
    }

    /// <inheritdoc />
    protected internal override bool TryReadValue(long index, out double value)
    {
        value = double.NaN;
        if (index < 0 || index >= _cellCount)
        {
            return false;
        }

        // The values as drawn: cells outside a mask are NaN there and read as missing.
        var values = _values.Length == _cellCount ? _values : Data.Values;
        value = values[index];
        return !double.IsNaN(value);
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        _valueTexture?.Dispose();
        _pixelTexture?.Dispose();
        _valueTexture = null;
        _pixelTexture = null;
        _texture = null;
        _uploadedVersion = -1;
        _pixelVersion = -1;
        _valueVersion = -1;
    }

    // A copy of the values with NaN outside the polygons; the array is kept for the next change.
    private float[] Masked(float[] values, Polygon[] mask)
    {
        if (_masked.Length != values.Length)
        {
            _masked = new float[values.Length];
        }

        values.CopyTo(_masked, 0);
        var columns = new double[Data.Width];
        var rows = new double[Data.Height];
        for (var i = 0; i < columns.Length; i++)
        {
            columns[i] = Center(i, _xEdges, _xStart, _xStep);
        }

        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = Center(i, _yEdges, _yStart, _yStep);
        }

        PolygonMask.Apply(_masked, columns, rows, mask);
        return _masked;
    }

    private static int FindCell(double value, double[]? edges, double start, double step, int count)
    {
        if (edges is null)
        {
            var cell = Math.Floor((value - start) / step);
            return cell >= 0 && cell < count ? (int)cell : -1;
        }

        if (!(value >= edges[0]) || value > edges[^1])
        {
            return -1;
        }

        var found = Array.BinarySearch(edges, value);
        return Math.Min(found >= 0 ? found : ~found - 1, count - 1);
    }

    private static double Center(int cell, double[]? edges, double start, double step) =>
        edges is null ? start + ((cell + 0.5) * step) : (edges[cell] + edges[cell + 1]) / 2;

    private void PrepareTexture(IRenderDevice device, in SeriesFrame frame)
    {
        if (_valueTexture is null)
        {
            _valueTexture = device.CreateTexture(new TextureDesc(new PixelSize(Data.Width, Data.Height), TextureFormat.R32Float));
            _uploadedVersion = -1;
        }

        if (_uploadedVersion != _valueVersion)
        {
            _valueTexture.Write(MemoryMarshal.AsBytes(_values.AsSpan()));
            _uploadedVersion = _valueVersion;
        }

        _texture = _valueTexture;
        (_bounds, _sectionOrigin, _sectionSize) = Place(frame, XRange, YRange, Vector2.One);
    }

    private void PrepareResampled(IRenderDevice device, in SeriesFrame frame)
    {
        var clipped = Clip(frame, XRange, YRange);
        var firstColumn = (int)MathF.Floor(clipped.X);
        var firstRow = (int)MathF.Floor(clipped.Y);
        var columns = (int)MathF.Ceiling(clipped.X + clipped.Width) - firstColumn;
        var rows = (int)MathF.Ceiling(clipped.Y + clipped.Height) - firstRow;
        _bounds = new PixelRect(firstColumn, firstRow, columns, rows);
        _sectionOrigin = Vector2.Zero;
        _sectionSize = Vector2.One;
        if (clipped.Width <= 0 || clipped.Height <= 0 || columns <= 0 || rows <= 0)
        {
            _texture = null;
            return;
        }

        if (_pixelTexture is null || _pixelTexture.Size != new PixelSize(columns, rows))
        {
            _pixelTexture?.Dispose();
            _pixelTexture = device.CreateTexture(new TextureDesc(new PixelSize(columns, rows), TextureFormat.R32Float));
            _pixelVersion = -1;
        }

        _texture = _pixelTexture;
        var version = _valueVersion + Volatile.Read(ref _layoutVersion);
        if (_pixelVersion == version && _pixelFrame == frame)
        {
            return;
        }

        Resample(frame, firstColumn, firstRow, columns, rows);
        _pixelTexture.Write(MemoryMarshal.AsBytes(_pixels.AsSpan()));
        _pixelVersion = version;
        _pixelFrame = frame;
    }

    private void Resample(in SeriesFrame frame, int firstColumn, int firstRow, int columns, int rows)
    {
        if (_pixels.Length != columns * rows)
        {
            _pixels = new float[columns * rows];
        }

        if (_columns.Length != columns)
        {
            _columns = new int[columns];
        }

        if (_rows.Length != rows)
        {
            _rows = new int[rows];
        }

        for (var i = 0; i < columns; i++)
        {
            _columns[i] = FindCell(frame.X.ToData(firstColumn + i + 0.5), _xEdges, _xStart, _xStep, Data.Width);
        }

        for (var i = 0; i < rows; i++)
        {
            _rows[i] = FindCell(frame.Y.ToData(firstRow + i + 0.5), _yEdges, _yStart, _yStep, Data.Height);
        }

        var values = _values.AsSpan();
        for (var y = 0; y < rows; y++)
        {
            var output = _pixels.AsSpan(y * columns, columns);
            if (_rows[y] < 0)
            {
                output.Fill(float.NaN);
                continue;
            }

            var line = values.Slice(_rows[y] * Data.Width, Data.Width);
            for (var x = 0; x < columns; x++)
            {
                output[x] = _columns[x] < 0 ? float.NaN : line[_columns[x]];
            }
        }
    }

    private void SetLayout(ref double field, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be finite.");
        }

        field = value;
        Interlocked.Increment(ref _layoutVersion);
    }

    private void SetStep(ref double field, double value)
    {
        if (!(value > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The step must be positive.");
        }

        SetLayout(ref field, value);
    }

    private void SetEdges(ref double[]? field, IReadOnlyList<double>? value, int count)
    {
        if (value is not null)
        {
            if (value.Count != count + 1)
            {
                throw new ArgumentException($"{count} cells need {count + 1} edges.", nameof(value));
            }

            for (var i = 0; i < value.Count; i++)
            {
                if (!double.IsFinite(value[i]) || (i > 0 && value[i] <= value[i - 1]))
                {
                    throw new ArgumentException("The edges must be finite and rise.", nameof(value));
                }
            }
        }

        field = value?.ToArray();
        Interlocked.Increment(ref _layoutVersion);
    }
}
