using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Annotations;

/// <summary>The drawing tools of a price chart.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<DrawingKind>))]
public enum DrawingKind
{
    /// <summary>A line through two points.</summary>
    TrendLine,

    /// <summary>The levels 0 to 100 % between a high and a low at the Fibonacci ratios.</summary>
    FibonacciRetracement,

    /// <summary>A move A to B projected from C at the Fibonacci ratios.</summary>
    FibonacciExtension,

    /// <summary>A trend line and a parallel line through a third point.</summary>
    TrendChannel,

    /// <summary>A median line from a pivot to the middle of two points, with parallels through them.</summary>
    Pitchfork,

    /// <summary>Six points of an impulse, labelled 0 to 5.</summary>
    ElliottWave,

    /// <summary>The five points X, A, B, C, D of a harmonic pattern with their ratios.</summary>
    Xabcd,

    /// <summary>Seven points of a head and shoulders pattern with its neckline.</summary>
    HeadAndShoulders,

    /// <summary>A free line along the pointer.</summary>
    Brush,
}

/// <summary>A point of a drawing in values of the axes. On a time axis, X is the absolute time, not the offset from the axis origin.</summary>
/// <param name="X">The value on the horizontal axis.</param>
/// <param name="Y">The value on the vertical axis.</param>
public readonly record struct DrawingPoint(double X, double Y);

/// <summary>A drawing on a chart, fixed to values of the axes so that it moves with zooming and panning.</summary>
public sealed class Drawing
{
    private DrawingPoint[] _points;

    /// <summary>Initializes a new instance of the <see cref="Drawing"/> class.</summary>
    /// <param name="kind">The tool.</param>
    /// <param name="points">The points; a brush takes two or more, the others the number <see cref="PointCount"/> gives.</param>
    /// <exception cref="ArgumentException">The number of points does not fit the tool, or a point is not finite.</exception>
    public Drawing(DrawingKind kind, IEnumerable<DrawingPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        Kind = kind;
        var copy = points.ToArray();
        var needed = PointCount(kind);
        if ((needed > 0 && copy.Length != needed) || copy.Length < 2)
        {
            throw new ArgumentException($"A {kind} needs {(needed > 0 ? needed.ToString(CultureInfo.InvariantCulture) : "two or more")} points, not {copy.Length}.", nameof(points));
        }

        _points = Check(copy);
    }

    /// <summary>Gets the tool.</summary>
    public DrawingKind Kind { get; }

    /// <summary>Gets the points. Safe from any thread: a change replaces the whole list.</summary>
    public IReadOnlyList<DrawingPoint> Points => Volatile.Read(ref _points);

    /// <summary>Gets or sets the color.</summary>
    public Rgba8 Color { get; set; } = new(255, 190, 11, 255);

    /// <summary>Gets or sets the line thickness in device-independent units.</summary>
    public float Thickness { get; set; } = 1.5f;

    /// <summary>Gets how many points a tool takes, or 0 for any number from two, as for a brush.</summary>
    /// <param name="kind">The tool.</param>
    /// <returns>The number of points.</returns>
    public static int PointCount(DrawingKind kind) => kind switch
    {
        DrawingKind.TrendLine or DrawingKind.FibonacciRetracement => 2,
        DrawingKind.FibonacciExtension or DrawingKind.TrendChannel or DrawingKind.Pitchfork => 3,
        DrawingKind.ElliottWave => 6,
        DrawingKind.Xabcd => 5,
        DrawingKind.HeadAndShoulders => 7,
        _ => 0,
    };

    internal DrawingPoint[] Snapshot => Volatile.Read(ref _points);

    internal void SetPoints(DrawingPoint[] points) => Volatile.Write(ref _points, Check(points));

    private static DrawingPoint[] Check(DrawingPoint[] points)
    {
        foreach (var point in points)
        {
            if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            {
                throw new ArgumentException("The points of a drawing must be finite.", nameof(points));
            }
        }

        return points;
    }
}

/// <summary>
/// Holds the drawings of a chart, draws them and saves them as JSON. With a <see cref="Tool"/> set,
/// <see cref="DrawingModifier"/> creates a drawing from clicks; without one, it moves drawings and their points.
/// </summary>
public sealed class DrawingLayer : ChartOverlay
{
    private const float HandleSize = 4;
    private const float LabelSize = 11;

    private readonly AxisBase _xAxis;
    private readonly AxisBase _yAxis;
    private readonly Lock _gate = new();
    private readonly List<DrawingShapes.Segment> _segments = [];
    private readonly List<DrawingShapes.Label> _labels = [];
    private readonly List<DrawingShapes.Band> _bands = [];
    private Drawing[] _drawings = [];
    private Drawing? _selected;
    private DrawingKind? _tool;
    private DrawingPoint[] _draft = [];
    private DrawingPoint? _preview;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="DrawingLayer"/> class.</summary>
    /// <param name="xAxis">The horizontal axis the points refer to.</param>
    /// <param name="yAxis">The vertical axis the points refer to.</param>
    public DrawingLayer(AxisBase xAxis, AxisBase yAxis)
    {
        _xAxis = xAxis ?? throw new ArgumentNullException(nameof(xAxis));
        _yAxis = yAxis ?? throw new ArgumentNullException(nameof(yAxis));
    }

    /// <summary>Occurs when a drawing was finished with the mouse.</summary>
    public event Action<Drawing>? Completed;

    /// <summary>Gets the drawings, the last on top.</summary>
    public IReadOnlyList<Drawing> Drawings => Volatile.Read(ref _drawings);

    /// <summary>Gets or sets the tool for the next drawing, or <see langword="null"/> to edit the existing ones.</summary>
    public DrawingKind? Tool
    {
        get => _tool;
        set
        {
            _tool = value;
            Volatile.Write(ref _draft, []);
            _preview = null;
            Touch();
        }
    }

    /// <summary>Gets or sets the drawing that shows its points and that Delete removes.</summary>
    public Drawing? Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the color of new drawings.</summary>
    public Rgba8 Color { get; set; } = new(255, 190, 11, 255);

    /// <summary>Gets the horizontal axis.</summary>
    public AxisBase XAxis => _xAxis;

    /// <summary>Gets the vertical axis.</summary>
    public AxisBase YAxis => _yAxis;

    /// <inheritdoc />
    protected internal override long ExternalVersion => Interlocked.Read(ref _version) + ChartText.Version;

    internal DrawingPoint[] Draft => Volatile.Read(ref _draft);

    /// <summary>Adds a drawing on top. This does not record a step for undo; <see cref="DrawingModifier"/> does.</summary>
    /// <param name="drawing">The drawing.</param>
    public void Add(Drawing drawing)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        lock (_gate)
        {
            Volatile.Write(ref _drawings, [.. _drawings, drawing]);
        }

        Touch();
    }

    /// <summary>Removes a drawing.</summary>
    /// <param name="drawing">The drawing.</param>
    /// <returns>Whether it was there.</returns>
    public bool Remove(Drawing drawing)
    {
        bool removed;
        lock (_gate)
        {
            removed = Array.IndexOf(_drawings, drawing) >= 0;
            Volatile.Write(ref _drawings, [.. _drawings.Where(item => item != drawing)]);
        }

        if (_selected == drawing)
        {
            _selected = null;
        }

        Touch();
        return removed;
    }

    /// <summary>Removes all drawings.</summary>
    public void Clear()
    {
        Volatile.Write(ref _drawings, []);
        _selected = null;
        Touch();
    }

    /// <summary>Writes all drawings as JSON.</summary>
    /// <returns>The text.</returns>
    public string Save() => DrawingJson.Write(Drawings);

    /// <summary>Replaces all drawings with those of a JSON text from <see cref="Save"/>.</summary>
    /// <param name="json">The text.</param>
    /// <exception cref="FormatException">The text is no list of drawings or a drawing is incomplete.</exception>
    public void Load(string json)
    {
        var drawings = DrawingJson.Read(json);
        Volatile.Write(ref _drawings, [.. drawings]);
        _selected = null;
        Touch();
    }

    internal void Touch() => Interlocked.Increment(ref _version);

    internal void SetDraft(DrawingPoint[] points, DrawingPoint? preview)
    {
        Volatile.Write(ref _draft, points);
        _preview = preview;
        Touch();
    }

    internal void NotifyCompleted(Drawing drawing) => Completed?.Invoke(drawing);

    /// <summary>Converts a point to a position in the chart, from the layout of the last frame.</summary>
    internal bool TryMap(ChartLayoutResult layout, DrawingPoint point, out Vector2 position)
    {
        position = default;
        if (!TryPlace(layout, _xAxis, out var x) || !TryPlace(layout, _yAxis, out var y))
        {
            return false;
        }

        position = new Vector2((float)x.ToPixel(point.X - Origin(_xAxis)), (float)y.ToPixel(point.Y - Origin(_yAxis)));
        return true;
    }

    /// <summary>Converts a position in the chart to a point.</summary>
    internal bool TryUnmap(ChartLayoutResult layout, Vector2 position, out DrawingPoint point)
    {
        point = default;
        if (!TryPlace(layout, _xAxis, out var x) || !TryPlace(layout, _yAxis, out var y))
        {
            return false;
        }

        point = new DrawingPoint(x.ToData(position.X) + Origin(_xAxis), y.ToData(position.Y) + Origin(_yAxis));
        return true;
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var layout = canvas.Layout;
        canvas.Clip = layout.PlotArea;
        foreach (var drawing in Drawings)
        {
            DrawOne(canvas, layout, drawing.Kind, drawing.Snapshot, drawing.Color, drawing.Thickness, drawing == _selected);
        }

        if (_tool is { } tool && Draft.Length > 0)
        {
            var points = _preview is { } preview ? [.. Draft, preview] : Draft;
            DrawOne(canvas, layout, tool, points, Color with { A = 200 }, 1.5f, isSelected: true);
        }

        canvas.Clip = null;
    }

    private static double Origin(AxisBase axis) => axis is DateTimeAxis time ? time.OriginTicks : 0;

    private static bool TryPlace(ChartLayoutResult layout, AxisBase axis, out Numerics.IAxisTransform transform)
    {
        foreach (var placement in layout.Axes)
        {
            if (placement.Axis == axis)
            {
                var bounds = placement.Bounds;
                transform = axis.IsHorizontal ? axis.CreateTransform(bounds.X, bounds.Width) : axis.CreateTransform(bounds.Y, bounds.Height);
                return true;
            }
        }

        transform = null!;
        return false;
    }

    private void DrawOne(OverlayCanvas canvas, ChartLayoutResult layout, DrawingKind kind, DrawingPoint[] points, Rgba8 color, float thickness, bool isSelected)
    {
        _segments.Clear();
        _labels.Clear();
        _bands.Clear();
        DrawingShapes.Build(this, layout, kind, points, _segments, _labels, _bands);
        var area = layout.PlotArea;
        Span<Vector2> corners = stackalloc Vector2[8];
        foreach (var band in _bands)
        {
            var count = DrawingShapes.ClipPolygon([band.A, band.B, band.C, band.D], area, corners);
            for (var i = 2; i < count; i++)
            {
                canvas.FillTriangle(corners[0], corners[i - 1], corners[i], color with { A = 28 });
            }
        }

        foreach (var segment in _segments)
        {
            var (from, to) = (segment.From, segment.To);
            if (DrawingShapes.ClipLine(ref from, ref to, area))
            {
                canvas.DrawLine(from, to, segment.IsFaint ? 1 : thickness, segment.IsFaint ? color with { A = 150 } : color);
            }
        }

        foreach (var label in _labels)
        {
            canvas.DrawText(label.Text, Math.Round(label.Position.X), Math.Round(label.Position.Y), LabelSize, color);
        }

        if (!isSelected)
        {
            return;
        }

        foreach (var point in points)
        {
            if (TryMap(layout, point, out var at))
            {
                canvas.FillRectangle(new DipRect(at.X - HandleSize, at.Y - HandleSize, HandleSize * 2, HandleSize * 2), canvas.BackgroundColor);
                canvas.DrawRectangle(new DipRect(at.X - HandleSize, at.Y - HandleSize, HandleSize * 2, HandleSize * 2), color);
            }
        }
    }
}

/// <summary>The lines, labels and filled bands of each tool, in positions of the chart.</summary>
internal static class DrawingShapes
{
    private static readonly double[] _retracement = [0, 0.236, 0.382, 0.5, 0.618, 0.786, 1];
    private static readonly double[] _extension = [0, 0.618, 1, 1.272, 1.618, 2.618];

    public static void Build(DrawingLayer layer, ChartLayoutResult layout, DrawingKind kind, ReadOnlySpan<DrawingPoint> points, List<Segment> segments, List<Label> labels, List<Band> bands)
    {
        Span<Vector2> at = stackalloc Vector2[Math.Min(points.Length, 64)];
        if (points.Length > at.Length)
        {
            at = new Vector2[points.Length];
        }

        for (var i = 0; i < points.Length; i++)
        {
            if (!layer.TryMap(layout, points[i], out at[i]))
            {
                return;
            }
        }

        var area = layout.PlotArea;
        switch (kind)
        {
            case DrawingKind.FibonacciRetracement when points.Length >= 2:
                Levels(layer, layout, points[0], points[1], points[1].X, _retracement, isExtension: false, segments, labels, bands);
                segments.Add(new Segment(at[0], at[1], IsFaint: true));
                break;
            case DrawingKind.FibonacciExtension when points.Length >= 3:
                var move = points[1].Y - points[0].Y;
                var end = points[2].X + (points[1].X - points[0].X);
                Levels(layer, layout, points[2], points[2] with { Y = points[2].Y + move }, end, _extension, isExtension: true, segments, labels, bands);
                Polyline(at[..3], segments, isFaint: true);
                break;
            case DrawingKind.TrendChannel when points.Length >= 3:
                Channel(at[0], at[1], at[2], segments, bands);
                break;
            case DrawingKind.Pitchfork when points.Length >= 3:
                Pitchfork(at[0], at[1], at[2], area, segments);
                break;
            case DrawingKind.ElliottWave:
                Polyline(at[..points.Length], segments, isFaint: false);
                for (var i = 0; i < points.Length; i++)
                {
                    Name(at, i, i.ToString(CultureInfo.InvariantCulture), labels);
                }

                break;
            case DrawingKind.Xabcd:
                Harmonic(points, at[..points.Length], segments, labels, bands);
                break;
            case DrawingKind.HeadAndShoulders:
                Shoulders(at[..points.Length], area, segments, labels);
                break;
            default:
                Polyline(at[..points.Length], segments, isFaint: false);
                break;
        }
    }

    /// <summary>Cuts a line to the part inside an area (Liang and Barsky).</summary>
    /// <returns>Whether a part is inside.</returns>
    public static bool ClipLine(ref Vector2 from, ref Vector2 to, DipRect area)
    {
        var (enter, leave) = (0.0, 1.0);
        var delta = to - from;
        ReadOnlySpan<double> p = [-delta.X, delta.X, -delta.Y, delta.Y];
        ReadOnlySpan<double> q = [from.X - area.X, area.Right - from.X, from.Y - area.Y, area.Bottom - from.Y];
        for (var i = 0; i < 4; i++)
        {
            if (p[i] == 0)
            {
                if (q[i] < 0)
                {
                    return false;
                }

                continue;
            }

            var r = q[i] / p[i];
            if (p[i] < 0)
            {
                enter = Math.Max(enter, r);
            }
            else
            {
                leave = Math.Min(leave, r);
            }
        }

        if (enter > leave)
        {
            return false;
        }

        (from, to) = (from + (delta * (float)enter), from + (delta * (float)leave));
        return true;
    }

    /// <summary>Cuts a convex polygon to an area, edge by edge (Sutherland and Hodgman).</summary>
    /// <returns>The number of corners written, at most eight.</returns>
    public static int ClipPolygon(ReadOnlySpan<Vector2> polygon, DipRect area, Span<Vector2> result)
    {
        Span<Vector2> a = stackalloc Vector2[8];
        Span<Vector2> b = stackalloc Vector2[8];
        polygon.CopyTo(a);
        var count = polygon.Length;
        for (var edge = 0; edge < 4 && count > 0; edge++)
        {
            var written = 0;
            for (var i = 0; i < count; i++)
            {
                var (current, next) = (a[i], a[(i + 1) % count]);
                var (isIn, nextIn) = (Inside(current, edge, area), Inside(next, edge, area));
                if (isIn && written < 8)
                {
                    b[written++] = current;
                }

                if (isIn != nextIn && written < 8)
                {
                    b[written++] = Cross(current, next, edge, area);
                }
            }

            b[..written].CopyTo(a);
            count = written;
        }

        a[..count].CopyTo(result);
        return count;
    }

    /// <summary>Finds the distance from a position to the nearest line of a drawing.</summary>
    public static double Distance(Vector2 position, List<Segment> segments)
    {
        var best = double.PositiveInfinity;
        foreach (var segment in segments)
        {
            var line = segment.To - segment.From;
            var length = line.LengthSquared();
            var t = length > 0 ? Math.Clamp(Vector2.Dot(position - segment.From, line) / length, 0, 1) : 0;
            best = Math.Min(best, Vector2.Distance(position, segment.From + (line * t)));
        }

        return best;
    }

    private static bool Inside(Vector2 point, int edge, DipRect area) => edge switch
    {
        0 => point.X >= area.X,
        1 => point.X <= area.Right,
        2 => point.Y >= area.Y,
        _ => point.Y <= area.Bottom,
    };

    private static Vector2 Cross(Vector2 a, Vector2 b, int edge, DipRect area)
    {
        var delta = b - a;
        var t = edge switch
        {
            0 => (area.X - a.X) / delta.X,
            1 => (area.Right - a.X) / delta.X,
            2 => (area.Y - a.Y) / delta.Y,
            _ => (area.Bottom - a.Y) / delta.Y,
        };
        return a + (delta * (float)t);
    }

    private static void Polyline(ReadOnlySpan<Vector2> at, List<Segment> segments, bool isFaint)
    {
        for (var i = 1; i < at.Length; i++)
        {
            segments.Add(new Segment(at[i - 1], at[i], isFaint));
        }
    }

    // Each level: a line from the first to the last time, its ratio and price; the bands between levels filled.
    private static void Levels(DrawingLayer layer, ChartLayoutResult layout, DrawingPoint from, DrawingPoint to, double end, double[] ratios, bool isExtension, List<Segment> segments, List<Label> labels, List<Band> bands)
    {
        var previous = (Vector2?)null;
        var previousEnd = default(Vector2);
        foreach (var ratio in ratios)
        {
            var price = isExtension ? from.Y + ((to.Y - from.Y) * ratio) : to.Y + ((from.Y - to.Y) * ratio);
            if (!layer.TryMap(layout, new DrawingPoint(from.X, price), out var left) || !layer.TryMap(layout, new DrawingPoint(end, price), out var right))
            {
                return;
            }

            segments.Add(new Segment(left, right, IsFaint: ratio is not (0 or 1)));
            labels.Add(new Label((ratio * 100).ToString("0.#", ChartText.Culture) + " % (" + price.ToString("G6", ChartText.Culture) + ")", new Vector2(Math.Max(left.X, right.X) + 4, left.Y + 4)));
            if (previous is { } top)
            {
                bands.Add(new Band(top, previousEnd, right, left));
            }

            previous = left;
            previousEnd = right;
        }
    }

    // The line through the first two points and its parallel through the third.
    private static void Channel(Vector2 a, Vector2 b, Vector2 c, List<Segment> segments, List<Band> bands)
    {
        var slope = b.X != a.X ? (b.Y - a.Y) / (b.X - a.X) : 0;
        var offset = c.Y - (a.Y + ((c.X - a.X) * slope));
        var a2 = a with { Y = a.Y + offset };
        var b2 = b with { Y = b.Y + offset };
        segments.Add(new Segment(a, b, IsFaint: false));
        segments.Add(new Segment(a2, b2, IsFaint: false));
        segments.Add(new Segment((a + a2) / 2, (b + b2) / 2, IsFaint: true));
        bands.Add(new Band(a, b, b2, a2));
    }

    // The median line from the pivot through the middle of the other two, and its parallels through them, to the edge.
    private static void Pitchfork(Vector2 pivot, Vector2 first, Vector2 second, DipRect area, List<Segment> segments)
    {
        var middle = (first + second) / 2;
        var direction = middle - pivot;
        if (direction.LengthSquared() <= 0)
        {
            return;
        }

        var reach = (float)(Math.Sqrt((area.Width * area.Width) + (area.Height * area.Height)) / direction.Length()) + 1;
        segments.Add(new Segment(pivot, pivot + (direction * reach), IsFaint: false));
        segments.Add(new Segment(first, first + (direction * reach), IsFaint: false));
        segments.Add(new Segment(second, second + (direction * reach), IsFaint: false));
        segments.Add(new Segment(first, second, IsFaint: true));
    }

    private static void Harmonic(ReadOnlySpan<DrawingPoint> points, ReadOnlySpan<Vector2> at, List<Segment> segments, List<Label> labels, List<Band> bands)
    {
        Polyline(at, segments, isFaint: false);
        string[] names = ["X", "A", "B", "C", "D"];
        for (var i = 0; i < at.Length; i++)
        {
            Name(at, i, names[i], labels);
        }

        if (at.Length >= 3)
        {
            segments.Add(new Segment(at[0], at[2], IsFaint: true));
            bands.Add(new Band(at[0], at[1], at[2], at[2]));
            Ratio(points[1].Y - points[2].Y, points[1].Y - points[0].Y, (at[0] + at[2]) / 2, labels);
        }

        if (at.Length >= 5)
        {
            segments.Add(new Segment(at[2], at[4], IsFaint: true));
            bands.Add(new Band(at[2], at[3], at[4], at[4]));
            Ratio(points[3].Y - points[4].Y, points[3].Y - points[2].Y, (at[2] + at[4]) / 2, labels);
        }
    }

    // The line through both lows of the neck, across the whole pattern.
    private static void Shoulders(ReadOnlySpan<Vector2> at, DipRect area, List<Segment> segments, List<Label> labels)
    {
        Polyline(at, segments, isFaint: false);
        string[] names = [string.Empty, "LS", string.Empty, "H", string.Empty, "RS", string.Empty];
        for (var i = 0; i < at.Length; i++)
        {
            Name(at, i, names[i], labels);
        }

        if (at.Length >= 7 && at[4].X != at[2].X)
        {
            var slope = (at[4].Y - at[2].Y) / (at[4].X - at[2].X);
            var left = new Vector2(at[0].X, at[2].Y + ((at[0].X - at[2].X) * slope));
            var right = new Vector2(Math.Min((float)area.Right, at[6].X + ((at[6].X - at[0].X) * 0.2f)), 0);
            right.Y = at[2].Y + ((right.X - at[2].X) * slope);
            segments.Add(new Segment(left, right, IsFaint: true));
        }
    }

    private static void Ratio(double part, double whole, Vector2 place, List<Label> labels)
    {
        if (whole != 0)
        {
            labels.Add(new Label(Math.Abs(part / whole).ToString("0.000", CultureInfo.InvariantCulture), place + new Vector2(4, 0)));
        }
    }

    // A name above a peak and below a trough.
    private static void Name(ReadOnlySpan<Vector2> at, int i, string text, List<Label> labels)
    {
        if (text.Length == 0)
        {
            return;
        }

        var neighbour = i > 0 ? at[i - 1].Y : i + 1 < at.Length ? at[i + 1].Y : at[i].Y;
        var isPeak = at[i].Y <= neighbour;
        labels.Add(new Label(text, new Vector2(at[i].X - 4, isPeak ? at[i].Y - 8 : at[i].Y + 16)));
    }

    internal readonly record struct Segment(Vector2 From, Vector2 To, bool IsFaint);

    internal readonly record struct Label(string Text, Vector2 Position);

    internal readonly record struct Band(Vector2 A, Vector2 B, Vector2 C, Vector2 D);
}

/// <summary>Reads and writes drawings as JSON: a version and a list of drawings with tool, color, thickness and points.</summary>
internal static class DrawingJson
{
    private const int Version = 1;

    private static readonly JsonSerializerOptions _options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Write(IEnumerable<Drawing> drawings)
    {
        var file = new FileDto(Version, [.. drawings.Select(drawing => new DrawingDto(
            drawing.Kind,
            new Themes.ThemeColor(drawing.Color).ToString(),
            drawing.Thickness,
            [.. drawing.Points.Select(point => new PointDto(point.X, point.Y))]))]);
        return JsonSerializer.Serialize(file, _options);
    }

    public static IReadOnlyList<Drawing> Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        FileDto? file;
        try
        {
            file = JsonSerializer.Deserialize<FileDto>(json, _options);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The text is no list of drawings: " + exception.Message, exception);
        }

        if (file?.Drawings is null)
        {
            throw new FormatException("The text holds no drawings.");
        }

        if (file.Version > Version)
        {
            throw new FormatException($"The drawings were saved by a newer version ({file.Version}).");
        }

        var drawings = new List<Drawing>();
        foreach (var dto in file.Drawings)
        {
            try
            {
                var drawing = new Drawing(dto.Kind, (dto.Points ?? []).Select(point => new DrawingPoint(point.X, point.Y)))
                {
                    Thickness = dto.Thickness > 0 ? dto.Thickness : 1.5f,
                };
                if (dto.Color is { Length: > 0 } color)
                {
                    drawing.Color = Themes.ThemeColor.Parse(color).Value;
                }

                drawings.Add(drawing);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("A drawing is incomplete: " + exception.Message, exception);
            }
        }

        return drawings;
    }

    private sealed record FileDto(int Version, DrawingDto[]? Drawings);

    private sealed record DrawingDto(DrawingKind Kind, string? Color, float Thickness, PointDto[]? Points);

    private sealed record PointDto(double X, double Y);
}

/// <summary>
/// Creates drawings with the tool of a <see cref="DrawingLayer"/> by clicks, a brush by dragging, and edits the
/// existing ones: drag a point or the whole drawing, Delete removes the selected one, Escape stops the tool.
/// Every finished change is a step of the undo chain of the chart. Number keys 1 to 9 choose a tool, 0 none.
/// </summary>
public sealed class DrawingModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above editing annotations and panning.</summary>
    public const int DefaultPriority = 30;

    private readonly DrawingLayer? _fixedLayer;
    private readonly List<DrawingShapes.Segment> _segments = [];
    private readonly List<DrawingShapes.Label> _labels = [];
    private readonly List<DrawingShapes.Band> _bands = [];
    private Drawing? _dragged;
    private int _point = -1;
    private DrawingPoint[] _before = [];
    private DrawingPoint _start;
    private bool _isBrushing;

    /// <summary>Initializes a new instance of the <see cref="DrawingModifier"/> class.</summary>
    /// <param name="layer">The drawings, or <see langword="null"/> for the first <see cref="DrawingLayer"/> of the chart.</param>
    public DrawingModifier(DrawingLayer? layer = null)
        : base(DefaultPriority) => _fixedLayer = layer;

    /// <summary>Gets or sets how near a position must be to a line or point, in device-independent units.</summary>
    public double Tolerance { get; set; } = 6;

    // Found anew each time, so that the modifier keeps working when the chart creates its overlays again.
    private DrawingLayer? Layer
    {
        get
        {
            if (_fixedLayer is not null || Interaction is not { } interaction)
            {
                return _fixedLayer;
            }

            foreach (var overlay in interaction.Chart.Overlays)
            {
                if (overlay is DrawingLayer layer)
                {
                    return layer;
                }
            }

            return null;
        }
    }

    private DrawingLayer ActiveLayer => Layer!;

    /// <summary>Gets or sets a value indicating whether the number keys choose a tool.</summary>
    public bool UsesNumberKeys { get; set; } = true;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { Chart.CurrentLayout: { } layout } interaction || Layer is null || !ActiveLayer.TryUnmap(layout, input.Position, out var point))
        {
            return ModifierResult.Ignored;
        }

        if (ActiveLayer.Tool is { } tool)
        {
            return Create(interaction, tool, input, point);
        }

        return input.Kind switch
        {
            PointerEventKind.Pressed when input.ChangedButton == PointerButton.Primary => Press(layout, input.Position, point),
            PointerEventKind.Moved when _dragged is not null => Drag(point),
            PointerEventKind.Released or PointerEventKind.Canceled when _dragged is not null => Finish(interaction),
            _ => ModifierResult.Ignored,
        };
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (input.Kind != KeyEventKind.Pressed || Interaction is not { } interaction || Layer is null)
        {
            return ModifierResult.Ignored;
        }

        switch (input.Key)
        {
            case ChartKey.Escape when ActiveLayer.Tool is not null:
                ActiveLayer.Tool = null;
                return ModifierResult.Handled;
            case ChartKey.Delete when ActiveLayer.Selected is { } selected:
                ActiveLayer.Remove(selected);
                interaction.History.Record(new ListEdit(ActiveLayer, selected, isAdded: false));
                return ModifierResult.Handled;
            case >= ChartKey.D0 and <= ChartKey.D9 when UsesNumberKeys && input.Modifiers == InputModifiers.None:
                var number = input.Key - ChartKey.D0;
                ActiveLayer.Tool = number == 0 ? null : (DrawingKind)(number - 1);
                return ModifierResult.Handled;
            default:
                return ModifierResult.Ignored;
        }
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture)
    {
        _dragged = null;
        _isBrushing = false;
    }

    private ModifierResult Create(ChartInteraction interaction, DrawingKind tool, in PointerInputEvent input, DrawingPoint point)
    {
        var draft = ActiveLayer.Draft;
        if (tool == DrawingKind.Brush)
        {
            switch (input.Kind)
            {
                case PointerEventKind.Pressed when input.ChangedButton == PointerButton.Primary:
                    _isBrushing = true;
                    ActiveLayer.SetDraft([point], null);
                    return ModifierResult.Capture;
                case PointerEventKind.Moved when _isBrushing:
                    ActiveLayer.SetDraft([.. draft, point], null);
                    return ModifierResult.Handled;
                case PointerEventKind.Released when _isBrushing:
                    _isBrushing = false;
                    Complete(interaction, tool, [.. draft, point]);
                    return ModifierResult.Release;
                default:
                    return ModifierResult.Ignored;
            }
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            if (draft.Length > 0)
            {
                ActiveLayer.SetDraft(draft, point);
            }

            return ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed || input.ChangedButton != PointerButton.Primary)
        {
            return ModifierResult.Ignored;
        }

        DrawingPoint[] points = [.. draft, point];
        if (points.Length >= Drawing.PointCount(tool))
        {
            Complete(interaction, tool, points);
        }
        else
        {
            ActiveLayer.SetDraft(points, point);
        }

        return ModifierResult.Handled;
    }

    private void Complete(ChartInteraction interaction, DrawingKind tool, DrawingPoint[] points)
    {
        ActiveLayer.Tool = null;
        if (points.Length < 2)
        {
            return;
        }

        var drawing = new Drawing(tool, points) { Color = ActiveLayer.Color };
        ActiveLayer.Add(drawing);
        ActiveLayer.Selected = drawing;
        interaction.History.Record(new ListEdit(ActiveLayer, drawing, isAdded: true));
        ActiveLayer.NotifyCompleted(drawing);
    }

    // A press on a point of the selected drawing drags the point; on any line, the whole drawing.
    private ModifierResult Press(ChartLayoutResult layout, Vector2 position, DrawingPoint point)
    {
        var drawings = ActiveLayer.Drawings;
        if (ActiveLayer.Selected is { } selected)
        {
            var points = selected.Snapshot;
            for (var i = 0; i < points.Length; i++)
            {
                if (ActiveLayer.TryMap(layout, points[i], out var at) && Vector2.Distance(at, position) <= Tolerance)
                {
                    return StartDrag(selected, i, point);
                }
            }
        }

        for (var i = drawings.Count - 1; i >= 0; i--)
        {
            _segments.Clear();
            DrawingShapes.Build(ActiveLayer, layout, drawings[i].Kind, drawings[i].Snapshot, _segments, _labels, _bands);
            _labels.Clear();
            _bands.Clear();
            if (DrawingShapes.Distance(position, _segments) <= Tolerance)
            {
                ActiveLayer.Selected = drawings[i];
                return StartDrag(drawings[i], -1, point);
            }
        }

        if (ActiveLayer.Selected is not null)
        {
            ActiveLayer.Selected = null;
        }

        return ModifierResult.Ignored;
    }

    private ModifierResult StartDrag(Drawing drawing, int index, DrawingPoint point)
    {
        _dragged = drawing;
        _point = index;
        _before = drawing.Snapshot;
        _start = point;
        return ModifierResult.Capture;
    }

    private ModifierResult Drag(DrawingPoint point)
    {
        var drawing = _dragged!;
        var moved = (DrawingPoint[])_before.Clone();
        if (_point >= 0)
        {
            moved[_point] = point;
        }
        else
        {
            var (dx, dy) = (point.X - _start.X, point.Y - _start.Y);
            for (var i = 0; i < moved.Length; i++)
            {
                moved[i] = new DrawingPoint(moved[i].X + dx, moved[i].Y + dy);
            }
        }

        drawing.SetPoints(moved);
        ActiveLayer.Touch();
        return ModifierResult.Handled;
    }

    private ModifierResult Finish(ChartInteraction interaction)
    {
        var drawing = _dragged!;
        _dragged = null;
        var after = drawing.Snapshot;
        if (!after.AsSpan().SequenceEqual(_before))
        {
            interaction.History.Record(new PointsEdit(ActiveLayer, drawing, _before, after));
        }

        return ModifierResult.Release;
    }

    private sealed class ListEdit(DrawingLayer layer, Drawing drawing, bool isAdded) : IChartEdit
    {
        public void Undo() => Apply(!isAdded);

        public void Redo() => Apply(isAdded);

        private void Apply(bool add)
        {
            if (add)
            {
                layer.Add(drawing);
            }
            else
            {
                layer.Remove(drawing);
            }
        }
    }

    private sealed class PointsEdit(DrawingLayer layer, Drawing drawing, DrawingPoint[] before, DrawingPoint[] after) : IChartEdit
    {
        public void Undo()
        {
            drawing.SetPoints(before);
            layer.Touch();
        }

        public void Redo()
        {
            drawing.SetPoints(after);
            layer.Touch();
        }
    }
}

/// <summary>Describes the drawing tools of a chart.</summary>
public static class DrawingTools
{
    /// <summary>Describes a layer of drawings over the first pane, with creating and editing by mouse and keyboard.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="created">Receives the layer when the chart is built, for example to set the tool from a toolbar or to save the drawings.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Drawings(this ChartBuilder builder, Action<DrawingLayer>? created = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        DrawingLayer? layer = null;
        return builder
            .AddOverlay(chart =>
            {
                // The same layer again when the chart creates its overlays anew, so that no drawing is lost.
                if (layer is null || layer.XAxis != chart.HorizontalAxes[0])
                {
                    layer = new DrawingLayer(chart.HorizontalAxes[0], chart.Panes[0].Axes[0]);
                    created?.Invoke(layer);
                }

                return layer;
            })
            .Modifiers(m => m.Custom(() => new DrawingModifier()));
    }
}
