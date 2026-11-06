using System.Numerics;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A page with the crosshair and the values of all series, with the pointer at a fixed share of the plot.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="configure">Builds the chart.</param>
/// <param name="share">Where the pointer stands, as a share of the width and height of the plot.</param>
public sealed class CrosshairValuesScene(string name, Action<ChartBuilder> configure, Vector2 share) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 520);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var builder = new ChartBuilder();
        configure(builder);
        builder.Theme(BuiltInThemes.TachyonDark);
        var built = builder.ToDefinition().Build(SkiaTextEngine.Default);
        var chart = built.Surface;
        chart.Render(device, target);

        // The same as the modifier does on a pointer move.
        var plot = chart.CurrentLayout!.PlotArea;
        var position = new Vector2((float)(plot.X + (plot.Width * share.X)), (float)(plot.Y + (plot.Height * share.Y)));
        var hits = new List<HitResult>();
        chart.HitTestAll(position, 40, HitTestMode.NearestX, hits);
        var header = chart.FirstXAxis.FormatCursorValue(new ChartInteraction(chart).ToXValue(position));
        var overlay = new CrosshairOverlay();
        overlay.Show(position, null, hits, header);
        chart.AddOverlay(overlay);
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
