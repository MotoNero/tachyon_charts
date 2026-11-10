using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Configurator;

/// <summary>A kind of annotation the configurator offers: its parameters, how it is made and how it is written.</summary>
/// <param name="Id">The identifier in <see cref="AnnotationSpec.Type"/>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Parameters">Its parameters.</param>
/// <param name="Create">Makes the annotation on the axes of a chart.</param>
/// <param name="Code">Writes the same as C#: the expression of the annotation from a chart called <c>c</c>.</param>
internal sealed record AnnotationInfo(string Id, string Name, IReadOnlyList<ParameterInfo> Parameters, Func<ChartSurface, SpecValues, ChartOverlay> Create, Func<SpecValues, string> Code);

/// <summary>The annotations: lines, a band, a box and text, on the first axes of the chart.</summary>
internal static class AnnotationCatalog
{
    // The axes of the annotations: the first X axis and the first value axis, also with the X axis downwards.
    private const string XCode = "c.FirstXAxis";
    private const string YCode = "c.IsXDown ? c.HorizontalAxes[0] : c.Panes[0].Axes[0]";

    private static readonly ParameterInfo _color = new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the usual color." };
    private static readonly ParameterInfo _label = new("label", "Label", ParameterKind.Text, string.Empty);

    public static IReadOnlyList<AnnotationInfo> All { get; } =
    [
        new("hline", "Horizontal line", [new("y", "Value", ParameterKind.Number, 0.5), _label, _color],
            (c, v) => Colored(new HorizontalLineAnnotation(Y(c)) { Y = v.GetNumber("y", 0.5), Label = Label(v) }, v),
            v => $"new HorizontalLineAnnotation({YCode}) {{ Y = {CodeText.Number(v.GetNumber("y", 0.5))}{LabelCode(v)}{ColorCode(v)} }}"),
        new("vline", "Vertical line", [new("x", "Place", ParameterKind.Number, 5.0), _label, _color],
            (c, v) => Colored(new VerticalLineAnnotation(c.FirstXAxis) { X = v.GetNumber("x", 5), Label = Label(v) }, v),
            v => $"new VerticalLineAnnotation({XCode}) {{ X = {CodeText.Number(v.GetNumber("x", 5))}{LabelCode(v)}{ColorCode(v)} }}"),
        new("band", "Band", [new("from", "From", ParameterKind.Number, 2.0), new("to", "To", ParameterKind.Number, 4.0), new("color", "Color", ParameterKind.Color, "#F0BE5A30") { Description = "A band covers what lies behind it; a color with little opacity keeps that visible." }],
            (c, v) =>
            {
                var band = new BandAnnotation(c.FirstXAxis);
                band.SetRange(v.GetNumber("from", 2), v.GetNumber("to", 4));
                return Colored(band, v, "#F0BE5A30");
            },
            v => $"Band(new BandAnnotation({XCode}), {CodeText.Number(v.GetNumber("from", 2))}, {CodeText.Number(v.GetNumber("to", 4))})"),
        new("box", "Box", [new("x1", "Left", ParameterKind.Number, 6.0), new("y1", "Bottom", ParameterKind.Number, -0.5), new("x2", "Right", ParameterKind.Number, 8.0), new("y2", "Top", ParameterKind.Number, 0.5), _color],
            (c, v) =>
            {
                var box = new BoxAnnotation(c.FirstXAxis, Y(c));
                box.SetCorners(v.GetNumber("x1", 6), v.GetNumber("y1", -0.5), v.GetNumber("x2", 8), v.GetNumber("y2", 0.5));
                return Colored(box, v);
            },
            v => $"Box(new BoxAnnotation({XCode}, {YCode}), {CodeText.Number(v.GetNumber("x1", 6))}, {CodeText.Number(v.GetNumber("y1", -0.5))}, {CodeText.Number(v.GetNumber("x2", 8))}, {CodeText.Number(v.GetNumber("y2", 0.5))})"),
        new("text", "Text", [new("x", "Place", ParameterKind.Number, 1.0), new("y", "Value", ParameterKind.Number, 0.8), new("text", "Text", ParameterKind.Text, "Note"), new("size", "Font size", ParameterKind.Number, 13.0) { Minimum = 6, Maximum = 72 }, _color],
            (c, v) =>
            {
                var text = new TextAnnotation(c.FirstXAxis, Y(c)) { Text = v.GetText("text", "Note"), FontSize = (float)v.GetNumber("size", 13) };
                text.MoveTo(v.GetNumber("x", 1), v.GetNumber("y", 0.8));
                return Colored(text, v);
            },
            v => $"Text(new TextAnnotation({XCode}, {YCode}) {{ Text = {CodeText.String(v.GetText("text", "Note"))}, FontSize = {CodeText.Number(v.GetNumber("size", 13))}f }}, {CodeText.Number(v.GetNumber("x", 1))}, {CodeText.Number(v.GetNumber("y", 0.8))})"),
    ];

    /// <summary>
    /// Places a new annotation in the part of the chart that shows, off its middle lines, where instruments draw their
    /// zero line and trigger point.
    /// </summary>
    /// <param name="annotation">The annotation.</param>
    /// <param name="x">The horizontal range that shows.</param>
    /// <param name="y">The vertical range that shows.</param>
    internal static void Place(AnnotationSpec annotation, Core.Numerics.DoubleRange x, Core.Numerics.DoubleRange y)
    {
        double X(double share) => Round(x.Min + ((x.Max - x.Min) * share), x);
        double Y(double share) => Round(y.Min + ((y.Max - y.Min) * share), y);
        var values = annotation.Parameters;
        switch (annotation.Type)
        {
            case "hline":
                values.Set("y", Y(0.7));
                break;
            case "vline":
                values.Set("x", X(0.25));
                break;
            case "band":
                values.Set("from", X(0.55));
                values.Set("to", X(0.7));
                break;
            case "box":
                values.Set("x1", X(0.35));
                values.Set("x2", X(0.65));
                values.Set("y1", Y(0.35));
                values.Set("y2", Y(0.65));
                break;
            case "text":
                values.Set("x", X(0.1));
                values.Set("y", Y(0.85));
                break;
        }
    }

    // Round to three significant digits of the range, so that the field shows a plain number such as -40 or 1.5E9.
    private static double Round(double value, Core.Numerics.DoubleRange range)
    {
        var span = Math.Abs(range.Max - range.Min);
        if (span <= 0 || !double.IsFinite(span))
        {
            return value;
        }

        var step = Math.Pow(10, Math.Floor(Math.Log10(span)) - 2);
        return Math.Round(value / step) * step;
    }

    /// <summary>Writes the helper methods the code of band, box and text needs, which set what has no property.</summary>
    /// <returns>The methods, indented by four spaces.</returns>
    public static string Helpers() => """

            private static BandAnnotation Band(BandAnnotation band, double from, double to)
            {
                band.SetRange(from, to);
                return band;
            }

            private static BoxAnnotation Box(BoxAnnotation box, double x1, double y1, double x2, double y2)
            {
                box.SetCorners(x1, y1, x2, y2);
                return box;
            }

            private static TextAnnotation Text(TextAnnotation text, double x, double y)
            {
                text.MoveTo(x, y);
                return text;
            }

            private static T Colored<T>(T annotation, Rgba8 color)
                where T : Annotation
            {
                annotation.Color = color;
                return annotation;
            }

        """;

    private static AxisBase Y(ChartSurface chart) => chart.IsXDown ? chart.HorizontalAxes[0] : chart.Panes[0].Axes[0];

    private static string? Label(SpecValues values) => values.GetText("label", string.Empty) is { Length: > 0 } label ? label : null;

    private static string LabelCode(SpecValues values) => Label(values) is { } label ? $", Label = {CodeText.String(label)}" : string.Empty;

    private static string ColorCode(SpecValues values) => SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? $", Color = {CodeText.Color(color)}" : string.Empty;

    private static Annotation Colored(Annotation annotation, SpecValues values, string standard = "")
    {
        if (SpecColor.TryParse(values.GetText("color", standard), out var color))
        {
            annotation.Color = color;
        }

        return annotation;
    }

    /// <summary>Writes an annotation with its color: the color wraps helpers that cannot take it in an initializer.</summary>
    /// <param name="info">The kind.</param>
    /// <param name="values">Its values.</param>
    /// <returns>The expression.</returns>
    public static string Expression(AnnotationInfo info, SpecValues values)
    {
        var code = info.Code(values);
        var standard = (string)info.Parameters.First(parameter => parameter.Name == "color").Default;
        return info.Id is "band" or "box" or "text" && SpecColor.TryParse(values.GetText("color", standard), out var color)
            ? $"Colored({code}, {CodeText.Color(color)})"
            : code;
    }
}
