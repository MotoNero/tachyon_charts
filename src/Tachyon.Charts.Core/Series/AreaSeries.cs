using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A series made of ribbons between two curves. X values rise and are shared by all curves. The ribbons
/// are rebuilt when the data or the view changes, with at most one column per pixel.
/// </summary>
/// <param name="xAxis">The horizontal axis.</param>
/// <param name="yAxis">The vertical axis.</param>
public abstract class RibbonSeries(AxisBase xAxis, AxisBase yAxis) : ChartSeries(xAxis, yAxis)
{
    private const int ValueSteps = 24;

    private readonly List<MeshVertex> _mesh = [];
    private MeshVertex[] _vertices = [];
    private int _count;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private long _appearanceVersion;

    /// <summary>Gets the X values of all curves.</summary>
    /// <returns>The values.</returns>
    protected abstract ReadOnlyMemory<double> ReadX();

    /// <summary>Fills in the ribbons of the current data.</summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="mesh">Receives the triangles.</param>
    protected abstract void Build(in SeriesFrame frame, List<MeshVertex> mesh);

    /// <summary>Requests a new frame after a change of the appearance.</summary>
    protected void OnAppearanceChanged() => Interlocked.Increment(ref _appearanceVersion);

    /// <summary>Gets the version of the appearance.</summary>
    protected long AppearanceVersion => Volatile.Read(ref _appearanceVersion);

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (_builtVersion == version && _builtFrame == frame)
        {
            return;
        }

        _mesh.Clear();
        Build(frame, _mesh);
        // The array only grows, so a view that moves does not allocate.
        if (_vertices.Length < _mesh.Count)
        {
            _vertices = new MeshVertex[_mesh.Count * 5 / 4];
        }

        _mesh.CopyTo(_vertices);
        _count = _mesh.Count;
        _builtVersion = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) =>
        commands.FillMesh(_vertices.AsSpan(0, _count), frame.Bounds);

    /// <summary>Adds the ribbon between two curves, with gaps where a curve has no value.</summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="x">The X values.</param>
    /// <param name="lower">The first curve.</param>
    /// <param name="upper">The second curve.</param>
    /// <param name="fill">The fill.</param>
    /// <param name="mesh">Receives the triangles.</param>
    /// <param name="smoothsTop">Whether the upper curve borders free space and gets a soft edge. An edge against another ribbon stays hard, or the two edges would show a seam.</param>
    protected static void AddRibbon(in SeriesFrame frame, ReadOnlySpan<double> x, ReadOnlySpan<double> lower, ReadOnlySpan<double> upper, SeriesFill fill, List<MeshVertex> mesh, bool smoothsTop = false)
    {
        var count = Math.Min(x.Length, Math.Min(lower.Length, upper.Length));
        var stride = Math.Max(count / Math.Max((int)(frame.Bounds.Width * 2), 1), 1);
        var hasPrevious = false;
        Column previous = default;
        for (var i = 0; i < count; i += stride)
        {
            // Within a stride the column shows the widest part, so that peaks survive.
            var low = lower[i];
            var high = upper[i];
            for (var j = i + 1; j < Math.Min(i + stride, count); j++)
            {
                low = Extreme(low, lower[j], high);
                high = Extreme(high, upper[j], low);
            }

            // On whole pixels: an edge through pixel centers would fall differently in each renderer.
            var column = new Column(MathF.Round((float)frame.X.ToPixel(x[i])), low, high);
            if (double.IsNaN(low) || double.IsNaN(high) || !float.IsFinite(column.Pixel))
            {
                hasPrevious = false;
                continue;
            }

            if (hasPrevious)
            {
                AddQuad(frame, previous, column, fill, mesh, smoothsTop);
            }

            previous = column;
            hasPrevious = true;
        }
    }

    // The value further away from the other curve.
    private static double Extreme(double current, double candidate, double other)
    {
        if (double.IsNaN(candidate) || double.IsNaN(current))
        {
            return double.IsNaN(candidate) ? double.NaN : current;
        }

        return Math.Abs(candidate - other) > Math.Abs(current - other) ? candidate : current;
    }

    private static void AddQuad(in SeriesFrame frame, in Column first, in Column second, SeriesFill fill, List<MeshVertex> mesh, bool smoothsTop)
    {
        // A soft band of one pixel above the upper curve; the steps between the columns join inside.
        if (smoothsTop)
        {
            var (a, b) = (Vertex(frame, first.Pixel, first.Lower, fill), Vertex(frame, first.Pixel, first.Upper, fill));
            var (c, d) = (Vertex(frame, second.Pixel, second.Upper, fill), Vertex(frame, second.Pixel, second.Lower, fill));
            MeshFringe.Add(mesh, b, c, (a.Position + d.Position) / 2);
        }

        // Colors blend linearly across a triangle, a colormap does not: it needs steps along the values.
        if (fill is ValueGradientFill)
        {
            for (var step = 0; step < ValueSteps; step++)
            {
                var from = step / (double)ValueSteps;
                var to = (step + 1) / (double)ValueSteps;
                AddSlice(frame, first with { Lower = Lerp(first, from), Upper = Lerp(first, to) }, second with { Lower = Lerp(second, from), Upper = Lerp(second, to) }, fill, mesh);
            }

            return;
        }

        AddSlice(frame, first, second, fill, mesh);
    }

    private static double Lerp(in Column column, double part) => column.Lower + ((column.Upper - column.Lower) * part);

    private static void AddSlice(in SeriesFrame frame, in Column first, in Column second, SeriesFill fill, List<MeshVertex> mesh)
    {
        var a = Vertex(frame, first.Pixel, first.Lower, fill);
        var b = Vertex(frame, first.Pixel, first.Upper, fill);
        var c = Vertex(frame, second.Pixel, second.Upper, fill);
        var d = Vertex(frame, second.Pixel, second.Lower, fill);
        mesh.Add(a);
        mesh.Add(b);
        mesh.Add(c);
        mesh.Add(a);
        mesh.Add(c);
        mesh.Add(d);
    }

    private static MeshVertex Vertex(in SeriesFrame frame, float x, double value, SeriesFill fill)
    {
        var y = frame.Y.ToPixel(value);
        return new MeshVertex(new Vector2(x, (float)y), fill.GetColor((y - frame.Bounds.Y) / frame.Bounds.Height, value));
    }

    private readonly record struct Column(float Pixel, double Lower, double Upper);
}

/// <summary>
/// The area between a curve and a baseline.
/// </summary>
public sealed class AreaSeries : RibbonSeries
{
    private readonly IDataSource<double, double> _source;
    private readonly SeriesHitTester<double, double> _hitTester;
    private double _baseline;
    private SeriesFill _fill = new SolidFill(new Rgba8(80, 170, 240, 160));
    private double[] _lower = [];

    /// <summary>Initializes a new instance of the <see cref="AreaSeries"/> class.</summary>
    /// <param name="source">The curve.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public AreaSeries(IDataSource<double, double> source, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _hitTester = new SeriesHitTester<double, double>(source);
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y) =>
        _hitTester.TryFind(frame, 0, pixel, maximumDistance, mode, out index, out x, out y);

    /// <inheritdoc />
    protected internal override void CollectPoints(in SeriesFrame frame, in SelectionArea area, List<long> indices) =>
        _hitTester.CollectPoints(frame, 0, area, indices);

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y) =>
        _hitTester.TryReadPoint(0, index, out x, out y);

    /// <summary>Gets or sets the value the area reaches down to.</summary>
    public double Baseline
    {
        get => _baseline;
        set
        {
            _baseline = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the fill.</summary>
    public SeriesFill Fill
    {
        get => _fill;
        set
        {
            _fill = value ?? throw new ArgumentNullException(nameof(value));
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _fill.GetColor(0, 0);

    /// <inheritdoc />
    protected internal override long Version => _source.Version + AppearanceVersion;

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var metadata = _source.Metadata;
        x = metadata.XRange;
        y = metadata.YRange.IsEmpty ? metadata.YRange : metadata.YRange.Union(new DoubleRange(_baseline, _baseline));
        return metadata.Count > 0;
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<double> ReadX() => _source.GetSnapshot().X;

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        var snapshot = _source.GetSnapshot();
        var count = snapshot.X.Length;
        if (_lower.Length != count)
        {
            _lower = new double[count];
        }

        Array.Fill(_lower, _baseline);
        AddRibbon(frame, snapshot.X.Span, _lower, snapshot.Y.Span, _fill, mesh, smoothsTop: true);
    }
}

/// <summary>
/// Areas on top of each other. All layers share the X values of the first one. A missing value (NaN)
/// counts as zero, so that the layers above keep their place.
/// </summary>
public sealed class StackedAreaSeries : RibbonSeries
{
    private readonly IReadOnlyList<IDataSource<double, double>> _layers;
    private IReadOnlyList<SeriesFill> _fills;
    private bool _isPercent;

    /// <summary>Initializes a new instance of the <see cref="StackedAreaSeries"/> class.</summary>
    /// <param name="layers">The layers, the first at the bottom.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    /// <exception cref="ArgumentException">There is no layer.</exception>
    public StackedAreaSeries(IReadOnlyList<IDataSource<double, double>> layers, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("A stack needs at least one layer.", nameof(layers));
        }

        _layers = layers;
        _fills = [.. layers.Select((_, index) => (SeriesFill)new SolidFill(DefaultColor(index)))];
    }

    /// <summary>Gets or sets the fills of the layers.</summary>
    public IReadOnlyList<SeriesFill> Fills
    {
        get => _fills;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count != _layers.Count)
            {
                throw new ArgumentException("Every layer needs a fill.", nameof(value));
            }

            _fills = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets a value indicating whether every column adds up to 100.</summary>
    public bool IsPercent
    {
        get => _isPercent;
        set
        {
            _isPercent = value;
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    protected internal override long Version => _layers.Sum(layer => layer.Version) + AppearanceVersion;

    /// <summary>Adds up layers.</summary>
    /// <param name="layers">The values of the layers, the first at the bottom.</param>
    /// <param name="isPercent">Whether every column adds up to 100.</param>
    /// <returns>The upper edge of each layer.</returns>
    public static double[][] Stack(IReadOnlyList<double[]> layers, bool isPercent)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var count = layers.Count == 0 ? 0 : layers.Min(layer => layer.Length);
        var edges = new double[layers.Count][];
        for (var layer = 0; layer < layers.Count; layer++)
        {
            edges[layer] = new double[count];
        }

        for (var i = 0; i < count; i++)
        {
            var sum = 0.0;
            for (var layer = 0; layer < layers.Count; layer++)
            {
                var value = layers[layer][i];
                sum += double.IsFinite(value) ? value : 0;
                edges[layer][i] = sum;
            }

            if (isPercent && sum != 0)
            {
                for (var layer = 0; layer < layers.Count; layer++)
                {
                    edges[layer][i] *= 100 / sum;
                }
            }
        }

        return edges;
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var edges = ReadEdges();
        x = _layers[0].Metadata.XRange;
        y = DoubleRange.Empty;
        foreach (var value in edges[^1])
        {
            y = y.Union(new DoubleRange(Math.Min(value, 0), Math.Max(value, 0)));
        }

        return edges[0].Length > 0;
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<double> ReadX() => _layers[0].GetSnapshot().X;

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        var edges = ReadEdges();
        var x = ReadX().Span;
        var zero = new double[edges[0].Length];
        for (var layer = 0; layer < edges.Length; layer++)
        {
            AddRibbon(frame, x, layer == 0 ? zero : edges[layer - 1], edges[layer], _fills[layer], mesh, smoothsTop: layer == edges.Length - 1);
        }
    }

    private static Rgba8 DefaultColor(int index)
    {
        ReadOnlySpan<Rgba8> colors = [new(76, 201, 240, 200), new(247, 37, 133, 200), new(124, 252, 154, 200), new(255, 183, 3, 200), new(179, 136, 255, 200)];
        return colors[index % colors.Length];
    }

    private double[][] ReadEdges() => Stack([.. _layers.Select(layer => layer.GetSnapshot().Y.ToArray())], _isPercent);
}

/// <summary>
/// The band between two curves, with a fill of its own for each side: where the first curve lies above
/// the second one, and the other way round. Crossings are found, so that the fills meet exactly.
/// </summary>
public sealed class BandSeries : RibbonSeries
{
    private readonly IDataSource<double, double> _first;
    private readonly IDataSource<double, double> _second;
    private SeriesFill _firstAboveFill = new SolidFill(new Rgba8(110, 210, 130, 150));
    private SeriesFill _secondAboveFill = new SolidFill(new Rgba8(240, 100, 100, 150));

    /// <summary>Initializes a new instance of the <see cref="BandSeries"/> class.</summary>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second curve, with the same X values.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public BandSeries(IDataSource<double, double> first, IDataSource<double, double> second, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _first = first ?? throw new ArgumentNullException(nameof(first));
        _second = second ?? throw new ArgumentNullException(nameof(second));
    }

    /// <summary>Gets or sets the fill where the first curve lies above the second one.</summary>
    public SeriesFill FirstAboveFill
    {
        get => _firstAboveFill;
        set
        {
            _firstAboveFill = value ?? throw new ArgumentNullException(nameof(value));
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the fill where the second curve lies above the first one.</summary>
    public SeriesFill SecondAboveFill
    {
        get => _secondAboveFill;
        set
        {
            _secondAboveFill = value ?? throw new ArgumentNullException(nameof(value));
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    protected internal override long Version => _first.Version + _second.Version + AppearanceVersion;

    /// <summary>
    /// Inserts the crossings of two curves as points of their own.
    /// </summary>
    /// <param name="x">The X values.</param>
    /// <param name="first">The first curve.</param>
    /// <param name="second">The second curve.</param>
    /// <returns>The points, with both curves equal at a crossing.</returns>
    public static (double[] X, double[] First, double[] Second) InsertCrossings(ReadOnlySpan<double> x, ReadOnlySpan<double> first, ReadOnlySpan<double> second)
    {
        var count = Math.Min(x.Length, Math.Min(first.Length, second.Length));
        var outX = new List<double>(count);
        var outFirst = new List<double>(count);
        var outSecond = new List<double>(count);
        for (var i = 0; i < count; i++)
        {
            if (i > 0)
            {
                var before = first[i - 1] - second[i - 1];
                var after = first[i] - second[i];
                if (before * after < 0)
                {
                    var part = before / (before - after);
                    var value = first[i - 1] + ((first[i] - first[i - 1]) * part);
                    outX.Add(x[i - 1] + ((x[i] - x[i - 1]) * part));
                    outFirst.Add(value);
                    outSecond.Add(value);
                }
            }

            outX.Add(x[i]);
            outFirst.Add(first[i]);
            outSecond.Add(second[i]);
        }

        return ([.. outX], [.. outFirst], [.. outSecond]);
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = _first.Metadata.XRange;
        y = _first.Metadata.YRange.Union(_second.Metadata.YRange);
        return _first.Metadata.Count > 0;
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<double> ReadX() => _first.GetSnapshot().X;

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        var (x, first, second) = InsertCrossings(ReadX().Span, _first.GetSnapshot().Y.Span, _second.GetSnapshot().Y.Span);

        // Each side is a ribbon that collapses to nothing where the other one is on top.
        var lowerOfBoth = new double[x.Length];
        var firstTop = new double[x.Length];
        var secondTop = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var low = Math.Min(first[i], second[i]);
            lowerOfBoth[i] = double.IsNaN(first[i]) || double.IsNaN(second[i]) ? double.NaN : low;
            firstTop[i] = first[i];
            secondTop[i] = second[i];
        }

        AddRibbon(frame, x, lowerOfBoth, firstTop, _firstAboveFill, mesh);
        AddRibbon(frame, x, lowerOfBoth, secondTop, _secondAboveFill, mesh);
    }
}
