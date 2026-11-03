using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>The calculation behind a Smith chart.</summary>
public static class SmithMath
{
    /// <summary>Calculates the reflection of a normalized impedance.</summary>
    /// <param name="impedance">The impedance divided by the reference.</param>
    /// <returns>The reflection coefficient, or 1 for an open circuit.</returns>
    public static Complex ToReflection(Complex impedance)
    {
        var divisor = impedance + 1;
        return double.IsInfinity(impedance.Real) || double.IsInfinity(impedance.Imaginary) || divisor == Complex.Zero
            ? Complex.One
            : (impedance - 1) / divisor;
    }

    /// <summary>Calculates the normalized impedance of a reflection.</summary>
    /// <param name="reflection">The reflection coefficient.</param>
    /// <returns>The impedance divided by the reference, infinite for an open circuit.</returns>
    public static Complex ToImpedance(Complex reflection)
    {
        var divisor = 1 - reflection;
        return divisor == Complex.Zero ? new Complex(double.PositiveInfinity, 0) : (1 + reflection) / divisor;
    }

    /// <summary>Calculates the standing wave ratio.</summary>
    /// <param name="reflection">The reflection coefficient.</param>
    /// <returns>The ratio, infinite for full reflection.</returns>
    public static double ToStandingWaveRatio(Complex reflection)
    {
        var size = reflection.Magnitude;
        return size >= 1 ? double.PositiveInfinity : (1 + size) / (1 - size);
    }
}

/// <summary>Places reflection coefficients in an area.</summary>
/// <param name="CenterX">The center.</param>
/// <param name="CenterY">The center.</param>
/// <param name="Radius">The radius of the unit circle.</param>
public readonly record struct SmithProjection(double CenterX, double CenterY, double Radius)
{
    private const double OuterShare = 0.9;

    /// <summary>Creates the projection of an area.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The projection, with room for labels around the circle.</returns>
    public static SmithProjection Fit(double x, double y, double width, double height) =>
        new(x + (width / 2), y + (height / 2), Math.Min(width, height) / 2 * OuterShare);

    /// <summary>Places a reflection coefficient.</summary>
    /// <param name="reflection">The reflection coefficient.</param>
    /// <returns>The position.</returns>
    public Vector2 ToPoint(Complex reflection) =>
        new((float)(CenterX + (reflection.Real * Radius)), (float)(CenterY - (reflection.Imaginary * Radius)));
}

/// <summary>A measured impedance curve in a Smith chart.</summary>
public sealed class SmithSeries : ChartSeries, IDisposable
{
    private readonly List<Vector2> _points = [];
    private readonly double[] _resistance;
    private readonly double[] _reactance;
    private IGpuBuffer? _buffer;
    private Rgba8 _color = new(240, 200, 60, 255);
    private double _thickness = 1.5;
    private long _version;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="SmithSeries"/> class.</summary>
    /// <param name="resistance">The real part of each impedance in ohms.</param>
    /// <param name="reactance">The imaginary part of each impedance in ohms.</param>
    /// <param name="xAxis">The hidden horizontal axis.</param>
    /// <param name="yAxis">The hidden vertical axis.</param>
    /// <param name="referenceImpedance">The reference in ohms.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public SmithSeries(double[] resistance, double[] reactance, AxisBase xAxis, AxisBase yAxis, double referenceImpedance = SmithChart.DefaultReference)
        : base(xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(resistance);
        ArgumentNullException.ThrowIfNull(reactance);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(referenceImpedance);
        if (resistance.Length != reactance.Length)
        {
            throw new ArgumentException("Each resistance needs a reactance.", nameof(reactance));
        }

        _resistance = resistance;
        _reactance = reactance;
        ReferenceImpedance = referenceImpedance;
    }

    /// <summary>Gets the reference in ohms.</summary>
    public double ReferenceImpedance { get; }

    /// <summary>Gets the number of points.</summary>
    public int Count => _resistance.Length;

    /// <summary>Gets or sets the color.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>Gets or sets the thickness in device-independent units.</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _thickness = value;
            Interlocked.Increment(ref _version);
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette) => Color = palette[0];

    /// <inheritdoc />
    protected internal override long Version => Volatile.Read(ref _version);

    /// <summary>Reads the impedance of a point.</summary>
    /// <param name="index">The point.</param>
    /// <returns>The impedance in ohms.</returns>
    public Complex GetImpedance(int index) => new(_resistance[index], _reactance[index]);

    /// <summary>Reads the reflection coefficient of a point.</summary>
    /// <param name="index">The point.</param>
    /// <returns>The reflection coefficient.</returns>
    public Complex GetReflection(int index) => SmithMath.ToReflection(GetImpedance(index) / ReferenceImpedance);

    /// <summary>Announces changed values in the arrays.</summary>
    public void Invalidate() => Interlocked.Increment(ref _version);

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
        x = new DoubleRange(-1, 1);
        y = new DoubleRange(-1, 1);
        return false;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(device);
        var version = Version;
        if (_builtVersion == version && _builtFrame == frame)
        {
            return;
        }

        var projection = SmithProjection.Fit(frame.Bounds.X, frame.Bounds.Y, frame.Bounds.Width, frame.Bounds.Height);
        _points.Clear();
        for (var i = 0; i < _resistance.Length; i++)
        {
            var isValid = double.IsFinite(_resistance[i]) && double.IsFinite(_reactance[i]) && _resistance[i] >= 0;
            _points.Add(isValid ? projection.ToPoint(GetReflection(i)) : new Vector2(PolylineStyle.Gap, 0));
        }

        if (_points.Count >= 2)
        {
            var size = _points.Count * 8L;
            if (_buffer is null || _buffer.SizeInBytes < size)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(size, BufferUsage.Vertex));
            }

            _buffer.Write<Vector2>(0, CollectionsMarshal.AsSpan(_points));
        }

        _builtVersion = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (_buffer is not null && _points.Count >= 2)
        {
            commands.DrawPolyline(_buffer, 0, _points.Count, new PolylineStyle((float)(_thickness * frame.DpiScale), _color) { Clip = frame.Bounds });
        }
    }
}

/// <summary>The circles and arcs of a Smith chart, with values relative to the reference or in ohms.</summary>
/// <param name="referenceImpedance">The reference in ohms.</param>
public sealed class SmithGridOverlay(double referenceImpedance) : ChartOverlay
{
    private const int Segments = 96;
    private const float LabelSize = 11;
    private const double Limit = 1.56;

    private static readonly double[] _values = [0.2, 0.5, 1, 2, 5];
    private bool _showsOhms;

    /// <summary>Gets or sets a value indicating whether the labels show ohms instead of ratios.</summary>
    public bool ShowsOhms
    {
        get => _showsOhms;
        set
        {
            _showsOhms = value;
            OnChanged();
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        var projection = SmithProjection.Fit(pane.X, pane.Y, pane.Width, pane.Height);
        var line = canvas.TextColor with { A = 70 };
        DrawCurve(canvas, projection, t => new Complex(0, Math.Tan(t)), line);
        canvas.DrawLine(projection.ToPoint(new Complex(-1, 0)), projection.ToPoint(new Complex(1, 0)), 1, line);
        foreach (var value in _values)
        {
            DrawCurve(canvas, projection, t => new Complex(value, Math.Tan(t)), line);
            DrawArc(canvas, projection, value, line);
            DrawArc(canvas, projection, -value, line);
            DrawLabels(canvas, projection, value);
        }
    }

    private static void DrawCurve(OverlayCanvas canvas, in SmithProjection projection, Func<double, Complex> curve, Rgba8 color)
    {
        var previous = projection.ToPoint(SmithMath.ToReflection(curve(-Limit)));
        for (var i = 1; i <= Segments; i++)
        {
            var point = projection.ToPoint(SmithMath.ToReflection(curve(-Limit + (2 * Limit * i / Segments))));
            canvas.DrawLine(previous, point, 1, color);
            previous = point;
        }
    }

    private static void DrawArc(OverlayCanvas canvas, in SmithProjection projection, double reactance, Rgba8 color)
    {
        var previous = projection.ToPoint(SmithMath.ToReflection(new Complex(0, reactance)));
        for (var i = 1; i <= Segments; i++)
        {
            var resistance = Math.Tan(Limit * i / Segments);
            var point = projection.ToPoint(SmithMath.ToReflection(new Complex(resistance, reactance)));
            canvas.DrawLine(previous, point, 1, color);
            previous = point;
        }
    }

    private void DrawLabels(OverlayCanvas canvas, in SmithProjection projection, double value)
    {
        var text = (_showsOhms ? value * referenceImpedance : value).ToString("G3", CultureInfo.InvariantCulture);
        var onAxis = projection.ToPoint(SmithMath.ToReflection(new Complex(value, 0)));
        canvas.DrawText(text, onAxis.X + 3, onAxis.Y - 3, LabelSize, canvas.TextColor);
        var tip = projection.ToPoint(SmithMath.ToReflection(new Complex(0, value)));
        var center = new Vector2((float)projection.CenterX, (float)projection.CenterY);
        var outward = center + ((tip - center) * 1.07f);
        canvas.DrawText("+j" + text, outward.X - 12, outward.Y + 4, LabelSize, canvas.TextColor);
        var mirrored = center + (new Vector2(tip.X - center.X, center.Y - tip.Y) * 1.07f);
        canvas.DrawText("-j" + text, mirrored.X - 12, mirrored.Y + 4, LabelSize, canvas.TextColor);
    }
}

/// <summary>Marks a point of a curve and shows its impedance, reflection and standing wave ratio.</summary>
/// <param name="series">The curve.</param>
/// <param name="index">The point.</param>
public sealed class SmithMarkerOverlay(SmithSeries series, int index) : ChartOverlay
{
    private const float LabelSize = 12;
    private const float Size = 9;

    private static readonly Rgba8 _markerColor = new(255, 160, 50, 255);

    private int _index = index;

    /// <summary>Gets or sets the point.</summary>
    public int Index
    {
        get => _index;
        set
        {
            _index = value;
            OnChanged();
        }
    }

    /// <summary>Puts a point into words.</summary>
    /// <param name="impedance">The impedance in ohms.</param>
    /// <param name="reflection">The reflection coefficient.</param>
    /// <returns>The lines of the label.</returns>
    public static string[] Describe(Complex impedance, Complex reflection)
    {
        var sign = impedance.Imaginary < 0 ? '-' : '+';
        var ratio = SmithMath.ToStandingWaveRatio(reflection);
        return
        [
            string.Create(CultureInfo.InvariantCulture, $"Z = {impedance.Real:F2} {sign} j{Math.Abs(impedance.Imaginary):F2} Ω"),
            string.Create(CultureInfo.InvariantCulture, $"Γ = {reflection.Magnitude:F3} at {reflection.Phase * 180 / Math.PI:F1}°"),
            string.Create(CultureInfo.InvariantCulture, $"VSWR = {ratio:F2}"),
        ];
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (_index < 0 || _index >= series.Count)
        {
            return;
        }

        var pane = canvas.Layout.Panes[0];
        var projection = SmithProjection.Fit(pane.X, pane.Y, pane.Width, pane.Height);
        var reflection = series.GetReflection(_index);
        var point = projection.ToPoint(reflection);
        var left = point - new Vector2(Size, 0);
        var right = point + new Vector2(Size, 0);
        canvas.FillTriangle(left, point - new Vector2(0, Size), right, _markerColor);
        canvas.FillTriangle(left, point + new Vector2(0, Size), right, _markerColor);
        var lines = Describe(series.GetImpedance(_index), reflection);
        var height = canvas.GetLineHeight(LabelSize);
        var baseline = pane.Y + height;
        foreach (var line in lines)
        {
            canvas.DrawText(line, pane.X + 6, baseline, LabelSize, canvas.TextColor);
            baseline += height;
        }
    }
}

/// <summary>Creates Smith charts.</summary>
public static class SmithChart
{
    /// <summary>The usual reference in ohms.</summary>
    public const double DefaultReference = 50;

    /// <summary>Describes a Smith chart, for WPF, Avalonia and charts without a UI framework.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="resistance">The real part of each impedance in ohms.</param>
    /// <param name="reactance">The imaginary part of each impedance in ohms.</param>
    /// <param name="markerIndex">The point to mark, or a negative value for none.</param>
    /// <param name="referenceImpedance">The reference in ohms.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Smith(this ChartBuilder builder, double[] resistance, double[] reactance, int markerIndex = -1, double referenceImpedance = DefaultReference)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder
            .XAxis(a => a.Numeric("x").Range(-1, 1).Hidden())
            .YAxis(a => a.Numeric("y").Range(-1, 1).Hidden())
            .AddSeries((x, y) => new SmithSeries(resistance, reactance, x, y, referenceImpedance))
            .AddOverlay(_ => new SmithGridOverlay(referenceImpedance));
        if (markerIndex >= 0)
        {
            builder
                .AddOverlay(chart => new SmithMarkerOverlay(chart.Series.OfType<SmithSeries>().First(), markerIndex))
                .Modifiers(m => m.Custom(() => new Instruments.SmithMarkerModifier()));
        }

        return builder;
    }

    /// <summary>Creates a chart whose cartesian axes are hidden.</summary>
    /// <param name="textEngine">The text engine.</param>
    /// <param name="referenceImpedance">The reference in ohms.</param>
    /// <returns>The chart with its axes and grid.</returns>
    public static (ChartSurface Chart, NumericAxis X, NumericAxis Y, SmithGridOverlay Grid) Create(ITextEngine textEngine, double referenceImpedance = DefaultReference)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(-1, 1), IsHidden = true };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(-1, 1), IsHidden = true };
        var chart = new ChartSurface([x], [new LayoutPane([y])], textEngine);
        var grid = new SmithGridOverlay(referenceImpedance);
        chart.AddOverlay(grid);
        return (chart, x, y, grid);
    }
}
