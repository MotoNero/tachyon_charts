using System.Collections.ObjectModel;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using CoreDateTimeAxis = Tachyon.Charts.Core.Axes.DateTimeAxis;
using CoreNumericAxis = Tachyon.Charts.Core.Axes.NumericAxis;
using KeyBinding = Tachyon.Charts.Core.Input.KeyBinding;
using Point = Avalonia.Point;
using SelectionMode = Tachyon.Charts.Core.Input.SelectionMode;

namespace Tachyon.Charts.Avalonia;

/// <summary>
/// Receives the changes of chart elements.
/// </summary>
internal interface IChartElementHost
{
    /// <summary>Queues a change for the render thread.</summary>
    void Post(Action change);

    /// <summary>Reports a change of the axes, which needs a new chart.</summary>
    void OnAxesChanged();

    /// <summary>Reports a change of the series.</summary>
    void OnSeriesChanged();

    /// <summary>Reports a change of the modifiers.</summary>
    void OnModifiersChanged();

    /// <summary>Reports a change of the annotations.</summary>
    void OnAnnotationsChanged();
}

/// <summary>
/// An element declared inside a <see cref="Chart"/>. It is a logical child, so bindings see the data
/// context of the chart.
/// </summary>
public abstract class ChartElement : StyledElement
{
    internal IChartElementHost? Host { get; set; }

    /// <summary>Tests whether a change belongs to the properties of the chart elements.</summary>
    /// <param name="change">The change.</param>
    /// <returns>Whether it is not a general property such as the data context.</returns>
    private protected static bool IsOwn(AvaloniaPropertyChangedEventArgs change) =>
        typeof(ChartElement).IsAssignableFrom(change.Property.OwnerType);
}

/// <summary>
/// A list of chart elements in a <see cref="Chart"/>.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
public sealed class ChartElementCollection<T> : ObservableCollection<T>
    where T : ChartElement
{
    internal ChartElementCollection()
    {
    }
}

/// <summary>
/// An axis of a <see cref="Chart"/>.
/// </summary>
public abstract class ChartAxis : ChartElement
{
    /// <summary>Identifies the <see cref="Id"/> property.</summary>
    public static readonly StyledProperty<string?> IdProperty = AvaloniaProperty.Register<ChartAxis, string?>(nameof(Id));

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<ChartAxis, string?>(nameof(Title));

    /// <summary>Identifies the <see cref="Position"/> property.</summary>
    public static readonly StyledProperty<AxisPosition?> PositionProperty = AvaloniaProperty.Register<ChartAxis, AxisPosition?>(nameof(Position));

    /// <summary>Identifies the <see cref="VisibleRange"/> property.</summary>
    public static readonly StyledProperty<DoubleRange> VisibleRangeProperty =
        AvaloniaProperty.Register<ChartAxis, DoubleRange>(nameof(VisibleRange), DoubleRange.Empty, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Identifies the <see cref="Limits"/> property.</summary>
    public static readonly StyledProperty<DoubleRange?> LimitsProperty = AvaloniaProperty.Register<ChartAxis, DoubleRange?>(nameof(Limits));

    /// <summary>Identifies the <see cref="IsAutoRange"/> property.</summary>
    public static readonly StyledProperty<bool> IsAutoRangeProperty =
        AvaloniaProperty.Register<ChartAxis, bool>(nameof(IsAutoRange), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Identifies the <see cref="IsInverted"/> property.</summary>
    public static readonly StyledProperty<bool> IsInvertedProperty = AvaloniaProperty.Register<ChartAxis, bool>(nameof(IsInverted));

    private bool _isPublishing;

    /// <summary>Gets or sets the name series use to refer to this axis.</summary>
    public string? Id
    {
        get => GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    /// <summary>Gets or sets the title.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the side of the plot, or <see langword="null"/> for the usual one.</summary>
    public AxisPosition? Position
    {
        get => GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>Gets or sets the visible range. Binds both ways, and an empty range leaves it to the engine.</summary>
    public DoubleRange VisibleRange
    {
        get => GetValue(VisibleRangeProperty);
        set => SetValue(VisibleRangeProperty, value);
    }

    /// <summary>Gets or sets the limits of navigation, or <see langword="null"/> for none.</summary>
    public DoubleRange? Limits
    {
        get => GetValue(LimitsProperty);
        set => SetValue(LimitsProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the axis follows the data.</summary>
    public bool IsAutoRange
    {
        get => GetValue(IsAutoRangeProperty);
        set => SetValue(IsAutoRangeProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the axis runs against its usual direction.</summary>
    public bool IsInverted
    {
        get => GetValue(IsInvertedProperty);
        set => SetValue(IsInvertedProperty, value);
    }

    internal AxisBase? Core { get; private set; }

    /// <summary>Gets or sets a value indicating whether the axis takes no room and shows nothing.</summary>
    public bool IsHidden { get; set; }

    internal AxisBase Build(string fallbackId, AxisPosition fallbackPosition)
    {
        var core = CreateCore(Id ?? fallbackId, Position ?? fallbackPosition);
        ReadState().ApplyTo(core);
        core.IsHidden = IsHidden;
        Core = core;
        return core;
    }

    /// <summary>Takes over a state of the engine without sending it back there.</summary>
    internal void Publish(DoubleRange range, bool isAutoRange)
    {
        _isPublishing = true;
        try
        {
            SetCurrentValue(VisibleRangeProperty, range);
            SetCurrentValue(IsAutoRangeProperty, isAutoRange);
        }
        finally
        {
            _isPublishing = false;
        }
    }

    /// <summary>Creates the axis of the engine.</summary>
    /// <param name="id">The axis name.</param>
    /// <param name="position">The position.</param>
    /// <returns>The axis.</returns>
    private protected abstract AxisBase CreateCore(string id, AxisPosition position);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (!IsOwn(change))
        {
            return;
        }

        if (change.Property != TitleProperty && change.Property != VisibleRangeProperty && change.Property != LimitsProperty
            && change.Property != IsAutoRangeProperty && change.Property != IsInvertedProperty)
        {
            Host?.OnAxesChanged();
            return;
        }

        if (_isPublishing || Host is not { } host || Core is not { } core)
        {
            return;
        }

        // Like on the engine axis, a range set from outside ends the automatic range.
        if (change.Property == VisibleRangeProperty)
        {
            SetCurrentValue(IsAutoRangeProperty, false);
        }

        var state = ReadState();
        host.Post(() => state.ApplyTo(core));
    }

    private AxisState ReadState() => new(Title, VisibleRange, Limits, IsAutoRange, IsInverted);

    // Properties belong to the UI thread, so a change travels as a copy.
    private readonly record struct AxisState(string? Title, DoubleRange Range, DoubleRange? Limits, bool IsAutoRange, bool IsInverted)
    {
        public void ApplyTo(AxisBase axis)
        {
            axis.Title = Title;
            if (axis.Limits != Limits)
            {
                axis.Limits = Limits;
            }

            axis.IsInverted = IsInverted;
            if (!Range.IsEmpty && axis.VisibleRange != Range)
            {
                axis.VisibleRange = Range;
            }

            // Last: setting a range ends the automatic range.
            axis.IsAutoRange = IsAutoRange;
        }
    }
}

/// <summary>An axis with names instead of numbers.</summary>
public sealed class CategoryAxis : ChartAxis
{
    /// <summary>Gets or sets the names of the places.</summary>
    public IReadOnlyList<string> Categories { get; set; } = [];

    private protected override AxisBase CreateCore(string id, AxisPosition position) => new Tachyon.Charts.Core.Axes.CategoryAxis(id, position, Categories);
}

/// <summary>An axis with numbers.</summary>
public sealed class NumericAxis : ChartAxis
{
    private protected override AxisBase CreateCore(string id, AxisPosition position) => new CoreNumericAxis(id, position);
}

/// <summary>An axis with a logarithmic scale.</summary>
public sealed class LogAxis : ChartAxis
{
    /// <summary>Identifies the <see cref="Base"/> property.</summary>
    public static readonly StyledProperty<double> BaseProperty = AvaloniaProperty.Register<LogAxis, double>(nameof(Base), 10);

    /// <summary>Identifies the <see cref="NonPositive"/> property.</summary>
    public static readonly StyledProperty<NonPositiveValues> NonPositiveProperty = AvaloniaProperty.Register<LogAxis, NonPositiveValues>(nameof(NonPositive));

    /// <summary>Gets or sets the base, greater than one.</summary>
    public double Base
    {
        get => GetValue(BaseProperty);
        set => SetValue(BaseProperty, value);
    }

    /// <summary>Gets or sets what happens to values at or below zero.</summary>
    public NonPositiveValues NonPositive
    {
        get => GetValue(NonPositiveProperty);
        set => SetValue(NonPositiveProperty, value);
    }

    private protected override AxisBase CreateCore(string id, AxisPosition position) =>
        new Core.Axes.LogAxis(id, position) { Base = Base, NonPositive = NonPositive };
}

/// <summary>An axis with times. It follows the data by default, because a fixed range needs an origin.</summary>
public sealed class DateTimeAxis : ChartAxis
{
    /// <summary>Initializes a new instance of the <see cref="DateTimeAxis"/> class.</summary>
    public DateTimeAxis()
    {
        IsAutoRange = true;
    }

    private protected override AxisBase CreateCore(string id, AxisPosition position) => new CoreDateTimeAxis(id, position);
}

/// <summary>A series of a <see cref="Chart"/>.</summary>
public abstract class ChartSeriesElement : ChartElement
{
    private bool _showsLastValue;
    private bool _showsValues;

    private ItemsDataSource? _items;

    /// <summary>Identifies the <see cref="DataSource"/> property.</summary>
    public static readonly StyledProperty<object?> DataSourceProperty = AvaloniaProperty.Register<ChartSeriesElement, object?>(nameof(DataSource));

    /// <summary>Identifies the <see cref="SeriesName"/> property.</summary>
    public static readonly StyledProperty<string?> SeriesNameProperty = AvaloniaProperty.Register<ChartSeriesElement, string?>(nameof(SeriesName));

    /// <summary>Identifies the <see cref="XAxisId"/> property.</summary>
    public static readonly StyledProperty<string?> XAxisIdProperty = AvaloniaProperty.Register<ChartSeriesElement, string?>(nameof(XAxisId));

    /// <summary>Identifies the <see cref="YAxisId"/> property.</summary>
    public static readonly StyledProperty<string?> YAxisIdProperty = AvaloniaProperty.Register<ChartSeriesElement, string?>(nameof(YAxisId));

    /// <summary>Gets or sets the data.</summary>
    public object? DataSource
    {
        get => GetValue(DataSourceProperty);
        set => SetValue(DataSourceProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the newest value shows as a mark at the vertical axis, live.</summary>
    public bool ShowsLastValue
    {
        get => _showsLastValue;
        set
        {
            _showsLastValue = value;
            Host?.OnSeriesChanged();
        }
    }

    /// <summary>Gets or sets a value indicating whether the values show as labels at their points.</summary>
    public bool ShowsValues
    {
        get => _showsValues;
        set
        {
            _showsValues = value;
            Host?.OnSeriesChanged();
        }
    }

    /// <summary>Gets or sets the name shown in rollovers and legends.</summary>
    public string? SeriesName
    {
        get => GetValue(SeriesNameProperty);
        set => SetValue(SeriesNameProperty, value);
    }

    /// <summary>Gets or sets a collection of objects as data, instead of <see cref="DataSource"/>. Meant for up to 100 000 items.</summary>
    public System.Collections.IEnumerable? ItemsSource { get; set; }

    /// <summary>Gets or sets the property of the items with the X value.</summary>
    public string? XPath { get; set; }

    /// <summary>Gets or sets the property of the items with the Y value.</summary>
    public string? YPath { get; set; }

    /// <summary>Gets or sets the name of the X axis, or <see langword="null"/> for the first one.</summary>
    public string? XAxisId
    {
        get => GetValue(XAxisIdProperty);
        set => SetValue(XAxisIdProperty, value);
    }

    /// <summary>Gets or sets the name of the Y axis, or <see langword="null"/> for the first one.</summary>
    public string? YAxisId
    {
        get => GetValue(YAxisIdProperty);
        set => SetValue(YAxisIdProperty, value);
    }

    /// <summary>Creates the series of the engine.</summary>
    /// <param name="x">The X axis.</param>
    /// <param name="y">The Y axis.</param>
    /// <returns>The series, or <see langword="null"/> without data.</returns>
    /// <exception cref="InvalidOperationException">The data has the wrong type.</exception>
    internal virtual ChartSeries? Build(AxisBase x, AxisBase y)
    {
        _items?.Dispose();
        _items = ItemsSource is { } items ? new ItemsDataSource(items, XPath ?? "X", YPath ?? "Y") : null;
        if ((_items?.Source ?? DataSource) is not { } source)
        {
            return null;
        }

        var contract = Array.Find(
            source.GetType().GetInterfaces(),
            type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDataSource<,>))
            ?? throw new InvalidOperationException($"{source.GetType().Name} is not a data source.");
        var series = BuildTyped(source, contract.GetGenericArguments(), x, y);
        series.Name = SeriesName;
        series.DiagnosticWarning = _items?.Warning;
        return series;
    }

    /// <summary>Gets the value scale of the series, or <see langword="null"/> without one.</summary>
    /// <param name="id">The identifier of a new axis.</param>
    /// <returns>The axis.</returns>
    internal virtual Core.Axes.ColorAxis? GetColorAxis(string id) => null;

    /// <summary>Creates the series for the value types of the data.</summary>
    private protected abstract ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (IsOwn(change))
        {
            OnSeriesPropertyChanged(change);
        }
    }

    /// <summary>Reports a change. By default it needs a new series.</summary>
    /// <param name="change">The change.</param>
    private protected virtual void OnSeriesPropertyChanged(AvaloniaPropertyChangedEventArgs change) => Host?.OnSeriesChanged();

    /// <summary>Converts a color.</summary>
    /// <param name="color">The Avalonia color.</param>
    /// <returns>The engine color.</returns>
    private protected static Rgba8 ToEngine(Color color) => new(color.R, color.G, color.B, color.A);
}

/// <summary>
/// A series created by code, for all kinds of series without an element of their own.
/// </summary>
public sealed class CustomSeries : ChartSeriesElement
{
    /// <summary>Gets or sets what creates the series from its axes. A chart can call it again.</summary>
    public Func<AxisBase, AxisBase, ChartSeries>? Factory { get; set; }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y)
    {
        if (Factory?.Invoke(x, y) is not { } series)
        {
            return null;
        }

        series.Name = SeriesName;
        return series;
    }

    private protected override ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y) =>
        throw new NotSupportedException("A custom series has no value types.");
}

/// <summary>A line through the points of a data source.</summary>
public sealed class LineSeries : ChartSeriesElement
{
    /// <summary>Identifies the <see cref="Stroke"/> property.</summary>
    public static readonly StyledProperty<Color> StrokeProperty = AvaloniaProperty.Register<LineSeries, Color>(nameof(Stroke), Color.FromRgb(80, 170, 240));

    /// <summary>Identifies the <see cref="StrokeThickness"/> property.</summary>
    public static readonly StyledProperty<double> StrokeThicknessProperty = AvaloniaProperty.Register<LineSeries, double>(nameof(StrokeThickness), 1.5);

    /// <summary>Identifies the <see cref="IsStep"/> property.</summary>
    public static readonly StyledProperty<bool> IsStepProperty = AvaloniaProperty.Register<LineSeries, bool>(nameof(IsStep));

    /// <summary>Gets or sets the line color.</summary>
    public Color Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Gets or sets the thickness in device-independent units.</summary>
    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Gets or sets the lengths of dashes and spaces in device-independent units.</summary>
    public IReadOnlyList<float>? StrokeDashArray { get; set; }

    /// <summary>Gets or sets a value indicating whether the line keeps each value until the next point.</summary>
    public bool IsStep
    {
        get => GetValue(IsStepProperty);
        set => SetValue(IsStepProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the line runs as a smooth curve.</summary>
    public bool IsSpline { get; set; }

    /// <summary>Gets or sets the radius of the glow around the line in pixels, or <see langword="null"/> for the glow of the theme.</summary>
    public double? GlowRadius { get; set; }

    /// <summary>Gets or sets the strength of the glow from 0 to 1.</summary>
    public double GlowIntensity { get; set; } = 0.8;

    private protected override ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y) =>
        (ChartSeries)typeof(LineSeries).GetMethod(nameof(Create), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(valueTypes)
            .Invoke(this, [source, x, y])!;

    private LineSeries<TX, TY> Create<TX, TY>(IDataSource<TX, TY> source, AxisBase x, AxisBase y)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>, IMinMaxValue<TY> => new(source, x, y)
        {
            Color = ToEngine(Stroke),
            Thickness = StrokeThickness,
            DashPattern = StrokeDashArray,
            IsStep = IsStep,
            IsSpline = IsSpline,
            GlowRadius = GlowRadius,
            GlowIntensity = GlowIntensity,
        };
}

/// <summary>Markers at the points of a data source, or a density map if there are too many.</summary>
public sealed class ScatterSeries : ChartSeriesElement
{
    /// <summary>Identifies the <see cref="Fill"/> property.</summary>
    public static readonly StyledProperty<Color> FillProperty = AvaloniaProperty.Register<ScatterSeries, Color>(nameof(Fill), Color.FromRgb(240, 130, 90));

    /// <summary>Identifies the <see cref="Marker"/> property.</summary>
    public static readonly StyledProperty<MarkerShape> MarkerProperty = AvaloniaProperty.Register<ScatterSeries, MarkerShape>(nameof(Marker));

    /// <summary>Identifies the <see cref="MarkerSize"/> property.</summary>
    public static readonly StyledProperty<double> MarkerSizeProperty = AvaloniaProperty.Register<ScatterSeries, double>(nameof(MarkerSize), 6);

    /// <summary>Identifies the <see cref="DensityColormap"/> property.</summary>
    public static readonly StyledProperty<Colormap?> DensityColormapProperty = AvaloniaProperty.Register<ScatterSeries, Colormap?>(nameof(DensityColormap));

    /// <summary>Identifies the <see cref="DensityThreshold"/> property.</summary>
    public static readonly StyledProperty<double> DensityThresholdProperty = AvaloniaProperty.Register<ScatterSeries, double>(nameof(DensityThreshold), 1);

    private Core.Axes.ColorAxis? _densityAxis;

    /// <summary>Gets or sets the marker color.</summary>
    public Color Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets the marker shape.</summary>
    public MarkerShape Marker
    {
        get => GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    /// <summary>Gets or sets the marker size in device-independent units.</summary>
    public double MarkerSize
    {
        get => GetValue(MarkerSizeProperty);
        set => SetValue(MarkerSizeProperty, value);
    }

    /// <summary>Gets or sets one size per point, for bubbles.</summary>
    public ReadOnlyMemory<float> PointSizes { get; set; }

    /// <summary>Gets or sets the colors of the density map, or <see langword="null"/> for markers only.</summary>
    public Colormap? DensityColormap
    {
        get => GetValue(DensityColormapProperty);
        set => SetValue(DensityColormapProperty, value);
    }

    /// <summary>Gets or sets the visible points per pixel from which the density map shows.</summary>
    public double DensityThreshold
    {
        get => GetValue(DensityThresholdProperty);
        set => SetValue(DensityThresholdProperty, value);
    }

    /// <inheritdoc />
    internal override Core.Axes.ColorAxis? GetColorAxis(string id)
    {
        if (DensityColormap is not { } colormap)
        {
            return null;
        }

        _densityAxis ??= new Core.Axes.ColorAxis(id) { IsLogarithmic = true, BelowColor = default(Rgba8) };
        _densityAxis.Colormap = colormap;
        return _densityAxis;
    }

    private protected override void OnSeriesPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == DensityColormapProperty && _densityAxis is { } axis)
        {
            GetColorAxis(axis.Id);
            return;
        }

        base.OnSeriesPropertyChanged(change);
    }

    private protected override ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y) =>
        (ChartSeries)typeof(ScatterSeries).GetMethod(nameof(Create), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(valueTypes)
            .Invoke(this, [source, x, y])!;

    private ScatterSeries<TX, TY> Create<TX, TY>(IDataSource<TX, TY> source, AxisBase x, AxisBase y)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY> => new(source, x, y)
        {
            Color = ToEngine(Fill),
            Shape = Marker,
            Size = MarkerSize,
            PointSizes = PointSizes,
            DensityAxis = GetColorAxis(string.Empty),
            DensityThreshold = DensityThreshold,
        };
}

/// <summary>A series that shows values as colors, with a color bar beside the plot.</summary>
public abstract class RasterSeriesElement : ChartSeriesElement
{
    /// <summary>Identifies the <see cref="Colormap"/> property.</summary>
    public static readonly StyledProperty<Colormap> ColormapProperty = AvaloniaProperty.Register<RasterSeriesElement, Colormap>(nameof(Colormap), Colormaps.Viridis);

    /// <summary>Identifies the <see cref="IsLogarithmic"/> property.</summary>
    public static readonly StyledProperty<bool> IsLogarithmicProperty = AvaloniaProperty.Register<RasterSeriesElement, bool>(nameof(IsLogarithmic));

    /// <summary>Identifies the <see cref="ColorRange"/> property.</summary>
    public static readonly StyledProperty<DoubleRange?> ColorRangeProperty = AvaloniaProperty.Register<RasterSeriesElement, DoubleRange?>(nameof(ColorRange));

    /// <summary>Identifies the <see cref="XStep"/> property.</summary>
    public static readonly StyledProperty<double> XStepProperty = AvaloniaProperty.Register<RasterSeriesElement, double>(nameof(XStep), 1);

    /// <summary>Identifies the <see cref="YStep"/> property.</summary>
    public static readonly StyledProperty<double> YStepProperty = AvaloniaProperty.Register<RasterSeriesElement, double>(nameof(YStep), 1);

    private Core.Axes.ColorAxis? _colorAxis;

    /// <summary>Gets or sets the colors. A change only replaces the table of the renderer.</summary>
    public Colormap Colormap
    {
        get => GetValue(ColormapProperty);
        set => SetValue(ColormapProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether colors follow the logarithm of the values.</summary>
    public bool IsLogarithmic
    {
        get => GetValue(IsLogarithmicProperty);
        set => SetValue(IsLogarithmicProperty, value);
    }

    /// <summary>Gets or sets the range of the colors, or <see langword="null"/> to follow the data. A fixed range keeps long runs free of new layouts.</summary>
    public DoubleRange? ColorRange
    {
        get => GetValue(ColorRangeProperty);
        set => SetValue(ColorRangeProperty, value);
    }

    /// <summary>Gets or sets the width of a column.</summary>
    public double XStep
    {
        get => GetValue(XStepProperty);
        set => SetValue(XStepProperty, value);
    }

    /// <summary>Gets or sets the height of a row.</summary>
    public double YStep
    {
        get => GetValue(YStepProperty);
        set => SetValue(YStepProperty, value);
    }

    /// <inheritdoc />
    internal override Core.Axes.ColorAxis GetColorAxis(string id)
    {
        _colorAxis ??= new Core.Axes.ColorAxis(id);
        _colorAxis.Colormap = Colormap;
        _colorAxis.IsLogarithmic = IsLogarithmic;
        if (ColorRange is { } range)
        {
            _colorAxis.VisibleRange = range;
        }

        return _colorAxis;
    }

    private protected T Source<T>()
        where T : class =>
        DataSource as T ?? throw new InvalidOperationException($"{DataSource?.GetType().Name} is not a {typeof(T).Name}.");

    private protected override ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y) =>
        throw new NotSupportedException("A raster has no value types.");

    // The axis object stays, so that the series and the texture of values stay as well.
    private protected override void OnSeriesPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ColormapProperty || change.Property == IsLogarithmicProperty || change.Property == ColorRangeProperty)
        {
            if (_colorAxis is { } axis)
            {
                GetColorAxis(axis.Id);
            }

            return;
        }

        base.OnSeriesPropertyChanged(change);
    }
}

/// <summary>A grid of values shown as colors.</summary>
public sealed class HeatmapSeries : RasterSeriesElement
{
    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart { get; set; }

    /// <summary>Gets or sets the lower edge of the first row.</summary>
    public double YStart { get; set; }

    /// <summary>Gets or sets a value indicating whether adjacent cell colors are blended.</summary>
    public bool IsInterpolated { get; set; }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y) => DataSource is null ? null : new Core.Series.HeatmapSeries(Source<HeatmapData>(), x, y, GetColorAxis(string.Empty))
    {
        Name = SeriesName,
        XStart = XStart,
        XStep = XStep,
        YStart = YStart,
        YStep = YStep,
        IsInterpolated = IsInterpolated,
    };
}

/// <summary>A waterfall of rows, fed by <see cref="SpectrogramData.AppendRow"/>.</summary>
public sealed class SpectrogramSeries : RasterSeriesElement
{
    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart { get; set; }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y) => DataSource is null ? null : new Core.Series.SpectrogramSeries(Source<SpectrogramData>(), x, y, GetColorAxis(string.Empty))
    {
        Name = SeriesName,
        XStart = XStart,
        XStep = XStep,
        YStep = YStep,
    };
}

/// <summary>Isolines of a grid, optionally with filled bands.</summary>
public sealed class ContourSeries : RasterSeriesElement
{
    /// <summary>Identifies the <see cref="IsFilled"/> property.</summary>
    public static readonly StyledProperty<bool> IsFilledProperty = AvaloniaProperty.Register<ContourSeries, bool>(nameof(IsFilled));

    /// <summary>Identifies the <see cref="ShowsLines"/> property.</summary>
    public static readonly StyledProperty<bool> ShowsLinesProperty = AvaloniaProperty.Register<ContourSeries, bool>(nameof(ShowsLines), true);

    /// <summary>Gets or sets the levels, or <see langword="null"/> for equal steps.</summary>
    public IReadOnlyList<double>? Levels { get; set; }

    /// <summary>Gets or sets a value indicating whether the bands between the lines are filled.</summary>
    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the lines are drawn.</summary>
    public bool ShowsLines
    {
        get => GetValue(ShowsLinesProperty);
        set => SetValue(ShowsLinesProperty, value);
    }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y) => DataSource is null ? null : new Core.Series.ContourSeries(Source<HeatmapData>(), x, y, GetColorAxis(string.Empty))
    {
        Name = SeriesName,
        Levels = Levels,
        IsFilled = IsFilled,
        ShowsLines = ShowsLines,
        XStep = XStep,
        YStep = YStep,
    };
}

/// <summary>Annotations do not exist as elements here yet; the shared controller needs the type.</summary>
internal abstract class ChartAnnotationElement : ChartElement
{
    public string? XAxisId => null; // NOSONAR: implements an interface

    public string? YAxisId => null; // NOSONAR: implements an interface

    public abstract Core.Overlays.ChartOverlay Build(AxisBase x, AxisBase y);
}

/// <summary>An interaction of a <see cref="Chart"/>.</summary>
public abstract class ChartModifierElement : ChartElement
{
    /// <summary>Creates the modifier of the engine.</summary>
    /// <returns>The modifier.</returns>
    internal abstract ChartModifier Build();

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (IsOwn(change))
        {
            Host?.OnModifiersChanged();
        }
    }
}

/// <summary>Zooms with the wheel and pans by dragging.</summary>
public sealed class ZoomPanModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="ZoomDirection"/> property.</summary>
    public static readonly StyledProperty<ChartDirection> ZoomDirectionProperty = AvaloniaProperty.Register<ZoomPanModifier, ChartDirection>(nameof(ZoomDirection), ChartDirection.Both);

    /// <summary>Identifies the <see cref="PanDirection"/> property.</summary>
    public static readonly StyledProperty<ChartDirection> PanDirectionProperty = AvaloniaProperty.Register<ZoomPanModifier, ChartDirection>(nameof(PanDirection), ChartDirection.Both);

    /// <summary>Gets or sets the directions of zooming.</summary>
    public ChartDirection ZoomDirection
    {
        get => GetValue(ZoomDirectionProperty);
        set => SetValue(ZoomDirectionProperty, value);
    }

    /// <summary>Gets or sets the directions of panning.</summary>
    public ChartDirection PanDirection
    {
        get => GetValue(PanDirectionProperty);
        set => SetValue(PanDirectionProperty, value);
    }

    internal override ChartModifier Build() => new Core.Input.ZoomPanModifier { ZoomDirection = ZoomDirection, PanDirection = PanDirection };
}

/// <summary>Zooms into a dragged rectangle.</summary>
public sealed class RubberBandZoomModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="Direction"/> property.</summary>
    public static readonly StyledProperty<ChartDirection> DirectionProperty = AvaloniaProperty.Register<RubberBandZoomModifier, ChartDirection>(nameof(Direction), ChartDirection.Both);

    /// <summary>Identifies the <see cref="ModifierKey"/> property.</summary>
    public static readonly StyledProperty<InputModifiers> ModifierKeyProperty = AvaloniaProperty.Register<RubberBandZoomModifier, InputModifiers>(nameof(ModifierKey), InputModifiers.Control);

    /// <summary>Gets or sets the directions of the rectangle.</summary>
    public ChartDirection Direction
    {
        get => GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    /// <summary>Gets or sets the keys to hold when dragging.</summary>
    public InputModifiers ModifierKey
    {
        get => GetValue(ModifierKeyProperty);
        set => SetValue(ModifierKeyProperty, value);
    }

    internal override ChartModifier Build() => new Core.Input.RubberBandZoomModifier { Direction = Direction, ModifierKey = ModifierKey };
}

/// <summary>Moves and scales single axes.</summary>
public sealed class AxisDragModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.AxisDragModifier();
}

/// <summary>An interaction created in code, for special chart types.</summary>
public sealed class CustomModifier : ChartModifierElement
{
    /// <summary>Gets or sets what creates the modifier. A chart can call it again.</summary>
    public Func<ChartModifier>? Factory { get; set; }

    internal override ChartModifier Build() => (Factory ?? throw new InvalidOperationException("A custom modifier needs a factory."))();
}

/// <summary>Selects points.</summary>
public sealed class SelectionModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="Mode"/> property.</summary>
    public static readonly StyledProperty<SelectionMode> ModeProperty = AvaloniaProperty.Register<SelectionModifier, SelectionMode>(nameof(Mode), SelectionMode.Rectangle);

    /// <summary>Gets or sets the kind of area.</summary>
    public SelectionMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Occurs when the selected points change.</summary>
    public event Action<IReadOnlyList<SeriesSelection>>? SelectionChanged;

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.SelectionModifier { Mode = Mode };
        modifier.SelectionChanged += selection => SelectionChanged?.Invoke(selection);
        return modifier;
    }
}

/// <summary>Measures between two cursors.</summary>
public sealed class MeasureCursorModifier : ChartModifierElement
{
    /// <summary>Occurs when the measurement changes.</summary>
    public event Action<MeasureResult?>? Changed;

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.MeasureCursorModifier();
        modifier.Changed += result => Changed?.Invoke(result);
        return modifier;
    }
}

/// <summary>Navigates with the keyboard.</summary>
public sealed class KeyboardNavigationModifier : ChartModifierElement
{
    /// <summary>Gets the keys that replace the default ones.</summary>
    public Dictionary<ChartKeyAction, KeyBinding> Bindings { get; } = [];

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.KeyboardNavigationModifier();
        foreach (var (action, binding) in Bindings)
        {
            modifier.Bindings[action] = binding;
        }

        return modifier;
    }
}

/// <summary>Shows a legend.</summary>
public sealed class LegendModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.LegendModifier();
}

/// <summary>Turns this chart into the minimap of another one.</summary>
public sealed class OverviewModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="Target"/> property.</summary>
    public static readonly StyledProperty<Chart?> TargetProperty = AvaloniaProperty.Register<OverviewModifier, Chart?>(nameof(Target));

    /// <summary>Gets or sets the chart that this one navigates.</summary>
    public Chart? Target
    {
        get => GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    // The render thread asks too, and properties belong to the UI thread: it keeps the chart as a copy.
    internal override ChartModifier Build()
    {
        var target = Target;
        return new Core.Input.OverviewModifier(() => target?.Interaction);
    }
}

/// <summary>Shows a crosshair.</summary>
public sealed class CrosshairModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="SnapToData"/> property.</summary>
    public static readonly StyledProperty<bool> SnapToDataProperty = AvaloniaProperty.Register<CrosshairModifier, bool>(nameof(SnapToData));

    /// <summary>Gets or sets a value indicating whether the crosshair jumps to the nearest point.</summary>
    public bool SnapToData
    {
        get => GetValue(SnapToDataProperty);
        set => SetValue(SnapToDataProperty, value);
    }

    internal override ChartModifier Build() => new Core.Input.CrosshairModifier { SnapToData = SnapToData };
}

/// <summary>Shows the values of all series at the pointer.</summary>
public sealed class RolloverModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.RolloverModifier();
}

/// <summary>Switches the diagnostics with F12.</summary>
public sealed class DiagnosticsModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.DiagnosticsModifier();
}

/// <summary>Moves annotations with the pointer.</summary>
public sealed class AnnotationEditModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.AnnotationEditModifier();
}

/// <summary>Shows the point under the pointer in a popup, which lies above a native child window too.</summary>
public sealed class TooltipModifier : ChartModifierElement
{
    private const double CursorOffset = 16;

    private readonly TextBlock _text = new() { Foreground = Brushes.WhiteSmoke, FontSize = 12 };
    private readonly Border _border = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(235, 24, 28, 36)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 4, 8, 4),
        CornerRadius = new CornerRadius(3),
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private readonly global::Avalonia.Controls.Primitives.Popup _popup;

    /// <summary>Initializes a new instance of the <see cref="TooltipModifier"/> class.</summary>
    public TooltipModifier()
    {
        _border.Child = _text;
        _popup = new global::Avalonia.Controls.Primitives.Popup
        {
            Child = _border,
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopLeft,
            PlacementGravity = PopupGravity.BottomRight,
            IsHitTestVisible = false,
        };
    }

    internal bool IsOpen => _popup.IsOpen;

    internal string? Text => _text.Text;

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.TooltipModifier();
        modifier.Changed += OnChanged;
        return modifier;
    }

    private void OnChanged(TooltipData? data)
    {
        if (data is not { } tooltip)
        {
            _popup.IsOpen = false;
            return;
        }

        _text.Text = tooltip.SeriesName is { Length: > 0 } name
            ? $"{name}{Environment.NewLine}{tooltip.XText}{Environment.NewLine}{tooltip.YText}"
            : $"{tooltip.XText}{Environment.NewLine}{tooltip.YText}";
        _border.BorderBrush = new SolidColorBrush(Color.FromRgb(tooltip.Color.R, tooltip.Color.G, tooltip.Color.B));
        _popup.PlacementTarget = Parent as Control;
        _popup.PlacementRect = new Rect(new Point(tooltip.Position.X + CursorOffset, tooltip.Position.Y + CursorOffset), new Size(1, 1));
        _popup.IsOpen = true;
    }
}

/// <summary>Turns the description of a chart into the elements XAML would declare.</summary>
internal static class ChartElementFactory
{
    public static ChartAxis Create(AxisDefinition definition)
    {
        ChartAxis axis = definition.Kind switch
        {
            AxisKind.DateTime => new DateTimeAxis(),
            AxisKind.Category => new CategoryAxis { Categories = definition.Categories },
            AxisKind.Log => new LogAxis { Base = definition.LogBase, NonPositive = definition.NonPositive },
            _ => new NumericAxis(),
        };
        axis.Id = definition.Id;
        axis.Title = definition.Title;
        axis.Position = definition.Position;
        axis.Limits = definition.Limits;
        axis.IsInverted = definition.IsInverted;
        axis.IsHidden = definition.IsHidden;
        if (definition.Range is { } range)
        {
            axis.VisibleRange = range;
        }

        axis.IsAutoRange = definition.IsAutoRange;
        return axis;
    }

    public static ChartSeriesElement Create(SeriesDefinition definition)
    {
        ChartSeriesElement series = definition switch
        {
            CustomSeriesDefinition custom => new CustomSeries { Factory = custom.Factory },
            LineDefinition line => new LineSeries
            {
                Stroke = ToColor(line.Color),
                StrokeThickness = line.Thickness,
                StrokeDashArray = line.DashPattern,
                IsStep = line.IsStep,
                IsSpline = line.IsSpline,
                GlowRadius = line.GlowRadius,
                GlowIntensity = line.GlowIntensity,
            },
            ScatterDefinition scatter => new ScatterSeries
            {
                Fill = ToColor(scatter.Color),
                Marker = scatter.Shape,
                MarkerSize = scatter.Size,
                PointSizes = scatter.PointSizes,
                DensityColormap = scatter.DensityAxis?.Colormap,
                DensityThreshold = scatter.DensityThreshold,
            },
            ContourDefinition contour => Raster(new ContourSeries
            {
                Levels = contour.Levels,
                IsFilled = contour.IsFilled,
                ShowsLines = contour.ShowsLines,
                XStep = contour.XStep,
                YStep = contour.YStep,
            }, contour),
            SpectrogramDefinition spectrogram => Raster(new SpectrogramSeries
            {
                XStart = spectrogram.XStart,
                XStep = spectrogram.XStep,
                YStep = spectrogram.YStep,
            }, spectrogram),
            HeatmapDefinition heatmap => Raster(new HeatmapSeries
            {
                XStart = heatmap.XStart,
                XStep = heatmap.XStep,
                YStart = heatmap.YStart,
                YStep = heatmap.YStep,
                IsInterpolated = heatmap.IsInterpolated,
            }, heatmap),
            _ => throw new NotSupportedException($"{definition.GetType().Name} has no element."),
        };

        series.SeriesName = definition.Name;
        series.ShowsLastValue = definition.ShowsLastValue;
        series.ShowsValues = definition.ShowsValues;
        series.DataSource = definition.Source;
        series.XAxisId = definition.XAxisId;
        series.YAxisId = definition.YAxisId;
        return series;
    }

    public static ChartModifierElement Create(ModifierDefinition definition) => definition switch
    {
        ZoomPanDefinition zoomPan => new ZoomPanModifier { ZoomDirection = zoomPan.ZoomDirection, PanDirection = zoomPan.PanDirection },
        RubberBandDefinition band => new RubberBandZoomModifier { Direction = band.Direction, ModifierKey = band.ModifierKey },
        AxisDragDefinition => new AxisDragModifier(),
        CustomModifierDefinition custom => new CustomModifier { Factory = custom.Factory },
        DiagnosticsDefinition => new DiagnosticsModifier(),
        CrosshairDefinition crosshair => new CrosshairModifier { SnapToData = crosshair.SnapToData },
        RolloverDefinition => new RolloverModifier(),
        TooltipDefinition => new TooltipModifier(),
        AnnotationEditDefinition => new AnnotationEditModifier(),
        LegendDefinition => new LegendModifier(),
        KeyboardNavigationDefinition => new KeyboardNavigationModifier(),
        MeasureCursorDefinition => new MeasureCursorModifier(),
        SelectionDefinition selection => new SelectionModifier { Mode = selection.Mode },
        _ => throw new NotSupportedException($"{definition.GetType().Name} has no element."),
    };

    private static RasterSeriesElement Raster(RasterSeriesElement series, RasterDefinition definition)
    {
        series.Colormap = definition.ColorAxis.Colormap;
        series.IsLogarithmic = definition.ColorAxis.IsLogarithmic;
        series.ColorRange = definition.ColorAxis.Range;
        return series;
    }

    private static Color ToColor(Rgba8 color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}
