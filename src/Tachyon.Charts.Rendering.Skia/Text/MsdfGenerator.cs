using System.Numerics;

namespace Tachyon.Charts.Rendering.Skia.Text;

/// <summary>
/// Creates multi-channel signed distance fields from outlines. Each channel sees only some edges, and
/// edges meeting at a corner share just one channel. The median of the channels then keeps the corner
/// sharp at any scale, where a single distance would round it.
/// </summary>
internal static class MsdfGenerator
{
    private const int Red = 1;
    private const int Green = 2;
    private const int Blue = 4;
    private const int White = Red | Green | Blue;
    private const int Cyan = Green | Blue;
    private const int Magenta = Red | Blue;
    private const int Yellow = Red | Green;
    private const float CornerDotLimit = 0.5f;

    /// <summary>
    /// Writes a field into RGBA pixels.
    /// </summary>
    /// <param name="contours">The closed contours, each a list of flattened curves in pixels of the field.</param>
    /// <param name="width">The width of the field.</param>
    /// <param name="height">The height of the field.</param>
    /// <param name="range">The distance in pixels that the value range covers to each side.</param>
    /// <param name="destination">The target pixels.</param>
    /// <param name="stride">The bytes per row of the target.</param>
    public static void Generate(IReadOnlyList<IReadOnlyList<Vector2[]>> contours, int width, int height, float range, Span<byte> destination, int stride)
    {
        var edges = new List<ColoredEdge>();
        foreach (var contour in contours)
        {
            AddColoredEdges(contour, edges);
        }

        var orientation = SignedArea(contours) >= 0 ? 1f : -1f;
        for (var y = 0; y < height; y++)
        {
            var row = destination[(y * stride)..];
            for (var x = 0; x < width; x++)
            {
                WritePixel(edges, new Vector2(x + 0.5f, y + 0.5f), orientation, range, row[(x * 4)..]);
            }
        }
    }

    private static void WritePixel(List<ColoredEdge> edges, Vector2 pixel, float orientation, float range, Span<byte> pixelBytes)
    {
        var red = Distance.Far;
        var green = Distance.Far;
        var blue = Distance.Far;
        foreach (var edge in edges)
        {
            var distance = edge.Measure(pixel);
            red = (edge.Color & Red) != 0 ? Distance.Closer(red, distance) : red;
            green = (edge.Color & Green) != 0 ? Distance.Closer(green, distance) : green;
            blue = (edge.Color & Blue) != 0 ? Distance.Closer(blue, distance) : blue;
        }

        pixelBytes[0] = Encode(red.Pseudo * orientation, range);
        pixelBytes[1] = Encode(green.Pseudo * orientation, range);
        pixelBytes[2] = Encode(blue.Pseudo * orientation, range);
        pixelBytes[3] = 255;
    }

    private static byte Encode(float distance, float range) =>
        (byte)Math.Clamp(((distance / (2 * range)) + 0.5f) * 255f + 0.5f, 0f, 255f);

    private static float SignedArea(IReadOnlyList<IReadOnlyList<Vector2[]>> contours)
    {
        var area = 0f;
        foreach (var contour in contours)
        {
            foreach (var curve in contour)
            {
                for (var i = 1; i < curve.Length; i++)
                {
                    area += (curve[i - 1].X * curve[i].Y) - (curve[i].X * curve[i - 1].Y);
                }
            }
        }

        return area;
    }

    private static bool IsCorner(Vector2[] before, Vector2[] after)
    {
        var incoming = Vector2.Normalize(before[^1] - before[^2]);
        var outgoing = Vector2.Normalize(after[1] - after[0]);
        return Vector2.Dot(incoming, outgoing) < CornerDotLimit;
    }

    private static void AddColoredEdges(IReadOnlyList<Vector2[]> contour, List<ColoredEdge> edges)
    {
        var corners = new List<int>();
        for (var i = 0; i < contour.Count; i++)
        {
            if (IsCorner(contour[(i + contour.Count - 1) % contour.Count], contour[i]))
            {
                corners.Add(i);
            }
        }

        if (corners.Count == 0)
        {
            edges.Add(new ColoredEdge(Join(contour, 0, contour.Count), White));
            return;
        }

        if (corners.Count == 1)
        {
            AddTeardrop(Join(contour, corners[0], contour.Count), edges);
            return;
        }

        for (var i = 0; i < corners.Count; i++)
        {
            var start = corners[i];
            var next = corners[(i + 1) % corners.Count];
            var count = ((next - start + contour.Count - 1) % contour.Count) + 1;
            var color = i % 2 == 1 ? Magenta : Yellow;
            edges.Add(new ColoredEdge(Join(contour, start, count), i == 0 ? Cyan : color));
        }
    }

    // A single corner has no neighbors to alternate with, so its outline splits into three edges.
    private static void AddTeardrop(Vector2[] points, List<ColoredEdge> edges)
    {
        var first = points.Length / 3;
        var second = 2 * points.Length / 3;
        edges.Add(new ColoredEdge(points[..(first + 1)], Cyan));
        edges.Add(new ColoredEdge(points[first..(second + 1)], Yellow));
        edges.Add(new ColoredEdge(points[second..], Magenta));
    }

    private static Vector2[] Join(IReadOnlyList<Vector2[]> contour, int start, int count)
    {
        var points = new List<Vector2>();
        for (var i = 0; i < count; i++)
        {
            var curve = contour[(start + i) % contour.Count];
            points.AddRange(i == 0 ? curve : curve[1..]);
        }

        return [.. points];
    }

    private readonly record struct Distance(float True, float Pseudo, float Orthogonality)
    {
        public static Distance Far { get; } = new(float.MaxValue, float.MaxValue, 0);

        // At a shared end point two edges are equally near; the one facing the point decides.
        public static Distance Closer(Distance a, Distance b)
        {
            const float Tie = 1e-4f;
            if (MathF.Abs(a.True - b.True) < Tie)
            {
                return b.Orthogonality > a.Orthogonality ? b : a;
            }

            return b.True < a.True ? b : a;
        }
    }

    private sealed class ColoredEdge(Vector2[] points, int color)
    {
        public int Color { get; } = color;

        public Distance Measure(Vector2 point)
        {
            var bestSquared = float.MaxValue;
            var bestSegment = 0;
            var bestParameter = 0f;
            for (var i = 0; i < points.Length - 1; i++)
            {
                var start = points[i];
                var along = points[i + 1] - start;
                var lengthSquared = along.LengthSquared();
                if (lengthSquared == 0)
                {
                    continue;
                }

                var parameter = Vector2.Dot(point - start, along) / lengthSquared;
                var nearest = start + (along * Math.Clamp(parameter, 0, 1));
                var squared = Vector2.DistanceSquared(point, nearest);
                if (squared < bestSquared)
                {
                    bestSquared = squared;
                    bestSegment = i;
                    bestParameter = parameter;
                }
            }

            return ToDistance(point, bestSegment, bestParameter, MathF.Sqrt(bestSquared));
        }

        private Distance ToDistance(Vector2 point, int segment, float parameter, float distance)
        {
            var start = points[segment];
            var direction = Vector2.Normalize(points[segment + 1] - start);
            var offset = point - start;
            var cross = (direction.X * offset.Y) - (direction.Y * offset.X);
            var sign = cross >= 0 ? 1f : -1f;

            var nearest = start + (points[segment + 1] - start) * Math.Clamp(parameter, 0, 1);
            var toPoint = point - nearest;
            var orthogonality = toPoint.LengthSquared() > 0
                ? MathF.Abs((direction.X * toPoint.Y) - (direction.Y * toPoint.X)) / toPoint.Length()
                : 1;

            // Beyond its ends, the edge continues as a straight line. That is what keeps corners sharp.
            var isBeyondStart = segment == 0 && parameter < 0;
            var isBeyondEnd = segment == points.Length - 2 && parameter > 1;
            var pseudo = (isBeyondStart || isBeyondEnd) && MathF.Abs(cross) <= distance ? cross : sign * distance;

            return new Distance(distance, pseudo, orthogonality);
        }
    }
}
