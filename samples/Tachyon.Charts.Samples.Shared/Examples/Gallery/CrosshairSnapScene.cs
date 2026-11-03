
namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A crosshair sitting on the nearest point.
/// </summary>
public sealed class CrosshairSnapScene : CrosshairScene
{
    /// <inheritdoc />
    public override string Name => "CrosshairSnap";

    /// <inheritdoc />
    protected override bool IsSnapping => true;
}
