using System.Numerics;
using System.Runtime.InteropServices;
using HarfBuzzSharp;
using SkiaSharp;
using Tachyon.Charts.Rendering.Skia.Text;
using Buffer = HarfBuzzSharp.Buffer;
using Font = HarfBuzzSharp.Font;

namespace Tachyon.Charts.Rendering.Skia;

/// <summary>
/// Text for all backends: HarfBuzz shapes, the font file provides the outlines, and a C# generator turns them
/// into a distance field atlas. The default font is the embedded Roboto, so that text looks the same
/// on every system. Further fonts (<see cref="TextFont"/>) share the atlas: Roboto bold and italic and Roboto Slab
/// are embedded too, other families come from the fonts installed.
/// </summary>
/// <remarks>Members are safe to use from any thread.</remarks>
public sealed class SkiaTextEngine : ITextEngine, IDisposable
{
    private const string EmbeddedFont = "Roboto-Regular.ttf";
    private const int AtlasSize = 1024;
    private const int FieldEmSize = 40;
    private const int FieldRange = 4;
    private const int CurveSegments = 8;

    // The slant of an italic made from an upright cut: the share of the height the top leans to the right.
    private const float SyntheticSlant = 0.2f;

    private const int MaximumShapedTexts = 4096;

    private static readonly Lazy<SkiaTextEngine> _default = new(() => new SkiaTextEngine(Embedded(EmbeddedFont)!, isEmbedded: true));

    private readonly Lock _gate = new();
    private readonly byte[] _fontData;
    private readonly bool _isEmbedded;
    private readonly FontFace _regular;
    private readonly Dictionary<TextFont, FontFace> _faces = [];
    private readonly List<FontFace> _owned = [];
    private readonly byte[] _atlas = new byte[AtlasSize * AtlasSize * 4];
    private readonly Dictionary<IRenderDevice, AtlasCopy> _copies = [];
    private int _shelfX = 1;
    private int _shelfY = 1;
    private int _shelfHeight;
    private long _atlasVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaTextEngine"/> class.
    /// </summary>
    /// <param name="fontData">The content of a TrueType or OpenType font file.</param>
    /// <exception cref="ArgumentException">The data is not a font.</exception>
    public SkiaTextEngine(byte[] fontData)
        : this(fontData, isEmbedded: false)
    {
    }

    private SkiaTextEngine(byte[] fontData, bool isEmbedded)
    {
        ArgumentNullException.ThrowIfNull(fontData);
        (_fontData, _isEmbedded) = (fontData, isEmbedded);
        _regular = new FontFace(fontData, 0);
        _owned.Add(_regular);
    }

    /// <summary>
    /// Gets the engine with the embedded font.
    /// </summary>
    public static SkiaTextEngine Default => _default.Value;

    /// <summary>
    /// Creates an engine with an installed font.
    /// </summary>
    /// <param name="familyName">The family name, such as Arial.</param>
    /// <returns>The engine.</returns>
    /// <exception cref="ArgumentException">The family is not installed.</exception>
    public static SkiaTextEngine FromSystemFont(string familyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(familyName);
        return new SkiaTextEngine(SystemFont(familyName, SKFontStyle.Normal, anyFamily: true)?.Data
            ?? throw new ArgumentException($"The font '{familyName}' is not installed.", nameof(familyName)));
    }

    /// <inheritdoc />
    public float GetLineHeight(float size) => size * _regular.LineHeightPerEm;

    /// <inheritdoc />
    public float GetLineHeight(float size, TextFont font)
    {
        using var scope = _gate.EnterScope();
        return size * FaceOf(font).LineHeightPerEm;
    }

    /// <inheritdoc />
    public float MeasureWidth(ReadOnlySpan<char> text, float size) => MeasureWidth(text, size, default);

    /// <inheritdoc />
    public float MeasureWidth(ReadOnlySpan<char> text, float size, TextFont font)
    {
        using var scope = _gate.EnterScope();

        var face = FaceOf(font);
        var advance = 0L;
        foreach (var glyph in face.GetShaped(text))
        {
            advance += glyph.XAdvance;
        }

        return advance * size / face.UnitsPerEm;
    }

    /// <summary>
    /// Tests whether the font has a real glyph for a character.
    /// </summary>
    public bool HasGlyph(char character)
    {
        using var scope = _gate.EnterScope();
        return _regular.Font.TryGetGlyph(character, out var glyph) && glyph != 0;
    }

    /// <inheritdoc />
    public int Layout(ReadOnlySpan<char> text, Vector2 baseline, float size, Rgba8 color, Span<GlyphInstance> glyphs) =>
        Layout(text, baseline, size, color, default, glyphs);

    /// <inheritdoc />
    public int Layout(ReadOnlySpan<char> text, Vector2 baseline, float size, Rgba8 color, TextFont font, Span<GlyphInstance> glyphs)
    {
        if (glyphs.Length < text.Length)
        {
            throw new ArgumentException("The destination is too short.", nameof(glyphs));
        }

        using var scope = _gate.EnterScope();

        var face = FaceOf(font);
        var scale = size / FieldEmSize;
        var distanceScale = 2 * FieldRange * scale;
        var shaped = face.GetShaped(text);

        // On whole pixels: the baseline, so that the feet of the letters stay sharp, and the start of the text, so that
        // the same text looks the same wherever it stands. The letters keep their spacing between them.
        var (startX, baselineY) = (MathF.Round(baseline.X), MathF.Round(baseline.Y));
        var pen = 0L;
        var count = 0;
        foreach (var glyph in shaped)
        {
            var atlasGlyph = GetGlyph(face, glyph.Codepoint);
            if (atlasGlyph.Width > 0)
            {
                var x = startX + ((pen + glyph.XOffset) * size / face.UnitsPerEm) + (atlasGlyph.Left * scale);
                var y = baselineY - (glyph.YOffset * size / face.UnitsPerEm) + (atlasGlyph.Top * scale);
                glyphs[count++] = new GlyphInstance(
                    new Vector2(x, y),
                    new Vector2(atlasGlyph.Width * scale, atlasGlyph.Height * scale),
                    new Vector2(atlasGlyph.AtlasX / (float)AtlasSize, atlasGlyph.AtlasY / (float)AtlasSize),
                    new Vector2((atlasGlyph.AtlasX + atlasGlyph.Width) / (float)AtlasSize, (atlasGlyph.AtlasY + atlasGlyph.Height) / (float)AtlasSize),
                    color,
                    distanceScale);
            }

            pen += glyph.XAdvance;
        }

        return count;
    }

    /// <inheritdoc />
    public IGpuTexture GetAtlas(IRenderDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        using var scope = _gate.EnterScope();
        if (!_copies.TryGetValue(device, out var copy))
        {
            copy = new AtlasCopy(device.CreateTexture(new TextureDesc(new PixelSize(AtlasSize, AtlasSize), TextureFormat.Rgba8Unorm)));
            _copies.Add(device, copy);
        }

        if (copy.Version != _atlasVersion)
        {
            copy.Texture.Write(_atlas);
            copy.Version = _atlasVersion;
        }

        return copy.Texture;
    }

    /// <summary>
    /// Releases the atlas of a device that goes away. The default engine lives on, its copies must not.
    /// </summary>
    /// <param name="device">The device.</param>
    public void ReleaseAtlas(IRenderDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        using var scope = _gate.EnterScope();
        if (_copies.Remove(device, out var copy))
        {
            copy.Texture.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var copy in _copies.Values)
        {
            copy.Texture.Dispose();
        }

        _copies.Clear();
        foreach (var face in _owned)
        {
            face.Dispose();
        }

        _owned.Clear();
        _faces.Clear();
    }

    // HarfBuzz reads tables on demand. Blob.FromStream leaves it a pointer into a managed array, which the
    // garbage collector frees: glyphs then come from foreign memory. Native memory lives as long as the blob.
    private static Blob CreateBlob(byte[] fontData)
    {
        var memory = Marshal.AllocHGlobal(fontData.Length);
        Marshal.Copy(fontData, 0, memory, fontData.Length);
        return new Blob(memory, fontData.Length, MemoryMode.ReadOnly, () => Marshal.FreeHGlobal(memory));
    }

    private static byte[]? Embedded(string file)
    {
        using var stream = typeof(SkiaTextEngine).Assembly.GetManifestResourceStream(file);
        if (stream is null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    // An installed family in a cut. For a cut of another family the font system may hand back a fallback, which does
    // not count; asked for by name alone, as in FromSystemFont, its choice stands.
    private static (byte[] Data, bool IsItalic)? SystemFont(string family, SKFontStyle style, bool anyFamily = false)
    {
        using var typeface = SKFontManager.Default.MatchFamily(family, style);
        if (typeface is null || (!anyFamily && !typeface.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        using var stream = typeface.OpenStream();
        var bytes = new byte[stream.Length];
        stream.Read(bytes, bytes.Length);
        return (bytes, typeface.FontSlant != SKFontStyleSlant.Upright);
    }

    private static Vector2[] FlattenQuad(Vector2 a, Vector2 b, Vector2 c)
    {
        var points = new Vector2[CurveSegments + 1];
        for (var i = 0; i <= CurveSegments; i++)
        {
            var t = i / (float)CurveSegments;
            var u = 1 - t;
            points[i] = (u * u * a) + (2 * u * t * b) + (t * t * c);
        }

        return points;
    }

    private static Vector2[] FlattenCubic(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var points = new Vector2[CurveSegments + 1];
        for (var i = 0; i <= CurveSegments; i++)
        {
            var t = i / (float)CurveSegments;
            var u = 1 - t;
            points[i] = (u * u * u * a) + (3 * u * u * t * b) + (3 * u * t * t * c) + (t * t * t * d);
        }

        return points;
    }

    private static List<List<Vector2[]>> ExtractContours(SKPath path, Vector2 offset)
    {
        var contours = new List<List<Vector2[]>>();
        List<Vector2[]>? contour = null;
        var points = new SKPoint[4];
        Vector2 Convert(SKPoint point) => new Vector2(point.X, point.Y) + offset;

        using var iterator = path.CreateRawIterator();
        SKPathVerb verb;
        var start = Vector2.Zero;
        var current = Vector2.Zero;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    contour = [];
                    contours.Add(contour);
                    start = current = Convert(points[0]);
                    break;
                case SKPathVerb.Line:
                    current = AddCurve(contour!, [current, Convert(points[1])]);
                    break;
                case SKPathVerb.Quad:
                case SKPathVerb.Conic:
                    current = AddCurve(contour!, FlattenQuad(current, Convert(points[1]), Convert(points[2])));
                    break;
                case SKPathVerb.Cubic:
                    current = AddCurve(contour!, FlattenCubic(current, Convert(points[1]), Convert(points[2]), Convert(points[3])));
                    break;
                case SKPathVerb.Close:
                    if (Vector2.DistanceSquared(current, start) > 1e-6f)
                    {
                        current = AddCurve(contour!, [current, start]);
                    }

                    break;
            }
        }

        contours.RemoveAll(item => item.Count == 0);
        return contours;
    }

    private static Vector2 AddCurve(List<Vector2[]> contour, Vector2[] curve)
    {
        if (Vector2.DistanceSquared(curve[0], curve[^1]) > 1e-8f)
        {
            contour.Add(curve);
        }

        return curve[^1];
    }

    // Finds or makes the face of a font: an embedded cut, an installed family, or the font of the engine. A cut that
    // is missing is the nearest one there is; italic is then made by leaning the outlines.
    private FontFace FaceOf(TextFont font)
    {
        if (font.IsDefault)
        {
            return _regular;
        }

        if (_faces.TryGetValue(font, out var known))
        {
            return known;
        }

        var face = MakeFace(font);
        _faces.Add(font, face);
        return face;
    }

    private FontFace MakeFace(TextFont font)
    {
        var family = font.Family?.Trim();
        var named = family ?? (_isEmbedded ? "Roboto" : null);
        var (file, leans) = named?.ToUpperInvariant() switch
        {
            "ROBOTO" => (font.Bold || font.Italic ? "Roboto-" + (font.Bold ? "Bold" : string.Empty) + (font.Italic ? "Italic" : string.Empty) + ".ttf" : EmbeddedFont, false),
            "ROBOTO SLAB" => ("RobotoSlab-" + (font.Bold ? "Bold" : "Regular") + ".ttf", font.Italic),
            _ => ((string?)null, false),
        };
        if (file is not null && Embedded(file) is { } embedded)
        {
            return Own(new FontFace(embedded, leans ? SyntheticSlant : 0));
        }

        var style = new SKFontStyle(font.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, font.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        if (family is { Length: > 0 } && SystemFont(family, style) is { } system)
        {
            return Own(new FontFace(system.Data, font.Italic && !system.IsItalic ? SyntheticSlant : 0));
        }

        // An unknown family takes the font of the engine in the same cut; the engine's own font can only lean.
        if (family is not null)
        {
            return FaceOf(font with { Family = null });
        }

        return font.Italic ? Own(new FontFace(_fontData, SyntheticSlant)) : _regular;
    }

    private FontFace Own(FontFace face)
    {
        _owned.Add(face);
        return face;
    }

    private AtlasGlyph GetGlyph(FontFace face, uint glyphId)
    {
        if (!face.Glyphs.TryGetValue(glyphId, out var glyph))
        {
            glyph = CreateGlyph(face, (ushort)glyphId);
            face.Glyphs.Add(glyphId, glyph);
        }

        return glyph;
    }

    private AtlasGlyph CreateGlyph(FontFace face, ushort glyphId)
    {
        var contours = face.GetContours(glyphId);
        if (contours.Count == 0)
        {
            return default;
        }

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var contour in contours)
        {
            foreach (var curve in contour)
            {
                foreach (var point in curve)
                {
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
            }
        }

        var margin = FieldRange + 1;
        var left = (int)MathF.Floor(min.X) - margin;
        var top = (int)MathF.Floor(min.Y) - margin;
        var width = (int)MathF.Ceiling(max.X) + margin - left;
        var height = (int)MathF.Ceiling(max.Y) + margin - top;

        var offset = new Vector2(-left, -top);
        foreach (var contour in contours)
        {
            foreach (var curve in contour)
            {
                for (var i = 0; i < curve.Length; i++)
                {
                    curve[i] += offset;
                }
            }
        }

        var (x, y) = Reserve(width, height);
        MsdfGenerator.Generate(contours, width, height, FieldRange, _atlas.AsSpan((y * AtlasSize * 4) + (x * 4)), AtlasSize * 4);
        _atlasVersion++;

        return new AtlasGlyph(x, y, width, height, left, top);
    }

    private (int X, int Y) Reserve(int width, int height)
    {
        if (_shelfX + width + 1 > AtlasSize)
        {
            _shelfX = 1;
            _shelfY += _shelfHeight + 1;
            _shelfHeight = 0;
        }

        if (_shelfY + height + 1 > AtlasSize)
        {
            throw new InvalidOperationException("The glyph atlas is full.");
        }

        var position = (_shelfX, _shelfY);
        _shelfX += width + 1;
        _shelfHeight = Math.Max(_shelfHeight, height);
        return position;
    }

    private readonly record struct AtlasGlyph(int AtlasX, int AtlasY, int Width, int Height, int Left, int Top);

    private readonly record struct ShapedGlyph(uint Codepoint, int XOffset, int YOffset, int XAdvance);

    private sealed class AtlasCopy(IGpuTexture texture)
    {
        public IGpuTexture Texture { get; } = texture;

        public long Version { get; set; } = -1;
    }

    // One font file with its shaping, its outlines and the glyphs it has in the atlas.
    private sealed class FontFace : IDisposable
    {
        private readonly SKTypeface _typeface;
        private readonly SKFont _outlineFont;
        private readonly TrueTypeOutlines? _outlines;
        private readonly Blob _blob;
        private readonly Face _face;
        private readonly Dictionary<string, ShapedGlyph[]> _shaped = new(StringComparer.Ordinal);
        private readonly float _slant;

        public FontFace(byte[] fontData, float slant)
        {
            using var data = SKData.CreateCopy(fontData);
            _typeface = SKTypeface.FromData(data) ?? throw new ArgumentException("The data is not a font.", nameof(fontData));
            _outlineFont = new SKFont(_typeface, FieldEmSize) { Hinting = SKFontHinting.None, Subpixel = true };
            _outlines = TrueTypeOutlines.TryCreate(fontData, CurveSegments);
            _slant = slant;

            _blob = CreateBlob(fontData);
            _face = new Face(_blob, 0);
            Font = new Font(_face);
            UnitsPerEm = _face.UnitsPerEm;
            Font.SetScale(UnitsPerEm, UnitsPerEm);
            Font.SetFunctionsOpenType();

            // HarfBuzz reads the font tables, Skia would ask the font system of the operating system.
            if (Font.TryGetHorizontalFontExtents(out var extents))
            {
                LineHeightPerEm = (extents.Ascender - extents.Descender + extents.LineGap) / (float)UnitsPerEm;
            }
            else
            {
                var metrics = _outlineFont.Metrics;
                LineHeightPerEm = (metrics.Descent - metrics.Ascent + metrics.Leading) / FieldEmSize;
            }
        }

        public Font Font { get; }

        public int UnitsPerEm { get; }

        public float LineHeightPerEm { get; }

        public Dictionary<uint, AtlasGlyph> Glyphs { get; } = [];

        // Shaping allocates, and overlays draw the same texts in every frame while the view changes.
        public ShapedGlyph[] GetShaped(ReadOnlySpan<char> text)
        {
            var lookup = _shaped.GetAlternateLookup<ReadOnlySpan<char>>();
            if (lookup.TryGetValue(text, out var known))
            {
                return known;
            }

            if (_shaped.Count >= MaximumShapedTexts)
            {
                _shaped.Clear();
            }

            using var buffer = new Buffer();
            buffer.AddUtf16(text);
            buffer.GuessSegmentProperties();
            Font.Shape(buffer);
            var infos = buffer.GlyphInfos;
            var positions = buffer.GlyphPositions;
            var shaped = new ShapedGlyph[infos.Length];
            for (var i = 0; i < shaped.Length; i++)
            {
                shaped[i] = new ShapedGlyph(infos[i].Codepoint, positions[i].XOffset, positions[i].YOffset, positions[i].XAdvance);
            }

            lookup[text] = shaped;
            return shaped;
        }

        // Outlines of the font file are the same everywhere. Skia is the way for fonts without a glyf table. A face that
        // leans shifts each point to the right by its height above the baseline, where y grows downwards.
        public List<List<Vector2[]>> GetContours(ushort glyphId)
        {
            List<List<Vector2[]>> contours;
            if (_outlines is { } outlines)
            {
                contours = outlines.GetContours(glyphId, FieldEmSize);
            }
            else
            {
                using var path = _outlineFont.GetGlyphPath(glyphId);
                contours = path is null || path.IsEmpty ? [] : ExtractContours(path, Vector2.Zero);
            }

            if (_slant != 0)
            {
                foreach (var contour in contours)
                {
                    foreach (var curve in contour)
                    {
                        for (var i = 0; i < curve.Length; i++)
                        {
                            curve[i] = new Vector2(curve[i].X - (curve[i].Y * _slant), curve[i].Y);
                        }
                    }
                }
            }

            return contours;
        }

        public void Dispose()
        {
            Font.Dispose();
            _face.Dispose();
            _blob.Dispose();
            _outlineFont.Dispose();
            _typeface.Dispose();
        }
    }
}
