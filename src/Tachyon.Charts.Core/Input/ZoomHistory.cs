using System.Diagnostics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Core.Input;

/// <summary>A change that the undo chain of a chart can take back and repeat, for example a drawing.</summary>
public interface IChartEdit
{
    /// <summary>Takes the change back.</summary>
    void Undo();

    /// <summary>Makes the change again.</summary>
    void Redo();
}

/// <summary>
/// Remembers the ranges of all axes before navigation, and other edits such as drawings, for undo and redo in one
/// order. Navigation steps in quick succession, such as wheel notches or the moves of one drag, count as one.
/// It belongs to the thread of the chart.
/// </summary>
public sealed class ZoomHistory
{
    private readonly List<Step> _undo = [];
    private readonly List<Step> _redo = [];
    private long _lastRecord;

    /// <summary>Gets or sets how many steps are kept.</summary>
    public int Capacity { get; set; } = 100;

    /// <summary>Gets or sets the time in which steps count as one.</summary>
    public TimeSpan MergeWindow { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Gets a value indicating whether there is a step to undo.</summary>
    public bool CanUndo => _undo.Count > 0;

    /// <summary>Gets a value indicating whether there is a step to redo.</summary>
    public bool CanRedo => _redo.Count > 0;

    internal Func<long> Clock { get; set; } = Stopwatch.GetTimestamp;

    /// <summary>Records the ranges before a change.</summary>
    /// <param name="chart">The chart.</param>
    public void Record(ChartSurface chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        var now = Clock();
        var isNewStep = _undo.Count == 0 || (now - _lastRecord) / (double)Stopwatch.Frequency > MergeWindow.TotalSeconds;
        _lastRecord = now;
        if (!isNewStep)
        {
            return;
        }

        Push(new Step(Capture(chart), null));
    }

    /// <summary>Records an edit that already happened, as a step of its own.</summary>
    /// <param name="edit">The edit.</param>
    public void Record(IChartEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Push(new Step(null, edit));

        // The next navigation is a step of its own.
        _lastRecord = long.MinValue / 2;
    }

    /// <summary>Goes back one step.</summary>
    /// <param name="chart">The chart.</param>
    /// <returns>Whether there was a step.</returns>
    public bool Undo(ChartSurface chart) => Move(chart, _undo, _redo, isUndo: true);

    /// <summary>Goes forward one step.</summary>
    /// <param name="chart">The chart.</param>
    /// <returns>Whether there was a step.</returns>
    public bool Redo(ChartSurface chart) => Move(chart, _redo, _undo, isUndo: false);

    /// <summary>Forgets all steps.</summary>
    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private static AxisState[] Capture(ChartSurface chart) =>
        [.. chart.HorizontalAxes.Concat(chart.Panes.SelectMany(pane => pane.Axes)).Select(axis => new AxisState(axis, axis.VisibleRange, axis.IsAutoRange))];

    private void Push(Step step)
    {
        _undo.Add(step);
        _redo.Clear();
        if (_undo.Count > Capacity)
        {
            _undo.RemoveAt(0);
        }
    }

    private bool Move(ChartSurface chart, List<Step> from, List<Step> to, bool isUndo)
    {
        ArgumentNullException.ThrowIfNull(chart);
        if (from.Count == 0)
        {
            return false;
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        if (step.Edit is { } edit)
        {
            if (isUndo)
            {
                edit.Undo();
            }
            else
            {
                edit.Redo();
            }

            to.Add(step);
            _lastRecord = long.MinValue / 2;
            return true;
        }

        to.Add(new Step(Capture(chart), null));
        foreach (var item in step.Axes!)
        {
            item.Axis.VisibleRange = item.Range;
            item.Axis.IsAutoRange = item.IsAutoRange;
        }

        // The next navigation is a step of its own.
        _lastRecord = long.MinValue / 2;
        return true;
    }

    private readonly record struct AxisState(AxisBase Axis, DoubleRange Range, bool IsAutoRange);

    private sealed record Step(AxisState[]? Axes, IChartEdit? Edit);
}

/// <summary>What a key can do.</summary>
public enum ChartKeyAction
{
    /// <summary>Moves the view to the left.</summary>
    PanLeft,

    /// <summary>Moves the view to the right.</summary>
    PanRight,

    /// <summary>Moves the view up.</summary>
    PanUp,

    /// <summary>Moves the view down.</summary>
    PanDown,

    /// <summary>Zooms in around the middle.</summary>
    ZoomIn,

    /// <summary>Zooms out around the middle.</summary>
    ZoomOut,

    /// <summary>Shows all data.</summary>
    AutoRange,

    /// <summary>Goes back one step of the history.</summary>
    Undo,

    /// <summary>Goes forward one step of the history.</summary>
    Redo,
}

/// <summary>
/// Navigates with the keyboard. The keys can be changed in <see cref="Bindings"/>.
/// </summary>
public sealed class KeyboardNavigationModifier : ChartModifier
{
    /// <summary>Initializes a new instance of the <see cref="KeyboardNavigationModifier"/> class.</summary>
    public KeyboardNavigationModifier()
        : base(5)
    {
    }

    /// <summary>Gets the keys of the actions. Removing an entry turns the action off.</summary>
    public Dictionary<ChartKeyAction, KeyBinding> Bindings { get; } = new()
    {
        [ChartKeyAction.PanLeft] = new(ChartKey.Left),
        [ChartKeyAction.PanRight] = new(ChartKey.Right),
        [ChartKeyAction.PanUp] = new(ChartKey.Up),
        [ChartKeyAction.PanDown] = new(ChartKey.Down),
        [ChartKeyAction.ZoomIn] = new(ChartKey.Add),
        [ChartKeyAction.ZoomOut] = new(ChartKey.Subtract),
        [ChartKeyAction.AutoRange] = new(ChartKey.Home),
        [ChartKeyAction.Undo] = new(ChartKey.Z, InputModifiers.Control),
        [ChartKeyAction.Redo] = new(ChartKey.Y, InputModifiers.Control),
    };

    /// <summary>Gets or sets the share of the plot one key press moves.</summary>
    public double PanStep { get; set; } = 0.1;

    /// <summary>Gets or sets the factor of one key press, greater than one.</summary>
    public double ZoomStep { get; set; } = 1.2;

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (Interaction is not { } interaction || interaction.Chart.CurrentLayout is not { } layout)
        {
            return ModifierResult.Ignored;
        }

        foreach (var (action, binding) in Bindings)
        {
            if (binding.Matches(input))
            {
                var plot = layout.PlotArea;
                Perform(interaction, action, new System.Numerics.Vector2((float)(plot.X + (plot.Width / 2)), (float)(plot.Y + (plot.Height / 2))), plot.Width, plot.Height);
                return ModifierResult.Handled;
            }
        }

        return ModifierResult.Ignored;
    }

    private void Perform(ChartInteraction interaction, ChartKeyAction action, System.Numerics.Vector2 center, double width, double height)
    {
        if (!interaction.TryHitPane(center, out var pane))
        {
            return;
        }

        var stepX = (float)(width * PanStep);
        var stepY = (float)(height * PanStep);
        switch (action)
        {
            case ChartKeyAction.PanLeft:
                interaction.Pan(pane, new System.Numerics.Vector2(stepX, 0), ChartDirection.X);
                break;
            case ChartKeyAction.PanRight:
                interaction.Pan(pane, new System.Numerics.Vector2(-stepX, 0), ChartDirection.X);
                break;
            case ChartKeyAction.PanUp:
                interaction.Pan(pane, new System.Numerics.Vector2(0, stepY), ChartDirection.Y);
                break;
            case ChartKeyAction.PanDown:
                interaction.Pan(pane, new System.Numerics.Vector2(0, -stepY), ChartDirection.Y);
                break;
            case ChartKeyAction.ZoomIn:
                interaction.Zoom(pane, center, 1 / ZoomStep, ChartDirection.Both);
                break;
            case ChartKeyAction.ZoomOut:
                interaction.Zoom(pane, center, ZoomStep, ChartDirection.Both);
                break;
            case ChartKeyAction.AutoRange:
                interaction.AutoRange(pane);
                break;
            case ChartKeyAction.Undo:
                interaction.Dispatch(() => interaction.History.Undo(interaction.Chart));
                break;
            default:
                interaction.Dispatch(() => interaction.History.Redo(interaction.Chart));
                break;
        }
    }
}
