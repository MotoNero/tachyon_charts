using System.Globalization;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Medical;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A real dataset for a showroom page, downloaded only on request: until then the page shows the synthetic phantom of
/// the same region. The data land in the local application data of the user, never in the application folder.
/// </summary>
public sealed class MedicalSampleData
{
    private readonly Func<MedicalVolume> _phantom;
    private readonly bool _isLung;
    private MedicalMprDisplay? _display;

    private MedicalSampleData(MedicalSampleDataset dataset, Func<MedicalVolume> phantom, bool isLung)
    {
        (Dataset, _phantom, _isLung) = (dataset, phantom, isLung);
        Slices =
        [
            chart => Display.Axial.Configure(chart),
            chart => Display.Coronal.Configure(chart),
            chart => Display.Sagittal.Configure(chart),
        ];
    }

    /// <summary>Gets the chest CT page.</summary>
    public static MedicalSampleData Thorax { get; } = new(MedicalSampleDataset.ThoraxCt, MedicalPhantoms.Thorax, isLung: true);

    /// <summary>Gets the head MRI page.</summary>
    public static MedicalSampleData Head { get; } = new(MedicalSampleDataset.HeadMri, MedicalPhantoms.Head, isLung: false);

    /// <summary>Gets the folder the data land in.</summary>
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "SampleData");

    /// <summary>Gets the dataset.</summary>
    public MedicalSampleDataset Dataset { get; }

    /// <summary>Gets a value indicating whether the real data are there.</summary>
    public bool HasData => Dataset.IsDownloaded(Folder);

    /// <summary>Gets the three linked 2D chart descriptions.</summary>
    public IReadOnlyList<Action<ChartBuilder>> Slices { get; }

    private MedicalMprDisplay Display => _display ??= Create();

    /// <summary>Describes the linked 3D volume.</summary>
    /// <param name="chart">The chart.</param>
    public void Configure(Chart3DBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        Display.Configure3D(chart);
        chart.Title(HasData ? Dataset.Name + " · " + Dataset.License : "Phantom · " + Dataset.Name + " not downloaded");
    }

    /// <summary>Downloads the data and shows them the next time the page is built.</summary>
    /// <param name="report">Receives a line on the progress.</param>
    /// <returns>The download.</returns>
    public async Task DownloadAsync(IProgress<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var progress = new Progress<MedicalDownloadProgress>(step => report.Report(string.Create(
            CultureInfo.InvariantCulture,
            $"{Dataset.Name}: {step.Bytes / 1e6:F1} MB{(step.Share is { } share ? $" ({share * 100:F0} %)" : string.Empty)}")));
        await Dataset.DownloadAsync(Folder, progress).ConfigureAwait(true);
        _display = null;
    }

    private MedicalMprDisplay Create()
    {
        var display = new MedicalMprDisplay(HasData ? Dataset.Read(Folder) : _phantom());
        if (_isLung)
        {
            display.ApplyPreset(CtWindowPresets.Lung);
        }
        else if (HasData)
        {
            // A T1 image brings no window; grey and white matter lie in the lower half of its values.
            display.ApplyPreset(new MedicalWindow("T1", 300, 600));
        }

        return display;
    }
}
