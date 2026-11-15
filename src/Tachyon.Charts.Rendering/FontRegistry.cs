using System.Collections.Concurrent;

namespace Tachyon.Charts.Rendering;

/// <summary>
/// Fonts that packages bring along, such as a font of symbols, found by their family name in a <see cref="TextFont"/>
/// before the fonts installed. Text engines read them here.
/// </summary>
public static class FontRegistry
{
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> _fonts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a family; the data are read on the first use, and a family registered again keeps the first.</summary>
    /// <param name="family">The family name.</param>
    /// <param name="fontData">Reads the content of a TrueType or OpenType font file.</param>
    public static void Register(string family, Func<byte[]> fontData)
    {
        ArgumentException.ThrowIfNullOrEmpty(family);
        ArgumentNullException.ThrowIfNull(fontData);
        _fonts.TryAdd(family, new Lazy<byte[]>(fontData));
    }

    /// <summary>Finds the font of a registered family.</summary>
    /// <param name="family">The family name.</param>
    /// <param name="fontData">Receives the content of the font file.</param>
    /// <returns>Whether the family is registered.</returns>
    public static bool TryGet(string family, out byte[] fontData)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (_fonts.TryGetValue(family, out var font))
        {
            fontData = font.Value;
            return true;
        }

        fontData = [];
        return false;
    }
}
