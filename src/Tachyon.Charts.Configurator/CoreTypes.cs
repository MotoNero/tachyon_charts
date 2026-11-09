using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>The 2D types of the core described by recipes: stacked area, band, measurements, statistics, pie and donut.</summary>
internal static class CoreTypes
{
    private static readonly ParameterInfo _fill = new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." };
    private static readonly ParameterInfo _thickness = new("thickness", "Thickness", ParameterKind.Number, 1.0) { Minimum = 0.5, Maximum = 10 };

    public static ChartTypeInfo StackedArea { get; } = new SeriesRecipe(
        typeof(StackedAreaSeries),
        [new("percent", "Share in percent", ParameterKind.Flag, false)],
        ("percent", nameof(StackedAreaSeries.IsPercent)))
        .Type("stackedarea", "Stacked area", DataShape.Layers, "layers", (_, data, x, y) => new StackedAreaSeries((DataSource<double, double>[])data, x, y), (_, data) => $"new StackedAreaSeries({data}, x, y)");

    public static ChartTypeInfo Band { get; } = new SeriesRecipe(
        typeof(BandSeries),
        [
            new("firstAbove", "Color where the first is above", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." },
            new("secondAbove", "Color where the second is above", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." },
        ],
        ("firstAbove", nameof(BandSeries.FirstAboveFill)),
        ("secondAbove", nameof(BandSeries.SecondAboveFill)))
        .Type("band", "Band", DataShape.Band, "band", (_, data, x, y) => Band2(data, x, y), (_, data) => $"new BandSeries({data}.First, {data}.Second, x, y)");

    public static ChartTypeInfo ErrorBars { get; } = new SeriesRecipe(
        typeof(ErrorBarSeries),
        [
            new("capWidth", "Width of the caps", ParameterKind.Number, 8.0) { Minimum = 0, Maximum = 40 },
            new("pointSize", "Size of the points", ParameterKind.Number, 5.0) { Minimum = 0, Maximum = 30 },
            _thickness,
            _fill,
        ],
        ("capWidth", nameof(ErrorBarSeries.CapWidth)),
        ("pointSize", nameof(ErrorBarSeries.PointSize)),
        ("thickness", nameof(ErrorBarSeries.Thickness)),
        ("color", nameof(ErrorBarSeries.Color)))
        .Type("errorbars", "Error bars", DataShape.Errors, "errors", (_, data, x, y) => Errors(data, x, y), (_, data) => $"new ErrorBarSeries({data}.Values, {data}.Lower, {data}.Upper, x, y)");

    public static ChartTypeInfo Stems { get; } = new SeriesRecipe(
        typeof(StemSeries),
        [
            new("baseline", "Baseline", ParameterKind.Number, 0.0),
            new("headSize", "Size of the heads", ParameterKind.Number, 6.0) { Minimum = 0, Maximum = 30 },
            _thickness,
            _fill,
        ],
        ("baseline", nameof(StemSeries.Baseline)),
        ("headSize", nameof(StemSeries.HeadSize)),
        ("thickness", nameof(StemSeries.Thickness)),
        ("color", nameof(StemSeries.Color)))
        .Type("stems", "Stems", DataShape.Points, "decay", (_, data, x, y) => new StemSeries((DataSource<double, double>)data, x, y), (_, data) => $"new StemSeries({data}, x, y)");

    public static ChartTypeInfo Histogram { get; } = new SeriesRecipe(
        typeof(BarSeries),
        [
            new("rule", "Bins", ParameterKind.Choice, nameof(BinningRule.Sturges)) { Choices = Enum.GetNames<BinningRule>() },
            new("parameter", "Count or width", ParameterKind.Number, 30.0) { Minimum = 0.001, Maximum = 10_000, Description = "The number of bins for a fixed count, their width for a fixed width." },
            new("width", "Width", ParameterKind.Number, 1.0) { Minimum = 0.05, Maximum = 1 },
        ],
        ("width", nameof(BarSeries.BarWidth)))
        .Type(
            "histogram",
            "Histogram",
            DataShape.Values,
            "skewed",
            (values, data, x, y) => BarSeries.FromHistogram(Core.Numerics.Histogram.Compute((double[])data, Rule(values), values.GetNumber("parameter", 30)), x, y),
            (values, data) => $"BarSeries.FromHistogram(Histogram.Compute({data}, BinningRule.{Rule(values)}, {CodeText.Number(values.GetNumber("parameter", 30))}), x, y)",
            "Tachyon.Charts.Core.Numerics");

    public static ChartTypeInfo BoxPlot { get; } = Distribution(typeof(BoxPlotSeries)).Type("boxplot", "Box plot", DataShape.Groups, "samples", (_, data, x, y) => new BoxPlotSeries((double[][])data, x, y), (_, data) => $"new BoxPlotSeries({data}, x, y)");

    public static ChartTypeInfo Violin { get; } = Distribution(typeof(ViolinSeries)).Type("violin", "Violin", DataShape.Groups, "samples", (_, data, x, y) => new ViolinSeries((double[][])data, x, y), (_, data) => $"new ViolinSeries({data}, x, y)");

    /// <summary>Makes a pie or, with a hole, a donut. Both bring their own hidden axes, labels and interaction.</summary>
    public static ChartTypeInfo Pie(string id, string name, double hole)
    {
        var recipe = new SeriesRecipe(
            typeof(PieSeries),
            [
                new("innerRadius", "Hole", ParameterKind.Number, hole) { Minimum = 0, Maximum = 0.95, Description = "The radius of the hole as a share of the radius." },
                new("labels", "Labels", ParameterKind.Choice, nameof(PieLabels.NameAndPercent)) { Choices = Enum.GetNames<PieLabels>() },
                new("exploded", "Slice pulled out", ParameterKind.Integer, -1.0) { Minimum = -1, Maximum = 100, Description = "-1 for none." },
                new("explodeDistance", "Pulled out by", ParameterKind.Number, 0.1) { Minimum = 0, Maximum = 0.5 },
                new("gap", "Gap", ParameterKind.Number, 1.5) { Minimum = 0, Maximum = 10 },
            ],
            ("innerRadius", nameof(PieSeries.InnerRadius)),
            ("labels", nameof(PieSeries.Labels)),
            ("exploded", nameof(PieSeries.ExplodedIndex)),
            ("explodeDistance", nameof(PieSeries.ExplodeDistance)),
            ("gap", nameof(PieSeries.Gap)));
        return new ChartTypeInfo(
            id,
            name,
            ChartCategory.TwoD,
            DataShape.Slices,
            "mix",
            recipe.Parameters,
            (chart, series, data, _) =>
            {
                var (values, names) = ((double[], string[]))data;
                chart.Pie(values, names, pie => recipe.Set(pie, series));
            },
            (series, data, _) => $".Pie({data}.Values, {data}.Names, p => {{ {string.Concat(recipe.Assignments(series).Select(assignment => "p." + assignment + "; "))}}})")
        {
            Usings = recipe.Usings,
            OwnsAxes = true,
        };
    }

    private static SeriesRecipe Distribution(Type series) => new(
        series,
        [
            new("width", "Width", ParameterKind.Number, 0.6) { Minimum = 0.05, Maximum = 1 },
            new("color", "Fill", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." },
            new("lineColor", "Line color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." },
        ],
        ("width", nameof(SampleSeries.Width)),
        ("color", nameof(SampleSeries.Fill)),
        ("lineColor", nameof(SampleSeries.LineColor)));

    private static BinningRule Rule(SpecValues values) => Enum.Parse<BinningRule>(values.GetText("rule", nameof(BinningRule.Sturges)));

    private static BandSeries Band2(object data, Core.Axes.AxisBase x, Core.Axes.AxisBase y)
    {
        var (first, second) = ((DataSource<double, double>, DataSource<double, double>))data;
        return new BandSeries(first, second, x, y);
    }

    private static ErrorBarSeries Errors(object data, Core.Axes.AxisBase x, Core.Axes.AxisBase y)
    {
        var (values, lower, upper) = ((DataSource<double, double>, double[], double[]))data;
        return new ErrorBarSeries(values, lower, upper, x, y);
    }
}
