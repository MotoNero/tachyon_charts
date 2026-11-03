using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.AntennaPatternPage;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Antenna3DPole“ of the snapshot tests.</summary>
public static class Antenna3DPolePicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Antenna3DPole";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        AntennaChart(c);
        c.Scene.Camera.Show(CameraView.Top);
    }
}
