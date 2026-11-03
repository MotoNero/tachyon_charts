using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// The base of series with one mark per point, such as lines with a head or error bars. They have no
/// hit test and no rows in a CSV export.
/// </summary>
public abstract class MarkSeries : RibbonSeries, IDisposable
{
    private readonly IDataSource<double, double> _source;
    private readonly List<MarkerInstance> _heads = [];
    private IGpuBuffer? _buffer;
    private bool _areHeadsDirty;
    private Rgba8 _color = new(80, 170, 240, 255);
    private double _thickness = 1;

    /// <summary>Initializes a new instance of the <see cref="MarkSeries"/> class.</summary>
    /// <param name="source">The points.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    protected MarkSeries(IDataSource<double, double> source, Axes.AxisBase xAxis, Axes.AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
    }

    /// <summary>Gets or sets the color.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the thickness of the lines in device-independent units.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double Thickness
    {
        get => _thickness;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _thickness = value;
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    protected internal override long Version => _source.Version + AppearanceVersion;

    /// <summary>Gets the points.</summary>
    protected IDataSource<double, double> Source => _source;

    /// <inheritdoc />
    protected override ReadOnlyMemory<double> ReadX() => _source.GetSnapshot().X;

    /// <summary>Adds a filled rectangle.</summary>
    /// <param name="mesh">The triangles.</param>
    /// <param name="left">The left edge in pixels.</param>
    /// <param name="top">The upper edge in pixels.</param>
    /// <param name="right">The right edge in pixels.</param>
    /// <param name="bottom">The lower edge in pixels.</param>
    protected void AddRectangle(List<MeshVertex> mesh, float left, float top, float right, float bottom)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        // On whole pixels: without edge smoothing, the backends agree only there.
        left = MathF.Round(left);
        top = MathF.Round(top);
        right = MathF.Max(MathF.Round(right), left + 1);
        bottom = MathF.Max(MathF.Round(bottom), top + 1);
        mesh.Add(new MeshVertex(new Vector2(left, top), _color));
        mesh.Add(new MeshVertex(new Vector2(right, top), _color));
        mesh.Add(new MeshVertex(new Vector2(right, bottom), _color));
        mesh.Add(new MeshVertex(new Vector2(left, top), _color));
        mesh.Add(new MeshVertex(new Vector2(right, bottom), _color));
        mesh.Add(new MeshVertex(new Vector2(left, bottom), _color));
    }

    /// <summary>Forgets the heads of the last frame. <see cref="RibbonSeries.Build"/> starts with this.</summary>
    protected void ClearHeads()
    {
        _heads.Clear();
        _areHeadsDirty = true;
    }

    /// <summary>Adds a round head, smoothed like the markers of a scatter series.</summary>
    /// <param name="center">The center in pixels.</param>
    /// <param name="diameter">The diameter in pixels.</param>
    protected void AddHead(Vector2 center, float diameter) => _heads.Add(new MarkerInstance(center, diameter, _color));

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(device);
        base.Prepare(device, frame);
        if (!_areHeadsDirty)
        {
            return;
        }

        _areHeadsDirty = false;
        var byteCount = (long)_heads.Count * Unsafe.SizeOf<MarkerInstance>();
        if (byteCount > 0)
        {
            if (_buffer is null || _buffer.SizeInBytes < byteCount)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(byteCount, BufferUsage.Vertex));
            }

            _buffer.Write<MarkerInstance>(0, CollectionsMarshal.AsSpan(_heads));
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(commands);
        base.Draw(commands, frame);
        if (_heads.Count > 0 && _buffer is not null)
        {
            commands.DrawMarkers(_buffer, 0, _heads.Count, new MarkerStyle(MarkerShape.Circle, 0, default) { Clip = frame.Bounds });
        }
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources() => Dispose();

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _areHeadsDirty = true;
        GC.SuppressFinalize(this);
    }
}

/// <summary>Vertical lines from a baseline to each value, with a head. Typical for spectra.</summary>
public sealed class StemSeries : MarkSeries
{
    private double _baseline;
    private double _headSize = 6;

    /// <summary>Initializes a new instance of the <see cref="StemSeries"/> class.</summary>
    /// <param name="source">The points.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public StemSeries(IDataSource<double, double> source, Axes.AxisBase xAxis, Axes.AxisBase yAxis)
        : base(source, xAxis, yAxis)
    {
    }

    /// <summary>Gets or sets the value the lines start from.</summary>
    public double Baseline
    {
        get => _baseline;
        set
        {
            _baseline = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the diameter of the heads in device-independent units, 0 for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double HeadSize
    {
        get => _headSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _headSize = value;
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var metadata = Source.Metadata;
        x = metadata.XRange;
        y = metadata.YRange.IsEmpty ? metadata.YRange : metadata.YRange.Union(new DoubleRange(_baseline, _baseline));
        return metadata.Count > 0;
    }

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        ClearHeads();
        var snapshot = Source.GetSnapshot();
        var xs = snapshot.X.Span;
        var ys = snapshot.Y.Span;
        var half = (float)(Thickness * frame.DpiScale / 2);
        var radius = (float)(_headSize * frame.DpiScale / 2);
        var start = (float)frame.Y.ToPixel(_baseline);
        for (var i = 0; i < xs.Length; i++)
        {
            var x = MathF.Round((float)frame.X.ToPixel(xs[i]));
            var y = (float)frame.Y.ToPixel(ys[i]);
            if (float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(start))
            {
                AddRectangle(mesh, x - half, MathF.Min(start, y), x + half, MathF.Max(start, y));
                if (radius > 0)
                {
                    AddHead(new Vector2(x, y), radius * 2);
                }
            }
        }
    }
}

/// <summary>Values with their uncertainty: a vertical bar with caps and a point in the middle.</summary>
public sealed class ErrorBarSeries : MarkSeries
{
    private ReadOnlyMemory<double> _lower;
    private ReadOnlyMemory<double> _upper;
    private double _capWidth = 8;
    private double _pointSize = 5;

    /// <summary>Initializes a new instance of the <see cref="ErrorBarSeries"/> class.</summary>
    /// <param name="source">The values.</param>
    /// <param name="lower">How far each bar reaches below its value.</param>
    /// <param name="upper">How far each bar reaches above its value.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public ErrorBarSeries(IDataSource<double, double> source, ReadOnlyMemory<double> lower, ReadOnlyMemory<double> upper, Axes.AxisBase xAxis, Axes.AxisBase yAxis)
        : base(source, xAxis, yAxis)
    {
        _lower = lower;
        _upper = upper;
    }

    /// <summary>Gets or sets the width of the caps in device-independent units, 0 for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double CapWidth
    {
        get => _capWidth;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _capWidth = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the diameter of the points in device-independent units, 0 for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public double PointSize
    {
        get => _pointSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _pointSize = value;
            OnAppearanceChanged();
        }
    }

    /// <summary>Sets new uncertainties, one pair per point.</summary>
    /// <param name="lower">How far each bar reaches below its value.</param>
    /// <param name="upper">How far each bar reaches above its value.</param>
    public void SetErrors(ReadOnlyMemory<double> lower, ReadOnlyMemory<double> upper)
    {
        _lower = lower;
        _upper = upper;
        OnAppearanceChanged();
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var snapshot = Source.GetSnapshot();
        x = Source.Metadata.XRange;
        y = DoubleRange.Empty;
        var ys = snapshot.Y.Span;
        var count = Math.Min(ys.Length, Math.Min(_lower.Length, _upper.Length));
        for (var i = 0; i < count; i++)
        {
            var low = ys[i] - _lower.Span[i];
            var high = ys[i] + _upper.Span[i];
            if (double.IsFinite(low) && double.IsFinite(high))
            {
                y = y.Union(new DoubleRange(low, high));
            }
        }

        return !y.IsEmpty;
    }

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        ClearHeads();
        var snapshot = Source.GetSnapshot();
        var xs = snapshot.X.Span;
        var ys = snapshot.Y.Span;
        var lower = _lower.Span;
        var upper = _upper.Span;
        var half = (float)(Thickness * frame.DpiScale / 2);
        var cap = (float)(_capWidth * frame.DpiScale / 2);
        var radius = (float)(_pointSize * frame.DpiScale / 2);
        var count = Math.Min(xs.Length, Math.Min(lower.Length, upper.Length));
        for (var i = 0; i < count; i++)
        {
            var x = MathF.Round((float)frame.X.ToPixel(xs[i]));
            var y = (float)frame.Y.ToPixel(ys[i]);
            var low = (float)frame.Y.ToPixel(ys[i] - lower[i]);
            var high = (float)frame.Y.ToPixel(ys[i] + upper[i]);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(low) || !float.IsFinite(high))
            {
                continue;
            }

            AddRectangle(mesh, x - half, MathF.Min(low, high), x + half, MathF.Max(low, high));
            if (cap > 0)
            {
                AddRectangle(mesh, x - cap, low - half, x + cap, low + half);
                AddRectangle(mesh, x - cap, high - half, x + cap, high + half);
            }

            if (radius > 0)
            {
                AddHead(new Vector2(x, y), radius * 2);
            }
        }
    }
}
