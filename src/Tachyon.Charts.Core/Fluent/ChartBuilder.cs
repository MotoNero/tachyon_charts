using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Fluent;

/// <summary>The kind of an axis.</summary>
public enum AxisKind
{
    /// <summary>An axis with numbers.</summary>
    Numeric,

    /// <summary>An axis with times.</summary>
    DateTime,

    /// <summary>An axis with a logarithmic scale.</summary>
    Log,

    /// <summary>An axis with names instead of numbers.</summary>
    Category,
}

/// <summary>A described axis.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Id">The name series use.</param>
public sealed record AxisDefinition(AxisKind Kind, string Id)
{
    /// <summary>Gets the names of a category axis.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    /// <summary>Gets the title.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the side of the chart, or <see langword="null"/> for the usual one.</summary>
    public AxisPosition? Position { get; init; }

    /// <summary>Gets the visible range, or <see langword="null"/> to leave it open.</summary>
    public DoubleRange? Range { get; init; }

    /// <summary>Gets the bounds of zooming and panning.</summary>
    public DoubleRange? Limits { get; init; }

    /// <summary>Gets a value indicating whether the range follows the data.</summary>
    public bool IsAutoRange { get; init; }

    /// <summary>Gets a value indicating whether the axis runs against its usual direction.</summary>
    public bool IsInverted { get; init; }

    /// <summary>Gets a value indicating whether the axis takes no room and shows nothing.</summary>
    public bool IsHidden { get; init; }

    /// <summary>Gets the pane of a vertical axis, counted from the top; 0 is the first.</summary>
    public int Pane { get; init; }

    /// <summary>Gets the height of the pane relative to the others.</summary>
    public double PaneWeight { get; init; } = 1;

    /// <summary>Gets the value of the other axis where this axis runs through the plot, or <see langword="null"/> for the edge.</summary>
    public double? CrossesAt { get; init; }

    /// <summary>Gets the ranges a numeric axis leaves out.</summary>
    public IReadOnlyList<DoubleRange> Breaks { get; init; } = [];

    /// <summary>Gets the hidden times of a time axis, or <see langword="null"/> for none.</summary>
    public TimeGapMap? Gaps { get; init; }

    /// <summary>Gets the base of a logarithmic axis.</summary>
    public double LogBase { get; init; } = 10;

    /// <summary>Gets what a logarithmic axis does with values at or below zero.</summary>
    public NonPositiveValues NonPositive { get; init; }
}

/// <summary>A described series.</summary>
public abstract record SeriesDefinition
{
    /// <summary>Gets the data, an <see cref="IDataSource{TX, TY}"/>.</summary>
    public required object Source { get; init; }

    /// <summary>Gets the name shown in rollovers and legends.</summary>
    public string? Name { get; init; }

    /// <summary>Gets a value indicating whether the newest value shows as a mark at the vertical axis.</summary>
    public bool ShowsLastValue { get; init; }

    /// <summary>Gets the name of the X axis, or <see langword="null"/> for the first one.</summary>
    public string? XAxisId { get; init; }

    /// <summary>Gets the name of the Y axis, or <see langword="null"/> for the first one.</summary>
    public string? YAxisId { get; init; }

    /// <summary>Gets the value scale of the series, if it has one.</summary>
    internal virtual ColorAxisDefinition? ColorScale => null;

    /// <summary>Creates the series with its value types.</summary>
    internal abstract ChartSeries Create(AxisBase x, AxisBase y);

    /// <summary>Creates the series with its value scale.</summary>
    internal virtual ChartSeries Create(AxisBase x, AxisBase y, ColorAxis? colorAxis) => Create(x, y);
}

/// <summary>A described value scale.</summary>
public sealed record ColorAxisDefinition
{
    /// <summary>Gets the colors.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Viridis;

    /// <summary>Gets a value indicating whether colors follow the logarithm of the values.</summary>
    public bool IsLogarithmic { get; init; }

    /// <summary>Gets the fixed range, or <see langword="null"/> to follow the data.</summary>
    public DoubleRange? Range { get; init; }

    /// <summary>Gets the color of missing values.</summary>
    public Rgba8 MissingColor { get; init; }

    /// <summary>Gets the title.</summary>
    public string? Title { get; init; }

    internal ColorAxis Create(string id)
    {
        var axis = new ColorAxis(id) { Colormap = Colormap, IsLogarithmic = IsLogarithmic, MissingColor = MissingColor, Title = Title };
        if (Range is { } range)
        {
            axis.VisibleRange = range;
        }

        return axis;
    }
}

/// <summary>A described series with a value scale.</summary>
public abstract record RasterDefinition : SeriesDefinition
{
    /// <summary>Gets the value scale.</summary>
    public ColorAxisDefinition ColorAxis { get; init; } = new();

    /// <inheritdoc />
    internal override ColorAxisDefinition? ColorScale => ColorAxis;

    /// <inheritdoc />
    internal sealed override ChartSeries Create(AxisBase x, AxisBase y) =>
        throw new InvalidOperationException("A raster needs its color axis.");

    /// <inheritdoc />
    internal abstract override ChartSeries Create(AxisBase x, AxisBase y, ColorAxis? colorAxis);
}

/// <summary>A described heatmap.</summary>
public sealed record HeatmapDefinition : RasterDefinition
{
    /// <summary>Gets the left edge of the first column.</summary>
    public double XStart { get; init; }

    /// <summary>Gets the width of a column.</summary>
    public double XStep { get; init; } = 1;

    /// <summary>Gets the lower edge of the first row.</summary>
    public double YStart { get; init; }

    /// <summary>Gets the height of a row.</summary>
    public double YStep { get; init; } = 1;

    /// <summary>Gets the edges of the columns, or <see langword="null"/> for equal columns.</summary>
    public IReadOnlyList<double>? XEdges { get; init; }

    /// <summary>Gets the polygons that cut the heatmap out, or <see langword="null"/> for all cells.</summary>
    public IReadOnlyList<Data.Polygon>? Mask { get; init; }

    /// <summary>Gets the edges of the rows, or <see langword="null"/> for equal rows.</summary>
    public IReadOnlyList<double>? YEdges { get; init; }

    /// <inheritdoc />
    internal override ChartSeries Create(AxisBase x, AxisBase y, ColorAxis? colorAxis) => new HeatmapSeries((HeatmapData)Source, x, y, colorAxis!)
    {
        XStart = XStart,
        XStep = XStep,
        YStart = YStart,
        YStep = YStep,
        XEdges = XEdges,
        Mask = Mask,
        YEdges = YEdges,
    };
}

/// <summary>A described spectrogram.</summary>
public sealed record SpectrogramDefinition : RasterDefinition
{
    /// <summary>Gets the left edge of the first column.</summary>
    public double XStart { get; init; }

    /// <summary>Gets the width of a column.</summary>
    public double XStep { get; init; } = 1;

    /// <summary>Gets the height of a row.</summary>
    public double YStep { get; init; } = 1;

    /// <inheritdoc />
    internal override ChartSeries Create(AxisBase x, AxisBase y, ColorAxis? colorAxis) => new SpectrogramSeries((SpectrogramData)Source, x, y, colorAxis!)
    {
        XStart = XStart,
        XStep = XStep,
        YStep = YStep,
    };
}

/// <summary>A described contour.</summary>
public sealed record ContourDefinition : RasterDefinition
{
    /// <summary>Gets the levels, or <see langword="null"/> for equal steps.</summary>
    public IReadOnlyList<double>? Levels { get; init; }

    /// <summary>Gets a value indicating whether the bands are filled.</summary>
    public bool IsFilled { get; init; }

    /// <summary>Gets a value indicating whether the lines are drawn.</summary>
    public bool ShowsLines { get; init; } = true;

    /// <summary>Gets a value indicating whether the lines carry their values.</summary>
    public bool ShowsLabels { get; init; }

    /// <summary>Gets the left edge of the first column.</summary>
    public double XStart { get; init; }

    /// <summary>Gets the width of a column.</summary>
    public double XStep { get; init; } = 1;

    /// <summary>Gets the lower edge of the first row.</summary>
    public double YStart { get; init; }

    /// <summary>Gets the height of a row.</summary>
    public double YStep { get; init; } = 1;

    /// <inheritdoc />
    internal override ChartSeries Create(AxisBase x, AxisBase y, ColorAxis? colorAxis) => new ContourSeries((HeatmapData)Source, x, y, colorAxis!)
    {
        Levels = Levels,
        IsFilled = IsFilled,
        ShowsLines = ShowsLines,
        ShowsLabels = ShowsLabels,
        XStart = XStart,
        XStep = XStep,
        YStart = YStart,
        YStep = YStep,
    };
}

/// <summary>A series the caller creates, for all series without a description of their own.</summary>
public sealed record CustomSeriesDefinition : SeriesDefinition
{
    /// <summary>Gets what creates the series from its axes.</summary>
    public required Func<AxisBase, AxisBase, ChartSeries> Factory { get; init; }

    /// <inheritdoc />
    internal override ChartSeries Create(AxisBase x, AxisBase y) => Factory(x, y);
}

/// <summary>A described line.</summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
public sealed record LineDefinition<TX, TY> : LineDefinition
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
{
    internal override ChartSeries Create(AxisBase x, AxisBase y) => new LineSeries<TX, TY>((IDataSource<TX, TY>)Source, x, y)
    {
        Color = Color,
        Thickness = Thickness,
        DashPattern = DashPattern,
        IsStep = IsStep,
        IsSpline = IsSpline,
    };
}

/// <summary>A described line, independent of its value types.</summary>
public abstract record LineDefinition : SeriesDefinition
{
    /// <summary>Gets the line color.</summary>
    public Rgba8 Color { get; init; } = new(80, 170, 240, 255);

    /// <summary>Gets the thickness in device-independent units.</summary>
    public double Thickness { get; init; } = 1.5;

    /// <summary>Gets the lengths of dashes and spaces, or <see langword="null"/> for a solid line.</summary>
    public IReadOnlyList<float>? DashPattern { get; init; }

    /// <summary>Gets a value indicating whether the line keeps each value until the next point.</summary>
    public bool IsStep { get; init; }

    /// <summary>Gets a value indicating whether the line runs as a smooth curve.</summary>
    public bool IsSpline { get; init; }
}

/// <summary>Described markers.</summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
public sealed record ScatterDefinition<TX, TY> : ScatterDefinition
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    internal override ChartSeries Create(AxisBase x, AxisBase y) => Create(x, y, null);

    /// <inheritdoc />
    internal override ChartSeries Create(AxisBase x, AxisBase y, ColorAxis? colorAxis)
    {
        if (colorAxis is not null)
        {
            colorAxis.BelowColor = default(Rgba8);
        }

        return new ScatterSeries<TX, TY>((IDataSource<TX, TY>)Source, x, y)
        {
            Color = Color,
            Shape = Shape,
            Size = Size,
            PointSizes = PointSizes,
            DensityAxis = colorAxis,
            DensityThreshold = DensityThreshold,
        };
    }
}

/// <summary>Described markers, independent of their value types.</summary>
public abstract record ScatterDefinition : SeriesDefinition
{
    /// <summary>Gets the marker color.</summary>
    public Rgba8 Color { get; init; } = new(240, 130, 90, 255);

    /// <summary>Gets the marker shape.</summary>
    public MarkerShape Shape { get; init; } = MarkerShape.Circle;

    /// <summary>Gets the marker size in device-independent units.</summary>
    public double Size { get; init; } = 6;

    /// <summary>Gets one diameter per point for bubbles, or nothing for equal markers.</summary>
    public ReadOnlyMemory<float> PointSizes { get; init; }

    /// <summary>Gets the scale of the density map, or <see langword="null"/> for markers only.</summary>
    public ColorAxisDefinition? DensityAxis { get; init; }

    /// <summary>Gets the visible points per pixel from which the density shows.</summary>
    public double DensityThreshold { get; init; } = 1;

    /// <inheritdoc />
    internal override ColorAxisDefinition? ColorScale => DensityAxis;
}

/// <summary>A described interaction.</summary>
public abstract record ModifierDefinition
{
    /// <summary>Creates the modifier.</summary>
    /// <returns>The modifier.</returns>
    public abstract ChartModifier Create();
}

/// <summary>An interaction the caller creates.</summary>
/// <param name="Factory">Creates the modifier. A chart can call it again.</param>
public sealed record CustomModifierDefinition(Func<ChartModifier> Factory) : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => Factory();
}

/// <summary>Zooming with the wheel and panning by dragging.</summary>
/// <param name="ZoomDirection">The directions of zooming.</param>
/// <param name="PanDirection">The directions of panning.</param>
public sealed record ZoomPanDefinition(ChartDirection ZoomDirection = ChartDirection.Both, ChartDirection PanDirection = ChartDirection.Both) : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new ZoomPanModifier { ZoomDirection = ZoomDirection, PanDirection = PanDirection };
}

/// <summary>Zooming into a dragged rectangle.</summary>
/// <param name="ModifierKey">The keys to hold when dragging.</param>
/// <param name="Direction">The directions of the rectangle.</param>
public sealed record RubberBandDefinition(InputModifiers ModifierKey = InputModifiers.Control, ChartDirection Direction = ChartDirection.Both) : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new RubberBandZoomModifier { ModifierKey = ModifierKey, Direction = Direction };
}

/// <summary>Panning and scaling a single axis.</summary>
public sealed record AxisDragDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new AxisDragModifier();
}

/// <summary>A crosshair at the pointer.</summary>
/// <param name="SnapToData">Whether it jumps to the nearest point.</param>
public sealed record CrosshairDefinition(bool SnapToData = false) : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new CrosshairModifier { SnapToData = SnapToData };
}

/// <summary>A vertical line with the values of all series.</summary>
public sealed record RolloverDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new RolloverModifier();
}

/// <summary>A tooltip with the nearest point.</summary>
public sealed record TooltipDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new TooltipModifier();
}

/// <summary>Editing of annotations.</summary>
public sealed record AnnotationEditDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new AnnotationEditModifier();
}

/// <summary>Selection of points.</summary>
/// <param name="Mode">How points are selected.</param>
public sealed record SelectionDefinition(SelectionMode Mode = SelectionMode.Rectangle) : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new SelectionModifier { Mode = Mode };
}

/// <summary>Two measuring cursors.</summary>
public sealed record MeasureCursorDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new MeasureCursorModifier();
}

/// <summary>Navigation with the keyboard.</summary>
public sealed record KeyboardNavigationDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new KeyboardNavigationModifier();
}

/// <summary>A legend.</summary>
public sealed record LegendDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new LegendModifier();
}

/// <summary>The diagnostics overlay on a key.</summary>
public sealed record DiagnosticsDefinition : ModifierDefinition
{
    /// <inheritdoc />
    public override ChartModifier Create() => new DiagnosticsModifier();
}

/// <summary>A running chart made from a <see cref="ChartDefinition"/>.</summary>
/// <param name="Surface">The chart.</param>
/// <param name="Modifiers">The interactions, to add to a <see cref="ModifierPipeline"/>.</param>
public sealed record BuiltChart(ChartSurface Surface, IReadOnlyList<ChartModifier> Modifiers);

/// <summary>
/// The description of a chart, independent of any UI framework. XAML hosts turn it into their elements.
/// </summary>
/// <param name="Title">The chart title.</param>
/// <param name="Theme">The theme.</param>
/// <param name="XAxes">The horizontal axes.</param>
/// <param name="YAxes">The vertical axes.</param>
/// <param name="Series">The series.</param>
/// <param name="Modifiers">The interactions.</param>
public sealed record ChartDefinition(
    string? Title,
    Theme? Theme,
    IReadOnlyList<AxisDefinition> XAxes,
    IReadOnlyList<AxisDefinition> YAxes,
    IReadOnlyList<SeriesDefinition> Series,
    IReadOnlyList<ModifierDefinition> Modifiers)
{
    /// <summary>Gets the name of the group this chart joins, or <see langword="null"/> for none.</summary>
    public string? GroupName { get; init; }

    /// <summary>Gets what creates the overlays. A chart can call it again, for example after a device loss.</summary>
    public IReadOnlyList<Func<ChartSurface, Overlays.ChartOverlay>> Overlays { get; init; } = [];

    /// <summary>Creates a chart without a UI framework.</summary>
    /// <param name="textEngine">The text engine.</param>
    /// <returns>The chart.</returns>
    /// <exception cref="InvalidOperationException">An axis is missing.</exception>
    public BuiltChart Build(ITextEngine textEngine)
    {
        var xAxes = XAxes.Select(axis => CreateAxis(axis, AxisPosition.Bottom)).ToArray();
        var yAxes = YAxes.Select(axis => CreateAxis(axis, AxisPosition.Left)).ToArray();
        var colorAxes = Series.Where(series => series.ColorScale is not null).Select((series, index) => (Series: series, Axis: series.ColorScale!.Create($"color{index}"))).ToArray();
        var surface = new ChartSurface(xAxes, CreatePanes(yAxes, colorAxes.Select(item => item.Axis)), textEngine) { Title = Title, Theme = Theme };
        foreach (var series in Series)
        {
            var x = Find(xAxes, series.XAxisId, "X");
            var y = Find(yAxes, series.YAxisId, "Y");
            var created = series.Create(x, y, Array.Find(colorAxes, item => ReferenceEquals(item.Series, series)).Axis);
            created.Name = series.Name;
            created.ShowsLastValue |= series.ShowsLastValue;
            surface.AddSeries(created);
        }

        foreach (var overlay in Overlays)
        {
            surface.AddOverlay(overlay(surface));
        }

        var modifiers = Modifiers.Select(modifier => modifier.Create()).ToList();
        if (colorAxes.Length > 0 && !modifiers.OfType<AxisDragModifier>().Any())
        {
            modifiers.Add(new AxisDragModifier { OnlyColorAxes = true });
        }

        return new BuiltChart(surface, modifiers);
    }

    // Vertical axes go into the pane they name, from the top; color axes join the first pane.
    private LayoutPane[] CreatePanes(AxisBase[] yAxes, IEnumerable<AxisBase> colorAxes)
    {
        var count = YAxes.Count == 0 ? 1 : YAxes.Max(axis => axis.Pane) + 1;
        var panes = new LayoutPane[count];
        for (var pane = 0; pane < count; pane++)
        {
            var axes = new List<AxisBase>();
            var weight = 1.0;
            for (var i = 0; i < yAxes.Length; i++)
            {
                if (YAxes[i].Pane == pane)
                {
                    axes.Add(yAxes[i]);
                    weight = YAxes[i].PaneWeight;
                }
            }

            if (pane == 0)
            {
                axes.AddRange(colorAxes);
            }

            panes[pane] = new LayoutPane(axes, weight);
        }

        return panes;
    }

    private static AxisBase CreateAxis(AxisDefinition definition, AxisPosition fallback)
    {
        var position = definition.Position ?? fallback;
        AxisBase axis = definition.Kind switch
        {
            AxisKind.DateTime => new DateTimeAxis(definition.Id, position) { Gaps = definition.Gaps },
            AxisKind.Category => new CategoryAxis(definition.Id, position, definition.Categories),
            AxisKind.Log => new LogAxis(definition.Id, position) { Base = definition.LogBase, NonPositive = definition.NonPositive },
            _ => new NumericAxis(definition.Id, position) { Breaks = definition.Breaks },
        };
        axis.Title = definition.Title;
        axis.Limits = definition.Limits;
        axis.IsInverted = definition.IsInverted;
        if (definition.Range is { } range)
        {
            axis.VisibleRange = range;
        }

        axis.IsAutoRange = definition.IsAutoRange;
        axis.IsHidden = definition.IsHidden;
        axis.CrossesAt = definition.CrossesAt;
        return axis;
    }

    private static AxisBase Find(AxisBase[] axes, string? id, string kind)
    {
        var axis = id is null ? axes.FirstOrDefault() : Array.Find(axes, candidate => candidate.Id == id);
        return axis ?? throw new InvalidOperationException($"The chart has no {kind} axis '{id}'.");
    }
}

/// <summary>Describes a legend.</summary>
public sealed class LegendBuilder
{
    internal Input.LegendPlacement Placement { get; private set; }

    internal string? TitleText { get; private set; }

    internal IReadOnlyList<string>? Names { get; private set; }

    /// <summary>Sets where the legend sits.</summary>
    /// <param name="placement">The place.</param>
    /// <returns>This builder.</returns>
    public LegendBuilder Place(Input.LegendPlacement placement)
    {
        Placement = placement;
        return this;
    }

    /// <summary>Sets a title above the rows.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public LegendBuilder Title(string title)
    {
        TitleText = title;
        return this;
    }

    /// <summary>Shows only the series with these names.</summary>
    /// <param name="names">The names.</param>
    /// <returns>This builder.</returns>
    public LegendBuilder Series(params IReadOnlyList<string> names)
    {
        Names = names;
        return this;
    }
}

/// <summary>Describes a value scale.</summary>
public sealed class ColorAxisBuilder
{
    internal ColorAxisDefinition Definition { get; private set; } = new();

    /// <summary>Sets the colors.</summary>
    /// <param name="colormap">The colors.</param>
    /// <returns>This builder.</returns>
    public ColorAxisBuilder Colormap(Colormap colormap) => Change(Definition with { Colormap = colormap });

    /// <summary>Lets colors follow the logarithm of the values.</summary>
    /// <returns>This builder.</returns>
    public ColorAxisBuilder Log() => Change(Definition with { IsLogarithmic = true });

    /// <summary>Fixes the range.</summary>
    /// <param name="minimum">The lowest value.</param>
    /// <param name="maximum">The highest value.</param>
    /// <returns>This builder.</returns>
    public ColorAxisBuilder Range(double minimum, double maximum) => Change(Definition with { Range = new DoubleRange(minimum, maximum) });

    /// <summary>Sets the color of missing values.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    public ColorAxisBuilder Missing(Rgba8 color) => Change(Definition with { MissingColor = color });

    /// <summary>Sets the title.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public ColorAxisBuilder Title(string title) => Change(Definition with { Title = title });

    private ColorAxisBuilder Change(ColorAxisDefinition definition)
    {
        Definition = definition;
        return this;
    }
}

/// <summary>Describes a heatmap.</summary>
public sealed class HeatmapBuilder
{
    internal HeatmapBuilder(HeatmapData data)
    {
        Definition = new HeatmapDefinition { Source = data };
    }

    internal HeatmapDefinition Definition { get; private set; }

    /// <summary>Sets the name shown in rollovers and legends.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder.</returns>
    public HeatmapBuilder Name(string name) => Change(Definition with { Name = name });

    /// <summary>Sets equal cells.</summary>
    /// <param name="xStart">The left edge of the first column.</param>
    /// <param name="xStep">The width of a column.</param>
    /// <param name="yStart">The lower edge of the first row.</param>
    /// <param name="yStep">The height of a row.</param>
    /// <returns>This builder.</returns>
    public HeatmapBuilder Cells(double xStart, double xStep, double yStart, double yStep) =>
        Change(Definition with { XStart = xStart, XStep = xStep, YStart = yStart, YStep = yStep });

    /// <summary>Sets cells of their own sizes.</summary>
    /// <param name="xEdges">The edges of the columns, or <see langword="null"/> for equal columns.</param>
    /// <param name="yEdges">The edges of the rows, or <see langword="null"/> for equal rows.</param>
    /// <returns>This builder.</returns>
    public HeatmapBuilder Edges(IReadOnlyList<double>? xEdges, IReadOnlyList<double>? yEdges) =>
        Change(Definition with { XEdges = xEdges, YEdges = yEdges });

    /// <summary>Shows only the cells inside polygons, for example the shape of a region.</summary>
    /// <param name="polygons">The polygons.</param>
    /// <returns>This builder.</returns>
    public HeatmapBuilder Mask(IReadOnlyList<Data.Polygon> polygons) => Change(Definition with { Mask = polygons });

    /// <summary>Describes the value scale.</summary>
    /// <param name="configure">Describes the scale.</param>
    /// <returns>This builder.</returns>
    public HeatmapBuilder ColorAxis(Action<ColorAxisBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ColorAxisBuilder();
        configure(builder);
        return Change(Definition with { ColorAxis = builder.Definition });
    }

    private HeatmapBuilder Change(HeatmapDefinition definition)
    {
        Definition = definition;
        return this;
    }
}

/// <summary>Describes a spectrogram.</summary>
public sealed class SpectrogramBuilder
{
    internal SpectrogramBuilder(SpectrogramData data)
    {
        Definition = new SpectrogramDefinition { Source = data };
    }

    internal SpectrogramDefinition Definition { get; private set; }

    /// <summary>Sets the name shown in rollovers and legends.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder.</returns>
    public SpectrogramBuilder Name(string name) => Change(Definition with { Name = name });

    /// <summary>Sets the size of the cells.</summary>
    /// <param name="xStart">The left edge of the first column.</param>
    /// <param name="xStep">The width of a column.</param>
    /// <param name="yStep">The height of a row.</param>
    /// <returns>This builder.</returns>
    public SpectrogramBuilder Cells(double xStart, double xStep, double yStep) =>
        Change(Definition with { XStart = xStart, XStep = xStep, YStep = yStep });

    /// <summary>Describes the value scale.</summary>
    /// <param name="configure">Describes the scale.</param>
    /// <returns>This builder.</returns>
    public SpectrogramBuilder ColorAxis(Action<ColorAxisBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ColorAxisBuilder();
        configure(builder);
        return Change(Definition with { ColorAxis = builder.Definition });
    }

    private SpectrogramBuilder Change(SpectrogramDefinition definition)
    {
        Definition = definition;
        return this;
    }
}

/// <summary>Describes a contour.</summary>
public sealed class ContourBuilder
{
    internal ContourBuilder(HeatmapData data)
    {
        Definition = new ContourDefinition { Source = data };
    }

    internal ContourDefinition Definition { get; private set; }

    /// <summary>Sets the name shown in rollovers and legends.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder.</returns>
    public ContourBuilder Name(string name) => Change(Definition with { Name = name });

    /// <summary>Sets the levels.</summary>
    /// <param name="levels">The levels.</param>
    /// <returns>This builder.</returns>
    public ContourBuilder Levels(params IReadOnlyList<double> levels) => Change(Definition with { Levels = levels });

    /// <summary>Fills the bands between the lines.</summary>
    /// <returns>This builder.</returns>
    public ContourBuilder Filled() => Change(Definition with { IsFilled = true });

    /// <summary>Leaves out the lines.</summary>
    /// <returns>This builder.</returns>
    public ContourBuilder WithoutLines() => Change(Definition with { ShowsLines = false });

    /// <summary>Writes the value of each line along the line.</summary>
    /// <returns>This builder.</returns>
    public ContourBuilder Labels() => Change(Definition with { ShowsLabels = true });

    /// <summary>Sets the size of the cells.</summary>
    /// <param name="xStart">The left edge of the first column.</param>
    /// <param name="xStep">The width of a column.</param>
    /// <param name="yStart">The lower edge of the first row.</param>
    /// <param name="yStep">The height of a row.</param>
    /// <returns>This builder.</returns>
    public ContourBuilder Cells(double xStart, double xStep, double yStart, double yStep) =>
        Change(Definition with { XStart = xStart, XStep = xStep, YStart = yStart, YStep = yStep });

    /// <summary>Describes the value scale.</summary>
    /// <param name="configure">Describes the scale.</param>
    /// <returns>This builder.</returns>
    public ContourBuilder ColorAxis(Action<ColorAxisBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ColorAxisBuilder();
        configure(builder);
        return Change(Definition with { ColorAxis = builder.Definition });
    }

    private ContourBuilder Change(ContourDefinition definition)
    {
        Definition = definition;
        return this;
    }
}

/// <summary>Describes an axis.</summary>
public sealed class AxisBuilder
{
    private AxisDefinition _definition;

    internal AxisBuilder(string fallbackId)
    {
        _definition = new AxisDefinition(AxisKind.Numeric, fallbackId);
    }

    internal AxisDefinition Definition => _definition;

    /// <summary>Makes this an axis with numbers.</summary>
    /// <param name="id">The name series use, or <see langword="null"/> for a generated one.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Numeric(string? id = null) => Change(_definition with { Kind = AxisKind.Numeric, Id = id ?? _definition.Id });

    /// <summary>Makes this an axis with times, which follows the data unless a range is set.</summary>
    /// <param name="id">The name series use, or <see langword="null"/> for a generated one.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder DateTime(string? id = null) => Change(_definition with { Kind = AxisKind.DateTime, Id = id ?? _definition.Id, IsAutoRange = true });

    /// <summary>Makes this an axis with a logarithmic scale.</summary>
    /// <param name="id">The name series use, or <see langword="null"/> for a generated one.</param>
    /// <param name="logBase">The base, greater than one.</param>
    /// <param name="nonPositive">What happens to values at or below zero.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Log(string? id = null, double logBase = 10, NonPositiveValues nonPositive = NonPositiveValues.Hide) =>
        Change(_definition with { Kind = AxisKind.Log, Id = id ?? _definition.Id, LogBase = logBase, NonPositive = nonPositive });

    /// <summary>Makes this an axis with names instead of numbers.</summary>
    /// <param name="categories">The names of the places.</param>
    /// <param name="id">The name of the axis.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Category(IReadOnlyList<string> categories, string? id = null) =>
        Change(_definition with { Kind = AxisKind.Category, Id = id ?? _definition.Id, Categories = categories });

    /// <summary>Sets the title.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Title(string title) => Change(_definition with { Title = title });

    /// <summary>Sets the side of the chart.</summary>
    /// <param name="position">The side.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Position(AxisPosition position) => Change(_definition with { Position = position });

    /// <summary>Sets the visible range, which ends following the data.</summary>
    /// <param name="min">The start.</param>
    /// <param name="max">The end.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Range(double min, double max) => Change(_definition with { Range = new DoubleRange(min, max), IsAutoRange = false });

    /// <summary>Sets the bounds of zooming and panning.</summary>
    /// <param name="min">The start.</param>
    /// <param name="max">The end.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Limits(double min, double max) => Change(_definition with { Limits = new DoubleRange(min, max) });

    /// <summary>Lets the range follow the data.</summary>
    /// <returns>This builder.</returns>
    public AxisBuilder AutoRange() => Change(_definition with { IsAutoRange = true });

    /// <summary>Runs the axis against its usual direction.</summary>
    /// <returns>This builder.</returns>
    public AxisBuilder Inverted() => Change(_definition with { IsInverted = true });

    /// <summary>Hides the axis: it takes no room and shows nothing.</summary>
    /// <returns>This builder.</returns>
    public AxisBuilder Hidden() => Change(Definition with { IsHidden = true });

    /// <summary>Lets the axis run through the plot where the other axis has a value, for example 0 for axes in the origin.</summary>
    /// <param name="value">The value of the other axis.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder CrossesAt(double value) => Change(Definition with { CrossesAt = value });

    /// <summary>Leaves ranges out of a numeric axis; they take no room and are marked.</summary>
    /// <param name="breaks">The ranges to leave out.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Breaks(params DoubleRange[] breaks) => Change(Definition with { Breaks = [.. breaks] });

    /// <summary>Hides times on a time axis, for example nights and weekends; they take no room and are marked.</summary>
    /// <param name="gaps">The hidden times.</param>
    /// <returns>This builder.</returns>
    public AxisBuilder Gaps(TimeGapMap gaps) => Change(Definition with { Gaps = gaps ?? throw new ArgumentNullException(nameof(gaps)) });

    /// <summary>Puts a vertical axis and its series into a pane of their own, below the first one.</summary>
    /// <param name="index">The pane, counted from the top; 0 is the first.</param>
    /// <param name="weight">The height of the pane relative to the others.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is negative or the weight not positive.</exception>
    public AxisBuilder Pane(int index, double weight = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(weight, 0);
        return Change(Definition with { Pane = index, PaneWeight = weight });
    }

    private AxisBuilder Change(AxisDefinition definition)
    {
        _definition = definition;
        return this;
    }
}

/// <summary>Describes a line.</summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
public sealed class LineBuilder<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
{
    internal LineBuilder(IDataSource<TX, TY> source)
    {
        Definition = new LineDefinition<TX, TY> { Source = source };
    }

    internal LineDefinition<TX, TY> Definition { get; private set; }

    /// <summary>Selects the X axis.</summary>
    /// <param name="id">The name of the axis.</param>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> XAxis(string id) => Change(Definition with { XAxisId = id });

    /// <summary>Sets the name shown in rollovers and legends.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> Name(string name) => Change(Definition with { Name = name });

    /// <summary>Shows the newest value as a mark at the vertical axis, live.</summary>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> LastValue() => Change(Definition with { ShowsLastValue = true });

    /// <summary>Selects the Y axis.</summary>
    /// <param name="id">The name of the axis.</param>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> YAxis(string id) => Change(Definition with { YAxisId = id });

    /// <summary>Sets the color.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> Stroke(Rgba8 color) => Change(Definition with { Color = color });

    /// <summary>Sets the thickness.</summary>
    /// <param name="thickness">The thickness in device-independent units.</param>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> Thickness(double thickness) => Change(Definition with { Thickness = thickness });

    /// <summary>Sets a dash pattern.</summary>
    /// <param name="lengths">The lengths of dashes and spaces in device-independent units.</param>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> Dash(params float[] lengths) => Change(Definition with { DashPattern = lengths });

    /// <summary>Keeps each value until the next point.</summary>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> Step() => Change(Definition with { IsStep = true });

    /// <summary>Draws a smooth curve through the points, without overshooting them.</summary>
    /// <returns>This builder.</returns>
    public LineBuilder<TX, TY> Spline() => Change(Definition with { IsSpline = true });

    private LineBuilder<TX, TY> Change(LineDefinition<TX, TY> definition)
    {
        Definition = definition;
        return this;
    }
}

/// <summary>Describes markers.</summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
public sealed class ScatterBuilder<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    internal ScatterBuilder(IDataSource<TX, TY> source)
    {
        Definition = new ScatterDefinition<TX, TY> { Source = source };
    }

    internal ScatterDefinition<TX, TY> Definition { get; private set; }

    /// <summary>Selects the X axis.</summary>
    /// <param name="id">The name of the axis.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> XAxis(string id) => Change(Definition with { XAxisId = id });

    /// <summary>Sets the name shown in rollovers and legends.</summary>
    /// <param name="name">The name.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> Name(string name) => Change(Definition with { Name = name });

    /// <summary>Selects the Y axis.</summary>
    /// <param name="id">The name of the axis.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> YAxis(string id) => Change(Definition with { YAxisId = id });

    /// <summary>Sets the color.</summary>
    /// <param name="color">The color.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> Fill(Rgba8 color) => Change(Definition with { Color = color });

    /// <summary>Sets the shape.</summary>
    /// <param name="shape">The shape.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> Marker(MarkerShape shape) => Change(Definition with { Shape = shape });

    /// <summary>Sets the size.</summary>
    /// <param name="size">The size in device-independent units.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> Size(double size) => Change(Definition with { Size = size });

    /// <summary>Gives each point its own diameter, which makes a bubble chart.</summary>
    /// <param name="sizes">One diameter per point in device-independent units.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> Sizes(ReadOnlyMemory<float> sizes) => Change(Definition with { PointSizes = sizes });

    /// <summary>Shows a density map once there are too many points. The scale is logarithmic by default.</summary>
    /// <param name="threshold">The visible points per pixel from which the map shows.</param>
    /// <param name="configure">Describes the scale.</param>
    /// <returns>This builder.</returns>
    public ScatterBuilder<TX, TY> Density(double threshold = 1, Action<ColorAxisBuilder>? configure = null)
    {
        var builder = new ColorAxisBuilder();
        builder.Log();
        configure?.Invoke(builder);
        return Change(Definition with { DensityAxis = builder.Definition, DensityThreshold = threshold });
    }

    private ScatterBuilder<TX, TY> Change(ScatterDefinition<TX, TY> definition)
    {
        Definition = definition;
        return this;
    }
}

/// <summary>Describes the interactions of a chart.</summary>
public sealed class ModifierBuilder
{
    internal List<ModifierDefinition> Definitions { get; } = [];

    /// <summary>Adds zooming with the wheel and panning by dragging.</summary>
    /// <param name="zoomDirection">The directions of zooming.</param>
    /// <param name="panDirection">The directions of panning.</param>
    /// <returns>This builder.</returns>
    public ModifierBuilder ZoomPan(ChartDirection zoomDirection = ChartDirection.Both, ChartDirection panDirection = ChartDirection.Both)
    {
        Definitions.Add(new ZoomPanDefinition(zoomDirection, panDirection));
        return this;
    }

    /// <summary>Adds zooming into a dragged rectangle.</summary>
    /// <param name="modifierKey">The keys to hold when dragging.</param>
    /// <param name="direction">The directions of the rectangle.</param>
    /// <returns>This builder.</returns>
    public ModifierBuilder RubberBand(InputModifiers modifierKey = InputModifiers.Control, ChartDirection direction = ChartDirection.Both)
    {
        Definitions.Add(new RubberBandDefinition(modifierKey, direction));
        return this;
    }

    /// <summary>Adds a crosshair at the pointer.</summary>
    /// <param name="snap">Whether it jumps to the nearest point.</param>
    /// <returns>This builder.</returns>
    public ModifierBuilder Crosshair(bool snap = false)
    {
        Definitions.Add(new CrosshairDefinition(snap));
        return this;
    }

    /// <summary>Adds a vertical line with the values of all series.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder Rollover()
    {
        Definitions.Add(new RolloverDefinition());
        return this;
    }

    /// <summary>Adds a tooltip with the nearest point.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder Tooltip()
    {
        Definitions.Add(new TooltipDefinition());
        return this;
    }

    /// <summary>Adds editing of annotations.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder AnnotationEdit()
    {
        Definitions.Add(new AnnotationEditDefinition());
        return this;
    }

    /// <summary>Adds selection of points while holding Shift.</summary>
    /// <param name="mode">How points are selected.</param>
    /// <returns>This builder.</returns>
    public ModifierBuilder Selection(SelectionMode mode = SelectionMode.Rectangle)
    {
        Definitions.Add(new SelectionDefinition(mode));
        return this;
    }

    /// <summary>Adds two measuring cursors while holding Alt.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder MeasureCursors()
    {
        Definitions.Add(new MeasureCursorDefinition());
        return this;
    }

    /// <summary>Adds navigation with the keyboard and undo.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder Keyboard()
    {
        Definitions.Add(new KeyboardNavigationDefinition());
        return this;
    }

    /// <summary>Adds a legend.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder Legend()
    {
        Definitions.Add(new LegendDefinition());
        return this;
    }

    /// <summary>Adds the diagnostics overlay on F12.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder Diagnostics()
    {
        Definitions.Add(new DiagnosticsDefinition());
        return this;
    }

    /// <summary>Adds an interaction of its own.</summary>
    /// <param name="factory">Creates the modifier. A chart can call it again.</param>
    /// <returns>This builder.</returns>
    public ModifierBuilder Custom(Func<ChartModifier> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Definitions.Add(new CustomModifierDefinition(factory));
        return this;
    }

    /// <summary>Adds dragging and zooming over axes.</summary>
    /// <returns>This builder.</returns>
    public ModifierBuilder AxisDrag()
    {
        Definitions.Add(new AxisDragDefinition());
        return this;
    }
}

/// <summary>
/// Describes a chart in code. Every step returns the builder, so that calls can be chained.
/// </summary>
public sealed class ChartBuilder
{
    private readonly List<Func<ChartSurface, Overlays.ChartOverlay>> _overlays = [];
    private readonly List<AxisDefinition> _xAxes = [];
    private readonly List<AxisDefinition> _yAxes = [];
    private readonly List<SeriesDefinition> _series = [];
    private readonly List<ModifierDefinition> _modifiers = [];
    private string? _title;
    private Theme? _theme;
    private string? _groupName;

    /// <summary>Sets the chart title.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>Sets the theme.</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder Theme(Theme theme)
    {
        _theme = theme;
        return this;
    }

    /// <summary>Adds a horizontal axis.</summary>
    /// <param name="configure">Describes the axis.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder XAxis(Action<AxisBuilder> configure)
    {
        _xAxes.Add(Describe(configure, $"x{_xAxes.Count}"));
        return this;
    }

    /// <summary>Adds a vertical axis.</summary>
    /// <param name="configure">Describes the axis.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder YAxis(Action<AxisBuilder> configure)
    {
        _yAxes.Add(Describe(configure, $"y{_yAxes.Count}"));
        return this;
    }

    /// <summary>Adds a line.</summary>
    /// <typeparam name="TX">The X type.</typeparam>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="source">The data.</param>
    /// <param name="configure">Describes the line.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddLine<TX, TY>(IDataSource<TX, TY> source, Action<LineBuilder<TX, TY>>? configure = null)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
    {
        ArgumentNullException.ThrowIfNull(source);
        var builder = new LineBuilder<TX, TY>(source);
        configure?.Invoke(builder);
        _series.Add(builder.Definition);
        return this;
    }

    /// <summary>Adds markers.</summary>
    /// <typeparam name="TX">The X type.</typeparam>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="source">The data.</param>
    /// <param name="configure">Describes the markers.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddScatter<TX, TY>(IDataSource<TX, TY> source, Action<ScatterBuilder<TX, TY>>? configure = null)
        where TX : unmanaged, INumber<TX>
        where TY : unmanaged, INumber<TY>
    {
        ArgumentNullException.ThrowIfNull(source);
        var builder = new ScatterBuilder<TX, TY>(source);
        configure?.Invoke(builder);
        _series.Add(builder.Definition);
        return this;
    }

    /// <summary>Adds a series the caller creates. It works in every UI framework, and the chart disposes it.</summary>
    /// <param name="factory">Creates the series from its axes. A chart can call it again, for example after a device loss.</param>
    /// <param name="xAxis">The name of the X axis, or <see langword="null"/> for the first one.</param>
    /// <param name="yAxis">The name of the Y axis, or <see langword="null"/> for the first one.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddSeries(Func<AxisBase, AxisBase, ChartSeries> factory, string? xAxis = null, string? yAxis = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _series.Add(new CustomSeriesDefinition { Source = factory, Factory = factory, XAxisId = xAxis, YAxisId = yAxis });
        return this;
    }

    /// <summary>Adds a heatmap.</summary>
    /// <param name="data">The grid.</param>
    /// <param name="configure">Describes the heatmap.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddHeatmap(HeatmapData data, Action<HeatmapBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new HeatmapBuilder(data);
        configure?.Invoke(builder);
        _series.Add(builder.Definition);
        return this;
    }

    /// <summary>Adds a spectrogram.</summary>
    /// <param name="data">The rows.</param>
    /// <param name="configure">Describes the spectrogram.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddSpectrogram(SpectrogramData data, Action<SpectrogramBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new SpectrogramBuilder(data);
        configure?.Invoke(builder);
        _series.Add(builder.Definition);
        return this;
    }

    /// <summary>Adds a contour.</summary>
    /// <param name="data">The samples.</param>
    /// <param name="configure">Describes the contour.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddContour(HeatmapData data, Action<ContourBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var builder = new ContourBuilder(data);
        configure?.Invoke(builder);
        _series.Add(builder.Definition);
        return this;
    }

    /// <summary>Adds interactions.</summary>
    /// <param name="configure">Describes the interactions.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder Modifiers(Action<ModifierBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ModifierBuilder();
        configure(builder);
        _modifiers.AddRange(builder.Definitions);
        return this;
    }

    /// <summary>
    /// Adds a legend. Call it more than once for several legends, each with its own series, title and place; a click
    /// in any of them switches a series on or off.
    /// </summary>
    /// <param name="configure">Chooses place, title and series; <see langword="null"/> shows all series at the top right.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder Legend(Action<LegendBuilder>? configure = null)
    {
        var legend = new LegendBuilder();
        configure?.Invoke(legend);
        var (placement, title, names) = (legend.Placement, legend.TitleText, legend.Names);
        _overlays.Add(chart => new Input.LegendOverlay(chart) { Placement = placement, Title = title, SeriesNames = names });
        if (!_modifiers.Any(modifier => modifier is LegendDefinition))
        {
            _modifiers.Add(new LegendDefinition());
        }

        return this;
    }

    /// <summary>Adds something the chart draws on top, such as the grid of a special chart type.</summary>
    /// <param name="factory">Creates the overlay for a chart, on the render thread, after the series.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder AddOverlay(Func<ChartSurface, Overlays.ChartOverlay> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _overlays.Add(factory);
        return this;
    }

    /// <summary>Joins a group of charts that stay in step.</summary>
    /// <param name="name">The name of the group.</param>
    /// <returns>This builder.</returns>
    public ChartBuilder Group(string name)
    {
        _groupName = name;
        return this;
    }

    /// <summary>Finishes the description.</summary>
    /// <returns>The description.</returns>
    /// <exception cref="InvalidOperationException">There is no X or no Y axis, or a name occurs twice.</exception>
    public ChartDefinition ToDefinition()
    {
        if (_xAxes.Count == 0 || _yAxes.Count == 0)
        {
            throw new InvalidOperationException("A chart needs an X axis and a Y axis.");
        }

        if (_xAxes.Concat(_yAxes).GroupBy(axis => axis.Id).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            throw new InvalidOperationException($"The axis name '{duplicate.Key}' occurs twice.");
        }

        return new ChartDefinition(_title, _theme, [.. _xAxes], [.. _yAxes], [.. _series], [.. _modifiers]) { GroupName = _groupName, Overlays = [.. _overlays] };
    }

    private static AxisDefinition Describe(Action<AxisBuilder> configure, string fallbackId)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new AxisBuilder(fallbackId);
        configure(builder);
        return builder.Definition;
    }
}
