using System.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>What a cloud needs in memory.</summary>
/// <param name="Gpu">The bytes on the graphics card, reserved at the start.</param>
/// <param name="Cpu">The bytes of the index in main memory, next to the arrays of the application.</param>
public readonly record struct CloudMemory(long Gpu, long Cpu);

/// <summary>The points of a cloud.</summary>
/// <param name="X">The first coordinates.</param>
/// <param name="Y">The second coordinates.</param>
/// <param name="Z">The third coordinates.</param>
/// <param name="Colors">The colors, or <see langword="null"/> for one color.</param>
public sealed record PointCloudArrays(double[] X, double[] Y, double[] Z, Rgba8[]? Colors = null);

/// <summary>
/// A very large cloud of points. An octree forms in the background, and each frame draws what helps the view: a coarse
/// sample shows at once, finer points follow, and every point drawn is a real point at its exact place.
/// </summary>
public sealed class PointCloud3D
{
    /// <summary>The most points on the GPU. One buffer holds 2 GiB, so a larger cloud shows an even sample.</summary>
    internal const int MaximumPoints = 130_000_000;

    /// <summary>The number of points that show at once.</summary>
    internal const int PreviewPoints = 1_000_000;

    /// <summary>Gets the size up to which a cloud shows whole in its first picture, so it needs no worker.</summary>
    public static int PreviewLimit => PreviewPoints;

    private const int PointBytes = 16;
    private const int IndexBytes = 4;

    private const double TreeShare = 0.5;

    private readonly Func<IProgress<double>, PointCloudArrays>? _load;
    private Task<CloudTree>? _tree;
    private long _stamp;
    private double _progress;
    private volatile bool _isLoaded;

    /// <summary>Initializes a new instance of the <see cref="PointCloud3D"/> class with points that exist already.</summary>
    /// <param name="x">The first coordinates. The cloud keeps the arrays instead of a copy.</param>
    /// <param name="y">The second coordinates.</param>
    /// <param name="z">The third coordinates.</param>
    /// <param name="colors">The colors, or <see langword="null"/> for one color.</param>
    public PointCloud3D(double[] x, double[] y, double[] z, Rgba8[]? colors = null)
    {
        X = x ?? throw new ArgumentNullException(nameof(x));
        Y = y ?? throw new ArgumentNullException(nameof(y));
        Z = z ?? throw new ArgumentNullException(nameof(z));
        Colors = colors;
        _isLoaded = true;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PointCloud3D"/> class with points that a worker creates or reads,
    /// so that the application stays free while a large cloud forms. The chart shows the progress.
    /// </summary>
    /// <param name="load">Creates the points. It reports its progress from 0 to 1.</param>
    public PointCloud3D(Func<IProgress<double>, PointCloudArrays> load) => _load = load ?? throw new ArgumentNullException(nameof(load));

    /// <summary>Gets the first coordinates.</summary>
    public double[] X { get; private set; } = [];

    /// <summary>Gets the second coordinates.</summary>
    public double[] Y { get; private set; } = [];

    /// <summary>Gets the third coordinates.</summary>
    public double[] Z { get; private set; } = [];

    /// <summary>Gets the colors.</summary>
    public Rgba8[]? Colors { get; private set; }

    /// <summary>Gets the color of a cloud without colors.</summary>
    public Rgba8 Color { get; init; } = new(90, 170, 255, 255);

    /// <summary>Gets the number of points per frame. It stays the same while turning and at rest, so the density never jumps.</summary>
    public int PointBudget { get; init; } = 30_000_000;

    /// <summary>Gets the diameter of the points once so few are in view that single pixels would leave gaps.</summary>
    public double PointSize { get; init; } = 2.5;

    /// <summary>Gets the number of points.</summary>
    public int Count => X.Length;

    /// <summary>Gets a value indicating whether all points have reached the GPU.</summary>
    public bool IsReady { get; internal set; }

    /// <summary>Gets how far the cloud has come, from 0 to 1.</summary>
    public double Progress => IsReady ? 1 : Volatile.Read(ref _progress);

    /// <summary>Gets a value indicating whether the chart shows how far the cloud has come. A small cloud shows whole at once.</summary>
    internal bool ShowsProgress => !IsReady && (!IsLoaded || Count > PreviewPoints);

    /// <summary>Gets a value indicating whether the points exist.</summary>
    internal bool IsLoaded => _isLoaded;

    /// <summary>Gets a number that changes as finer points arrive.</summary>
    internal long Stamp => Interlocked.Read(ref _stamp);

    /// <summary>Gets the octree as soon as it stands.</summary>
    internal CloudTree? Tree => _tree is { IsCompletedSuccessfully: true } ? _tree.Result : null;

    /// <summary>Gets what a cloud of this size needs, before it is built.</summary>
    /// <param name="count">The number of points.</param>
    /// <returns>The memory.</returns>
    public static CloudMemory EstimateMemory(int count)
    {
        var shown = Math.Min(count, MaximumPoints);
        return new CloudMemory((shown + (long)Math.Min(count, PreviewPoints)) * PointBytes, (long)count * IndexBytes);
    }

    /// <summary>Starts the octree in the background.</summary>
    internal void Start()
    {
        _tree ??= Task.Run(() =>
        {
            LoadPoints();
            var tree = new CloudTree(this);
            Report(PointCloud3DShares.AfterTree);
            Touch();
            return tree;
        });
    }

    /// <summary>Takes note of the progress.</summary>
    /// <param name="progress">The share from 0 to 1.</param>
    internal void Report(double progress)
    {
        if ((int)(progress * 100) != (int)(Volatile.Read(ref _progress) * 100))
        {
            Volatile.Write(ref _progress, progress);
            Touch();
        }
    }

    private void LoadPoints()
    {
        if (_load is not null)
        {
            var points = _load(new LoadProgress(this));
            (X, Y, Z, Colors) = (points.X, points.Y, points.Z, points.Colors);
            _isLoaded = true;
        }

        Report(TreeShare);
    }

    /// <summary>Waits for the octree.</summary>
    /// <returns>The octree.</returns>
    internal CloudTree WaitForTree()
    {
        Start();
        return _tree!.GetAwaiter().GetResult();
    }

    /// <summary>Asks for a new picture.</summary>
    internal void Touch() => Interlocked.Increment(ref _stamp);

    private sealed class LoadProgress(PointCloud3D cloud) : IProgress<double>
    {
        public void Report(double value) => cloud.Report(Math.Clamp(value, 0, 1) * TreeShare);
    }
}

/// <summary>Where the stages of a cloud end on the way to 1.</summary>
internal static class PointCloud3DShares
{
    /// <summary>The share once the octree stands.</summary>
    public const double AfterTree = 0.7;
}

/// <summary>A bottom part of the octree: its points lie together on the GPU, in random order.</summary>
internal struct CloudLeaf
{
    /// <summary>The first place in the index.</summary>
    public int First;

    /// <summary>The number of points.</summary>
    public int Count;

    /// <summary>The first place on the GPU.</summary>
    public int GpuFirst;

    /// <summary>The lower corner in the units of the axes.</summary>
    public Point3D Low;

    /// <summary>The upper corner in the units of the axes.</summary>
    public Point3D High;
}

/// <summary>
/// Sorts points along a space-filling curve, so that each part of the octree lies together, and mixes each leaf. Any
/// start of a leaf is then an even sample of it, which is what lets the number of points change without a jump.
/// </summary>
internal sealed class CloudTree
{
    private const int Bits = 8;
    private const int Cells = 1 << (3 * Bits);
    private const uint NoCell = uint.MaxValue;
    private const int LeafSize = 32_768;

    private readonly PointCloud3D _cloud;
    private readonly List<CloudLeaf> _leaves = [];

    /// <summary>Initializes a new instance of the <see cref="CloudTree"/> class.</summary>
    /// <param name="cloud">The cloud.</param>
    public CloudTree(PointCloud3D cloud)
    {
        _cloud = cloud;
        var (low, high) = FindBounds();
        var keys = CreateKeys(low, high);
        var starts = new int[Cells + 1];
        foreach (var key in keys)
        {
            if (key != NoCell)
            {
                starts[key + 1]++;
            }
        }

        for (var i = 1; i < starts.Length; i++)
        {
            starts[i] += starts[i - 1];
        }

        Order = new int[starts[^1]];
        var next = starts[..^1];
        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i] != NoCell)
            {
                Order[next[keys[i]]++] = i;
            }
        }

        Visit(starts, 0, Cells);
        Leaves = [.. _leaves];
        Parallel.For(0, Leaves.Length, Finish);
        LayOut();
    }

    /// <summary>Gets the numbers of the points in the order of the leaves.</summary>
    public int[] Order { get; }

    /// <summary>Gets the leaves.</summary>
    public CloudLeaf[] Leaves { get; }

    /// <summary>Gets the number of points on the GPU.</summary>
    public int GpuCount { get; private set; }

    /// <summary>Gets a point.</summary>
    /// <param name="place">The place in the index.</param>
    /// <returns>The point.</returns>
    public Point3D Get(int place)
    {
        var point = Order[place];
        return new Point3D(_cloud.X[point], _cloud.Y[point], _cloud.Z[point]);
    }

    private static uint Spread(uint value)
    {
        value = (value | (value << 8)) & 0x0F00F;
        value = (value | (value << 4)) & 0x0C30C3;
        return (value | (value << 2)) & 0x249249;
    }

    private static int ToCell(double value, double low, double high) =>
        high > low ? Math.Clamp((int)((value - low) / (high - low) * (1 << Bits)), 0, (1 << Bits) - 1) : 0;

    private static Point3D Min(in Point3D a, in Point3D b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

    private static Point3D Max(in Point3D a, in Point3D b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    private static bool IsValid(double x, double y, double z) => double.IsFinite(x + y + z);

    private (Point3D Low, Point3D High) FindBounds()
    {
        var (low, high) = (new Point3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), new Point3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));
        var gate = new Lock();
        Parallel.For(0, Environment.ProcessorCount, part =>
        {
            var (first, last) = Part(part);
            var (partLow, partHigh) = (new Point3D(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity), new Point3D(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));
            for (var i = first; i < last; i++)
            {
                if (IsValid(_cloud.X[i], _cloud.Y[i], _cloud.Z[i]))
                {
                    var point = new Point3D(_cloud.X[i], _cloud.Y[i], _cloud.Z[i]);
                    (partLow, partHigh) = (Min(partLow, point), Max(partHigh, point));
                }
            }

            lock (gate)
            {
                (low, high) = (Min(low, partLow), Max(high, partHigh));
            }
        });
        return (low, high);
    }

    private (int First, int Last) Part(int part) =>
        ((int)((long)_cloud.Count * part / Environment.ProcessorCount), (int)((long)_cloud.Count * (part + 1) / Environment.ProcessorCount));

    private uint[] CreateKeys(Point3D low, Point3D high)
    {
        var keys = new uint[_cloud.Count];
        Parallel.For(0, Environment.ProcessorCount, part =>
        {
            var (first, last) = Part(part);
            for (var i = first; i < last; i++)
            {
                var (x, y, z) = (_cloud.X[i], _cloud.Y[i], _cloud.Z[i]);
                keys[i] = IsValid(x, y, z)
                    ? Spread((uint)ToCell(x, low.X, high.X)) | (Spread((uint)ToCell(y, low.Y, high.Y)) << 1) | (Spread((uint)ToCell(z, low.Z, high.Z)) << 2)
                    : NoCell;
            }
        });
        return keys;
    }

    // Walks down until a part is small enough. A part that is one cell stays whole, even when it is large.
    private void Visit(int[] starts, int from, int to)
    {
        var count = starts[to] - starts[from];
        if (count == 0)
        {
            return;
        }

        if (count <= LeafSize || to - from == 1)
        {
            _leaves.Add(new CloudLeaf { First = starts[from], Count = count });
            return;
        }

        var step = (to - from) / 8;
        for (var child = 0; child < 8; child++)
        {
            Visit(starts, from + (child * step), from + ((child + 1) * step));
        }
    }

    private void Finish(int index)
    {
        ref var leaf = ref Leaves[index];
        var random = new Random(index);
        var span = Order.AsSpan(leaf.First, leaf.Count);
        random.Shuffle(span);
        var (low, high) = (Get(leaf.First), Get(leaf.First));
        for (var place = leaf.First + 1; place < leaf.First + leaf.Count; place++)
        {
            var point = Get(place);
            (low, high) = (Min(low, point), Max(high, point));
        }

        (leaf.Low, leaf.High) = (low, high);
    }

    private void LayOut()
    {
        var share = Math.Min(1.0, PointCloud3D.MaximumPoints / (double)Math.Max(Order.Length, 1));
        var place = 0;
        for (var i = 0; i < Leaves.Length; i++)
        {
            Leaves[i].GpuFirst = place;
            Leaves[i].Count = (int)Math.Ceiling(Leaves[i].Count * share);
            place += Leaves[i].Count;
        }

        GpuCount = place;
    }
}

/// <summary>A run of points to draw.</summary>
/// <param name="First">The first place on the GPU.</param>
/// <param name="Count">The number of points.</param>
internal readonly record struct CloudDraw(int First, int Count);

/// <summary>Chooses how many points of each leaf a frame draws, without any allocation.</summary>
internal sealed class CloudSelection
{
    private const int Steps = 24;
    private const int LeastPoints = 8;
    private const double LeastDensity = 1e-7;
    private const double MostDensity = 1e6;
    private const int CornerCount = 8;

    private float[] _areas = [];
    private int[] _indices = [];

    /// <summary>Gets the runs to draw.</summary>
    public CloudDraw[] Draws { get; private set; } = [];

    /// <summary>Gets the number of runs.</summary>
    public int DrawCount { get; private set; }

    /// <summary>Gets the number of points in all runs.</summary>
    public long Total { get; private set; }

    /// <summary>
    /// Shares the budget out by the area of the leaves on the screen: all get the same density, and a leaf that holds
    /// fewer points gives its share to the others. The result only depends on the view, so it stays at rest.
    /// </summary>
    /// <param name="leaves">The leaves on the GPU.</param>
    /// <param name="boxes">The corners of each leaf in the box, two per leaf.</param>
    /// <param name="matrix">The camera.</param>
    /// <param name="size">The picture in pixels.</param>
    /// <param name="budget">The most points.</param>
    /// <param name="planes">The planes that cut the scene.</param>
    public void Select(ReadOnlySpan<CloudLeaf> leaves, ReadOnlySpan<Point3D> boxes, in Matrix3D matrix, Vector2 size, int budget, IReadOnlyList<ClipPlane3D> planes)
    {
        if (_areas.Length < leaves.Length)
        {
            (_areas, _indices) = (new float[leaves.Length], new int[leaves.Length]);
            Draws = new CloudDraw[leaves.Length];
        }

        var visible = Measure(leaves, boxes, matrix, size, planes);
        var density = FindDensity(leaves, visible, budget);
        DrawCount = 0;
        Total = 0;
        for (var i = 0; i < visible; i++)
        {
            var leaf = leaves[_indices[i]];
            var count = Share(leaf.Count, _areas[i], density);
            Draws[DrawCount++] = new CloudDraw(leaf.GpuFirst, count);
            Total += count;
        }
    }

    private static int Share(int count, float area, double density) => (int)Math.Min(count, LeastPoints + (density * area));

    private static bool IsCut(in Point3D low, in Point3D high, IReadOnlyList<ClipPlane3D> planes)
    {
        for (var i = 0; i < planes.Count; i++)
        {
            var plane = planes[i];
            var far = new Point3D(plane.Normal.X > 0 ? high.X : low.X, plane.Normal.Y > 0 ? high.Y : low.Y, plane.Normal.Z > 0 ? high.Z : low.Z);
            if (plane.Hides && Point3D.Dot(far, plane.Normal) < plane.Offset)
            {
                return true;
            }
        }

        return false;
    }

    private static float MeasureArea(in Point3D low, in Point3D high, in Matrix3D matrix, Vector2 size)
    {
        var (left, right, bottom, top) = (double.PositiveInfinity, double.NegativeInfinity, double.PositiveInfinity, double.NegativeInfinity);
        for (var corner = 0; corner < CornerCount; corner++)
        {
            var place = new Point3D((corner & 1) == 0 ? low.X : high.X, (corner & 2) == 0 ? low.Y : high.Y, (corner & 4) == 0 ? low.Z : high.Z);
            var (x, y, w) = matrix.ProjectWithW(place);

            // A corner behind the eye can land anywhere, so the leaf counts as the whole picture.
            if (w <= 0)
            {
                return size.X * size.Y;
            }

            (left, right, bottom, top) = (Math.Min(left, x), Math.Max(right, x), Math.Min(bottom, y), Math.Max(top, y));
        }

        var (width, height) = (Math.Min(right, 1) - Math.Max(left, -1), Math.Min(top, 1) - Math.Max(bottom, -1));
        return width <= 0 || height <= 0 ? 0 : Math.Max((float)(width * height * size.X * size.Y / 4), 1);
    }

    private int Measure(ReadOnlySpan<CloudLeaf> leaves, ReadOnlySpan<Point3D> boxes, in Matrix3D matrix, Vector2 size, IReadOnlyList<ClipPlane3D> planes)
    {
        var visible = 0;
        for (var i = 0; i < leaves.Length; i++)
        {
            var (low, high) = (boxes[2 * i], boxes[(2 * i) + 1]);
            var area = IsCut(low, high, planes) ? 0 : MeasureArea(low, high, matrix, size);
            if (area > 0)
            {
                (_areas[visible], _indices[visible]) = (area, i);
                visible++;
            }
        }

        return visible;
    }

    private double FindDensity(ReadOnlySpan<CloudLeaf> leaves, int visible, int budget)
    {
        var (low, high) = (LeastDensity, MostDensity);
        for (var step = 0; step < Steps; step++)
        {
            // In equal ratios, as a density can lie anywhere between a point per screen and thousands per pixel.
            var middle = Math.Sqrt(low * high);
            var total = 0L;
            for (var i = 0; i < visible; i++)
            {
                total += Share(leaves[_indices[i]].Count, _areas[i], middle);
            }

            if (total > budget)
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return low;
    }
}
