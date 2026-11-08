using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>A real, defaced head MRI, downloaded on request; the synthetic head until then.</summary>
public static class MedicalHeadMriPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Medical head MRI (real data)";

    /// <summary>How to operate the charts, with the source and the license of the data.</summary>
    public const string Hint = "Button 'Download data': loads the real data, the phantom shows until then | Data: OpenNeuro ds000001, doi:10.18112/openneuro.ds000001.v1.0.0, CC0, face removed | Wheel: slices | Drag: window/level";

    /// <summary>Gets the three linked 2D chart descriptions.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Slices => MedicalSampleData.Head.Slices;

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart) => MedicalSampleData.Head.Configure(chart);

    /// <summary>Downloads the real data on request.</summary>
    /// <param name="report">Receives a line on the progress.</param>
    /// <returns>The download.</returns>
    public static Task DownloadAsync(IProgress<string> report) => MedicalSampleData.Head.DownloadAsync(report);
}
