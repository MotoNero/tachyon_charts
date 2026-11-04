using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Data;

/// <summary>A row of a Gantt chart, for example a resource, a station or a step of a test sequence.</summary>
/// <param name="Name">The name shown at the left.</param>
/// <param name="Group">The group the row belongs to, or <see langword="null"/> for none. Rows of a group can be collapsed.</param>
public sealed record GanttRow(string Name, string? Group = null);

/// <summary>A bar of a Gantt chart: from start to end on one row. A task whose end equals its start is a milestone.</summary>
/// <param name="Row">The number of the row.</param>
/// <param name="Start">The start in units of the time axis.</param>
/// <param name="End">The end in units of the time axis.</param>
/// <param name="Label">The text in the bar.</param>
/// <param name="Color">The color, or the default value for the palette of the theme.</param>
public readonly record struct GanttTask(int Row, double Start, double End, string Label = "", Rgba8 Color = default)
{
    /// <summary>Gets a value indicating whether the task is a milestone, a point in time.</summary>
    public bool IsMilestone => End <= Start;
}

/// <summary>A dependency: the second task starts after the first one.</summary>
/// <param name="From">The number of the task that comes first.</param>
/// <param name="To">The number of the task that follows.</param>
public readonly record struct GanttLink(int From, int To);

/// <summary>
/// Rows, tasks and dependencies of a Gantt chart. Writing is safe from any thread; readers take a published state
/// without a lock, which is built anew after a change, sorted by row and start for drawing only what is visible.
/// </summary>
public sealed class GanttData
{
    private readonly Lock _gate = new();
    private readonly List<GanttRow> _rows = [];
    private readonly List<GanttTask> _tasks = [];
    private readonly List<GanttLink> _links = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private State? _state;
    private long _version;

    /// <summary>Gets a number that grows with every change.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Gets the number of tasks.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _tasks.Count;
            }
        }
    }

    /// <summary>Gets the rows.</summary>
    public IReadOnlyList<GanttRow> Rows
    {
        get
        {
            lock (_gate)
            {
                return [.. _rows];
            }
        }
    }

    /// <summary>Gets the state for drawing, built after the last change.</summary>
    internal State Current
    {
        get
        {
            var state = Volatile.Read(ref _state);
            if (state is not null && state.Version == Version)
            {
                return state;
            }

            lock (_gate)
            {
                state = State.Create(_rows, _tasks, _links, _collapsed, Version);
                Volatile.Write(ref _state, state);
                return state;
            }
        }
    }

    /// <summary>Gets a task.</summary>
    /// <param name="index">The number of the task.</param>
    /// <returns>The task.</returns>
    public GanttTask this[int index]
    {
        get
        {
            lock (_gate)
            {
                return _tasks[index];
            }
        }
    }

    /// <summary>Adds a row at the bottom.</summary>
    /// <param name="name">The name.</param>
    /// <param name="group">The group, or <see langword="null"/> for none.</param>
    /// <returns>The number of the row.</returns>
    public int AddRow(string name, string? group = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        lock (_gate)
        {
            _rows.Add(new GanttRow(name, group));
            Touch();
            return _rows.Count - 1;
        }
    }

    /// <summary>Adds a task.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The number of the task.</returns>
    /// <exception cref="ArgumentException">The row does not exist, a time is not finite, or the end lies before the start.</exception>
    public int Add(in GanttTask task)
    {
        if (!double.IsFinite(task.Start) || !double.IsFinite(task.End) || task.End < task.Start)
        {
            throw new ArgumentException("A task needs a finite start and an end at or after it.", nameof(task));
        }

        lock (_gate)
        {
            if (task.Row < 0 || task.Row >= _rows.Count)
            {
                throw new ArgumentException($"There is no row {task.Row}.", nameof(task));
            }

            _tasks.Add(task with { Label = task.Label ?? string.Empty });
            Touch();
            return _tasks.Count - 1;
        }
    }

    /// <summary>Adds a dependency between two tasks.</summary>
    /// <param name="from">The task that comes first.</param>
    /// <param name="to">The task that follows.</param>
    /// <exception cref="ArgumentException">A task does not exist.</exception>
    public void Link(int from, int to)
    {
        lock (_gate)
        {
            if (from < 0 || to < 0 || from >= _tasks.Count || to >= _tasks.Count)
            {
                throw new ArgumentException("Both tasks of a dependency must exist.", nameof(to));
            }

            _links.Add(new GanttLink(from, to));
            Touch();
        }
    }

    /// <summary>Collapses or expands a group: a collapsed group shows one row with the span of all its tasks.</summary>
    /// <param name="group">The group.</param>
    /// <param name="isCollapsed">Whether the group is collapsed.</param>
    public void SetCollapsed(string group, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(group);
        lock (_gate)
        {
            var changed = isCollapsed ? _collapsed.Add(group) : _collapsed.Remove(group);
            if (changed)
            {
                Touch();
            }
        }
    }

    /// <summary>Gets a value indicating whether a group is collapsed.</summary>
    /// <param name="group">The group.</param>
    /// <returns>Whether it is collapsed.</returns>
    public bool IsCollapsed(string group)
    {
        lock (_gate)
        {
            return _collapsed.Contains(group);
        }
    }

    private void Touch() => Interlocked.Increment(ref _version);

    /// <summary>What a line of the chart shows: a group with the span of its tasks, or a row.</summary>
    internal readonly record struct Line(string Name, string? Group, int Row, bool IsGroup, bool IsCollapsed);

    /// <summary>The published state: the visible lines from the top, and the tasks of each row sorted by start.</summary>
    internal sealed class State
    {
        private State(long version, Line[] lines, GanttTask[] tasks, int[][] byRow, double[] longest, bool[] hasMilestones, GanttLink[] links, int[] lineOfRow, (double Start, double End)[] groupSpans)
        {
            HasMilestones = hasMilestones;
            Version = version;
            Lines = lines;
            Tasks = tasks;
            ByRow = byRow;
            Longest = longest;
            Links = links;
            LineOfRow = lineOfRow;
            GroupSpans = groupSpans;
        }

        public long Version { get; }

        public Line[] Lines { get; }

        public GanttTask[] Tasks { get; }

        /// <summary>Gets the numbers of the tasks of each row, sorted by start.</summary>
        public int[][] ByRow { get; }

        /// <summary>Gets the longest task of each row, so that a search by start finds tasks that began before the view.</summary>
        public double[] Longest { get; }

        /// <summary>Gets for each row whether it has a milestone.</summary>
        public bool[] HasMilestones { get; }

        public GanttLink[] Links { get; }

        /// <summary>Gets the line of each row, or -1 while its group is collapsed.</summary>
        public int[] LineOfRow { get; }

        /// <summary>Gets the span of the tasks of each line that is a group.</summary>
        public (double Start, double End)[] GroupSpans { get; }

        public static State Create(List<GanttRow> rows, List<GanttTask> tasks, List<GanttLink> links, HashSet<string> collapsed, long version)
        {
            var byRow = new List<int>[rows.Count];
            var longest = new double[rows.Count];
            var hasMilestones = new bool[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                byRow[r] = [];
            }

            var array = tasks.ToArray();
            for (var i = 0; i < array.Length; i++)
            {
                byRow[array[i].Row].Add(i);
                longest[array[i].Row] = Math.Max(longest[array[i].Row], array[i].End - array[i].Start);
                hasMilestones[array[i].Row] |= array[i].IsMilestone;
            }

            var sorted = new int[rows.Count][];
            for (var r = 0; r < rows.Count; r++)
            {
                byRow[r].Sort((a, b) => array[a].Start.CompareTo(array[b].Start));
                sorted[r] = [.. byRow[r]];
            }

            // A group gets a line of its own above its first row; a collapsed group hides its rows.
            var lines = new List<Line>();
            var spans = new List<(double, double)>();
            var lineOfRow = new int[rows.Count];
            string? current = null;
            for (var r = 0; r < rows.Count; r++)
            {
                var group = rows[r].Group;
                if (group is not null && group != current)
                {
                    var isCollapsed = collapsed.Contains(group);
                    lines.Add(new Line(group, group, -1, IsGroup: true, isCollapsed));
                    spans.Add(Span(rows, array, group));
                }

                current = group;
                if (group is not null && collapsed.Contains(group))
                {
                    lineOfRow[r] = -1;
                    continue;
                }

                lineOfRow[r] = lines.Count;
                lines.Add(new Line(rows[r].Name, group, r, IsGroup: false, IsCollapsed: false));
                spans.Add((0, 0));
            }

            return new State(version, [.. lines], array, sorted, longest, hasMilestones, [.. links], lineOfRow, [.. spans]);
        }

        /// <summary>Finds the first task of a row that may still be visible at a time.</summary>
        public int FirstVisible(int row, double time)
        {
            var tasks = ByRow[row];
            var from = time - Longest[row];
            var (low, high) = (0, tasks.Length);
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (Tasks[tasks[middle]].Start < from)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return low;
        }

        private static (double Start, double End) Span(List<GanttRow> rows, GanttTask[] tasks, string group)
        {
            var (start, end) = (double.PositiveInfinity, double.NegativeInfinity);
            foreach (var task in tasks)
            {
                if (rows[task.Row].Group == group)
                {
                    start = Math.Min(start, task.Start);
                    end = Math.Max(end, task.End);
                }
            }

            return double.IsFinite(start) ? (start, end) : (0, 0);
        }
    }
}
