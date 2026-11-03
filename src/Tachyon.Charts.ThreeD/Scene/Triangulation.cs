namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>
/// Delaunay triangulation by a sweep around the center: points join the hull in order of their distance, and
/// flipped edges restore the Delaunay condition. Sorting makes it O(n log n) in practice. The method follows
/// the sweep-hull idea of Sinclair and the half-edge form known as Delaunator.
/// </summary>
internal sealed class Triangulation
{
    private const int Missing = -1;

    private readonly double[] _x;
    private readonly double[] _y;
    private readonly int[] _triangles;
    private readonly int[] _halfEdges;
    private readonly int[] _next;
    private readonly int[] _previous;
    private readonly int[] _hullTriangle;
    private readonly int[] _hash;
    private readonly int[] _stack = new int[4096];
    private double _centerX;
    private double _centerY;
    private int _hullStart;
    private int _edges;

    private Triangulation(double[] x, double[] y)
    {
        _x = x;
        _y = y;
        var count = x.Length;
        var capacity = Math.Max((2 * count) - 5, 1) * 3;
        _triangles = new int[capacity];
        _halfEdges = new int[capacity];
        _next = new int[count];
        _previous = new int[count];
        _hullTriangle = new int[count];
        _hash = new int[Math.Max((int)Math.Ceiling(Math.Sqrt(count)), 1)];
        Array.Fill(_hash, Missing);
    }

    /// <summary>Connects points to triangles.</summary>
    /// <param name="x">The first coordinates.</param>
    /// <param name="y">The second coordinates.</param>
    /// <returns>Three numbers of points per triangle. Doubled points are skipped. Fewer than three points, or points on one line, give none.</returns>
    public static int[] Create(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length)
        {
            throw new ArgumentException("Both lists need the same length.", nameof(y));
        }

        if (x.Length < 3)
        {
            return [];
        }

        var triangulation = new Triangulation(x, y);
        return triangulation.Run() ? triangulation._triangles[..triangulation._edges] : [];
    }

    private static double Distance(double ax, double ay, double bx, double by) => ((ax - bx) * (ax - bx)) + ((ay - by) * (ay - by));

    private static double Pseudo(double dx, double dy)
    {
        var angle = dx / (Math.Abs(dx) + Math.Abs(dy));
        return (dy > 0 ? 3 - angle : 1 + angle) / 4;
    }

    private double Orient(int a, int b, int c) => ((_y[b] - _y[a]) * (_x[c] - _x[b])) - ((_x[b] - _x[a]) * (_y[c] - _y[b]));

    private double Radius(int a, int b, int c)
    {
        var (dx, dy) = (_x[b] - _x[a], _y[b] - _y[a]);
        var (ex, ey) = (_x[c] - _x[a], _y[c] - _y[a]);
        var (bl, cl) = ((dx * dx) + (dy * dy), (ex * ex) + (ey * ey));
        var d = 0.5 / ((dx * ey) - (dy * ex));
        var (rx, ry) = (((ey * bl) - (dy * cl)) * d, ((dx * cl) - (ex * bl)) * d);
        return (rx * rx) + (ry * ry);
    }

    private (double X, double Y) Circumcenter(int a, int b, int c)
    {
        var (dx, dy) = (_x[b] - _x[a], _y[b] - _y[a]);
        var (ex, ey) = (_x[c] - _x[a], _y[c] - _y[a]);
        var (bl, cl) = ((dx * dx) + (dy * dy), (ex * ex) + (ey * ey));
        var d = 0.5 / ((dx * ey) - (dy * ex));
        return (_x[a] + (((ey * bl) - (dy * cl)) * d), _y[a] + (((dx * cl) - (ex * bl)) * d));
    }

    private bool InCircle(int a, int b, int c, int p)
    {
        var (dx, dy) = (_x[a] - _x[p], _y[a] - _y[p]);
        var (ex, ey) = (_x[b] - _x[p], _y[b] - _y[p]);
        var (fx, fy) = (_x[c] - _x[p], _y[c] - _y[p]);
        var (ap, bp, cp) = ((dx * dx) + (dy * dy), (ex * ex) + (ey * ey), (fx * fx) + (fy * fy));
        return (dx * ((ey * cp) - (bp * fy))) - (dy * ((ex * cp) - (bp * fx))) + (ap * ((ex * fy) - (ey * fx))) < 0;
    }

    private int HashKey(double x, double y) =>
        (int)Math.Floor(Pseudo(x - _centerX, y - _centerY) * _hash.Length) % _hash.Length;

    private bool FindSeed(out int first, out int second, out int third)
    {
        first = FindClosest((_x.Min() + _x.Max()) / 2, (_y.Min() + _y.Max()) / 2, Missing);
        second = FindClosest(_x[first], _y[first], first);
        third = second >= 0 ? FindSmallestCircle(first, second) : Missing;
        if (third < 0)
        {
            return false;
        }

        if (Orient(first, second, third) < 0)
        {
            (second, third) = (third, second);
        }

        return true;
    }

    // Points at the very same place as the search point do not count, except when no point is excluded.
    private int FindClosest(double x, double y, int excluded)
    {
        var (best, found) = (double.PositiveInfinity, Missing);
        for (var i = 0; i < _x.Length; i++)
        {
            var distance = Distance(x, y, _x[i], _y[i]);
            if (i != excluded && distance < best && (excluded < 0 || distance > 0))
            {
                (best, found) = (distance, i);
            }
        }

        return found;
    }

    private int FindSmallestCircle(int first, int second)
    {
        var (best, found) = (double.PositiveInfinity, Missing);
        for (var i = 0; i < _x.Length; i++)
        {
            var radius = i != first && i != second ? Radius(first, second, i) : double.PositiveInfinity;
            if (radius < best)
            {
                (best, found) = (radius, i);
            }
        }

        return found;
    }

    private bool Run()
    {
        if (!FindSeed(out var i0, out var i1, out var i2))
        {
            return false;
        }

        (_centerX, _centerY) = Circumcenter(i0, i1, i2);
        var count = _x.Length;
        var order = new int[count];
        var distances = new double[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
            distances[i] = Distance(_x[i], _y[i], _centerX, _centerY);
        }

        Array.Sort(distances, order);
        _hullStart = i0;
        (_next[i0], _previous[i2]) = (i1, i1);
        (_next[i1], _previous[i0]) = (i2, i2);
        (_next[i2], _previous[i1]) = (i0, i0);
        (_hullTriangle[i0], _hullTriangle[i1], _hullTriangle[i2]) = (0, 1, 2);
        _hash[HashKey(_x[i0], _y[i0])] = i0;
        _hash[HashKey(_x[i1], _y[i1])] = i1;
        _hash[HashKey(_x[i2], _y[i2])] = i2;
        AddTriangle(i0, i1, i2, Missing, Missing, Missing);

        var (lastX, lastY, isFirst) = (0.0, 0.0, true);
        foreach (var point in order)
        {
            var (x, y) = (_x[point], _y[point]);
            if ((!isFirst && x == lastX && y == lastY) || point == i0 || point == i1 || point == i2)
            {
                continue;
            }

            (lastX, lastY, isFirst) = (x, y, false);
            AddPoint(point, x, y);
        }

        return true;
    }

    private void AddPoint(int point, double x, double y)
    {
        var start = FindVisibleStart(x, y);
        if (start < 0)
        {
            return;
        }

        var edge = start;
        int following;
        while (true)
        {
            following = _next[edge];
            if (Orient(point, edge, following) < 0)
            {
                break;
            }

            edge = following;
            if (edge == start)
            {
                return;
            }
        }

        var triangle = AddTriangle(edge, point, following, Missing, Missing, _hullTriangle[edge]);
        _hullTriangle[point] = Legalize(triangle + 2);
        _hullTriangle[edge] = triangle;
        following = WalkForward(point, following);
        if (edge == start)
        {
            edge = WalkBackward(point, edge);
        }

        _hullStart = _previous[point] = edge;
        _next[edge] = _previous[following] = point;
        _next[point] = following;
        _hash[HashKey(x, y)] = point;
        _hash[HashKey(_x[edge], _y[edge])] = edge;
    }

    private int FindVisibleStart(double x, double y)
    {
        var key = HashKey(x, y);
        var start = 0;
        for (var j = 0; j < _hash.Length; j++)
        {
            start = _hash[(key + j) % _hash.Length];
            if (start != Missing && start != _next[start])
            {
                break;
            }
        }

        return _previous[start];
    }

    private int WalkForward(int point, int edge)
    {
        var current = edge;
        while (true)
        {
            var following = _next[current];
            if (Orient(point, current, following) >= 0)
            {
                return current;
            }

            var triangle = AddTriangle(current, point, following, _hullTriangle[point], Missing, _hullTriangle[current]);
            _hullTriangle[point] = Legalize(triangle + 2);
            _next[current] = current;
            current = following;
        }
    }

    private int WalkBackward(int point, int edge)
    {
        var current = edge;
        while (true)
        {
            var before = _previous[current];
            if (Orient(point, before, current) >= 0)
            {
                return current;
            }

            var triangle = AddTriangle(before, point, current, Missing, _hullTriangle[current], _hullTriangle[before]);
            Legalize(triangle + 2);
            _hullTriangle[before] = triangle;
            _next[current] = current;
            current = before;
        }
    }

    private int AddTriangle(int a, int b, int c, int ab, int bc, int ca)
    {
        var at = _edges;
        (_triangles[at], _triangles[at + 1], _triangles[at + 2]) = (a, b, c);
        Link(at, ab);
        Link(at + 1, bc);
        Link(at + 2, ca);
        _edges += 3;
        return at;
    }

    private void Link(int a, int b)
    {
        _halfEdges[a] = b;
        if (b != Missing)
        {
            _halfEdges[b] = a;
        }
    }

    // Flips edges until the Delaunay condition holds. It uses a stack of its own, because long flip chains would be deep.
    private int Legalize(int startEdge)
    {
        var depth = 0;
        var a = startEdge;
        while (true)
        {
            var flipped = Flip(a);
            if (flipped != Missing)
            {
                if (depth < _stack.Length)
                {
                    _stack[depth++] = flipped;
                }

                continue;
            }

            if (depth == 0)
            {
                return a - (a % 3) + ((a + 2) % 3);
            }

            a = _stack[--depth];
        }
    }

    // Returns the edge that still needs a look after a flip, or Missing when the edge was fine.
    private int Flip(int a)
    {
        var b = _halfEdges[a];
        if (b == Missing)
        {
            return Missing;
        }

        var (a0, b0) = (a - (a % 3), b - (b % 3));
        var (al, ar, bl) = (a0 + ((a + 1) % 3), a0 + ((a + 2) % 3), b0 + ((b + 2) % 3));
        var (p0, pr, pl, p1) = (_triangles[ar], _triangles[a], _triangles[al], _triangles[bl]);
        if (!InCircle(p0, pr, pl, p1))
        {
            return Missing;
        }

        _triangles[a] = p1;
        _triangles[b] = p0;
        var hbl = _halfEdges[bl];
        if (hbl == Missing)
        {
            RepairHull(bl, a);
        }

        Link(a, hbl);
        Link(b, _halfEdges[ar]);
        Link(ar, bl);
        return b0 + ((b + 1) % 3);
    }

    private void RepairHull(int oldEdge, int newEdge)
    {
        var edge = _hullStart;
        do
        {
            if (_hullTriangle[edge] == oldEdge)
            {
                _hullTriangle[edge] = newEdge;
                return;
            }

            edge = _previous[edge];
        }
        while (edge != _hullStart);
    }
}
