using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Export;

/// <summary>A chart and the place it takes in a video, in pixels of the video.</summary>
/// <param name="Chart">The chart.</param>
/// <param name="Bounds">Where it lies in the frame.</param>
public readonly record struct RecordedChart(ChartSurface Chart, PixelRect Bounds);

/// <summary>
/// Puts several charts into one frame at their places, as a window shows them side by side or one above the other.
/// Each chart renders at its own size, so its layout is the one of the screen. Runs on the render thread.
/// </summary>
internal sealed class FrameComposer : IDisposable
{
    private static readonly Rgba8 _background = new(11, 14, 19, 255);

    private readonly Dictionary<PixelSize, (IOffscreenRenderTarget Target, Rgba8[] Pixels)> _targets = [];

    /// <summary>Renders the charts and puts them into the frame.</summary>
    /// <param name="charts">The charts and their places.</param>
    /// <param name="device">The device the charts render with.</param>
    /// <param name="frame">Receives the picture, top row first.</param>
    /// <param name="size">The size of the frame.</param>
    /// <param name="passes">How often each chart renders; a second pass settles a layout that depends on the size.</param>
    public void Compose(IReadOnlyList<RecordedChart> charts, IRenderDevice device, Rgba8[] frame, PixelSize size, int passes)
    {
        if (charts.Count != 1 || charts[0].Bounds != new PixelRect(0, 0, size.Width, size.Height))
        {
            frame.AsSpan(0, (int)size.PixelCount).Fill(_background);
        }

        foreach (var (chart, bounds) in charts)
        {
            var place = new PixelSize((int)MathF.Round(bounds.Width), (int)MathF.Round(bounds.Height));
            if (place.Width <= 0 || place.Height <= 0)
            {
                continue;
            }

            if (!_targets.TryGetValue(place, out var entry))
            {
                entry = (device.CreateOffscreenTarget(place, 1), new Rgba8[place.PixelCount]);
                _targets[place] = entry;
            }

            var showsDiagnostics = chart.ShowDiagnostics;
            chart.ShowDiagnostics = false;
            for (var pass = 0; pass < passes; pass++)
            {
                chart.Render(device, entry.Target);
            }

            chart.ShowDiagnostics = showsDiagnostics;
            device.WaitForIdle();
            entry.Target.ReadPixels(entry.Pixels);
            Copy(entry.Pixels, place, frame, size, (int)MathF.Round(bounds.X), (int)MathF.Round(bounds.Y));
        }
    }

    public void Dispose()
    {
        foreach (var (target, _) in _targets.Values)
        {
            target.Dispose();
        }

        _targets.Clear();
    }

    // Copies a picture into the frame; what lies outside of it is cut off.
    private static void Copy(Rgba8[] source, PixelSize from, Rgba8[] frame, PixelSize size, int left, int top)
    {
        var first = Math.Max(0, -left);
        var last = Math.Min(from.Width, size.Width - left);
        if (last <= first)
        {
            return;
        }

        for (var y = Math.Max(0, -top); y < from.Height && top + y < size.Height; y++)
        {
            source.AsSpan((y * from.Width) + first, last - first).CopyTo(frame.AsSpan(((top + y) * size.Width) + left + first));
        }
    }
}
