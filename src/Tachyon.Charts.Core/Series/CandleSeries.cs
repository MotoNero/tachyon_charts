using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>How a price period is drawn.</summary>
public enum CandleStyle
{
    /// <summary>A body from open to close with a wick from low to high.</summary>
    Candles,

    /// <summary>A bar from low to high with a tick for the open on the left and one for the close on the right.</summary>
    Ohlc,
}

/// <summary>The look of rising or of falling periods.</summary>
/// <param name="Body">The fill of the body.</param>
/// <param name="Line">The color of the wick, the outline and the OHLC bar.</param>
/// <param name="IsHollow">Whether the body is only outlined.</param>
public readonly record struct CandleLook(Rgba8 Body, Rgba8 Line, bool IsHollow = false);

/// <summary>
/// Candles or OHLC bars from <see cref="CandleData"/> over a time axis. When more periods share a pixel column than
/// fit, the column shows them as one: the first open, the highest high, the lowest low and the last close, so a
/// million candles draw as fast as a few thousand.
/// </summary>
public sealed class CandleSeries : ChartSeries
{
    private readonly CandleData _data;
    private readonly List<MeshVertex> _mesh = [];
    private readonly List<CandleColumns.Column> _columns = [];
    private CandleStyle _style;
    private CandleLook _rising = new(new Rgba8(38, 166, 154, 255), new Rgba8(38, 166, 154, 255));
    private CandleLook _falling = new(new Rgba8(239, 83, 80, 255), new Rgba8(239, 83, 80, 255));
    private double _bodyWidth = 0.7;
    private int _hovered = -1;
    private long _appearance;
    private long _built = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="CandleSeries"/> class.</summary>
    /// <param name="data">The candles.</param>
    /// <param name="xAxis">The time axis.</param>
    /// <param name="yAxis">The price axis.</param>
    public CandleSeries(CandleData data, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis) => _data = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets the candles.</summary>
    public CandleData Data => _data;

    /// <summary>Gets or sets how a period is drawn.</summary>
    public CandleStyle Style
    {
        get => _style;
        set
        {
            _style = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the look of periods that closed at or above their open.</summary>
    public CandleLook Rising
    {
        get => _rising;
        set
        {
            _rising = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the look of periods that closed below their open.</summary>
    public CandleLook Falling
    {
        get => _falling;
        set
        {
            _falling = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the width of a body as a share of the room of one period.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not above 0 and at most 1.</exception>
    public double BodyWidth
    {
        get => _bodyWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1);
            _bodyWidth = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the candle under the pointer, or -1 for none. <see cref="CandleModifier"/> sets it.</summary>
    public int HoveredIndex
    {
        get => Volatile.Read(ref _hovered);
        set
        {
            if (Interlocked.Exchange(ref _hovered, value) != value)
            {
                Touch();
            }
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _rising.Line;

    /// <inheritdoc />
    protected internal override long Version => _data.Version + Interlocked.Read(ref _appearance);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _columns.Count;

    internal int VertexCount => _mesh.Count;

    /// <inheritdoc />
    protected internal override long SourcePointCount => _data.Count;

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var state = _data.Current;
        y = state.PriceRange;
        x = CandleColumns.TimeRange(state, CandleColumns.OriginOf(XAxis));
        return state.Count > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        var state = _data.Current;
        var origin = CandleColumns.OriginOf(XAxis);
        var found = CandleColumns.FindNearest(state, frame.X, origin, pixel.X, maximumDistance);
        index = found;
        x = found >= 0 ? state.Time[found] - origin : 0;
        y = found >= 0 ? state.Close[found] : 0;
        return found >= 0;
    }

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y)
    {
        var state = _data.Current;
        var exists = index >= 0 && index < state.Count;
        x = exists ? state.Time[index] - CandleColumns.OriginOf(XAxis) : 0;
        y = exists ? state.Close[index] : 0;
        return exists;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        CandleColumns.Collect(_data.Current, frame, CandleColumns.OriginOf(XAxis), _columns);
        _mesh.Clear();
        var scale = (float)frame.DpiScale;
        foreach (var column in _columns)
        {
            var look = column.Close >= column.Open ? _rising : _falling;
            if (_style == CandleStyle.Ohlc)
            {
                AddBar(frame, column, look, scale);
            }
            else
            {
                AddCandle(frame, column, look, scale);
            }
        }

        _built = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) =>
        commands.FillMesh(CollectionsMarshal.AsSpan(_mesh), frame.Bounds);

    internal static void AddRectangle(List<MeshVertex> mesh, float left, float top, float right, float bottom, Rgba8 color)
    {
        var a = new MeshVertex(new Vector2(left, top), color);
        var b = new MeshVertex(new Vector2(right, top), color);
        var c = new MeshVertex(new Vector2(right, bottom), color);
        var d = new MeshVertex(new Vector2(left, bottom), color);
        mesh.Add(a);
        mesh.Add(b);
        mesh.Add(c);
        mesh.Add(a);
        mesh.Add(c);
        mesh.Add(d);
    }

    private void Touch() => Interlocked.Increment(ref _appearance);

    private void AddCandle(in SeriesFrame frame, in CandleColumns.Column column, in CandleLook look, float scale)
    {
        var half = MathF.Max(scale * 0.5f, column.Width * (float)_bodyWidth / 2);
        var line = MathF.Max(1, MathF.Round(scale));
        var high = (float)frame.Y.ToPixel(column.High);
        var low = (float)frame.Y.ToPixel(column.Low);
        var open = (float)frame.Y.ToPixel(column.Open);
        var close = (float)frame.Y.ToPixel(column.Close);
        var top = MathF.Min(open, close);
        var bottom = MathF.Max(MathF.Max(open, close), top + line);
        var center = column.Center;
        AddRectangle(_mesh, center - (line / 2), MathF.Min(high, low), center + (line / 2), MathF.Max(high, low), look.Line);
        if (!look.IsHollow || half * 2 <= line * 3)
        {
            AddRectangle(_mesh, center - half, top, center + half, bottom, look.Body);
            return;
        }

        AddRectangle(_mesh, center - half, top, center + half, top + line, look.Line);
        AddRectangle(_mesh, center - half, bottom - line, center + half, bottom, look.Line);
        AddRectangle(_mesh, center - half, top, center - half + line, bottom, look.Line);
        AddRectangle(_mesh, center + half - line, top, center + half, bottom, look.Line);
    }

    private void AddBar(in SeriesFrame frame, in CandleColumns.Column column, in CandleLook look, float scale)
    {
        var half = MathF.Max(scale, column.Width * (float)_bodyWidth / 2);
        var line = MathF.Max(1, MathF.Round(scale));
        var high = (float)frame.Y.ToPixel(column.High);
        var low = (float)frame.Y.ToPixel(column.Low);
        var open = (float)frame.Y.ToPixel(column.Open);
        var close = (float)frame.Y.ToPixel(column.Close);
        var center = column.Center;
        AddRectangle(_mesh, center - (line / 2), MathF.Min(high, low), center + (line / 2), MathF.Max(MathF.Max(high, low), MathF.Min(high, low) + line), look.Line);
        AddRectangle(_mesh, center - half, open - (line / 2), center, open + (line / 2), look.Line);
        AddRectangle(_mesh, center, close - (line / 2), center + half, close + (line / 2), look.Line);
    }
}

/// <summary>The volume of each period as a bar from zero, in the colors of its candle. Usually in a pane below the candles.</summary>
public sealed class VolumeSeries : ChartSeries
{
    private readonly CandleData _data;
    private readonly List<MeshVertex> _mesh = [];
    private readonly List<CandleColumns.Column> _columns = [];
    private Rgba8 _rising = new(38, 166, 154, 140);
    private Rgba8 _falling = new(239, 83, 80, 140);
    private long _appearance;
    private long _built = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="VolumeSeries"/> class.</summary>
    /// <param name="data">The candles.</param>
    /// <param name="xAxis">The time axis.</param>
    /// <param name="yAxis">The volume axis.</param>
    public VolumeSeries(CandleData data, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis) => _data = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets or sets the color of rising periods.</summary>
    public Rgba8 RisingColor
    {
        get => _rising;
        set
        {
            _rising = value;
            Interlocked.Increment(ref _appearance);
        }
    }

    /// <summary>Gets or sets the color of falling periods.</summary>
    public Rgba8 FallingColor
    {
        get => _falling;
        set
        {
            _falling = value;
            Interlocked.Increment(ref _appearance);
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _rising;

    /// <inheritdoc />
    protected internal override long Version => _data.Version + Interlocked.Read(ref _appearance);

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var state = _data.Current;
        x = CandleColumns.TimeRange(state, CandleColumns.OriginOf(XAxis));
        y = new DoubleRange(0, state.MostVolume > 0 ? state.MostVolume : 1);
        return state.Count > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        var state = _data.Current;
        var origin = CandleColumns.OriginOf(XAxis);
        var found = CandleColumns.FindNearest(state, frame.X, origin, pixel.X, maximumDistance);
        index = found;
        x = found >= 0 ? state.Time[found] - origin : 0;
        y = found >= 0 ? state.Volume[found] : 0;
        return found >= 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        CandleColumns.Collect(_data.Current, frame, CandleColumns.OriginOf(XAxis), _columns);
        _mesh.Clear();
        var zero = (float)frame.Y.ToPixel(0);
        var gap = (float)frame.DpiScale;
        foreach (var column in _columns)
        {
            var half = MathF.Max(gap * 0.5f, (column.Width / 2) - (gap / 2));
            var top = (float)frame.Y.ToPixel(column.Volume);
            CandleSeries.AddRectangle(_mesh, column.Center - half, MathF.Min(top, zero), column.Center + half, MathF.Max(top, zero), column.Close >= column.Open ? _rising : _falling);
        }

        _built = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) =>
        commands.FillMesh(CollectionsMarshal.AsSpan(_mesh), frame.Bounds);
}

/// <summary>Groups the visible candles into pixel columns. Runs per frame without allocations once the list has grown.</summary>
internal static class CandleColumns
{
    // Below this many pixels per period, periods are merged into columns of this width.
    private const double ColumnPixels = 3;

    // A time axis counts from its origin; times in the data are absolute.
    public static double OriginOf(AxisBase axis) => axis is DateTimeAxis time ? time.OriginTicks : 0;

    public static DoubleRange TimeRange(CandleData.State state, double origin) =>
        state.Count > 0 ? new DoubleRange(state.Time[0] - origin, state.Time[state.Count - 1] - origin) : new DoubleRange(0, 1);

    public static void Collect(CandleData.State state, in SeriesFrame frame, double origin, List<Column> columns)
    {
        columns.Clear();
        if (state.Count == 0)
        {
            return;
        }

        var left = frame.Bounds.X;
        var right = (double)frame.Bounds.X + frame.Bounds.Width;
        var (start, end) = Visible(state, frame.X, origin, left, right);
        if (end <= start)
        {
            return;
        }

        var slot = Slot(state, frame.X, origin, start, end);
        if (slot >= ColumnPixels)
        {
            for (var i = start; i < end; i++)
            {
                columns.Add(new Column((float)frame.X.ToPixel(state.Time[i] - origin), (float)slot, state.Open[i], state.High[i], state.Low[i], state.Close[i], state.Volume[i], i));
            }

            return;
        }

        var current = long.MinValue;
        for (var i = start; i < end; i++)
        {
            var pixel = frame.X.ToPixel(state.Time[i] - origin);
            var key = (long)Math.Floor((pixel - left) / ColumnPixels);
            if (key != current)
            {
                columns.Add(new Column((float)(left + ((key + 0.5) * ColumnPixels)), (float)ColumnPixels, state.Open[i], state.High[i], state.Low[i], state.Close[i], state.Volume[i], i));
                current = key;
                continue;
            }

            var last = columns[^1];
            columns[^1] = last with
            {
                High = Math.Max(last.High, state.High[i]),
                Low = Math.Min(last.Low, state.Low[i]),
                Close = state.Close[i],
                Volume = last.Volume + state.Volume[i],
                Last = i,
            };
        }
    }

    // The nearest candle to a pixel column, if it lies within half a period or the given distance.
    public static int FindNearest(CandleData.State state, LinearAxisTransform x, double origin, double pixel, double maximumDistance)
    {
        if (state.Count == 0)
        {
            return -1;
        }

        var time = x.ToData(pixel) + origin;
        var after = Math.Min(state.FindFirst(time), state.Count - 1);
        var before = Math.Max(after - 1, 0);
        var nearest = Math.Abs(state.Time[after] - time) < Math.Abs(state.Time[before] - time) ? after : before;
        var reach = Math.Max(maximumDistance, Slot(state, x, origin, 0, state.Count) / 2);
        return Math.Abs(x.ToPixel(state.Time[nearest] - origin) - pixel) <= reach ? nearest : -1;
    }

    private static (int Start, int End) Visible(CandleData.State state, LinearAxisTransform x, double origin, double left, double right)
    {
        var (low, high) = (x.ToData(left) + origin, x.ToData(right) + origin);
        if (low > high)
        {
            (low, high) = (high, low);
        }

        // One period more on each side, so that candles cut by the edge still show.
        var start = Math.Max(0, state.FindFirst(low) - 1);
        var end = Math.Min(state.Count, state.FindFirst(high) + 1);
        return (start, end);
    }

    // The room of one period on the screen: the distance from the first to the last candle divided by the periods
    // between them. Measured in pixels, it stays right on an axis that hides nights or weekends.
    private static double Slot(CandleData.State state, LinearAxisTransform x, double origin, int start, int end) =>
        end - start > 1
            ? Math.Abs(x.ToPixel(state.Time[end - 1] - origin) - x.ToPixel(state.Time[start] - origin)) / (end - start - 1)
            : Math.Abs(x.PixelsPerUnit);

    internal readonly record struct Column(float Center, float Width, double Open, double High, double Low, double Close, double Volume, int Last);
}

/// <summary>Shows open, high, low, close and volume of the candle under the pointer, or of the last one, at the top left.</summary>
/// <param name="series">The candles.</param>
public sealed class CandleReadoutOverlay(CandleSeries series) : ChartOverlay
{
    private const float LabelSize = 12;

    private readonly CandleSeries _series = series ?? throw new ArgumentNullException(nameof(series));

    /// <inheritdoc />
    protected internal override long ExternalVersion => _series.Data.Version + _series.HoveredIndex + ChartText.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var data = _series.Data;
        var count = data.Count;
        if (count == 0)
        {
            return;
        }

        var index = _series.HoveredIndex is >= 0 and var hovered && hovered < count ? hovered : count - 1;
        var candle = data[index];
        var culture = ChartText.Culture;
        var text = _series.XAxis.FormatCursorValue(candle.Time - CandleColumns.OriginOf(_series.XAxis))
            + "   O " + candle.Open.ToString("G6", culture)
            + "   H " + candle.High.ToString("G6", culture)
            + "   L " + candle.Low.ToString("G6", culture)
            + "   C " + candle.Close.ToString("G6", culture)
            + (candle.Volume > 0 ? "   V " + candle.Volume.ToString("N0", culture) : string.Empty);
        var pane = canvas.Layout.Panes[0];
        var color = candle.IsRising ? _series.Rising.Line : _series.Falling.Line;
        canvas.DrawText(text, Math.Round(pane.X + 8), Math.Round(pane.Y + 6 + canvas.GetLineHeight(LabelSize) * 0.8), LabelSize, color);
    }
}

/// <summary>Marks the candle under the pointer, in the price pane and in the volume pane.</summary>
public sealed class CandleModifier : ChartModifier
{
    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart is not { } chart || chart.Series.OfType<CandleSeries>().FirstOrDefault() is not { } candles)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled)
        {
            candles.HoveredIndex = -1;
        }
        else if (input.Kind == PointerEventKind.Moved)
        {
            candles.HoveredIndex = chart.TryHitTest(input.Position, double.MaxValue, HitTestMode.NearestX, out var hit) && hit.Series is CandleSeries or VolumeSeries
                ? (int)hit.Index
                : -1;
        }

        return ModifierResult.Ignored;
    }
}

/// <summary>Describes charts of prices.</summary>
public static class FinanceCharts
{
    /// <summary>Describes candles with the values of the candle under the pointer.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="data">The candles.</param>
    /// <param name="configure">Sets further properties, for example <see cref="CandleSeries.Style"/>.</param>
    /// <param name="yAxis">The id of the price axis, or <see langword="null"/> for the first one.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Candles(this ChartBuilder builder, CandleData data, Action<CandleSeries>? configure = null, string? yAxis = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddSeries(
                (x, y) =>
                {
                    var series = new CandleSeries(data, x, y);
                    configure?.Invoke(series);
                    return series;
                },
                yAxis: yAxis)
            .AddOverlay(chart => new CandleReadoutOverlay(chart.Series.OfType<CandleSeries>().First()))
            .Modifiers(m => m.Custom(() => new CandleModifier()));
    }

    /// <summary>Describes the volume bars of candles.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="data">The candles.</param>
    /// <param name="yAxis">The id of the volume axis, usually in a pane of its own.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Volume(this ChartBuilder builder, CandleData data, string? yAxis = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSeries((x, y) => new VolumeSeries(data, x, y), yAxis: yAxis);
    }
}
