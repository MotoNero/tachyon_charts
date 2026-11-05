using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>What the labels of a pie show.</summary>
public enum PieLabels
{
    /// <summary>No labels.</summary>
    None,

    /// <summary>The name of each slice.</summary>
    Name,

    /// <summary>The share in percent.</summary>
    Percent,

    /// <summary>The value.</summary>
    Value,

    /// <summary>The name and the share in percent.</summary>
    NameAndPercent,
}

/// <summary>
/// A pie or, with an <see cref="InnerRadius"/>, a donut: one slice per value, clockwise from the top.
/// Values that are zero, negative or not finite get no slice.
/// </summary>
public sealed class PieSeries : ChartSeries
{
    private const int StepsPerTurn = 180;
    private const float RadiusWithLabels = 0.62f;
    private const float RadiusWithoutLabels = 0.92f;
    private const double HoverLift = 0.03;

    private static readonly Rgba8[] _defaultPalette =
    [
        new(76, 201, 240, 255), new(247, 37, 133, 255), new(255, 190, 11, 255), new(114, 9, 183, 255),
        new(58, 134, 255, 255), new(6, 214, 160, 255), new(251, 86, 7, 255), new(131, 56, 236, 255),
    ];

    private readonly List<MeshVertex> _mesh = [];
    private MeshVertex[] _vertices = [];
    private int _count;
    private Slices _slices;
    private Rgba8[] _palette = _defaultPalette;
    private IReadOnlyList<Rgba8>? _colors;
    private double _innerRadius;
    private double _explodeDistance = 0.1;
    private double _gap = 1.5;
    private int _exploded = -1;
    private int _highlighted = -1;
    private PieLabels _labels = PieLabels.NameAndPercent;
    private long _version;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="PieSeries"/> class.</summary>
    /// <param name="values">The values, one per slice.</param>
    /// <param name="names">The names of the slices, or <see langword="null"/> for none.</param>
    /// <param name="xAxis">The horizontal axis of the chart, which the pie does not use.</param>
    /// <param name="yAxis">The vertical axis of the chart, which the pie does not use.</param>
    /// <exception cref="ArgumentException">The number of names differs from the number of values.</exception>
    public PieSeries(IReadOnlyList<double> values, IReadOnlyList<string>? names, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis) => _slices = Slices.Create(values, names);

    /// <summary>Gets the number of values, including those without a slice.</summary>
    public int Count => Volatile.Read(ref _slices).Values.Length;

    /// <summary>Gets or sets the inner radius as a share of the outer one: 0 for a pie, for example 0.55 for a donut.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0 to 0.95.</exception>
    public double InnerRadius
    {
        get => _innerRadius;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0.95);
            _innerRadius = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the slice pulled out of the pie, or -1 for none.</summary>
    public int ExplodedIndex
    {
        get => Volatile.Read(ref _exploded);
        set
        {
            Volatile.Write(ref _exploded, value);
            Touch();
        }
    }

    /// <summary>Gets or sets how far the pulled-out slice moves, as a share of the radius.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or larger than 0.5.</exception>
    public double ExplodeDistance
    {
        get => _explodeDistance;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0.5);
            _explodeDistance = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the slice under the pointer, or -1 for none. <see cref="PieModifier"/> sets it.</summary>
    public int HighlightedIndex
    {
        get => Volatile.Read(ref _highlighted);
        set
        {
            if (Interlocked.Exchange(ref _highlighted, value) != value)
            {
                Touch();
            }
        }
    }

    /// <summary>Gets or sets the gap between the slices in device-independent units.</summary>
    public double Gap
    {
        get => _gap;
        set
        {
            _gap = Math.Max(0, value);
            Touch();
        }
    }

    /// <summary>Gets or sets what the labels show.</summary>
    public PieLabels Labels
    {
        get => _labels;
        set
        {
            _labels = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the colors of the slices, repeated when there are more slices; <see langword="null"/> takes the palette of the theme.</summary>
    public IReadOnlyList<Rgba8>? Colors
    {
        get => _colors;
        set
        {
            _colors = value is { Count: 0 } ? null : value;
            Touch();
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => ColorOf(0);

    /// <inheritdoc />
    protected internal override long Version => Volatile.Read(ref _version);

    /// <summary>Replaces the values, for example for live data. Safe from any thread.</summary>
    /// <param name="values">The values, one per slice.</param>
    /// <param name="names">The names, or <see langword="null"/> to keep the current ones when their number fits.</param>
    /// <exception cref="ArgumentException">The number of names differs from the number of values.</exception>
    public void SetValues(IReadOnlyList<double> values, IReadOnlyList<string>? names = null)
    {
        var current = Volatile.Read(ref _slices);
        Volatile.Write(ref _slices, Slices.Create(values, names ?? (current.Names.Length == values?.Count ? current.Names : null)));
        Touch();
    }

    /// <summary>Gets the value of a slice.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <returns>The value as given.</returns>
    public double GetValue(int index) => Volatile.Read(ref _slices).Values[index];

    /// <summary>Gets the name of a slice.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <returns>The name, or an empty text.</returns>
    public string GetName(int index) => Volatile.Read(ref _slices).Names[index];

    /// <summary>Gets the share of a slice in the sum of all slices.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <returns>The share from 0 to 1; 0 for values without a slice.</returns>
    public double GetShare(int index)
    {
        var slices = Volatile.Read(ref _slices);
        return slices.Total > 0 ? slices.Sweep[index] / (2 * Math.PI) : 0;
    }

    internal Slices Current => Volatile.Read(ref _slices);

    internal (Vector2 Center, float Radius) Fit(double x, double y, double width, double height)
    {
        var share = _labels == PieLabels.None ? RadiusWithoutLabels : RadiusWithLabels;
        return (new Vector2((float)(x + (width / 2)), (float)(y + (height / 2))), (float)(Math.Min(width, height) / 2 * share));
    }

    internal Rgba8 ColorOf(int index)
    {
        if (_colors is { Count: > 0 } colors)
        {
            return colors[index % colors.Count];
        }

        var palette = _palette;
        return palette[index % palette.Length];
    }

    // How far a slice moves out along its middle, as a share of the radius.
    internal double LiftOf(int index)
    {
        if (index == ExplodedIndex)
        {
            return _explodeDistance;
        }

        return index == HighlightedIndex ? HoverLift : 0;
    }

    internal static Vector2 Direction(double angle) => new((float)Math.Sin(angle), (float)-Math.Cos(angle));

    /// <summary>Finds the slice at a point, in the same units as the area.</summary>
    internal int FindSlice(Vector2 point, double x, double y, double width, double height)
    {
        var slices = Current;
        if (slices.Total <= 0)
        {
            return -1;
        }

        var (center, radius) = Fit(x, y, width, height);
        for (var i = 0; i < slices.Values.Length; i++)
        {
            if (slices.Sweep[i] <= 0)
            {
                continue;
            }

            var middle = slices.Start[i] + (slices.Sweep[i] / 2);
            var shifted = point - (Direction(middle) * (float)(LiftOf(i) * radius));
            var offset = shifted - center;
            var distance = offset.Length();
            if (distance > radius || distance < radius * _innerRadius)
            {
                continue;
            }

            var angle = Math.Atan2(offset.X, -offset.Y);
            angle = angle < 0 ? angle + (2 * Math.PI) : angle;
            if (angle >= slices.Start[i] && angle < slices.Start[i] + slices.Sweep[i])
            {
                return i;
            }
        }

        return -1;
    }

    // A pie has no X axis to turn; it starts at the top in every chart.
    internal override bool TurnsWithChart => false;

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette)
    {
        _palette = palette.ToArray();
        Touch();
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = new DoubleRange(0, 1);
        y = new DoubleRange(0, 1);
        return true;
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        var bounds = frame.Bounds;
        var found = FindSlice(pixel, bounds.X, bounds.Y, bounds.Width, bounds.Height);
        index = found;
        x = found;
        y = found >= 0 ? GetValue(found) : double.NaN;
        return found >= 0;
    }

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y)
    {
        var slices = Current;
        if (index < 0 || index >= slices.Values.Length)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = index;
        y = slices.Values[index];
        return true;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (_builtVersion == version && _builtFrame == frame)
        {
            return;
        }

        Build(frame);

        // The array only grows, so a slice under the pointer does not allocate.
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
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) => commands.FillMesh(_vertices.AsSpan(0, _count), frame.Bounds);

    private static Rgba8 Lighten(Rgba8 color) => new(
        (byte)(color.R + ((255 - color.R) / 4)),
        (byte)(color.G + ((255 - color.G) / 4)),
        (byte)(color.B + ((255 - color.B) / 4)),
        color.A);

    private void Touch() => Interlocked.Increment(ref _version);

    private void Build(in SeriesFrame frame)
    {
        _mesh.Clear();
        var slices = Current;
        if (slices.Total <= 0)
        {
            return;
        }

        var bounds = frame.Bounds;
        var (center, radius) = Fit(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        var inner = (float)(radius * _innerRadius);
        var drawn = 0;
        foreach (var sweep in slices.Sweep)
        {
            drawn += sweep > 0 ? 1 : 0;
        }

        // A single slice is a closed circle; otherwise a gap on both sides keeps the slices apart. The soft band along
        // each cut reaches into the gap, so the gap grows by the band on both sides.
        var gap = drawn > 1 ? ((_gap * frame.DpiScale) + (2 * MeshFringe.Width)) / radius : 0;
        for (var i = 0; i < slices.Values.Length; i++)
        {
            var sweep = slices.Sweep[i];
            if (sweep <= 0)
            {
                continue;
            }

            var shrink = Math.Min(gap, sweep * 0.5);
            var start = slices.Start[i] + (shrink / 2);
            var end = slices.Start[i] + sweep - (shrink / 2);
            var middle = (start + end) / 2;
            var origin = center + (Direction(middle) * (float)(LiftOf(i) * radius));
            var color = i == HighlightedIndex ? Lighten(ColorOf(i)) : ColorOf(i);
            AddSlice(origin, radius, inner, start, end, color);
        }
    }

    // The arcs and the cuts get a soft band of one pixel, so that they show no steps.
    private void AddSlice(Vector2 origin, float radius, float inner, double start, double end, Rgba8 color)
    {
        var steps = Math.Max(1, (int)Math.Ceiling((end - start) / (2 * Math.PI) * StepsPerTurn));
        var center = new MeshVertex(origin, color);
        for (var step = 0; step < steps; step++)
        {
            var a = Direction(start + ((end - start) * step / steps));
            var b = Direction(start + ((end - start) * (step + 1) / steps));
            var outerA = new MeshVertex(origin + (a * radius), color);
            var outerB = new MeshVertex(origin + (b * radius), color);
            MeshFringe.Add(_mesh, outerA, outerB, origin);
            if (inner <= 0)
            {
                _mesh.Add(center);
                _mesh.Add(outerA);
                _mesh.Add(outerB);
                continue;
            }

            var innerA = new MeshVertex(origin + (a * inner), color);
            var innerB = new MeshVertex(origin + (b * inner), color);
            _mesh.AddRange([innerA, outerA, outerB, innerA, outerB, innerB]);
            MeshFringe.Add(_mesh, innerA, innerB, outerA.Position);
        }

        if (end - start >= (2 * Math.PI) - 1e-9)
        {
            return;
        }

        // The straight cuts at both ends, seen from a point inside the slice.
        var middle = origin + (Direction((start + end) / 2) * ((radius + inner) / 2));
        foreach (var angle in (ReadOnlySpan<double>)[start, end])
        {
            var direction = Direction(angle);
            MeshFringe.Add(_mesh, new MeshVertex(origin + (direction * inner), color), new MeshVertex(origin + (direction * radius), color), middle);
        }
    }

    /// <summary>The values as angles; replaced as a whole, so readers on other threads see one state.</summary>
    internal sealed record Slices(double[] Values, string[] Names, double Total, double[] Start, double[] Sweep)
    {
        public static Slices Create(IReadOnlyList<double> values, IReadOnlyList<string>? names)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (names is not null && names.Count != values.Count)
            {
                throw new ArgumentException("Every value needs a name.", nameof(names));
            }

            var copy = values.ToArray();
            var total = 0.0;
            foreach (var value in copy)
            {
                total += Counts(value) ? value : 0;
            }

            var start = new double[copy.Length];
            var sweep = new double[copy.Length];
            var angle = 0.0;
            for (var i = 0; i < copy.Length; i++)
            {
                start[i] = angle;
                sweep[i] = total > 0 && Counts(copy[i]) ? copy[i] / total * 2 * Math.PI : 0;
                angle += sweep[i];
            }

            return new Slices(copy, names?.ToArray() ?? Enumerable.Repeat(string.Empty, copy.Length).ToArray(), double.IsFinite(total) ? total : 0, start, sweep);
        }

        private static bool Counts(double value) => double.IsFinite(value) && value > 0;
    }
}

/// <summary>
/// The labels of a pie: inside large slices, outside small ones with a line to their slice. Outside labels on each
/// side are moved apart so that they never overlap. The slice under the pointer shows its name, value and share.
/// </summary>
/// <param name="series">The pie.</param>
public sealed class PieLabelOverlay(PieSeries series) : ChartOverlay
{
    private const float LabelSize = 12;
    private const double InsideSweep = 0.5;
    private const double Elbow = 1.12;
    private const double LabelGap = 6;

    private readonly PieSeries _series = series ?? throw new ArgumentNullException(nameof(series));
    private readonly List<Outside> _left = [];
    private readonly List<Outside> _right = [];

    /// <inheritdoc />
    protected internal override long ExternalVersion => _series.Version + ChartText.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        var slices = _series.Current;
        if (slices.Total <= 0)
        {
            return;
        }

        var (center, radius) = _series.Fit(pane.X, pane.Y, pane.Width, pane.Height);
        var line = canvas.GetLineHeight(LabelSize);
        _left.Clear();
        _right.Clear();
        if (_series.Labels != PieLabels.None)
        {
            for (var i = 0; i < slices.Values.Length; i++)
            {
                if (slices.Sweep[i] > 0)
                {
                    PlaceLabel(canvas, slices, i, center, radius, line);
                }
            }

            DrawOutside(canvas, _left, pane, line, isLeft: true);
            DrawOutside(canvas, _right, pane, line, isLeft: false);
        }

        DrawHover(canvas, slices, center, radius);
    }

    private static Rgba8 ContrastTo(Rgba8 color) => (0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B) > 150
        ? new Rgba8(20, 20, 20, 255)
        : new Rgba8(255, 255, 255, 255);

    private static string Percent(double share) =>
        (share * 100).ToString(share < 0.1 ? "0.#" : "0", ChartText.Culture) + " %";

    private string TextOf(PieSeries.Slices slices, int index)
    {
        var share = slices.Sweep[index] / (2 * Math.PI);
        return _series.Labels switch
        {
            PieLabels.Name => slices.Names[index],
            PieLabels.Percent => Percent(share),
            PieLabels.Value => slices.Values[index].ToString("G4", ChartText.Culture),
            _ => slices.Names[index].Length == 0 ? Percent(share) : slices.Names[index] + " " + Percent(share),
        };
    }

    private void PlaceLabel(OverlayCanvas canvas, PieSeries.Slices slices, int index, Vector2 center, float radius, float line)
    {
        var text = TextOf(slices, index);
        if (text.Length == 0)
        {
            return;
        }

        var middle = slices.Start[index] + (slices.Sweep[index] / 2);
        var direction = PieSeries.Direction(middle);
        var origin = center + (direction * (float)(_series.LiftOf(index) * radius));
        var width = canvas.MeasureWidth(text, LabelSize);
        var ring = radius * (1 - _series.InnerRadius);

        // Inside when the slice is wide enough: at its middle for a pie, in the middle of the ring for a donut.
        var at = _series.InnerRadius > 0 ? radius * (1 - (ring / radius / 2)) : radius * 0.62f;
        var room = 2 * at * Math.Sin(Math.Min(slices.Sweep[index], Math.PI) / 2);
        if (slices.Sweep[index] >= InsideSweep && width <= room * 0.9 && width <= ring * 1.8 && line <= ring * 0.9)
        {
            var place = origin + (direction * (float)at);
            canvas.DrawText(text, Math.Round(place.X - (width / 2)), Math.Round(place.Y + (line * 0.35)), LabelSize, ContrastTo(_series.ColorOf(index)));
            return;
        }

        var anchor = origin + (direction * radius);
        var elbow = origin + (direction * (float)(radius * Elbow));
        var outside = new Outside(text, width, anchor, elbow.Y, elbow.Y);
        (direction.X < 0 ? _left : _right).Add(outside);
    }

    // Spreads the labels of one side top to bottom with at least one line between them, then draws them with their lines.
    private void DrawOutside(OverlayCanvas canvas, List<Outside> labels, DipRect pane, float line, bool isLeft)
    {
        if (labels.Count == 0)
        {
            return;
        }

        labels.Sort(static (a, b) => a.Wanted.CompareTo(b.Wanted));
        var places = new double[labels.Count];
        for (var i = 0; i < places.Length; i++)
        {
            places[i] = labels[i].Wanted;
        }

        Spread(places, pane.Y + line, pane.Bottom - (line * 0.3), line);
        for (var i = 0; i < places.Length; i++)
        {
            labels[i] = labels[i] with { Y = places[i] };
        }

        var (center, radius) = _series.Fit(pane.X, pane.Y, pane.Width, pane.Height);
        var edge = isLeft ? center.X - (radius * Elbow) - LabelGap : center.X + (radius * Elbow) + LabelGap;
        var color = canvas.TextColor with { A = 150 };
        foreach (var label in labels)
        {
            var bend = new Vector2(isLeft ? Math.Min(label.Anchor.X, (float)edge + (float)LabelGap) : Math.Max(label.Anchor.X, (float)edge - (float)LabelGap), (float)label.Y);
            var end = new Vector2((float)edge, (float)label.Y);
            canvas.DrawLine(label.Anchor, bend, 1, color);
            canvas.DrawLine(bend, end, 1, color);
            var x = isLeft ? edge - 4 - label.Width : edge + 4;
            canvas.DrawText(label.Text, Math.Round(x), Math.Round(label.Y + (line * 0.35)), LabelSize, canvas.TextColor);
        }
    }

    /// <summary>Moves sorted places apart to at least one line, first downwards, then back up from the bottom.</summary>
    internal static void Spread(Span<double> places, double top, double bottom, double line)
    {
        for (var i = 0; i < places.Length; i++)
        {
            places[i] = Math.Max(places[i], i == 0 ? top : places[i - 1] + line);
        }

        for (var i = places.Length - 1; i >= 0; i--)
        {
            places[i] = Math.Min(places[i], i == places.Length - 1 ? bottom : places[i + 1] - line);
        }
    }

    private void DrawHover(OverlayCanvas canvas, PieSeries.Slices slices, Vector2 center, float radius)
    {
        var index = _series.HighlightedIndex;
        if (index < 0 || index >= slices.Values.Length || slices.Sweep[index] <= 0)
        {
            return;
        }

        var name = slices.Names[index];
        var text = (name.Length > 0 ? name + ": " : string.Empty)
            + slices.Values[index].ToString("G6", ChartText.Culture) + " (" + Percent(slices.Sweep[index] / (2 * Math.PI)) + ")";
        var middle = slices.Start[index] + (slices.Sweep[index] / 2);
        var place = center + (PieSeries.Direction(middle) * radius * 0.5f);
        canvas.DrawLabel(text, place.X - (canvas.MeasureWidth(text, canvas.LabelSize) / 2), place.Y, canvas.BackgroundColor with { A = 235 }, canvas.TextColor);
    }

    private readonly record struct Outside(string Text, float Width, Vector2 Anchor, double Wanted, double Y);
}

/// <summary>Highlights the slice under the pointer; a click pulls a slice out, a second click puts it back.</summary>
public sealed class PieModifier : ChartModifier
{
    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart is not { CurrentLayout: { } layout } chart || chart.Series.OfType<PieSeries>().FirstOrDefault() is not { } pie)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled)
        {
            pie.HighlightedIndex = -1;
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        var index = pie.FindSlice(input.Position, pane.X, pane.Y, pane.Width, pane.Height);
        if (input.Kind == PointerEventKind.Moved)
        {
            pie.HighlightedIndex = index;
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary && index >= 0)
        {
            pie.ExplodedIndex = pie.ExplodedIndex == index ? -1 : index;
            return ModifierResult.Handled;
        }

        return ModifierResult.Ignored;
    }
}

/// <summary>Describes pie and donut charts.</summary>
public static class PieCharts
{
    /// <summary>Describes a pie: hidden axes, the slices, their labels and the operation with the mouse.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="values">The values, one per slice.</param>
    /// <param name="names">The names of the slices.</param>
    /// <param name="configure">Sets further properties, for example <see cref="PieSeries.InnerRadius"/> for a donut.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Pie(this ChartBuilder builder, IReadOnlyList<double> values, IReadOnlyList<string>? names = null, Action<PieSeries>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .XAxis(axis => axis.Numeric("x").Range(0, 1).Hidden())
            .YAxis(axis => axis.Numeric("y").Range(0, 1).Hidden())
            .AddSeries((x, y) =>
            {
                var pie = new PieSeries(values, names, x, y);
                configure?.Invoke(pie);
                return pie;
            })
            .AddOverlay(chart => new PieLabelOverlay(chart.Series.OfType<PieSeries>().First()))
            .Modifiers(m => m.Custom(() => new PieModifier()));
    }
}
