using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Invented areas: a region with a lake, a ring, a star, and a field of hexagons.</summary>
public static class PolygonDemo
{
    /// <summary>How to operate a page with polygons.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Pointer: the polygon under it | Double click: all";

    /// <summary>Gets a region of 0 to 10 in both directions, with a lake inside.</summary>
    /// <returns>The region.</returns>
    public static Polygon Region()
    {
        var (x, y) = Blob(5, 5, 4.2, 0.9, 120, 3);
        var (lakeX, lakeY) = Blob(6, 5.6, 1.1, 0.25, 40, 7);
        return new Polygon(new PolygonRing(x, y), [new PolygonRing(lakeX, lakeY)]) { Name = "Region" };
    }

    /// <summary>Gets a few shapes, some with holes.</summary>
    /// <returns>The shapes.</returns>
    public static Polygon[] Shapes()
    {
        var (ringX, ringY) = Circle(3, 7, 2, 48);
        var (holeX, holeY) = Circle(3, 7, 1, 32);
        var (starX, starY) = Star(7.5, 7, 2, 0.9, 5);
        var (blobX, blobY) = Blob(5, 2.6, 2.2, 0.6, 80, 5);
        var (lakeX, lakeY) = Circle(5.6, 2.4, 0.6, 24);
        var (lake2X, lake2Y) = Circle(4.2, 3, 0.4, 20);
        return
        [
            new Polygon(new PolygonRing(ringX, ringY), [new PolygonRing(holeX, holeY)]) { Name = "Ring" },
            new Polygon(new PolygonRing(starX, starY)) { Name = "Star" },
            new Polygon(new PolygonRing(blobX, blobY), [new PolygonRing(lakeX, lakeY), new PolygonRing(lake2X, lake2Y)]) { Name = "Island with two lakes" },
        ];
    }

    /// <summary>Gets a field of hexagons, 100 by 100.</summary>
    /// <returns>The hexagons.</returns>
    public static Polygon[] Hexagons()
    {
        var hexagons = new Polygon[10_000];
        for (var row = 0; row < 100; row++)
        {
            for (var column = 0; column < 100; column++)
            {
                var (x, y) = Circle((column * 1.5) + (row % 2 * 0.75), row * 0.433, 0.48, 6);
                hexagons[(row * 100) + column] = new Polygon(new PolygonRing(x, y)) { Name = $"Cell {row}/{column}" };
            }
        }

        return hexagons;
    }

    private static (double[] X, double[] Y) Circle(double cx, double cy, double radius, int corners)
    {
        var (x, y) = (new double[corners], new double[corners]);
        for (var i = 0; i < corners; i++)
        {
            var angle = i * 2 * Math.PI / corners;
            (x[i], y[i]) = (cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
        }

        return (x, y);
    }

    private static (double[] X, double[] Y) Star(double cx, double cy, double outer, double inner, int points)
    {
        var (x, y) = (new double[points * 2], new double[points * 2]);
        for (var i = 0; i < x.Length; i++)
        {
            var angle = (Math.PI / 2) + (i * Math.PI / points);
            var radius = i % 2 == 0 ? outer : inner;
            (x[i], y[i]) = (cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle)));
        }

        return (x, y);
    }

    // A wobbly round shape, the same on every run.
    private static (double[] X, double[] Y) Blob(double cx, double cy, double radius, double wobble, int corners, int seed)
    {
        var (x, y) = (new double[corners], new double[corners]);
        for (var i = 0; i < corners; i++)
        {
            var angle = i * 2 * Math.PI / corners;
            var r = radius + (wobble * ((0.5 * Math.Sin((angle * 3) + seed)) + (0.3 * Math.Sin((angle * 7) + (seed * 2))) + (0.2 * Math.Sin((angle * 11) + seed))));
            (x[i], y[i]) = (cx + (r * Math.Cos(angle)), cy + (r * Math.Sin(angle)));
        }

        return (x, y);
    }
}
