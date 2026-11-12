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
        ["Long dashes"] = [10, 6],
        ["Sparse dots"] = [1, 7],
        ["Long dash dot"] = [16, 4, 4, 4],
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
            new("glowIntensity", "Glow intensity", ParameterKind.Number, 0.8) { Minimum = 0, Maximum = 1, Description = "How bright the glow is, from 0 to 1." },
            new("lastValue", "Last value at the axis", ParameterKind.Flag, false),
            ValuesParameter,
        ], ApplyLine, LineCode),
        new("scatter", "Scatter", ChartCategory.TwoD, DataShape.Points, "signal",
        [
            new("size", "Size", ParameterKind.Number, 4.0) { Minimum = 1, Maximum = 30 },
            new("marker", "Marker", ParameterKind.Choice, nameof(MarkerShape.Circle)) { Choices = Enum.GetNames<MarkerShape>() },
            _color,
            new("density", "Density colors", ParameterKind.Flag, false) { Description = "Colors by how many points lie close together." },
            new("densityColormap", "Colormap of the density", ParameterKind.Choice, "Viridis") { Choices = Colormaps },
            new("borderColor", "Border color", ParameterKind.Color, string.Empty) { Description = "Empty for the usual dark border." },
            new("border", "Border thickness", ParameterKind.Number, 1.0) { Minimum = 0, Maximum = 10 },
            ValuesParameter,
        ], ApplyScatter, ScatterCode) { Usings = ["Tachyon.Charts.Rendering", "Tachyon.Charts.Core.Colors"] },
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
            new("shadowColor", "Shadow color", ParameterKind.Color, string.Empty) { Description = "Empty for a dark shadow." },
            new("gradientFrom", "Gradient from", ParameterKind.Color, string.Empty) { Description = "With the next color, every bar runs from this color at its foot to that color at its top." },
            new("gradientTo", "Gradient to", ParameterKind.Color, string.Empty),
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
            new("size", "Extent", ParameterKind.Number, 10.0) { Minimum = 1e-9, Description = "The grid covers 0 to this value on both axes." },
            new("missingColor", "Color of missing values", ParameterKind.Color, string.Empty) { Description = "Empty to leave them clear." },
            new("logColors", "Colors on a logarithmic scale", ParameterKind.Flag, false),
            ValuesParameter,
        ], ApplyHeatmap, HeatmapCode) { Usings = ["Tachyon.Charts.Core.Colors"] },
    ];

    public IReadOnlyList<SampleInfo> Samples { get; } = [.. SampleCatalog.Create(), .. ScienceSamples.Create(), .. FinanceTypes.Samples(), .. InstrumentTypes.Samples(), .. ShowroomSamples.Create(), .. ShowroomShapes.Create(), .. ShowroomPrices.Create(), .. ShowroomPlans.Create(), .. ShowroomGallery.Create(), .. ShowroomChecks.Create()];

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
            code += $".Glow({CodeText.Number(values.GetNumber("glow", 0))}, {CodeText.Number(values.GetNumber("glowIntensity", 0.8))})";
        }

        return code + (values.GetFlag("lastValue", false) ? ".LastValue()" : string.Empty) + Values(values) + ")";
    }

    // A border other than the usual one, or none when the usual one stays.
    private static (Rgba8 Color, double Thickness)? Border(SpecValues values)
    {
        var thickness = values.GetNumber("border", 1);
        var isColored = SpecColor.TryParse(values.GetText("borderColor", string.Empty), out var color);
        return isColored || thickness != 1 ? (isColored ? color : new Rgba8(20, 50, 70, 255), thickness) : null;
    }

    private static string ScatterCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".AddScatter({data}, s => s.Name({CodeText.String(series.Name)}).XAxis({CodeText.String(axes.X)}).YAxis({CodeText.String(axes.Y)}).Size({CodeText.Number(values.GetNumber("size", 4))}).Marker(MarkerShape.{values.GetText("marker", nameof(MarkerShape.Circle))})";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            code += $".Fill({CodeText.Color(color)})";
        }

        if (Border(values) is { } border)
        {
            code += $".Border({CodeText.Color(border.Color)}, {CodeText.Number(border.Thickness)})";
        }

        return code + (values.GetFlag("density", false) ? $".Density(1, a => a.Colormap(Colormaps.{values.GetText("densityColormap", "Viridis")}))" : string.Empty) + Values(values) + ")";
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
        return $".AddSeries((x, y) => new BarSeries({data}, x, y) {{ Name = {CodeText.String(series.Name)}, Layout = BarLayout.{values.GetText("layout", nameof(BarLayout.Grouped))}, BarWidth = {CodeText.Number(values.GetNumber("width", 0.8))}, IsHorizontal = {CodeText.Flag(values.GetFlag("horizontal", false))}, CornerRadius = {CodeText.Number(values.GetNumber("corners", 0))}{(Shadow(values) is { } shadow ? $", ShadowColor = {CodeText.Color(shadow)}" : string.Empty)}{(Gradient(values) is { } gradient ? $", Fills = [.. Enumerable.Repeat<SeriesFill>(new PositionGradientFill({CodeText.Color(gradient.From)}, {CodeText.Color(gradient.To)}), {data}.Length)]" : string.Empty)}, ShowsValues = {CodeText.Flag(values.GetFlag("values", false))} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    private static string HeatmapCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        return $".AddHeatmap({data}, h => h.Name({CodeText.String(series.Name)}).Cells(0, {CodeText.Number(values.GetNumber("size", 10))} / (double){data}.Width, 0, {CodeText.Number(values.GetNumber("size", 10))} / (double){data}.Height).Interpolated({CodeText.Flag(values.GetFlag("interpolated", false))}).ColorAxis(a => a.Colormap(Colormaps.{values.GetText("colormap", "Viridis")}){ColorScaleCode(values)}){Values(values)})";
    }

    // Bars keep the colors of their rows unless a gradient replaces them.
    private static BarSeries WithGradient(BarSeries bars, SpecValues values, int rows)
    {
        if (Gradient(values) is { } gradient)
        {
            bars.Fills = [.. Enumerable.Repeat<SeriesFill>(new PositionGradientFill(gradient.From, gradient.To), rows)];
        }

        return bars;
    }

    // The shadow of the bars: its own color, a dark one, or none.
    private static Rgba8? Shadow(SpecValues values) =>
        SpecColor.TryParse(values.GetText("shadowColor", string.Empty), out var color) ? color : values.GetFlag("shadow", false) ? new Rgba8(0, 0, 0, 110) : null;

    private static (Rgba8 From, Rgba8 To)? Gradient(SpecValues values) =>
        SpecColor.TryParse(values.GetText("gradientFrom", string.Empty), out var from) && SpecColor.TryParse(values.GetText("gradientTo", string.Empty), out var to) ? (from, to) : null;

    /// <summary>Sets the scale of a color axis: logarithmic, and the color of missing values.</summary>
    /// <param name="axis">The color axis.</param>
    /// <param name="values">The values of the series.</param>
    /// <returns>The color axis.</returns>
    internal static ColorAxisBuilder ColorScale(ColorAxisBuilder axis, SpecValues values)
    {
        if (values.GetFlag("logColors", false))
        {
            axis.Log();
        }

        return SpecColor.TryParse(values.GetText("missingColor", string.Empty), out var missing) ? axis.Missing(missing) : axis;
    }

    /// <summary>Writes the scale of a color axis as code.</summary>
    /// <param name="values">The values of the series.</param>
    /// <returns>The calls.</returns>
    internal static string ColorScaleCode(SpecValues values) =>
        (values.GetFlag("logColors", false) ? ".Log()" : string.Empty)
        + (SpecColor.TryParse(values.GetText("missingColor", string.Empty), out var missing) ? $".Missing({CodeText.Color(missing)})" : string.Empty);

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
                line.Glow(values.GetNumber("glow", 0), values.GetNumber("glowIntensity", 0.8));
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

    // Points of doubles, or of floats for very many points.
    private static void ApplyScatter(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        if (data is IDataSource<float, float> floats)
        {
            Scatter(chart, series, floats, axes);
        }
        else
        {
            Scatter(chart, series, Source(data), axes);
        }
    }

    private static void Scatter<TX, TY>(ChartBuilder chart, SeriesSpec series, IDataSource<TX, TY> data, SeriesAxes axes)
        where TX : unmanaged, System.Numerics.INumber<TX>
        where TY : unmanaged, System.Numerics.INumber<TY>
    {
        var values = series.Parameters;
        chart.AddScatter(data, scatter =>
        {
            scatter.Name(series.Name).XAxis(axes.X).YAxis(axes.Y).Size(values.GetNumber("size", 4))
                .Marker(Enum.Parse<MarkerShape>(values.GetText("marker", nameof(MarkerShape.Circle))));
            if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
            {
                scatter.Fill(color);
            }

            if (Border(values) is { } border)
            {
                scatter.Border(border.Color, border.Thickness);
            }

            if (values.GetFlag("density", false))
            {
                scatter.Density(1, axis => axis.Colormap(Colormap(values.GetText("densityColormap", "Viridis"))));
            }

            if (values.GetFlag("values", false))
            {
                scatter.Values();
            }
        });
    }

    // Samples of bubbles that bring a color per point.
    internal static readonly HashSet<string> Colored = new(StringComparer.Ordinal) { "colorGrid" };

    private static string BubblesCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".AddScatter({data}.Points, s => s.Name({CodeText.String(series.Name)}).XAxis({CodeText.String(axes.X)}).YAxis({CodeText.String(axes.Y)}).Marker(MarkerShape.{values.GetText("marker", nameof(MarkerShape.Circle))}).Sizes({data}.Sizes)";
        if (Colored.Contains(series.Data.Sample))
        {
            code += $".Colors({data}.Colors).Border(default, 0)";
        }

        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            code += $".Fill({CodeText.Color(color)})";
        }

        return code + Values(values) + ")";
    }

    private static void ApplyBubbles(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var tuple = (System.Runtime.CompilerServices.ITuple)data;
        var (points, sizes) = ((DataSource<double, double>)tuple[0]!, (float[])tuple[1]!);
        chart.AddScatter(points, scatter =>
        {
            scatter.Name(series.Name).XAxis(axes.X).YAxis(axes.Y).Marker(Enum.Parse<MarkerShape>(values.GetText("marker", nameof(MarkerShape.Circle)))).Sizes(sizes);
            if (tuple.Length > 2 && tuple[2] is Rgba8[] colors)
            {
                scatter.Colors(colors).Border(default, 0);
            }

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
            (x, y) => WithGradient(new BarSeries((double[][])data, x, y)
            {
                Name = series.Name,
                Layout = Enum.Parse<BarLayout>(values.GetText("layout", nameof(BarLayout.Grouped))),
                BarWidth = values.GetNumber("width", 0.8),
                IsHorizontal = values.GetFlag("horizontal", false),
                CornerRadius = values.GetNumber("corners", 0),
                ShadowColor = Shadow(values),
                ShowsValues = values.GetFlag("values", false),
            }, values, ((double[][])data).Length),
            axes.X,
            axes.Y);
    }

    private static void ApplyHeatmap(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var grid = (HeatmapData)data;
        chart.AddHeatmap(grid, heatmap => heatmap
            .Name(series.Name)
            .Cells(0, values.GetNumber("size", 10) / grid.Width, 0, values.GetNumber("size", 10) / grid.Height)
            .Interpolated(values.GetFlag("interpolated", false))
            .ColorAxis(axis => ColorScale(axis.Colormap(Colormap(values.GetText("colormap", "Viridis"))), values))
            .Values(values.GetFlag("values", false)));
    }
}
