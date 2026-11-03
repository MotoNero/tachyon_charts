using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Brings a cloud to the GPU in portions. A worker prepares each portion, so the render thread only copies a finished
/// block and no frame waits for millions of conversions: first an even sample, then leaf by leaf. It also holds the
/// choice of points for each view. The front of the buffer belongs to the sample.
/// </summary>
/// <param name="cloud">The cloud.</param>
internal sealed class CloudLoader(PointCloud3D cloud)
{
    private const int PointsPerPortion = 2_000_000;
    private const int SpotsFrom = 8_000_000;
    private const int PointsTo = 10_000_000;
    private const int LeavesPerThread = 8;
    private const double UploadStart = 0.7;

    private readonly CloudSelection _selection = new();
    private readonly List<CloudDraw> _draws = [];
    private LineVertex[] _chunk = [];
    private Point3D[] _boxes = [];
    private CloudTree? _tree;
    private (DoubleRange, DoubleRange, DoubleRange) _ranges;
    private Task<Portion>? _portion;
    private bool _mustRestart;
    private int _previewCount;
    private int _nextLeaf;
    private bool _usesSpots;

    /// <summary>Gets the number of points in the last picture.</summary>
    public long Drawn { get; private set; }

    /// <summary>Gets the number of leaves on the GPU.</summary>
    public int LoadedLeaves => _nextLeaf;

    private bool IsSmall => cloud.Count <= PointCloud3D.PreviewPoints;

    private int GpuStart => Math.Min(cloud.Count, PointCloud3D.PreviewPoints);

    private byte SizeCode => Points3D.ToSizeCode(cloud.PointSize);

    /// <summary>Brings the next portion to the GPU.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="target">The buffer.</param>
    public void Load(Scene3D scene, ITraceTarget target)
    {
        cloud.Start();
        if (!cloud.IsLoaded)
        {
            return;
        }

        var ranges = (scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange);
        if (ranges != _ranges)
        {
            _mustRestart = _previewCount > 0 || _nextLeaf > 0 || _portion is not null;
            _ranges = ranges;
        }

        var map = new BoxMap(scene);
        _tree = cloud.Tree;
        if (IsSmall)
        {
            LoadAtOnce(map, target);
            return;
        }

        if (_portion is { IsCompleted: false })
        {
            return;
        }

        TakePortion(target);
        Restart();
        StartNextPortion(map);
    }

    /// <summary>Chooses the points for a view. As long as leaves are missing, the sample fills in for them.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="matrix">The camera.</param>
    /// <param name="size">The picture in pixels.</param>
    /// <param name="renderer">The renderer.</param>
    /// <param name="budget">The most points.</param>
    public void Select(Scene3D scene, in Matrix3D matrix, PixelSize size, Renderer3D renderer, int budget)
    {
        _draws.Clear();
        if (_previewCount > 0 && (!cloud.IsReady || IsSmall))
        {
            _draws.Add(new CloudDraw(0, _previewCount));
            budget -= _previewCount;
        }

        var total = (long)(_draws.Count > 0 ? _previewCount : 0);
        if (_tree is { } tree && _nextLeaf > 0)
        {
            _selection.Select(tree.Leaves.AsSpan(0, _nextLeaf), _boxes, matrix, new System.Numerics.Vector2(size.Width, size.Height), budget, scene.Planes);
            for (var i = 0; i < _selection.DrawCount; i++)
            {
                _draws.Add(_selection.Draws[i] with { First = _selection.Draws[i].First + GpuStart });
            }

            total += _selection.Total;
        }

        Drawn = total;
        _usesSpots = _usesSpots ? total < PointsTo : total < SpotsFrom;
        renderer.SetDraws(cloud, CollectionsMarshal.AsSpan(_draws), _usesSpots);
    }

    private static LineVertex[] Ensure(LineVertex[] chunk, int count) => chunk.Length < count ? new LineVertex[count] : chunk;

    // The sample of a small cloud is the whole cloud, so the picture does not depend on when the tree is done.
    private void LoadAtOnce(in BoxMap map, ITraceTarget target)
    {
        if (_previewCount == 0 || _mustRestart)
        {
            _mustRestart = false;
            _chunk = Ensure(_chunk, GpuStart);
            WritePreview(_chunk, map);
            target.Write(0, _chunk.AsSpan(0, GpuStart));
            _previewCount = GpuStart;
        }

        cloud.IsReady = _tree is not null;
    }

    private void Restart()
    {
        if (_mustRestart)
        {
            (_mustRestart, _previewCount, _nextLeaf) = (false, 0, 0);
            cloud.IsReady = false;
        }
    }

    private void TakePortion(ITraceTarget target)
    {
        if (_portion is not { IsCompletedSuccessfully: true } finished)
        {
            return;
        }

        _portion = null;
        var portion = finished.Result;
        if (_mustRestart)
        {
            return;
        }

        target.Write(portion.Place, _chunk.AsSpan(0, portion.Count));
        _previewCount = Math.Max(_previewCount, portion.IsPreview ? portion.Count : 0);
        _nextLeaf = portion.NextLeaf;
        if (_tree is { } tree)
        {
            cloud.IsReady = _nextLeaf >= tree.Leaves.Length;
            cloud.Report(UploadStart + ((1 - UploadStart) * _nextLeaf / tree.Leaves.Length));
        }

        cloud.Touch();
    }

    private void StartNextPortion(BoxMap map)
    {
        if (_previewCount == 0)
        {
            var count = GpuStart;
            _chunk = Ensure(_chunk, count);
            var chunk = _chunk;
            Begin(Task.Run(() =>
            {
                WritePreview(chunk, map);
                return new Portion(0, count, 0, true);
            }));
        }
        else if (_tree is { } tree && _nextLeaf < tree.Leaves.Length)
        {
            var (first, chunk) = (_nextLeaf, _chunk);
            Begin(Task.Run(() => WriteLeaves(tree, first, chunk, map)));
        }
    }

    // When the worker is done, a new picture takes over its result.
    private void Begin(Task<Portion> portion)
    {
        _portion = portion;
        portion.ContinueWith(_ => cloud.Touch(), TaskScheduler.Default);
    }

    private void WritePreview(LineVertex[] chunk, in BoxMap map)
    {
        var (count, step, code) = (GpuStart, (long)cloud.Count / GpuStart, SizeCode);
        var view = map;
        Parallel.For(0, count, i => chunk[i] = ToVertex((int)(i * step), view, code));
    }

    private LineVertex ToVertex(int point, in BoxMap map, byte code)
    {
        var color = cloud.Colors?[point] ?? cloud.Color;
        return new LineVertex(map.ToGpu(cloud.X[point], cloud.Y[point], cloud.Z[point]), new Rgba8(color.R, color.G, color.B, code));
    }

    // On a worker. The leaves of a portion lie side by side on the GPU, so they arrive as one block.
    private Portion WriteLeaves(CloudTree tree, int first, LineVertex[] chunk, BoxMap map)
    {
        if (_boxes.Length != 2 * tree.Leaves.Length)
        {
            _boxes = new Point3D[2 * tree.Leaves.Length];
        }

        var (last, count) = (first, 0);
        while (last < tree.Leaves.Length && count < PointsPerPortion)
        {
            count += tree.Leaves[last++].Count;
        }

        // The render thread never touches the chunk while a worker fills it, so only this worker may replace it.
        _chunk = chunk = Ensure(chunk, count);
        var (target, code, start) = (chunk, SizeCode, tree.Leaves[first].GpuFirst);
        var end = last;
        Parallel.For(first, end, new ParallelOptions { MaxDegreeOfParallelism = LeavesPerThread }, index =>
        {
            ref readonly var leaf = ref tree.Leaves[index];
            var (a, b) = (map.ToBox(leaf.Low), map.ToBox(leaf.High));
            _boxes[2 * index] = new Point3D(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            _boxes[(2 * index) + 1] = new Point3D(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
            for (var i = 0; i < leaf.Count; i++)
            {
                target[leaf.GpuFirst - start + i] = ToVertex(tree.Order[leaf.First + i], map, code);
            }
        });
        return new Portion(GpuStart + start, count, last, false);
    }

    private readonly record struct Portion(int Place, int Count, int NextLeaf, bool IsPreview);
}

/// <summary>
/// Follows the time that the GPU really needs: a heavy view lowers the number of points at once, and spare time raises
/// it slowly. It only hears about frames while the view moves, so that the picture at rest stays as it is. The distance
/// between frames would be no measure, as a slow mouse moves the view less often than the GPU could follow.
/// </summary>
/// <param name="most">The largest budget.</param>
internal sealed class CloudBudget(int most)
{
    private const int Least = 1_000_000;
    private const double TargetTime = 11;
    private const double SpareTime = 7.5;
    private const double Raise = 1.1;

    private readonly int _most = most;

    /// <summary>Gets the number of points per frame.</summary>
    public int Current { get; private set; } = most;

    /// <summary>Takes note of a measured frame.</summary>
    /// <param name="milliseconds">The time that the GPU needed for the 3D picture.</param>
    public void Update(double milliseconds)
    {
        if (milliseconds > TargetTime)
        {
            Current = Math.Max((int)(Current * TargetTime / milliseconds), Math.Min(Least, _most));
        }
        else if (milliseconds < SpareTime)
        {
            Current = Math.Min((int)(Current * Raise), _most);
        }
    }
}
