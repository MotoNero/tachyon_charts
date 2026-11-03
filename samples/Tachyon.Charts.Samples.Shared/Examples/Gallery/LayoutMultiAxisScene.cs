using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Axes on all four sides, two of them on the left.
/// </summary>
public sealed class LayoutMultiAxisScene : LayoutScene
{
    /// <inheritdoc />
    public override string Name => "LayoutMultiAxis";

    /// <inheritdoc />
    protected override string? Title => "Layout with axes on four sides";

    /// <inheritdoc />
    protected override IReadOnlyList<AxisBase> HorizontalAxes { get; } =
    [
        CreateAxis("bottom", AxisPosition.Bottom, 0, 1000, "Time"),
        CreateAxis("top", AxisPosition.Top, 0.25, 0.75),
    ];

    /// <inheritdoc />
    protected override IReadOnlyList<LayoutPane> Panes { get; } =
    [
        new LayoutPane(
        [
            CreateAxis("left", AxisPosition.Left, 0, 10),
            CreateAxis("outer", AxisPosition.Left, -123456, 123456, "Pressure"),
            CreateAxis("right", AxisPosition.Right, 0, 0.001),
        ]),
    ];
}
