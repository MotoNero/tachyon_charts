using System.Numerics;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

/// <summary>A button of the bar of measuring tools.</summary>
internal enum MedicalToolButton
{
    /// <summary>No tool: a drag moves points and the window.</summary>
    Move,

    /// <summary>The distance tool.</summary>
    Distance,

    /// <summary>The angle tool.</summary>
    Angle,

    /// <summary>The circle tool.</summary>
    Circle,

    /// <summary>The rectangle tool.</summary>
    Rectangle,

    /// <summary>The freehand tool.</summary>
    Freehand,

    /// <summary>Removes the selected measurement.</summary>
    Delete,
}

/// <summary>
/// The bar of measuring tools in the lower left corner of an image: square buttons with drawn symbols, the chosen tool
/// lit, and the name with its key above the button under the pointer.
/// </summary>
internal static class MedicalToolBar
{
    private const double Size = 26;
    private const double Gap = 3;
    private const double Margin = 8;

    private static readonly string[] _names = ["Move points and window (Esc)", "Distance (D)", "Angle (A)", "Circle (C)", "Rectangle (R)", "Freehand (F)", "Delete the selected measurement (Del)"];

    /// <summary>Gets the number of buttons.</summary>
    public static int Count => _names.Length;

    /// <summary>Gets the place of a button in a pane.</summary>
    /// <param name="pane">The pane.</param>
    /// <param name="button">The button.</param>
    /// <returns>The place.</returns>
    public static DipRect Bounds(DipRect pane, MedicalToolButton button) =>
        new(pane.X + Margin + ((int)button * (Size + Gap)), pane.Bottom - Margin - Size, Size, Size);

    /// <summary>Finds the button under a position.</summary>
    /// <param name="layout">The layout of the chart.</param>
    /// <param name="position">The position.</param>
    /// <param name="button">The button.</param>
    /// <returns><see langword="true"/> when the position lies on a button.</returns>
    public static bool TryHit(ChartLayoutResult layout, Vector2 position, out MedicalToolButton button)
    {
        button = default;
        if (layout.Panes.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < Count; i++)
        {
            var bounds = Bounds(layout.Panes[0], (MedicalToolButton)i);
            if (position.X >= bounds.X && position.X <= bounds.Right && position.Y >= bounds.Y && position.Y <= bounds.Bottom)
            {
                button = (MedicalToolButton)i;
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets the button of a tool.</summary>
    /// <param name="tool">The tool, or <see langword="null"/> for none.</param>
    /// <returns>The button.</returns>
    public static MedicalToolButton Of(MedicalMeasurementKind? tool) => tool switch
    {
        MedicalMeasurementKind.Distance => MedicalToolButton.Distance,
        MedicalMeasurementKind.Angle => MedicalToolButton.Angle,
        MedicalMeasurementKind.Circle => MedicalToolButton.Circle,
        MedicalMeasurementKind.Rectangle => MedicalToolButton.Rectangle,
        MedicalMeasurementKind.Freehand => MedicalToolButton.Freehand,
        _ => MedicalToolButton.Move,
    };

    /// <summary>Gets the tool of a button.</summary>
    /// <param name="button">The button; not <see cref="MedicalToolButton.Delete"/>.</param>
    /// <returns>The tool, or <see langword="null"/> for moving.</returns>
    public static MedicalMeasurementKind? ToolOf(MedicalToolButton button) => button switch
    {
        MedicalToolButton.Distance => MedicalMeasurementKind.Distance,
        MedicalToolButton.Angle => MedicalMeasurementKind.Angle,
        MedicalToolButton.Circle => MedicalMeasurementKind.Circle,
        MedicalToolButton.Rectangle => MedicalMeasurementKind.Rectangle,
        MedicalToolButton.Freehand => MedicalMeasurementKind.Freehand,
        _ => null,
    };

    /// <summary>Draws the bar.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="tool">The chosen tool.</param>
    /// <param name="hovered">The button under the pointer, or <see langword="null"/>.</param>
    /// <param name="canDelete">Whether a measurement is selected.</param>
    /// <param name="lit">The color of the chosen tool.</param>
    public static void Draw(OverlayCanvas canvas, MedicalMeasurementKind? tool, MedicalToolButton? hovered, bool canDelete, Rgba8 lit)
    {
        if (canvas.Layout.Panes.Count == 0)
        {
            return;
        }

        var pane = canvas.Layout.Panes[0];
        var chosen = Of(tool);
        var ink = canvas.TextColor;
        var back = canvas.BackgroundColor;
        for (var i = 0; i < Count; i++)
        {
            var button = (MedicalToolButton)i;
            var bounds = Bounds(pane, button);
            var isChosen = button == chosen;
            var isHovered = button == hovered;
            canvas.FillRectangle(bounds, isChosen ? lit with { A = 90 } : back with { A = (byte)(isHovered ? 250 : 210) });
            canvas.DrawRectangle(bounds, isChosen ? lit : ink with { A = (byte)(isHovered ? 200 : 90) });
            var color = button == MedicalToolButton.Delete && !canDelete ? ink with { A = 70 } : isChosen ? lit : ink;
            Symbol(canvas, button, bounds, color);
        }

        if (hovered is { } shown)
        {
            var bounds = Bounds(pane, shown);
            canvas.DrawLabel(_names[(int)shown], bounds.X, bounds.Y - canvas.GetLineHeight(canvas.LabelSize) - 8, back with { A = 235 }, ink);
        }
    }

    private static void Head(OverlayCanvas canvas, Vector2 tip, Vector2 one, Vector2 other, Rgba8 color)
    {
        canvas.DrawLine(tip, tip + one, 1.5f, color);
        canvas.DrawLine(tip, tip + other, 1.5f, color);
    }

    // A symbol of 12 × 12 in the middle of a button, made of lines.
    private static void Symbol(OverlayCanvas canvas, MedicalToolButton button, DipRect bounds, Rgba8 color)
    {
        var (left, top) = ((float)bounds.X + 7, (float)bounds.Y + 7);
        var (right, bottom) = (left + 12, top + 12);
        var middle = new Vector2(left + 6, top + 6);
        const float Line = 1.5f;
        switch (button)
        {
            case MedicalToolButton.Move:
                // Four arrows from the middle.
                canvas.DrawLine(new(left, middle.Y), new(right, middle.Y), Line, color);
                canvas.DrawLine(new(middle.X, top), new(middle.X, bottom), Line, color);
                Head(canvas, new(left, middle.Y), new(3, -3), new(3, 3), color);
                Head(canvas, new(right, middle.Y), new(-3, -3), new(-3, 3), color);
                Head(canvas, new(middle.X, top), new(-3, 3), new(3, 3), color);
                Head(canvas, new(middle.X, bottom), new(-3, -3), new(3, -3), color);
                break;
            case MedicalToolButton.Distance:
                canvas.DrawLine(new(left, bottom), new(right, top), Line, color);
                canvas.DrawLine(new(left - 2, bottom - 2), new(left + 2, bottom + 2), Line, color);
                canvas.DrawLine(new(right - 2, top - 2), new(right + 2, top + 2), Line, color);
                break;
            case MedicalToolButton.Angle:
                canvas.DrawLine(new(left, bottom), new(right, bottom), Line, color);
                canvas.DrawLine(new(left, bottom), new(right, top), Line, color);
                canvas.DrawLine(new(left + 6, bottom), new(left + 5, bottom - 4), Line, color);
                break;
            case MedicalToolButton.Circle:
                for (var i = 0; i < 16; i++)
                {
                    var (a, b) = (i * MathF.PI / 8, (i + 1) * MathF.PI / 8);
                    canvas.DrawLine(middle + (new Vector2(MathF.Cos(a), MathF.Sin(a)) * 6), middle + (new Vector2(MathF.Cos(b), MathF.Sin(b)) * 6), Line, color);
                }

                break;
            case MedicalToolButton.Rectangle:
                canvas.DrawRectangle(new DipRect(left, top + 2, 12, 8), color);
                break;
            case MedicalToolButton.Freehand:
                canvas.DrawLine(new(left, top + 8), new(left + 3, top + 3), Line, color);
                canvas.DrawLine(new(left + 3, top + 3), new(left + 6, top + 9), Line, color);
                canvas.DrawLine(new(left + 6, top + 9), new(left + 9, top + 2), Line, color);
                canvas.DrawLine(new(left + 9, top + 2), new(right, top + 7), Line, color);

                break;
            default:
                canvas.DrawLine(new(left + 1, top + 1), new(right - 1, bottom - 1), Line, color);
                canvas.DrawLine(new(right - 1, top + 1), new(left + 1, bottom - 1), Line, color);
                break;
        }
    }
}
