using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>The 2D types of the core and the interactions; more types follow over the same metadata.</summary>
internal sealed class CoreModule : IChartModule
{
    // The dash patterns by name: lengths of dash and gap in device independent units.
    private static readonly Dictionary<string, float[]> _dashes = new()
    {
        ["Solid"] = [],
        ["Dashed"] = [8, 4],
        ["Dotted"] = [2, 3],
        ["Dash dot"] = [8, 3, 2, 3],
    };

    private static readonly ParameterInfo _color = new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." };

    /// <summary>Gets the switch for labels with the values, which every type with points offers.</summary>
    public static ParameterInfo ValuesParameter { get; } = new("values", "Show values", ParameterKind.Flag, false) { Description = "Labels with the values at the points; labels that would cover others are left out." };

    /// <summary>Gets the names of the colormaps of heatmaps.</summary>
    public static IReadOnlyList<string> Colormaps { get; } = ["Viridis", "Inferno", "Magma", "Plasma", "Turbo", "Grayscale"];

    public IReadOnlyList<ChartTypeInfo> Types { get; } = [.. Basic(), .. ScienceTypes.Create(), .. FinanceTypes.Create(), .. InstrumentTypes.Create()];

    private static IReadOnlyList<ChartTypeInfo> Basic() =>
    [
        new("line", "Line", ChartCategory.TwoD, DataShape.Points, "sine",
        [
            new("thickness", "Thickness", ParameterKind.Number, 1.5) { Minimum = 0.5, Maximum = 10 },
            _color,
            new("style", "Style", ParameterKind.Choice, "straight") { Choices = ["straight", "step", "spline"] },
            new("dash", "Dashes", ParameterKind.Choice, "Solid") { Choices = [.. _dashes.Keys] },
            new("glow", "Glow radius", ParameterKind.Number, 0.0) { Minimum = 0, Maximum = 40, Description = "0 for the glow of the theme." },
            new("lastValue", "Last value at the axis", ParameterKind.Flag, false),
            ValuesParameter,
        ], ApplyLine, LineCode),
        new("scatter", "Scatter", ChartCategory.TwoD, DataShape.Points, "signal",
        [
            new("size", "Size", ParameterKind.Number, 4.0) { Minimum = 1, Maximum = 30 },
            new("marker", "Marker", ParameterKind.Choice, nameof(MarkerShape.Circle)) { Choices = Enum.GetNames<MarkerShape>() },
            _color,
            new("density", "Density colors", ParameterKind.Flag, false) { Description = "Colors by how many points lie close together." },
            ValuesParameter,
        ], ApplyScatter, ScatterCode) { Usings = ["Tachyon.Charts.Rendering"] },
        new("bubbles", "Bubbles", ChartCategory.TwoD, DataShape.Bubbles, "bubbles",
        [
            new("marker", "Marker", ParameterKind.Choice, nameof(MarkerShape.Circle)) { Choices = Enum.GetNames<MarkerShape>() },
            _color,
            ValuesParameter,
        ], ApplyBubbles, BubblesCode) { Usings = ["Tachyon.Charts.Rendering"] },
        new("area", "Area", ChartCategory.TwoD, DataShape.Points, "damped",
        [
            new("baseline", "Baseline", ParameterKind.Number, 0.0),
            new("valueColors", "Colors by value", ParameterKind.Flag, false) { Description = "The fill takes the color of its value from a colormap." },
            new("colormap", "Colormap", ParameterKind.Choice, "Turbo") { Choices = Colormaps },
            new("colorFrom", "Lowest color at", ParameterKind.Number, -1.0),
            new("colorTo", "Highest color at", ParameterKind.Number, 1.0),
            _color,
            ValuesParameter,
        ], ApplyArea, AreaCode) { Usings = ["Tachyon.Charts.Core.Series", "Tachyon.Charts.Core.Colors", "Tachyon.Charts.Core.Numerics"] },
        CoreTypes.StackedArea,
        CoreTypes.Band,
        new("bars", "Bars", ChartCategory.TwoD, DataShape.Rows, "groups",
        [
            new("layout", "Layout", ParameterKind.Choice, nameof(BarLayout.Grouped)) { Choices = Enum.GetNames<BarLayout>() },
            new("width", "Width", ParameterKind.Number, 0.8) { Minimum = 0.05, Maximum = 1 },
            new("horizontal", "Horizontal", ParameterKind.Flag, false),
            new("corners", "Corner radius", ParameterKind.Number, 0.0) { Minimum = 0, Maximum = 30 },
            new("shadow", "Soft shadow", ParameterKind.Flag, false),
            ValuesParameter,
        ], ApplyBars, BarsCode) { Usings = ["Tachyon.Charts.Core.Series", "Tachyon.Charts.Rendering"] },
        CoreTypes.ErrorBars,
        CoreTypes.Stems,
        CoreTypes.Histogram,
        CoreTypes.BoxPlot,
        CoreTypes.Violin,
        CoreTypes.Pie("pie", "Pie", 0),
        CoreTypes.Pie("donut", "Donut", 0.55),
        new("heatmap", "Heatmap", ChartCategory.TwoD, DataShape.Grid, "peaks",
        [
            new("colormap", "Colormap", ParameterKind.Choice, "Viridis") { Choices = Colormaps },
            new("interpolated", "Smooth", ParameterKind.Flag, false),
            ValuesParameter,
        ], ApplyHeatmap, HeatmapCode) { Usings = ["Tachyon.Charts.Core.Colors"] },
    ];

    public IReadOnlyList<SampleInfo> Samples { get; } = [.. SampleCatalog.Create(), .. ScienceSamples.Create(), .. FinanceTypes.Samples(), .. InstrumentTypes.Samples()];

    /// <inheritdoc />
    public IReadOnlyList<PageInfo> Pages => ShowroomPages.Core;

    /// <inheritdoc />
    public IReadOnlyList<TemplateInfo> Templates { get; } =
    [
        new("trendLimits", "Trend with limits", catalog =>
        {
            // A measured value over time with an upper and a lower limit and a legend.
            var spec = catalog.CreateSpec("line");
            spec.Title = "Trend with limits";
            spec.Series[0].Name = "Pressure";
            spec.Series[0].Data.Sample = "signal";
            spec.Legend = new LegendSpec();
            foreach (var (value, label, color) in new[] { (1.2, "Upper limit", "#E63946"), (-1.2, "Lower limit", "#3A86FF") })
            {
                var limit = new AnnotationSpec { Type = "hline" };
                limit.Parameters.Set("y", value);
                limit.Parameters.Set("label", label);
                limit.Parameters.Set("color", color);
                spec.Annotations.Add(limit);
            }

            return spec;
        }),
        new("spectrumMask", "Spectrum with limit mask and marker", catalog => catalog.CreateSpec("spectrum")),
        new("scopeFour", "Oscilloscope with four channels", catalog =>
        {
            var spec = catalog.CreateSpec("oscilloscope");
            spec.Series[0].Parameters.Set("channels", 4.0);
            spec.Series[0].Parameters.Set("signals", "Sine, Square, Burst, Triangle");
            return spec;
        }),
    ];

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
        new("selection", "Select points", [new("lasso", "Lasso", ParameterKind.Flag, false) { Description = "Shift and a free line around the points instead of a rectangle." }],
            (m, values) => m.Selection(values.GetFlag("lasso", false) ? SelectionMode.Lasso : SelectionMode.Rectangle),
            values => values.GetFlag("lasso", false) ? ".Selection(Tachyon.Charts.Core.Input.SelectionMode.Lasso)" : ".Selection()"),
        new("annotationedit", "Edit annotations by mouse", [], (m, _) => m.AnnotationEdit(), _ => ".AnnotationEdit()"),
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
        if (_dashes[values.GetText("dash", "Solid")] is { Length: > 0 } dashes)
        {
            code += $".Dash({string.Join(", ", dashes.Select(length => CodeText.Number(length)))})";
        }

        if (values.GetNumber("glow", 0) > 0)
        {
            code += $".Glow({CodeText.Number(values.GetNumber("glow", 0))})";
        }

        return code + (values.GetFlag("lastValue", false) ? ".LastValue()" : string.Empty) + Values(values) + ")";
    }

    private static string ScatterCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".AddScatter({data}, s => s.Name({CodeText.String(series.Name)}).XAxis({CodeText.String(axes.X)}).YAxis({CodeText.String(axes.Y)}).Size({CodeText.Number(values.GetNumber("size", 4))}).Marker(MarkerShape.{values.GetText("marker", nameof(MarkerShape.Circle))})";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            code += $".Fill({CodeText.Color(color)})";
        }

        return code + (values.GetFlag("density", false) ? ".Density()" : string.Empty) + Values(values) + ")";
    }

    private static string AreaCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var fill = values.GetFlag("valueColors", false)
            ? $", Fill = new ValueGradientFill(Colormaps.{values.GetText("colormap", "Turbo")}, new DoubleRange({CodeText.Number(values.GetNumber("colorFrom", -1))}, {CodeText.Number(values.GetNumber("colorTo", 1))}))"
            : SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? $", Fill = new SolidFill({CodeText.Color(color)})" : string.Empty;
        return $".AddSeries((x, y) => new AreaSeries({data}, x, y) {{ Name = {CodeText.String(series.Name)}, Baseline = {CodeText.Number(values.GetNumber("baseline", 0))}{fill}, ShowsValues = {CodeText.Flag(values.GetFlag("values", false))} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    private static string BarsCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        return $".AddSeries((x, y) => new BarSeries({data}, x, y) {{ Name = {CodeText.String(series.Name)}, Layout = BarLayout.{values.GetText("layout", nameof(BarLayout.Grouped))}, BarWidth = {CodeText.Number(values.GetNumber("width", 0.8))}, IsHorizontal = {CodeText.Flag(values.GetFlag("horizontal", false))}, CornerRadius = {CodeText.Number(values.GetNumber("corners", 0))}{(values.GetFlag("shadow", false) ? ", ShadowColor = new Rgba8(0, 0, 0, 110)" : string.Empty)}, ShowsValues = {CodeText.Flag(values.GetFlag("values", false))} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    private static string HeatmapCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        return $".AddHeatmap({data}, h => h.Name({CodeText.String(series.Name)}).Cells(0, 10.0 / {data}.Width, 0, 10.0 / {data}.Height).Interpolated({CodeText.Flag(values.GetFlag("interpolated", false))}).ColorAxis(a => a.Colormap(Colormaps.{values.GetText("colormap", "Viridis")})){Values(values)})";
    }

    private static string Values(SpecValues values) => values.GetFlag("values", false) ? ".Values()" : string.Empty;

    private static IDataSource<double, double> Source(object data) => (IDataSource<double, double>)data;

    private static void ApplyLine(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
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

            if (_dashes[values.GetText("dash", "Solid")] is { Length: > 0 } dashes)
            {
                line.Dash(dashes);
            }

            if (values.GetNumber("glow", 0) > 0)
            {
                line.Glow(values.GetNumber("glow", 0));
            }

            if (values.GetFlag("lastValue", false))
            {
                line.LastValue();
            }

            if (values.GetFlag("values", false))
            {
                line.Values();
            }
        });
    }

    private static void ApplyScatter(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
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

            if (values.GetFlag("density", false))
            {
                scatter.Density();
            }

            if (values.GetFlag("values", false))
            {
                scatter.Values();
            }
        });
    }

    private static string BubblesCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".AddScatter({data}.Points, s => s.Name({CodeText.String(series.Name)}).XAxis({CodeText.String(axes.X)}).YAxis({CodeText.String(axes.Y)}).Marker(MarkerShape.{values.GetText("marker", nameof(MarkerShape.Circle))}).Sizes({data}.Sizes)";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            code += $".Fill({CodeText.Color(color)})";
        }

        return code + Values(values) + ")";
    }

    private static void ApplyBubbles(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var (points, sizes) = ((DataSource<double, double>, float[]))data;
        chart.AddScatter(points, scatter =>
        {
            scatter.Name(series.Name).XAxis(axes.X).YAxis(axes.Y).Marker(Enum.Parse<MarkerShape>(values.GetText("marker", nameof(MarkerShape.Circle)))).Sizes(sizes);
            if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
            {
                scatter.Fill(color);
            }

            if (values.GetFlag("values", false))
            {
                scatter.Values();
            }
        });
    }

    private static void ApplyArea(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddSeries(
            (x, y) =>
            {
                var area = new AreaSeries(Source(data), x, y) { Name = series.Name, Baseline = values.GetNumber("baseline", 0), ShowsValues = values.GetFlag("values", false) };
                if (values.GetFlag("valueColors", false))
                {
                    area.Fill = new ValueGradientFill(Colormap(values.GetText("colormap", "Turbo")), new DoubleRange(values.GetNumber("colorFrom", -1), values.GetNumber("colorTo", 1)));
                }
                else if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
                {
                    area.Fill = new SolidFill(color);
                }

                return area;
            },
            axes.X,
            axes.Y);
    }

    private static void ApplyBars(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        chart.AddSeries(
            (x, y) => new BarSeries((double[][])data, x, y)
            {
                Name = series.Name,
                Layout = Enum.Parse<BarLayout>(values.GetText("layout", nameof(BarLayout.Grouped))),
                BarWidth = values.GetNumber("width", 0.8),
                IsHorizontal = values.GetFlag("horizontal", false),
                CornerRadius = values.GetNumber("corners", 0),
                ShadowColor = values.GetFlag("shadow", false) ? new Rgba8(0, 0, 0, 110) : null,
                ShowsValues = values.GetFlag("values", false),
            },
            axes.X,
            axes.Y);
    }

    private static void ApplyHeatmap(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var grid = (HeatmapData)data;
        chart.AddHeatmap(grid, heatmap => heatmap
            .Name(series.Name)
            .Cells(0, 10.0 / grid.Width, 0, 10.0 / grid.Height)
            .Interpolated(values.GetFlag("interpolated", false))
            .ColorAxis(axis => axis.Colormap(Colormap(values.GetText("colormap", "Viridis"))))
            .Values(values.GetFlag("values", false)));
    }
}
