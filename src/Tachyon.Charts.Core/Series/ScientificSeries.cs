using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>Places three shares in a triangle: A at the top, B on the lower left, C on the lower right.</summary>
/// <param name="CenterX">The middle of the base.</param>
/// <param name="Top">The height of the tip.</param>
/// <param name="Side">The length of a side.</param>
public readonly record struct TernaryProjection(double CenterX, double Top, double Side)
{
    private const double OuterShare = 0.82;
    private static readonly double _heightPerSide = Math.Sqrt(3) / 2;

    /// <summary>Gets the height of the triangle.</summary>
    public double Height => Side * _heightPerSide;

    /// <summary>Creates the projection of an area.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <returns>The projection, with room for labels.</returns>
    public static TernaryProjection Fit(double x, double y, double width, double height)
    {
        var side = Math.Min(width, height / _heightPerSide) * OuterShare;
        return new TernaryProjection(x + (width / 2), y + ((height - (side * _heightPerSide)) / 2), side);
    }

    /// <summary>Places three shares. They need not add up to one.</summary>
    /// <param name="a">The share of the top corner.</param>
    /// <param name="b">The share of the left corner.</param>
    /// <param name="c">The share of the right corner.</param>
    /// <returns>The position, or NaN without a positive sum.</returns>
    public Vector2 ToPoint(double a, double b, double c)
    {
        var sum = a + b + c;
        if (!(sum > 0) || !double.IsFinite(sum))
        {
            return new Vector2(float.NaN);
        }

        return new Vector2((float)(CenterX + ((c - b) / sum * Side / 2)), (float)(Top + ((1 - (a / sum)) * Height)));
    }
}

/// <summary>Points in a ternary diagram, for mixtures of three parts.</summary>
public sealed class TernarySeries : MarkSeries
{
    private readonly double[] _a;
    private readonly double[] _b;
    private readonly double[] _c;

    /// <summary>Initializes a new instance of the <see cref="TernarySeries"/> class.</summary>
    /// <param name="a">The shares of the top corner.</param>
    /// <param name="b">The shares of the left corner.</param>
    /// <param name="c">The shares of the right corner.</param>
    /// <param name="xAxis">The hidden horizontal axis.</param>
    /// <param name="yAxis">The hidden vertical axis.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public TernarySeries(double[] a, double[] b, double[] c, AxisBase xAxis, AxisBase yAxis)
        : base(new DataSource<double, double>(), xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ArgumentNullException.ThrowIfNull(c);
        if (a.Length != b.Length || a.Length != c.Length)
        {
            throw new ArgumentException("All parts need the same number of values.", nameof(c));
        }

        _a = a;
        _b = b;
        _c = c;
    }

    /// <inheritdoc />
    /// <remarks>A point has three shares and no single value, so a ternary chart writes none.</remarks>
    protected internal override void CollectValueLabels(DoubleRange x, DoubleRange y, List<ValueLabel> labels)
    {
    }

    /// <summary>Gets or sets the diameter of the points in device-independent units.</summary>
    public float PointSize { get; set; } = 7;

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette) => Color = palette[0];

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = new DoubleRange(0, 1);
        y = new DoubleRange(0, 1);
        return false;
    }

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        ClearHeads();
        var projection = TernaryProjection.Fit(frame.Bounds.X, frame.Bounds.Y, frame.Bounds.Width, frame.Bounds.Height);
        for (var i = 0; i < _a.Length; i++)
        {
            var point = projection.ToPoint(_a[i], _b[i], _c[i]);
            if (float.IsFinite(point.X) && float.IsFinite(point.Y))
            {
                AddHead(point, (float)(PointSize * frame.DpiScale));
            }
        }
    }
}

/// <summary>The triangle of a ternary diagram with lines every 20 percent.</summary>
/// <param name="labelA">The name of the top corner.</param>
/// <param name="labelB">The name of the left corner.</param>
/// <param name="labelC">The name of the right corner.</param>
public sealed class TernaryGridOverlay(string labelA, string labelB, string labelC) : ChartOverlay
{
    private const int Divisions = 5;
    private const float LabelSize = 12;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        var projection = TernaryProjection.Fit(pane.X, pane.Y, pane.Width, pane.Height);
        var line = canvas.TextColor with { A = 60 };
        for (var i = 1; i < Divisions; i++)
        {
            var share = i / (double)Divisions;
            var rest = 1 - share;
            canvas.DrawLine(projection.ToPoint(share, rest, 0), projection.ToPoint(share, 0, rest), 1, line);
            canvas.DrawLine(projection.ToPoint(rest, share, 0), projection.ToPoint(0, share, rest), 1, line);
            canvas.DrawLine(projection.ToPoint(rest, 0, share), projection.ToPoint(0, rest, share), 1, line);
            var text = (share * 100).ToString("F0", CultureInfo.InvariantCulture);
            var mark = projection.ToPoint(share, 0, rest);
            canvas.DrawText(text, MathF.Round(mark.X + 6), MathF.Round(mark.Y + 4), LabelSize * 0.9f, canvas.TextColor);
        }

        var top = projection.ToPoint(1, 0, 0);
        var left = projection.ToPoint(0, 1, 0);
        var right = projection.ToPoint(0, 0, 1);
        var edge = canvas.TextColor with { A = 200 };
        canvas.DrawLine(top, left, 1, edge);
        canvas.DrawLine(left, right, 1, edge);
        canvas.DrawLine(right, top, 1, edge);
        canvas.DrawText(labelA, MathF.Round(top.X - (canvas.MeasureWidth(labelA, LabelSize) / 2)), MathF.Round(top.Y - 6), LabelSize, canvas.TextColor);
        canvas.DrawText(labelB, MathF.Round(left.X - (canvas.MeasureWidth(labelB, LabelSize) / 2)), MathF.Round(left.Y + LabelSize + 4), LabelSize, canvas.TextColor);
        canvas.DrawText(labelC, MathF.Round(right.X - (canvas.MeasureWidth(labelC, LabelSize) / 2)), MathF.Round(right.Y + LabelSize + 4), LabelSize, canvas.TextColor);
    }
}

/// <summary>
/// Many rows over several measures: each row is a line across parallel axes, each scaled to its own range.
/// </summary>
public sealed class ParallelCoordinatesSeries : ChartSeries, IDisposable
{
    private readonly double[][] _columns;
    private readonly DoubleRange[] _ranges;
    private readonly int _rows;
    private int[] _order;
    private Vector2[] _points = [];
    private readonly DoubleRange?[] _filters;
    private IGpuBuffer? _buffer;
    private const int DenseRows = 2000;
    private const int MinimumAlpha = 4;

    private Rgba8 _color = new(80, 170, 240, 40);
    private long _version;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private int _count;

    /// <summary>Initializes a new instance of the <see cref="ParallelCoordinatesSeries"/> class.</summary>
    /// <param name="columns">The values of each measure.</param>
    /// <param name="xAxis">The hidden horizontal axis.</param>
    /// <param name="yAxis">The hidden vertical axis.</param>
    /// <exception cref="ArgumentException">There are fewer than two measures, or their lengths differ.</exception>
    public ParallelCoordinatesSeries(double[][] columns, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length < 2)
        {
            throw new ArgumentException("At least two measures are needed.", nameof(columns));
        }

        _rows = columns[0].Length;

        // Many rows must not merge into one area: the density has to stay visible.
        _color = _color with { A = (byte)Math.Clamp(_color.A * DenseRows / Math.Max(_rows, 1), MinimumAlpha, _color.A) };
        if (Array.Exists(columns, column => column.Length != _rows))
        {
            throw new ArgumentException("All measures need the same number of rows.", nameof(columns));
        }

        _columns = columns;
        _ranges = Array.ConvertAll(columns, FindRange);
        _order = [.. Enumerable.Range(0, columns.Length)];
        _filters = new DoubleRange?[columns.Length];
    }

    /// <summary>Gets the order of the measures from left to right.</summary>
    public IReadOnlyList<int> Order => _order;

    /// <summary>Gets or sets the color. Use a low alpha for many rows.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Interlocked.Increment(ref _version);
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color with { A = 255 };

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette) => Color = palette[0] with { A = _color.A };

    /// <inheritdoc />
    protected internal override long Version => Volatile.Read(ref _version);

    /// <summary>Gets the range of a measure.</summary>
    /// <param name="column">The measure.</param>
    /// <returns>The range.</returns>
    public DoubleRange GetRange(int column) => _ranges[column];

    /// <summary>Gets the filter of a measure.</summary>
    /// <param name="column">The measure.</param>
    /// <returns>The range, or <see langword="null"/> for all rows.</returns>
    public DoubleRange? GetFilter(int column) => _filters[column];

    /// <summary>Shows all rows again.</summary>
    public void ClearFilters()
    {
        Array.Clear(_filters);
        Interlocked.Increment(ref _version);
    }

    /// <summary>Moves a measure to another place.</summary>
    /// <param name="from">The place from the left.</param>
    /// <param name="to">The new place from the left.</param>
    public void MoveAxis(int from, int to)
    {
        var order = _order.ToList();
        var column = order[from];
        order.RemoveAt(from);
        order.Insert(to, column);
        _order = [.. order];
        Interlocked.Increment(ref _version);
    }

    /// <summary>Shows only rows inside a range of a measure.</summary>
    /// <param name="column">The measure.</param>
    /// <param name="range">The range, or <see langword="null"/> for all.</param>
    public void SetFilter(int column, DoubleRange? range)
    {
        _filters[column] = range;
        Interlocked.Increment(ref _version);
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
        x = new DoubleRange(0, 1);
        y = new DoubleRange(0, 1);
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

        BuildPoints(frame);
        if (_count >= 2)
        {
            var size = _count * 8L;
            if (_buffer is null || _buffer.SizeInBytes < size)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(size, BufferUsage.Vertex));
            }

            _buffer.Write<Vector2>(0, _points.AsSpan(0, _count));
        }

        _builtVersion = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (_buffer is not null && _count >= 2)
        {
            commands.DrawPolyline(_buffer, 0, _count, new PolylineStyle((float)frame.DpiScale, _color) { Clip = frame.Bounds });
        }
    }

    private static DoubleRange FindRange(double[] column)
    {
        var range = DoubleRange.Empty;
        foreach (var value in column)
        {
            if (double.IsFinite(value))
            {
                range = range.Union(new DoubleRange(value, value));
            }
        }

        return range;
    }

    private void BuildPoints(in SeriesFrame frame)
    {
        var axes = _order.Length;
        var needed = _rows * (axes + 1);
        if (_points.Length < needed)
        {
            _points = new Vector2[needed];
        }

        var bounds = frame.Bounds;
        var width = bounds.Width / (axes - 1);
        _count = 0;
        for (var row = 0; row < _rows; row++)
        {
            if (!IsShown(row))
            {
                continue;
            }

            for (var place = 0; place < axes; place++)
            {
                var column = _order[place];
                var range = _ranges[column];
                var share = range.Max > range.Min ? (_columns[column][row] - range.Min) / (range.Max - range.Min) : 0.5;
                _points[_count++] = double.IsFinite(share)
                    ? new Vector2(bounds.X + (place * width), bounds.Y + bounds.Height - ((float)share * bounds.Height))
                    : new Vector2(PolylineStyle.Gap, 0);
            }

            _points[_count++] = new Vector2(PolylineStyle.Gap, 0);
        }
    }

    private bool IsShown(int row)
    {
        for (var column = 0; column < _filters.Length; column++)
        {
            if (_filters[column] is { } filter && !(_columns[column][row] >= filter.Min && _columns[column][row] <= filter.Max))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>The axes of parallel coordinates with their names and ranges.</summary>
/// <param name="series">The rows.</param>
/// <param name="names">The names of the measures.</param>
public sealed class ParallelAxesOverlay(ParallelCoordinatesSeries series, IReadOnlyList<string> names) : ChartOverlay
{
    private const float LabelSize = 11;

    /// <inheritdoc />
    protected internal override long ExternalVersion => series.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        var order = series.Order;
        var width = pane.Width / (order.Count - 1);
        var line = canvas.TextColor with { A = 160 };
        for (var place = 0; place < order.Count; place++)
        {
            var column = order[place];
            var x = (float)(pane.X + (place * width));
            canvas.DrawLine(new Vector2(x, (float)pane.Y), new Vector2(x, (float)pane.Bottom), 1, line);
            var range = series.GetRange(column);
            if (series.GetFilter(column) is { } filter)
            {
                var top = pane.Y + ((range.Max - filter.Max) / (range.Max - range.Min) * pane.Height);
                var bottom = pane.Y + ((range.Max - filter.Min) / (range.Max - range.Min) * pane.Height);
                canvas.FillRectangle(new Layout.DipRect(x - 4, top, 8, bottom - top), canvas.AccentColor with { A = 140 });
            }

            var name = column < names.Count ? names[column] : string.Empty;
            Label(canvas, name, x, pane.Y + LabelSize, pane.X, pane.Right);
            Label(canvas, Instruments.ScopeFormat.Value(range.Max, string.Empty), x, pane.Y + (LabelSize * 2.3), pane.X, pane.Right);
            Label(canvas, Instruments.ScopeFormat.Value(range.Min, string.Empty), x, pane.Bottom - 4, pane.X, pane.Right);
        }
    }

    private static void Label(OverlayCanvas canvas, string text, double x, double baseline, double left, double right)
    {
        var width = canvas.MeasureWidth(text, LabelSize);
        canvas.DrawText(text, Math.Round(Math.Clamp(x + 4, left, right - width)), Math.Round(baseline), LabelSize, canvas.TextColor);
    }
}

/// <summary>Describes charts for scientific evaluations.</summary>
public static class ScientificCharts
{
    /// <summary>Describes a ternary diagram.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="a">The shares of the top corner.</param>
    /// <param name="b">The shares of the left corner.</param>
    /// <param name="c">The shares of the right corner.</param>
    /// <param name="names">The names of the corners: top, left, right.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Ternary(this ChartBuilder builder, double[] a, double[] b, double[] c, (string Top, string Left, string Right) names)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .XAxis(axis => axis.Numeric("x").Range(0, 1).Hidden())
            .YAxis(axis => axis.Numeric("y").Range(0, 1).Hidden())
            .AddSeries((x, y) => new TernarySeries(a, b, c, x, y))
            .AddOverlay(_ => new TernaryGridOverlay(names.Top, names.Left, names.Right));
    }

    /// <summary>Describes parallel coordinates.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="columns">The values of each measure.</param>
    /// <param name="names">The names of the measures.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder ParallelCoordinates(this ChartBuilder builder, double[][] columns, IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .XAxis(axis => axis.Numeric("x").Range(0, 1).Hidden())
            .YAxis(axis => axis.Numeric("y").Range(0, 1).Hidden())
            .AddSeries((x, y) => new ParallelCoordinatesSeries(columns, x, y))
            .AddOverlay(chart => new ParallelAxesOverlay(chart.Series.OfType<ParallelCoordinatesSeries>().First(), names))
            .Modifiers(m => m.Custom(() => new Instruments.ParallelAxesModifier()));
    }
}
