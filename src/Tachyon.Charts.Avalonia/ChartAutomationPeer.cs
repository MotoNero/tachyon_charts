using Avalonia.Automation.Peers;

namespace Tachyon.Charts.Avalonia;

/// <summary>
/// Tells screen readers and test tools what a chart shows: its title as the name, its axes with their
/// ranges and the number of series as the help text.
/// </summary>
/// <param name="owner">The chart.</param>
public sealed class ChartAutomationPeer(Chart owner) : ControlAutomationPeer(owner)
{
    private Chart Chart => (Chart)Owner;

    /// <inheritdoc />
    protected override string GetClassNameCore() => nameof(Avalonia.Chart);

    /// <inheritdoc />
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

    /// <inheritdoc />
    protected override string? GetNameCore()
    {
        var name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? Chart.Title ?? nameof(Avalonia.Chart) : name;
    }

    /// <inheritdoc />
    protected override string? GetHelpTextCore()
    {
        var help = base.GetHelpTextCore();
        return string.IsNullOrEmpty(help) ? ChartDescription.Describe(Chart.XAxes.Concat(Chart.YAxes), Chart.Series.Count) : help;
    }
}
