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

/// <summary>The side of the chart a volume profile grows from.</summary>
public enum ProfileSide
{
    /// <summary>From the right edge to the left.</summary>
    Right,

    /// <summary>From the left edge to the right.</summary>
    Left,
}

/// <summary>
/// A market profile: the volume traded at each price level of the visible candles, as horizontal bars over the
/// candles. Each candle spreads its volume evenly from its low to its high. The level with the most volume, the point
/// of control, and the levels around it that hold <see cref="ValueAreaShare"/> of the volume, the value area, stand out.
/// The profile follows the visible time and price range.
/// </summary>
public sealed class VolumeProfileSeries : ChartSeries
{
    private readonly CandleData _data;
    private readonly List<MeshVertex> _mesh = [];
    private readonly Profile[] _profiles = [new(), new()];
    private int _published;
    private int _levels = 48;
    private double _width = 0.25;
    private double _valueArea = 0.7;
    private ProfileSide _side;
    private Rgba8 _color = new(120, 144, 200, 90);
    private Rgba8 _valueAreaColor = new(120, 144, 200, 160);
    private Rgba8 _pointOfControl = new(255, 190, 11, 220);
    private int _hovered = -1;
    private long _appearance;
    private long _built = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="VolumeProfileSeries"/> class.</summary>
    /// <param name="data">The candles.</param>
    /// <param name="xAxis">The time axis.</param>
    /// <param name="yAxis">The price axis.</param>
    public VolumeProfileSeries(CandleData data, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis) => _data = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets or sets the number of price levels across the visible price range.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 2 or above 1000.</exception>
    public int Levels
    {
        get => _levels;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 2);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1000);
            _levels = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the length of the longest bar as a share of the width of the pane.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not above 0 and at most 1.</exception>
    public double Width
    {
        get => _width;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1);
            _width = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the share of the volume that the value area holds, usually 0.7.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0 to 1.</exception>
    public double ValueAreaShare
    {
        get => _valueArea;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1);
            _valueArea = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the side the bars grow from.</summary>
    public ProfileSide Side
    {
        get => _side;
        set
        {
            _side = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the color of the levels outside the value area.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the color of the value area.</summary>
    public Rgba8 ValueAreaColor
    {
        get => _valueAreaColor;
        set
        {
            _valueAreaColor = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the color of the point of control.</summary>
    public Rgba8 PointOfControlColor
    {
        get => _pointOfControl;
        set
        {
            _pointOfControl = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the level under the pointer, or -1 for none. <see cref="VolumeProfileModifier"/> sets it.</summary>
    public int HoveredLevel
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
    public override Rgba8 AccentColor => _valueAreaColor;

    /// <inheritdoc />
    protected internal override long Version => _data.Version + Interlocked.Read(ref _appearance);

    /// <summary>Gets the profile last drawn. Safe from any thread.</summary>
    internal Profile Current => _profiles[Volatile.Read(ref _published)];

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var state = _data.Current;
        x = CandleColumns.TimeRange(state, CandleColumns.OriginOf(XAxis));
        y = state.PriceRange;
        return state.Count > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        var profile = Current;
        var level = profile.LevelAt(pixel.X, pixel.Y);
        index = level;
        x = 0;
        y = level >= 0 ? profile.PriceOf(level) : 0;
        return level >= 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        // Two profiles take turns, so a reader on another thread keeps a complete one while the next is built.
        var next = 1 - _published;
        var profile = _profiles[next];
        profile.Build(_data.Current, frame, CandleColumns.OriginOf(XAxis), _levels, _valueArea, _width, _side);
        Volatile.Write(ref _published, next);

        _mesh.Clear();
        var hovered = HoveredLevel;
        for (var level = 0; level < profile.Count; level++)
        {
            if (profile.Volumes[level] <= 0)
            {
                continue;
            }

            var (left, top, right, bottom) = profile.BarOf(level);
            var color = level == profile.PointOfControl ? _pointOfControl : profile.IsInValueArea(level) ? _valueAreaColor : _color;
            if (level == hovered)
            {
                color = color with { A = (byte)Math.Min(255, color.A + 60) };
            }

            CandleSeries.AddRectangle(_mesh, left, top + 0.5f, right, bottom - 0.5f, color);
        }

        _built = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) =>
        commands.FillMesh(CollectionsMarshal.AsSpan(_mesh), frame.Bounds);

    private void Touch() => Interlocked.Increment(ref _appearance);

    /// <summary>The volume of each price level and where its bar lies, in the pixels of the frame it was built for.</summary>
    internal sealed class Profile
    {
        private double[] _volumes = new double[64];
        private float _left;
        private float _top;
        private float _right;
        private float _levelHeight;
        private float _scale;
        private ProfileSide _side;

        public int Count { get; private set; }

        public ReadOnlySpan<double> Volumes => _volumes.AsSpan(0, Count);

        public double Largest { get; private set; }

        public double Total { get; private set; }

        public int PointOfControl { get; private set; } = -1;

        public int ValueAreaLow { get; private set; }

        public int ValueAreaHigh { get; private set; } = -1;

        /// <summary>Gets the price range of the levels; level 0 is the lowest.</summary>
        public DoubleRange Prices { get; private set; }

        /// <summary>Gets the dpi scale of the pixels, so that positions in device-independent units can be compared.</summary>
        public float Scale => _scale;

        public bool IsInValueArea(int level) => level >= ValueAreaLow && level <= ValueAreaHigh;

        public double PriceOf(int level) => Prices.Min + ((level + 0.5) * Prices.Length / Count);

        public DoubleRange RangeOf(int level) => new(Prices.Min + (level * Prices.Length / Count), Prices.Min + ((level + 1) * Prices.Length / Count));

        public (float Left, float Top, float Right, float Bottom) BarOf(int level)
        {
            var length = (float)(Largest > 0 ? _volumes[level] / Largest : 0) * (_right - _left);
            var bottom = _top + ((Count - level) * _levelHeight);
            var top = bottom - _levelHeight;
            return _side == ProfileSide.Right ? (_right - length, top, _right, bottom) : (_left, top, _left + length, bottom);
        }

        /// <summary>Finds the level whose bar lies under a point in pixels, or -1.</summary>
        public int LevelAt(float x, float y)
        {
            if (Count == 0 || _levelHeight <= 0)
            {
                return -1;
            }

            var level = Count - 1 - (int)Math.Floor((y - _top) / _levelHeight);
            if (level < 0 || level >= Count)
            {
                return -1;
            }

            var (left, _, right, _) = BarOf(level);
            return x >= left && x <= right && _volumes[level] > 0 ? level : -1;
        }

        public void Build(CandleData.State state, in SeriesFrame frame, double origin, int levels, double valueArea, double width, ProfileSide side)
        {
            if (_volumes.Length < levels)
            {
                _volumes = new double[levels];
            }

            Count = levels;
            Array.Clear(_volumes, 0, levels);
            var bounds = frame.Bounds;
            _scale = (float)frame.DpiScale;
            _side = side;
            _top = bounds.Y;
            _levelHeight = bounds.Height / (float)levels;
            var length = (float)(bounds.Width * width);
            (_left, _right) = side == ProfileSide.Right ? (bounds.X + bounds.Width - length, (float)(bounds.X + bounds.Width)) : (bounds.X, bounds.X + length);
            var (low, high) = (frame.Y.ToData(bounds.Y + bounds.Height), frame.Y.ToData(bounds.Y));
            Prices = new DoubleRange(Math.Min(low, high), Math.Max(low, high));
            Total = 0;
            Largest = 0;
            PointOfControl = -1;
            ValueAreaLow = 0;
            ValueAreaHigh = -1;
            if (state.Count == 0 || Prices.Length <= 0)
            {
                return;
            }

            var (first, last) = (frame.X.ToData(bounds.X) + origin, frame.X.ToData(bounds.X + bounds.Width) + origin);
            var start = state.FindFirst(Math.Min(first, last));
            var end = state.FindFirst(Math.Max(first, last));
            for (var i = start; i < end || (i < state.Count && state.Time[i] <= Math.Max(first, last)); i++)
            {
                Spread(state.Low[i], state.High[i], state.Volume[i]);
            }

            FindValueArea(valueArea);
        }

        // The volume of a candle falls evenly on the levels from its low to its high, as far as they are visible.
        private void Spread(double low, double high, double volume)
        {
            if (volume <= 0)
            {
                return;
            }

            var step = Prices.Length / Count;
            var from = (int)Math.Floor((low - Prices.Min) / step);
            var to = (int)Math.Floor((high - Prices.Min) / step);
            var share = volume / (to - from + 1);
            for (var level = Math.Max(from, 0); level <= Math.Min(to, Count - 1); level++)
            {
                _volumes[level] += share;
                Total += share;
            }
        }

        // Grows from the point of control to the side with more volume until the share is reached.
        private void FindValueArea(double share)
        {
            for (var level = 0; level < Count; level++)
            {
                if (_volumes[level] > Largest)
                {
                    Largest = _volumes[level];
                    PointOfControl = level;
                }
            }

            if (PointOfControl < 0)
            {
                return;
            }

            var (low, high) = (PointOfControl, PointOfControl);
            var sum = _volumes[PointOfControl];
            while (sum < Total * share && (low > 0 || high < Count - 1))
            {
                var below = low > 0 ? _volumes[low - 1] : -1;
                var above = high < Count - 1 ? _volumes[high + 1] : -1;
                if (above >= below)
                {
                    high++;
                    sum += above;
                }
                else
                {
                    low--;
                    sum += below;
                }
            }

            (ValueAreaLow, ValueAreaHigh) = (low, high);
        }
    }
}

/// <summary>Shows the price range and the volume of the level under the pointer.</summary>
/// <param name="series">The profile.</param>
public sealed class VolumeProfileOverlay(VolumeProfileSeries series) : ChartOverlay
{
    private readonly VolumeProfileSeries _series = series ?? throw new ArgumentNullException(nameof(series));

    /// <inheritdoc />
    protected internal override long ExternalVersion => _series.Version + ChartText.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var profile = _series.Current;
        var level = _series.HoveredLevel;
        if (level < 0 || level >= profile.Count || profile.Scale <= 0)
        {
            return;
        }

        var culture = ChartText.Culture;
        var range = profile.RangeOf(level);
        var share = profile.Total > 0 ? profile.Volumes[level] / profile.Total : 0;
        var text = range.Min.ToString("G5", culture) + " – " + range.Max.ToString("G5", culture) + ": "
            + profile.Volumes[level].ToString("N0", culture) + " (" + (share * 100).ToString("0.#", culture) + " %)";
        var (left, top, right, _) = profile.BarOf(level);
        var width = canvas.MeasureWidth(text, canvas.LabelSize) + 12;
        var x = _series.Side == ProfileSide.Right ? (left / profile.Scale) - width - 4 : (right / profile.Scale) + 4;
        canvas.DrawLabel(text, x, (top / profile.Scale) - 4, canvas.BackgroundColor with { A = 235 }, canvas.TextColor);
    }
}

/// <summary>Marks the level of a volume profile under the pointer.</summary>
public sealed class VolumeProfileModifier : ChartModifier
{
    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart is not { } chart || chart.Series.OfType<VolumeProfileSeries>().FirstOrDefault() is not { } series)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled)
        {
            series.HoveredLevel = -1;
        }
        else if (input.Kind == PointerEventKind.Moved)
        {
            var profile = series.Current;
            series.HoveredLevel = profile.LevelAt(input.Position.X * profile.Scale, input.Position.Y * profile.Scale);
        }

        return ModifierResult.Ignored;
    }
}

/// <summary>Describes market profiles.</summary>
public static class MarketProfiles
{
    /// <summary>Describes the volume per price level of the visible candles, over the candles.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="data">The candles.</param>
    /// <param name="configure">Sets further properties, for example <see cref="VolumeProfileSeries.Levels"/>.</param>
    /// <param name="yAxis">The id of the price axis, or <see langword="null"/> for the first one.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder VolumeProfile(this ChartBuilder builder, CandleData data, Action<VolumeProfileSeries>? configure = null, string? yAxis = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddSeries(
                (x, y) =>
                {
                    var series = new VolumeProfileSeries(data, x, y);
                    configure?.Invoke(series);
                    return series;
                },
                yAxis: yAxis)
            .AddOverlay(chart => new VolumeProfileOverlay(chart.Series.OfType<VolumeProfileSeries>().First()))
            .Modifiers(m => m.Custom(() => new VolumeProfileModifier()));
    }
}
