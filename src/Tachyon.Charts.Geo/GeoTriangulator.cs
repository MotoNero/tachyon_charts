namespace Tachyon.Charts.Geo;

/// <summary>
/// Cuts areas of many thousand corners into triangles, as countries at a fine level of detail have: the holes are joined
/// to the outer ring by bridges, then ears are cut along a linked ring, and a grid finds the corners that could lie in
/// an ear, so a ring of 50,000 corners takes milliseconds, not minutes.
/// </summary>
internal static class GeoTriangulator
{
    /// <summary>Cuts an area into triangles.</summary>
    /// <param name="outer">The outer ring.</param>
    /// <param name="holes">The holes.</param>
    /// <returns>The corners, holes after the outer ring, three indices into them per triangle, and the corners of each ring.</returns>
    public static (double[] X, double[] Y, int[] Triangles, int[] Rings) Triangulate((double[] X, double[] Y) outer, IReadOnlyList<(double[] X, double[] Y)> holes)
    {
        var xs = new List<double>();
        var ys = new List<double>();
        var start = Add(xs, ys, outer.X, outer.Y, positive: true);
        if (start is null)
        {
            return ([], [], [], []);
        }

        var rings = new List<int> { xs.Count };

        // The holes from the one reaching farthest east, each joined by a bridge to the ring so far.
        var holeStarts = new List<Node>();
        foreach (var hole in holes)
        {
            var before = xs.Count;
            if (Add(xs, ys, hole.X, hole.Y, positive: false) is { } node)
            {
                holeStarts.Add(Rightmost(node));
                rings.Add(xs.Count - before);
            }
        }

        holeStarts.Sort((a, b) => b.X.CompareTo(a.X));
        foreach (var hole in holeStarts)
        {
            Bridge(start, hole);
        }

        var triangles = new List<int>(Math.Max(0, (xs.Count - 2) * 3));
        Cut(start, triangles);
        return ([.. xs], [.. ys], [.. triangles], [.. rings]);
    }

    // A ring as a linked list, turned positively (counterclockwise) for an outer ring and negatively for a hole.
    private static Node? Add(List<double> xs, List<double> ys, double[] x, double[] y, bool positive)
    {
        var count = Math.Min(x.Length, y.Length);
        if (count > 1 && x[0] == x[count - 1] && y[0] == y[count - 1])
        {
            count--;
        }

        if (count < 3)
        {
            return null;
        }

        var area = 0.0;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            area += (x[j] - x[i]) * (y[j] + y[i]);
        }

        // The sum is positive for a counterclockwise ring.
        var reverse = (area > 0) != positive;
        Node? first = null, last = null;
        for (var k = 0; k < count; k++)
        {
            var i = reverse ? count - 1 - k : k;
            var node = new Node(xs.Count, x[i], y[i]);
            xs.Add(x[i]);
            ys.Add(y[i]);
            if (last is null)
            {
                first = node;
            }
            else
            {
                (last.Next, node.Prev) = (node, last);
            }

            last = node;
        }

        (last!.Next, first!.Prev) = (first, last);
        return first;
    }

    private static Node Rightmost(Node start)
    {
        var (best, node) = (start, start.Next);
        while (node != start)
        {
            if (node.X > best.X || (node.X == best.X && node.Y < best.Y))
            {
                best = node;
            }

            node = node.Next;
        }

        return best;
    }

    // Eberly: a ray from the hole east meets an edge; the bridge goes to the end of that edge, or to the corner within
    // the triangle of hole, meeting point and edge end that lies at the smallest angle.
    private static void Bridge(Node outer, Node hole)
    {
        var (hx, hy) = (hole.X, hole.Y);
        var (edge, nearest) = RayHit(outer, hx, hy);
        if (edge is null)
        {
            return;
        }

        var target = Visible(outer, edge, hx, hy, nearest);

        // Two copies of each end close the bridge: target → hole → around the hole → hole' → target' → on.
        var targetCopy = new Node(target.Index, target.X, target.Y);
        var holeCopy = new Node(hole.Index, hole.X, hole.Y);
        var (afterTarget, beforeHole) = (target.Next, hole.Prev);
        (target.Next, hole.Prev) = (hole, target);
        (beforeHole.Next, holeCopy.Prev) = (holeCopy, beforeHole);
        (holeCopy.Next, targetCopy.Prev) = (targetCopy, holeCopy);
        (targetCopy.Next, afterTarget.Prev) = (afterTarget, targetCopy);
    }

    // The edge the ray from the hole east meets first, and its end further east.
    private static (Node? Edge, double X) RayHit(Node outer, double hx, double hy)
    {
        Node? edge = null;
        var nearest = double.PositiveInfinity;
        var node = outer;
        do
        {
            var next = node.Next;
            var crosses = (node.Y <= hy && next.Y >= hy) || (next.Y <= hy && node.Y >= hy);
            if (crosses && next.Y != node.Y)
            {
                var x = node.X + ((hy - node.Y) * (next.X - node.X) / (next.Y - node.Y));
                if (x >= hx && x < nearest)
                {
                    (nearest, edge) = (x, node.X > next.X ? node : next);
                }
            }

            node = next;
        }
        while (node != outer);
        return (edge, nearest);
    }

    // Corners inside the triangle could block the bridge: the one at the smallest angle to the ray is visible.
    private static Node Visible(Node outer, Node edge, double hx, double hy, double mx)
    {
        var target = edge;
        var smallest = double.PositiveInfinity;
        var node = outer;
        do
        {
            if (node != edge && node.X >= hx && InTriangle(hx, hy, mx, hy, edge.X, edge.Y, node.X, node.Y))
            {
                var angle = Math.Abs(node.Y - hy) / Math.Max(node.X - hx, 1e-300);
                if (angle < smallest || (angle == smallest && node.X > target.X))
                {
                    (smallest, target) = (angle, node);
                }
            }

            node = node.Next;
        }
        while (node != outer);
        return target;
    }

    // Cuts ears along the ring; a grid over the corners finds those that could lie inside an ear.
    private static void Cut(Node start, List<int> triangles)
    {
        var all = new List<Node>();
        var node = start;
        do
        {
            all.Add(node);
            node = node.Next;
        }
        while (node != start);

        var grid = new CornerGrid(all);
        var left = all.Count;
        var stall = 0;
        node = start;
        while (left > 3)
        {
            var (prev, next) = (node.Prev, node.Next);
            if (IsEar(prev, node, next, grid) || stall > left)
            {
                triangles.Add(prev.Index);
                triangles.Add(node.Index);
                triangles.Add(next.Index);
                (prev.Next, next.Prev) = (next, prev);
                node.IsRemoved = true;
                left--;
                stall = 0;

                // Back one, as the corner before may have become an ear.
                node = prev;
                continue;
            }

            node = next;
            stall++;
        }

        if (left == 3)
        {
            triangles.Add(node.Prev.Index);
            triangles.Add(node.Index);
            triangles.Add(node.Next.Index);
        }

    }

    private static bool IsEar(Node a, Node b, Node c, CornerGrid grid)
    {
        // Convex: the corner turns left on a counterclockwise ring.
        if (Cross(a, b, c) <= 0)
        {
            return false;
        }

        var (minX, maxX) = (Math.Min(a.X, Math.Min(b.X, c.X)), Math.Max(a.X, Math.Max(b.X, c.X)));
        var (minY, maxY) = (Math.Min(a.Y, Math.Min(b.Y, c.Y)), Math.Max(a.Y, Math.Max(b.Y, c.Y)));
        foreach (var other in grid.Within(minX, minY, maxX, maxY))
        {
            if (other.IsRemoved || other == a || other == b || other == c || (other.X == a.X && other.Y == a.Y) || (other.X == b.X && other.Y == b.Y) || (other.X == c.X && other.Y == c.Y))
            {
                continue;
            }

            // Only a corner that turns right (a reflex one) can lie inside an ear.
            if (Cross(other.Prev, other, other.Next) <= 0 && InTriangle(a.X, a.Y, b.X, b.Y, c.X, c.Y, other.X, other.Y))
            {
                return false;
            }
        }

        return true;
    }

    private static double Cross(Node a, Node b, Node c) => ((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X));

    private static bool InTriangle(double ax, double ay, double bx, double by, double cx, double cy, double px, double py) =>
        ((cx - px) * (ay - py)) - ((ax - px) * (cy - py)) >= 0
        && ((ax - px) * (by - py)) - ((bx - px) * (ay - py)) >= 0
        && ((bx - px) * (cy - py)) - ((cx - px) * (by - py)) >= 0
        || ((cx - px) * (ay - py)) - ((ax - px) * (cy - py)) <= 0
        && ((ax - px) * (by - py)) - ((bx - px) * (ay - py)) <= 0
        && ((bx - px) * (cy - py)) - ((cx - px) * (by - py)) <= 0;

    private sealed class Node(int index, double x, double y)
    {
        public int Index { get; } = index;

        public double X { get; } = x;

        public double Y { get; } = y;

        public Node Prev { get; set; } = null!;

        public Node Next { get; set; } = null!;

        public bool IsRemoved { get; set; }
    }

    // A grid of about one corner per cell over the box of the ring.
    private sealed class CornerGrid
    {
        private readonly List<Node>[] _cells;
        private readonly int _side;
        private readonly double _minX;
        private readonly double _minY;
        private readonly double _cellWidth;
        private readonly double _cellHeight;
        private readonly List<Node> _found = [];

        public CornerGrid(List<Node> nodes)
        {
            _side = Math.Clamp((int)Math.Sqrt(nodes.Count), 1, 1024);
            (_minX, _minY) = (nodes.Min(node => node.X), nodes.Min(node => node.Y));
            _cellWidth = Math.Max((nodes.Max(node => node.X) - _minX) / _side, 1e-12);
            _cellHeight = Math.Max((nodes.Max(node => node.Y) - _minY) / _side, 1e-12);
            _cells = new List<Node>[_side * _side];
            foreach (var node in nodes)
            {
                (_cells[Cell(node.X, node.Y)] ??= []).Add(node);
            }
        }

        public List<Node> Within(double minX, double minY, double maxX, double maxY)
        {
            _found.Clear();
            var (x0, y0) = (Column(minX), Row(minY));
            var (x1, y1) = (Column(maxX), Row(maxY));
            for (var row = y0; row <= y1; row++)
            {
                for (var column = x0; column <= x1; column++)
                {
                    if (_cells[(row * _side) + column] is { } cell)
                    {
                        _found.AddRange(cell);
                    }
                }
            }

            return _found;
        }

        private int Cell(double x, double y) => (Row(y) * _side) + Column(x);

        private int Column(double x) => Math.Clamp((int)((x - _minX) / _cellWidth), 0, _side - 1);

        private int Row(double y) => Math.Clamp((int)((y - _minY) / _cellHeight), 0, _side - 1);
    }
}
