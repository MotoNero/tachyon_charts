using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Resampling;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A line over a large column, such as a file of billions of values: each pixel column draws the lowest and the highest
/// value of its range from the pyramid of the column, so no outlier is lost and a frame reads only a few blocks. Narrow
/// ranges draw the values themselves.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks>Until the pyramid is complete, wide ranges stay empty and narrow ones read the column.</remarks>
public sealed class ColumnLineSeries<T> : ChartSeries, IDisposable
    where T : unmanaged, INumber<T>, IMinMaxValue<T>
{
    // Without the pyramid a frame reads at most this many values.
    private const long DirectLimit = 1 << 22;

    // From this many values a pixel column is drawn from whole blocks of the pyramid.
    private const long WholeBlocks = 16L * ColumnPyramid<double>.BlockSize;

    private readonly IColumnSource<T> _source;
    private readonly ColumnPyramid<T> _pyramid;
    private Vector2[] _vertices = new Vector2[4096];
    private IGpuBuffer? _buffer;
    private int _count;
    private int _runLength;
    private long _appearanceVersion;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private Rgba8 _color = new(80, 170, 240, 255);
    private double _thickness = 1.5;

    /// <summary>Initializes a new instance of the <see cref="ColumnLineSeries{T}"/> class.</summary>
    /// <param name="source">The column.</param>
    /// <param name="pyramid">Its pyramid, see <see cref="ColumnPyramid{T}.Build"/>.</param>
    /// <param name="xAxis">The X axis.</param>
    /// <param name="yAxis">The Y axis.</param>
    public ColumnLineSeries(IColumnSource<T> source, ColumnPyramid<T> pyramid, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _pyramid = pyramid ?? throw new ArgumentNullException(nameof(pyramid));
    }

    /// <summary>Gets or sets the color.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Interlocked.Increment(ref _appearanceVersion);
        }
    }

    /// <summary>Gets or sets the width in device-independent units.</summary>
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
            Interlocked.Increment(ref _appearanceVersion);
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    protected internal override long Version => _source.Version + Volatile.Read(ref _appearanceVersion) + (_pyramid.IsComplete ? 1L << 40 : 0);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _count;

    /// <inheritdoc />
    protected internal override long SourcePointCount => _source.Count;

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
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
        var origin = Origin();
        x = _source.Count == 0 ? DoubleRange.Empty : new DoubleRange(_source.XStart - origin, _source.XStart + ((_source.Count - 1) * _source.XStep) - origin);
        var total = _pyramid.Total;
        y = total.Min > total.Max ? DoubleRange.Empty : new DoubleRange(double.CreateTruncating(total.Min), double.CreateTruncating(total.Max));
        return _source.Count > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y)
    {
        var values = _source.Read(index, 1);
        x = _source.XStart + (index * _source.XStep) - Origin();
        y = values.IsEmpty ? double.NaN : double.CreateTruncating(values[0]);
        return !values.IsEmpty;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _builtVersion && frame == _builtFrame)
        {
            return;
        }

        (_builtVersion, _builtFrame, _count, _runLength) = (version, frame, 0, 0);
        var count = _source.Count;
        if (count > 0 && _source.XStep > 0)
        {
            // The indices in view, one more on each side so that the line leaves the plot area.
            var origin = Origin();
            var visible = XAxis.VisibleRange;
            var first = Math.Clamp((long)Math.Floor((visible.Min + origin - _source.XStart) / _source.XStep) - 1, 0, count - 1);
            var last = Math.Clamp((long)Math.Ceiling((visible.Max + origin - _source.XStart) / _source.XStep) + 2, first + 1, count);
            var columns = Math.Max((int)MathF.Ceiling(frame.Bounds.Width), 1);
            if (last - first <= columns * 2L)
            {
                AddValues(frame, first, last, origin);
            }
            else if (_pyramid.IsComplete || last - first <= DirectLimit)
            {
                AddColumns(frame, first, last, columns);
            }
        }

        EndRun();
        if (_count > 0)
        {
            if (_buffer is null || _buffer.SizeInBytes < _count * 8L)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(Math.Max(_count * 8L, 4096), BufferUsage.Vertex));
            }

            _buffer.Write<Vector2>(0, _vertices.AsSpan(0, _count));
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_count >= 2 && _buffer is not null)
        {
            var thickness = IsHighlighted ? _thickness + 1.5 : _thickness;
            commands.DrawPolyline(_buffer, 0, _count, new PolylineStyle((float)(thickness * frame.DpiScale), _color) { Clip = frame.Bounds });
        }
    }

    private double Origin() => XAxis is DateTimeAxis time ? time.OriginTicks : 0;

    private void AddValues(in SeriesFrame frame, long first, long last, double origin)
    {
        for (var at = first; at < last;)
        {
            var values = _source.Read(at, (int)(last - at));
            for (var i = 0; i < values.Length; i++)
            {
                var value = double.CreateTruncating(values[i]);
                var y = frame.Y.ToPixel(value);
                if (!double.IsFinite(value) || !double.IsFinite(y))
                {
                    AddGap();
                    continue;
                }

                AddVertex(new Vector2((float)frame.X.ToPixel(_source.XStart + ((at + i) * _source.XStep) - origin), (float)y));
            }

            at += values.Length;
        }
    }

    // Each pixel column holds its lowest and highest value, the one nearer to where the line comes from first.
    private void AddColumns(in SeriesFrame frame, long first, long last, int columns)
    {
        var span = last - first;
        var previous = float.NaN;
        var origin = Origin();
        for (var column = 0; column < columns; column++)
        {
            var start = first + (span * column / columns);
            var end = first + (span * (column + 1) / columns);
            // A column of many blocks takes whole blocks: no value of the file is read, which keeps panning smooth when
            // the file is far larger than the cache; the outer blocks shift an extreme by less than a pixel.
            var extremes = end - start >= WholeBlocks ? _pyramid.QueryBlocks(start, end) : _pyramid.Query(start, end);
            if (extremes.Min > extremes.Max)
            {
                AddGap();
                previous = float.NaN;
                continue;
            }

            var low = (float)frame.Y.ToPixel(double.CreateTruncating(extremes.Min));
            var high = (float)frame.Y.ToPixel(double.CreateTruncating(extremes.Max));
            // The middle of the range on the axis, so that inverted and turned axes place it too; the pair spreads over the column.
            var x = (float)frame.X.ToPixel(_source.XStart + ((start + end) * 0.5 * _source.XStep) - origin);
            var (near, far) = float.IsNaN(previous) || Math.Abs(previous - low) <= Math.Abs(previous - high) ? (low, high) : (high, low);
            AddVertex(new Vector2(x - 0.25f, near));
            AddVertex(new Vector2(x + 0.25f, far));
            previous = far;
        }
    }

    private void AddVertex(Vector2 vertex)
    {
        if (_count == _vertices.Length)
        {
            Array.Resize(ref _vertices, _count * 2);
        }

        _vertices[_count++] = vertex;
        _runLength++;
    }

    private void AddGap()
    {
        if (_runLength > 0)
        {
            EndRun();
            AddVertex(new Vector2(PolylineStyle.Gap, 0));
            _runLength = 0;
        }
    }

    // A point between two gaps has no segment. A segment of no length shows it as a dot.
    private void EndRun()
    {
        if (_runLength == 1)
        {
            AddVertex(_vertices[_count - 1]);
        }
    }
}
