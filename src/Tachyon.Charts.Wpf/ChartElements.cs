using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using CoreDateTimeAxis = Tachyon.Charts.Core.Axes.DateTimeAxis;
using CoreNumericAxis = Tachyon.Charts.Core.Axes.NumericAxis;
using KeyBinding = Tachyon.Charts.Core.Input.KeyBinding;

namespace Tachyon.Charts.Wpf;

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
/// An element declared inside a <see cref="Chart"/>. It is part of the logical tree, so bindings see
/// the data context of the chart.
/// </summary>
public abstract class ChartElement : FrameworkContentElement
{
    internal IChartElementHost? Host { get; set; }
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
    public static readonly DependencyProperty IdProperty = DependencyProperty.Register(
        nameof(Id), typeof(string), typeof(ChartAxis), new PropertyMetadata(null, OnStructureChanged));

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ChartAxis), new PropertyMetadata(null, OnStateChanged));

    /// <summary>Identifies the <see cref="Position"/> property.</summary>
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(AxisPosition?), typeof(ChartAxis), new PropertyMetadata(null, OnStructureChanged));

    /// <summary>Identifies the <see cref="VisibleRange"/> property.</summary>
    public static readonly DependencyProperty VisibleRangeProperty = DependencyProperty.Register(
        nameof(VisibleRange),
        typeof(DoubleRange),
        typeof(ChartAxis),
        new FrameworkPropertyMetadata(DoubleRange.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged));

    /// <summary>Identifies the <see cref="Limits"/> property.</summary>
    public static readonly DependencyProperty LimitsProperty = DependencyProperty.Register(
        nameof(Limits), typeof(DoubleRange?), typeof(ChartAxis), new PropertyMetadata(null, OnStateChanged));

    /// <summary>Identifies the <see cref="IsAutoRange"/> property.</summary>
    public static readonly DependencyProperty IsAutoRangeProperty = DependencyProperty.Register(
        nameof(IsAutoRange),
        typeof(bool),
        typeof(ChartAxis),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnStateChanged));

    /// <summary>Identifies the <see cref="IsInverted"/> property.</summary>
    public static readonly DependencyProperty IsInvertedProperty = DependencyProperty.Register(
        nameof(IsInverted), typeof(bool), typeof(ChartAxis), new PropertyMetadata(false, OnStateChanged));

    private bool _isPublishing;

    /// <summary>Gets or sets the name series use to refer to this axis.</summary>
    public string? Id
    {
        get => (string?)GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    /// <summary>Gets or sets the title.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the side of the chart. By default X axes sit at the bottom and Y axes on the left.
    /// </summary>
    public AxisPosition? Position
    {
        get => (AxisPosition?)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the visible range. It follows zooming and panning, and binds both ways by default.
    /// An empty range leaves the axis as it is. A time axis counts from its origin.
    /// </summary>
    public DoubleRange VisibleRange
    {
        get => (DoubleRange)GetValue(VisibleRangeProperty);
        set => SetValue(VisibleRangeProperty, value);
    }

    /// <summary>Gets or sets the bounds of zooming and panning, or <see langword="null"/> for none.</summary>
    public DoubleRange? Limits
    {
        get => (DoubleRange?)GetValue(LimitsProperty);
        set => SetValue(LimitsProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the range follows the data.</summary>
    public bool IsAutoRange
    {
        get => (bool)GetValue(IsAutoRangeProperty);
        set => SetValue(IsAutoRangeProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the axis runs against its usual direction.</summary>
    public bool IsInverted
    {
        get => (bool)GetValue(IsInvertedProperty);
        set => SetValue(IsInvertedProperty, value);
    }

    internal AxisBase? Core { get; private set; }

    /// <summary>Creates the axis of the engine for a position.</summary>
    /// <param name="id">The axis name.</param>
    /// <param name="position">The position.</param>
    /// <returns>The axis.</returns>
    private protected abstract AxisBase CreateCore(string id, AxisPosition position);

    /// <summary>Gets or sets a value indicating whether the axis takes no room and shows nothing.</summary>
    public bool IsHidden { get; set; }

    internal AxisBase Build(string fallbackId, AxisPosition fallbackPosition)
    {
        var core = CreateCore(Id ?? fallbackId, Position ?? fallbackPosition);
        var state = ReadState();
        state.ApplyTo(core);
        core.IsHidden = IsHidden;
        Core = core;
        return core;
    }

    /// <summary>
    /// Takes over a state of the engine without sending it back there.
    /// </summary>
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

    /// <summary>Reports a change that needs a new axis.</summary>
    /// <param name="sender">The element.</param>
    /// <param name="args">The change.</param>
    private protected static void OnStructureChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ChartAxis)sender).Host?.OnAxesChanged();

    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var axis = (ChartAxis)sender;
        if (axis._isPublishing || axis.Host is not { } host || axis.Core is not { } core)
        {
            return;
        }

        // Like on the engine axis, a range set from outside ends the automatic range.
        if (args.Property == VisibleRangeProperty)
        {
            axis.SetCurrentValue(IsAutoRangeProperty, false);
        }

        var state = axis.ReadState();
        host.Post(() => state.ApplyTo(core));
    }

    private AxisState ReadState() => new(Title, VisibleRange, Limits, IsAutoRange, IsInverted);

    // Dependency properties belong to the UI thread, so a change travels as a copy.
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

/// <summary>
/// An axis with numbers.
/// </summary>
public sealed class NumericAxis : ChartAxis
{
    private protected override AxisBase CreateCore(string id, AxisPosition position) => new CoreNumericAxis(id, position);
}

/// <summary>
/// An axis with a logarithmic scale.
/// </summary>
public sealed class LogAxis : ChartAxis
{
    /// <summary>Identifies the <see cref="Base"/> property.</summary>
    public static readonly DependencyProperty BaseProperty = DependencyProperty.Register(
        nameof(Base), typeof(double), typeof(LogAxis), new PropertyMetadata(10.0, OnStructureChanged));

    /// <summary>Identifies the <see cref="NonPositive"/> property.</summary>
    public static readonly DependencyProperty NonPositiveProperty = DependencyProperty.Register(
        nameof(NonPositive), typeof(NonPositiveValues), typeof(LogAxis), new PropertyMetadata(NonPositiveValues.Hide, OnStructureChanged));

    /// <summary>Gets or sets the base, greater than one.</summary>
    public double Base
    {
        get => (double)GetValue(BaseProperty);
        set => SetValue(BaseProperty, value);
    }

    /// <summary>Gets or sets what happens to values at or below zero.</summary>
    public NonPositiveValues NonPositive
    {
        get => (NonPositiveValues)GetValue(NonPositiveProperty);
        set => SetValue(NonPositiveProperty, value);
    }

    private protected override AxisBase CreateCore(string id, AxisPosition position) =>
        new Core.Axes.LogAxis(id, position) { Base = Base, NonPositive = NonPositive };
}

/// <summary>
/// An axis with times. It follows the data by default, because a fixed range needs an origin.
/// </summary>
public sealed class DateTimeAxis : ChartAxis
{
    /// <summary>Initializes a new instance of the <see cref="DateTimeAxis"/> class.</summary>
    public DateTimeAxis()
    {
        SetCurrentValue(IsAutoRangeProperty, true);
    }

    private protected override AxisBase CreateCore(string id, AxisPosition position) => new CoreDateTimeAxis(id, position);
}

/// <summary>
/// A series of a <see cref="Chart"/>.
/// </summary>
public abstract class ChartSeriesElement : ChartElement
{
    private ItemsDataSource? _items;

    /// <summary>Identifies the <see cref="DataSource"/> property.</summary>
    public static readonly DependencyProperty DataSourceProperty = DependencyProperty.Register(
        nameof(DataSource), typeof(object), typeof(ChartSeriesElement), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies the <see cref="SeriesName"/> property.</summary>
    public static readonly DependencyProperty SeriesNameProperty = DependencyProperty.Register(
        nameof(SeriesName), typeof(string), typeof(ChartSeriesElement), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies the <see cref="XAxisId"/> property.</summary>
    public static readonly DependencyProperty XAxisIdProperty = DependencyProperty.Register(
        nameof(XAxisId), typeof(string), typeof(ChartSeriesElement), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies the <see cref="YAxisId"/> property.</summary>
    public static readonly DependencyProperty YAxisIdProperty = DependencyProperty.Register(
        nameof(YAxisId), typeof(string), typeof(ChartSeriesElement), new PropertyMetadata(null, OnChanged));

    /// <summary>Gets or sets the name shown in rollovers and legends.</summary>
    public string? SeriesName
    {
        get => (string?)GetValue(SeriesNameProperty);
        set => SetValue(SeriesNameProperty, value);
    }

    /// <summary>Gets or sets the data, an <see cref="IDataSource{TX, TY}"/>.</summary>
    public object? DataSource
    {
        get => GetValue(DataSourceProperty);
        set => SetValue(DataSourceProperty, value);
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
        get => (string?)GetValue(XAxisIdProperty);
        set => SetValue(XAxisIdProperty, value);
    }

    /// <summary>Gets or sets the name of the Y axis, or <see langword="null"/> for the first one.</summary>
    public string? YAxisId
    {
        get => (string?)GetValue(YAxisIdProperty);
        set => SetValue(YAxisIdProperty, value);
    }

    /// <summary>
    /// Creates the series of the engine.
    /// </summary>
    /// <param name="x">The X axis.</param>
    /// <param name="y">The Y axis.</param>
    /// <returns>The series, or <see langword="null"/> without data.</returns>
    /// <exception cref="InvalidOperationException">The data is not a data source.</exception>
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

    /// <summary>Gets the value scale of the series, created on first use, or <see langword="null"/> without one.</summary>
    /// <param name="id">The identifier of a new axis.</param>
    /// <returns>The axis.</returns>
    internal virtual Core.Axes.ColorAxis? GetColorAxis(string id) => null;

    /// <summary>Creates the series for the value types of the data.</summary>
    private protected abstract ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y);

    /// <summary>Reports a change that needs a new series.</summary>
    /// <param name="sender">The element.</param>
    /// <param name="args">The change.</param>
    private protected static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ChartSeriesElement)sender).Host?.OnSeriesChanged();

    /// <summary>Converts a color.</summary>
    /// <param name="color">The WPF color.</param>
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

/// <summary>
/// A line through the points of a data source.
/// </summary>
public sealed class LineSeries : ChartSeriesElement
{
    /// <summary>Identifies the <see cref="Stroke"/> property.</summary>
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Color), typeof(LineSeries), new PropertyMetadata(Color.FromRgb(80, 170, 240), OnChanged));

    /// <summary>Identifies the <see cref="StrokeThickness"/> property.</summary>
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(LineSeries), new PropertyMetadata(1.5, OnChanged));

    /// <summary>Identifies the <see cref="StrokeDashArray"/> property.</summary>
    public static readonly DependencyProperty StrokeDashArrayProperty = DependencyProperty.Register(
        nameof(StrokeDashArray), typeof(DoubleCollection), typeof(LineSeries), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies the <see cref="IsStep"/> property.</summary>
    public static readonly DependencyProperty IsStepProperty = DependencyProperty.Register(
        nameof(IsStep), typeof(bool), typeof(LineSeries), new PropertyMetadata(false, OnChanged));

    /// <summary>Gets or sets the line color.</summary>
    public Color Stroke
    {
        get => (Color)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>Gets or sets the thickness in device-independent units.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    /// <summary>Gets or sets the lengths of dashes and spaces in device-independent units.</summary>
    public DoubleCollection? StrokeDashArray
    {
        get => (DoubleCollection?)GetValue(StrokeDashArrayProperty);
        set => SetValue(StrokeDashArrayProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the line keeps each value until the next point.</summary>
    public bool IsStep
    {
        get => (bool)GetValue(IsStepProperty);
        set => SetValue(IsStepProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the line runs as a smooth curve.</summary>
    public bool IsSpline { get; set; }

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
            DashPattern = StrokeDashArray is { Count: > 0 } dashes ? [.. dashes.Select(length => (float)length)] : null,
            IsStep = IsStep,
            IsSpline = IsSpline,
        };
}

/// <summary>
/// A series that shows values as colors, with a color bar beside the plot.
/// </summary>
public abstract class RasterSeriesElement : ChartSeriesElement
{
    /// <summary>Identifies the <see cref="Colormap"/> property.</summary>
    public static readonly DependencyProperty ColormapProperty = DependencyProperty.Register(
        nameof(Colormap), typeof(Colormap), typeof(RasterSeriesElement), new PropertyMetadata(Colormaps.Viridis, OnScaleChanged));

    /// <summary>Identifies the <see cref="IsLogarithmic"/> property.</summary>
    public static readonly DependencyProperty IsLogarithmicProperty = DependencyProperty.Register(
        nameof(IsLogarithmic), typeof(bool), typeof(RasterSeriesElement), new PropertyMetadata(false, OnScaleChanged));

    /// <summary>Identifies the <see cref="ColorRange"/> property.</summary>
    public static readonly DependencyProperty ColorRangeProperty = DependencyProperty.Register(
        nameof(ColorRange), typeof(DoubleRange?), typeof(RasterSeriesElement), new PropertyMetadata(null, OnScaleChanged));

    private Core.Axes.ColorAxis? _colorAxis;

    /// <summary>Gets or sets the range of the colors, or <see langword="null"/> to follow the data. A fixed range keeps long runs free of new layouts.</summary>
    public DoubleRange? ColorRange
    {
        get => (DoubleRange?)GetValue(ColorRangeProperty);
        set => SetValue(ColorRangeProperty, value);
    }

    /// <summary>Gets or sets the colors. A change only replaces the table of the renderer.</summary>
    public Colormap Colormap
    {
        get => (Colormap)GetValue(ColormapProperty);
        set => SetValue(ColormapProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether colors follow the logarithm of the values.</summary>
    public bool IsLogarithmic
    {
        get => (bool)GetValue(IsLogarithmicProperty);
        set => SetValue(IsLogarithmicProperty, value);
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

    private protected override ChartSeries BuildTyped(object source, Type[] valueTypes, AxisBase x, AxisBase y) =>
        throw new NotSupportedException("A raster has no value types.");

    // The axis object stays, so that the series and the texture of values stay as well.
    private static void OnScaleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var series = (RasterSeriesElement)sender;
        if (series._colorAxis is { } axis)
        {
            series.GetColorAxis(axis.Id);
        }
    }
}

/// <summary>
/// Isolines of a grid, optionally with filled bands.
/// </summary>
public sealed class ContourSeries : RasterSeriesElement
{
    /// <summary>Identifies the <see cref="Levels"/> property.</summary>
    public static readonly DependencyProperty LevelsProperty = DependencyProperty.Register(
        nameof(Levels), typeof(DoubleCollection), typeof(ContourSeries), new PropertyMetadata(null, OnChanged));

    /// <summary>Identifies the <see cref="IsFilled"/> property.</summary>
    public static readonly DependencyProperty IsFilledProperty = DependencyProperty.Register(
        nameof(IsFilled), typeof(bool), typeof(ContourSeries), new PropertyMetadata(false, OnChanged));

    /// <summary>Identifies the <see cref="ShowsLines"/> property.</summary>
    public static readonly DependencyProperty ShowsLinesProperty = DependencyProperty.Register(
        nameof(ShowsLines), typeof(bool), typeof(ContourSeries), new PropertyMetadata(true, OnChanged));

    /// <summary>Identifies the <see cref="XStep"/> property.</summary>
    public static readonly DependencyProperty XStepProperty = DependencyProperty.Register(
        nameof(XStep), typeof(double), typeof(ContourSeries), new PropertyMetadata(1.0, OnChanged));

    /// <summary>Identifies the <see cref="YStep"/> property.</summary>
    public static readonly DependencyProperty YStepProperty = DependencyProperty.Register(
        nameof(YStep), typeof(double), typeof(ContourSeries), new PropertyMetadata(1.0, OnChanged));

    /// <summary>Gets or sets the levels, or <see langword="null"/> for equal steps.</summary>
    public DoubleCollection? Levels
    {
        get => (DoubleCollection?)GetValue(LevelsProperty);
        set => SetValue(LevelsProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the bands between the lines are filled.</summary>
    public bool IsFilled
    {
        get => (bool)GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether the lines are drawn.</summary>
    public bool ShowsLines
    {
        get => (bool)GetValue(ShowsLinesProperty);
        set => SetValue(ShowsLinesProperty, value);
    }

    /// <summary>Gets or sets the width of a column.</summary>
    public double XStep
    {
        get => (double)GetValue(XStepProperty);
        set => SetValue(XStepProperty, value);
    }

    /// <summary>Gets or sets the height of a row.</summary>
    public double YStep
    {
        get => (double)GetValue(YStepProperty);
        set => SetValue(YStepProperty, value);
    }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y)
    {
        if (DataSource is null)
        {
            return null;
        }

        if (DataSource is not HeatmapData data)
        {
            throw new InvalidOperationException($"{DataSource.GetType().Name} is not a grid.");
        }

        return new Core.Series.ContourSeries(data, x, y, GetColorAxis(string.Empty))
        {
            Name = SeriesName,
            Levels = Levels is { Count: > 0 } levels ? [.. levels] : null,
            IsFilled = IsFilled,
            ShowsLines = ShowsLines,
            XStep = XStep,
            YStep = YStep,
        };
    }
}

/// <summary>
/// A waterfall of rows, fed by <see cref="SpectrogramData.AppendRow"/>.
/// </summary>
public sealed class SpectrogramSeries : RasterSeriesElement
{
    /// <summary>Identifies the <see cref="XStart"/> property.</summary>
    public static readonly DependencyProperty XStartProperty = DependencyProperty.Register(
        nameof(XStart), typeof(double), typeof(SpectrogramSeries), new PropertyMetadata(0.0, OnChanged));

    /// <summary>Identifies the <see cref="XStep"/> property.</summary>
    public static readonly DependencyProperty XStepProperty = DependencyProperty.Register(
        nameof(XStep), typeof(double), typeof(SpectrogramSeries), new PropertyMetadata(1.0, OnChanged));

    /// <summary>Identifies the <see cref="YStep"/> property.</summary>
    public static readonly DependencyProperty YStepProperty = DependencyProperty.Register(
        nameof(YStep), typeof(double), typeof(SpectrogramSeries), new PropertyMetadata(1.0, OnChanged));

    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart
    {
        get => (double)GetValue(XStartProperty);
        set => SetValue(XStartProperty, value);
    }

    /// <summary>Gets or sets the width of a column.</summary>
    public double XStep
    {
        get => (double)GetValue(XStepProperty);
        set => SetValue(XStepProperty, value);
    }

    /// <summary>Gets or sets the height of a row.</summary>
    public double YStep
    {
        get => (double)GetValue(YStepProperty);
        set => SetValue(YStepProperty, value);
    }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y)
    {
        if (DataSource is null)
        {
            return null;
        }

        if (DataSource is not SpectrogramData data)
        {
            throw new InvalidOperationException($"{DataSource.GetType().Name} is not spectrogram data.");
        }

        return new Core.Series.SpectrogramSeries(data, x, y, GetColorAxis(string.Empty))
        {
            Name = SeriesName,
            XStart = XStart,
            XStep = XStep,
            YStep = YStep,
        };
    }
}

/// <summary>
/// A grid of values shown as colors, with a color bar beside the plot.
/// </summary>
public sealed class HeatmapSeries : RasterSeriesElement
{
    /// <summary>Identifies the <see cref="XStart"/> property.</summary>
    public static readonly DependencyProperty XStartProperty = DependencyProperty.Register(
        nameof(XStart), typeof(double), typeof(HeatmapSeries), new PropertyMetadata(0.0, OnChanged));

    /// <summary>Identifies the <see cref="XStep"/> property.</summary>
    public static readonly DependencyProperty XStepProperty = DependencyProperty.Register(
        nameof(XStep), typeof(double), typeof(HeatmapSeries), new PropertyMetadata(1.0, OnChanged));

    /// <summary>Identifies the <see cref="YStart"/> property.</summary>
    public static readonly DependencyProperty YStartProperty = DependencyProperty.Register(
        nameof(YStart), typeof(double), typeof(HeatmapSeries), new PropertyMetadata(0.0, OnChanged));

    /// <summary>Identifies the <see cref="YStep"/> property.</summary>
    public static readonly DependencyProperty YStepProperty = DependencyProperty.Register(
        nameof(YStep), typeof(double), typeof(HeatmapSeries), new PropertyMetadata(1.0, OnChanged));

    /// <summary>Identifies the <see cref="IsInterpolated"/> property.</summary>
    public static readonly DependencyProperty IsInterpolatedProperty = DependencyProperty.Register(
        nameof(IsInterpolated), typeof(bool), typeof(HeatmapSeries), new PropertyMetadata(false, OnChanged));

    /// <summary>Gets or sets the left edge of the first column.</summary>
    public double XStart
    {
        get => (double)GetValue(XStartProperty);
        set => SetValue(XStartProperty, value);
    }

    /// <summary>Gets or sets the width of a column.</summary>
    public double XStep
    {
        get => (double)GetValue(XStepProperty);
        set => SetValue(XStepProperty, value);
    }

    /// <summary>Gets or sets the lower edge of the first row.</summary>
    public double YStart
    {
        get => (double)GetValue(YStartProperty);
        set => SetValue(YStartProperty, value);
    }

    /// <summary>Gets or sets the height of a row.</summary>
    public double YStep
    {
        get => (double)GetValue(YStepProperty);
        set => SetValue(YStepProperty, value);
    }

    /// <summary>Gets or sets a value indicating whether adjacent cell colors are blended.</summary>
    public bool IsInterpolated
    {
        get => (bool)GetValue(IsInterpolatedProperty);
        set => SetValue(IsInterpolatedProperty, value);
    }

    /// <inheritdoc />
    internal override ChartSeries? Build(AxisBase x, AxisBase y)
    {
        if (DataSource is null)
        {
            return null;
        }

        if (DataSource is not HeatmapData data)
        {
            throw new InvalidOperationException($"{DataSource.GetType().Name} is not a heatmap grid.");
        }

        return new Core.Series.HeatmapSeries(data, x, y, GetColorAxis(string.Empty))
        {
            Name = SeriesName,
            XStart = XStart,
            XStep = XStep,
            YStart = YStart,
            YStep = YStep,
            IsInterpolated = IsInterpolated,
        };
    }
}

/// <summary>
/// Markers at the points of a data source.
/// </summary>
public sealed class ScatterSeries : ChartSeriesElement
{
    /// <summary>Identifies the <see cref="DensityColormap"/> property.</summary>
    public static readonly DependencyProperty DensityColormapProperty = DependencyProperty.Register(
        nameof(DensityColormap), typeof(Colormap), typeof(ScatterSeries), new PropertyMetadata(null, OnDensityChanged));

    /// <summary>Identifies the <see cref="DensityThreshold"/> property.</summary>
    public static readonly DependencyProperty DensityThresholdProperty = DependencyProperty.Register(
        nameof(DensityThreshold), typeof(double), typeof(ScatterSeries), new PropertyMetadata(1.0, OnChanged));

    private Core.Axes.ColorAxis? _densityAxis;

    /// <summary>Gets or sets the colors of the density map, or <see langword="null"/> for markers only.</summary>
    public Colormap? DensityColormap
    {
        get => (Colormap?)GetValue(DensityColormapProperty);
        set => SetValue(DensityColormapProperty, value);
    }

    /// <summary>Gets or sets the visible points per pixel from which the density map shows.</summary>
    public double DensityThreshold
    {
        get => (double)GetValue(DensityThresholdProperty);
        set => SetValue(DensityThresholdProperty, value);
    }

    /// <summary>Identifies the <see cref="Fill"/> property.</summary>
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Color), typeof(ScatterSeries), new PropertyMetadata(Color.FromRgb(240, 130, 90), OnChanged));

    /// <summary>Identifies the <see cref="Marker"/> property.</summary>
    public static readonly DependencyProperty MarkerProperty = DependencyProperty.Register(
        nameof(Marker), typeof(MarkerShape), typeof(ScatterSeries), new PropertyMetadata(MarkerShape.Circle, OnChanged));

    /// <summary>Identifies the <see cref="MarkerSize"/> property.</summary>
    public static readonly DependencyProperty MarkerSizeProperty = DependencyProperty.Register(
        nameof(MarkerSize), typeof(double), typeof(ScatterSeries), new PropertyMetadata(6.0, OnChanged));

    /// <summary>Gets or sets the marker color.</summary>
    public Color Fill
    {
        get => (Color)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    /// <summary>Gets or sets the marker shape.</summary>
    public MarkerShape Marker
    {
        get => (MarkerShape)GetValue(MarkerProperty);
        set => SetValue(MarkerProperty, value);
    }

    /// <summary>Gets or sets the marker size in device-independent units.</summary>
    public double MarkerSize
    {
        get => (double)GetValue(MarkerSizeProperty);
        set => SetValue(MarkerSizeProperty, value);
    }

    /// <summary>Gets or sets one size per point, for bubbles.</summary>
    public ReadOnlyMemory<float> PointSizes { get; set; }

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

    private static void OnDensityChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var series = (ScatterSeries)sender;
        if (series._densityAxis is { } axis)
        {
            series.GetColorAxis(axis.Id);
        }
        else
        {
            series.Host?.OnSeriesChanged();
        }
    }
}

/// <summary>
/// An interaction of a <see cref="Chart"/>.
/// </summary>
public abstract class ChartModifierElement : ChartElement
{
    /// <summary>Creates the modifier of the engine.</summary>
    /// <returns>The modifier.</returns>
    internal abstract ChartModifier Build();

    /// <summary>Reports a change that needs a new modifier.</summary>
    /// <param name="sender">The element.</param>
    /// <param name="args">The change.</param>
    private protected static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ChartModifierElement)sender).Host?.OnModifiersChanged();
}

/// <summary>
/// Zooms with the wheel around the cursor and pans by dragging.
/// </summary>
public sealed class ZoomPanModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="ZoomDirection"/> property.</summary>
    public static readonly DependencyProperty ZoomDirectionProperty = DependencyProperty.Register(
        nameof(ZoomDirection), typeof(ChartDirection), typeof(ZoomPanModifier), new PropertyMetadata(ChartDirection.Both, OnChanged));

    /// <summary>Identifies the <see cref="PanDirection"/> property.</summary>
    public static readonly DependencyProperty PanDirectionProperty = DependencyProperty.Register(
        nameof(PanDirection), typeof(ChartDirection), typeof(ZoomPanModifier), new PropertyMetadata(ChartDirection.Both, OnChanged));

    /// <summary>Gets or sets the directions of zooming.</summary>
    public ChartDirection ZoomDirection
    {
        get => (ChartDirection)GetValue(ZoomDirectionProperty);
        set => SetValue(ZoomDirectionProperty, value);
    }

    /// <summary>Gets or sets the directions of panning.</summary>
    public ChartDirection PanDirection
    {
        get => (ChartDirection)GetValue(PanDirectionProperty);
        set => SetValue(PanDirectionProperty, value);
    }

    internal override ChartModifier Build() => new Core.Input.ZoomPanModifier { ZoomDirection = ZoomDirection, PanDirection = PanDirection };
}

/// <summary>
/// Zooms into a dragged rectangle.
/// </summary>
public sealed class RubberBandZoomModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="Direction"/> property.</summary>
    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(ChartDirection), typeof(RubberBandZoomModifier), new PropertyMetadata(ChartDirection.Both, OnChanged));

    /// <summary>Identifies the <see cref="ModifierKey"/> property.</summary>
    public static readonly DependencyProperty ModifierKeyProperty = DependencyProperty.Register(
        nameof(ModifierKey), typeof(ModifierKeys), typeof(RubberBandZoomModifier), new PropertyMetadata(ModifierKeys.Control, OnChanged));

    /// <summary>Gets or sets the directions of the rectangle.</summary>
    public ChartDirection Direction
    {
        get => (ChartDirection)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    /// <summary>Gets or sets the keys to hold when dragging.</summary>
    public ModifierKeys ModifierKey
    {
        get => (ModifierKeys)GetValue(ModifierKeyProperty);
        set => SetValue(ModifierKeyProperty, value);
    }

    internal override ChartModifier Build() => new Core.Input.RubberBandZoomModifier
    {
        Direction = Direction,
        ModifierKey = WpfInput.ToModifiers(ModifierKey),
    };
}

/// <summary>
/// Pans and scales a single axis, and lets it follow the data after a double click.
/// </summary>
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

/// <summary>
/// Selects points by click, rectangle or lasso while holding Shift.
/// </summary>
public sealed class SelectionModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="Mode"/> property.</summary>
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(SelectionMode), typeof(SelectionModifier), new PropertyMetadata(SelectionMode.Rectangle, OnChanged));

    /// <summary>Gets or sets how points are selected.</summary>
    public SelectionMode Mode
    {
        get => (SelectionMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    /// <summary>Occurs when the selection changed.</summary>
    public event Action<IReadOnlyList<SeriesSelection>>? SelectionChanged;

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.SelectionModifier { Mode = Mode };
        modifier.SelectionChanged += selection => SelectionChanged?.Invoke(selection);
        return modifier;
    }
}

/// <summary>
/// Measures between two cursors while holding Alt.
/// </summary>
public sealed class MeasureCursorModifier : ChartModifierElement
{
    /// <summary>Occurs when the measurement changed.</summary>
    public event Action<MeasureResult?>? Changed;

    internal override ChartModifier Build()
    {
        var modifier = new Core.Input.MeasureCursorModifier();
        modifier.Changed += result => Changed?.Invoke(result);
        return modifier;
    }
}

/// <summary>
/// Navigates with the keyboard: arrows, plus, minus, Home, Ctrl+Z and Ctrl+Y.
/// </summary>
public sealed class KeyboardNavigationModifier : ChartModifierElement
{
    /// <summary>Gets the changed keys, which replace the defaults of their actions.</summary>
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

/// <summary>
/// Shows a legend. A click switches a series on or off, the pointer highlights it.
/// </summary>
public sealed class LegendModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.LegendModifier();
}

/// <summary>
/// Turns this chart into the overview of another one. Dragging the window moves the other chart,
/// dragging its edges scales it, and a click outside centers it there.
/// </summary>
public sealed class OverviewModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="Target"/> property.</summary>
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(Chart), typeof(OverviewModifier), new PropertyMetadata(null, OnChanged));

    /// <summary>Gets or sets the chart this one gives an overview of.</summary>
    public Chart? Target
    {
        get => (Chart?)GetValue(TargetProperty);
        set => SetValue(TargetProperty, value);
    }

    // The target can start later or get a new engine chart, so it is looked up on every use. The render
    // thread asks too, and dependency properties belong to the UI thread: it keeps the chart as a copy.
    internal override ChartModifier Build()
    {
        var target = Target;
        return new Core.Input.OverviewModifier(() => target?.Interaction);
    }
}

/// <summary>
/// Shows a crosshair at the pointer.
/// </summary>
public sealed class CrosshairModifier : ChartModifierElement
{
    /// <summary>Identifies the <see cref="SnapToData"/> property.</summary>
    public static readonly DependencyProperty SnapToDataProperty = DependencyProperty.Register(
        nameof(SnapToData), typeof(bool), typeof(CrosshairModifier), new PropertyMetadata(false, OnChanged));

    /// <summary>Gets or sets a value indicating whether the crosshair jumps to the nearest point.</summary>
    public bool SnapToData
    {
        get => (bool)GetValue(SnapToDataProperty);
        set => SetValue(SnapToDataProperty, value);
    }

    internal override ChartModifier Build() => new Core.Input.CrosshairModifier { SnapToData = SnapToData };
}

/// <summary>
/// Shows a vertical line with the values of all series at the pointer.
/// </summary>
public sealed class RolloverModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.RolloverModifier();
}

/// <summary>
/// Switches the diagnostics overlay with F12. The chart needs the keyboard focus for that.
/// </summary>
public sealed class DiagnosticsModifier : ChartModifierElement
{
    internal override ChartModifier Build() => new Core.Input.DiagnosticsModifier();
}

/// <summary>
/// Refers to a built-in theme in XAML.
/// </summary>
[MarkupExtensionReturnType(typeof(Theme))]
public sealed class ThemeExtension : MarkupExtension
{
    /// <summary>Initializes a new instance of the <see cref="ThemeExtension"/> class.</summary>
    public ThemeExtension()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ThemeExtension"/> class.</summary>
    /// <param name="name">The name of the theme.</param>
    public ThemeExtension(string name)
    {
        Name = name;
    }

    /// <summary>Gets or sets the name of the theme, for example <c>TachyonDark</c>.</summary>
    [ConstructorArgument("name")]
    public string? Name { get; set; }

    /// <inheritdoc />
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        (Name switch
        {
            "TachyonDark" => BuiltInThemes.TachyonDark,
            "TachyonLight" => BuiltInThemes.TachyonLight,
            _ => BuiltInThemes.Find(Name ?? string.Empty),
        }) ?? throw new InvalidOperationException($"There is no theme '{Name}'.");
}

/// <summary>
/// Keeps elements in the logical tree and connected to a host.
/// </summary>
internal static class ElementCollections
{
    public static void Track<T>(ChartElementCollection<T> elements, Action<object> added, Action<object> removed, Action changed)
        where T : ChartElement
    {
        var known = new List<T>();
        elements.CollectionChanged += (_, _) =>
        {
            foreach (var element in known.Except(elements).ToArray())
            {
                known.Remove(element);
                removed(element);
            }

            foreach (var element in elements.Except(known).ToArray())
            {
                known.Add(element);
                added(element);
            }

            changed();
        };
    }
}

/// <summary>
/// Turns the description of a chart into the elements XAML would declare.
/// </summary>
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
                StrokeDashArray = line.DashPattern is { } dashes ? [.. dashes.Select(length => (double)length)] : null,
                IsStep = line.IsStep,
                IsSpline = line.IsSpline,
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
            ContourDefinition contour => new ContourSeries
            {
                Colormap = contour.ColorAxis.Colormap,
                IsLogarithmic = contour.ColorAxis.IsLogarithmic,
                ColorRange = contour.ColorAxis.Range,
                Levels = contour.Levels is { } levels ? [.. levels] : null,
                IsFilled = contour.IsFilled,
                ShowsLines = contour.ShowsLines,
                XStep = contour.XStep,
                YStep = contour.YStep,
            },
            SpectrogramDefinition spectrogram => new SpectrogramSeries
            {
                Colormap = spectrogram.ColorAxis.Colormap,
                IsLogarithmic = spectrogram.ColorAxis.IsLogarithmic,
                ColorRange = spectrogram.ColorAxis.Range,
                XStart = spectrogram.XStart,
                XStep = spectrogram.XStep,
                YStep = spectrogram.YStep,
            },
            HeatmapDefinition heatmap => new HeatmapSeries
            {
                Colormap = heatmap.ColorAxis.Colormap,
                IsLogarithmic = heatmap.ColorAxis.IsLogarithmic,
                ColorRange = heatmap.ColorAxis.Range,
                XStart = heatmap.XStart,
                XStep = heatmap.XStep,
                YStart = heatmap.YStart,
                YStep = heatmap.YStep,
                IsInterpolated = heatmap.IsInterpolated,
            },
            _ => throw new NotSupportedException($"{definition.GetType().Name} has no element."),
        };

        series.SeriesName = definition.Name;
        series.DataSource = definition.Source;
        series.XAxisId = definition.XAxisId;
        series.YAxisId = definition.YAxisId;
        return series;
    }

    public static ChartModifierElement Create(ModifierDefinition definition) => definition switch
    {
        ZoomPanDefinition zoomPan => new ZoomPanModifier { ZoomDirection = zoomPan.ZoomDirection, PanDirection = zoomPan.PanDirection },
        RubberBandDefinition band => new RubberBandZoomModifier { Direction = band.Direction, ModifierKey = WpfInput.ToModifierKeys(band.ModifierKey) },
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

    private static Color ToColor(Rgba8 color) => Color.FromArgb(color.A, color.R, color.G, color.B);
}
