using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>A kind of annotation the configurator offers: its parameters, how it is made and how it is written.</summary>
/// <param name="Id">The identifier in <see cref="AnnotationSpec.Type"/>.</param>
/// <param name="Name">The name shown.</param>
/// <param name="Parameters">Its parameters.</param>
/// <param name="Create">Makes the annotation on the axes of a chart.</param>
/// <param name="Code">
/// Writes the same as C#: the expression of the annotation from a chart called <c>c</c>; the second argument is a
/// further member of its initializer, or empty.
/// </param>
internal sealed record AnnotationInfo(string Id, string Name, IReadOnlyList<ParameterInfo> Parameters, Func<ChartSurface, SpecValues, ChartOverlay> Create, Func<SpecValues, string, string> Code);

/// <summary>The annotations: lines, a band, a box, text and a free line, on the first axes of the chart.</summary>
internal static class AnnotationCatalog
{
    // The axes of the annotations: the first X axis and the first value axis, also with the X axis downwards.
    private const string XCode = "c.FirstXAxis";
    private const string YCode = "c.IsXDown ? c.HorizontalAxes[0] : c.Panes[0].Axes[0]";

    private static readonly ParameterInfo _color = new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the usual color." };
    private static readonly ParameterInfo _label = new("label", "Label", ParameterKind.Text, string.Empty);

    // How the label and the lines look; left as they are, an annotation draws as it always did.
    private static readonly ParameterInfo[] _look =
    [
        new("labelPlace", "Label along", ParameterKind.Choice, nameof(LabelPlace.Auto)) { Choices = Enum.GetNames<LabelPlace>(), Description = "Where the label stands along its line: at the start, in the middle or at the end." },
        new("labelSide", "Label beside", ParameterKind.Choice, nameof(LabelSide.Auto)) { Choices = Enum.GetNames<LabelSide>(), Description = "Before: above a level line, left of an upright one; After: the other side." },
        new("labelOffset", "Label distance", ParameterKind.Number, 0.0) { Minimum = -200, Maximum = 200, Description = "A further distance from the line, in device-independent units." },
        new("labelSize", "Label size", ParameterKind.Number, 0.0) { Minimum = 0, Maximum = 72, Description = "The size of the text; 0 for that of the chart." },
        new("labelColor", "Label text color", ParameterKind.Color, string.Empty) { Description = "Empty for the background of the chart on a plate, the color of the annotation without one." },
        new("labelFill", "Label plate", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the annotation; a transparent color leaves the plate out." },
        new("labelBorder", "Label border", ParameterKind.Color, string.Empty) { Description = "Empty for none." },
        new("labelBorderWidth", "Label border width", ParameterKind.Number, 1.0) { Minimum = 0.5, Maximum = 10 },
        new("labelTurn", "Label turn", ParameterKind.Choice, nameof(LabelTurn.Level)) { Choices = Enum.GetNames<LabelTurn>(), Description = "Level, along the line (upright at an upright line), or by the angle." },
        new("labelAngle", "Label angle", ParameterKind.Number, 0.0) { Minimum = -180, Maximum = 180, Description = "In degrees, counterclockwise, for the turn Angle." },
        new("labelIcon", "Label sign", ParameterKind.Choice, nameof(LabelIcon.None)) { Choices = Enum.GetNames<LabelIcon>() },
        new("dash", "Line", ParameterKind.Choice, nameof(LineDash.Solid)) { Choices = Enum.GetNames<LineDash>() },
        new("thickness", "Line width", ParameterKind.Number, 0.0) { Minimum = 0, Maximum = 20, Description = "0 for that of the annotation; a band draws its edges only with a width." },
    ];

    public static IReadOnlyList<AnnotationInfo> All { get; } =
    [
        new("hline", "Horizontal line", [new("y", "Value", ParameterKind.Number, 0.5), _label, _color, .. _look],
            (c, v) => Colored(new HorizontalLineAnnotation(Y(c)) { Y = v.GetNumber("y", 0.5), Label = Label(v) }, v),
            (v, more) => $"new HorizontalLineAnnotation({YCode}){Init($"Y = {CodeText.Number(v.GetNumber("y", 0.5))}", LabelCode(v), ColorCode(v), more)}"),
        new("vline", "Vertical line", [new("x", "Place", ParameterKind.Number, 5.0), _label, _color, .. _look],
            (c, v) => Colored(new VerticalLineAnnotation(c.FirstXAxis) { X = v.GetNumber("x", 5), Label = Label(v) }, v),
            (v, more) => $"new VerticalLineAnnotation({XCode}){Init($"X = {CodeText.Number(v.GetNumber("x", 5))}", LabelCode(v), ColorCode(v), more)}"),
        new("band", "Band", [new("from", "From", ParameterKind.Number, 2.0), new("to", "To", ParameterKind.Number, 4.0), new("color", "Color", ParameterKind.Color, "#F0BE5A30") { Description = "A band covers what lies behind it; a color with little opacity keeps that visible." }, _label, .. _look],
            (c, v) =>
            {
                var band = new BandAnnotation(c.FirstXAxis) { Label = Label(v) };
                band.SetRange(v.GetNumber("from", 2), v.GetNumber("to", 4));
                return Colored(band, v, "#F0BE5A30");
            },
            (v, more) => $"Band(new BandAnnotation({XCode}){Init(LabelCode(v), more)}, {CodeText.Number(v.GetNumber("from", 2))}, {CodeText.Number(v.GetNumber("to", 4))})"),
        new("box", "Box", [new("x1", "Left", ParameterKind.Number, 6.0), new("y1", "Bottom", ParameterKind.Number, -0.5), new("x2", "Right", ParameterKind.Number, 8.0), new("y2", "Top", ParameterKind.Number, 0.5), _label, _color, .. _look],
            (c, v) =>
            {
                var box = new BoxAnnotation(c.FirstXAxis, Y(c)) { Label = Label(v) };
                box.SetCorners(v.GetNumber("x1", 6), v.GetNumber("y1", -0.5), v.GetNumber("x2", 8), v.GetNumber("y2", 0.5));
                return Colored(box, v);
            },
            (v, more) => $"Box(new BoxAnnotation({XCode}, {YCode}){Init(LabelCode(v), more)}, {CodeText.Number(v.GetNumber("x1", 6))}, {CodeText.Number(v.GetNumber("y1", -0.5))}, {CodeText.Number(v.GetNumber("x2", 8))}, {CodeText.Number(v.GetNumber("y2", 0.5))})"),
        new("text", "Text", [new("x", "Place", ParameterKind.Number, 1.0), new("y", "Value", ParameterKind.Number, 0.8), new("text", "Text", ParameterKind.Text, "Note"), new("size", "Font size", ParameterKind.Number, 13.0) { Minimum = 6, Maximum = 72 }, _color, .. _look],
            (c, v) =>
            {
                var text = new TextAnnotation(c.FirstXAxis, Y(c)) { Text = v.GetText("text", "Note"), FontSize = (float)v.GetNumber("size", 13) };
                text.MoveTo(v.GetNumber("x", 1), v.GetNumber("y", 0.8));
                return Colored(text, v);
            },
            (v, more) => $"Text(new TextAnnotation({XCode}, {YCode}){Init($"Text = {CodeText.String(v.GetText("text", "Note"))}", $"FontSize = {CodeText.Number(v.GetNumber("size", 13))}f", more)}, {CodeText.Number(v.GetNumber("x", 1))}, {CodeText.Number(v.GetNumber("y", 0.8))})"),
        new("line", "Line", [new("x1", "Start place", ParameterKind.Number, 2.0), new("y1", "Start value", ParameterKind.Number, -0.5), new("x2", "End place", ParameterKind.Number, 8.0), new("y2", "End value", ParameterKind.Number, 0.5), _label, _color, .. _look],
            (c, v) =>
            {
                var line = new LineAnnotation(c.FirstXAxis, Y(c)) { Label = Label(v) };
                line.SetEnds(v.GetNumber("x1", 2), v.GetNumber("y1", -0.5), v.GetNumber("x2", 8), v.GetNumber("y2", 0.5));
                return Colored(line, v);
            },
            (v, more) => $"Line(new LineAnnotation({XCode}, {YCode}){Init(LabelCode(v), ColorCode(v), more)}, {CodeText.Number(v.GetNumber("x1", 2))}, {CodeText.Number(v.GetNumber("y1", -0.5))}, {CodeText.Number(v.GetNumber("x2", 8))}, {CodeText.Number(v.GetNumber("y2", 0.5))})"),
    ];

    /// <summary>
    /// Reads back what a drag changed: the values of an annotation the catalog made, rounded to four significant digits
    /// of the range its axis shows, so that the fields show plain numbers and the code draws the same within a pixel.
    /// Call it on the thread of the chart.
    /// </summary>
    /// <param name="annotation">The annotation.</param>
    /// <returns>The kind and the values by parameter, or <see langword="null"/> for an annotation not from the catalog.</returns>
    internal static (string Kind, (string Name, double Value)[] Values)? Read(Annotation annotation) => annotation switch
    {
        HorizontalLineAnnotation line => ("hline", [("y", Fine(line.Y, line.YAxis))]),
        VerticalLineAnnotation line => ("vline", [("x", Fine(line.X, line.XAxis))]),
        BandAnnotation band => ("band", [("from", Fine(band.Start, band.XAxis ?? band.YAxis)), ("to", Fine(band.End, band.XAxis ?? band.YAxis))]),
        BoxAnnotation box => ("box", [("x1", Fine(box.First.X, box.XAxis)), ("y1", Fine(box.First.Y, box.YAxis)), ("x2", Fine(box.Second.X, box.XAxis)), ("y2", Fine(box.Second.Y, box.YAxis))]),
        TextAnnotation text => ("text", [("x", Fine(text.Position.X, text.XAxis)), ("y", Fine(text.Position.Y, text.YAxis))]),
        LineAnnotation line => ("line", [("x1", Fine(line.Start.X, line.XAxis)), ("y1", Fine(line.Start.Y, line.YAxis)), ("x2", Fine(line.End.X, line.XAxis)), ("y2", Fine(line.End.Y, line.YAxis))]),
        _ => null,
    };

    private static double Fine(double value, AxisBase? axis)
    {
        var range = axis?.VisibleRange ?? default;
        var span = Math.Abs(range.Max - range.Min);
        if (span <= 0 || !double.IsFinite(span) || !double.IsFinite(value))
        {
            return value;
        }

        var step = Math.Pow(10, Math.Floor(Math.Log10(span)) - 3);
        return Math.Round(Math.Round(value / step) * step, 12);
    }

    // An object initializer of the members that are not empty, or nothing.
    private static string Init(params string[] members)
    {
        var used = members.Where(member => member.Length > 0).ToArray();
        return used.Length == 0 ? string.Empty : " { " + string.Join(", ", used) + " }";
    }

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
            case "line":
                values.Set("x1", X(0.2));
                values.Set("y1", Y(0.3));
                values.Set("x2", X(0.6));
                values.Set("y2", Y(0.75));
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

    /// <summary>Writes the helper methods the code of band, box, text and line needs, which set what has no property.</summary>
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

            private static LineAnnotation Line(LineAnnotation line, double x1, double y1, double x2, double y2)
            {
                line.SetEnds(x1, y1, x2, y2);
                return line;
            }

            private static T Colored<T>(T annotation, Rgba8 color)
                where T : Annotation
            {
                annotation.Color = color;
                return annotation;
            }

        """;

    private static AxisBase Y(ChartSurface chart) => chart.IsXDown ? chart.HorizontalAxes[0] : chart.Panes[0].Axes[0];

    private static Rgba8? ColorOf(SpecValues values, string name) => SpecColor.TryParse(values.GetText(name, string.Empty), out var color) ? color : null;

    private static float? Positive(SpecValues values, string name) => values.GetNumber(name, 0) is > 0 and var value ? (float)value : null;

    private static T Choice<T>(SpecValues values, string name, T standard)
        where T : struct, Enum => Enum.TryParse<T>(values.GetText(name, standard.ToString()), out var value) ? value : standard;

    /// <summary>Reads the appearance the fields describe.</summary>
    /// <param name="values">The values of an annotation.</param>
    /// <returns>The appearance.</returns>
    internal static AnnotationAppearance Look(SpecValues values) => new()
    {
        LabelPlace = Choice(values, "labelPlace", LabelPlace.Auto),
        LabelSide = Choice(values, "labelSide", LabelSide.Auto),
        LabelOffset = values.GetNumber("labelOffset", 0),
        LabelSize = Positive(values, "labelSize"),
        LabelColor = ColorOf(values, "labelColor"),
        LabelFill = ColorOf(values, "labelFill"),
        LabelBorder = ColorOf(values, "labelBorder"),
        LabelBorderThickness = Positive(values, "labelBorderWidth") ?? 1,
        LabelTurn = Choice(values, "labelTurn", LabelTurn.Level),
        LabelAngle = values.GetNumber("labelAngle", 0),
        LabelIcon = Choice(values, "labelIcon", LabelIcon.None),
        Dash = Choice(values, "dash", LineDash.Solid),
        Thickness = Positive(values, "thickness"),
    };

    // The same as code: only what differs from an appearance left as it is, or nothing.
    private static string LookCode(SpecValues values)
    {
        var look = Look(values);
        var parts = new List<string>();
        void Add(bool differs, string member)
        {
            if (differs)
            {
                parts.Add(member);
            }
        }

        Add(look.LabelPlace != LabelPlace.Auto, $"LabelPlace = LabelPlace.{look.LabelPlace}");
        Add(look.LabelSide != LabelSide.Auto, $"LabelSide = LabelSide.{look.LabelSide}");
        Add(look.LabelOffset != 0, $"LabelOffset = {CodeText.Number(look.LabelOffset)}");
        Add(look.LabelSize is not null, $"LabelSize = {CodeText.Number(look.LabelSize ?? 0)}f");
        Add(look.LabelColor is not null, $"LabelColor = {CodeText.Color(look.LabelColor ?? default)}");
        Add(look.LabelFill is not null, $"LabelFill = {CodeText.Color(look.LabelFill ?? default)}");
        Add(look.LabelBorder is not null, $"LabelBorder = {CodeText.Color(look.LabelBorder ?? default)}");
        Add(look.LabelBorderThickness != 1, $"LabelBorderThickness = {CodeText.Number(look.LabelBorderThickness)}f");
        Add(look.LabelTurn != LabelTurn.Level, $"LabelTurn = LabelTurn.{look.LabelTurn}");
        Add(look.LabelAngle != 0, $"LabelAngle = {CodeText.Number(look.LabelAngle)}");
        Add(look.LabelIcon != LabelIcon.None, $"LabelIcon = LabelIcon.{look.LabelIcon}");
        Add(look.Dash != LineDash.Solid, $"Dash = LineDash.{look.Dash}");
        Add(look.Thickness is not null, $"Thickness = {CodeText.Number(look.Thickness ?? 0)}f");
        return parts.Count == 0 ? string.Empty : $"Appearance = new AnnotationAppearance {{ {string.Join(", ", parts)} }}";
    }

    private static string? Label(SpecValues values) => values.GetText("label", string.Empty) is { Length: > 0 } label ? label : null;

    private static string LabelCode(SpecValues values) => Label(values) is { } label ? $"Label = {CodeText.String(label)}" : string.Empty;

    private static string ColorCode(SpecValues values) => SpecColor.TryParse(values.GetText("color", string.Empty), out var color) ? $"Color = {CodeText.Color(color)}" : string.Empty;

    private static Annotation Colored(Annotation annotation, SpecValues values, string standard = "")
    {
        if (SpecColor.TryParse(values.GetText("color", standard), out var color))
        {
            annotation.Color = color;
        }

        annotation.Appearance = Look(values);
        return annotation;
    }

    /// <summary>Writes an annotation with its color: the color wraps helpers that cannot take it in an initializer.</summary>
    /// <param name="info">The kind.</param>
    /// <param name="values">Its values.</param>
    /// <param name="isEditable">Whether the annotation can be edited with the mouse.</param>
    /// <returns>The expression.</returns>
    public static string Expression(AnnotationInfo info, SpecValues values, bool isEditable)
    {
        var more = string.Join(", ", new[] { isEditable ? "IsEditable = true" : string.Empty, LookCode(values) }.Where(member => member.Length > 0));
        var code = info.Code(values, more);
        var standard = (string)info.Parameters.First(parameter => parameter.Name == "color").Default;
        return info.Id is "band" or "box" or "text" && SpecColor.TryParse(values.GetText("color", standard), out var color)
            ? $"Colored({code}, {CodeText.Color(color)})"
            : code;
    }
}
