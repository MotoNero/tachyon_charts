using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Geo;

/// <summary>
/// A map of regions, such as the countries of the world, on the plane of a projection: the sea, the land in one color
/// or a color per region, and the borders. A map with several levels of detail draws the coarse one for the whole world
/// and finer ones as the view comes closer, and only the regions in view, so it stays smooth from the world down to a
/// city. The map keeps one scale on both axes, so that shapes are not stretched.
/// </summary>
public sealed class GeoMapSeries : ChartSeries, IDisposable
{
    // From these shares of the width of the world the next finer level takes over.
    private const double MediumBelow = 0.35;
    private const double FineBelow = 0.06;

    private readonly IReadOnlyList<GeoMap> _levels;
    private readonly GeoProjection _projection;
    private readonly ProjectedLevel?[] _projected;
    private readonly List<MeshVertex> _mesh = [];
    private readonly List<Vector2> _borders = [];
    private readonly Dictionary<string, Rgba8> _colors = new(StringComparer.OrdinalIgnoreCase);
    private readonly (double[] X, double[] Y) _world;
    private readonly DoubleRange _worldX;
    private readonly DoubleRange _worldY;
    private IGpuBuffer? _buffer;
    private long _appearance;
    private long _built = -1;
    private SeriesFrame _builtFrame;
    private Rgba8 _land = new(214, 214, 200, 255);
    private Rgba8 _sea = new(196, 216, 232, 255);
    private Rgba8 _border = new(120, 120, 110, 255);
    private double _borderThickness = 0.6;
    private int _level;

    /// <summary>Starts a map.</summary>
    /// <param name="levels">The levels of detail, coarse to fine, such as <see cref="WorldMap.CountryLevels"/>; one map for one level.</param>
    /// <param name="projection">The projection.</param>
    /// <param name="xAxis">The X axis.</param>
    /// <param name="yAxis">The Y axis.</param>
    public GeoMapSeries(IReadOnlyList<GeoMap> levels, GeoProjection projection, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _levels = levels is { Count: > 0 } ? levels : throw new ArgumentException("A map needs a level of detail.", nameof(levels));
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _projected = new ProjectedLevel?[levels.Count];
        _world = Outline(projection);
        (_worldX, _worldY) = (new DoubleRange(_world.X.Min(), _world.X.Max()), new DoubleRange(_world.Y.Min(), _world.Y.Max()));
    }

    /// <summary>Gets the projection.</summary>
    public GeoProjection Projection => _projection;

    /// <summary>Gets or sets the color of land without a color of its own.</summary>
    public Rgba8 Land
    {
        get => _land;
        set => Change(ref _land, value);
    }

    /// <summary>Gets or sets the color of the sea; transparent for none.</summary>
    public Rgba8 Sea
    {
        get => _sea;
        set => Change(ref _sea, value);
    }

    /// <summary>Gets or sets the color of the borders; transparent for none.</summary>
    public Rgba8 Border
    {
        get => _border;
        set => Change(ref _border, value);
    }

    /// <summary>Gets or sets the thickness of the borders in device independent units.</summary>
    public double BorderThickness
    {
        get => _borderThickness;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _borderThickness = value;
            Interlocked.Increment(ref _appearance);
        }
    }

    /// <summary>Gets or sets a value indicating whether the map keeps one scale on both axes; on by default.</summary>
    public bool KeepsAspect { get; set; } = true;

    /// <summary>Gets the level of detail of the last frame, 0 the coarsest.</summary>
    public int Level => _level;

    /// <inheritdoc />
    public override Rgba8 AccentColor => _land;

    /// <inheritdoc />
    protected override long Version => Interlocked.Read(ref _appearance);

    /// <inheritdoc />
    protected override int DrawnPointCount => _mesh.Count;

    /// <summary>Colors regions by their codes, such as countries by a value; other regions keep <see cref="Land"/>.</summary>
    /// <param name="colors">The colors by code.</param>
    public void SetColors(IReadOnlyDictionary<string, Rgba8> colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        lock (_colors)
        {
            _colors.Clear();
            foreach (var (code, color) in colors)
            {
                _colors[code] = color;
            }
        }

        Interlocked.Increment(ref _appearance);
    }

    /// <summary>Finds the region at a place, at the finest level of the map.</summary>
    /// <param name="longitude">The longitude in degrees.</param>
    /// <param name="latitude">The latitude in degrees.</param>
    /// <returns>The region, or <see langword="null"/> at sea.</returns>
    public GeoRegion? RegionAt(double longitude, double latitude)
    {
        foreach (var region in _levels[^1].Regions)
        {
            var (west, south, east, north) = region.Bounds;
            if (longitude < west || longitude > east || latitude < south || latitude > north)
            {
                continue;
            }

            foreach (var polygon in region.Polygons)
            {
                if (polygon.Contains(longitude, latitude))
                {
                    return region;
                }
            }
        }

        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    /// <inheritdoc />
    protected override void ReleaseResources()
    {
        Dispose();
        _built = -1;
    }

    /// <inheritdoc />
    protected override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        (x, y) = (_worldX, _worldY);
        return true;
    }

    /// <inheritdoc />
    protected override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        (x, y) = (frame.X.ToData(pixel.X), frame.Y.ToData(pixel.Y));
        index = -1;
        if (!_projection.TryUnproject(x, y, out var longitude, out var latitude) || RegionAt(longitude, latitude) is not { } region)
        {
            return false;
        }

        index = IndexOf(region);
        return index >= 0;
    }

    /// <inheritdoc />
    protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        if (KeepsAspect && KeepAspect(frame))
        {
            return;
        }

        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        (_built, _builtFrame) = (version, frame);
        _mesh.Clear();
        _borders.Clear();
        var (left, right) = Order(frame.X.ToData(frame.Bounds.X), frame.X.ToData(frame.Bounds.X + frame.Bounds.Width));
        var (bottom, top) = Order(frame.Y.ToData(frame.Bounds.Y + frame.Bounds.Height), frame.Y.ToData(frame.Bounds.Y));
        var share = (right - left) / Math.Max(_worldX.Length, 1e-12);
        _level = Math.Min(_levels.Count - 1, LevelOf(share));
        var level = _projected[_level] ??= Project(_levels[_level]);

        if (_sea.A > 0)
        {
            AddSea(frame);
        }

        lock (_colors)
        {
            for (var r = 0; r < level.Regions.Length; r++)
            {
                ref readonly var region = ref level.Regions[r];
                if (region.MaxX >= left && region.MinX <= right && region.MaxY >= bottom && region.MinY <= top)
                {
                    AddRegion(frame, region);
                }
            }
        }

        if (_borders.Count >= 2)
        {
            UploadBorders(device);
        }
    }

    // The triangles of a region in its color, and its borders.
    private void AddRegion(in SeriesFrame frame, in ProjectedRegion region)
    {
        var color = _colors.TryGetValue(region.Code, out var own) ? own : _land;
        foreach (var area in region.Areas)
        {
            foreach (var corner in area.Triangles)
            {
                _mesh.Add(new MeshVertex(ToPixel(frame, area.X[corner], area.Y[corner]), color));
            }

            if (_border.A > 0 && _borderThickness > 0)
            {
                AddRings(frame, area);
            }
        }
    }

    private void UploadBorders(IRenderDevice device)
    {
        if (_buffer is null || _buffer.SizeInBytes < _borders.Count * 8L)
        {
            _buffer?.Dispose();
            _buffer = device.CreateBuffer(new BufferDesc(Math.Max(_borders.Count * 8L, 4096), BufferUsage.Vertex));
        }

        _buffer.Write<Vector2>(0, CollectionsMarshal.AsSpan(_borders));
    }

    /// <inheritdoc />
    protected override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        commands.FillMesh(CollectionsMarshal.AsSpan(_mesh), frame.Bounds);
        if (_buffer is not null && _borders.Count >= 2 && _border.A > 0)
        {
            commands.DrawPolyline(_buffer, 0, _borders.Count, new PolylineStyle((float)(_borderThickness * frame.DpiScale), _border) { Clip = frame.Bounds });
        }
    }

    private static Vector2 ToPixel(in SeriesFrame frame, double x, double y) => new((float)frame.X.ToPixel(x), (float)frame.Y.ToPixel(y));

    private static (double Low, double High) Order(double a, double b) => a <= b ? (a, b) : (b, a);

    // The outline of the world on the plane: along the meridians at ±180° and the parallels at ±90°.
    private static (double[] X, double[] Y) Outline(GeoProjection projection)
    {
        var (xs, ys) = (new List<double>(), new List<double>());
        void Add(double longitude, double latitude)
        {
            var (x, y) = projection.Project(longitude, latitude);
            xs.Add(x);
            ys.Add(y);
        }

        for (var latitude = -90.0; latitude <= 90; latitude += 2)
        {
            Add(-180, latitude);
        }

        for (var longitude = -180.0; longitude <= 180; longitude += 2)
        {
            Add(longitude, 90);
        }

        for (var latitude = 90.0; latitude >= -90; latitude -= 2)
        {
            Add(180, latitude);
        }

        for (var longitude = 180.0; longitude >= -180; longitude -= 2)
        {
            Add(longitude, -90);
        }

        return ([.. xs], [.. ys]);
    }

    // The sea as a fan from the middle of the outline, which every projection here keeps convex enough.
    private void AddSea(in SeriesFrame frame)
    {
        var middle = ToPixel(frame, (_worldX.Min + _worldX.Max) / 2, (_worldY.Min + _worldY.Max) / 2);
        for (var i = 0; i < _world.X.Length; i++)
        {
            var next = (i + 1) % _world.X.Length;
            _mesh.Add(new MeshVertex(middle, _sea));
            _mesh.Add(new MeshVertex(ToPixel(frame, _world.X[i], _world.Y[i]), _sea));
            _mesh.Add(new MeshVertex(ToPixel(frame, _world.X[next], _world.Y[next]), _sea));
        }
    }

    private void AddRings(in SeriesFrame frame, Area area)
    {
        var start = 0;
        foreach (var length in area.Rings)
        {
            if (_borders.Count > 0)
            {
                _borders.Add(new Vector2(PolylineStyle.Gap, 0));
            }

            for (var i = 0; i <= length; i++)
            {
                var at = start + (i % length);
                _borders.Add(ToPixel(frame, area.X[at], area.Y[at]));
            }

            start += length;
        }
    }

    // One scale on both axes: the vertical range follows the horizontal one around its middle; the next frame draws it.
    private bool KeepAspect(in SeriesFrame frame)
    {
        if (frame.Bounds.Width < 1 || frame.Bounds.Height < 1)
        {
            return false;
        }

        var (xRange, yRange) = (XAxis.VisibleRange, YAxis.VisibleRange);
        var wanted = xRange.Length * frame.Bounds.Height / frame.Bounds.Width;
        if (!double.IsFinite(wanted) || wanted <= 0 || Math.Abs(wanted - yRange.Length) <= yRange.Length * 1e-3)
        {
            return false;
        }

        var middle = (yRange.Min + yRange.Max) / 2;
        YAxis.VisibleRange = new DoubleRange(middle - (wanted / 2), middle + (wanted / 2));
        Interlocked.Increment(ref _appearance);
        return false;
    }

    private int IndexOf(GeoRegion region)
    {
        var regions = _levels[^1].Regions;
        for (var i = 0; i < regions.Count; i++)
        {
            if (ReferenceEquals(regions[i], region))
            {
                return i;
            }
        }

        return -1;
    }

    private void Change(ref Rgba8 field, Rgba8 value)
    {
        field = value;
        Interlocked.Increment(ref _appearance);
    }

    // A level on the plane: each region with its box and its areas, corners projected once.
    private ProjectedLevel Project(GeoMap map)
    {
        var regions = new ProjectedRegion[map.Regions.Count];
        Parallel.For(0, regions.Length, r =>
        {
            var region = map.Regions[r];
            var areas = new Area[region.Polygons.Count];
            (double minX, double minY, double maxX, double maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
            for (var p = 0; p < areas.Length; p++)
            {
                var (lon, lat, triangles, rings) = region.Triangles(p);
                var (x, y) = _projection.Project(lon, lat);
                for (var i = 0; i < x.Length; i++)
                {
                    (minX, minY, maxX, maxY) = (Math.Min(minX, x[i]), Math.Min(minY, y[i]), Math.Max(maxX, x[i]), Math.Max(maxY, y[i]));
                }

                areas[p] = new Area(x, y, triangles, rings);
            }

            regions[r] = new ProjectedRegion(region.Code, areas, minX, minY, maxX, maxY);
        });
        return new ProjectedLevel(regions);
    }

    private sealed record ProjectedLevel(ProjectedRegion[] Regions);

    private readonly record struct ProjectedRegion(string Code, Area[] Areas, double MinX, double MinY, double MaxX, double MaxY);

    private sealed record Area(double[] X, double[] Y, int[] Triangles, int[] Rings);

    // The coarse level for wide views, the finer ones as the view narrows.
    private static int LevelOf(double share)
    {
        if (share > MediumBelow)
        {
            return 0;
        }

        return share > FineBelow ? 1 : 2;
    }
}
