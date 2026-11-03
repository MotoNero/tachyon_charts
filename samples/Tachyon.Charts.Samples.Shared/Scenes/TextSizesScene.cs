using System.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>
/// Text from 8 to 32 pixels at the physical sizes of 100, 150 and 200 percent scaling, with the
/// special characters of chart labels.
/// </summary>
public sealed class TextSizesScene : IScene
{
    private const string Sample = "Größe µV 37 °C m² −0,25 Ää";
    private const float ColumnGap = 30;

    private static readonly float[] _sizes = [8, 10, 12, 14, 16, 20, 24, 32];
    private static readonly float[] _scales = [1, 1.5f, 2];
    private static readonly Rgba8 _light = new(235, 238, 245, 255);

    /// <inheritdoc />
    public string Name => "TextSizes";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(2000, 480);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var engine = SkiaTextEngine.Default;
        var glyphs = new GlyphInstance[_sizes.Length * _scales.Length * Sample.Length];
        var count = 0;
        var x = 10f;
        foreach (var scale in _scales)
        {
            var y = 10f;
            foreach (var size in _sizes)
            {
                var pixels = size * scale;
                y += engine.GetLineHeight(pixels);
                count += engine.Layout(Sample, new Vector2(x, y), pixels, _light, glyphs.AsSpan(count));
            }

            x += engine.MeasureWidth(Sample, _sizes[^1] * scale) + ColumnGap;
        }

        var atlas = engine.GetAtlas(device);
        using var frame = device.BeginFrame(target);
        frame.Clear(new Rgba8(24, 28, 36, 255));
        frame.DrawGlyphs(atlas, glyphs.AsSpan(0, count));
        frame.Submit();
    }
}
