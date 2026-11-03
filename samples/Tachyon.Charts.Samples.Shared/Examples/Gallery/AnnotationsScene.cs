using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// All kinds of annotations over a line.
/// </summary>
public sealed class AnnotationsScene : LineScene
{
    /// <inheritdoc />
    public override string Name => "Annotations";

    /// <summary>
    /// Creates a small checkerboard with a transparent half.
    /// </summary>
    /// <returns>The pixels of 8 by 8 squares.</returns>
    private static Rgba8[] CreateImage()
    {
        var pixels = new Rgba8[64];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var alpha = (byte)(x < 4 ? 255 : 120);
                pixels[(y * 8) + x] = (x + y) % 2 == 0 ? new Rgba8(255, 255, 255, alpha) : new Rgba8(200, 60, 160, alpha);
            }
        }

        return pixels;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);

        var band = new BandAnnotation(x) { Color = new Rgba8(110, 210, 130, 45) };
        band.SetRange(7.5, 9);
        chart.AddOverlay(band);

        var box = new BoxAnnotation(x, y);
        box.SetCorners(2, 1.2, 4, 0.4);
        chart.AddOverlay(box);

        chart.AddOverlay(new HorizontalLineAnnotation(y) { Y = -1.1, Label = "Grenzwert", Color = new Rgba8(240, 90, 90, 255) });
        chart.AddOverlay(new VerticalLineAnnotation(x) { X = 6, Label = "Start", Color = Green });

        var text = new TextAnnotation(x, y) { Text = "Maximum", Color = Orange };
        text.MoveTo(0.75, 1.1);
        chart.AddOverlay(text);

        var arrow = new ArrowAnnotation(x, y) { Color = Orange };
        arrow.SetEnds(0.9, 1.05, 1.21, 0.935);
        chart.AddOverlay(arrow);

        var marker = new MarkerAnnotation(x, y) { Color = new Rgba8(240, 90, 90, 255), Size = 14 };
        marker.MoveTo(3.62, -0.9);
        chart.AddOverlay(marker);

        var image = new ImageAnnotation(CreateImage(), new PixelSize(8, 8), null, null) { DisplaySize = (64, 64) };
        image.MoveTo(800, 60);
        chart.AddOverlay(image);

        return [line];
    }
}
