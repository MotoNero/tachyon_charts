using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// A named scene that sample apps, snapshot tool and tests render identically.
/// </summary>
public interface IScene
{
    /// <summary>
    /// Gets the scene name, also the name of its snapshot file.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the size to render at.
    /// </summary>
    PixelSize Size { get; }

    /// <summary>
    /// Renders one frame.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="target">The render target of <paramref name="device"/>.</param>
    void Render(IRenderDevice device, IRenderTarget target);
}

/// <summary>
/// A scene that draws one chart and hands it out, so that the chart can be compared with the same page in the
/// configurator.
/// </summary>
public interface IChartScene
{
    /// <summary>Creates the chart of the scene, as the scene draws it.</summary>
    /// <param name="disposables">Receives what ends with the chart.</param>
    /// <returns>The chart.</returns>
    ChartSurface CreateChart(out IReadOnlyList<IDisposable> disposables);

    /// <summary>Creates the interactions the scene shows, none for a still picture.</summary>
    /// <returns>The interactions.</returns>
    IEnumerable<Core.Input.ChartModifier> CreateInteractions() => [];
}
