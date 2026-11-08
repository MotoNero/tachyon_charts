using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>A real chest CT, downloaded on request; the synthetic thorax until then.</summary>
public static class MedicalChestCtPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Medical chest CT (real data)";

    /// <summary>How to operate the charts, with the source and the license of the data.</summary>
    public const string Hint = "Button 'Download data': loads the real data, the phantom shows until then | Data: LIDC-IDRI, The Cancer Imaging Archive, doi:10.7937/K9/TCIA.2015.LO9QL9SX, CC BY 3.0; cite the data, never identify a person | Wheel: slices | Drag: window/level";

    /// <summary>Gets the three linked 2D chart descriptions.</summary>
    public static IReadOnlyList<Action<ChartBuilder>> Slices => MedicalSampleData.Thorax.Slices;

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public static void Configure(Chart3DBuilder chart) => MedicalSampleData.Thorax.Configure(chart);

    /// <summary>Downloads the real data on request.</summary>
    /// <param name="report">Receives a line on the progress.</param>
    /// <returns>The download.</returns>
    public static Task DownloadAsync(IProgress<string> report) => MedicalSampleData.Thorax.DownloadAsync(report);
}
