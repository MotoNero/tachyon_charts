using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Annotations;

/// <summary>
/// A mark on a chart. Coordinates are values of the axes. Without an axis, that direction uses chart
/// coordinates in device-independent units, so the annotation stays in place while zooming.
/// </summary>
/// <param name="xAxis">The horizontal axis, or <see langword="null"/> for chart coordinates.</param>
/// <param name="yAxis">The vertical axis, or <see langword="null"/> for chart coordinates.</param>
public abstract class Annotation(AxisBase? xAxis, AxisBase? yAxis) : ChartOverlay
{
    private volatile StrongBounds? _bounds;

    /// <summary>Gets the horizontal axis.</summary>
    public AxisBase? XAxis { get; } = xAxis;

    /// <summary>Gets the vertical axis.</summary>
    public AxisBase? YAxis { get; } = yAxis;

    /// <summary>Gets or sets the main color.</summary>
    public Rgba8 Color { get; set; } = new(240, 190, 90, 255);

    /// <summary>Gets or sets a value indicating whether an edit modifier may change it.</summary>
    public bool IsEditable { get; set; }

    /// <summary>
    /// Gets the area of the last frame in chart coordinates, for hit tests on any thread.
    /// </summary>
    public DipRect? LastBounds => _bounds?.Value;

    /// <summary>Occurs on the thread of the chart after a change by interaction.</summary>
    public event Action<Annotation>? Edited;

    /// <summary>Occurs when a drag of the annotation ends, once per drag, after the steps <see cref="Edited"/> reports.</summary>
    public event Action<Annotation>? EditFinished;

    /// <summary>Tests whether a position touches the annotation.</summary>
    /// <param name="position">The position in chart coordinates.</param>
    /// <param name="tolerance">The tolerance in device-independent units.</param>
    /// <returns>Whether it does.</returns>
    public virtual bool Contains(Vector2 position, double tolerance) =>
        _bounds?.Value is { } bounds
        && position.X >= bounds.X - tolerance && position.X <= bounds.Right + tolerance
        && position.Y >= bounds.Y - tolerance && position.Y <= bounds.Bottom + tolerance;

    /// <summary>Moves the annotation.</summary>
    /// <param name="deltaX">The distance in X, in the unit of the coordinate.</param>
    /// <param name="deltaY">The distance in Y, in the unit of the coordinate.</param>
    public abstract void Move(double deltaX, double deltaY);

    /// <summary>
    /// Changes the size by moving the corner at the far end. The default is that the size is fixed.
    /// </summary>
    /// <param name="deltaX">The distance in X, in the unit of the coordinate.</param>
    /// <param name="deltaY">The distance in Y, in the unit of the coordinate.</param>
    /// <returns>Whether the annotation can change its size.</returns>
    public virtual bool Resize(double deltaX, double deltaY) => false;

    internal void NotifyEdited() => Edited?.Invoke(this);

    internal void NotifyEditFinished() => EditFinished?.Invoke(this);

    private volatile HandleAreas? _handles;
    private bool _isHot;

    /// <summary>Gets or sets a value indicating whether the pointer is over the annotation, so that it shows its handles.</summary>
    internal bool IsHot
    {
        get => _isHot;
        set
        {
            if (_isHot != value)
            {
                _isHot = value;
                OnChanged();
            }
        }
    }

    /// <summary>Finds the handle at a position: a part a drag changes alone, such as an edge of a band or an end of a line.</summary>
    /// <param name="position">The position in the chart.</param>
    /// <param name="tolerance">How near the position must be.</param>
    /// <returns>The number of the handle, or -1 for none.</returns>
    internal int HandleAt(Vector2 position, double tolerance)
    {
        if (_handles?.Areas is not { } areas)
        {
            return -1;
        }

        for (var i = 0; i < areas.Length; i++)
        {
            var area = areas[i];
            if (position.X >= area.X - tolerance && position.X <= area.Right + tolerance && position.Y >= area.Y - tolerance && position.Y <= area.Bottom + tolerance)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Moves one handle by a distance in the units of the axes; see <see cref="SetHandles"/>.</summary>
    /// <param name="handle">The number of the handle.</param>
    /// <param name="deltaX">The distance along the X axis.</param>
    /// <param name="deltaY">The distance along the Y axis.</param>
    public virtual void MoveHandle(int handle, double deltaX, double deltaY)
    {
    }

    /// <summary>Gets the state a drag changes, to take it back; <see langword="null"/> when the annotation keeps none.</summary>
    /// <returns>The state.</returns>
    public virtual object? Capture() => null;

    /// <summary>Takes back a state from <see cref="Capture"/>.</summary>
    /// <param name="state">The state.</param>
    public virtual void Restore(object state)
    {
    }

    /// <summary>
    /// Sets the places of the handles as last drawn, in chart units: a point for an end or a corner, a line for an edge.
    /// The first that holds a position wins, so corners come before edges.
    /// </summary>
    /// <param name="areas">The places.</param>
    protected void SetHandles(params DipRect[] areas) => _handles = new HandleAreas(areas);

    /// <summary>Draws small squares at points, while the pointer is over an annotation that may be edited.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="points">The points.</param>
    protected void DrawHandles(OverlayCanvas canvas, ReadOnlySpan<Vector2> points)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        if (!IsEditable || !_isHot)
        {
            return;
        }

        const float Half = 4;
        foreach (var point in points)
        {
            var square = new DipRect(point.X - Half, point.Y - Half, Half * 2, Half * 2);
            canvas.FillRectangle(square, canvas.BackgroundColor);
            canvas.DrawRectangle(square, Color);
        }
    }

    private sealed record HandleAreas(DipRect[] Areas);

    /// <summary>Converts coordinates to the position in the chart.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <returns>The position.</returns>
    protected Vector2 ToChart(OverlayCanvas canvas, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var alongX = XAxis is { } horizontal && canvas.TryGetPosition(horizontal, x, out var positionX) ? positionX : x;
        var alongY = YAxis is { } vertical && canvas.TryGetPosition(vertical, y, out var positionY) ? positionY : y;

        // With the X axis down, X goes up and down on the screen and Y across.
        return IsTurned ? new Vector2((float)alongY, (float)alongX) : new Vector2((float)alongX, (float)alongY);
    }

    /// <summary>Gets a value indicating whether the axes lie turned, with the X axis running down.</summary>
    protected bool IsTurned => XAxis is { IsHorizontal: false } || YAxis is { IsHorizontal: true };

    /// <summary>Draws a line across the plot at a place of an axis, upright for a horizontal axis, level for a standing one.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="axis">The axis.</param>
    /// <param name="value">The value.</param>
    /// <param name="label">A label at the start of the line, or <see langword="null"/>.</param>
    protected void DrawAcross(OverlayCanvas canvas, AxisBase axis, double value, string? label)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(axis);
        var plot = canvas.Layout.PlotArea;
        if (!canvas.TryGetPosition(axis, value, out var at))
        {
            return;
        }

        canvas.Clip = plot;
        if (axis.IsHorizontal)
        {
            canvas.DrawLine(new Vector2((float)at, (float)plot.Y), new Vector2((float)at, (float)plot.Bottom), 1, Color);
            SetBounds(new DipRect(at, plot.Y, 1, plot.Height));
        }
        else
        {
            canvas.DrawLine(new Vector2((float)plot.X, (float)at), new Vector2((float)plot.Right, (float)at), 1, Color);
            SetBounds(new DipRect(plot.X, at, plot.Width, 1));
        }

        if (label is null)
        {
            return;
        }

        // A label at the top of an upright line, at the right end of a level one.
        var width = canvas.MeasureWidth(label, canvas.LabelSize) + 8;
        if (axis.IsHorizontal)
        {
            canvas.DrawLabel(label, at + 4, plot.Y + 4, Color, canvas.BackgroundColor);
        }
        else
        {
            canvas.DrawLabel(label, plot.Right - width - 4, at - canvas.GetLineHeight(canvas.LabelSize) - 3, Color, canvas.BackgroundColor);
        }
    }

    /// <summary>Remembers the area of this frame.</summary>
    /// <param name="bounds">The area.</param>
    protected void SetBounds(DipRect bounds) => _bounds = new StrongBounds(bounds);

    /// <summary>Spans two positions.</summary>
    /// <param name="a">One corner.</param>
    /// <param name="b">The other corner.</param>
    /// <returns>The rectangle.</returns>
    protected static DipRect Span(Vector2 a, Vector2 b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new DipRect(left, top, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
    }

    /// <summary>Gets a rectangle without size at a point, a handle at an end or a corner.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The rectangle.</returns>
    protected static DipRect Point(Vector2 point) => new(point.X, point.Y, 0, 0);

    private sealed record StrongBounds(DipRect Value);
}

/// <summary>A line across the plot at a Y value.</summary>
/// <param name="yAxis">The vertical axis.</param>
public sealed class HorizontalLineAnnotation(AxisBase yAxis) : Annotation(null, yAxis)
{
    private double _y;

    /// <summary>Gets or sets the value.</summary>
    public double Y
    {
        get => _y;
        set
        {
            _y = value;
            OnChanged();
        }
    }

    /// <summary>Gets or sets a label at the right end, or <see langword="null"/> for none.</summary>
    public string? Label { get; set; }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => Y += deltaY;

    /// <inheritdoc />
    public override object? Capture() => _y;

    /// <inheritdoc />
    public override void Restore(object state) => Y = (double)state;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas) => DrawAcross(canvas, YAxis!, _y, Label);
}

/// <summary>A line across the plot at an X value.</summary>
/// <param name="xAxis">The horizontal axis.</param>
public sealed class VerticalLineAnnotation(AxisBase xAxis) : Annotation(xAxis, null)
{
    private double _x;

    /// <summary>Gets or sets the value.</summary>
    public double X
    {
        get => _x;
        set
        {
            _x = value;
            OnChanged();
        }
    }

    /// <summary>Gets or sets a label at the top, or <see langword="null"/> for none.</summary>
    public string? Label { get; set; }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => X += deltaX;

    /// <inheritdoc />
    public override object? Capture() => _x;

    /// <inheritdoc />
    public override void Restore(object state) => X = (double)state;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas) => DrawAcross(canvas, XAxis!, _x, Label);
}

/// <summary>A rectangle between two corners.</summary>
/// <param name="xAxis">The horizontal axis, or <see langword="null"/> for chart coordinates.</param>
/// <param name="yAxis">The vertical axis, or <see langword="null"/> for chart coordinates.</param>
public sealed class BoxAnnotation(AxisBase? xAxis, AxisBase? yAxis) : Annotation(xAxis, yAxis)
{
    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;

    /// <summary>Gets or sets the fill, which can be transparent.</summary>
    public Rgba8 Fill { get; set; } = new(240, 190, 90, 40);

    /// <summary>Gets the first corner.</summary>
    public (double X, double Y) First => (_x1, _y1);

    /// <summary>Gets the second corner.</summary>
    public (double X, double Y) Second => (_x2, _y2);

    /// <summary>Sets both corners.</summary>
    /// <param name="x1">The X value of the first corner.</param>
    /// <param name="y1">The Y value of the first corner.</param>
    /// <param name="x2">The X value of the second corner.</param>
    /// <param name="y2">The Y value of the second corner.</param>
    public void SetCorners(double x1, double y1, double x2, double y2)
    {
        (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => SetCorners(_x1 + deltaX, _y1 + deltaY, _x2 + deltaX, _y2 + deltaY);

    /// <inheritdoc />
    public override bool Resize(double deltaX, double deltaY)
    {
        SetCorners(_x1, _y1, _x2 + deltaX, _y2 + deltaY);
        return true;
    }

    /// <summary>
    /// Moves a corner (0: first, 1: second X with first Y, 2: second, 3: first X with second Y) or an edge (4: first X,
    /// 5: second X, 6: first Y, 7: second Y).
    /// </summary>
    /// <param name="handle">The number of the handle.</param>
    /// <param name="deltaX">The distance along the X axis.</param>
    /// <param name="deltaY">The distance along the Y axis.</param>
    public override void MoveHandle(int handle, double deltaX, double deltaY)
    {
        var (x1, y1, x2, y2) = (_x1, _y1, _x2, _y2);
        switch (handle)
        {
            case 0: (x1, y1) = (x1 + deltaX, y1 + deltaY); break;
            case 1: (x2, y1) = (x2 + deltaX, y1 + deltaY); break;
            case 2: (x2, y2) = (x2 + deltaX, y2 + deltaY); break;
            case 3: (x1, y2) = (x1 + deltaX, y2 + deltaY); break;
            case 4: x1 += deltaX; break;
            case 5: x2 += deltaX; break;
            case 6: y1 += deltaY; break;
            case 7: y2 += deltaY; break;
            default: return;
        }

        SetCorners(x1, y1, x2, y2);
    }

    /// <inheritdoc />
    public override object? Capture() => (_x1, _y1, _x2, _y2);

    /// <inheritdoc />
    public override void Restore(object state)
    {
        var (x1, y1, x2, y2) = ((double, double, double, double))state;
        SetCorners(x1, y1, x2, y2);
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var (a, b, c, d) = (ToChart(canvas, _x1, _y1), ToChart(canvas, _x2, _y1), ToChart(canvas, _x2, _y2), ToChart(canvas, _x1, _y2));
        var area = Span(a, c);
        canvas.Clip = canvas.Layout.PlotArea;
        canvas.FillRectangle(area, Fill);
        canvas.DrawRectangle(area, Color);
        SetBounds(area);
        SetHandles(Point(a), Point(b), Point(c), Point(d), Span(a, d), Span(b, c), Span(a, b), Span(d, c));
        DrawHandles(canvas, [a, b, c, d]);
    }
}

/// <summary>A strip across the plot between two values of an axis.</summary>
/// <param name="axis">The axis.</param>
public sealed class BandAnnotation(AxisBase axis) : Annotation(axis.IsHorizontal ? axis : null, axis.IsHorizontal ? null : axis)
{
    private double _start;
    private double _end;

    /// <summary>Gets the first value.</summary>
    public double Start => _start;

    /// <summary>Gets the second value.</summary>
    public double End => _end;

    /// <summary>Sets the values.</summary>
    /// <param name="start">The first value.</param>
    /// <param name="end">The second value.</param>
    public void SetRange(double start, double end)
    {
        (_start, _end) = (start, end);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY)
    {
        var delta = axis.IsHorizontal ? deltaX : deltaY;
        SetRange(_start + delta, _end + delta);
    }

    /// <inheritdoc />
    public override bool Resize(double deltaX, double deltaY)
    {
        SetRange(_start, _end + (axis.IsHorizontal ? deltaX : deltaY));
        return true;
    }

    /// <summary>Moves an edge: 0 the start, 1 the end.</summary>
    /// <param name="handle">The number of the handle.</param>
    /// <param name="deltaX">The distance along the X axis.</param>
    /// <param name="deltaY">The distance along the Y axis.</param>
    public override void MoveHandle(int handle, double deltaX, double deltaY)
    {
        var delta = axis.IsHorizontal ? deltaX : deltaY;
        if (handle == 0)
        {
            SetRange(_start + delta, _end);
        }
        else if (handle == 1)
        {
            SetRange(_start, _end + delta);
        }
    }

    /// <inheritdoc />
    public override object? Capture() => (_start, _end);

    /// <inheritdoc />
    public override void Restore(object state)
    {
        var (start, end) = ((double, double))state;
        SetRange(start, end);
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var plot = canvas.Layout.PlotArea;
        if (!canvas.TryGetPosition(axis, _start, out var first) || !canvas.TryGetPosition(axis, _end, out var second))
        {
            return;
        }

        var low = Math.Min(first, second);
        var length = Math.Abs(second - first);
        var area = axis.IsHorizontal ? new DipRect(low, plot.Y, length, plot.Height) : new DipRect(plot.X, low, plot.Width, length);
        canvas.Clip = plot;
        canvas.FillRectangle(area, Color);
        SetBounds(area);

        // The edges of the start and the end, each as a line across the plot.
        var (startEdge, endEdge) = axis.IsHorizontal
            ? (new DipRect(first, plot.Y, 0, plot.Height), new DipRect(second, plot.Y, 0, plot.Height))
            : (new DipRect(plot.X, first, plot.Width, 0), new DipRect(plot.X, second, plot.Width, 0));
        SetHandles(startEdge, endEdge);
        var middle = axis.IsHorizontal ? (float)(plot.Y + (plot.Height / 2)) : (float)(plot.X + (plot.Width / 2));
        DrawHandles(canvas, axis.IsHorizontal ? [new Vector2((float)first, middle), new Vector2((float)second, middle)] : [new Vector2(middle, (float)first), new Vector2(middle, (float)second)]);
    }
}

/// <summary>Text at a place.</summary>
/// <param name="xAxis">The horizontal axis, or <see langword="null"/> for chart coordinates.</param>
/// <param name="yAxis">The vertical axis, or <see langword="null"/> for chart coordinates.</param>
public sealed class TextAnnotation(AxisBase? xAxis, AxisBase? yAxis) : Annotation(xAxis, yAxis)
{
    private double _x;
    private double _y;

    /// <summary>Gets or sets the text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the font size.</summary>
    public float FontSize { get; set; } = 13;

    /// <summary>Gets the position.</summary>
    public (double X, double Y) Position => (_x, _y);

    /// <summary>Sets the position.</summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    public void MoveTo(double x, double y)
    {
        (_x, _y) = (x, y);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => MoveTo(_x + deltaX, _y + deltaY);

    /// <inheritdoc />
    public override object? Capture() => (_x, _y);

    /// <inheritdoc />
    public override void Restore(object state)
    {
        var (x, y) = ((double, double))state;
        MoveTo(x, y);
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var position = ToChart(canvas, _x, _y);
        canvas.DrawText(Text, position.X, position.Y, FontSize, Color);
        var height = canvas.GetLineHeight(FontSize);
        SetBounds(new DipRect(position.X, position.Y - height, canvas.MeasureWidth(Text, FontSize), height));
    }
}

/// <summary>
/// A straight line between two points in the units of the axes, at any angle. A drag moves it whole; a drag at an end
/// moves that end alone, so that place and direction change freely.
/// </summary>
/// <param name="xAxis">The horizontal axis.</param>
/// <param name="yAxis">The vertical axis.</param>
public sealed class LineAnnotation(AxisBase? xAxis, AxisBase? yAxis) : Annotation(xAxis, yAxis)
{
    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;

    /// <summary>Gets or sets the width in device-independent units.</summary>
    public float Thickness { get; set; } = 2;

    /// <summary>Gets or sets the text at the second end, or <see langword="null"/> for none.</summary>
    public string? Label { get; set; }

    /// <summary>Gets the first end.</summary>
    public (double X, double Y) Start => (_x1, _y1);

    /// <summary>Gets the second end.</summary>
    public (double X, double Y) End => (_x2, _y2);

    /// <summary>Sets both ends.</summary>
    /// <param name="x1">The X of the first end.</param>
    /// <param name="y1">The Y of the first end.</param>
    /// <param name="x2">The X of the second end.</param>
    /// <param name="y2">The Y of the second end.</param>
    public void SetEnds(double x1, double y1, double x2, double y2)
    {
        (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => SetEnds(_x1 + deltaX, _y1 + deltaY, _x2 + deltaX, _y2 + deltaY);

    /// <summary>Moves an end: 0 the first, 1 the second.</summary>
    /// <param name="handle">The number of the handle.</param>
    /// <param name="deltaX">The distance along the X axis.</param>
    /// <param name="deltaY">The distance along the Y axis.</param>
    public override void MoveHandle(int handle, double deltaX, double deltaY)
    {
        if (handle == 0)
        {
            SetEnds(_x1 + deltaX, _y1 + deltaY, _x2, _y2);
        }
        else if (handle == 1)
        {
            SetEnds(_x1, _y1, _x2 + deltaX, _y2 + deltaY);
        }
    }

    /// <inheritdoc />
    public override bool Contains(Vector2 position, double tolerance)
    {
        // Near the line itself, not anywhere in the rectangle it spans.
        if (LastEnds is not { } ends)
        {
            return false;
        }

        var (a, b) = ends;
        var along = b - a;
        var length = along.LengthSquared();
        var share = length <= 0 ? 0 : Math.Clamp(Vector2.Dot(position - a, along) / length, 0, 1);
        return Vector2.Distance(position, a + (along * share)) <= tolerance + Thickness;
    }

    /// <inheritdoc />
    public override object? Capture() => (_x1, _y1, _x2, _y2);

    /// <inheritdoc />
    public override void Restore(object state)
    {
        var (x1, y1, x2, y2) = ((double, double, double, double))state;
        SetEnds(x1, y1, x2, y2);
    }

    private volatile Ends? _ends;

    private (Vector2 A, Vector2 B)? LastEnds => _ends is { } ends ? (ends.A, ends.B) : null;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var (a, b) = (ToChart(canvas, _x1, _y1), ToChart(canvas, _x2, _y2));
        _ends = new Ends(a, b);
        canvas.Clip = canvas.Layout.PlotArea;
        canvas.DrawLine(a, b, Thickness, Color);
        SetBounds(Span(a, b));
        SetHandles(Point(a), Point(b));
        DrawHandles(canvas, [a, b]);
        if (Label is { Length: > 0 } label)
        {
            canvas.DrawLabel(label, b.X + 6, b.Y - canvas.GetLineHeight(canvas.LabelSize) - 2, Color, canvas.BackgroundColor);
        }
    }

    private sealed record Ends(Vector2 A, Vector2 B);
}

/// <summary>An arrow from one place to another.</summary>
/// <param name="xAxis">The horizontal axis, or <see langword="null"/> for chart coordinates.</param>
/// <param name="yAxis">The vertical axis, or <see langword="null"/> for chart coordinates.</param>
public sealed class ArrowAnnotation(AxisBase? xAxis, AxisBase? yAxis) : Annotation(xAxis, yAxis)
{
    private const float HeadLength = 10;
    private const float HeadWidth = 5;

    private double _x1;
    private double _y1;
    private double _x2;
    private double _y2;

    /// <summary>Gets or sets the thickness.</summary>
    public float Thickness { get; set; } = 2;

    /// <summary>Gets the start.</summary>
    public (double X, double Y) Start => (_x1, _y1);

    /// <summary>Gets the tip.</summary>
    public (double X, double Y) Tip => (_x2, _y2);

    /// <summary>Sets both ends.</summary>
    /// <param name="x1">The X value of the start.</param>
    /// <param name="y1">The Y value of the start.</param>
    /// <param name="x2">The X value of the tip.</param>
    /// <param name="y2">The Y value of the tip.</param>
    public void SetEnds(double x1, double y1, double x2, double y2)
    {
        (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => SetEnds(_x1 + deltaX, _y1 + deltaY, _x2 + deltaX, _y2 + deltaY);

    /// <inheritdoc />
    public override bool Resize(double deltaX, double deltaY)
    {
        SetEnds(_x1, _y1, _x2 + deltaX, _y2 + deltaY);
        return true;
    }

    /// <summary>Moves an end: 0 the start, 1 the tip.</summary>
    /// <param name="handle">The number of the handle.</param>
    /// <param name="deltaX">The distance along the X axis.</param>
    /// <param name="deltaY">The distance along the Y axis.</param>
    public override void MoveHandle(int handle, double deltaX, double deltaY)
    {
        if (handle == 0)
        {
            SetEnds(_x1 + deltaX, _y1 + deltaY, _x2, _y2);
        }
        else if (handle == 1)
        {
            SetEnds(_x1, _y1, _x2 + deltaX, _y2 + deltaY);
        }
    }

    /// <inheritdoc />
    public override object? Capture() => (_x1, _y1, _x2, _y2);

    /// <inheritdoc />
    public override void Restore(object state)
    {
        var (x1, y1, x2, y2) = ((double, double, double, double))state;
        SetEnds(x1, y1, x2, y2);
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var start = ToChart(canvas, _x1, _y1);
        var tip = ToChart(canvas, _x2, _y2);
        SetBounds(Span(start, tip));
        SetHandles(Point(start), Point(tip));
        DrawHandles(canvas, [start, tip]);
        var delta = tip - start;
        var length = delta.Length();
        if (length < 1)
        {
            return;
        }

        var direction = delta / length;
        var normal = new Vector2(-direction.Y, direction.X) * HeadWidth;
        var neck = tip - (direction * Math.Min(HeadLength, length));
        canvas.DrawLine(start, neck, Thickness, Color);
        canvas.FillTriangle(tip, neck + normal, neck - normal, Color);
    }
}

/// <summary>A marker at a place.</summary>
/// <param name="xAxis">The horizontal axis, or <see langword="null"/> for chart coordinates.</param>
/// <param name="yAxis">The vertical axis, or <see langword="null"/> for chart coordinates.</param>
public sealed class MarkerAnnotation(AxisBase? xAxis, AxisBase? yAxis) : Annotation(xAxis, yAxis)
{
    private double _x;
    private double _y;

    /// <summary>Gets or sets the size.</summary>
    public float Size { get; set; } = 12;

    /// <summary>Gets the position.</summary>
    public (double X, double Y) Position => (_x, _y);

    /// <summary>Sets the position.</summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    public void MoveTo(double x, double y)
    {
        (_x, _y) = (x, y);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => MoveTo(_x + deltaX, _y + deltaY);

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var center = ToChart(canvas, _x, _y);
        var half = Size / 2;
        var top = center - new Vector2(0, half);
        var right = center + new Vector2(half, 0);
        var bottom = center + new Vector2(0, half);
        var left = center - new Vector2(half, 0);
        canvas.FillTriangle(top, right, bottom, Color);
        canvas.FillTriangle(top, bottom, left, Color);
        SetBounds(new DipRect(center.X - half, center.Y - half, Size, Size));
    }
}

/// <summary>An image at a place, in a fixed size on the screen.</summary>
/// <param name="pixels">The pixels, top row first.</param>
/// <param name="size">The size of the image in pixels.</param>
/// <param name="xAxis">The horizontal axis, or <see langword="null"/> for chart coordinates.</param>
/// <param name="yAxis">The vertical axis, or <see langword="null"/> for chart coordinates.</param>
public sealed class ImageAnnotation(Rgba8[] pixels, PixelSize size, AxisBase? xAxis, AxisBase? yAxis) : Annotation(xAxis, yAxis)
{
    private double _x;
    private double _y;

    /// <summary>Gets the position of the top left corner.</summary>
    public (double X, double Y) Position => (_x, _y);

    /// <summary>Gets or sets the size on the screen in device-independent units, or <see langword="null"/> for one unit per pixel.</summary>
    public (double Width, double Height)? DisplaySize { get; set; }

    /// <summary>Sets the position of the top left corner.</summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    public void MoveTo(double x, double y)
    {
        (_x, _y) = (x, y);
        OnChanged();
    }

    /// <inheritdoc />
    public override void Move(double deltaX, double deltaY) => MoveTo(_x + deltaX, _y + deltaY);

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var corner = ToChart(canvas, _x, _y);
        var (width, height) = DisplaySize ?? (size.Width, size.Height);
        var area = new DipRect(corner.X, corner.Y, width, height);
        canvas.DrawImage(this, pixels, size, area);
        SetBounds(area);
    }
}
