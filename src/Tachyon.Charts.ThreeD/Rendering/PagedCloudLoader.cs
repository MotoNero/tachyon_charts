using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Shows a cloud from pages far larger than the GPU: a sample of every page stays on the GPU, so the whole cloud is in
/// view at once; the pages a view needs finer come into slots of a cache on the GPU, the ones longest unused leave
/// first. A worker reads and converts pages while the render thread only uploads what is ready.
/// </summary>
/// <param name="cloud">The cloud, with its pages.</param>
internal sealed class PagedCloudLoader(PointCloud3D cloud)
{
    /// <summary>The points of each page that always stay on the GPU.</summary>
    internal const int SampleSize = 64;

    private const int PagesPerPortion = 32;
    private const int SpotsFrom = 8_000_000;
    private const int PointsTo = 10_000_000;
    private const int VertexBytes = 16;

    private readonly CloudPages _pages = cloud.Pages!;
    private readonly CloudSelection _selection = new();
    private readonly List<CloudDraw> _draws = [];
    private readonly List<(int Count, int Page)> _wanted = [];
    private CloudLeaf[] _leaves = [];
    private Point3D[] _boxes = [];
    private int[] _slotOf = [];
    private int[] _pageInSlot = [];
    private long[] _slotUsed = [];
    private bool[] _isWanted = [];
    private LineVertex[] _chunk = [];
    private Task<Portion>? _portion;
    private (DoubleRange, DoubleRange, DoubleRange) _ranges;
    private long _frame;
    private int _generation;
    private bool _hasSamples;
    private bool _usesSpots;

    /// <summary>Gets the points drawn in the last frame.</summary>
    public long Drawn { get; private set; }

    /// <summary>Gets the pages on the GPU in full.</summary>
    public int LoadedPages { get; private set; }

    private int SampleTotal => _leaves.Length * SampleSize;

    /// <summary>Gets the bytes of the GPU a cloud from pages takes: its samples and its slots.</summary>
    /// <param name="cloud">The cloud, its pages complete.</param>
    /// <returns>The bytes.</returns>
    public static long BufferBytes(PointCloud3D cloud) => ((long)cloud.Pages!.PageCount * SampleSize + ((long)Slots(cloud) * CloudPages.PageSize)) * VertexBytes;

    private static int Slots(PointCloud3D cloud)
    {
        var samples = (long)cloud.Pages!.PageCount * SampleSize * VertexBytes;
        var slots = (cloud.PageMemory - samples) / ((long)CloudPages.PageSize * VertexBytes);

        // At least one slot; no more than the pages, and the places stay within an int.
        return (int)Math.Clamp(slots, 1, Math.Min(cloud.Pages.PageCount, (int.MaxValue - (cloud.Pages.PageCount * (long)SampleSize)) / CloudPages.PageSize));
    }

    public void Load(Scene3D scene, ITraceTarget target)
    {
        if (!_pages.IsComplete)
        {
            cloud.Report(_pages.Progress * 0.9);
            return;
        }

        if (_leaves.Length != _pages.PageCount)
        {
            Create();
        }

        var ranges = (scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange);
        if (ranges != _ranges)
        {
            // Other ranges place every point anew: the samples and the slots are written again.
            _ranges = ranges;
            _generation++;
            (_hasSamples, LoadedPages) = (false, 0);
            Array.Fill(_slotOf, -1);
            Array.Fill(_pageInSlot, -1);
            Measure(new BoxMap(scene));
        }

        if (_portion is { IsCompleted: false })
        {
            return;
        }

        TakePortion(target);
        StartPortion(new BoxMap(scene));
    }

    public void Select(Scene3D scene, in Matrix3D matrix, PixelSize size, Renderer3D renderer, int budget)
    {
        _draws.Clear();
        _frame++;
        var total = 0L;
        if (_hasSamples)
        {
            _selection.Select(_leaves, _boxes, matrix, new Vector2(size.Width, size.Height), budget, scene.Planes);
            _wanted.Clear();
            for (var i = 0; i < _selection.DrawCount; i++)
            {
                var (page, count) = (_selection.Draws[i].First, _selection.Draws[i].Count);
                var slot = _slotOf[page];
                if (slot >= 0)
                {
                    AddDraw(SampleTotal + (slot * CloudPages.PageSize), count);
                    _slotUsed[slot] = _frame;
                    total += count;
                    continue;
                }

                // Until its page arrives, the sample of the page stands in for it.
                var sample = Math.Min(count, Math.Min(SampleSize, _leaves[page].Count));
                AddDraw(page * SampleSize, sample);
                total += sample;
                if (count > sample && !_isWanted[page])
                {
                    Want(count, page);
                }
            }
        }

        Drawn = total;
        _usesSpots = _usesSpots ? total < PointsTo : total < SpotsFrom;
        renderer.SetDraws(cloud, CollectionsMarshal.AsSpan(_draws), _usesSpots);
    }

    // Keeps the pages that want the most points, largest first; only as many as a portion loads.
    private void Want(int count, int page)
    {
        if (_wanted.Count == PagesPerPortion && _wanted[^1].Count >= count)
        {
            return;
        }

        var at = _wanted.Count;
        while (at > 0 && _wanted[at - 1].Count < count)
        {
            at--;
        }

        _wanted.Insert(at, (count, page));
        if (_wanted.Count > PagesPerPortion)
        {
            _wanted.RemoveAt(_wanted.Count - 1);
        }
    }

    // A draw that starts where the one before ends joins it: whole samples lie side by side, so a view of the whole
    // cloud takes a few draws instead of one per page.
    private void AddDraw(int first, int count)
    {
        if (_draws.Count > 0 && _draws[^1] is var last && last.First + last.Count == first)
        {
            _draws[^1] = last with { Count = last.Count + count };
            return;
        }

        _draws.Add(new CloudDraw(first, count));
    }

    private void Create()
    {
        var count = _pages.PageCount;
        _leaves = new CloudLeaf[count];
        for (var i = 0; i < count; i++)
        {
            // The place on the GPU of a leaf is its page; the loader finds where the page lies.
            _leaves[i] = new CloudLeaf { First = i, Count = _pages.GetPage(i).Count, GpuFirst = i };
        }

        _boxes = new Point3D[2 * count];
        _isWanted = new bool[count];
        _slotOf = new int[count];
        var slots = Slots(cloud);
        (_pageInSlot, _slotUsed) = (new int[slots], new long[slots]);
        Array.Fill(_slotOf, -1);
        Array.Fill(_pageInSlot, -1);
    }

    // The boxes of the pages in the space of the scene, for the choice of what is in view.
    private void Measure(in BoxMap map)
    {
        for (var i = 0; i < _leaves.Length; i++)
        {
            ref readonly var page = ref _pages.GetPage(i);
            var (a, b) = (map.ToBox(ToData(page.Low)), map.ToBox(ToData(page.High)));
            _boxes[2 * i] = new Point3D(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
            _boxes[(2 * i) + 1] = new Point3D(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        }
    }

    private Point3D ToData(Vector3 share) => new(
        _pages.Low.X + (share.X * (_pages.High.X - _pages.Low.X)),
        _pages.Low.Y + (share.Y * (_pages.High.Y - _pages.Low.Y)),
        _pages.Low.Z + (share.Z * (_pages.High.Z - _pages.Low.Z)));

    private void TakePortion(ITraceTarget target)
    {
        if (_portion is not { IsCompletedSuccessfully: true } finished)
        {
            return;
        }

        _portion = null;
        var portion = finished.Result;
        if (portion.Generation != _generation)
        {
            foreach (var (page, _) in portion.Pages)
            {
                _isWanted[page] = false;
            }

            return;
        }

        if (portion.Pages.Length == 0)
        {
            target.Write(0, _chunk.AsSpan(0, SampleTotal));
            _hasSamples = true;
            cloud.IsReady = true;
            cloud.Report(1);
        }
        else
        {
            var at = 0;
            foreach (var (page, slot) in portion.Pages)
            {
                var count = _leaves[page].Count;
                target.Write(SampleTotal + (slot * CloudPages.PageSize), _chunk.AsSpan(at, count));
                at += count;
                (_slotOf[page], _isWanted[page]) = (slot, false);
                LoadedPages++;
            }
        }

        cloud.Touch();
    }

    private void StartPortion(BoxMap map)
    {
        var generation = _generation;
        if (!_hasSamples)
        {
            _chunk = Ensure(_chunk, SampleTotal);
            var chunk = _chunk;
            Begin(Task.Run(() =>
            {
                Parallel.For(0, _leaves.Length, page =>
                {
                    var points = _pages.Points(page, SampleSize);
                    Convert(points, chunk.AsSpan(page * SampleSize, points.Length), map);
                });
                return new Portion(generation, []);
            }));
            return;
        }

        // The pages the last frames wanted most, each into a free slot or the one longest unused.
        var chosen = new List<(int Page, int Slot)>();
        var points = 0;
        for (var i = 0; i < _wanted.Count && chosen.Count < PagesPerPortion; i++)
        {
            var page = _wanted[i].Page;
            if (_isWanted[page] || _slotOf[page] >= 0)
            {
                continue;
            }

            var slot = FreeSlot();
            if (slot < 0)
            {
                break;
            }

            if (_pageInSlot[slot] is var old and >= 0)
            {
                _slotOf[old] = -1;
                LoadedPages--;
            }

            (_pageInSlot[slot], _slotUsed[slot], _isWanted[page]) = (page, _frame, true);
            chosen.Add((page, slot));
            points += _leaves[page].Count;
        }

        _wanted.Clear();
        if (chosen.Count == 0)
        {
            return;
        }

        _chunk = Ensure(_chunk, Math.Max(points, SampleTotal));
        var target = _chunk;
        var pages = chosen.ToArray();
        Begin(Task.Run(() =>
        {
            var at = 0;
            var starts = new int[pages.Length];
            for (var i = 0; i < pages.Length; i++)
            {
                starts[i] = at;
                at += _leaves[pages[i].Page].Count;
            }

            Parallel.For(0, pages.Length, i =>
            {
                var page = pages[i].Page;
                Convert(_pages.Points(page, CloudPages.PageSize), target.AsSpan(starts[i], _leaves[page].Count), map);
            });
            return new Portion(generation, pages);
        }));
    }

    // A slot no page of the last frame used: an empty one first, else the one unused longest.
    private int FreeSlot()
    {
        var (best, oldest) = (-1, long.MaxValue);
        for (var slot = 0; slot < _pageInSlot.Length; slot++)
        {
            if (_pageInSlot[slot] < 0)
            {
                return slot;
            }

            if (_slotUsed[slot] < _frame - 1 && _slotUsed[slot] < oldest)
            {
                (best, oldest) = (slot, _slotUsed[slot]);
            }
        }

        return best;
    }

    private void Convert(ReadOnlySpan<Vector3> points, Span<LineVertex> target, in BoxMap map)
    {
        var code = Points3D.ToSizeCode(cloud.PointSize);
        var colors = cloud.HeightColors;
        var color = cloud.Color;
        for (var i = 0; i < points.Length; i++)
        {
            var place = ToData(points[i]);
            var shade = colors?.Sample(points[i].Z) ?? color;
            target[i] = new LineVertex(map.ToGpu(place.X, place.Y, place.Z), new Rgba8(shade.R, shade.G, shade.B, code));
        }
    }

    // When the worker is done, a new picture takes over its result.
    private void Begin(Task<Portion> portion)
    {
        _portion = portion;
        portion.ContinueWith(_ => cloud.Touch(), TaskScheduler.Default);
    }

    private static LineVertex[] Ensure(LineVertex[] chunk, int count) => chunk.Length < count ? new LineVertex[count] : chunk;

    private readonly record struct Portion(int Generation, (int Page, int Slot)[] Pages);
}
