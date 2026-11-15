using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>
/// The 3D page „Cloud from a file“ of the showroom: 200 million points of a scanned landscape from a file of 2.4 GB,
/// sorted once into pages; a sample of every page shows at once, the pages in view come onto the GPU as the view needs
/// them. The first visit writes the file and sorts its pages in the background.
/// </summary>
public static class CloudFromAFilePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Cloud from a file";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag: turn | Shift+drag: move | Wheel: zoom | Arrow keys: turn | Double click, Home: start | The first visit writes a test file of 2.4 GB";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => c
        .Title(SampleText.T("200 million points from a file"))
        .XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
        .AddCloud(new PointCloud3D(CloudPagesDemo.Open(CloudPagesDemo.PageCount)) { HeightColors = Colormaps.Viridis, PointSize = 2 });
}
