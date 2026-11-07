using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;

using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>What a 3D chart shows: its camera and three axes.</summary>
public sealed class Scene3D
{
    private readonly List<Mesh3D> _meshes = [];
    private readonly List<Surface3D> _surfaces = [];
    private readonly List<Waterfall3D> _waterfalls = [];
    private readonly List<MeasuredMap3D> _maps = [];
    private readonly Lock _markerGate = new();
    private Pick3D[] _markers = [];
    private readonly List<Line3D> _lines = [];
    private readonly List<Scatter3D> _scatters = [];
    private readonly List<Bars3D> _bars = [];
    private readonly List<Bubbles3D> _bubbles = [];
    private readonly List<Pie3D> _pies = [];
    private bool _showsAxes = true;
    private readonly List<PointCloud3D> _clouds = [];
    private readonly List<Volume3D> _volumes = [];
    private readonly List<ClipPlane3D> _planes = [];
    private const int MaximumHidingPlanes = 4;
    private long _markerVersion;
    private readonly List<CutCurveFeed> _feeds = [];
    private double? _cursor;
    private readonly List<AntennaPattern3D> _antennas = [];
    private long _version;

    /// <summary>Occurs after a marker has been added from code or by a click in the chart.</summary>
    public event Action<Pick3D>? MarkerAdded;

    /// <summary>Initializes a new instance of the <see cref="Scene3D"/> class with three axes from 0 to 1.</summary>
    public Scene3D()
    {
        XAxis = CreateAxis("x3d");
        YAxis = CreateAxis("y3d");
        ZAxis = CreateAxis("z3d");
    }

    /// <summary>Gets the surfaces.</summary>
    public IReadOnlyList<Mesh3D> Meshes => _meshes;

    /// <summary>Gets the raster surfaces.</summary>
    public IReadOnlyList<Surface3D> Surfaces => _surfaces;

    /// <summary>Gets the maps from measured points.</summary>
    public IReadOnlyList<MeasuredMap3D> Maps => _maps;

    /// <summary>Gets the antenna patterns.</summary>
    public IReadOnlyList<AntennaPattern3D> Antennas => _antennas;

    /// <summary>Gets the markers.</summary>
    public IReadOnlyList<Pick3D> Markers => Volatile.Read(ref _markers);

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<Line3D> Lines => _lines;

    /// <summary>Gets the clouds of points.</summary>
    public IReadOnlyList<Scatter3D> Scatters => _scatters;

    /// <summary>Gets the columns.</summary>
    public IReadOnlyList<Bars3D> Bars => _bars;

    /// <summary>Gets the bubbles.</summary>
    public IReadOnlyList<Bubbles3D> Bubbles => _bubbles;

    /// <summary>Gets the pies.</summary>
    public IReadOnlyList<Pie3D> Pies => _pies;

    /// <summary>Gets or sets a value indicating whether the box shows its grid, frame, scales and titles. A pie needs none of them.</summary>
    public bool ShowsAxes
    {
        get => Volatile.Read(ref _showsAxes);
        set
        {
            Volatile.Write(ref _showsAxes, value);
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Gets the planes that cut the scene.</summary>
    public IReadOnlyList<ClipPlane3D> Planes => _planes;

    /// <summary>Gets the very large clouds of points.</summary>
    public IReadOnlyList<PointCloud3D> Clouds => _clouds;

    /// <summary>Gets the volumes.</summary>
    public IReadOnlyList<Volume3D> Volumes => _volumes;

    /// <summary>Gets the waterfalls.</summary>
    public IReadOnlyList<Waterfall3D> Waterfalls => _waterfalls;

    /// <summary>Gets a number that grows when the content changes.</summary>
    public long Version
    {
        get
        {
            // Ranges belong to the content: a chart group moves them from outside.
            var version = Volatile.Read(ref _version) + XAxis.Version + YAxis.Version + ZAxis.Version;
            foreach (var surface in _surfaces)
            {
                version += surface.Data.Version;
            }

            foreach (var waterfall in _waterfalls)
            {
                version += waterfall.Data.TotalRows;
            }

            foreach (var line in _lines)
            {
                version += line.Points.Total;
            }

            foreach (var scatter in _scatters)
            {
                version += scatter.Points.Total;
            }

            foreach (var bars in _bars)
            {
                version += bars.Data.Version;
            }

            foreach (var pie in _pies)
            {
                version += pie.Version;
            }

            foreach (var plane in _planes)
            {
                version += plane.Version;
            }

            foreach (var cloud in _clouds)
            {
                version += cloud.Stamp;
            }

            foreach (var volume in _volumes)
            {
                version += volume.Stamp + (volume.Transfer?.Version ?? 0);
            }

            return version;
        }
    }

    /// <summary>Gets the view.</summary>
    public Camera3D Camera { get; } = new();

    /// <summary>Gets or sets the first axis. Numeric, time and logarithmic axes work.</summary>
    public AxisBase XAxis { get; set; }

    /// <summary>Gets or sets the second axis.</summary>
    public AxisBase YAxis { get; set; }

    /// <summary>Gets or sets the third axis, which points up.</summary>
    public AxisBase ZAxis { get; set; }

    /// <summary>Adds a surface.</summary>
    /// <param name="mesh">The surface.</param>
    public void Add(Mesh3D mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        _meshes.Add(mesh);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a raster surface.</summary>
    /// <param name="surface">The surface.</param>
    public void Add(Surface3D surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        _surfaces.Add(surface);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a waterfall.</summary>
    /// <param name="waterfall">The waterfall.</param>
    public void Add(Waterfall3D waterfall)
    {
        ArgumentNullException.ThrowIfNull(waterfall);
        _waterfalls.Add(waterfall);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a map from measured points.</summary>
    /// <param name="map">The map.</param>
    public void Add(MeasuredMap3D map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _maps.Add(map);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds an antenna pattern.</summary>
    /// <param name="antenna">The pattern.</param>
    public void Add(AntennaPattern3D antenna)
    {
        ArgumentNullException.ThrowIfNull(antenna);
        _antennas.Add(antenna);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a line.</summary>
    /// <param name="line">The line.</param>
    public void Add(Line3D line)
    {
        ArgumentNullException.ThrowIfNull(line);
        _lines.Add(line);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a cloud of points.</summary>
    /// <param name="scatter">The cloud.</param>
    public void Add(Scatter3D scatter)
    {
        ArgumentNullException.ThrowIfNull(scatter);
        _scatters.Add(scatter);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds columns.</summary>
    /// <param name="bars">The columns.</param>
    public void Add(Bars3D bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        _bars.Add(bars);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds bubbles.</summary>
    /// <param name="bubbles">The bubbles.</param>
    public void Add(Bubbles3D bubbles)
    {
        ArgumentNullException.ThrowIfNull(bubbles);
        _bubbles.Add(bubbles);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a pie.</summary>
    /// <param name="pie">The pie.</param>
    public void Add(Pie3D pie)
    {
        ArgumentNullException.ThrowIfNull(pie);
        _pies.Add(pie);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a plane. Up to four of them can hide data, which is what the GPU takes per frame.</summary>
    /// <param name="plane">The plane.</param>
    /// <exception cref="InvalidOperationException">A fifth plane should hide data.</exception>
    public void Add(ClipPlane3D plane)
    {
        ArgumentNullException.ThrowIfNull(plane);
        if (plane.Hides && _planes.Count(p => p.Hides) >= MaximumHidingPlanes)
        {
            throw new InvalidOperationException("At most four planes can hide data.");
        }

        _planes.Add(plane);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a very large cloud of points.</summary>
    /// <param name="cloud">The cloud.</param>
    public void Add(PointCloud3D cloud)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        _clouds.Add(cloud);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a volume with its three slices.</summary>
    /// <param name="volume">The volume.</param>
    public void Add(Volume3D volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        _volumes.Add(volume);
        if (volume.ShowsSlices)
        {
            Add(volume.SliceX);
            Add(volume.SliceY);
            Add(volume.SliceZ);
        }

        Interlocked.Increment(ref _version);
    }

    /// <summary>Adds a curve that follows a cut through a surface.</summary>
    /// <param name="feed">The curve.</param>
    public void Add(CutCurveFeed feed)
    {
        ArgumentNullException.ThrowIfNull(feed);
        _feeds.Add(feed);
        feed.Scene = this;
    }

    /// <summary>Gets the curves that follow cuts.</summary>
    internal IReadOnlyList<CutCurveFeed> Feeds => _feeds;

    /// <summary>Gets or sets the place of a cursor on the first axis, shown as a frame across the box. A chart group sets it from the cursor of its 2D charts.</summary>
    public double? Cursor
    {
        get => _cursor;
        set
        {
            _cursor = value;
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Gets or sets the group of charts this scene belongs to.</summary>
    internal ChartGroup? Group { get; set; }

    /// <summary>Gets a number that changes with the markers. They lie over the picture, so it stays as it is.</summary>
    internal long MarkerVersion => Volatile.Read(ref _markerVersion);

    /// <summary>Sets a marker.</summary>
    /// <param name="marker">The place.</param>
    public void AddMarker(Pick3D marker)
    {
        lock (_markerGate)
        {
            Volatile.Write(ref _markers, [.. _markers, marker]);
        }

        Interlocked.Increment(ref _markerVersion);
        MarkerAdded?.Invoke(marker);
    }

    /// <summary>Removes the newest marker.</summary>
    public void RemoveLastMarker() => RemoveMarker(Markers.Count - 1);

    /// <summary>Removes a marker.</summary>
    /// <param name="index">The place of the marker in <see cref="Markers"/>; outside of it nothing happens.</param>
    public void RemoveMarker(int index)
    {
        lock (_markerGate)
        {
            if (index < 0 || index >= _markers.Length)
            {
                return;
            }

            Volatile.Write(ref _markers, [.. _markers[..index], .. _markers[(index + 1)..]]);
        }

        Interlocked.Increment(ref _markerVersion);
    }

    private static NumericAxis CreateAxis(string id) => new(id, AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 1) };
}
