using System.Numerics;
using System.Windows;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Wpf;

namespace Tachyon.Charts.Samples.Wpf;

/// <summary>
/// A guided tour through the showroom for a demo video: page after page with a caption, and on each page what a user
/// would do, given to the chart directly. The pointer of the system does not move. Started with <c>--demo</c>; the
/// window closes at the end.
/// </summary>
internal sealed partial class SampleWindow
{
    private static readonly TimeSpan _tick = TimeSpan.FromMilliseconds(33);

    /// <summary>Gets or sets a value indicating whether the tour runs after the start.</summary>
    public static bool RunsDemo { get; set; }

    private async Task RunDemo()
    {
        await Task.Delay(4000);

        await Show("Live-Chart (interaktiv)", "Live data from any thread", "50,000 new points per second; crosshair, zoom and the overview below", async chart =>
        {
            await Sweep(chart, 3);
            await Wheel(chart, 4, 1);
            await Sweep(chart, 2);
            await Rubber(chart);
            await Task.Delay(1500);
        });
        await Show("Gruppe (3 Charts)", "Linked charts", "Three charts share their range and cursor", async chart => await Sweep(chart, 3));
        await Show("Oszilloskop (interaktiv)", "Oscilloscope", "Four channels with trigger and persistence", _ => Task.Delay(5000));
        await Show("Spektrum (interaktiv)", "Spectrum analyzer", "Spectrum, mask and markers above, the spectrogram below", _ => Task.Delay(6000));
        await Show("Network (interaktiv)", "Network analyzer", "S-parameters with limit lines and markers", async chart => await Sweep(chart, 3));
        await Show("Smith (interaktiv)", "Smith chart", "Impedance with a marker", _ => Task.Delay(3000));

        await Show("Large file", "A billion values from a 4 GB file", "Read straight from the disk; zooming in down to single values, no outlier lost", async chart =>
        {
            await Task.Delay(1500);
            await Wheel(chart, 12, 1);
            await Drag(chart, 0.6f, 0.5f, 0.3f, 0.5f);
            await Task.Delay(1000);
        });
        await Show("Recording", "Recording ten million values per second", "Into a ring of files on the disk, the view rolling with it", _ => Task.Delay(6000));
        await Show("Digital channels", "16 digital channels of a billion points each", "Edges of a single point stay visible at any zoom", async chart =>
        {
            await Wheel(chart, 10, 1);
            await Task.Delay(1000);
        });
        await Show("Scatter from a file", "200 million points from a file", "As a density; zooming down to the points themselves", async chart =>
        {
            await Wheel(chart, 8, 1);
            await Task.Delay(1000);
        });
        await Show("Heatmap from a file", "268 million cells from a file", "Read through stages built once beside the file", async chart =>
        {
            await Wheel(chart, 8, 1);
            await Task.Delay(1000);
        });
        await Show("World map", "Maps without the internet", "Countries in three levels of detail, layers of data on top", async chart =>
        {
            await Wheel(chart, 6, 1);
            await Task.Delay(1000);
        });

        await Show("Candles", "Finance", "Candles and volume from a thread-safe store", async chart => await Sweep(chart, 3));
        await Show("Drawing tools", "Drawing tools", "Fibonacci, channels, pitchfork and patterns, fixed to values", _ => Task.Delay(3500));
        await Show("Annotations", "Annotations", "Styled labels, symbols, dashed lines; editable with the mouse", _ => Task.Delay(3500));

        await Show3D("Surface 4096 × 4096", "3D: a surface of 16 million values", "Turning at the frame rate of the screen");
        await Show3D("Cloud from a file", "3D: 200 million points from a file", "Sorted into pages once; the pages in view load onto the graphics card as needed");
        await Show3D("Through a volume 512³", "3D: looking through a volume", "With a transfer function edited in the chart");
        await Show3D("Model with measuring points", "3D: a model from an STL file", "Colored by measured values, cut open");
        await Show3D("Waterfall, live", "3D: live waterfall", "Linked to the spectrum analyzer beside it");
        await Show3D("Medical MPR", "Medical add-on", "Linked axial, coronal and sagittal planes and the volume in 3D");

        await Show("Spektrum (interaktiv)", "Themes and languages", "Seven built-in themes, your own from JSON or XAML; English and German", async _ =>
        {
            foreach (var theme in new[] { 4, 5, 6, 1 })
            {
                _theme.SelectedIndex = theme;
                await Task.Delay(300);
                (_pageTitle.Text, _hint.Text) = ("Themes and languages", "Seven built-in themes, your own from JSON or XAML; English and German");
                await Task.Delay(1500);
            }

            _language.SelectedIndex = 1;
            await Task.Delay(300);
            (_pageTitle.Text, _hint.Text) = ("Themes and languages", "Seven built-in themes, your own from JSON or XAML; English and German");
            await Task.Delay(1700);
            _language.SelectedIndex = 0;
            await Task.Delay(800);
        });
        await Show("Large file", "Every page opens in the configurator", "Take the chart and its C# code from there", _ => Task.Delay(3000));
        Close();
    }

    private async Task Show(string page, string caption, string subtitle, Func<Chart, Task> act)
    {
        _list.SelectedItem = _pageList.First(entry => entry.Id == page && !entry.IsHeader);
        _list.ScrollIntoView(_list.SelectedItem);
        (_pageTitle.Text, _hint.Text) = (caption, subtitle);
        await Task.Delay(1500);
        (_pageTitle.Text, _hint.Text) = (caption, subtitle);
        await act(ExportedChart);
    }

    private async Task Show3D(string page, string caption, string subtitle)
    {
        _list.SelectedItem = _pageList.First(entry => entry.Id == page && !entry.IsHeader);
        _list.ScrollIntoView(_list.SelectedItem);
        (_pageTitle.Text, _hint.Text) = (caption, subtitle);

        // A scene reads its data first; the turning starts once it shows.
        await Task.Delay(3500);
        (_pageTitle.Text, _hint.Text) = (caption, subtitle);
        if (!_hosts3D.TryGetValue(page, out var host))
        {
            await Task.Delay(5000);
            return;
        }

        for (var step = 0; step < 150; step++)
        {
            host.Scene.Camera.Orbit(0.012, 0);
            await Task.Delay(_tick);
        }
    }

    // The crosshair runs from left to right through the middle of the chart.
    private static async Task Sweep(Chart chart, double seconds)
    {
        var steps = (int)(seconds / _tick.TotalSeconds);
        var (width, height) = ((float)chart.ActualWidth, (float)chart.ActualHeight);
        for (var i = 0; i <= steps; i++)
        {
            var x = width * (0.12f + (0.76f * i / steps));
            var y = height * (0.5f + (0.12f * MathF.Sin(i * 0.08f)));
            Pointer(chart, PointerEventKind.Moved, new Vector2(x, y), PointerButtons.None, PointerButton.None);
            await Task.Delay(_tick);
        }
    }

    private static async Task Wheel(Chart chart, int notches, float direction)
    {
        var center = new Vector2((float)chart.ActualWidth * 0.55f, (float)chart.ActualHeight * 0.5f);
        Pointer(chart, PointerEventKind.Moved, center, PointerButtons.None, PointerButton.None);
        for (var i = 0; i < notches; i++)
        {
            chart.SendInput(new WheelInputEvent(center, new Vector2(0, direction), InputModifiers.None, Now));
            await Task.Delay(220);
        }
    }

    // Ctrl and drag: a rectangle to zoom into.
    private static Task Rubber(Chart chart) => Drag(chart, 0.35f, 0.3f, 0.6f, 0.7f, InputModifiers.Control);

    private static async Task Drag(Chart chart, float fromX, float fromY, float toX, float toY, InputModifiers modifiers = InputModifiers.None)
    {
        var size = new Vector2((float)chart.ActualWidth, (float)chart.ActualHeight);
        var (from, to) = (new Vector2(fromX, fromY) * size, new Vector2(toX, toY) * size);
        Pointer(chart, PointerEventKind.Moved, from, PointerButtons.None, PointerButton.None, modifiers);
        Pointer(chart, PointerEventKind.Pressed, from, PointerButtons.Primary, PointerButton.Primary, modifiers);
        for (var i = 1; i <= 30; i++)
        {
            Pointer(chart, PointerEventKind.Moved, Vector2.Lerp(from, to, i / 30f), PointerButtons.Primary, PointerButton.None, modifiers);
            await Task.Delay(_tick);
        }

        Pointer(chart, PointerEventKind.Released, to, PointerButtons.None, PointerButton.Primary, modifiers);
        await Task.Delay(400);
    }

    private static void Pointer(Chart chart, PointerEventKind kind, Vector2 position, PointerButtons buttons, PointerButton changed, InputModifiers modifiers = InputModifiers.None) =>
        chart.SendInput(new PointerInputEvent(kind, 1, position, buttons, changed, modifiers, Now, PointerDeviceKind.Mouse, kind == PointerEventKind.Pressed ? 1 : 0));

    private static TimeSpan Now => TimeSpan.FromTicks(Environment.TickCount64 * TimeSpan.TicksPerMillisecond);
}
