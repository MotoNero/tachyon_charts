using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// Filled polygons with holes and their outlines, for areas such as regions, zones or limits. Each polygon is cut
/// into triangles once; a frame only places the corners on the screen. The pointer finds the polygon it is in.
/// </summary>
public sealed class PolygonSeries : ChartSeries, IDisposable
{
    private readonly Polygon[] _polygons;
    private readonly List<MeshVertex> _mesh = [];
    private readonly List<Vector2> _outline = [];
    private (double[] X, double[] Y, int[] Triangles)[]? _triangles;
    private Rgba8[] _palette = [new(76, 201, 240, 255), new(247, 37, 133, 255), new(255, 190, 11, 255), new(6, 214, 160, 255)];
    private IGpuBuffer? _buffer;
    private byte _fillAlpha = 170;
    private Rgba8 _stroke = new(255, 255, 255, 200);
    private double _thickness = 1;
    private long _appearance;
    private long _built = -1;
    private SeriesFrame _builtFrame;
    private DoubleRange _x;
    private DoubleRange _y;

    /// <summary>Initializes a new instance of the <see cref="PolygonSeries"/> class.</summary>
    /// <param name="polygons">The polygons.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public PolygonSeries(IReadOnlyList<Polygon> polygons, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(polygons);
        _polygons = [.. polygons];
        (_x, _y) = (DoubleRange.Empty, DoubleRange.Empty);
        foreach (var polygon in _polygons)
        {
            for (var i = 0; i < polygon.Outer.Count; i++)
            {
                _x = _x.Union(new DoubleRange(polygon.Outer.X[i], polygon.Outer.X[i]));
                _y = _y.Union(new DoubleRange(polygon.Outer.Y[i], polygon.Outer.Y[i]));
            }
        }
    }

    /// <summary>Gets the polygons.</summary>
    public IReadOnlyList<Polygon> Polygons => _polygons;

    /// <summary>Gets or sets the opacity of the fill, from 0 to 255; 0 draws only the outlines.</summary>
    public byte FillAlpha
    {
        get => _fillAlpha;
        set
        {
            _fillAlpha = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the color of the outlines; a transparent color draws none.</summary>
    public Rgba8 Stroke
    {
        get => _stroke;
        set
        {
            _stroke = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the thickness of the outlines in device-independent units.</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            _thickness = value;
            Touch();
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _palette[0];

    /// <inheritdoc />
    protected internal override long Version => Interlocked.Read(ref _appearance);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _mesh.Count;

    internal int TriangleCount => _mesh.Count / 3;

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _built = -1;
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources() => Dispose();

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette)
    {
        _palette = palette.ToArray();
        Touch();
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        (x, y) = (_x, _y);
        return !_x.IsEmpty;
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        (x, y) = (frame.X.ToData(pixel.X), frame.Y.ToData(pixel.Y));
        for (var i = _polygons.Length - 1; i >= 0; i--)
        {
            if (_polygons[i].Contains(x, y))
            {
                index = i;
                return true;
            }
        }

        index = -1;
        return false;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        _triangles ??= [.. _polygons.Select(PolygonTriangulator.Triangulate)];
        _mesh.Clear();
        _outline.Clear();
        for (var p = 0; p < _polygons.Length; p++)
        {
            AddPolygon(frame, p);
        }

        if (_outline.Count >= 2)
        {
            WriteOutline(device);
        }

        _built = version;
        _builtFrame = frame;
    }

    private void AddPolygon(in SeriesFrame frame, int p)
    {
        var polygon = _polygons[p];
        var color = (polygon.Color.A > 0 ? polygon.Color : _palette[p % _palette.Length]) with { A = _fillAlpha };
        if (_fillAlpha > 0)
        {
            var (xs, ys, triangles) = _triangles![p];
            foreach (var corner in triangles)
            {
                _mesh.Add(new MeshVertex(new Vector2((float)frame.X.ToPixel(xs[corner]), (float)frame.Y.ToPixel(ys[corner])), color));
            }
        }

        if (_stroke.A > 0)
        {
            AddRing(frame, polygon.Outer);
            foreach (var hole in polygon.Holes)
            {
                AddRing(frame, hole);
            }
        }
    }

    private void WriteOutline(IRenderDevice device)
    {
        var size = _outline.Count * 8L;
        if (_buffer is null || _buffer.SizeInBytes < size)
        {
            _buffer?.Dispose();
            _buffer = device.CreateBuffer(new BufferDesc(size, BufferUsage.Vertex));
        }

        _buffer.Write<Vector2>(0, CollectionsMarshal.AsSpan(_outline));
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        commands.FillMesh(CollectionsMarshal.AsSpan(_mesh), frame.Bounds);
        if (_buffer is not null && _outline.Count >= 2)
        {
            commands.DrawPolyline(_buffer, 0, _outline.Count, new PolylineStyle((float)(_thickness * frame.DpiScale), _stroke) { Clip = frame.Bounds });
        }
    }

    private void Touch() => Interlocked.Increment(ref _appearance);

    private void AddRing(in SeriesFrame frame, PolygonRing ring)
    {
        for (var i = 0; i <= ring.Count; i++)
        {
            var k = i % ring.Count;
            _outline.Add(new Vector2((float)frame.X.ToPixel(ring.X[k]), (float)frame.Y.ToPixel(ring.Y[k])));
        }

        _outline.Add(new Vector2(PolylineStyle.Gap));
    }
}

/// <summary>Describes polygons.</summary>
public static class PolygonCharts
{
    /// <summary>Describes filled polygons with holes and outlines.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="polygons">The polygons.</param>
    /// <param name="configure">Sets further properties, for example <see cref="PolygonSeries.FillAlpha"/>.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Polygons(this ChartBuilder builder, IReadOnlyList<Polygon> polygons, Action<PolygonSeries>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddSeries((x, y) =>
        {
            var series = new PolygonSeries(polygons, x, y);
            configure?.Invoke(series);
            return series;
        });
    }
}
