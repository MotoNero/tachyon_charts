using System.Globalization;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Colors;

/// <summary>
/// A table of colors that values are mapped to.
/// </summary>
public sealed class Colormap
{
    /// <summary>The number of entries of every table.</summary>
    public const int EntryCount = 256;

    private readonly Rgba8[] _entries;

    private Colormap(string name, Rgba8[] entries)
    {
        Name = name;
        _entries = entries;
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Gets the colors, from the lowest value to the highest.</summary>
    public ReadOnlySpan<Rgba8> Entries => _entries;

    /// <summary>
    /// Creates a colormap that runs through colors at equal distances.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="stops">The colors, at least two.</param>
    /// <returns>The colormap.</returns>
    /// <exception cref="ArgumentException">There are fewer than two colors.</exception>
    public static Colormap FromColors(string name, ReadOnlySpan<Rgba8> stops)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (stops.Length < 2)
        {
            throw new ArgumentException("A colormap needs at least two colors.", nameof(stops));
        }

        var entries = new Rgba8[EntryCount];
        for (var i = 0; i < EntryCount; i++)
        {
            var position = i * (stops.Length - 1) / (double)(EntryCount - 1);
            var first = Math.Min((int)position, stops.Length - 2);
            var fraction = position - first;
            entries[i] = Mix(stops[first], stops[first + 1], fraction);
        }

        return new Colormap(name, entries);
    }

    /// <summary>
    /// Finds the color of a position, the same way the renderers do.
    /// </summary>
    /// <param name="position">The position from 0 to 1. Others count as the nearest end.</param>
    /// <returns>The color.</returns>
    public Rgba8 Sample(double position)
    {
        var index = double.IsNaN(position) ? 0 : (int)Math.Floor((Math.Clamp(position, 0, 1) * (EntryCount - 1)) + 0.5);
        return _entries[index];
    }

    internal static Colormap FromEntries(string name, Rgba8[] entries) => new(name, entries);

    internal static Colormap FromHex(string name, string hex)
    {
        var entries = new Rgba8[EntryCount];
        for (var i = 0; i < EntryCount; i++)
        {
            var value = int.Parse(hex.AsSpan(i * 6, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            entries[i] = new Rgba8((byte)(value >> 16), (byte)(value >> 8), (byte)value, 255);
        }

        return new Colormap(name, entries);
    }

    private static Rgba8 Mix(Rgba8 first, Rgba8 second, double fraction)
    {
        return new Rgba8(Channel(first.R, second.R), Channel(first.G, second.G), Channel(first.B, second.B), Channel(first.A, second.A));

        byte Channel(byte from, byte to) => (byte)Math.Round(from + ((to - from) * fraction));
    }
}

/// <summary>
/// The predefined colormaps.
/// </summary>
public static class Colormaps
{
    /// <summary>Gets the perceptually uniform map from purple over green to yellow.</summary>
    public static Colormap Viridis { get; } = Colormap.FromHex(nameof(Viridis), ColormapTables.Viridis);

    /// <summary>Gets the perceptually uniform map from black over red to pale yellow.</summary>
    public static Colormap Inferno { get; } = Colormap.FromHex(nameof(Inferno), ColormapTables.Inferno);

    /// <summary>Gets the perceptually uniform map from black over magenta to pale yellow.</summary>
    public static Colormap Magma { get; } = Colormap.FromHex(nameof(Magma), ColormapTables.Magma);

    /// <summary>Gets the perceptually uniform map from blue over magenta to yellow.</summary>
    public static Colormap Plasma { get; } = Colormap.FromHex(nameof(Plasma), ColormapTables.Plasma);

    /// <summary>Gets the rainbow map with high contrast, without being perceptually uniform.</summary>
    public static Colormap Turbo { get; } = Colormap.FromHex(nameof(Turbo), ColormapTables.Turbo);

    /// <summary>Gets the map from black to white.</summary>
    public static Colormap Grayscale { get; } = Colormap.FromColors(nameof(Grayscale), [new Rgba8(0, 0, 0, 255), new Rgba8(255, 255, 255, 255)]);

    /// <summary>Gets all predefined colormaps.</summary>
    public static IReadOnlyList<Colormap> All { get; } = [Viridis, Inferno, Magma, Plasma, Turbo, Grayscale];
}

/// <summary>
/// Keeps a colormap as a texture, so that a change of the colormap is a change of this texture only.
/// </summary>
public sealed class ColormapTexture : IDisposable
{
    private IGpuTexture? _texture;
    private Colormap? _colormap;

    /// <summary>
    /// Brings the texture up to date. Writes only if the colormap changed.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="colormap">The colormap.</param>
    /// <returns>The texture, one row of <see cref="Colormap.EntryCount"/> pixels.</returns>
    public IGpuTexture Update(IRenderDevice device, Colormap colormap)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(colormap);
        _texture ??= device.CreateTexture(new TextureDesc(new PixelSize(Colormap.EntryCount, 1), TextureFormat.Rgba8Unorm));
        if (!ReferenceEquals(_colormap, colormap))
        {
            _texture.Write(System.Runtime.InteropServices.MemoryMarshal.AsBytes(colormap.Entries));
            _colormap = colormap;
        }

        return _texture;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;
        _colormap = null;
    }
}
