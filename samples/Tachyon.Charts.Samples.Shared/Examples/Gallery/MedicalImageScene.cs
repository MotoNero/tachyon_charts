using System.Numerics;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>A synthetic medical image in one of the principal patient orientations.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="create">Creates the image.</param>
public sealed class MedicalImageScene(string name, Func<MedicalImageDisplay> create) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 620);

    /// <summary>Builds the chart: the image on the dark theme.</summary>
    /// <param name="chart">The chart.</param>
    public void Configure(ChartBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        create().Configure(chart.Theme(BuiltInThemes.TachyonDark));
    }

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var builder = new ChartBuilder();
        Configure(builder);
        var built = builder.ToDefinition().Build(SkiaTextEngine.Default);
        var chart = built.Surface;
        chart.Render(device, target);

        var pipeline = new ModifierPipeline { Interaction = new ChartInteraction(chart) };
        foreach (var modifier in built.Modifiers)
        {
            pipeline.Add(modifier);
        }

        var plot = chart.CurrentLayout!.PlotArea;
        pipeline.Process(new PointerInputEvent(
            PointerEventKind.Moved,
            1,
            new Vector2((float)(plot.X + (plot.Width * 0.64)), (float)(plot.Y + (plot.Height * 0.48)))));
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
    }
}
