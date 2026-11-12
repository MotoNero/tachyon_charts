using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The scientific types and rasters: uneven and masked heatmaps, contours, spectrograms, polar and radar charts,
/// ternary, Smith and parallel coordinates, and polygons.
/// </summary>
internal static class ScienceTypes
{
    // The spokes of a polar chart: evenly around, named by their angle.
    private static string[] Degrees(SpecValues values)
    {
        var count = Math.Clamp((int)values.GetNumber("spokes", 12), 3, 72);
        return [.. Enumerable.Range(0, count).Select(i => (i * 360.0 / count).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "°")];
    }
    private static readonly string[] _parts = ["Line", "Area", "Markers", "Area, Line", "Line, Markers", "Area, Line, Markers"];
    private static string DegreesCode(SpecValues values) => "[" + string.Join(", ", Degrees(values).Select(CodeText.String)) + "]";

    private static ParameterInfo Colormap(string standard) => new("colormap", "Colormap", ParameterKind.Choice, standard) { Choices = CoreModule.Colormaps };

    private static ParameterInfo Color => new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the type." };

    private static ParameterInfo Thickness => new("thickness", "Thickness", ParameterKind.Number, 1.5) { Minimum = 0.5, Maximum = 10 };

    public static IReadOnlyList<ChartTypeInfo> Create() =>
    [
        UnevenHeatmap(),
        MaskedHeatmap(),
        Contour(),
        Spectrogram(),
        Polar(),
        Radar(),
        Ternary(),
        Smith(),
        Parallel(),
        Polygons(),
    ];

    private static readonly ParameterInfo _missingColor = new("missingColor", "Color of missing values", ParameterKind.Color, string.Empty) { Description = "Empty to leave them clear." };

    private static readonly ParameterInfo _logColors = new("logColors", "Colors on a logarithmic scale", ParameterKind.Flag, false);

    private static readonly ParameterInfo _colorRange = new("colorRange", "Range of the colors", ParameterKind.Text, string.Empty) { Description = "From..to, for example -0.1..1.1; empty to follow the values." };

    // A fixed range of the colors, when one is given.
    private static ColorAxisBuilder Ranged(ColorAxisBuilder axis, SpecValues values) =>
        Range(values) is { } range ? axis.Range(range.From, range.To) : axis;

    private static string RangedCode(SpecValues values) =>
        Range(values) is { } range ? $".Range({CodeText.Number(range.From)}, {CodeText.Number(range.To)})" : string.Empty;

    private static (double From, double To)? Range(SpecValues values)
    {
        var parts = values.GetText("colorRange", string.Empty).Split("..", StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var from)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var to)
            && to > from ? (from, to) : null;
    }

    private static ChartTypeInfo UnevenHeatmap() => new(
        "unevenheatmap",
        "Uneven heatmap",
        ChartCategory.TwoD,
        DataShape.UnevenGrid,
        "uneven",
        [Colormap("Turbo"), _missingColor, _logColors, CoreModule.ValuesParameter],
        (chart, series, data, _) =>
        {
            var (grid, x, y) = ((HeatmapData, double[], double[]))data;
            chart.AddHeatmap(grid, heatmap => heatmap.Name(series.Name).Edges(x, y)
                .ColorAxis(axis => CoreModule.ColorScale(axis.Colormap(CoreModule.Colormap(series.Parameters.GetText("colormap", "Turbo"))), series.Parameters))
                .Values(series.Parameters.GetFlag("values", false)));
        },
        (series, data, _) => $".AddHeatmap({data}.Data, h => h.Name({CodeText.String(series.Name)}).Edges({data}.XEdges, {data}.YEdges).ColorAxis(a => a.Colormap(Colormaps.{series.Parameters.GetText("colormap", "Turbo")}){CoreModule.ColorScaleCode(series.Parameters)}){Values(series)})")
    {
        Usings = ["Tachyon.Charts.Core.Colors"],
    };

    private static ChartTypeInfo MaskedHeatmap() => new(
        "maskedheatmap",
        "Heatmap in an area",
        ChartCategory.TwoD,
        DataShape.MaskedGrid,
        "region",
        [Colormap("Turbo"), new("outline", "Outline of the area", ParameterKind.Flag, true), CoreModule.ValuesParameter],
        (chart, series, data, _) =>
        {
            var (grid, region) = ((HeatmapData, Polygon))data;
            chart.AddHeatmap(grid, heatmap => heatmap.Name(series.Name).Cells(0, 10.0 / grid.Width, 0, 10.0 / grid.Height).Mask([region])
                .ColorAxis(axis => axis.Colormap(CoreModule.Colormap(series.Parameters.GetText("colormap", "Turbo"))))
                .Values(series.Parameters.GetFlag("values", false)));
            if (series.Parameters.GetFlag("outline", true))
            {
                chart.Polygons([region], outline => outline.FillAlpha = 0);
            }
        },
        (series, data, _) => $".AddHeatmap({data}.Data, h => h.Name({CodeText.String(series.Name)}).Cells(0, 10.0 / {data}.Data.Width, 0, 10.0 / {data}.Data.Height).Mask([{data}.Region]).ColorAxis(a => a.Colormap(Colormaps.{series.Parameters.GetText("colormap", "Turbo")})){Values(series)})"
            + (series.Parameters.GetFlag("outline", true) ? $".Polygons([{data}.Region], s => s.FillAlpha = 0)" : string.Empty))
    {
        Usings = ["Tachyon.Charts.Core.Colors", "Tachyon.Charts.Core.Series"],
    };

    // The levels run evenly from the first to the last; the code writes them as numbers.
    private static ChartTypeInfo Contour() => new(
        "contour",
        "Contour",
        ChartCategory.TwoD,
        DataShape.Grid,
        "peaks",
        [
            Colormap("Viridis"),
            new("from", "First level", ParameterKind.Number, 0.0),
            new("to", "Last level", ParameterKind.Number, 1.0),
            new("levels", "Number of levels", ParameterKind.Integer, 11.0) { Minimum = 2, Maximum = 100 },
            new("filled", "Filled", ParameterKind.Flag, true),
            new("lines", "Lines", ParameterKind.Flag, true),
            new("labels", "Labels on the lines", ParameterKind.Flag, true),
            new("size", "Extent", ParameterKind.Number, 10.0) { Minimum = 1e-9, Description = "The grid covers 0 to this value on both axes." },
        ],
        (chart, series, data, _) =>
        {
            var grid = (HeatmapData)data;
            var values = series.Parameters;
            chart.AddContour(grid, contour =>
            {
                var size = values.GetNumber("size", 10);
                contour.Name(series.Name).Cells(0, size / grid.Width, 0, size / grid.Height).Levels(Levels(values))
                    .ColorAxis(axis => axis.Colormap(CoreModule.Colormap(values.GetText("colormap", "Viridis"))));
                if (values.GetFlag("filled", true))
                {
                    contour.Filled();
                }

                if (!values.GetFlag("lines", true))
                {
                    contour.WithoutLines();
                }

                if (values.GetFlag("labels", true))
                {
                    contour.Labels();
                }
            });
        },
        (series, data, _) =>
        {
            var values = series.Parameters;
            var size = CodeText.Number(values.GetNumber("size", 10));
            return $".AddContour({data}, c => c.Name({CodeText.String(series.Name)}).Cells(0, {size} / (double){data}.Width, 0, {size} / (double){data}.Height).Levels({string.Join(", ", Levels(values).Select(CodeText.Number))}).ColorAxis(a => a.Colormap(Colormaps.{values.GetText("colormap", "Viridis")}))"
                + (values.GetFlag("filled", true) ? ".Filled()" : string.Empty)
                + (values.GetFlag("lines", true) ? string.Empty : ".WithoutLines()")
                + (values.GetFlag("labels", true) ? ".Labels()" : string.Empty) + ")";
        })
    {
        Usings = ["Tachyon.Charts.Core.Colors"],
    };

    private static ChartTypeInfo Spectrogram() => new(
        "spectrogram",
        "Spectrogram",
        ChartCategory.TwoD,
        DataShape.Spectrogram,
        "waves",
        [Colormap("Plasma"), _colorRange, _missingColor],
        (chart, series, data, _) => chart.AddSpectrogram((SpectrogramData)data, spectrogram => spectrogram.Name(series.Name)
            .ColorAxis(axis => Ranged(CoreModule.ColorScale(axis.Colormap(CoreModule.Colormap(series.Parameters.GetText("colormap", "Plasma"))), series.Parameters), series.Parameters))),
        (series, data, _) => $".AddSpectrogram({data}, s => s.Name({CodeText.String(series.Name)}).ColorAxis(a => a.Colormap(Colormaps.{series.Parameters.GetText("colormap", "Plasma")}){CoreModule.ColorScaleCode(series.Parameters)}{RangedCode(series.Parameters)}))")
    {
        Usings = ["Tachyon.Charts.Core.Colors"],
    };

    private static ChartTypeInfo Polar()
    {
        var recipe = new SeriesRecipe(
            typeof(PolarSeries),
            [
                new("radius", "Outer ring", ParameterKind.Number, 10.0) { Minimum = 1e-9 },
                new("spokes", "Spokes", ParameterKind.Integer, 12.0) { Minimum = 3, Maximum = 72, Description = "Lines from the middle, evenly around, named by their angle." },
                new("parts", "Show", ParameterKind.Choice, "Line") { Choices = _parts },
                new("closed", "Closed", ParameterKind.Flag, false),
                Thickness,
                Color,
            ],
            ("parts", nameof(PolarSeries.Parts)),
            ("closed", nameof(PolarSeries.IsClosed)),
            ("thickness", nameof(PolarSeries.Thickness)),
            ("color", nameof(PolarSeries.Color)));
        return new ChartTypeInfo(
            "polar",
            "Polar",
            ChartCategory.TwoD,
            DataShape.Polar,
            "lobe",
            recipe.Parameters,
            (chart, series, data, _) =>
            {
                var (angles, values) = ((double[], double[]))data;
                chart.Polar(series.Parameters.GetNumber("radius", 10), Degrees(series.Parameters), (x, y) =>
                {
                    var created = new PolarSeries(angles, values, x, y);
                    recipe.Set(created, series);
                    return created;
                });
            },
            (series, data, _) => $".Polar({CodeText.Number(series.Parameters.GetNumber("radius", 10))}, {DegreesCode(series.Parameters)}, {recipe.Factory($"new PolarSeries({data}.Angles, {data}.Values, x, y)", series)})")
        {
            Usings = recipe.Usings,
            OwnsAxes = true,
        };
    }

    private static ChartTypeInfo Radar()
    {
        var recipe = new SeriesRecipe(
            typeof(PolarSeries),
            [new("radius", "Outer ring", ParameterKind.Number, 40.0) { Minimum = 1e-9 }, Thickness, Color],
            ("thickness", nameof(PolarSeries.Thickness)),
            ("color", nameof(PolarSeries.Color)));
        return new ChartTypeInfo(
            "radar",
            "Radar",
            ChartCategory.TwoD,
            DataShape.Slices,
            "mix",
            recipe.Parameters,
            (chart, series, data, _) =>
            {
                var (values, names) = ((double[], string[]))data;
                chart.Polar(series.Parameters.GetNumber("radius", 40), names, (x, y) =>
                {
                    var created = PolarSeries.Radar(values, x, y);
                    recipe.Set(created, series);
                    return created;
                });
            },
            (series, data, _) => $".Polar({CodeText.Number(series.Parameters.GetNumber("radius", 40))}, {data}.Names, {recipe.Factory($"PolarSeries.Radar({data}.Values, x, y)", series)})")
        {
            Usings = recipe.Usings,
            OwnsAxes = true,
        };
    }

    private static ChartTypeInfo Ternary() => new(
        "ternary",
        "Ternary",
        ChartCategory.TwoD,
        DataShape.Ternary,
        "mixture",
        [
            new("top", "Top corner", ParameterKind.Text, "A"),
            new("left", "Left corner", ParameterKind.Text, "B"),
            new("right", "Right corner", ParameterKind.Text, "C"),
        ],
        (chart, series, data, _) =>
        {
            var (a, b, c) = ((double[], double[], double[]))data;
            var values = series.Parameters;
            chart.Ternary(a, b, c, (values.GetText("top", "A"), values.GetText("left", "B"), values.GetText("right", "C")));
        },
        (series, data, _) =>
        {
            var values = series.Parameters;
            return $".Ternary({data}.A, {data}.B, {data}.C, ({CodeText.String(values.GetText("top", "A"))}, {CodeText.String(values.GetText("left", "B"))}, {CodeText.String(values.GetText("right", "C"))}))";
        })
    {
        Usings = ["Tachyon.Charts.Core.Series"],
        OwnsAxes = true,
    };

    private static ChartTypeInfo Smith() => new(
        "smith",
        "Smith chart",
        ChartCategory.TwoD,
        DataShape.Smith,
        "impedance",
        [
            new("marker", "Marker at point", ParameterKind.Integer, 100.0) { Minimum = -1, Maximum = 10_000_000, Description = "-1 for none." },
            new("reference", "Reference impedance in Ω", ParameterKind.Number, 50.0) { Minimum = 1e-9 },
        ],
        (chart, series, data, _) =>
        {
            var (resistance, reactance) = ((double[], double[]))data;
            chart.Smith(resistance, reactance, Math.Min((int)series.Parameters.GetNumber("marker", 100), resistance.Length - 1), series.Parameters.GetNumber("reference", 50));
        },
        (series, data, _) => $".Smith({data}.Resistance, {data}.Reactance, Math.Min({(int)series.Parameters.GetNumber("marker", 100)}, {data}.Resistance.Length - 1), {CodeText.Number(series.Parameters.GetNumber("reference", 50))})")
    {
        Usings = ["Tachyon.Charts.Core.Series"],
        OwnsAxes = true,
    };

    private static ChartTypeInfo Parallel() => new(
        "parallel",
        "Parallel coordinates",
        ChartCategory.TwoD,
        DataShape.Groups,
        "samples",
        [
            new("names", "Names of the columns", ParameterKind.Text, "Speed, Price, Quality, Service, Range") { Description = "One per column, separated by commas." },
            Color,
        ],
        (chart, series, data, _) =>
        {
            chart.ParallelCoordinates((double[][])data, Names(series), SpecColor.TryParse(series.Parameters.GetText("color", string.Empty), out var color) ? color : null);
        },
        (series, data, _) => SpecColor.TryParse(series.Parameters.GetText("color", string.Empty), out var color)
            ? $".ParallelCoordinates({data}, {NamesCode(series)}, {CodeText.Color(color)})"
            : $".ParallelCoordinates({data}, {NamesCode(series)})")
    {
        Usings = ["Tachyon.Charts.Core.Series"],
        OwnsAxes = true,
    };

    private static ChartTypeInfo Polygons() => new(
        "polygons",
        "Polygons",
        ChartCategory.TwoD,
        DataShape.Polygons,
        "shapes",
        [Thickness, new("fill", "Fill opacity", ParameterKind.Integer, 160.0) { Minimum = 0, Maximum = 255 }],
        (chart, series, data, axes) => chart.AddSeries(
            (x, y) => new PolygonSeries((List<Polygon>)data, x, y)
            {
                Name = series.Name,
                Thickness = series.Parameters.GetNumber("thickness", 1.5),
                FillAlpha = (byte)series.Parameters.GetNumber("fill", 160),
            },
            axes.X,
            axes.Y),
        (series, data, axes) => $".AddSeries((x, y) => new PolygonSeries({data}, x, y) {{ Name = {CodeText.String(series.Name)}, Thickness = {CodeText.Number(series.Parameters.GetNumber("thickness", 1.5))}, FillAlpha = {(int)series.Parameters.GetNumber("fill", 160)} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})")
    {
        Usings = ["Tachyon.Charts.Core.Series"],
    };

    private static double[] Levels(SpecValues values)
    {
        var (from, to, count) = (values.GetNumber("from", 0), values.GetNumber("to", 1), (int)values.GetNumber("levels", 11));
        var levels = new double[count];
        for (var i = 0; i < count; i++)
        {
            levels[i] = from + (i * (to - from) / (count - 1));
        }

        return levels;
    }

    // The names as given; a column without one stays without.
    private static string[] Names(SeriesSpec series) =>
        series.Parameters.GetText("names", string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string NamesCode(SeriesSpec series) => "[" + string.Join(", ", Names(series).Select(CodeText.String)) + "]";

    private static string Values(SeriesSpec series) => series.Parameters.GetFlag("values", false) ? ".Values()" : string.Empty;
}
