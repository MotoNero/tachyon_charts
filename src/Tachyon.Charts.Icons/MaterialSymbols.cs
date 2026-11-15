using System.Globalization;
using System.IO.Compression;
using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Icons;

/// <summary>
/// Material Symbols (Outlined) as signs of annotations. Each symbol is a glyph of the font, drawn through the glyph atlas
/// of the charts: sharp at every size and in the color of the sign. The font registers in the font registry on the
/// first use of this class.
/// </summary>
public static class MaterialSymbols
{
    /// <summary>The family name of the font, for a <see cref="TextFont"/>.</summary>
    public const string Family = "Material Symbols Outlined";

    /// <summary>The version of the font.</summary>
    public const string Version = "2.978";

    private static readonly Lazy<Dictionary<string, string>> _glyphs = new(ReadGlyphs);
    private static readonly Lazy<string[]> _names = new(() => [.. _glyphs.Value.Keys.Order(StringComparer.Ordinal)]);
    private static readonly Lazy<Dictionary<string, string>> _paths = new(ReadPaths);

#pragma warning disable S3963 // The registration must run before any symbol is drawn, which a field initializer cannot ensure.
    static MaterialSymbols() => FontRegistry.Register(Family, ReadFont);
#pragma warning restore S3963

    /// <summary>Gets the names of all symbols, such as <c>warning</c> or <c>thermostat</c>, in order.</summary>
    public static IReadOnlyList<string> Names => _names.Value;

    /// <summary>Gets the font of the symbols.</summary>
    public static TextFont Font => new(Family);

    /// <summary>Makes the font known to the text engine, for symbols that arrive without going through this class, such as from a saved file.</summary>
    public static void Register()
    {
        // The static constructor does the work; calling a member is enough.
    }

    /// <summary>Finds the character of a symbol.</summary>
    /// <param name="name">The name, such as <c>warning</c>.</param>
    /// <param name="glyph">Receives the character in the font.</param>
    /// <returns>Whether the symbol exists.</returns>
    public static bool TryGetGlyph(string name, out string glyph)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _glyphs.Value.TryGetValue(name, out glyph!);
    }

    /// <summary>Makes the sign of an annotation from a symbol.</summary>
    /// <param name="name">The name, such as <c>warning</c>; see <see cref="Names"/>.</param>
    /// <param name="size">The size in device-independent units, or <see langword="null"/> for the height of the label.</param>
    /// <returns>The symbol.</returns>
    /// <exception cref="ArgumentException">There is no symbol of that name.</exception>
    public static AnnotationSymbol Symbol(string name, float? size = null) =>
        TryGetGlyph(name, out var glyph) ? new AnnotationSymbol(glyph, Font, size) : throw new ArgumentException($"There is no symbol '{name}'.", nameof(name));

    /// <summary>
    /// Gets the outline of a symbol as SVG path data in a box of 24 × 24, for previews in a user interface.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The path data, or <see langword="null"/> for an unknown name.</returns>
    public static string? PathData(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _paths.Value.GetValueOrDefault(name);
    }

    // Each line names a symbol and its outline, made beforehand from the font by tools/icons/make-paths.cs.
    private static Dictionary<string, string> ReadPaths()
    {
        using var stream = typeof(MaterialSymbols).Assembly.GetManifestResourceStream("MaterialSymbolsOutlined.paths.gz")!;
        using var unpacked = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(unpacked);
        var paths = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab > 0)
            {
                paths.TryAdd(line[..tab], line[(tab + 1)..]);
            }
        }

        return paths;
    }

    private static byte[] ReadFont()
    {
        using var stream = typeof(MaterialSymbols).Assembly.GetManifestResourceStream("MaterialSymbolsOutlined.ttf.gz")!;
        using var unpacked = new GZipStream(stream, CompressionMode.Decompress);
        using var memory = new MemoryStream();
        unpacked.CopyTo(memory);
        return memory.ToArray();
    }

    // Each line names a symbol and its code point in hexadecimal, such as "warning f083".
    private static Dictionary<string, string> ReadGlyphs()
    {
        using var stream = typeof(MaterialSymbols).Assembly.GetManifestResourceStream("MaterialSymbolsOutlined.codepoints")!;
        using var reader = new StreamReader(stream);
        var glyphs = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                glyphs.TryAdd(parts[0], char.ConvertFromUtf32(code));
            }
        }

        return glyphs;
    }
}
