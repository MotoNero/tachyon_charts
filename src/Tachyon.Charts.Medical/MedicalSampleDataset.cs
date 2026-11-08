using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tachyon.Charts.Medical;

/// <summary>How far a download is.</summary>
/// <param name="Bytes">The bytes received.</param>
/// <param name="Total">The bytes expected, when the server tells.</param>
public readonly record struct MedicalDownloadProgress(long Bytes, long? Total)
{
    /// <summary>Gets the share received from 0 to 1, or <see langword="null"/> when the total is unknown.</summary>
    public double? Share => Total is > 0 ? Math.Min(1, Bytes / (double)Total.Value) : null;
}

/// <summary>
/// A real, freely licensed and already anonymized dataset for trying the package. Nothing is part of the package: a
/// dataset is downloaded only when <see cref="DownloadAsync"/> or <see cref="LoadAsync"/> is called, into a folder the
/// application chooses, and checked against a SHA-256 checksum. Show <see cref="License"/>, <see cref="Citation"/> and,
/// where given, <see cref="TermsUri"/> to the user before the download, as the licenses ask for attribution.
/// </summary>
public sealed class MedicalSampleDataset
{
    private static readonly Lazy<HttpClient> _client = new(() => new HttpClient { Timeout = TimeSpan.FromMinutes(10) });

    private readonly bool _isZip;

    internal MedicalSampleDataset(string folderName, string name, string license, Uri licenseUri, string citation, Uri sourceUri, Uri? termsUri, Uri downloadUri, string sha256, long size, bool isZip)
    {
        (FolderName, Name, License, LicenseUri, Citation, SourceUri, TermsUri, DownloadUri, Sha256, Size, _isZip) =
            (folderName, name, license, licenseUri, citation, sourceUri, termsUri, downloadUri, sha256, size, isZip);
    }

    /// <summary>
    /// Gets a chest CT: LIDC-IDRI, patient LIDC-IDRI-0001, 133 slices, a download of 35 MB that unpacks to 70 MB
    /// of DICOM files from The Cancer Imaging Archive, licensed CC BY 3.0. The archive's data usage policy applies: cite the data and never try to identify a
    /// person.
    /// </summary>
    public static MedicalSampleDataset ThoraxCt { get; } = Load("ThoraxCt");

    /// <summary>
    /// Gets a head MRI: OpenNeuro ds000001, subject 01, T1-weighted, 5.7 MB as NIfTI, dedicated to the public domain
    /// (CC0). The face was removed before publication.
    /// </summary>
    public static MedicalSampleDataset HeadMri { get; } = Load("HeadMri");

    /// <summary>Gets all datasets.</summary>
    public static IReadOnlyList<MedicalSampleDataset> All { get; } = [ThoraxCt, HeadMri];

    /// <summary>Gets the name to show.</summary>
    public string Name { get; }

    /// <summary>Gets the name of the license.</summary>
    public string License { get; }

    /// <summary>Gets the text of the license.</summary>
    public Uri LicenseUri { get; }

    /// <summary>Gets how to cite the data.</summary>
    public string Citation { get; }

    /// <summary>Gets the page of the dataset.</summary>
    public Uri SourceUri { get; }

    /// <summary>Gets the terms of use of the archive that holds the data, or <see langword="null"/> when there are none beyond the license.</summary>
    public Uri? TermsUri { get; }

    /// <summary>Gets where the data come from.</summary>
    public Uri DownloadUri { get; }

    /// <summary>
    /// Gets the SHA-256 checksum, lower case: of the file, or for an archive packed anew on each download, of the name
    /// and bytes of each entry in ordinal order of the names.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>Gets about how many bytes the download takes.</summary>
    public long Size { get; }

    /// <summary>Gets the name of the subfolder the dataset lands in.</summary>
    public string FolderName { get; }

    /// <summary>Gets a value indicating whether the dataset is in a folder and was checked.</summary>
    /// <param name="folder">The folder of the application.</param>
    /// <returns><see langword="true"/> when it is there.</returns>
    public bool IsDownloaded(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var marker = Path.Combine(folder, FolderName, ".sha256");
        return File.Exists(marker) && File.ReadAllText(marker).Trim() == Sha256;
    }

    /// <summary>Downloads the dataset into a subfolder of a folder, unless it is already there, and checks it.</summary>
    /// <param name="folder">The folder of the application; created when missing.</param>
    /// <param name="progress">Receives how far the download is.</param>
    /// <param name="cancellation">Stops the download.</param>
    /// <returns>The subfolder with the data.</returns>
    /// <exception cref="InvalidDataException">The data do not match the checksum; nothing is kept.</exception>
    /// <exception cref="HttpRequestException">The server could not be reached or refused.</exception>
    public Task<string> DownloadAsync(string folder, IProgress<MedicalDownloadProgress>? progress = null, CancellationToken cancellation = default) =>
        DownloadWithAsync(folder, _client.Value, progress, cancellation);

    /// <summary>Downloads the dataset when needed and reads it.</summary>
    /// <param name="folder">The folder of the application.</param>
    /// <param name="progress">Receives how far the download is.</param>
    /// <param name="cancellation">Stops the download.</param>
    /// <returns>The volume.</returns>
    public async Task<MedicalVolume> LoadAsync(string folder, IProgress<MedicalDownloadProgress>? progress = null, CancellationToken cancellation = default)
    {
        var target = await DownloadAsync(folder, progress, cancellation).ConfigureAwait(false);
        return await Task.Run(() => ReadFrom(target), cancellation).ConfigureAwait(false);
    }

    /// <summary>Reads the dataset after it was downloaded into a folder.</summary>
    /// <param name="folder">The folder of the application.</param>
    /// <returns>The volume.</returns>
    /// <exception cref="InvalidOperationException">The dataset is not there; see <see cref="IsDownloaded"/>.</exception>
    public MedicalVolume Read(string folder) => IsDownloaded(folder)
        ? ReadFrom(Path.Combine(folder, FolderName))
        : throw new InvalidOperationException($"{Name} has not been downloaded into {folder}.");

    /// <inheritdoc />
    public override string ToString() => Name;

    // The addresses, checksums and licenses are data, kept in a file beside the code.
    private static MedicalSampleDataset Load(string key)
    {
        using var stream = typeof(MedicalSampleDataset).Assembly.GetManifestResourceStream("Tachyon.Charts.Medical.SampleDatasets.json")
            ?? throw new InvalidOperationException("The list of sample datasets is missing.");
        using var document = JsonDocument.Parse(stream);
        var entry = document.RootElement.GetProperty(key);
        string Text(string name) => entry.GetProperty(name).GetString() ?? throw new InvalidOperationException($"The sample dataset {key} has no {name}.");
        var terms = entry.GetProperty("termsUri").GetString();
        return new MedicalSampleDataset(
            Text("folderName"),
            Text("name"),
            Text("license"),
            new Uri(Text("licenseUri")),
            Text("citation"),
            new Uri(Text("sourceUri")),
            terms is null ? null : new Uri(terms),
            new Uri(Text("downloadUri")),
            Text("sha256"),
            entry.GetProperty("size").GetInt64(),
            entry.GetProperty("isZip").GetBoolean());
    }

    internal async Task<string> DownloadWithAsync(string folder, HttpClient client, IProgress<MedicalDownloadProgress>? progress, CancellationToken cancellation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var target = Path.Combine(folder, FolderName);
        if (IsDownloaded(folder))
        {
            return target;
        }

        Directory.CreateDirectory(folder);
        var part = Path.Combine(folder, FolderName + ".part");
        try
        {
            using (var response = await client.GetAsync(DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                await using var file = File.Create(part);
                var buffer = new byte[1 << 16];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                    received += read;
                    progress?.Report(new MedicalDownloadProgress(received, total));
                }
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.CreateDirectory(target);
            var checksum = _isZip ? Extract(part, target) : Keep(part, target);
            if (checksum != Sha256)
            {
                Directory.Delete(target, recursive: true);
                throw new InvalidDataException($"The download of {Name} does not match its checksum: {checksum} instead of {Sha256}.");
            }

            await File.WriteAllTextAsync(Path.Combine(target, ".sha256"), Sha256, cancellation).ConfigureAwait(false);
            return target;
        }
        finally
        {
            File.Delete(part);
        }
    }

    internal MedicalVolume ReadFrom(string target)
    {
        if (_isZip)
        {
            var files = Directory.GetFiles(target, "*.dcm");
            var streams = new List<Stream>(files.Length);
            try
            {
                foreach (var file in files)
                {
                    streams.Add(File.OpenRead(file));
                }

                return DicomSeriesReader.Read(streams);
            }
            finally
            {
                streams.ForEach(stream => stream.Dispose());
            }
        }

        using var image = File.OpenRead(Path.Combine(target, Path.GetFileName(DownloadUri.AbsolutePath)));
        return NiftiImageReader.Read(image);
    }

    // The checksum of an archive covers its entries, not the packing; only plain files are unpacked, flat.
    private static string Extract(string archive, string target)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var zip = ZipFile.OpenRead(archive);
        foreach (var entry in zip.Entries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            if (entry.FullName.EndsWith('/'))
            {
                continue;
            }

            hash.AppendData(Encoding.UTF8.GetBytes(entry.FullName));
            using var content = new MemoryStream();
            using (var stream = entry.Open())
            {
                stream.CopyTo(content);
            }

            hash.AppendData(content.GetBuffer(), 0, (int)content.Length);
            File.WriteAllBytes(Path.Combine(target, Path.GetFileName(entry.FullName)), content.ToArray());
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private string Keep(string part, string target)
    {
        string checksum;
        using (var file = File.OpenRead(part))
        {
            checksum = Convert.ToHexStringLower(SHA256.HashData(file));
        }

        File.Copy(part, Path.Combine(target, Path.GetFileName(DownloadUri.AbsolutePath)), overwrite: true);
        return checksum;
    }
}
