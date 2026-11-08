using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>The 2D types of the core and the interactions; more types follow over the same metadata.</summary>
internal sealed class CoreModule : IChartModule
{
    private static readonly ParameterInfo _color = new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." };

    /// <summary>Gets the names of the colormaps of heatmaps.</summary>
    public static IReadOnlyList<string> Colormaps { get; } = ["Viridis", "Inferno", "Magma", "Plasma", "Turbo", "Grayscale"];

    public IReadOnlyList<ChartTypeInfo> Types { get; } =
    [
        new("line", "Line", ChartCategory.TwoD, DataShape.Points, "sine",
        [
            new("thickness", "Thickness", ParameterKind.Number, 1.5) { Minimum = 0.5, Maximum = 10 },
            _color,
            new("style", "Style", ParameterKind.Choice, "straight") { Choices = ["straight", "step", "spline"] },
            new("lastValue", "Last value at the axis", ParameterKind.Flag, false),
        ], ApplyLine, LineCode),
        new("scatter", "Scatter", ChartCategory.TwoD, DataShape.Points, "signal",
        [
            new("size", "Size", ParameterKind.Number, 4.0) { Minimum = 1, Maximum = 30 },
            new("marker", "Marker", ParameterKind.Choice, nameof(MarkerShape.Circle)) { Choices = Enum.GetNames<MarkerShape>() },
            _color,
        ], ApplyScatter, ScatterCode) { Usings = ["Tachyon.Charts.Rendering"] },
        new("area", "Area", ChartCategory.TwoD, DataShape.Points, "damped",
        [
            new("baseline", "Baseline", ParameterKind.Number, 0.0),
            _color,
        ], ApplyArea, AreaCode) { Usings = ["Tachyon.Charts.Core.Series"] },
        new("bars", "Bars", ChartCategory.TwoD, DataShape.Rows, "groups",
        [
            new("layout", "Layout", ParameterKind.Choice, nameof(BarLayout.Grouped)) { Choices = Enum.GetNames<BarLayout>() },
            new("width", "Width", ParameterKind.Number, 0.8) { Minimum = 0.05, Maximum = 1 },
            new("horizontal", "Horizontal", ParameterKind.Flag, false),
        ], ApplyBars, BarsCode) { Usings = ["Tachyon.Charts.Core.Series"] },
        new("heatmap", "Heatmap", ChartCategory.TwoD, DataShape.Grid, "peaks",
        [
            new("colormap", "Colormap", ParameterKind.Choice, "Viridis") { Choices = Colormaps },
            new("interpolated", "Smooth", ParameterKind.Flag, false),
        ], ApplyHeatmap, HeatmapCode) { Usings = ["Tachyon.Charts.Core.Colors"] },
    ];

    public IReadOnlyList<SampleInfo> Samples { get; } = SampleCatalog.Create();

    public static IReadOnlyList<ModifierInfo> CreateModifiers() =>
    [
        new("zoompan", "Zoom and pan", [], (m, _) => m.ZoomPan(), _ => ".ZoomPan()"),
        new("rubberband", "Zoom into a rectangle", [], (m, _) => m.RubberBand(), _ => ".RubberBand()"),
        new("crosshair", "Crosshair",
        [
            new("snap", "Snap to the data", ParameterKind.Flag, false),
            new("values", "Values of all series", ParameterKind.Flag, false),
        ], (m, values) => m.Crosshair(values.GetFlag("snap", false), values.GetFlag("values", false)), CrosshairCode),
        new("tooltip", "Tooltip", [], (m, _) => m.Tooltip(), _ => ".Tooltip()"),
        new("rollover", "Rollover", [], (m, _) => m.Rollover(), _ => ".Rollover()"),
        new("axisdrag", "Drag the axes", [], (m, _) => m.AxisDrag(), _ => ".AxisDrag()"),
        new("measure", "Measuring cursors", [], (m, _) => m.MeasureCursors(), _ => ".MeasureCursors()"),
        new("keyboard", "Keyboard", [], (m, _) => m.Keyboard(), _ => ".Keyboard()"),
        new("legend", "Legend", [], (m, _) => m.Legend(), _ => ".Legend()"),
        new("diagnostics", "Diagnostics", [], (m, _) => m.Diagnostics(), _ => ".Diagnostics()"),
    ];

    /// <summary>Gets a colormap by name.</summary>
    public static Colormap Colormap(string name) => name switch
    {
        "Inferno" => Core.Colors.Colormaps.Inferno,
        "Magma" => Core.Colors.Colormaps.Magma,
        "Plasma" => Core.Colors.Colormaps.Plasma,
        "Turbo" => Core.Colors.Colormaps.Turbo,
        "Grayscale" => Core.Colors.Colormaps.Grayscale,
        _ => Core.Colors.Colormaps.Viridis,
    };

    private static string CrosshairCode(SpecValues values)
    {
        var (snap, all) = (values.GetFlag("snap", false), values.GetFlag("values", false));
        return snap || all ? $".Crosshair(snap: {CodeText.Flag(snap)}, values: {CodeText.Flag(all)})" : ".Crosshair()";
    }

    private static string LineCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".AddLine({data}, s => s.Name({CodeText.String(series.Name)}).XAxis({CodeText.String(axes.X)}).YAxis({CodeText.String(axes.Y)}).Thickness({CodeText.Number(values.GetNumber("thickness", 1.5))})";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            code += $".Stroke({CodeText.Color(color)})";
        }

        code += values.GetText("style", "straight") switch
        {
            "step" => ".Step()",
            "spline" => ".Spline()",
            _ => string.Empty,
        };
        return code + (values.GetFlag("lastValue", false) ? ".LastValue()" : string.Empty) + ")";
    }

    private static string ScatterCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".AddScatter({data}, s => s.Name({CodeText.String(series.Name)}).XAxis({CodeText.String(axes.X)}).YAxis({CodeText.String(axes.Y)}).Size({CodeText.Number(values.GetNumber("size", 4))}).Marker(MarkerShape.{values.GetText("marker", nameof(MarkerShape.Circle))})";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            code += $".Fill({CodeText.Color(color)})";
        }

        return code + ")";
    }

    private static string AreaCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var fill = SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? $", Fill = new SolidFill({CodeText.Color(color)})" : string.Empty;
        return $".AddSeries((x, y) => new AreaSeries({data}, x, y) {{ Name = {CodeText.String(series.Name)}, Baseline = {CodeText.Number(values.GetNumber("baseline", 0))}{fill} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    private static string BarsCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        return $".AddSeries((x, y) => new BarSeries({data}, x, y) {{ Name = {CodeText.String(series.Name)}, Layout = BarLayout.{values.GetText("layout", nameof(BarLayout.Grouped))}, BarWidth = {CodeText.Number(values.GetNumber("width", 0.8))}, IsHorizontal = {CodeText.Flag(values.GetFlag("horizontal", false))} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    private static string HeatmapCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        return $".AddHeatmap({data}, h => h.Name({CodeText.String(series.Name)}).Cells(0, 10.0 / {data}.Width, 0, 10.0 / {data}.Height).Interpolated({CodeText.Flag(values.GetFlag("interpolated", false))}).ColorAxis(a => a.Colormap(Colormaps.{values.GetText("colormap", "Viridis")})))";
    }

    private static DataSource<double, double> Source(SampleSet data)
    {
        var source = new DataSource<double, double>(Math.Max(1, data.X.Length));
        source.Append(data.X, data.Y);
        return source;
    }

    private static void ApplyLine(ChartBuilder chart, SeriesSpec series, SampleSet data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddLine(Source(data), line =>
        {
            line.Name(series.Name).XAxis(axes.X).YAxis(axes.Y).Thickness(values.GetNumber("thickness", 1.5));
            if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
            {
                line.Stroke(color);
            }

            switch (values.GetText("style", "straight"))
            {
                case "step":
                    line.Step();
                    break;
                case "spline":
                    line.Spline();
                    break;
            }

            if (values.GetFlag("lastValue", false))
            {
                line.LastValue();
            }
        });
    }

    private static void ApplyScatter(ChartBuilder chart, SeriesSpec series, SampleSet data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddScatter(Source(data), scatter =>
        {
            scatter.Name(series.Name).XAxis(axes.X).YAxis(axes.Y).Size(values.GetNumber("size", 4))
                .Marker(Enum.Parse<MarkerShape>(values.GetText("marker", nameof(MarkerShape.Circle))));
            if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
            {
                scatter.Fill(color);
            }
        });
    }

    private static void ApplyArea(ChartBuilder chart, SeriesSpec series, SampleSet data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddSeries(
            (x, y) =>
            {
                var area = new AreaSeries(Source(data), x, y) { Name = series.Name, Baseline = values.GetNumber("baseline", 0) };
                if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
                {
                    area.Fill = new SolidFill(color);
                }

                return area;
            },
            axes.X,
            axes.Y);
    }

    private static void ApplyBars(ChartBuilder chart, SeriesSpec series, SampleSet data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddSeries(
            (x, y) => new BarSeries(data.Rows, x, y)
            {
                Name = series.Name,
                Layout = Enum.Parse<BarLayout>(values.GetText("layout", nameof(BarLayout.Grouped))),
                BarWidth = values.GetNumber("width", 0.8),
                IsHorizontal = values.GetFlag("horizontal", false),
            },
            axes.X,
            axes.Y);
    }

    private static void ApplyHeatmap(ChartBuilder chart, SeriesSpec series, SampleSet data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var grid = new HeatmapData(data.GridWidth, data.GridHeight);
        grid.SetValues(data.Grid);
        chart.AddHeatmap(grid, heatmap => heatmap
            .Name(series.Name)
            .Cells(0, 10.0 / data.GridWidth, 0, 10.0 / data.GridHeight)
            .Interpolated(values.GetFlag("interpolated", false))
            .ColorAxis(axis => axis.Colormap(Colormap(values.GetText("colormap", "Viridis")))));
    }
}
