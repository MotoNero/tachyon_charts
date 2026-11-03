using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Cloud3DLoading“ of the snapshot tests.</summary>
public static class Cloud3DLoadingPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Cloud3DLoading";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => CloudOnItsWay(c);

    /// <summary>Describes a cloud whose points are still on their way, which shows the progress in the chart.</summary>
    /// <param name="chart">The chart.</param>
    public static void CloudOnItsWay(Chart3DBuilder chart)
    {
        var cloud = new PointCloud3D(_ =>
        {
            Thread.Sleep(3000);
            return new PointCloudArrays([0], [0], [0]);
        });
        chart.Title("Scanned landscape").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddCloud(cloud);
    }
}
