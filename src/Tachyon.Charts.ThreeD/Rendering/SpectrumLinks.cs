using System.Numerics;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Shows what a spectrum display holds in its waterfall: the mask as a red fence across the depth, and the markers as
/// orange frames across the box, numbered at the level of the newest trace. The levels of the display set the height of the box.
/// </summary>
internal static class SpectrumLinks
{
    /// <summary>The color of the mask, as in the spectrum.</summary>
    public static readonly Rgba8 MaskColor = new(240, 80, 70, 255);

    /// <summary>The color of the markers, as in the spectrum.</summary>
    public static readonly Rgba8 MarkerColor = new(255, 160, 50, 255);

    /// <summary>Gets the levels that the spectrum shows, from the bottom to the reference.</summary>
    /// <param name="display">The display.</param>
    /// <returns>The range.</returns>
    public static DoubleRange Levels(SpectrumDisplay display) =>
        new(display.ReferenceLevel - (display.PerDivision * ScopeDisplay.VerticalDivisions), display.ReferenceLevel);

    /// <summary>Gets the frequencies of all markers of a display, the first one first.</summary>
    /// <param name="display">The display.</param>
    /// <param name="frequencies">Receives the frequencies.</param>
    public static void ReadMarkers(SpectrumDisplay display, List<double> frequencies)
    {
        frequencies.Clear();
        frequencies.Add(display.MarkerPoint.Frequency);
        frequencies.AddRange(display.ExtraMarkers);
    }

    /// <summary>Gets a number that changes when the mask, the markers or the levels change.</summary>
    /// <param name="display">The display.</param>
    /// <returns>The number.</returns>
    public static int Stamp(SpectrumDisplay display)
    {
        var stamp = new HashCode();
        stamp.Add(display.Mask);
        stamp.Add(display.MarkerPoint);
        stamp.Add(display.ReferenceLevel);
        stamp.Add(display.PerDivision);
        foreach (var marker in display.ExtraMarkers)
        {
            stamp.Add(marker);
        }

        return stamp.ToHashCode();
    }

    /// <summary>Gets a number that changes when a spectrum behind a waterfall of the scene changes its mask, markers or levels.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The number.</returns>
    public static long Stamp(Scene3D scene)
    {
        var stamp = 0L;
        var waterfalls = scene.Waterfalls;
        for (var i = 0; i < waterfalls.Count; i++)
        {
            if (waterfalls[i].Display is { } display)
            {
                stamp += Stamp(display);
            }
        }

        return stamp;
    }

    /// <summary>Adds the lines of the mask and the markers.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="waterfall">The waterfall.</param>
    /// <param name="display">The display.</param>
    /// <param name="lines">Receives two points per line.</param>
    /// <param name="markers">A list to keep the frequencies of the markers in.</param>
    public static void AddLines(Scene3D scene, Waterfall3D waterfall, SpectrumDisplay display, List<LineVertex> lines, List<double> markers)
    {
        var map = new BoxMap(scene);
        var (front, back) = (waterfall.Y.Min, waterfall.Y.Max);
        if (display.Mask is { } mask)
        {
            for (var i = 0; i < mask.Frequencies.Count; i++)
            {
                var (frequency, level) = (mask.Frequencies[i], mask.Levels[i]);
                if (i > 0)
                {
                    var (before, below) = (mask.Frequencies[i - 1], mask.Levels[i - 1]);
                    Add(lines, map, new Point3D(before, front, below), new Point3D(frequency, front, level), MaskColor);
                    Add(lines, map, new Point3D(before, back, below), new Point3D(frequency, back, level), MaskColor);
                }

                Add(lines, map, new Point3D(frequency, front, level), new Point3D(frequency, back, level), MaskColor);
            }
        }

        // Each marker is a frame across the box, so that the surface never hides it.
        var (floor, top) = (scene.ZAxis.VisibleRange.Min, scene.ZAxis.VisibleRange.Max);
        ReadMarkers(display, markers);
        foreach (var frequency in markers)
        {
            Add(lines, map, new Point3D(frequency, front, floor), new Point3D(frequency, back, floor), MarkerColor);
            Add(lines, map, new Point3D(frequency, back, floor), new Point3D(frequency, back, top), MarkerColor);
            Add(lines, map, new Point3D(frequency, back, top), new Point3D(frequency, front, top), MarkerColor);
            Add(lines, map, new Point3D(frequency, front, top), new Point3D(frequency, front, floor), MarkerColor);
        }
    }

    /// <summary>Gets the top of the post of a marker, where its number goes.</summary>
    /// <param name="waterfall">The waterfall.</param>
    /// <param name="display">The display.</param>
    /// <param name="frequency">The frequency of the marker.</param>
    /// <returns>The place in the units of the axes.</returns>
    public static Point3D Top(Waterfall3D waterfall, SpectrumDisplay display, double frequency)
    {
        var (_, level) = display.ReadPoint(frequency);
        return new Point3D(frequency, waterfall.Y.Min, double.IsFinite(level) ? level : display.ReferenceLevel);
    }

    /// <summary>Gets the name of a marker without making a new string each frame.</summary>
    /// <param name="number">The number, from 1.</param>
    /// <returns>The name.</returns>
    public static string Name(int number) =>
        number > 0 && number <= _names.Length ? _names[number - 1] : number.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static readonly string[] _names = [.. Enumerable.Range(1, 32).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture))];

    private static void Add(List<LineVertex> lines, in BoxMap map, in Point3D a, in Point3D b, Rgba8 color)
    {
        var (from, to) = (map.ToBox(a), map.ToBox(b));
        if (double.IsFinite(from.X + from.Z + to.X + to.Z))
        {
            lines.Add(new LineVertex(new Vector3((float)from.X, (float)from.Y, (float)Math.Clamp(from.Z, -1, 1)), color));
            lines.Add(new LineVertex(new Vector3((float)to.X, (float)to.Y, (float)Math.Clamp(to.Z, -1, 1)), color));
        }
    }
}

/// <summary>Sets and removes markers from a click in 3D, so that a spectrum behind a waterfall keeps them too.</summary>
internal static class MarkerLinks
{
    /// <summary>The share of the first axis within which a click removes a marker of a spectrum.</summary>
    private const double SpectrumReach = 0.015;

    /// <summary>The distance in device independent pixels within which a click hits a marker of the scene.</summary>
    private const float MarkerReach = 10;

    /// <summary>Sets a marker at a hit, or removes the marker that the click met.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="pick">The hit.</param>
    /// <param name="nearest">The marker of the scene under the click, if any.</param>
    public static void Place(Scene3D scene, in Pick3D pick, int? nearest)
    {
        if (pick.Source is Waterfall3D { Display: { } display })
        {
            var reach = SpectrumReach * Math.Abs(scene.XAxis.VisibleRange.Max - scene.XAxis.VisibleRange.Min);
            if (!display.RemoveMarker(pick.Position.X, reach))
            {
                display.AddMarker(pick.Position.X);
            }

            return;
        }

        if (nearest is { } index)
        {
            scene.RemoveMarker(index);
            return;
        }

        scene.AddMarker(pick);
    }

    /// <summary>Finds the marker of the scene that lies under a point of the picture.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="pane">The area of the picture.</param>
    /// <param name="position">The point.</param>
    /// <returns>The place of the marker in <see cref="Scene3D.Markers"/>, or <see langword="null"/>.</returns>
    public static int? FindNear(Scene3D scene, DipRect pane, Vector2 position)
    {
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var markers = scene.Markers;
        int? nearest = null;
        var distance = (double)MarkerReach;
        for (var i = 0; i < markers.Count; i++)
        {
            var (x, y, _) = matrix.Project(markers[i].Box);
            var (dx, dy) = (pane.X + ((x + 1) / 2 * pane.Width) - position.X, pane.Y + ((1 - y) / 2 * pane.Height) - position.Y);
            if (Math.Sqrt((dx * dx) + (dy * dy)) <= distance)
            {
                (nearest, distance) = (i, Math.Sqrt((dx * dx) + (dy * dy)));
            }
        }

        return nearest;
    }

    /// <summary>Removes the newest marker: of the scene first, then of a spectrum behind a waterfall.</summary>
    /// <param name="scene">The scene.</param>
    public static void RemoveNewest(Scene3D scene)
    {
        if (scene.Markers.Count > 0)
        {
            scene.RemoveLastMarker();
            return;
        }

        foreach (var waterfall in scene.Waterfalls)
        {
            if (waterfall.Display is { ExtraMarkers.Count: > 0 } display)
            {
                display.RemoveMarker(display.ExtraMarkers[^1], 0);
                return;
            }
        }
    }
}
