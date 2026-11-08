using System.Globalization;
using Tachyon.Charts.Medical;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// The volumes a medical page of the showroom can show: the synthetic phantoms, always there, and the real datasets,
/// downloaded only when chosen. The data land in the local application data of the user, never in the application folder.
/// </summary>
public sealed class MedicalDataChoice : IPageData
{
    private readonly Source[] _sources;
    private readonly MedicalVolume?[] _volumes;

    /// <summary>Initializes a choice that starts with the head or the thorax phantom.</summary>
    /// <param name="startsWithThorax">Whether the thorax phantom comes first.</param>
    public MedicalDataChoice(bool startsWithThorax)
    {
        Source head = new("Head phantom (synthetic)", MedicalPhantoms.Head, null, CtWindowPresets.Brain);
        Source thorax = new("Thorax phantom (synthetic)", MedicalPhantoms.Thorax, null, default);
        _sources =
        [
            startsWithThorax ? thorax : head,
            startsWithThorax ? head : thorax,
            new("Chest CT, LIDC-IDRI (CC BY 3.0)", null, MedicalSampleDataset.ThoraxCt, CtWindowPresets.Lung),
            new("Head MRI, OpenNeuro ds000001 (CC0)", null, MedicalSampleDataset.HeadMri, default),
        ];
        _volumes = new MedicalVolume?[_sources.Length];
    }

    /// <summary>Gets the folder the real data land in.</summary>
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tachyon", "SampleData");

    /// <summary>Gets the names to choose from.</summary>
    public IReadOnlyList<string> Names => [.. _sources.Select(source => source.Name)];

    /// <summary>Gets the chosen one.</summary>
    public int Index { get; private set; }

    /// <summary>Gets the name of the chosen one.</summary>
    public string Name => _sources[Index].Name;

    /// <summary>Gets a number that changes with each choice, so that pages know when to build anew.</summary>
    public int Version { get; private set; }

    /// <summary>Creates a display of the chosen volume with a window that suits it.</summary>
    /// <returns>The display.</returns>
    public MedicalMprDisplay CreateDisplay()
    {
        var source = _sources[Index];
        var volume = _volumes[Index] ??= source.Phantom is { } phantom ? phantom() : source.Dataset!.Read(Folder);
        var display = new MedicalMprDisplay(volume);
        if (source.Window.Width > 0)
        {
            display.ApplyPreset(source.Window);
        }
        else if (source.Dataset is not null)
        {
            // An MRI brings no window: the tissue lies in the lower half of its values.
            var range = volume.Data.ValueRange;
            display.ApplyPreset(new MedicalWindow("MRI", range.Min + (range.Length * 0.25), range.Length * 0.5));
        }

        return display;
    }

    /// <summary>Chooses a volume, downloading a real dataset first when it is not there yet.</summary>
    /// <param name="index">The choice.</param>
    /// <param name="report">Receives a line on the progress of a download.</param>
    /// <returns>A line on the source and the license of what is shown now.</returns>
    public async Task<string> ChooseAsync(int index, IProgress<string> report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _sources.Length);
        if (_sources[index].Dataset is { } dataset)
        {
            var progress = new Progress<MedicalDownloadProgress>(step => report.Report(string.Create(
                CultureInfo.InvariantCulture,
                $"{dataset.Name}: {step.Bytes / 1e6:F1} MB{(step.Share is { } share ? $" ({share * 100:F0} %)" : string.Empty)}")));
            await dataset.DownloadAsync(Folder, progress).ConfigureAwait(true);
        }

        (Index, Version) = (index, Version + 1);
        if (_sources[index].Dataset is not { } shown)
        {
            return _sources[index].Name + ": no data of any person.";
        }

        var rules = shown.TermsUri is { } terms ? " | Terms: " + terms : string.Empty;
        return "Source: " + shown.Citation + " | License: " + shown.License + rules;
    }

    private readonly record struct Source(string Name, Func<MedicalVolume>? Phantom, MedicalSampleDataset? Dataset, MedicalWindow Window);
}
