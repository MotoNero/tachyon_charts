using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>
/// The chart type „Recording“ of the showroom: ten million values per second recorded into a ring of files on the disk,
/// the view rolling over the whole ring, over a billion values once it is full. The recording pauses while the page is
/// not shown.
/// </summary>
public static class RecordingPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Recording";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Double click: all, rolling | Records up to 4.6 GB on the disk";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c) => c
        .Title(SampleText.T("10 million values per second, recorded on the disk"))
        .XAxis(a => a.Numeric("x").Title(SampleText.T("Time in s")).AutoRange())
        .YAxis(a => a.Numeric("y").Title(SampleText.T("Value")).AutoRange())
        .AddSeries((x, y) => new ColumnLineSeries<float>(RecordingDemo.Shared, RecordingDemo.Shared, x, y) { Name = SampleText.T("Measurement"), Color = new Rgba8(80, 170, 240, 255), Thickness = 1 })
        .Modifiers(m => m.ZoomPan().RubberBand());
}
