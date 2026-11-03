using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A waterfall of rows: columns run along the X axis, time along the Y axis. A new row reaches the GPU
/// as a single row of a ring texture, and the shader rotates the rows, so nothing else moves in memory.
/// </summary>
public sealed class SpectrogramSeries : RasterSeries
{
    private readonly long _capacity;

    private double _xStart;
    private double _xStep = 1;
    private double _yStart;
    private double _yStep = 1;
    private bool _isAnchoredToNewest = true;
    private long _layoutVersion;
    private IGpuTexture? _texture;
    private long _uploaded = -1;
    private long _uploadedRows;
    private PixelRect _bounds;
    private Vector2 _sectionOrigin;
    private Vector2 _sectionSize;
    private RasterReduction _reduction = RasterReduction.Maximum;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrogramSeries"/> class.
    /// </summary>
    /// <param name="data">The rows.</param>
    /// <param name="xAxis">The horizontal axis, linear.</param>
    /// <param name="yAxis">The vertical axis, linear.</param>
    /// <param name="colorAxis">The value scale.</param>
    /// <exception cref="ArgumentException">An axis is not linear: the texture is drawn as it is.</exception>
    public SpectrogramSeries(SpectrogramData data, AxisBase xAxis, AxisBase yAxis, ColorAxis colorAxis)
        : base(xAxis, yAxis, colorAxis)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        if (XAxis.Scaling is not null || YAxis.Scaling is not null)
        {
            throw new ArgumentException("A spectrogram needs linear axes.", nameof(xAxis));
        }

        _capacity = (long)data.Columns * data.Rows;
    }

    /// <summary>Gets the rows.</summary>
    public SpectrogramData Data { get; }

    /// <summary>Gets the number of rows uploaded so far, a diagnostic counter.</summary>
    public long UploadedRowCount => Interlocked.Read(ref _uploadedRows);

    /// <summary>Gets or sets what a pixel shows when several cells fall on it. The default keeps narrow lines visible.</summary>
    public RasterReduction Reduction
    {
        get => _reduction;
        set
        {
            _reduction = value;
            Interlocked.Increment(ref _layoutVersion);
        }
    }

    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart
    {
        get => _xStart;
        set => SetLayout(ref _xStart, value);
    }

    /// <summary>Gets or sets the width of a column.</summary>
    public double XStep
    {
        get => _xStep;
        set => SetLayout(ref _xStep, value);
    }

    /// <summary>Gets or sets the lower edge of the first row, unless the rows are anchored to the newest.</summary>
    public double YStart
    {
        get => _yStart;
        set => SetLayout(ref _yStart, value);
    }

    /// <summary>Gets or sets the height of a row.</summary>
    public double YStep
    {
        get => _yStep;
        set => SetLayout(ref _yStep, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether Y counts back from the newest row, which ends at zero.
    /// The axis then stays still while the rows pass through it, which is the default. Otherwise a
    /// row keeps its Y value, and the axis has to move along.
    /// </summary>
    public bool IsAnchoredToNewest
    {
        get => _isAnchoredToNewest;
        set
        {
            _isAnchoredToNewest = value;
            Interlocked.Increment(ref _layoutVersion);
        }
    }

    /// <inheritdoc />
    protected internal override long Version => Data.TotalRows + Volatile.Read(ref _layoutVersion);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => (int)Math.Min(_capacity, int.MaxValue);

    /// <inheritdoc />
    protected internal override long SourcePointCount => _capacity;

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var total = Data.TotalRows;
        x = new DoubleRange(_xStart, _xStart + (_xStep * Data.Columns));
        y = RowRange(total);
        return total > 0;
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
        _texture ??= device.CreateTexture(new TextureDesc(new PixelSize(Data.Columns, Data.Rows), TextureFormat.R32Float));
        var total = Data.Upload(_texture, _uploaded, ref _uploadedRows);
        _uploaded = total;

        var shown = Math.Min(total, Data.Rows);
        if (shown == 0)
        {
            _bounds = default;
            return;
        }

        var x = new DoubleRange(_xStart, _xStart + (_xStep * Data.Columns));
        (_bounds, _sectionOrigin, _sectionSize) = Place(frame, x, RowRange(total), new Vector2(1, shown / (float)Data.Rows));
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is null || Table is null || _bounds.Width <= 0 || _bounds.Height <= 0)
        {
            return;
        }

        var style = ColorAxis.CreateRasterStyle() with
        {
            SectionOrigin = _sectionOrigin,
            SectionSize = _sectionSize,
            Reduction = _reduction,

            // The oldest row is the next to be replaced.
            RowShift = _uploaded >= Data.Rows ? (int)(_uploaded % Data.Rows) : 0,
        };
        commands.DrawRaster(_texture, Table, _bounds, style);
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        index = -1;
        x = 0;
        y = 0;
        var total = Data.TotalRows;
        var start = RowRange(total).Min - (Math.Max(total - Data.Rows, 0) * _yStep);
        var column = Math.Floor((frame.X.ToData(pixel.X) - _xStart) / _xStep);
        var row = Math.Floor((frame.Y.ToData(pixel.Y) - start) / _yStep);
        if (!(column >= 0 && column < Data.Columns) || !(row >= 0 && row < long.MaxValue / Data.Columns)
            || !Data.TryRead((long)row, (int)column, out var value) || float.IsNaN(value))
        {
            return false;
        }

        index = ((long)row * Data.Columns) + (long)column;
        x = _xStart + ((column + 0.5) * _xStep);
        y = start + ((row + 0.5) * _yStep);
        return true;
    }

    /// <inheritdoc />
    protected internal override bool TryReadValue(long index, out double value)
    {
        var found = Data.TryRead(index / Data.Columns, (int)(index % Data.Columns), out var read);
        value = read;
        return found && !float.IsNaN(read);
    }

    /// <inheritdoc />
    protected override void DisposeCore()
    {
        _texture?.Dispose();
        _texture = null;
        _uploaded = -1;
    }

    // The range of the rows kept, the newest one last.
    private DoubleRange RowRange(long total)
    {
        var end = _isAnchoredToNewest ? 0 : _yStart + (total * _yStep);
        return new DoubleRange(end - (Math.Min(total, Data.Rows) * _yStep), end);
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
}
