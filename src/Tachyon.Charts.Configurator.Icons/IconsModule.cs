using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Icons;

namespace Tachyon.Charts.Configurator.Icons;

/// <summary>
/// The symbols of the icon package in the configurator: a field at every annotation to choose one of the Material Symbols
/// with a box to search and a picture of each, and its size. Without this module the configurator offers the built-in
/// signs only.
/// </summary>
public sealed class IconsModule : IChartModule
{
    private const string LabelSymbolKey = "labelSymbol";
    private const string LabelSymbolSize = "labelSymbolSize";
    private const string Color = "color";

    /// <summary>The choice without a symbol.</summary>
    internal const string NoSymbol = "None";

    /// <summary>The version of the icon package the generated projects refer to.</summary>
    public const string PackageVersion = "0.1.0";

    /// <inheritdoc />
    public IReadOnlyList<ChartTypeInfo> Types { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<SampleInfo> Samples { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<PageInfo> Pages { get; } = [new("AnnotationSymbols", SymbolPage)];

    /// <inheritdoc />
    public IReadOnlyList<AnnotationLookInfo> AnnotationLooks { get; } =
    [
        new(
            [
                new(LabelSymbolKey, "Label symbol", ParameterKind.Choice, NoSymbol) { Choices = [NoSymbol, .. MaterialSymbols.Names], ChoicePictures = SymbolPictures.Instance, Description = "A symbol of Material Symbols before the text, in place of the sign; type to search. Its color is that of the sign." },
                new(LabelSymbolSize, "Label symbol size", ParameterKind.Number, 0.0) { Minimum = 0, Maximum = 96, Description = "The size of the symbol; 0 for the height of the label." },
            ],
            (values, look) => look.LabelSymbol = SymbolOf(values),
            values => SymbolOf(values) is { } symbol ? [$"LabelSymbol = MaterialSymbols.Symbol({CodeText.String(values.GetText(LabelSymbolKey, NoSymbol))}{SizeCode(symbol)})"] : [],
            ["Tachyon.Charts.Icons"],
            "Tachyon.Charts.Icons",
            PackageVersion),
    ];

    private static string SizeCode(AnnotationSymbol symbol) => symbol.Size is { } size ? ", " + CodeText.Number(size) + "f" : string.Empty;

    // The symbol the values choose, or none.
    private static AnnotationSymbol? SymbolOf(SpecValues values)
    {
        var name = values.GetText(LabelSymbolKey, NoSymbol);
        var size = values.GetNumber(LabelSymbolSize, 0);
        if (name == NoSymbol || !MaterialSymbols.TryGetGlyph(name, out _))
        {
            return null;
        }

        return MaterialSymbols.Symbol(name, size > 0 ? (float)size : null);
    }

    // The gallery page with symbols: a thermometer, a lightning bolt and a wrench, each in its own color and size.
    private static ChartSpec SymbolPage(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Symbols at annotations",
            Theme = "TachyonDark",
            XAxes = { new AxisSpec { Id = "x", Title = "Time in s", Minimum = 0, Maximum = 10 } },
            YAxes = { new AxisSpec { Id = "y", Title = "Level", Minimum = -1.5, Maximum = 1.5 } },
            Modifiers = { new ModifierSpec { Type = "annotationedit" } },
        };
        var line = new SeriesSpec { Type = "line", Name = "Wave", Data = new DataSpec { Sample = "wave13", Count = 240 } };
        line.Parameters.Set(Color, "#50AAF0");
        line.Parameters.Set("thickness", 2.0);
        spec.Series.Add(line);
        spec.Annotations.Add(Annotation("hline", ("y", 1.1), ("label", "Too hot"), (Color, "#F05A5AFF"), ("labelPlace", "End"), (LabelSymbolKey, "thermostat"), ("labelColor", "#FFFFFFFF")));
        spec.Annotations.Add(Annotation("vline", ("x", 6.0), ("label", "Power on"), (Color, "#F0BE5AFF"), (LabelSymbolKey, "bolt"), (LabelSymbolSize, 20.0), ("labelIconColor", "#1E1E1EFF")));
        spec.Annotations.Add(Annotation("hline", ("y", -1.2), ("label", "Service due"), (Color, "#6ED282FF"), ("labelPlace", "Start"), (LabelSymbolKey, "build"), (LabelSymbolSize, 24.0), ("labelFill", "#00000000"), ("labelColor", "#6ED282FF")));
        return spec;
    }

    private static AnnotationSpec Annotation(string type, params (string Name, object Value)[] values)
    {
        var annotation = new AnnotationSpec { Type = type };
        foreach (var (name, value) in values)
        {
            if (value is double number)
            {
                annotation.Parameters.Set(name, number);
            }
            else
            {
                annotation.Parameters.Set(name, (string)value);
            }
        }

        return annotation;
    }
}
