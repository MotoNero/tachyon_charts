using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// One channel of a <see cref="SweepData"/> on a fixed axis from 0 to the window: the trace writes over the previous
/// one from the left and leaves a gap ahead of itself, or, in stepping mode, fills one page after the other.
/// Each pixel column shows the lowest and highest value of its samples, so hundreds of thousands of samples per
/// second draw at the cost of the screen width. Series of the same data run in step, also in different panes.
/// </summary>
public sealed class SweepSeries : ChartSeries, IDisposable
{
    private const int MostColumns = 8192;

    private readonly SweepData _data;
    private readonly int _channel;
    private readonly List<Vector2> _points = [];
    private float[] _low = new float[256];
    private float[] _high = new float[256];
    private IGpuBuffer? _buffer;
    private Rgba8 _color = new(6, 214, 160, 255);
    private double _thickness = 1.5;
    private long _appearance;
    private long _built = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="SweepSeries"/> class.</summary>
    /// <param name="data">The samples.</param>
    /// <param name="channel">The channel this series shows.</param>
    /// <param name="xAxis">The axis of the window, from 0 to <see cref="SweepData.Window"/>.</param>
    /// <param name="yAxis">The axis of the values.</param>
    /// <exception cref="ArgumentOutOfRangeException">The channel does not exist.</exception>
    public SweepSeries(SweepData data, int channel, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        ArgumentOutOfRangeException.ThrowIfNegative(channel);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, data.Channels);
        _channel = channel;
    }

    /// <summary>Gets the samples.</summary>
    public SweepData Data => _data;

    /// <summary>Gets the channel this series shows.</summary>
    public int Channel => _channel;

    /// <summary>Gets or sets the color of the trace.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Interlocked.Increment(ref _appearance);
        }
    }

    /// <summary>Gets or sets the line thickness in device-independent units.</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            _thickness = value;
            Interlocked.Increment(ref _appearance);
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    protected internal override long Version => _data.Version + Interlocked.Read(ref _appearance);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _points.Count;

    internal ReadOnlySpan<Vector2> Points => CollectionsMarshal.AsSpan(_points);

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _built = -1;
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources() => Dispose();

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette) => Color = palette[_channel % palette.Length];

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = new DoubleRange(0, _data.Window);
        y = new DoubleRange(-1, 1);
        return false;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        Build(frame);
        if (_points.Count >= 2)
        {
            var size = _points.Count * 8L;
            if (_buffer is null || _buffer.SizeInBytes < size)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(Math.Max(size, 4096), BufferUsage.Vertex));
            }

            _buffer.Write<Vector2>(0, CollectionsMarshal.AsSpan(_points));
        }

        _built = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_buffer is not null && _points.Count >= 2)
        {
            commands.DrawPolyline(_buffer, 0, _points.Count, new PolylineStyle((float)(_thickness * frame.DpiScale), _color) { Clip = frame.Bounds });
        }
    }

    // Each column of the window: its lowest and highest value, both on the column, so steep edges stay whole. The
    // extreme nearer to where the line comes from goes first; always low before high would draw a saw tooth on falling
    // edges, up and back down in every column. Both extremes spread over the width of the column, the first at its left
    // and the second at its right edge: a steady edge then becomes one slanted line instead of a staircase of upright
    // and flat pieces.
    private void Build(in SeriesFrame frame)
    {
        _points.Clear();
        var bounds = frame.Bounds;
        var columns = Math.Clamp((int)bounds.Width, 2, MostColumns);
        if (_low.Length < columns)
        {
            _low = new float[columns];
            _high = new float[columns];
        }

        if (_data.Collect(_channel, columns, _low, _high) < 0)
        {
            return;
        }

        var window = _data.Window;
        var isOpen = false;
        var last = 0f;
        for (var column = 0; column < columns; column++)
        {
            if (float.IsNaN(_low[column]))
            {
                if (isOpen)
                {
                    _points.Add(new Vector2(PolylineStyle.Gap));
                    isOpen = false;
                }

                continue;
            }

            var x = (float)frame.X.ToPixel((column + 0.5) * window / columns);
            var (low, high) = ((float)frame.Y.ToPixel(_low[column]), (float)frame.Y.ToPixel(_high[column]));
            var (first, second) = isOpen && MathF.Abs(last - high) < MathF.Abs(last - low) ? (high, low) : (low, high);
            if (second == first)
            {
                _points.Add(new Vector2(x, first));
            }
            else
            {
                var half = (float)(frame.X.ToPixel((column + 1.0) * window / columns) - frame.X.ToPixel((double)column * window / columns)) / 2;
                _points.Add(new Vector2(x - half, first));
                _points.Add(new Vector2(x + half, second));
            }

            last = second;
            isOpen = true;
        }
    }
}

/// <summary>Describes monitor displays.</summary>
public static class SweepCharts
{
    /// <summary>
    /// Describes a monitor display: an axis from 0 to the window and one pane per channel, each with its own value
    /// range, all running in step.
    /// </summary>
    /// <param name="builder">The description.</param>
    /// <param name="data">The samples.</param>
    /// <param name="ranges">The value range of each channel, from the top pane down.</param>
    /// <param name="titles">The titles of the value axes, or <see langword="null"/>.</param>
    /// <returns>The description.</returns>
    /// <exception cref="ArgumentException">The number of ranges differs from the channels.</exception>
    public static ChartBuilder Sweep(this ChartBuilder builder, SweepData data, IReadOnlyList<DoubleRange> ranges, IReadOnlyList<string>? titles = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(ranges);
        if (ranges.Count != data.Channels)
        {
            throw new ArgumentException("Every channel needs a range.", nameof(ranges));
        }

        builder.XAxis(a => a.Numeric("sweep").Range(0, data.Window));
        for (var channel = 0; channel < data.Channels; channel++)
        {
            var index = channel;
            var id = "channel" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            builder.YAxis(a =>
            {
                a.Numeric(id).Range(ranges[index].Min, ranges[index].Max).Pane(index);
                if (titles is not null && index < titles.Count)
                {
                    a.Title(titles[index]);
                }
            });
            builder.AddSeries((x, y) => new SweepSeries(data, index, x, y), yAxis: id);
        }

        return builder;
    }
}
