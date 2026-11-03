using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>How several rows of values share a place.</summary>
public enum BarLayout
{
    /// <summary>Next to each other.</summary>
    Grouped,

    /// <summary>On top of each other.</summary>
    Stacked,

    /// <summary>On top of each other, adding up to 100.</summary>
    Percent,
}

/// <summary>
/// Bars or columns. Narrower than a pixel they merge to one bar per pixel, which keeps peaks visible
/// and a million bars fast.
/// </summary>
public sealed class BarSeries : ChartSeries
{
    private readonly List<MeshVertex> _mesh = [];
    private double[][] _rows;
    private double[]? _positions;
    private BarLayout _layout;
    private bool _isHorizontal;
    private double _barWidth = 0.8;
    private double _cornerRadius;
    private IReadOnlyList<SeriesFill> _fills;
    private Rgba8? _shadowColor;
    private double _shadowBlur = 6;
    private long _version;
    private MeshVertex[] _vertices = [];
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="BarSeries"/> class.</summary>
    /// <param name="rows">The rows of values, one value per place.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    /// <exception cref="ArgumentException">There is no row, or the rows differ in length.</exception>
    public BarSeries(IReadOnlyList<double[]> rows, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _rows = Validate(rows);
        _fills = [.. rows.Select((_, index) => (SeriesFill)new SolidFill(DefaultColor(index)))];
    }

    /// <summary>Gets or sets the places, rising and evenly spaced, or <see langword="null"/> for 0, 1, 2 and so on.</summary>
    public IReadOnlyList<double>? Positions
    {
        get => _positions;
        set
        {
            _positions = value?.ToArray();
            Touch();
        }
    }

    /// <summary>Gets or sets how the rows share a place.</summary>
    public BarLayout Layout
    {
        get => _layout;
        set
        {
            _layout = value;
            Touch();
        }
    }

    /// <summary>Gets or sets a value indicating whether the bars run along the X axis, with their places on the Y axis.</summary>
    public bool IsHorizontal
    {
        get => _isHorizontal;
        set
        {
            _isHorizontal = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the share of a place the bars fill, from 0 to 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside of this range.</exception>
    public double BarWidth
    {
        get => _barWidth;
        set
        {
            if (!(value > 0 && value <= 1))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The share must be above 0 and at most 1.");
            }

            _barWidth = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the radius of the corners in device-independent units.</summary>
    public double CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(value, 0);
            Touch();
        }
    }

    /// <summary>Gets or sets the color of a soft shadow, or <see langword="null"/> for none.</summary>
    public Rgba8? ShadowColor
    {
        get => _shadowColor;
        set
        {
            _shadowColor = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the width of the soft edge of the shadow in device-independent units.</summary>
    public double ShadowBlur
    {
        get => _shadowBlur;
        set
        {
            _shadowBlur = Math.Max(value, 0);
            Touch();
        }
    }

    /// <summary>Gets or sets the fills of the rows.</summary>
    /// <exception cref="ArgumentException">The number does not match the rows.</exception>
    public IReadOnlyList<SeriesFill> Fills
    {
        get => _fills;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count != _rows.Length)
            {
                throw new ArgumentException("Every row needs a fill.", nameof(value));
            }

            _fills = value;
            Touch();
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _fills[0].GetColor(0, 0);

    /// <inheritdoc />
    protected internal override long Version => Volatile.Read(ref _version);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _vertices.Length / 6;

    /// <summary>Creates the bars of a histogram.</summary>
    /// <param name="histogram">The histogram.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    /// <returns>Bars that touch each other.</returns>
    public static BarSeries FromHistogram(Histogram histogram, AxisBase xAxis, AxisBase yAxis)
    {
        ArgumentNullException.ThrowIfNull(histogram);
        var positions = new double[histogram.Counts.Length];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = (histogram.Edges[i] + histogram.Edges[i + 1]) / 2;
        }

        return new BarSeries([Array.ConvertAll(histogram.Counts, count => (double)count)], xAxis, yAxis) { Positions = positions, BarWidth = 1 };
    }

    /// <summary>Replaces the values.</summary>
    /// <param name="rows">The rows, as many as before.</param>
    /// <exception cref="ArgumentException">The number of rows changed, or they differ in length.</exception>
    public void SetValues(IReadOnlyList<double[]> rows)
    {
        var valid = Validate(rows);
        if (valid.Length != _rows.Length)
        {
            throw new ArgumentException("The number of rows must stay the same.", nameof(rows));
        }

        _rows = valid;
        Touch();
    }

    /// <summary>Finds where a row of bars starts and ends at each place.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="layout">How they share a place.</param>
    /// <returns>The lower and upper end per row. NaN counts as zero in a stack and as no bar otherwise.</returns>
    public static (double[][] Lower, double[][] Upper) ComputeEnds(IReadOnlyList<double[]> rows, BarLayout layout)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var count = rows.Count == 0 ? 0 : rows[0].Length;
        var lower = new double[rows.Count][];
        var upper = new double[rows.Count][];
        for (var row = 0; row < rows.Count; row++)
        {
            lower[row] = new double[count];
            upper[row] = new double[count];
        }

        for (var i = 0; i < count; i++)
        {
            FillPlace(rows, layout, i, lower, upper);
        }

        return (lower, upper);
    }

    private static void FillPlace(IReadOnlyList<double[]> rows, BarLayout layout, int i, double[][] lower, double[][] upper)
    {
        var above = 0.0;
        var below = 0.0;
        for (var row = 0; row < rows.Count; row++)
        {
            var value = rows[row][i];
            if (layout == BarLayout.Grouped)
            {
                upper[row][i] = value;
            }
            else
            {
                var amount = double.IsFinite(value) ? value : 0;

                // Negative values stack downwards from zero, each side on its own.
                ref var edge = ref amount >= 0 ? ref above : ref below;
                lower[row][i] = edge;
                edge += amount;
                upper[row][i] = edge;
            }
        }

        if (layout == BarLayout.Percent && above - below != 0)
        {
            var factor = 100 / (above - below);
            for (var row = 0; row < rows.Count; row++)
            {
                lower[row][i] *= factor;
                upper[row][i] *= factor;
            }
        }
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var count = _rows[0].Length;
        var (lower, upper) = ComputeEnds(_rows, _layout);
        var values = new DoubleRange(0, 0);
        for (var row = 0; row < _rows.Length; row++)
        {
            for (var i = 0; i < count; i++)
            {
                if (double.IsFinite(upper[row][i]))
                {
                    values = values.Union(new DoubleRange(Math.Min(lower[row][i], upper[row][i]), Math.Max(lower[row][i], upper[row][i])));
                }
            }
        }

        var step = Step();
        var places = count == 0 ? DoubleRange.Empty : new DoubleRange(Position(0) - (step / 2), Position(count - 1) + (step / 2));
        x = _isHorizontal ? values : places;
        y = _isHorizontal ? places : values;
        return count > 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (_builtVersion == version && _builtFrame == frame)
        {
            return;
        }

        _mesh.Clear();
        Build(frame);
        _vertices = _mesh.Count == _vertices.Length ? _vertices : new MeshVertex[_mesh.Count];
        _mesh.CopyTo(_vertices);
        _builtVersion = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) =>
        commands.FillMesh(_vertices, frame.Bounds);

    private static double[][] Validate(IReadOnlyList<double[]> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0 || rows.Any(row => row.Length != rows[0].Length))
        {
            throw new ArgumentException("There must be rows of equal length.", nameof(rows));
        }

        return [.. rows];
    }

    private static Rgba8 DefaultColor(int index)
    {
        ReadOnlySpan<Rgba8> colors = [new(76, 201, 240, 255), new(247, 37, 133, 255), new(124, 252, 154, 255), new(255, 183, 3, 255), new(179, 136, 255, 255)];
        return colors[index % colors.Length];
    }

    private void Touch() => Interlocked.Increment(ref _version);

    private double Position(int index) => _positions is { } positions ? positions[index] : index;

    private double Step() => _rows[0].Length < 2 ? 1 : (Position(_rows[0].Length - 1) - Position(0)) / (_rows[0].Length - 1);

    private void Build(in SeriesFrame frame)
    {
        var count = _rows[0].Length;
        if (count == 0)
        {
            return;
        }

        var places = _isHorizontal ? frame.Y : frame.X;
        var values = _isHorizontal ? frame.X : frame.Y;
        var step = Step();
        var pixelsPerPlace = Math.Abs(places.ToPixel(step) - places.ToPixel(0));
        var (lower, upper) = ComputeEnds(_rows, _layout);

        // Only the rows that are visible, by the even spacing of the places.
        var visibleStart = Math.Min(places.ToData(_isHorizontal ? frame.Bounds.Y : frame.Bounds.X), places.ToData(_isHorizontal ? frame.Bounds.Y + frame.Bounds.Height : frame.Bounds.X + frame.Bounds.Width));
        var visibleEnd = Math.Max(places.ToData(_isHorizontal ? frame.Bounds.Y : frame.Bounds.X), places.ToData(_isHorizontal ? frame.Bounds.Y + frame.Bounds.Height : frame.Bounds.X + frame.Bounds.Width));
        var first = Math.Clamp((int)Math.Floor(((visibleStart - Position(0)) / step) - 1), 0, count - 1);
        var last = Math.Clamp((int)Math.Ceiling(((visibleEnd - Position(0)) / step) + 1), 0, count - 1);

        if (pixelsPerPlace < 1)
        {
            BuildMerged(frame, places, values, lower, upper, first, last);
            return;
        }

        AddBars(frame, places, values, lower, upper, first, last, pixelsPerPlace);
    }

    private void AddBars(in SeriesFrame frame, in LinearAxisTransform places, in LinearAxisTransform values, double[][] lower, double[][] upper, int first, int last, double pixelsPerPlace)
    {
        var isGrouped = _layout == BarLayout.Grouped;
        var slots = isGrouped ? _rows.Length : 1;
        var width = pixelsPerPlace * _barWidth;
        var slotWidth = width / slots;
        for (var i = first; i <= last; i++)
        {
            var center = places.ToPixel(Position(i));
            for (var row = 0; row < _rows.Length; row++)
            {
                if (double.IsFinite(upper[row][i]))
                {
                    var start = Math.Round(center - (width / 2) + (isGrouped ? row * slotWidth : 0));
                    var end = Math.Round(center - (width / 2) + ((isGrouped ? row + 1 : 1) * slotWidth));
                    AddBar(frame, row, start, end, Math.Round(values.ToPixel(lower[row][i])), Math.Round(values.ToPixel(upper[row][i])), pixelsPerPlace, upper[row][i]);
                }
            }
        }
    }

    private void AddBar(in SeriesFrame frame, int row, double start, double end, double from, double to, double pixelsPerPlace, double value)
    {
        var radius = (float)(_cornerRadius * frame.DpiScale);
        var blur = (float)(_shadowBlur * frame.DpiScale);
        var rectangle = _isHorizontal
            ? new Bar((float)Math.Min(from, to), (float)start, (float)Math.Abs(to - from), (float)(end - start))
            : new Bar((float)start, (float)Math.Min(from, to), (float)(end - start), (float)Math.Abs(to - from));
        if (_shadowColor is { } shadow && !isDense(pixelsPerPlace))
        {
            MeshShapes.AddSoftRectangle(_mesh, rectangle.X + (blur / 3), rectangle.Y + (blur / 3), rectangle.Width, rectangle.Height, blur, shadow);
        }

        MeshShapes.AddRoundedRectangle(_mesh, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, radius, frame.Bounds, _fills[row], value);

        static bool isDense(double pixels) => pixels < 3;
    }

    // One bar per pixel and row, from the lowest to the highest end in that pixel.
    private void BuildMerged(in SeriesFrame frame, in LinearAxisTransform places, in LinearAxisTransform values, double[][] lower, double[][] upper, int first, int last)
    {
        var origin = _isHorizontal ? frame.Bounds.Y : frame.Bounds.X;
        var length = (int)(_isHorizontal ? frame.Bounds.Height : frame.Bounds.Width);
        if (length <= 0)
        {
            return;
        }

        var low = new double[length];
        var high = new double[length];
        for (var row = 0; row < _rows.Length; row++)
        {
            Array.Fill(low, double.PositiveInfinity);
            Array.Fill(high, double.NegativeInfinity);
            for (var i = first; i <= last; i++)
            {
                var column = (int)(places.ToPixel(Position(i)) - origin);
                if (column >= 0 && column < length && double.IsFinite(upper[row][i]))
                {
                    low[column] = Math.Min(low[column], Math.Min(lower[row][i], upper[row][i]));
                    high[column] = Math.Max(high[column], Math.Max(lower[row][i], upper[row][i]));
                }
            }

            AddMergedColumns(frame, values, low, high, row, origin);
        }
    }

    private void AddMergedColumns(in SeriesFrame frame, in LinearAxisTransform values, double[] low, double[] high, int row, float origin)
    {
        for (var column = 0; column < low.Length; column++)
        {
            if (low[column] > high[column])
            {
                continue;
            }

            var from = (float)Math.Round(values.ToPixel(low[column]));
            var to = (float)Math.Round(values.ToPixel(high[column]));
            var start = origin + column;
            var rectangle = _isHorizontal
                ? new Bar(Math.Min(from, to), start, Math.Max(Math.Abs(to - from), 1), 1)
                : new Bar(start, Math.Min(from, to), 1, Math.Max(Math.Abs(to - from), 1));
            MeshShapes.AddRoundedRectangle(_mesh, rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, 0, frame.Bounds, _fills[row], high[column]);
        }
    }

    private readonly record struct Bar(float X, float Y, float Width, float Height);
}

/// <summary>
/// Shapes made of triangles for <see cref="ICommandList.FillMesh"/>.
/// </summary>
public static class MeshShapes
{
    private const int CornerSegments = 6;

    /// <summary>Adds a rectangle with rounded corners.</summary>
    /// <param name="mesh">Receives the triangles.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="radius">The radius of the corners, limited by the size.</param>
    /// <param name="pane">The pane, for fills by position.</param>
    /// <param name="fill">The fill.</param>
    /// <param name="value">The data value of the shape.</param>
    public static void AddRoundedRectangle(List<MeshVertex> mesh, float x, float y, float width, float height, float radius, PixelRect pane, SeriesFill fill, double value)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(fill);
        var limited = Math.Min(radius, Math.Min(width, height) / 2);
        var center = Vertex(x + (width / 2), y + (height / 2), pane, fill, value);
        if (limited < 0.5f)
        {
            Quad(mesh, Vertex(x, y, pane, fill, value), Vertex(x + width, y, pane, fill, value), Vertex(x + width, y + height, pane, fill, value), Vertex(x, y + height, pane, fill, value));
            return;
        }

        // A fan around the middle, corner by corner.
        var previous = default(MeshVertex);
        var hasPrevious = false;
        var firstVertex = default(MeshVertex);
        for (var corner = 0; corner < 4; corner++)
        {
            var cornerX = corner is 1 or 2 ? x + width - limited : x + limited;
            var cornerY = corner is 2 or 3 ? y + height - limited : y + limited;
            for (var step = 0; step <= CornerSegments; step++)
            {
                var angle = (MathF.PI * (corner + 2) / 2) + (step * MathF.PI / 2 / CornerSegments);
                var vertex = Vertex(cornerX + (MathF.Cos(angle) * limited), cornerY + (MathF.Sin(angle) * limited), pane, fill, value);
                if (hasPrevious)
                {
                    mesh.Add(center);
                    mesh.Add(previous);
                    mesh.Add(vertex);
                }
                else
                {
                    firstVertex = vertex;
                    hasPrevious = true;
                }

                previous = vertex;
            }
        }

        mesh.Add(center);
        mesh.Add(previous);
        mesh.Add(firstVertex);
    }

    /// <summary>Adds a rectangle whose edges fade out, as a soft shadow.</summary>
    /// <param name="mesh">Receives the triangles.</param>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="blur">The width of the fading edge.</param>
    /// <param name="color">The color inside.</param>
    public static void AddSoftRectangle(List<MeshVertex> mesh, float x, float y, float width, float height, float blur, Rgba8 color)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var outer = color with { A = 0 };
        var right = x + width;
        var bottom = y + height;
        Quad(mesh, new(new Vector2(x, y), color), new(new Vector2(right, y), color), new(new Vector2(right, bottom), color), new(new Vector2(x, bottom), color));
        Quad(mesh, new(new Vector2(x, y - blur), outer), new(new Vector2(right, y - blur), outer), new(new Vector2(right, y), color), new(new Vector2(x, y), color));
        Quad(mesh, new(new Vector2(x, bottom), color), new(new Vector2(right, bottom), color), new(new Vector2(right, bottom + blur), outer), new(new Vector2(x, bottom + blur), outer));
        Quad(mesh, new(new Vector2(x - blur, y), outer), new(new Vector2(x, y), color), new(new Vector2(x, bottom), color), new(new Vector2(x - blur, bottom), outer));
        Quad(mesh, new(new Vector2(right, y), color), new(new Vector2(right + blur, y), outer), new(new Vector2(right + blur, bottom), outer), new(new Vector2(right, bottom), color));
        Corner(mesh, new Vector2(x, y), new Vector2(x - blur, y), new Vector2(x, y - blur), color, outer);
        Corner(mesh, new Vector2(right, y), new Vector2(right, y - blur), new Vector2(right + blur, y), color, outer);
        Corner(mesh, new Vector2(right, bottom), new Vector2(right + blur, bottom), new Vector2(right, bottom + blur), color, outer);
        Corner(mesh, new Vector2(x, bottom), new Vector2(x, bottom + blur), new Vector2(x - blur, bottom), color, outer);
    }

    private static void Corner(List<MeshVertex> mesh, Vector2 center, Vector2 start, Vector2 end, Rgba8 color, Rgba8 outer)
    {
        mesh.Add(new MeshVertex(center, color));
        mesh.Add(new MeshVertex(start, outer));
        mesh.Add(new MeshVertex(end, outer));
    }

    private static MeshVertex Vertex(float x, float y, PixelRect pane, SeriesFill fill, double value) =>
        new(new Vector2(x, y), fill.GetColor((y - pane.Y) / pane.Height, value));

    private static void Quad(List<MeshVertex> mesh, MeshVertex a, MeshVertex b, MeshVertex c, MeshVertex d)
    {
        mesh.Add(a);
        mesh.Add(b);
        mesh.Add(c);
        mesh.Add(a);
        mesh.Add(c);
        mesh.Add(d);
    }
}
