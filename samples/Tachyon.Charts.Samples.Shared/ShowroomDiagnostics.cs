using System.Globalization;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>How a page of the showroom shows the diagnostics that F12 switches.</summary>
public enum DiagnosticsView
{
    /// <summary>A heading of the list, not a page.</summary>
    None,

    /// <summary>The page holds running charts, each shows the diagnostics overlay.</summary>
    Overlay,

    /// <summary>The page shows a still image; one line tells how long it took to render.</summary>
    RenderTime,
}

/// <summary>What both showroom apps share for the diagnostics: which page shows them how, and the line under a still image.</summary>
public static class ShowroomDiagnostics
{
    /// <summary>The pages with running charts that the apps build themselves, not from a catalog.</summary>
    public static IReadOnlyList<string> FixedPages { get; } =
    [
        "Live-Chart (interaktiv)",
        "Gruppe (3 Charts)",
        "Raster (interaktiv)",
        "Oszilloskop (interaktiv)",
        "Spektrum (interaktiv)",
    ];

    /// <summary>Finds how a page shows the diagnostics.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The view, or <see cref="DiagnosticsView.None"/> for a heading or an unknown page.</returns>
    public static DiagnosticsView Find(SamplePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.IsHeader)
        {
            return DiagnosticsView.None;
        }

        if (FixedPages.Contains(page.Id) || InstrumentPages.TryGet(page.Id, out _) || Pages3D.Find(page.Id) is not null)
        {
            return DiagnosticsView.Overlay;
        }

        return SceneCatalog.Find(page.Id) is not null ? DiagnosticsView.RenderTime : DiagnosticsView.None;
    }

    /// <summary>Writes the line under a still image.</summary>
    /// <param name="milliseconds">How long the image took, from the start of the device to the pixels.</param>
    /// <param name="size">The size of the image.</param>
    /// <param name="backend">The renderer.</param>
    /// <returns>The line.</returns>
    public static string DescribeStill(double milliseconds, PixelSize size, string backend) =>
        string.Format(CultureInfo.InvariantCulture, "Still image: {0:F1} ms for {1} × {2} px | {3}", milliseconds, size.Width, size.Height, backend);
}
