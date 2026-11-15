using System.Globalization;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Geo;

/// <summary>
/// Where the tiles of a map come from: a server the application names, with the notice of its source that the map shows.
/// Tachyon Charts sets no server and no key; the application answers for the licence and the terms of use of its server.
/// </summary>
public sealed class TileSource
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Initializes a new instance of the <see cref="TileSource"/> class for a server.</summary>
    /// <param name="urlTemplate">The address of a tile, with <c>{z}</c>, <c>{x}</c> and <c>{y}</c>, such as <c>https://tiles.example.org/{z}/{x}/{y}.png</c>.</param>
    /// <param name="attribution">The notice of the source the map shows, as the server asks for it.</param>
    public TileSource(string urlTemplate, string attribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(urlTemplate);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribution);
        if (!urlTemplate.Contains("{z}", StringComparison.Ordinal) || !urlTemplate.Contains("{x}", StringComparison.Ordinal) || !urlTemplate.Contains("{y}", StringComparison.Ordinal))
        {
            throw new ArgumentException("The address needs {z}, {x} and {y}.", nameof(urlTemplate));
        }

        (UrlTemplate, Attribution) = (urlTemplate, attribution);
    }

    private TileSource(string attribution, Func<int, int, int, TileImage?> paint)
    {
        (UrlTemplate, Attribution, Paint) = (string.Empty, attribution, paint);
    }

    /// <summary>Gets the address of a tile, empty for tiles drawn locally.</summary>
    public string UrlTemplate { get; }

    /// <summary>Gets the notice of the source the map shows.</summary>
    public string Attribution { get; }

    /// <summary>Gets or sets the deepest level of tiles the server has; deeper views enlarge it.</summary>
    public int MaxZoom { get; init; } = 19;

    /// <summary>Gets or sets the name the requests give of the application, as many servers ask for.</summary>
    public string UserAgent { get; init; } = "Tachyon.Charts";

    /// <summary>Gets or sets a folder that keeps the tiles between runs, or <see langword="null"/> to keep them in memory only.</summary>
    public string? CacheFolder { get; init; }

    /// <summary>Gets or sets how many tiles stay in memory.</summary>
    public int MemoryTiles { get; init; } = 512;

    /// <summary>
    /// Gets or sets how a tile is read, such as with a key or another client; <see langword="null"/> asks the address
    /// with HTTP GET.
    /// </summary>
    public Func<Uri, CancellationToken, Task<byte[]>>? Fetch { get; init; }

    /// <summary>Gets or sets how a tile is decoded; the default reads PNG. JPEG tiles need a decoder of the application.</summary>
    public Func<ReadOnlyMemory<byte>, TileImage?> Decode { get; init; } = bytes => PngDecoder.Decode(bytes.Span);

    /// <summary>Gets how tiles drawn locally are made, or <see langword="null"/> for a server.</summary>
    internal Func<int, int, int, TileImage?>? Paint { get; }

    /// <summary>
    /// Gets a source of tiles drawn locally: a light grid with the border of each tile, to see how tiles load and stack
    /// without a server. It shows on the map as „Grid of tiles drawn locally“.
    /// </summary>
    /// <returns>The source.</returns>
    public static TileSource Grid() => new("Grid of tiles drawn locally", PaintGrid);

    /// <summary>Gets the address of a tile.</summary>
    /// <param name="z">The level.</param>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <returns>The address.</returns>
    public Uri AddressOf(int z, int x, int y) => new(UrlTemplate
        .Replace("{z}", z.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{x}", x.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
        .Replace("{y}", y.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));

    /// <summary>Reads a tile: drawn locally, from the folder, or from the server, kept in the folder afterwards.</summary>
    internal async Task<TileImage?> LoadAsync(int z, int x, int y, CancellationToken cancellation)
    {
        if (Paint is { } paint)
        {
            return paint(z, x, y);
        }

        var file = CacheFolder is { } folder ? Path.Combine(folder, z.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".tile") : null;
        if (file is not null && File.Exists(file))
        {
            return Decode(await File.ReadAllBytesAsync(file, cancellation).ConfigureAwait(false));
        }

        var bytes = Fetch is { } fetch ? await fetch(AddressOf(z, x, y), cancellation).ConfigureAwait(false) : await Download(AddressOf(z, x, y), cancellation).ConfigureAwait(false);
        if (file is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllBytesAsync(file, bytes, cancellation).ConfigureAwait(false);
        }

        return Decode(bytes);
    }

    private async Task<byte[]> Download(Uri address, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await _client.SendAsync(request, cancellation).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellation).ConfigureAwait(false);
    }

    // A tile mostly clear, with its border and a soft tint that changes with the level, so the stacking shows.
    private static TileImage PaintGrid(int z, int x, int y)
    {
        const int Size = 256;
        var pixels = new Rgba8[Size * Size];
        var tint = new Rgba8((byte)(60 + ((z * 37) % 120)), (byte)(90 + ((x * 23) % 100)), (byte)(140 + ((y * 41) % 100)), 40);
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                var edge = row < 2 || column < 2;
                pixels[(row * Size) + column] = edge ? new Rgba8(40, 60, 90, 200) : tint;
            }
        }

        return new TileImage(pixels, Size, Size);
    }
}
