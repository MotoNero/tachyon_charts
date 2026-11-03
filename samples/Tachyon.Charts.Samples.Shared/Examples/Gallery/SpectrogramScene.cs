using System.Globalization;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared.Scenes;
using static Tachyon.Charts.Samples.Shared.Examples.Common.SpectrogramSignal;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// A ring of 256 rows after 400 appended rows: a tone that sweeps up and down, and a pause of missing
/// rows. The ring has wrapped, and the rows must still run without a seam.
/// </summary>
public sealed class SpectrogramScene : IScene
{
    private const int Columns = 256;
    private const int Rows = 256;
    private const int Appended = 400;

    private static readonly NumericLabelFormat _labels = new() { Culture = CultureInfo.InvariantCulture };

    /// <inheritdoc />
    public string Name => "Spectrogram";

    /// <inheritdoc />
    public PixelSize Size { get; } = new(800, 500);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var x = new NumericAxis("x", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, Columns), LabelFormat = _labels, Title = "Frequency" };
        var y = new NumericAxis("y", AxisPosition.Left) { VisibleRange = new DoubleRange(-Rows, 0), LabelFormat = _labels };
        var color = new ColorAxis("color") { LabelFormat = _labels, Colormap = Colormaps.Plasma, VisibleRange = new DoubleRange(-0.1, 1.1), MissingColor = new Rgba8(40, 40, 40, 255) };
        var chart = new ChartSurface([x], [new LayoutPane([y, color])], SkiaTextEngine.Default) { Title = Name };
        var data = new SpectrogramData(Columns, Rows);
        using var series = new SpectrogramSeries(data, x, y, color);
        chart.AddSeries(series);

        // The first half arrives before the first frame, the rest row by row.
        var row = new float[Columns];
        for (var i = 0; i < Appended; i++)
        {
            AppendSignal(data, i, row);
            if (i >= Appended / 2)
            {
                chart.Render(device, target);
            }
        }

        device.WaitForIdle();
    }
}
