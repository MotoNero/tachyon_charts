using System.Collections.Concurrent;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>What lies under the pointer.</summary>
/// <param name="Position">The place in the units of the axes. On a raster it is the nearest measured point.</param>
/// <param name="Source">The object that was hit.</param>
public readonly record struct Pick3D(Point3D Position, object Source)
{
    /// <summary>Gets the value at this place, for data with a value beside the three axes.</summary>
    public double? Value { get; init; }

    /// <summary>Gets the place in the box.</summary>
    internal Point3D Box { get; init; }
}

/// <summary>A ray in the box.</summary>
/// <param name="Start">Where it starts.</param>
/// <param name="Direction">Its direction with the length 1.</param>
internal readonly record struct Ray3D(Point3D Start, Point3D Direction)
{
    private const double Limit = 1;
    private const double Tiny = 1e-12;

    /// <summary>Gets a point of the ray.</summary>
    /// <param name="distance">The distance from the start.</param>
    /// <returns>The point.</returns>
    public Point3D At(double distance) => Start + (Direction * distance);

    /// <summary>Limits the ray to one axis.</summary>
    /// <param name="start">The start on this axis.</param>
    /// <param name="direction">The direction on this axis.</param>
    /// <param name="low">The lower end.</param>
    /// <param name="high">The upper end.</param>
    /// <param name="near">The first distance, moved inward.</param>
    /// <param name="far">The last distance, moved inward.</param>
    /// <returns>Whether something is left.</returns>
    public static bool Limit1(double start, double direction, double low, double high, ref double near, ref double far)
    {
        if (Math.Abs(direction) < Tiny)
        {
            return start >= low && start <= high;
        }

        var (a, b) = ((low - start) / direction, (high - start) / direction);
        (a, b) = a <= b ? (a, b) : (b, a);
        near = Math.Max(near, a);
        far = Math.Min(far, b);
        return near <= far;
    }

    /// <summary>Limits the ray to the box.</summary>
    /// <param name="near">The first distance inside.</param>
    /// <param name="far">The last distance inside.</param>
    /// <returns>Whether the ray meets the box.</returns>
    public bool LimitToBox(out double near, out double far)
    {
        (near, far) = (0, double.PositiveInfinity);
        return Limit1(Start.X, Direction.X, -Limit, Limit, ref near, ref far)
            && Limit1(Start.Y, Direction.Y, -Limit, Limit, ref near, ref far)
            && Limit1(Start.Z, Direction.Z, -Limit, Limit, ref near, ref far);
    }

    /// <summary>Limits the ray to the side of a plane that stays.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance.</param>
    /// <returns>Whether something is left.</returns>
    public bool LimitToPlane(ClipPlane3D plane, ref double near, ref double far)
    {
        var height = Point3D.Dot(Start, plane.Normal) - plane.Offset;
        var climb = Point3D.Dot(Direction, plane.Normal);
        if (Math.Abs(climb) < Tiny)
        {
            return height >= 0;
        }

        var crossing = -height / climb;
        if (climb > 0)
        {
            near = Math.Max(near, crossing);
        }
        else
        {
            far = Math.Min(far, crossing);
        }

        return near <= far;
    }

    /// <summary>Meets a triangle, seen from both sides.</summary>
    /// <param name="a">The first corner.</param>
    /// <param name="b">The second corner.</param>
    /// <param name="c">The third corner.</param>
    /// <param name="distance">The distance of the hit.</param>
    /// <param name="u">The share of the second corner.</param>
    /// <param name="v">The share of the third corner.</param>
    /// <returns>Whether the ray hits.</returns>
    public bool Hit(in Point3D a, in Point3D b, in Point3D c, out double distance, out double u, out double v)
    {
        var (first, second) = (b - a, c - a);
        var side = Point3D.Cross(Direction, second);
        var area = Point3D.Dot(first, side);
        (distance, u, v) = (double.PositiveInfinity, 0, 0);
        if (Math.Abs(area) < Tiny)
        {
            return false;
        }

        var inverse = 1 / area;
        var offset = Start - a;
        u = Point3D.Dot(offset, side) * inverse;
        if (u < 0 || u > 1)
        {
            return false;
        }

        var turned = Point3D.Cross(offset, first);
        v = Point3D.Dot(Direction, turned) * inverse;
        if (v < 0 || u + v > 1)
        {
            return false;
        }

        distance = Point3D.Dot(second, turned) * inverse;
        return distance >= 0;
    }
}

/// <summary>Finds what lies under the pointer, on the CPU, so that a frame never waits for the GPU.</summary>
/// <param name="scene">The scene.</param>
internal sealed class ScenePicker(Scene3D scene)
{
    private const int CloudSearch = 60_000;
    private const int CloudLeafSearch = 4096;
    private const long GridPause = 1000;

    private readonly ConcurrentDictionary<Mesh3D, MeshEntry> _meshes = [];
    private readonly Dictionary<Scatter3D, GridEntry> _grids = [];
    private readonly List<(double Near, int Index)> _leaves = [];

    /// <summary>Takes over a surface. Its search tree forms in the background.</summary>
    /// <param name="mesh">The surface.</param>
    /// <param name="positions">The points in the box.</param>
    /// <param name="indices">The triangles without invalid ones.</param>
    /// <param name="source">The object that a hit names.</param>
    public void Add(Mesh3D mesh, Point3D[] positions, uint[] indices, object source) =>
        _meshes[mesh] = new MeshEntry(Task.Run(() => new MeshTree(positions, indices)), source);

    /// <summary>Waits until all search trees stand.</summary>
    public void WaitForTrees()
    {
        foreach (var entry in _meshes.Values)
        {
            entry.Tree.GetAwaiter().GetResult();
        }

        foreach (var scatter in scene.Scatters)
        {
            GetGrid(scatter);
            _grids[scatter].Pending?.GetAwaiter().GetResult();
        }
    }

    /// <summary>Finds the nearest thing on a ray.</summary>
    /// <param name="ray">The ray in the box.</param>
    /// <param name="reach">How far from the ray a line or point still counts.</param>
    /// <returns>The hit, or <see langword="null"/>. Only what lies inside the box counts.</returns>
    public Pick3D? Find(in Ray3D ray, in PickReach reach = default)
    {
        if (!ray.LimitToBox(out var near, out var far))
        {
            return null;
        }

        foreach (var plane in scene.Planes)
        {
            if (plane.Hides && !ray.LimitToPlane(plane, ref near, ref far))
            {
                return null;
            }
        }

        Pick3D? best = null;
        foreach (var surface in scene.Surfaces)
        {
            FindOnSurface(ray, surface, near, ref far, ref best);
        }

        foreach (var waterfall in scene.Waterfalls)
        {
            FindOnWaterfall(ray, waterfall, near, ref far, ref best);
        }

        foreach (var bars in scene.Bars)
        {
            FindOnBars(ray, bars, near, ref far, ref best);
        }

        FindOnMeshes(ray, near, ref far, ref best);

        if (scene.Lines.Count > 0)
        {
            FindOnLines(ray, reach, near, ref far, ref best);
        }

        foreach (var scatter in scene.Scatters)
        {
            FindOnScatter(ray, reach, scatter, near, ref far, ref best);
        }

        foreach (var cloud in scene.Clouds)
        {
            FindOnCloud(ray, reach, cloud, near, ref far, ref best);
        }

        FindInVolumes(ray, near, ref far, ref best);
        return best;
    }

    private void FindOnMeshes(in Ray3D ray, double near, ref double far, ref Pick3D? best)
    {
        foreach (var (mesh, entry) in _meshes)
        {
            if (entry.Tree.IsCompletedSuccessfully && entry.Tree.Result.Find(ray, near, ref far, out var triangle, out var u, out var v))
            {
                best = new Pick3D(entry.Tree.Result.ToData(mesh, triangle, u, v), entry.Source) { Box = ray.At(far) };
            }
        }
    }

    // A slice in view wins over the inside of the volume behind it.
    private void FindInVolumes(in Ray3D ray, double near, ref double far, ref Pick3D? best)
    {
        foreach (var volume in scene.Volumes)
        {
            if ((volume.ShowsSlices && volume.FindOnSlice(ray, scene, near, ref far, out var box, out var place))
                || volume.FindInside(ray, scene, near, ref far, out box, out place))
            {
                best = new Pick3D(volume.ToData(place), volume) { Box = box, Value = volume.Data[place.X, place.Y, place.Z] };
            }
        }
    }

    internal static double Across(double low, double high, int index, int count) => low + ((high - low) * index / (count - 1));

    // Looks through the leaves on the ray, front ones first, and stops at the first one with a point near the ray.
    private void FindOnCloud(in Ray3D ray, in PickReach reach, PointCloud3D cloud, double near, ref double far, ref Pick3D? best)
    {
        if (cloud.Tree is not { } tree)
        {
            return;
        }

        var map = new BoxMap(scene);
        var margin = reach.At(far);
        _leaves.Clear();
        for (var i = 0; i < tree.Leaves.Length; i++)
        {
            var (a, b) = (map.ToBox(tree.Leaves[i].Low), map.ToBox(tree.Leaves[i].High));
            var (from, to) = (near, far);
            if (Ray3D.Limit1(ray.Start.X, ray.Direction.X, Math.Min(a.X, b.X) - margin, Math.Max(a.X, b.X) + margin, ref from, ref to)
                && Ray3D.Limit1(ray.Start.Y, ray.Direction.Y, Math.Min(a.Y, b.Y) - margin, Math.Max(a.Y, b.Y) + margin, ref from, ref to)
                && Ray3D.Limit1(ray.Start.Z, ray.Direction.Z, Math.Min(a.Z, b.Z) - margin, Math.Max(a.Z, b.Z) + margin, ref from, ref to))
            {
                _leaves.Add((from, i));
            }
        }

        _leaves.Sort();
        var left = CloudSearch;
        foreach (var (_, index) in _leaves)
        {
            var leaf = tree.Leaves[index];
            var count = Math.Min(leaf.Count, Math.Min(left, CloudLeafSearch));
            left -= count;
            if (FindInLeaf(ray, reach, tree, map, leaf.First, count, cloud, near, ref far, ref best) || left <= 0)
            {
                return;
            }
        }
    }

    private static bool FindInLeaf(in Ray3D ray, in PickReach reach, CloudTree tree, in BoxMap map, int first, int count, PointCloud3D cloud, double near, ref double far, ref Pick3D? best)
    {
        var isFound = false;
        for (var place = first; place < first + count; place++)
        {
            var data = tree.Get(place);
            var box = map.ToBox(data);
            var along = Point3D.Dot(box - ray.Start, ray.Direction);
            if (along >= near && along <= far && (box - ray.At(along)).Length <= reach.At(along))
            {
                far = along;
                best = new Pick3D(data, cloud) { Box = box };
                isFound = true;
            }
        }

        return isFound;
    }

    private void FindOnScatter(in Ray3D ray, in PickReach reach, Scatter3D scatter, double near, ref double far, ref Pick3D? best)
    {
        if (GetGrid(scatter) is { } grid && grid.Find(ray, reach, scatter.Points, new BoxMap(scene), near, ref far, out var found))
        {
            best = new Pick3D(found.Data, scatter) { Box = found.Box };
        }
    }

    // Answers with the raster that stands and starts the next one when the points have moved on.
    private PointGrid? GetGrid(Scatter3D scatter)
    {
        _grids.TryGetValue(scatter, out var entry);
        var ready = entry.Ready;
        if (entry.Pending is { IsCompletedSuccessfully: true } pending)
        {
            ready = pending.Result;
            entry = new GridEntry(ready, null) { Started = entry.Started };
            _grids[scatter] = entry;
        }

        // A live cloud changes all the time, so its raster renews at most once per second.
        var now = Environment.TickCount64;
        if (entry.Pending is null && ready?.Total != scatter.Points.Total && (ready is null || now - entry.Started >= GridPause))
        {
            var map = new BoxMap(scene);
            _grids[scatter] = new GridEntry(ready, Task.Run(() => new PointGrid(scatter.Points, map))) { Started = now };
        }

        return ready;
    }

    private void FindOnLines(in Ray3D ray, in PickReach reach, double near, ref double far, ref Pick3D? best)
    {
        var map = new BoxMap(scene);
        foreach (var line in scene.Lines)
        {
            if (line.Points.FindOnLine(ray, reach, map, near, ref far, out var found))
            {
                best = new Pick3D(found.Data, line) { Box = found.Box };
            }
        }
    }

    private static bool Read(Waterfall3D waterfall, long first, (int Column, int Row) node, out int column, out int row, out float value)
    {
        (column, row) = node;
        return waterfall.Data.TryRead(first + row, column, out value);
    }

    private void FindOnBars(in Ray3D ray, Bars3D bars, double near, ref double far, ref Pick3D? best)
    {
        var place = bars.Cells(scene);
        if (HeightGrid.Find(ray, place, new BarCells(bars, place, new BoxMap(scene)), near, ref far, out var column, out var row))
        {
            var x = bars.X.Min + ((column + 0.5) * (bars.X.Max - bars.X.Min) / bars.Data.Width);
            var y = bars.Y.Min + ((row + 0.5) * (bars.Y.Max - bars.Y.Min) / bars.Data.Height);
            best = new Pick3D(new Point3D(x, y, bars.Data[column, row]), bars) { Box = ray.At(far) };
        }
    }

    private void FindOnSurface(in Ray3D ray, Surface3D surface, double near, ref double far, ref Pick3D? best)
    {
        var (columns, rows) = (surface.Data.Width, surface.Data.Height);
        var place = GridPlace.Create(scene, surface.X, surface.Y, columns, rows);
        var heights = new HeightCells<RasterHeights>(place, new RasterHeights(surface.Data, scene.ZAxis));
        if (HeightGrid.Find(ray, place, heights, near, ref far, out _, out _))
        {
            var (column, row) = place.Nearest(ray.At(far));
            best = new Pick3D(new Point3D(Across(surface.X.Min, surface.X.Max, column, columns), Across(surface.Y.Min, surface.Y.Max, row, rows), surface.Data[column, row]), surface) { Box = ray.At(far) };
        }
    }

    private void FindOnWaterfall(in Ray3D ray, Waterfall3D waterfall, double near, ref double far, ref Pick3D? best)
    {
        var (columns, rows) = (waterfall.Data.Columns, waterfall.Data.Rows);
        var first = waterfall.Data.TotalRows - rows;
        var place = GridPlace.Create(scene, waterfall.X, waterfall.Y, columns, rows);
        var heights = new HeightCells<RingHeights>(place, new RingHeights(waterfall.Data, scene.ZAxis, first));
        if (HeightGrid.Find(ray, place, heights, near, ref far, out _, out _) && Read(waterfall, first, place.Nearest(ray.At(far)), out var column, out var row, out var value))
        {
            best = new Pick3D(new Point3D(Across(waterfall.X.Min, waterfall.X.Max, column, columns), Across(waterfall.Y.Min, waterfall.Y.Max, row, rows), value), waterfall) { Box = ray.At(far) };
        }
    }

    private sealed record MeshEntry(Task<MeshTree> Tree, object Source);

    private readonly record struct GridEntry(PointGrid? Ready, Task<PointGrid>? Pending)
    {
        public long Started { get; init; }
    }
}

/// <summary>The heights of a raster in the box.</summary>
internal interface IGridHeights
{
    /// <summary>Gets a height.</summary>
    /// <param name="column">The column.</param>
    /// <param name="row">The row.</param>
    /// <returns>The height in the box, NaN where it is missing.</returns>
    double At(int column, int row);
}

/// <summary>The cells of a raster that a ray can hit.</summary>
internal interface IGridCells
{
    /// <summary>Meets a cell.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="i">The column of the cell.</param>
    /// <param name="j">The row of the cell.</param>
    /// <param name="from">The first distance.</param>
    /// <param name="to">The last distance.</param>
    /// <param name="distance">The distance of the hit.</param>
    /// <returns>Whether the ray hits.</returns>
    bool Hit(in Ray3D ray, int i, int j, double from, double to, out double distance);
}

/// <summary>The place of a raster in the box.</summary>
/// <param name="Left">The first column.</param>
/// <param name="Front">The first row.</param>
/// <param name="StepX">The step per column.</param>
/// <param name="StepY">The step per row.</param>
/// <param name="Columns">The number of columns.</param>
/// <param name="Rows">The number of rows.</param>
internal readonly record struct GridPlace(double Left, double Front, double StepX, double StepY, int Columns, int Rows)
{
    /// <summary>Gets the point of the raster nearest to a place.</summary>
    /// <param name="place">The place in the box.</param>
    /// <returns>Its column and row.</returns>
    public (int Column, int Row) Nearest(in Point3D place) =>
        (Math.Clamp((int)Math.Round((place.X - Left) / StepX), 0, Columns - 1), Math.Clamp((int)Math.Round((place.Y - Front) / StepY), 0, Rows - 1));

    /// <summary>Places a raster, the same way as the surface on the GPU.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="x">The range of the columns.</param>
    /// <param name="y">The range of the rows.</param>
    /// <param name="columns">The number of columns.</param>
    /// <param name="rows">The number of rows.</param>
    /// <returns>The place.</returns>
    public static GridPlace Create(Scene3D scene, Core.Numerics.DoubleRange x, Core.Numerics.DoubleRange y, int columns, int rows)
    {
        var (left, right) = (AxisBox3D.ToBox(scene.XAxis, x.Min), AxisBox3D.ToBox(scene.XAxis, x.Max));
        var (front, back) = (AxisBox3D.ToBox(scene.YAxis, y.Min), AxisBox3D.ToBox(scene.YAxis, y.Max));
        return new GridPlace(left, front, (right - left) / (columns - 1), (back - front) / (rows - 1), columns, rows);
    }
}

/// <summary>
/// Follows a ray from cell to cell of a raster. It crosses at most columns + rows cells, so even 4096 × 4096 stays
/// far below a millisecond without any preparation, and a raster that changes all the time costs nothing extra.
/// Heights and columns only differ in what a cell holds.
/// </summary>
internal static class HeightGrid
{
    /// <summary>Finds the first hit.</summary>
    /// <typeparam name="T">The kind of cells. A struct, so that they need no virtual call.</typeparam>
    /// <param name="ray">The ray.</param>
    /// <param name="place">The place of the raster.</param>
    /// <param name="cells">The cells.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance, which becomes the distance of the hit.</param>
    /// <param name="column">The column of the cell.</param>
    /// <param name="row">The row of the cell.</param>
    /// <returns>Whether the ray hits.</returns>
    public static bool Find<T>(in Ray3D ray, in GridPlace place, T cells, double near, ref double far, out int column, out int row)
        where T : struct, IGridCells
    {
        (column, row) = (0, 0);
        if (place.Columns < 2 || place.Rows < 2)
        {
            return false;
        }

        var (startX, startY) = ((ray.Start.X - place.Left) / place.StepX, (ray.Start.Y - place.Front) / place.StepY);
        var (directionX, directionY) = (ray.Direction.X / place.StepX, ray.Direction.Y / place.StepY);
        var (from, to) = (near, far);
        if (!Ray3D.Limit1(startX, directionX, 0, place.Columns - 1, ref from, ref to)
            || !Ray3D.Limit1(startY, directionY, 0, place.Rows - 1, ref from, ref to))
        {
            return false;
        }

        var i = Math.Clamp((int)Math.Floor(startX + (directionX * from)), 0, place.Columns - 2);
        var j = Math.Clamp((int)Math.Floor(startY + (directionY * from)), 0, place.Rows - 2);
        var (stepI, stepJ) = (Math.Sign(directionX), Math.Sign(directionY));
        var (nextI, nextJ) = (Crossing(startX, directionX, i), Crossing(startY, directionY, j));
        var (deltaI, deltaJ) = (Math.Abs(1 / directionX), Math.Abs(1 / directionY));
        while (i >= 0 && j >= 0 && i < place.Columns - 1 && j < place.Rows - 1)
        {
            if (cells.Hit(ray, i, j, from, to, out var distance))
            {
                (far, column, row) = (distance, i, j);
                return true;
            }

            if (Math.Min(nextI, nextJ) > to)
            {
                return false;
            }

            if (nextI < nextJ)
            {
                (i, nextI) = (i + stepI, nextI + deltaI);
            }
            else
            {
                (j, nextJ) = (j + stepJ, nextJ + deltaJ);
            }
        }

        return false;
    }

    private static double Crossing(double start, double direction, int cell)
    {
        if (direction == 0)
        {
            return double.PositiveInfinity;
        }

        return (cell + (direction > 0 ? 1 : 0) - start) / direction;
    }

}

/// <summary>The cells of a surface: two triangles between four heights.</summary>
/// <typeparam name="T">The kind of raster.</typeparam>
/// <param name="place">The place of the raster.</param>
/// <param name="heights">The heights.</param>
internal readonly struct HeightCells<T>(in GridPlace place, T heights) : IGridCells
    where T : struct, IGridHeights
{
    private readonly GridPlace _place = place;

    /// <inheritdoc />
    public bool Hit(in Ray3D ray, int i, int j, double from, double to, out double distance)
    {
        distance = double.PositiveInfinity;
        var (h00, h10, h01, h11) = (heights.At(i, j), heights.At(i + 1, j), heights.At(i, j + 1), heights.At(i + 1, j + 1));
        if (double.IsNaN(h00 + h10 + h01 + h11))
        {
            return false;
        }

        var (x0, y0) = (_place.Left + (i * _place.StepX), _place.Front + (j * _place.StepY));
        var (x1, y1) = (x0 + _place.StepX, y0 + _place.StepY);
        var (a, b, c, d) = (new Point3D(x0, y0, h00), new Point3D(x1, y0, h10), new Point3D(x1, y1, h11), new Point3D(x0, y1, h01));
        if (ray.Hit(a, b, c, out var first, out _, out _) && first >= from && first <= to)
        {
            distance = first;
        }

        if (ray.Hit(a, c, d, out var second, out _, out _) && second >= from && second <= to)
        {
            distance = Math.Min(distance, second);
        }

        return double.IsFinite(distance);
    }
}

/// <summary>The heights of a <see cref="HeatmapData"/>.</summary>
/// <param name="data">The raster.</param>
/// <param name="axis">The third axis.</param>
internal readonly struct RasterHeights(HeatmapData data, AxisBase axis) : IGridHeights
{
    private readonly LinearAxisTransform _transform = axis.CreateTransform(-1, 2);
    private readonly int _sign = axis.IsHorizontal ? 1 : -1;

    /// <inheritdoc />
    public double At(int column, int row) => ToHeight(data[column, row]);

    private double ToHeight(double value) => double.IsFinite(value) ? _sign * _transform.ToPixel(value) : double.NaN;
}

/// <summary>The heights of a <see cref="SpectrogramData"/>, whose oldest row lies in front.</summary>
/// <param name="data">The rows.</param>
/// <param name="axis">The third axis.</param>
/// <param name="first">The number of the row in front.</param>
internal readonly struct RingHeights(SpectrogramData data, AxisBase axis, long first) : IGridHeights
{
    private readonly LinearAxisTransform _transform = axis.CreateTransform(-1, 2);
    private readonly int _sign = axis.IsHorizontal ? 1 : -1;

    /// <inheritdoc />
    public double At(int column, int row) =>
        data.TryRead(first + row, column, out var value) && float.IsFinite(value) ? _sign * _transform.ToPixel(value) : double.NaN;
}

/// <summary>A tree of boxes around the triangles of a surface, so that a search only visits few of them.</summary>
internal sealed class MeshTree
{
    private const int LeafSize = 8;
    private const int Depth = 64;

    private readonly Point3D[] _positions;
    private readonly uint[] _indices;
    private readonly int[] _order;
    private readonly List<Node> _nodes = [];

    /// <summary>Initializes a new instance of the <see cref="MeshTree"/> class.</summary>
    /// <param name="positions">The points in the box.</param>
    /// <param name="indices">The triangles.</param>
    public MeshTree(Point3D[] positions, uint[] indices)
    {
        _positions = positions;
        _indices = indices;
        _order = new int[indices.Length / 3];
        for (var i = 0; i < _order.Length; i++)
        {
            _order[i] = i;
        }

        if (_order.Length > 0)
        {
            Build(0, _order.Length);
        }
    }

    /// <summary>Finds the first hit.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance, which becomes the distance of the hit.</param>
    /// <param name="triangle">The triangle.</param>
    /// <param name="u">The share of its second corner.</param>
    /// <param name="v">The share of its third corner.</param>
    /// <returns>Whether the ray hits.</returns>
    public bool Find(in Ray3D ray, double near, ref double far, out int triangle, out double u, out double v)
    {
        (triangle, u, v) = (-1, 0, 0);
        if (_nodes.Count == 0)
        {
            return false;
        }

        Span<int> stack = stackalloc int[Depth];
        var depth = 0;
        stack[depth++] = 0;
        while (depth > 0)
        {
            var node = _nodes[stack[--depth]];
            if (!Meets(ray, node, near, far))
            {
                continue;
            }

            if (node.Count == 0)
            {
                stack[depth++] = node.First;
                stack[depth++] = node.First + 1;
                continue;
            }

            for (var i = node.First; i < node.First + node.Count; i++)
            {
                var at = _order[i] * 3;
                if (ray.Hit(_positions[_indices[at]], _positions[_indices[at + 1]], _positions[_indices[at + 2]], out var distance, out var a, out var b) && distance >= near && distance <= far)
                {
                    (far, triangle, u, v) = (distance, _order[i], a, b);
                }
            }
        }

        return triangle >= 0;
    }

    /// <summary>Gets the place of a hit in the units of the axes.</summary>
    /// <param name="mesh">The surface.</param>
    /// <param name="triangle">The triangle.</param>
    /// <param name="u">The share of its second corner.</param>
    /// <param name="v">The share of its third corner.</param>
    /// <returns>The place.</returns>
    public Point3D ToData(Mesh3D mesh, int triangle, double u, double v)
    {
        var at = triangle * 3;
        var a = mesh.Vertices[(int)_indices[at]].Position;
        var b = mesh.Vertices[(int)_indices[at + 1]].Position;
        var c = mesh.Vertices[(int)_indices[at + 2]].Position;
        return (a * (1 - u - v)) + (b * u) + (c * v);
    }

    private static bool Meets(in Ray3D ray, in Node node, double near, double far) =>
        Ray3D.Limit1(ray.Start.X, ray.Direction.X, node.Low.X, node.High.X, ref near, ref far)
        && Ray3D.Limit1(ray.Start.Y, ray.Direction.Y, node.Low.Y, node.High.Y, ref near, ref far)
        && Ray3D.Limit1(ray.Start.Z, ray.Direction.Z, node.Low.Z, node.High.Z, ref near, ref far);

    private static double Get(in Point3D point, int axis) => axis switch
    {
        0 => point.X,
        1 => point.Y,
        _ => point.Z,
    };

    private static int Longest(in Point3D size)
    {
        if (size.X >= size.Y && size.X >= size.Z)
        {
            return 0;
        }

        return size.Y >= size.Z ? 1 : 2;
    }

    private static Point3D Min(in Point3D a, in Point3D b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Point3D Max(in Point3D a, in Point3D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private Point3D Center(int triangle)
    {
        var at = triangle * 3;
        return (_positions[_indices[at]] + _positions[_indices[at + 1]] + _positions[_indices[at + 2]]) * (1.0 / 3);
    }

    private int Build(int first, int count)
    {
        var (low, high) = Bounds(first, count);
        var index = _nodes.Count;
        _nodes.Add(new Node(low, high, first, count));
        if (count <= LeafSize)
        {
            return index;
        }

        var size = high - low;
        var axis = Longest(size);
        var middle = Split(first, count, axis, (Get(low, axis) + Get(high, axis)) / 2);
        var children = _nodes.Count;
        _nodes.Add(default);
        _nodes.Add(default);
        _nodes[children] = _nodes[Build(first, middle)];
        _nodes[children + 1] = _nodes[Build(first + middle, count - middle)];
        _nodes[index] = new Node(low, high, children, 0);
        return index;
    }

    private (Point3D Low, Point3D High) Bounds(int first, int count)
    {
        var (low, high) = (new Point3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), new Point3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));
        for (var i = first; i < first + count; i++)
        {
            var at = _order[i] * 3;
            for (var corner = 0; corner < 3; corner++)
            {
                low = Min(low, _positions[_indices[at + corner]]);
                high = Max(high, _positions[_indices[at + corner]]);
            }
        }

        return (low, high);
    }

    // Splits at the middle of the space. Should all fall to one side, it halves the list, which keeps the depth bounded.
    private int Split(int first, int count, int axis, double middle)
    {
        var (left, right) = (first, first + count - 1);
        while (left <= right)
        {
            if (Get(Center(_order[left]), axis) < middle)
            {
                left++;
            }
            else
            {
                (_order[left], _order[right]) = (_order[right], _order[left]);
                right--;
            }
        }

        var below = left - first;
        return below == 0 || below == count ? count / 2 : below;
    }

    private readonly record struct Node(Point3D Low, Point3D High, int First, int Count);
}
