using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// Keeps charts in step: the same X range, a shared cursor, and axes of the same width, so that their
/// plots line up. All charts of a group must render on one thread, which a shared render thread does.
/// </summary>
public sealed class ChartGroup
{
    private static readonly Lock _registryGate = new();
    private static readonly Dictionary<string, ChartGroup> _registry = [];

    private readonly List<Member> _members = [];
    private readonly List<CrosshairModifier> _crosshairs = [];
    private DoubleRange _range = DoubleRange.Empty;
    private double _left;
    private double _right;

    /// <summary>Gets or sets a value indicating whether charts share their X range.</summary>
    public bool SynchronizesRange { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether charts share the room of their axes.</summary>
    public bool SynchronizesAxisWidth { get; set; } = true;

    /// <summary>
    /// Occurs when the cursor of a member moves, with its place on the shared axis, or <see langword="null"/> when it
    /// leaves. Charts without a crosshair, such as 3D charts, follow the cursor through it.
    /// </summary>
    public event Action<double?>? CursorMoved;

    /// <summary>Gets the charts.</summary>
    public IReadOnlyList<ChartSurface> Charts => [.. _members.Select(member => member.Chart)];

    /// <summary>Finds a group by name and creates it if necessary.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The group.</returns>
    public static ChartGroup Get(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        using var scope = _registryGate.EnterScope();
        if (!_registry.TryGetValue(name, out var group))
        {
            group = new ChartGroup();
            _registry.Add(name, group);
        }

        return group;
    }

    /// <summary>Adds a chart. It takes over the range of the group, if there is one.</summary>
    /// <param name="chart">The chart.</param>
    /// <exception cref="ArgumentException">The chart has no horizontal axis.</exception>
    public void Add(ChartSurface chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (chart.HorizontalAxes.Count == 0)
        {
            throw new ArgumentException("The chart has no horizontal axis.", nameof(chart));
        }

        if (_members.Exists(member => member.Chart == chart))
        {
            return;
        }

        var axis = chart.HorizontalAxes[0];
        if (SynchronizesRange && !_range.IsEmpty)
        {
            axis.VisibleRange = _range;
        }

        _members.Add(new Member(chart, axis) { Stamp = axis.Version });
        chart.Group = this;
    }

    /// <summary>
    /// Adds a chart that shares another axis than its first horizontal one, for example the first axis of a 3D chart.
    /// The width of its axes stays its own, as its picture does not line up with the plots of the others.
    /// </summary>
    /// <param name="chart">The chart.</param>
    /// <param name="axis">The axis that follows the group.</param>
    public void Add(ChartSurface chart, AxisBase axis)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(axis);
        if (_members.Exists(member => member.Chart == chart))
        {
            return;
        }

        if (SynchronizesRange && !_range.IsEmpty)
        {
            axis.VisibleRange = _range;
        }

        _members.Add(new Member(chart, axis) { Stamp = axis.Version, SharesAxisWidth = false });
        chart.Group = this;
    }

    /// <summary>Shows a cursor at a place on all crosshairs of the group, for charts without a crosshair of their own.</summary>
    /// <param name="value">The place on the shared axis, or <see langword="null"/> to hide the cursor.</param>
    public void ShowCursor(double? value) => OnCursorMoved(null, value);

    /// <summary>Removes a chart.</summary>
    /// <param name="chart">The chart.</param>
    /// <returns>Whether it was a member.</returns>
    public bool Remove(ChartSurface chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (_members.RemoveAll(member => member.Chart == chart) == 0)
        {
            return false;
        }

        chart.Group = null;
        chart.SetPlotInsets(0, 0);
        return true;
    }

    /// <summary>Shows the cursor of this crosshair on all other connected ones.</summary>
    /// <param name="crosshair">The crosshair.</param>
    public void Connect(CrosshairModifier crosshair)
    {
        ArgumentNullException.ThrowIfNull(crosshair);
        if (!_crosshairs.Contains(crosshair))
        {
            _crosshairs.Add(crosshair);
            crosshair.Moved += OnCursorMoved;
        }
    }

    /// <summary>Stops sharing the cursor of this crosshair.</summary>
    /// <param name="crosshair">The crosshair.</param>
    public void Disconnect(CrosshairModifier crosshair)
    {
        ArgumentNullException.ThrowIfNull(crosshair);
        if (_crosshairs.Remove(crosshair))
        {
            crosshair.Moved -= OnCursorMoved;
        }
    }

    /// <summary>Passes a changed range on to the other charts, before a frame uses it.</summary>
    internal void OnFrameStarting(ChartSurface chart)
    {
        if (!SynchronizesRange)
        {
            return;
        }

        var member = _members.Find(item => item.Chart == chart);
        if (member is null || member.Axis.Version == member.Stamp)
        {
            return;
        }

        member.Stamp = member.Axis.Version;
        _range = member.Axis.VisibleRange;
        foreach (var other in _members)
        {
            if (other != member && other.Axis.VisibleRange != _range)
            {
                other.Axis.VisibleRange = _range;
                other.Stamp = other.Axis.Version;
                other.Chart.Invalidate();
            }
        }
    }

    /// <summary>
    /// Collects the room a chart needs for its axes. Charts that now have too little or too much get a
    /// new layout.
    /// </summary>
    internal void OnMeasured(ChartSurface chart, double left, double right)
    {
        if (!SynchronizesAxisWidth)
        {
            return;
        }

        var member = _members.Find(item => item.Chart == chart);
        if (member is not { SharesAxisWidth: true })
        {
            return;
        }

        member.Left = left;
        member.Right = right;
        var requiredLeft = _members.Where(item => item.SharesAxisWidth).Max(item => item.Left);
        var requiredRight = _members.Where(item => item.SharesAxisWidth).Max(item => item.Right);
        if (requiredLeft == _left && requiredRight == _right)
        {
            chart.SetPlotInsets(_left, _right);
            return;
        }

        (_left, _right) = (requiredLeft, requiredRight);
        foreach (var other in _members)
        {
            if (other.SharesAxisWidth)
            {
                other.Chart.SetPlotInsets(_left, _right);
            }
        }
    }

    private void OnCursorMoved(CrosshairModifier? source, double? value)
    {
        CursorMoved?.Invoke(value);
        foreach (var crosshair in _crosshairs)
        {
            if (crosshair == source || crosshair.Interaction is not { } interaction)
            {
                continue;
            }

            var overlay = crosshair.Overlay;
            var axis = interaction.Chart.HorizontalAxes[0];
            interaction.Dispatch(() =>
            {
                if (value is { } x)
                {
                    overlay.ShowAt(axis, x);
                }
                else
                {
                    overlay.Hide();
                }
            });
        }
    }

    private sealed class Member(ChartSurface chart, AxisBase axis)
    {
        public ChartSurface Chart { get; } = chart;

        public AxisBase Axis { get; } = axis;

        public long Stamp { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }

        public bool SharesAxisWidth { get; init; } = true;
    }
}
