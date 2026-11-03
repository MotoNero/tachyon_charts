using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Three stacked panes of different heights, aligned despite axes of different widths.
/// </summary>
public sealed class LayoutStackedPanesScene : LayoutScene
{
    /// <inheritdoc />
    public override string Name => "LayoutStackedPanes";

    /// <inheritdoc />
    protected override string? Title => null;

    /// <inheritdoc />
    protected override IReadOnlyList<AxisBase> HorizontalAxes { get; } =
    [
        CreateAxis("x", AxisPosition.Bottom, 0, 500),
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<LayoutPane> Panes { get; } =
    [
        new LayoutPane([CreateAxis("a", AxisPosition.Left, -1, 1)], 2),
        new LayoutPane([CreateAxis("b", AxisPosition.Left, 0, 100000)]),
        new LayoutPane([CreateAxis("c", AxisPosition.Left, 0, 5), CreateAxis("d", AxisPosition.Right, 1000, 2000, "Flow")]),
    ];
}
