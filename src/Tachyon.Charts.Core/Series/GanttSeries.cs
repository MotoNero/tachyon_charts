using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// The bars of a Gantt chart over a time axis, one line per row and per group. Only visible lines and tasks are
/// drawn; tasks narrower than a pixel that touch merge into one bar, so 100 000 tasks stay fluid. A collapsed group
/// shows one bar with the span of all its tasks. The Y axis is a <see cref="CategoryAxis"/> that the series fills
/// with the names of the visible lines, from the top.
/// </summary>
public sealed class GanttSeries : ChartSeries
{
    private const float MilestoneRadius = 6;

    private readonly GanttData _data;
    private readonly List<MeshVertex> _mesh = [];
    private Rgba8[] _palette = [new(76, 201, 240, 255), new(247, 37, 133, 255), new(255, 190, 11, 255), new(6, 214, 160, 255), new(131, 56, 236, 255)];
    private double _barHeight = 0.6;
    private int _hovered = -1;
    private long _appearance;
    private long _built = -1;
    private long _linesVersion = -1;
    private SeriesFrame _builtFrame;

    /// <summary>Initializes a new instance of the <see cref="GanttSeries"/> class.</summary>
    /// <param name="data">The rows and tasks.</param>
    /// <param name="xAxis">The time axis.</param>
    /// <param name="yAxis">The axis of the lines, usually a <see cref="CategoryAxis"/>.</param>
    public GanttSeries(GanttData data, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis) => _data = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets the rows and tasks.</summary>
    public GanttData Data => _data;

    /// <summary>Gets or sets the height of a bar as a share of its line.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not above 0 and at most 1.</exception>
    public double BarHeight
    {
        get => _barHeight;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1);
            _barHeight = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the task under the pointer, or -1 for none. <see cref="GanttModifier"/> sets it.</summary>
    public int HoveredTask
    {
        get => Volatile.Read(ref _hovered);
        set
        {
            if (Interlocked.Exchange(ref _hovered, value) != value)
            {
                Touch();
            }
        }
    }

    /// <inheritdoc />
    public override Rgba8 AccentColor => _palette[0];

    /// <inheritdoc />
    protected internal override long Version => _data.Version + Interlocked.Read(ref _appearance);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _mesh.Count / 6;

    /// <inheritdoc />
    protected internal override long SourcePointCount => _data.Count;

    internal int VertexCount => _mesh.Count;

    /// <summary>Gets the names of the lines from the top, as the Y axis shows them: groups with + or −, rows indented.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> LineNames()
    {
        var lines = _data.Current.Lines;
        var names = new string[lines.Length];
        for (var i = 0; i < lines.Length; i++)
        {
            names[i] = NameOf(lines[i]);
        }

        return names;
    }

    // Groups show whether they are open; tasks of a group stand indented.
    private static string NameOf(GanttData.Line line)
    {
        if (line.IsGroup)
        {
            return (line.IsCollapsed ? "+ " : "− ") + line.Name;
        }

        return line.Group is null ? line.Name : "   " + line.Name;
    }

    internal Rgba8 ColorOf(in GanttTask task) => task.Color.A > 0 ? task.Color : _palette[task.Row % _palette.Length];

    /// <summary>Finds the line at a vertical position in the units of the layout.</summary>
    internal int FindLine(ChartLayoutResult layout, double y)
    {
        if (!TryTransforms(layout, out _, out var lineAxis))
        {
            return -1;
        }

        var line = (int)Math.Round(lineAxis.ToData(y));
        return line >= 0 && line < _data.Current.Lines.Length ? line : -1;
    }

    /// <summary>Finds the task under a position in the units of the layout, the latest on top.</summary>
    internal int FindTask(ChartLayoutResult layout, Vector2 position)
    {
        var line = FindLine(layout, position.Y);
        if (line < 0 || !TryTransforms(layout, out var time, out var lineAxis))
        {
            return -1;
        }

        var state = _data.Current;
        var entry = state.Lines[line];
        if (entry.IsGroup || Math.Abs(lineAxis.ToData(position.Y) - line) > _barHeight / 2)
        {
            return -1;
        }

        var origin = CandleColumns.OriginOf(XAxis);
        var at = time.ToData(position.X) + origin;
        var reach = Math.Abs(time.ToData(position.X + MilestoneRadius) - time.ToData(position.X));
        var tasks = state.ByRow[entry.Row];
        var found = -1;
        for (var i = state.FirstVisible(entry.Row, at - reach); i < tasks.Length && state.Tasks[tasks[i]].Start <= at + reach; i++)
        {
            var task = state.Tasks[tasks[i]];
            var (start, end) = task.IsMilestone ? (task.Start - reach, task.Start + reach) : (task.Start, task.End);
            if (at >= start && at <= end)
            {
                found = tasks[i];
            }
        }

        return found;
    }

    /// <inheritdoc />
    protected internal override void OnPalette(ReadOnlySpan<Rgba8> palette)
    {
        _palette = palette.ToArray();
        Touch();
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var state = _data.Current;
        var (start, end) = (double.PositiveInfinity, double.NegativeInfinity);
        foreach (var task in state.Tasks)
        {
            start = Math.Min(start, task.Start);
            end = Math.Max(end, task.End);
        }

        var origin = CandleColumns.OriginOf(XAxis);
        // Some room on both sides, so that the first and last bar and a milestone at the end show whole.
        var room = Math.Max((end - start) * 0.03, 1);
        x = double.IsFinite(start) ? new DoubleRange(start - origin - room, end - origin + room) : new DoubleRange(0, 1);
        y = new DoubleRange(-0.5, Math.Max(state.Lines.Length - 0.5, 0.5));
        return state.Tasks.Length > 0;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _built && frame == _builtFrame)
        {
            return;
        }

        var state = _data.Current;
        if (state.Version != _linesVersion && YAxis is CategoryAxis axis)
        {
            // New or collapsed lines: the axis shows their names; the visible range stays where it was.
            var range = axis.VisibleRange;
            var isSameCount = axis.Categories.Count == state.Lines.Length;
            axis.Categories = LineNames();
            if (isSameCount)
            {
                axis.VisibleRange = range;
            }

            _linesVersion = state.Version;
        }

        _mesh.Clear();
        var bounds = frame.Bounds;
        var (top, bottom) = (frame.Y.ToData(bounds.Y), frame.Y.ToData(bounds.Y + bounds.Height));
        var first = Math.Max(0, (int)Math.Floor(Math.Min(top, bottom)));
        var last = Math.Min(state.Lines.Length - 1, (int)Math.Ceiling(Math.Max(top, bottom)));
        var origin = CandleColumns.OriginOf(XAxis);
        var (left, right) = (frame.X.ToData(bounds.X) + origin, frame.X.ToData(bounds.X + bounds.Width) + origin);
        var (from, to) = (Math.Min(left, right), Math.Max(left, right));
        var half = (float)(Math.Abs(frame.Y.PixelsPerUnit) * _barHeight / 2);
        for (var line = first; line <= last; line++)
        {
            var center = (float)frame.Y.ToPixel(line);
            var entry = state.Lines[line];
            if (entry.IsGroup)
            {
                AddGroup(frame, state.GroupSpans[line], origin, center, half);
                continue;
            }

            AddRow(frame, state, entry.Row, from, to, origin, center, half);
        }

        _built = version;
        _builtFrame = frame;
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame) =>
        commands.FillMesh(CollectionsMarshal.AsSpan(_mesh), frame.Bounds);

    private void Touch() => Interlocked.Increment(ref _appearance);

    private bool TryTransforms(ChartLayoutResult layout, out IAxisTransform time, out IAxisTransform lines)
    {
        time = null!;
        lines = null!;
        foreach (var placement in layout.Axes)
        {
            var bounds = placement.Bounds;
            if (placement.Axis == XAxis)
            {
                time = XAxis.CreateTransform(bounds.X, bounds.Width);
            }
            else if (placement.Axis == YAxis)
            {
                lines = YAxis.CreateTransform(bounds.Y, bounds.Height);
            }
        }

        return time is not null && lines is not null;
    }

    private void AddGroup(in SeriesFrame frame, (double Start, double End) span, double origin, float center, float half)
    {
        if (span.End <= span.Start)
        {
            return;
        }

        var (a, b) = ((float)frame.X.ToPixel(span.Start - origin), (float)frame.X.ToPixel(span.End - origin));
        var color = new Rgba8(150, 160, 175, 200);
        var thin = MathF.Max(2, half * 0.35f);
        CandleSeries.AddRectangle(_mesh, MathF.Min(a, b), center - thin, MathF.Max(a, b), center + thin, color);
    }

    // The tasks of a row in the visible time; bars thinner than a pixel that touch the previous one extend it.
    private void AddRow(in SeriesFrame frame, GanttData.State state, int row, double from, double to, double origin, float center, float half)
    {
        var tasks = state.ByRow[row];
        var mergeStart = float.NaN;
        var mergeEnd = float.NaN;
        var mergeColor = default(Rgba8);
        var hovered = HoveredTask;
        for (var i = state.FirstVisible(row, from); i < tasks.Length && state.Tasks[tasks[i]].Start <= to; i++)
        {
            var task = state.Tasks[tasks[i]];
            if (task.End < from)
            {
                continue;
            }

            var color = ColorOf(task);
            if (tasks[i] == hovered)
            {
                color = new Rgba8((byte)(color.R + ((255 - color.R) / 3)), (byte)(color.G + ((255 - color.G) / 3)), (byte)(color.B + ((255 - color.B) / 3)), color.A);
            }

            var start = (float)frame.X.ToPixel(task.Start - origin);
            if (task.IsMilestone)
            {
                AddDiamond(start, center, MathF.Min(MilestoneRadius * (float)frame.DpiScale, half + 2), color);
                continue;
            }

            var exact = (float)frame.X.ToPixel(task.End - origin);
            var end = MathF.Max(exact, start + 1);
            if (!float.IsNaN(mergeEnd) && exact - start < 1 && start <= mergeEnd + 1)
            {
                mergeEnd = MathF.Max(mergeEnd, end);
                continue;
            }

            Flush(mergeStart, mergeEnd, center, half, mergeColor);
            (mergeStart, mergeEnd, mergeColor) = (start, end, color);
        }

        Flush(mergeStart, mergeEnd, center, half, mergeColor);
    }

    private void Flush(float start, float end, float center, float half, Rgba8 color)
    {
        if (!float.IsNaN(start))
        {
            CandleSeries.AddRectangle(_mesh, start, center - half, end, center + half, color);
        }
    }

    private void AddDiamond(float x, float y, float radius, Rgba8 color)
    {
        var top = new MeshVertex(new Vector2(x, y - radius), color);
        var right = new MeshVertex(new Vector2(x + radius, y), color);
        var bottom = new MeshVertex(new Vector2(x, y + radius), color);
        var left = new MeshVertex(new Vector2(x - radius, y), color);
        _mesh.AddRange([top, right, bottom, top, bottom, left]);
        var middle = new Vector2(x, y);
        MeshFringe.Add(_mesh, top, right, middle);
        MeshFringe.Add(_mesh, right, bottom, middle);
        MeshFringe.Add(_mesh, bottom, left, middle);
        MeshFringe.Add(_mesh, left, top, middle);
    }
}

/// <summary>Labels in the bars that fit, arrows of the dependencies, and start, end and duration of the task under the pointer.</summary>
/// <param name="series">The Gantt chart.</param>
public sealed class GanttOverlay(GanttSeries series) : ChartOverlay
{
    private const float LabelSize = 11;
    private const int MostLabels = 1000;
    private const double ShortestLabel = 24;
    private const int MostLinks = 2000;

    private readonly GanttSeries _series = series ?? throw new ArgumentNullException(nameof(series));

    /// <inheritdoc />
    protected internal override long ExternalVersion => _series.Version + ChartText.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var state = _series.Data.Current;
        var layout = canvas.Layout;
        var pane = layout.Panes[0];
        canvas.Clip = pane;
        var origin = CandleColumns.OriginOf(_series.XAxis);
        var line = canvas.GetLineHeight(LabelSize);
        // Only the visible lines and times, found like the bars of the series.
        canvas.TryGetValue(_series.YAxis, pane.Y, out var topLine);
        canvas.TryGetValue(_series.YAxis, pane.Bottom, out var bottomLine);
        canvas.TryGetValue(_series.XAxis, pane.X, out var leftTime);
        canvas.TryGetValue(_series.XAxis, pane.Right, out var rightTime);
        var first = Math.Max(0, (int)Math.Floor(Math.Min(topLine, bottomLine)));
        var last = Math.Min(state.Lines.Length - 1, (int)Math.Ceiling(Math.Max(topLine, bottomLine)));
        var (from, to) = (Math.Min(leftTime, rightTime) + origin, Math.Max(leftTime, rightTime) + origin);
        canvas.TryGetPosition(_series.XAxis, leftTime, out var leftPixel);
        canvas.TryGetPosition(_series.XAxis, rightTime, out var rightPixel);
        var perUnit = rightTime != leftTime ? Math.Abs((rightPixel - leftPixel) / (rightTime - leftTime)) : 0;
        var labels = 0;
        for (var lineIndex = first; lineIndex <= last && labels < MostLabels; lineIndex++)
        {
            labels = DrawRow(canvas, state, state.Lines[lineIndex], (from, to), perUnit, origin, line, pane, labels);
        }

        DrawLinks(canvas, state, origin);
        canvas.Clip = null;
        DrawHover(canvas, state, origin);
    }

    // Rows whose longest task is too narrow for any label are skipped as a whole, except for milestones.
    private int DrawRow(OverlayCanvas canvas, GanttData.State state, GanttData.Line entry, (double From, double To) times, double perUnit, double origin, float line, DipRect pane, int labels)
    {
        if (entry.IsGroup || (state.Longest[entry.Row] * perUnit < ShortestLabel && !state.HasMilestones[entry.Row]))
        {
            return labels;
        }

        var tasks = state.ByRow[entry.Row];
        for (var k = state.FirstVisible(entry.Row, times.From); k < tasks.Length && state.Tasks[tasks[k]].Start <= times.To && labels < MostLabels; k++)
        {
            var task = state.Tasks[tasks[k]];
            if (task.IsMilestone || (task.End - task.Start) * perUnit >= ShortestLabel)
            {
                labels += DrawLabel(canvas, state, task, origin, line, pane) ? 1 : 0;
            }
        }

        return labels;
    }

    private bool DrawLabel(OverlayCanvas canvas, GanttData.State state, in GanttTask task, double origin, float line, DipRect pane)
    {
        if (task.Label.Length == 0 || !TryPlace(canvas, state, task.Row, task.Start - origin, out var left, out var y))
        {
            return false;
        }

        var width = canvas.MeasureWidth(task.Label, LabelSize);

        // A milestone has its name beside the diamond.
        if (task.IsMilestone)
        {
            var at = left + 9 + width > pane.Right ? left - 9 - width : left + 9;
            canvas.DrawText(task.Label, Math.Round(at), Math.Round(y + (line * 0.35)), LabelSize, canvas.TextColor);
            return true;
        }

        canvas.TryGetPosition(_series.XAxis, task.End - origin, out var right);
        if (right - left < width + 8 || right < pane.X || left > pane.Right)
        {
            return false;
        }

        canvas.DrawText(task.Label, Math.Round(Math.Max(left, pane.X) + 4), Math.Round(y + (line * 0.35)), LabelSize, new Rgba8(255, 255, 255, 235));
        return true;
    }

    private static string Duration(AxisBase axis, double length)
    {
        if (axis is DateTimeAxis time)
        {
            var span = TimeSpan.FromTicks((long)(length * time.NanosecondsPerUnit / 100));
            return span.TotalDays >= 1 ? span.ToString(@"d\.hh\:mm", ChartText.Culture) + " d" : span.ToString(@"hh\:mm\:ss", ChartText.Culture);
        }

        return length.ToString("G6", ChartText.Culture);
    }

    private bool TryPlace(OverlayCanvas canvas, GanttData.State state, int row, double time, out double x, out double y)
    {
        y = 0;
        var line = state.LineOfRow[row];
        var isOnAxis = canvas.TryGetPosition(_series.XAxis, time, out x);
        return isOnAxis && line >= 0 && canvas.TryGetPosition(_series.YAxis, line, out y);
    }

    // From the end of the first task right, down or up to the line of the second, and right into its start.
    private void DrawLinks(OverlayCanvas canvas, GanttData.State state, double origin)
    {
        var color = canvas.TextColor with { A = 170 };
        var drawn = 0;
        foreach (var link in state.Links)
        {
            var (from, to) = (state.Tasks[link.From], state.Tasks[link.To]);
            if (drawn >= MostLinks || !TryPlace(canvas, state, from.Row, from.End - origin, out var x1, out var y1) || !TryPlace(canvas, state, to.Row, to.Start - origin, out var x2, out var y2))
            {
                continue;
            }

            var bend = (float)Math.Max(x1 + 6, x2 - 6);
            var (a, b, c, d) = (new Vector2((float)x1, (float)y1), new Vector2(bend, (float)y1), new Vector2(bend, (float)y2), new Vector2((float)x2, (float)y2));
            canvas.DrawLine(a, b, 1, color);
            canvas.DrawLine(b, c, 1, color);
            canvas.DrawLine(c, d, 1, color);
            canvas.FillTriangle(d, d + new Vector2(-5, -3), d + new Vector2(-5, 3), color);
            drawn++;
        }
    }

    private void DrawHover(OverlayCanvas canvas, GanttData.State state, double origin)
    {
        var index = _series.HoveredTask;
        if (index < 0 || index >= state.Tasks.Length || !TryPlace(canvas, state, state.Tasks[index].Row, state.Tasks[index].Start - origin, out var x, out var y))
        {
            return;
        }

        var task = state.Tasks[index];
        var axis = _series.XAxis;
        var name = task.Label.Length > 0 ? task.Label + ": " : string.Empty;
        var text = task.IsMilestone
            ? name + axis.FormatCursorValue(task.Start - origin)
            : name + axis.FormatCursorValue(task.Start - origin) + " – " + axis.FormatCursorValue(task.End - origin) + " (" + Duration(axis, task.End - task.Start) + ")";
        canvas.DrawLabel(text, x, y + 12, canvas.BackgroundColor with { A = 235 }, canvas.TextColor);
    }
}

/// <summary>Marks the task under the pointer; a click on the line of a group collapses or expands it.</summary>
public sealed class GanttModifier : ChartModifier
{
    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { Chart: { CurrentLayout: { } layout } chart } || chart.Series.OfType<GanttSeries>().FirstOrDefault() is not { } gantt)
        {
            return ModifierResult.Ignored;
        }

        switch (input.Kind)
        {
            case PointerEventKind.Exited or PointerEventKind.Canceled:
                gantt.HoveredTask = -1;
                return ModifierResult.Ignored;
            case PointerEventKind.Moved:
                gantt.HoveredTask = gantt.FindTask(layout, input.Position);
                return ModifierResult.Ignored;
            case PointerEventKind.Pressed when input.ChangedButton == PointerButton.Primary:
                var line = gantt.FindLine(layout, input.Position.Y);
                var lines = gantt.Data.Current.Lines;
                if (line < 0 || !lines[line].IsGroup || input.Position.X > layout.PlotArea.Right)
                {
                    return ModifierResult.Ignored;
                }

                gantt.Data.SetCollapsed(lines[line].Name, !lines[line].IsCollapsed);
                return ModifierResult.Handled;
            default:
                return ModifierResult.Ignored;
        }
    }
}

/// <summary>Describes Gantt charts and time lines.</summary>
public static class GanttCharts
{
    /// <summary>Describes a Gantt chart: a Y axis with the names of the lines from the top, the bars, labels, dependencies and the operation with the mouse. Describe the time axis yourself.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="data">The rows and tasks.</param>
    /// <param name="configure">Sets further properties.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Gantt(this ChartBuilder builder, GanttData data, Action<GanttSeries>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(data);
        var series = new GanttSeries(data, new NumericAxis("x", AxisPosition.Bottom), new NumericAxis("y", AxisPosition.Left));
        return builder
            .YAxis(a => a.Category(series.LineNames(), "gantt").Inverted())
            .AddSeries(
                (x, y) =>
                {
                    var created = new GanttSeries(data, x, y);
                    configure?.Invoke(created);
                    return created;
                },
                yAxis: "gantt")
            .AddOverlay(chart => new GanttOverlay(chart.Series.OfType<GanttSeries>().First()))
            .Modifiers(m => m.Custom(() => new GanttModifier()));
    }
}
