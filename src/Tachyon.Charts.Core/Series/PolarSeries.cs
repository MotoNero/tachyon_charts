using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// Maps angles and radii to a pane. Zero degrees point up, angles run clockwise.
/// </summary>
/// <param name="CenterX">The center.</param>
/// <param name="CenterY">The center.</param>
/// <param name="Radius">The radius of the outer circle.</param>
/// <param name="Range">The radial values from the center to the outer circle.</param>
public readonly record struct PolarProjection(double CenterX, double CenterY, double Radius, DoubleRange Range)
{
    private const double OuterShare = 0.88;

    /// <summary>Creates the projection of an area.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="range">The radial range.</param>
    /// <returns>The projection, with room for labels around the circle.</returns>
    public static PolarProjection Fit(double x, double y, double width, double height, DoubleRange range) =>
        new(x + (width / 2), y + (height / 2), Math.Min(width, height) / 2 * OuterShare, range);

    /// <summary>Finds a point. Radii outside of the range stay at the center or on the outer circle.</summary>
    /// <param name="angle">The angle in degrees.</param>
    /// <param name="value">The radial value.</param>
    /// <returns>The point.</returns>
    public Vector2 ToPoint(double angle, double value)
    {
        var share = Math.Clamp((value - Range.Min) / (Range.Max - Range.Min), 0, 1);
        var radians = angle * Math.PI / 180;
        return new Vector2((float)(CenterX + (Math.Sin(radians) * share * Radius)), (float)(CenterY - (Math.Cos(radians) * share * Radius)));
    }
}

/// <summary>What a polar series draws.</summary>
[Flags]
public enum PolarParts
{
    /// <summary>The area around the center.</summary>
    Area = 1,

    /// <summary>The line through the points.</summary>
    Line = 2,

    /// <summary>A marker at each point.</summary>
    Markers = 4,
}

/// <summary>
/// Points given by angle and radius, in a chart made by <see cref="PolarChart.Create"/>. The Y axis of
/// the series is the radial axis.
/// </summary>
public sealed class PolarSeries : ChartSeries, IDisposable
{
    private const float MarkerRadius = 3;

    private readonly double[] _angles;
    private readonly double[] _values;
    private readonly List<MeshVertex> _mesh = [];
    private readonly List<Vector2> _points = [];
    private MeshVertex[] _vertices = [];
    private IGpuBuffer? _buffer;
    private long _version;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private DoubleRange _builtRange;
    private PolarParts _parts = PolarParts.Line;
    private bool _isClosed;
    private Rgba8 _color = new(76, 201, 240, 255);
    private SeriesFill _fill = new SolidFill(new Rgba8(76, 201, 240, 80));
    private double _thickness = 1.5;

    /// <summary>Initializes a new instance of the <see cref="PolarSeries"/> class.</summary>
    /// <param name="angles">The angles in degrees.</param>
    /// <param name="values">The radial values.</param>
    /// <param name="xAxis">The horizontal axis of the chart.</param>
    /// <param name="yAxis">The radial axis.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public PolarSeries(double[] angles, double[] values, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(angles);
        ArgumentNullException.ThrowIfNull(values);
        if (angles.Length != values.Length)
        {
            throw new ArgumentException("Every angle needs a value.", nameof(values));
        }

        _angles = angles;
        _values = values;
    }

    /// <summary>Gets or sets what is drawn.</summary>
    public PolarParts Parts
    {
        get => _parts;
        set
        {
            _parts = value;
            Touch();
        }
    }

    /// <summary>Gets or sets a value indicating whether the last point connects to the first.</summary>
    public bool IsClosed
    {
        get => _isClosed;
        set
        {
            _isClosed = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the color of lines and markers.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the fill of the area.</summary>
    public SeriesFill Fill
    {
        get => _fill;
        set
        {
            _fill = value ?? throw new ArgumentNullException(nameof(value));
            Touch();
        }
    }

    /// <summary>Gets or sets the line thickness in device-independent units.</summary>
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
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    protected internal override long Version => Volatile.Read(ref _version);

    /// <summary>Creates a closed polygon with one corner per category, as a radar chart shows it.</summary>
    /// <param name="values">The values, one per category.</param>
    /// <param name="xAxis">The horizontal axis of the chart.</param>
    /// <param name="yAxis">The radial axis.</param>
    /// <returns>The series.</returns>
    public static PolarSeries Radar(double[] values, AxisBase xAxis, AxisBase yAxis)
    {
        ArgumentNullException.ThrowIfNull(values);
        var angles = new double[values.Length];
        for (var i = 0; i < angles.Length; i++)
        {
            angles[i] = i * 360.0 / angles.Length;
        }

        return new PolarSeries(angles, values, xAxis, yAxis) { IsClosed = true, Parts = PolarParts.Area | PolarParts.Line | PolarParts.Markers };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        _builtVersion = -1;
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources() => Dispose();

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = new DoubleRange(0, 360);
        y = new DoubleRange(0, 0);
        foreach (var value in _values)
        {
            if (double.IsFinite(value))
            {
                y = y.Union(new DoubleRange(value, value));
            }
        }

        return _values.Length > 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var range = YAxis.VisibleRange;
        var version = Version;
        if (_builtVersion == version && _builtFrame == frame && _builtRange == range)
        {
            return;
        }

        var projection = PolarProjection.Fit(frame.Bounds.X, frame.Bounds.Y, frame.Bounds.Width, frame.Bounds.Height, range);
        Build(projection);
        _vertices = [.. _mesh];
        if (_points.Count >= 2)
        {
            var size = _points.Count * 8L;
            if (_buffer is null || _buffer.SizeInBytes < size)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(size, BufferUsage.Vertex));
            }

            _buffer.Write<Vector2>(0, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_points));
        }

        _builtVersion = version;
        _builtFrame = frame;
        _builtRange = range;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        commands.FillMesh(_vertices, frame.Bounds);
        if (_buffer is not null && _points.Count >= 2 && (_parts & PolarParts.Line) != 0)
        {
            commands.DrawPolyline(_buffer, 0, _points.Count, new PolylineStyle((float)(_thickness * frame.DpiScale), _color) { Clip = frame.Bounds });
        }
    }

    private void Touch() => Interlocked.Increment(ref _version);

    private void Build(in PolarProjection projection)
    {
        _mesh.Clear();
        _points.Clear();
        var center = new Vector2((float)projection.CenterX, (float)projection.CenterY);
        var previous = default(Vector2);
        var first = default(Vector2);
        var previousValue = 0.0;
        var hasPrevious = false;
        for (var i = 0; i < _angles.Length; i++)
        {
            if (!double.IsFinite(_values[i]) || !double.IsFinite(_angles[i]))
            {
                _points.Add(new Vector2(PolylineStyle.Gap));
                hasPrevious = false;
                continue;
            }

            var point = projection.ToPoint(_angles[i], _values[i]);
            _points.Add(point);
            if (hasPrevious)
            {
                AddWedge(center, previous, point, previousValue, _values[i], projection);
            }
            else if (i == 0)
            {
                first = point;
            }

            if ((_parts & PolarParts.Markers) != 0)
            {
                AddMarker(point);
            }

            previous = point;
            previousValue = _values[i];
            hasPrevious = true;
        }

        if (_isClosed && hasPrevious && _angles.Length > 2 && double.IsFinite(_values[0]))
        {
            _points.Add(first);
            AddWedge(center, previous, first, previousValue, _values[0], projection);
        }
    }

    private void AddWedge(Vector2 center, Vector2 start, Vector2 end, double startValue, double endValue, in PolarProjection projection)
    {
        if ((_parts & PolarParts.Area) == 0)
        {
            return;
        }

        var height = projection.Radius * 2;
        var top = projection.CenterY - projection.Radius;
        _mesh.Add(new MeshVertex(center, _fill.GetColor((center.Y - top) / height, projection.Range.Min)));
        _mesh.Add(new MeshVertex(start, _fill.GetColor((start.Y - top) / height, startValue)));
        _mesh.Add(new MeshVertex(end, _fill.GetColor((end.Y - top) / height, endValue)));
    }

    private void AddMarker(Vector2 point)
    {
        var a = new MeshVertex(point + new Vector2(0, -MarkerRadius), _color);
        var b = new MeshVertex(point + new Vector2(MarkerRadius, 0), _color);
        var c = new MeshVertex(point + new Vector2(0, MarkerRadius), _color);
        var d = new MeshVertex(point + new Vector2(-MarkerRadius, 0), _color);
        _mesh.AddRange([a, b, c, a, c, d]);
    }
}

/// <summary>
/// The circles, spokes and labels of a polar chart.
/// </summary>
/// <param name="radialAxis">The radial axis.</param>
/// <param name="spokes">The labels of the spokes, evenly around the circle.</param>
public sealed class PolarGridOverlay(AxisBase radialAxis, IReadOnlyList<string> spokes) : ChartOverlay
{
    private const int CircleCount = 4;
    private const int CircleSegments = 72;
    private const float LabelSize = 12;
    private const float LabelGap = 6;

    // Between the spokes of the usual layouts, where the labels of the circles stay free.
    private const double LabelAngle = 22.5;

    /// <inheritdoc />
    protected internal override long ExternalVersion => radialAxis.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        var range = radialAxis.VisibleRange;
        var projection = PolarProjection.Fit(pane.X, pane.Y, pane.Width, pane.Height, range);
        var line = canvas.TextColor with { A = 70 };
        for (var circle = 1; circle <= CircleCount; circle++)
        {
            var value = range.Min + ((range.Max - range.Min) * circle / CircleCount);
            var previous = projection.ToPoint(0, value);
            for (var step = 1; step <= CircleSegments; step++)
            {
                var point = projection.ToPoint(step * 360.0 / CircleSegments, value);
                canvas.DrawLine(previous, point, 1, line);
                previous = point;
            }

            canvas.DrawText(value.ToString("G4", System.Globalization.CultureInfo.InvariantCulture), projection.ToPoint(LabelAngle, value).X + 3, projection.ToPoint(LabelAngle, value).Y + 4, LabelSize, canvas.TextColor);
        }

        var center = new Vector2((float)projection.CenterX, (float)projection.CenterY);
        for (var spoke = 0; spoke < spokes.Count; spoke++)
        {
            var angle = spoke * 360.0 / spokes.Count;
            canvas.DrawLine(center, projection.ToPoint(angle, range.Max), 1, line);
            var outside = PolarProjection.Fit(pane.X, pane.Y, pane.Width, pane.Height, range) with { Radius = projection.Radius + LabelGap };
            var tip = outside.ToPoint(angle, range.Max);
            var isRight = angle is > 0 and < 180;
            var isCentered = angle is 0 or 180;
            var width = spokes[spoke].Length * LabelSize * 0.55;
            var x = isRight ? tip.X : tip.X - width;
            if (isCentered)
            {
                x = tip.X - (width / 2);
            }

            canvas.DrawText(spokes[spoke], x, tip.Y + (angle is > 90 and < 270 ? LabelSize : 4), LabelSize, canvas.TextColor);
        }
    }
}

/// <summary>
/// Zooms the radial axis with the wheel: the center stays, the outer circle moves.
/// </summary>
public sealed class RadialZoomModifier : ChartModifier
{
    private readonly AxisBase? _radialAxis;

    /// <summary>Initializes a new instance of the <see cref="RadialZoomModifier"/> class for the Y axis of the first series.</summary>
    public RadialZoomModifier()
        : base(ZoomPanModifier.DefaultPriority)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RadialZoomModifier"/> class.</summary>
    /// <param name="radialAxis">The radial axis.</param>
    public RadialZoomModifier(AxisBase radialAxis)
        : base(ZoomPanModifier.DefaultPriority)
    {
        _radialAxis = radialAxis ?? throw new ArgumentNullException(nameof(radialAxis));
    }

    /// <summary>Gets the radial axis.</summary>
    public AxisBase RadialAxis => _radialAxis ?? Interaction?.Chart.Series[0].YAxis ?? throw new InvalidOperationException("The modifier is not attached.");

    /// <summary>Gets or sets the change of the range per wheel step.</summary>
    public double FactorPerStep { get; set; } = 1.2;

    /// <inheritdoc />
    protected internal override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (Interaction is not { } interaction || input.Delta.Y == 0 || !interaction.TryHitPane(input.Position, out _))
        {
            return ModifierResult.Ignored;
        }

        var factor = Math.Pow(FactorPerStep, -input.Delta.Y);
        var axis = RadialAxis;
        interaction.Dispatch(() =>
        {
            var range = axis.VisibleRange;
            axis.VisibleRange = new DoubleRange(range.Min, range.Min + ((range.Max - range.Min) * factor));
        });
        return ModifierResult.Handled;
    }
}

/// <summary>
/// Creates charts with polar coordinates.
/// </summary>
public static class PolarChart
{
    private static readonly string[] _degrees = ["0°", "45°", "90°", "135°", "180°", "225°", "270°", "315°"];

    /// <summary>Creates a chart whose cartesian axes are hidden.</summary>
    /// <param name="textEngine">The text engine.</param>
    /// <param name="range">The radial range.</param>
    /// <param name="spokes">The labels of the spokes, or <see langword="null"/> for degrees.</param>
    /// <returns>The chart with its axes. The Y axis is the radial axis.</returns>
    public static (ChartSurface Chart, NumericAxis Angle, NumericAxis Radial) Create(ITextEngine textEngine, DoubleRange range, IReadOnlyList<string>? spokes = null)
    {
        var angle = new NumericAxis("angle", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 360), IsHidden = true };
        var radial = new NumericAxis("radial", AxisPosition.Left) { VisibleRange = range, IsHidden = true };
        var chart = new ChartSurface([angle], [new LayoutPane([radial])], textEngine);
        chart.AddOverlay(new PolarGridOverlay(radial, spokes ?? _degrees));
        return (chart, angle, radial);
    }
}
