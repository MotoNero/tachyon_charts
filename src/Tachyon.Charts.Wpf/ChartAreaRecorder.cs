using System.Windows;
using System.Windows.Media;
using Tachyon.Charts.Export;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// Records all charts in an area of a window together, as the window shows them: a spectrum with its history, a 3D
/// chart with the 2D chart below it, or a page of several charts. The places of the charts are taken when the recording
/// starts.
/// </summary>
public static class ChartAreaRecorder
{
    /// <summary>Starts to record the area while the user works with its charts, like a video recorder.</summary>
    /// <param name="area">The area, for example the panel that holds the charts.</param>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The frame rate and encoding, or <see langword="null"/>; the size is the one of the area.</param>
    /// <returns>The recording; stop it with <see cref="IChartRecording.StopAsync"/>.</returns>
    /// <exception cref="InvalidOperationException">The area shows no chart, or the encoder failed to start.</exception>
    /// <exception cref="System.IO.FileNotFoundException">FFmpeg is needed and was not found.</exception>
    public static IChartRecording Start(FrameworkElement area, string path, VideoOptions? options = null)
    {
        var (charts, size) = Find(area);

        // The charts are looked up again while the recording runs: one that gets its size only now, or a page that changes.
        var search = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var recording = AreaRecording.Start(charts, size, path, options, search.Stop);
        search.Tick += (_, _) => recording.Update(Find(area, needsChart: false).Charts);
        search.Start();
        return recording;
    }

    /// <summary>Records a planned video of the area, frame by frame at fixed times.</summary>
    /// <param name="area">The area.</param>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The length and encoding, or <see langword="null"/>; the size is the one of the area.</param>
    /// <param name="step">Called before each frame with its number and time, to move a camera or to add data.</param>
    /// <returns>When the file is written.</returns>
    /// <exception cref="InvalidOperationException">The area shows no chart, or the encoder failed.</exception>
    /// <exception cref="System.IO.FileNotFoundException">FFmpeg is needed and was not found.</exception>
    public static Task ExportVideoAsync(FrameworkElement area, string path, VideoOptions? options = null, Action<int, TimeSpan>? step = null)
    {
        var (charts, size) = Find(area);
        return AreaRecording.ExportVideoAsync(charts, size, path, options, step);
    }

    // The visible charts, with their places in pixels of the area; one that does not run yet joins once it runs.
    private static (List<(Func<ChartController?> Controller, PixelRect Bounds)> Charts, PixelSize Size) Find(FrameworkElement area, bool needsChart = true)
    {
        ArgumentNullException.ThrowIfNull(area);
        var scale = VisualTreeHelper.GetDpi(area).DpiScaleX;
        var charts = new List<(Func<ChartController?> Controller, PixelRect Bounds)>();
        Collect(area, area, scale, charts);
        if (charts.Count == 0 && needsChart)
        {
            throw new InvalidOperationException("The area shows no chart to record.");
        }

        var size = new PixelSize(Math.Max((int)Math.Round(area.ActualWidth * scale), 2), Math.Max((int)Math.Round(area.ActualHeight * scale), 2));
        return (charts, size);
    }

    private static void Collect(DependencyObject parent, FrameworkElement area, double scale, List<(Func<ChartController?> Controller, PixelRect Bounds)> charts)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is UIElement { Visibility: not Visibility.Visible })
            {
                continue;
            }

            if (child is Chart chart)
            {
                var origin = chart.TransformToAncestor(area).Transform(new Point(0, 0));
                charts.Add((() => chart.Controller, new PixelRect((float)Math.Round(origin.X * scale), (float)Math.Round(origin.Y * scale), (float)Math.Round(chart.ActualWidth * scale), (float)Math.Round(chart.ActualHeight * scale))));
                continue;
            }

            Collect(child, area, scale, charts);
        }
    }
}
