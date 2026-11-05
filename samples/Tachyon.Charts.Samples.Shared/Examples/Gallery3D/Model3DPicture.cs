using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Model3D“ of the snapshot tests: a pipe bend from an STL file with measured values, cut open.</summary>
public static class Model3DPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Model3D";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => ModelWithMeasuringPointsPage.Configure(c);
}
