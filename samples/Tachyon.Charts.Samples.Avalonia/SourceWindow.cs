using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Tachyon.Charts.Samples.Shared;

namespace Tachyon.Charts.Samples.Avalonia;

/// <summary>Shows the code of a showroom page, a tab for each file, with buttons to copy and save it.</summary>
internal sealed class SourceWindow : Window
{
    private const double LineHeight = 18;

    private readonly TabControl _tabs = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };

    public SourceWindow(string page, IReadOnlyList<SourceFile> files)
    {
        Title = SampleText.T("Source code") + " – " + page;
        Width = 1000;
        Height = 760;
        Background = new SolidColorBrush(Color.FromRgb(15, 19, 25));
        RequestedThemeVariant = global::Avalonia.Styling.ThemeVariant.Dark;
        StyleTabs();
        var items = new List<TabItem>();
        foreach (var file in files)
        {
            var item = new TabItem { Header = Path.GetFileName(file.Name), Tag = file, Content = CreateView(file) };
            ToolTip.SetTip(item, file.Name);
            items.Add(item);
        }

        _tabs.ItemsSource = items;
        _tabs.SelectedIndex = 0;
        var copy = new Button { Content = SampleText.T("Copy"), Margin = new Thickness(0, 0, 6, 0) };
        copy.Click += async (_, _) => await CopyAsync();
        var save = new Button { Content = SampleText.T("Save file"), Margin = new Thickness(0, 0, 6, 0) };
        save.Click += async (_, _) => await SaveAsync();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 10, 12, 10) };
        bar.Children.Add(copy);
        bar.Children.Add(save);
        bar.Children.Add(_status);
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(_tabs);
        Content = root;
    }

    private SourceFile? Selected => (_tabs.SelectedItem as TabItem)?.Tag as SourceFile;

    // The tabs take the colors of the showroom instead of the large default ones. The keys are those of Avalonia.Themes.Fluent 12.1.4.
    private void StyleTabs()
    {
        var ink = new SolidColorBrush(Color.FromRgb(232, 237, 244));
        var muted = new SolidColorBrush(Color.FromRgb(139, 151, 168));
        var raised = new SolidColorBrush(Color.FromRgb(30, 37, 48));
        var accent = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb(56, 189, 248), 0), new GradientStop(Color.FromRgb(99, 102, 241), 1) },
        };
        Resources["TabItemHeaderFontSize"] = 13.0;
        Resources["TabItemHeaderThemeFontWeight"] = FontWeight.Normal;
        Resources["TabItemHeaderMargin"] = new Thickness(14, 0, 14, 0);
        Resources["TabItemHeaderSelectedPipeFill"] = accent;
        Resources["TabItemHeaderForegroundUnselected"] = muted;
        Resources["TabItemHeaderForegroundUnselectedPointerOver"] = ink;
        Resources["TabItemHeaderForegroundUnselectedPressed"] = ink;
        Resources["TabItemHeaderForegroundSelected"] = ink;
        Resources["TabItemHeaderForegroundSelectedPointerOver"] = ink;
        Resources["TabItemHeaderForegroundSelectedPressed"] = ink;
        Resources["TabItemHeaderBackgroundUnselected"] = Brushes.Transparent;
        Resources["TabItemHeaderBackgroundUnselectedPointerOver"] = Brushes.Transparent;
        Resources["TabItemHeaderBackgroundSelected"] = raised;
        Resources["TabItemHeaderBackgroundSelectedPointerOver"] = raised;
        Resources["TabItemHeaderBackgroundSelectedPressed"] = raised;
    }

    // A fixed line height keeps the lines as in an editor.
    private static ScrollViewer CreateView(SourceFile file)
    {
        var brushes = CodeTokens.DarkColors.ToDictionary(pair => pair.Key, pair => (IBrush)new SolidColorBrush(Color.FromRgb(pair.Value.R, pair.Value.G, pair.Value.B)));
        var text = new SelectableTextBlock
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New, monospace"),
            FontSize = 13,
            LineHeight = LineHeight,
            Margin = new Thickness(12, 8, 12, 8),
            Inlines = [],
        };
        var code = file.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        foreach (var token in CodeTokens.Split(code))
        {
            var parts = code.Substring(token.Start, token.Length).Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    text.Inlines.Add(new LineBreak());
                }

                if (parts[i].Length > 0)
                {
                    text.Inlines.Add(new Run(parts[i]) { Foreground = brushes[token.Kind] });
                }
            }
        }

        var view = new ScrollViewer
        {
            Content = text,
            Background = new SolidColorBrush(Color.FromRgb(11, 14, 19)),
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        return view;
    }

    private async Task CopyAsync()
    {
        if (Selected is { } file && GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(file.Text);
            _status.Text = SampleText.T("The code is on the clipboard");
        }
    }

    // Next to the exports of the app, without a dialog.
    private async Task SaveAsync()
    {
        if (Selected is not { } file)
        {
            return;
        }

        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tachyon Source");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, Path.GetFileName(file.Name));
        await File.WriteAllTextAsync(path, file.Text);
        _status.Text = SampleText.T("Saved: ") + path;
    }
}
