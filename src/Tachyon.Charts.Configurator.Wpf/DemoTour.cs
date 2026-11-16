using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Tachyon.Charts.Configurator.Wpf;

/// <summary>
/// A guided tour through the configurator for a demo video: types, options, templates, a page of the showroom and the
/// generated code, each with a caption above the window. Started with <c>--demo</c>; the window closes at the end.
/// </summary>
internal sealed partial class ConfiguratorWindow
{
    private readonly TextBlock _caption = new() { FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(16, 10, 16, 0) };
    private readonly TextBlock _subtitle = new() { FontSize = 14, Margin = new Thickness(16, 2, 16, 8) };

    /// <summary>Gets or sets a value indicating whether the tour runs after the start.</summary>
    public bool RunsDemo { get; init; }

    // The captions above the three columns.
    private void AddCaptions()
    {
        var root = (UIElement)Content;
        var panel = new DockPanel();
        var captions = new StackPanel { Background = (Brush)FindResource("Shell") };
        _subtitle.Foreground = (Brush)FindResource("Muted");
        captions.Children.Add(_caption);
        captions.Children.Add(_subtitle);
        DockPanel.SetDock(captions, Dock.Top);
        Content = null;
        panel.Children.Add(captions);
        panel.Children.Add(root);
        Content = panel;
    }

    private async Task RunDemo()
    {
        AddCaptions();
        await Caption("Configurator: put a chart together, take its code", "The settings on the left, the chart in the middle, its C# on the right", 4000);

        await Caption("Every chart type of the library", "Each with its options and its own sample data", 0);
        foreach (var type in new[] { "heatmap", "candles", "pie", "polar", "spectrum", "surface3d", "line" })
        {
            ChooseType(type);
            await Task.Delay(2600);
        }

        await Caption("Every change shows at once, in the chart and in the code", "Thickness, style and color of the line", 0);
        foreach (var (name, value) in new (string, object)[] { ("thickness", 4.0), ("style", "step"), ("color", "#F0825A"), ("style", "spline"), ("thickness", 2.0) })
        {
            if (_session.SeriesFields.FirstOrDefault(field => field.Info.Name == name) is { } field)
            {
                field.Set(value);
                Fill();
            }

            await Task.Delay(2200);
        }

        await Caption("The generated code", "Fluent C#, a view model with the data, the window in XAML, a complete project", 0);
        for (var tab = 0; tab < _code.Items.Count; tab++)
        {
            _code.SelectedIndex = tab;
            await Task.Delay(2500);
        }

        _code.SelectedIndex = 0;
        await Caption("Templates to start from", "A spectrum with its live waterfall in 3D", 0);
        _session.UseTemplate("spectrumWaterfall");
        await Task.Delay(6000);

        await Caption("Every page of the showroom opens here completely", "A billion values from a 4 GB file", 0);
        _session.OpenPage("Large file");
        await Task.Delay(5000);
        await Caption("Every page of the showroom opens here completely", "16 digital channels of a billion points each", 0);
        _session.OpenPage("Digital channels");
        await Task.Delay(5000);
        await Caption("Every page of the showroom opens here completely", "World map without the internet", 0);
        _session.OpenPage("World map");
        await Task.Delay(5000);

        await Caption("Tachyon Charts", "GPU-accelerated 2D and 3D charts for .NET, WPF and Avalonia", 4000);
        Close();
    }

    private async Task Caption(string caption, string subtitle, int wait)
    {
        (_caption.Text, _subtitle.Text) = (caption, subtitle);
        await Task.Delay(wait);
    }

    private void ChooseType(string id)
    {
        var index = _session.Catalog.Types.ToList().FindIndex(type => type.Id == id);
        if (index >= 0 && _types is not null)
        {
            _types.SelectedIndex = index;
        }
    }
}
