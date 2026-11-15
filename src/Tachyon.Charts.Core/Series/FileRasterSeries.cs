using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A heatmap or spectrogram of a raster file far larger than memory. Each frame reads the stage of the
/// <see cref="RasterPyramid"/> with about one cell per pixel, and of it only the cells in view: a grid of 64k × 64k pans
/// as fast as one of 2k × 2k.
/// </summary>
public sealed class FileRasterSeries : ChartSeries, IDisposable, Overlays.IRefiningSeries
{
    // Down to this many cells per pixel a stage is fine enough: a cell is then at most two pixels wide.
    private const double MinimumCellsPerPixel = 0.5;

    private readonly RasterPyramid _pyramid;
    private readonly ColorAxis _colorAxis;
    private readonly ColormapTexture _table = new();
    private readonly Action<int> _fillBand;
    private IGpuTexture? _texture;
    private IGpuTexture? _colors;
    private IRenderDevice? _device;
    private float[] _values = [];
    private int[] _pixelColumns = [];
    private long[] _rowsOfPixels = [];
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private long _appearanceVersion;
    private bool _rowsAlongX;
    private (int Level, int Width, int Height, int Bands) _job;

    /// <summary>Starts a series.</summary>
    /// <param name="pyramid">The raster and its stages.</param>
    /// <param name="xAxis">The X axis.</param>
    /// <param name="yAxis">The Y axis.</param>
    /// <param name="colorAxis">The scale of the values.</param>
    public FileRasterSeries(RasterPyramid pyramid, AxisBase xAxis, AxisBase yAxis, ColorAxis colorAxis)
        : base(xAxis, yAxis)
    {
        _pyramid = pyramid ?? throw new ArgumentNullException(nameof(pyramid));
        _colorAxis = colorAxis ?? throw new ArgumentNullException(nameof(colorAxis));
        _fillBand = FillBand;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the rows of the file run along the X axis, as the rows of a spectrogram
    /// written over time do: a row is then a column of the picture.
    /// </summary>
    public bool RowsAlongX
    {
        get => _rowsAlongX;
        set
        {
            _rowsAlongX = value;
            Interlocked.Increment(ref _appearanceVersion);
        }
    }

    /// <summary>Gets the stage of the last frame; 0 is the raster itself.</summary>
    public int Level => _job.Level;

    /// <inheritdoc />
    public double Progress => _pyramid.Progress;

    /// <inheritdoc />
    public override ColorAxis? ColorAxis => _colorAxis;

    /// <summary>Gets the values of the last frame, row by row from the top, for tests.</summary>
    internal ReadOnlySpan<float> Values => _values;

    /// <inheritdoc />
    protected internal override long Version => (_pyramid.IsComplete ? 1L << 40 : (long)(_pyramid.Progress * 1000)) + Volatile.Read(ref _appearanceVersion);

    /// <inheritdoc />
    protected internal override long SourcePointCount => _pyramid.Source.Width * _pyramid.Source.Height;

    /// <inheritdoc />
    public void Dispose()
    {
        _table.Dispose();
        _texture?.Dispose();
        (_texture, _colors, _device) = (null, null, null);
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources()
    {
        Dispose();
        _builtVersion = -1;
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var source = _pyramid.Source;
        var columns = new DoubleRange(source.XStart, source.XStart + (source.Width * source.XStep));
        var rows = new DoubleRange(source.YStart, source.YStart + (source.Height * source.YStep));
        (x, y) = _rowsAlongX ? (rows, columns) : (columns, rows);
        return source.Height > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryGetValueRange(out DoubleRange range)
    {
        range = _pyramid.Range;
        return _pyramid.IsComplete && range.Min <= range.Max;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        if (_device != device)
        {
            _texture?.Dispose();
            (_texture, _device) = (null, device);
        }

        var width = Math.Max((int)frame.Bounds.Width, 1);
        var height = Math.Max((int)frame.Bounds.Height, 1);
        var size = new PixelSize(width, height);
        if (_texture is null || _texture.Size != size)
        {
            _texture?.Dispose();
            _texture = device.CreateTexture(new TextureDesc(size, TextureFormat.R32Float));
            _builtVersion = -1;
        }

        _colors = _table.Update(device, _colorAxis.Colormap);
        var version = Version;
        if (version == _builtVersion && frame == _builtFrame)
        {
            return;
        }

        (_builtVersion, _builtFrame) = (version, frame);
        if (_values.Length != width * height)
        {
            _values = new float[width * height];
        }

        if (_pixelColumns.Length != width)
        {
            _pixelColumns = new int[width];
        }

        if (_rowsOfPixels.Length != height)
        {
            _rowsOfPixels = new long[height];
        }

        var level = ChooseLevel(frame, width, height);
        if (level < 0)
        {
            Array.Fill(_values, float.NaN);
        }
        else
        {
            Map(frame, level, width, height);
            _job = (level, width, height, Math.Clamp(Environment.ProcessorCount, 1, 8));
            Parallel.For(0, _job.Bands, _fillBand);
        }

        _texture.Write(MemoryMarshal.AsBytes(_values.AsSpan()));
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is not null && _colors is not null)
        {
            commands.DrawRaster(_texture, _colors, frame.Bounds, _colorAxis.CreateRasterStyle());
        }
    }

    // The coarsest stage that still has half a cell or more per pixel in both directions (a cell at most two pixels wide); −1 while the stages are not built
    // and the view would need more than the raster itself can give quickly.
    private int ChooseLevel(in SeriesFrame frame, int width, int height)
    {
        var source = _pyramid.Source;
        var (columnsInView, rowsInView) = CellsInView(frame, source);
        var (columnPixels, rowPixels) = _rowsAlongX ? (height, width) : (width, height);
        if (!_pyramid.IsComplete)
        {
            // The raster itself only for narrow views: few rows, read where they are.
            return rowsInView <= 4 * rowPixels ? 0 : -1;
        }

        for (var level = _pyramid.LevelCount - 1; level > 0; level--)
        {
            var (levelWidth, levelHeight) = _pyramid.Size(level);
            // A direction is fine enough with half a cell per pixel, or when the stage is as fine there as the raster.
            var columnsFine = levelWidth == source.Width || columnsInView * levelWidth / source.Width >= columnPixels * MinimumCellsPerPixel;
            var rowsFine = levelHeight == source.Height || rowsInView * levelHeight / source.Height >= rowPixels * MinimumCellsPerPixel;
            if (columnsFine && rowsFine)
            {
                return level;
            }
        }

        return 0;
    }

    private (double Columns, double Rows) CellsInView(in SeriesFrame frame, RasterFile source)
    {
        var horizontal = Math.Abs(frame.X.ToData(frame.Bounds.X + frame.Bounds.Width) - frame.X.ToData(frame.Bounds.X));
        var vertical = Math.Abs(frame.Y.ToData(frame.Bounds.Y) - frame.Y.ToData(frame.Bounds.Y + frame.Bounds.Height));
        return _rowsAlongX
            ? (vertical / Math.Abs(source.XStep), horizontal / Math.Abs(source.YStep))
            : (horizontal / Math.Abs(source.XStep), vertical / Math.Abs(source.YStep));
    }

    // For each pixel column and row, the cell of the stage under its middle, or −1 outside the raster.
    private void Map(in SeriesFrame frame, int level, int width, int height)
    {
        var source = _pyramid.Source;
        var (levelWidth, levelHeight) = _pyramid.Size(level);
        var origin = XAxis is DateTimeAxis time ? time.OriginTicks : 0;
        for (var pixel = 0; pixel < width; pixel++)
        {
            var value = frame.X.ToData(frame.Bounds.X + pixel + 0.5) + origin;
            _pixelColumns[pixel] = _rowsAlongX
                ? (int)Math.Clamp(Cell(value, source.YStart, source.YStep, source.Height, levelHeight), -1, int.MaxValue)
                : (int)Cell(value, source.XStart, source.XStep, source.Width, levelWidth);
        }

        for (var pixel = 0; pixel < height; pixel++)
        {
            var value = frame.Y.ToData(frame.Bounds.Y + pixel + 0.5);
            var cell = _rowsAlongX
                ? Cell(value, source.XStart, source.XStep, source.Width, levelWidth)
                : Cell(value, source.YStart, source.YStep, source.Height, levelHeight);
            _rowsOfPixels[pixel] = cell;
        }
    }

    // The cell of a stage at a value: the cell of the raster, scaled to the size of the stage.
    private static long Cell(double value, double start, double step, long cells, long levelCells)
    {
        var cell = Math.Floor((value - start) / step);
        if (!(cell >= 0 && cell < cells))
        {
            return -1;
        }

        return (long)(cell * levelCells / cells);
    }

    // A band of pixel rows, each reading one row of the stage; with rows along X a band of pixel columns instead, each
    // reading one row of the stage from top to bottom, so that every row of the file is read in one piece.
    private void FillBand(int band)
    {
        var (level, width, height, bands) = _job;
        if (_rowsAlongX)
        {
            for (var pixelX = width * band / bands; pixelX < width * (band + 1) / bands; pixelX++)
            {
                var row = _pixelColumns[pixelX];
                var values = row < 0 ? default : _pyramid.Row(level, row);
                for (var pixelY = 0; pixelY < height; pixelY++)
                {
                    var cell = _rowsOfPixels[pixelY];
                    _values[(pixelY * width) + pixelX] = row < 0 || cell < 0 ? float.NaN : values[(int)cell];
                }
            }

            return;
        }

        for (var pixelY = height * band / bands; pixelY < height * (band + 1) / bands; pixelY++)
        {
            var target = _values.AsSpan(pixelY * width, width);
            var cell = _rowsOfPixels[pixelY];
            if (cell < 0)
            {
                target.Fill(float.NaN);
                continue;
            }

            var values = _pyramid.Row(level, cell);
            for (var pixelX = 0; pixelX < width; pixelX++)
            {
                var column = _pixelColumns[pixelX];
                target[pixelX] = column < 0 ? float.NaN : values[column];
            }
        }
    }
}
