using System.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Annotations;

/// <summary>Where a label stands along its line or edge.</summary>
public enum LabelPlace
{
    /// <summary>Where the annotation puts it by itself.</summary>
    Auto,

    /// <summary>At the start: the left end of a level line, the top of an upright one, the first end of a free line.</summary>
    Start,

    /// <summary>In the middle.</summary>
    Middle,

    /// <summary>At the end.</summary>
    End,
}

/// <summary>On which side of its line a label stands.</summary>
public enum LabelSide
{
    /// <summary>Where the annotation puts it by itself.</summary>
    Auto,

    /// <summary>Above a level line, right of an upright one, outside a band or box.</summary>
    Before,

    /// <summary>Below a level line, left of an upright one, inside a band or box.</summary>
    After,
}

/// <summary>How a label is turned.</summary>
public enum LabelTurn
{
    /// <summary>Level, as text usually reads.</summary>
    Level,

    /// <summary>In the direction of its line, readable from left or from below: upright at an upright line.</summary>
    AlongLine,

    /// <summary>By <see cref="AnnotationAppearance.LabelAngle"/>.</summary>
    Angle,
}

/// <summary>A small sign before the text of a label.</summary>
public enum LabelIcon
{
    /// <summary>No sign.</summary>
    None,

    /// <summary>A warning triangle.</summary>
    Warning,

    /// <summary>A round dot for a note.</summary>
    Info,

    /// <summary>An arrow pointing forward.</summary>
    Arrow,

    /// <summary>A square mark.</summary>
    Marker,
}

/// <summary>How a line is drawn.</summary>
public enum LineDash
{
    /// <summary>Without gaps.</summary>
    Solid,

    /// <summary>In dashes.</summary>
    Dashed,

    /// <summary>In dots.</summary>
    Dotted,
}

/// <summary>
/// How an annotation draws its label and its lines. Left as it is, each annotation draws as it always did: the label on
/// a plate in its color at its usual place.
/// </summary>
public sealed class AnnotationAppearance
{
    /// <summary>Gets or sets where the label stands along its line.</summary>
    public LabelPlace LabelPlace { get; set; }

    /// <summary>Gets or sets on which side of its line the label stands.</summary>
    public LabelSide LabelSide { get; set; }

    /// <summary>Gets or sets a further distance of the label from its line, in device-independent units.</summary>
    public double LabelOffset { get; set; }

    /// <summary>Gets or sets the size of the text, or <see langword="null"/> for the size of the labels of the chart.</summary>
    public float? LabelSize { get; set; }

    /// <summary>Gets or sets the font of the text: family, bold and italic; the default is the font of the chart.</summary>
    public TextFont LabelFont { get; set; }

    /// <summary>Gets or sets the color of the text, or <see langword="null"/>: the background of the chart on a plate, the color of the annotation without one.</summary>
    public Rgba8? LabelColor { get; set; }

    /// <summary>Gets or sets the plate behind the text, or <see langword="null"/> for the color of the annotation; a transparent color leaves the plate out.</summary>
    public Rgba8? LabelFill { get; set; }

    /// <summary>Gets or sets the border of the plate, or <see langword="null"/> for none.</summary>
    public Rgba8? LabelBorder { get; set; }

    /// <summary>Gets or sets the width of the border of the plate, in device-independent units.</summary>
    public float LabelBorderThickness { get; set; } = 1;

    /// <summary>Gets or sets how the label is turned.</summary>
    public LabelTurn LabelTurn { get; set; }

    /// <summary>Gets or sets the angle of the label in degrees, counterclockwise, for <see cref="LabelTurn.Angle"/>.</summary>
    public double LabelAngle { get; set; }

    /// <summary>Gets or sets a sign before the text.</summary>
    public LabelIcon LabelIcon { get; set; }

    /// <summary>Gets or sets the color of the sign, or <see langword="null"/> for that of the text.</summary>
    public Rgba8? LabelIconColor { get; set; }

    /// <summary>Gets or sets how the lines of the annotation are drawn.</summary>
    public LineDash Dash { get; set; }

    /// <summary>
    /// Gets or sets the width of the lines, or <see langword="null"/> for that of the annotation; for a band the width of
    /// its edges, which it draws only with this set.
    /// </summary>
    public float? Thickness { get; set; }

    /// <summary>Gets a value indicating whether the label draws as it always did.</summary>
    internal bool IsPlainLabel => LabelPlace == LabelPlace.Auto && LabelSide == LabelSide.Auto && LabelOffset == 0 && LabelSize is null && LabelFont.IsDefault && LabelColor is null
        && LabelFill is null && LabelBorder is null && LabelTurn == LabelTurn.Level && LabelIcon == LabelIcon.None;
}

/// <summary>Draws labels and lines of annotations as their appearance asks; allocates nothing.</summary>
internal static class AnnotationPainter
{
    private const float Padding = 4;
    private const float Gap = 4;

    /// <summary>Draws a line, solid, dashed or dotted.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="a">The start.</param>
    /// <param name="b">The end.</param>
    /// <param name="width">The width.</param>
    /// <param name="color">The color.</param>
    /// <param name="dash">The dash.</param>
    public static void Line(OverlayCanvas canvas, Vector2 a, Vector2 b, float width, Rgba8 color, LineDash dash)
    {
        if (dash == LineDash.Solid)
        {
            canvas.DrawLine(a, b, width, color);
            return;
        }

        var length = Vector2.Distance(a, b);
        if (length <= 0)
        {
            return;
        }

        var direction = (b - a) / length;
        var (on, off) = dash == LineDash.Dashed ? (6 * Math.Max(1, width), 4 * Math.Max(1, width)) : (Math.Max(1.5f, width), 3 * Math.Max(1, width));
        for (var at = 0f; at < length; at += on + off)
        {
            canvas.DrawLine(a + (direction * at), a + (direction * Math.Min(at + on, length)), width, color);
        }
    }

    /// <summary>Draws a label along a line from <paramref name="from"/> to <paramref name="to"/>.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="text">The text.</param>
    /// <param name="from">The start of the line.</param>
    /// <param name="to">The end of the line.</param>
    /// <param name="look">The appearance.</param>
    /// <param name="color">The color of the annotation.</param>
    /// <param name="place">The place where the annotation puts it by itself.</param>
    /// <param name="side">The side where the annotation puts it by itself.</param>
    /// <param name="defaultSize">The size of the text without one of the appearance, or <see langword="null"/> for that of the chart.</param>
    /// <param name="defaultFill">The plate without one of the appearance, or <see langword="null"/> for the color of the annotation.</param>
    public static void Label(OverlayCanvas canvas, string text, Vector2 from, Vector2 to, AnnotationAppearance look, Rgba8 color, LabelPlace place, LabelSide side, float? defaultSize = null, Rgba8? defaultFill = null)
    {
        var size = look.LabelSize ?? defaultSize ?? canvas.LabelSize;
        var along = to - from;
        var direction = along.LengthSquared() > 0 ? Vector2.Normalize(along) : Vector2.UnitX;
        var before = new Vector2(direction.Y, -direction.X);
        var normal = (look.LabelSide == LabelSide.Auto ? side : look.LabelSide) == LabelSide.Before ? before : -before;
        var t = (look.LabelPlace == LabelPlace.Auto ? place : look.LabelPlace) switch
        {
            LabelPlace.Start => 0f,
            LabelPlace.Middle => 0.5f,
            _ => 1f,
        };
        var anchor = from + (along * t);

        // The text reads from the left or from below; the angle turns clockwise on the screen.
        var angle = look.LabelTurn switch
        {
            LabelTurn.AlongLine => Readable(MathF.Atan2(direction.Y, direction.X)),
            LabelTurn.Angle => (float)(-look.LabelAngle * Math.PI / 180),
            _ => 0f,
        };
        var (u, v) = (new Vector2(MathF.Cos(angle), MathF.Sin(angle)), new Vector2(-MathF.Sin(angle), MathF.Cos(angle)));
        var height = canvas.GetLineHeight(size, look.LabelFont) + 2;
        var icon = look.LabelIcon == LabelIcon.None ? 0 : height * 0.8f;
        var width = canvas.MeasureWidth(text, size, look.LabelFont) + (Padding * 2) + icon;
        var (halfWidth, halfHeight) = (width / 2, height / 2);

        // The middle of the plate: beside the line by half its extent across it, and along it from the chosen end.
        float Reach(Vector2 towards) => (Math.Abs(Vector2.Dot(towards, u)) * halfWidth) + (Math.Abs(Vector2.Dot(towards, v)) * halfHeight);
        var center = anchor + (normal * (Gap + (float)look.LabelOffset + Reach(normal))) + (direction * (t == 0 ? Reach(direction) + Gap : t == 1 ? -(Reach(direction) + Gap) : 0));
        center = KeepInside(canvas, center, Reach(Vector2.UnitX), Reach(Vector2.UnitY));

        Vector2 Corner(float x, float y) => center + (u * x) + (v * y);
        var fill = look.LabelFill ?? defaultFill ?? color;
        var clip = canvas.Clip;
        canvas.Clip = null;
        if (fill.A > 0)
        {
            canvas.FillPolygon([Corner(-halfWidth, -halfHeight), Corner(halfWidth, -halfHeight), Corner(halfWidth, halfHeight), Corner(-halfWidth, halfHeight)], fill);
        }

        if (look.LabelBorder is { } border)
        {
            var corners = (Corner(-halfWidth, -halfHeight), Corner(halfWidth, -halfHeight), Corner(halfWidth, halfHeight), Corner(-halfWidth, halfHeight));
            canvas.DrawLine(corners.Item1, corners.Item2, look.LabelBorderThickness, border);
            canvas.DrawLine(corners.Item2, corners.Item3, look.LabelBorderThickness, border);
            canvas.DrawLine(corners.Item3, corners.Item4, look.LabelBorderThickness, border);
            canvas.DrawLine(corners.Item4, corners.Item1, look.LabelBorderThickness, border);
        }

        var ink = look.LabelColor ?? (fill.A > 0 ? canvas.BackgroundColor : color);
        if (icon > 0)
        {
            DrawIcon(canvas, look.LabelIcon, Corner(-halfWidth + Padding + (icon / 2), 0), icon * 0.8f, u, v, look.LabelIconColor ?? ink, fill.A > 0 ? fill with { A = 255 } : canvas.BackgroundColor);
        }

        var textMiddle = Corner((icon / 2) + 0, 0);
        if (angle == 0)
        {
            var textWidth = canvas.MeasureWidth(text, size, look.LabelFont);
            canvas.DrawText(text, textMiddle.X - (textWidth / 2), textMiddle.Y + (height / 2) - (height * 0.28), size, ink, look.LabelFont);
        }
        else
        {
            canvas.DrawTurnedText(text, textMiddle, angle, size, ink, look.LabelFont);
        }

        canvas.Clip = clip;
    }

    // Text turned by more than a quarter would stand on its head; it turns the other way round.
    // Text stays upright and reads upward along an upright line.
    private static float Readable(float angle) => angle > (MathF.PI / 2) - 0.001f ? angle - MathF.PI : angle < -(MathF.PI / 2) - 0.001f ? angle + MathF.PI : angle;

    private static Vector2 KeepInside(OverlayCanvas canvas, Vector2 center, float halfWidth, float halfHeight)
    {
        var (width, height) = ((float)canvas.ChartWidth, (float)canvas.ChartHeight);
        var x = width > (halfWidth * 2) + 4 ? Math.Clamp(center.X, halfWidth + 2, width - halfWidth - 2) : center.X;
        var y = height > (halfHeight * 2) + 4 ? Math.Clamp(center.Y, halfHeight + 2, height - halfHeight - 2) : center.Y;
        return new Vector2(x, y);
    }

    // The signs, drawn from triangles in the frame of the label.
    private static void DrawIcon(OverlayCanvas canvas, LabelIcon icon, Vector2 middle, float size, Vector2 u, Vector2 v, Rgba8 color, Rgba8 cut)
    {
        var half = size / 2;
        Vector2 At(float x, float y) => middle + (u * x) + (v * y);
        switch (icon)
        {
            case LabelIcon.Warning:
                canvas.FillPolygon([At(0, -half), At(half, half), At(-half, half)], color);

                // An exclamation mark cut out of the triangle.
                var line = Math.Max(1.2f, size / 8);
                canvas.DrawLine(At(0, -half * 0.25f), At(0, half * 0.4f), line, cut);
                canvas.DrawLine(At(0, half * 0.58f), At(0, half * 0.75f), line, cut);
                break;
            case LabelIcon.Info:
                const int Steps = 24;
                Span<Vector2> disc = stackalloc Vector2[Steps];
                for (var i = 0; i < Steps; i++)
                {
                    var turn = i * MathF.Tau / Steps;
                    disc[i] = At(MathF.Cos(turn) * half, MathF.Sin(turn) * half);
                }

                canvas.FillPolygon(disc, color);

                // An "i" cut out of the disc.
                var stroke = Math.Max(1.2f, size / 7);
                canvas.DrawLine(At(0, -half * 0.12f), At(0, half * 0.6f), stroke, cut);
                canvas.DrawLine(At(0, -half * 0.55f), At(0, -half * 0.35f), stroke, cut);

                break;
            case LabelIcon.Arrow:
                canvas.DrawLine(At(-half, 0), At(0, 0), Math.Max(1.5f, size / 5), color);
                canvas.FillPolygon([At(0, -half * 0.7f), At(half, 0), At(0, half * 0.7f)], color);
                break;
            case LabelIcon.Marker:
                canvas.FillPolygon([At(-half * 0.8f, -half * 0.8f), At(half * 0.8f, -half * 0.8f), At(half * 0.8f, half * 0.8f), At(-half * 0.8f, half * 0.8f)], color);
                break;
        }
    }
}
