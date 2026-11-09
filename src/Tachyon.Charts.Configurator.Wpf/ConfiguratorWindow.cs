using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Tachyon.Charts.Samples.Wpf;
using Tachyon.Charts.Wpf;

namespace Tachyon.Charts.Configurator.Wpf;

/// <summary>
/// The configurator: settings on the left, the chart in the middle, the code on the right. Every change shows at once in
/// both, since preview and code come from the same description.
/// </summary>
internal sealed class ConfiguratorWindow : Window
{
    private readonly ConfiguratorSession _session = new(new ChartCatalog());
    private readonly Chart _preview = new();
    private readonly StackPanel _fields = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _series = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TabControl _code = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 6, 12, 8), TextWrapping = TextWrapping.Wrap };
    private bool _isFilling;

    public ConfiguratorWindow()
    {
        Title = "Tachyon Charts Configurator";
        Width = 1600;
        Height = 950;
        Background = (Brush)FindResource("Shell");
        TextElement.SetForeground(this, (Brush)FindResource("Ink"));
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560) });
        root.Children.Add(CreateSettings());
        var middle = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        middle.Children.Add(_status);
        middle.Children.Add(new Border { Child = _preview, Margin = new Thickness(8), CornerRadius = new CornerRadius(8), ClipToBounds = true });
        Grid.SetColumn(middle, 1);
        root.Children.Add(middle);
        var code = CreateCode();
        Grid.SetColumn(code, 2);
        root.Children.Add(code);
        Content = root;
        SourceInitialized += (_, _) => SampleStyle.DarkenTitleBar(this);
        _session.Changed += ShowState;
        _session.FieldsChanged += Fill;
        Fill();
        ShowState();
    }

    /// <summary>Saves a picture of the window once it shows, then closes it.</summary>
    public string? ScreenshotPath { get; init; }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (ScreenshotPath is { } path)
        {
            await Task.Delay(1500);
            var content = (FrameworkElement)Content;
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var file = File.Create(path))
            {
                encoder.Save(file);
            }

            Close();
        }
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 14, 0, 6) };

    private UIElement CreateSettings()
    {
        var types = new ComboBox { Margin = new Thickness(0, 0, 0, 8), ItemsSource = _session.Catalog.Types.Select(type => type.Name).ToList(), SelectedIndex = 0 };
        types.SelectionChanged += (_, _) => _session.NewChart(_session.Catalog.Types[types.SelectedIndex].Id);
        var add = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ItemsSource = _session.Catalog.Types.Select(type => type.Name).ToList(), SelectedIndex = 0, MinWidth = 120 };
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(add);
        buttons.Children.Add(Action("+ Series", () => _session.AddSeries(_session.Catalog.Types[add.SelectedIndex].Id)));
        buttons.Children.Add(Action("− Series", _session.RemoveSeries));
        buttons.Children.Add(Action("+ Y axis", _session.AddYAxis));
        _series.SelectionChanged += (_, _) =>
        {
            if (!_isFilling && _series.SelectedIndex >= 0)
            {
                _session.SelectedSeries = _series.SelectedIndex;
            }
        };
        var top = new StackPanel { Margin = new Thickness(16, 16, 8, 0) };
        top.Children.Add(new TextBlock { Text = "Configurator", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        top.Children.Add(Heading("Chart type"));
        top.Children.Add(types);
        top.Children.Add(buttons);
        top.Children.Add(Heading("Series"));
        top.Children.Add(_series);
        var panel = new DockPanel { Background = (Brush)FindResource("Sidebar") };
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new ScrollViewer { Content = _fields, Padding = new Thickness(16, 0, 8, 16), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        return panel;
    }

    private UIElement CreateCode()
    {
        var framework = new ComboBox { ItemsSource = new[] { "WPF", "Avalonia" }, SelectedIndex = 0, MinWidth = 110, Margin = new Thickness(0, 0, 6, 0) };
        framework.SelectionChanged += (_, _) => _session.Options = _session.Options with { Framework = framework.SelectedIndex == 0 ? UiFramework.Wpf : UiFramework.Avalonia };
        var bar = new WrapPanel { Margin = new Thickness(8, 12, 8, 8) };
        bar.Children.Add(framework);
        bar.Children.Add(Action("Copy", () => Clipboard.SetText(((TextBox)((TabItem)_code.SelectedItem).Content).Text)));
        bar.Children.Add(Action("Save project …", SaveProject));
        var panel = new DockPanel { Background = (Brush)FindResource("Panel") };
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(_code);
        return panel;
    }

    private Button Action(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 0) };
        button.Click += (_, _) => action();
        return button;
    }

    private void SaveProject()
    {
        var dialog = new SaveFileDialog { FileName = _session.Options.ProjectName + ".zip", Filter = "ZIP archive|*.zip" };
        if (dialog.ShowDialog(this) == true)
        {
            using var file = File.Create(dialog.FileName);
            new CodeGenerator(_session.Catalog).WriteZip(_session.Spec, _session.Options, file);
            _status.Text = "Saved: " + dialog.FileName;
        }
    }

    // The settings are built anew when fields come or go; a changed value keeps its control and its focus.
    private void Fill()
    {
        _isFilling = true;
        _series.ItemsSource = _session.Spec.Series.Select((series, index) => $"{index + 1}. {series.Name}").ToList();
        _series.SelectedIndex = _session.SelectedSeries;
        _isFilling = false;
        _fields.Children.Clear();
        string? group = null;
        foreach (var field in _session.SeriesFields.Concat(_session.ChartFields).Concat(_session.AxisFields).Concat(_session.ModifierFields))
        {
            if (field.Group != group)
            {
                group = field.Group;
                _fields.Children.Add(Heading(group));
            }

            _fields.Children.Add(Control(field));
        }
    }

    private FrameworkElement Control(FieldModel field)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = field.Info.Description.Length > 0 ? field.Info.Description : null };
        var problem = new TextBlock { Foreground = Brushes.OrangeRed, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        void Report(string? text) => (problem.Text, problem.Visibility) = (text ?? string.Empty, text is null ? Visibility.Collapsed : Visibility.Visible);
        FrameworkElement editor;
        switch (field.Info.Kind)
        {
            case ParameterKind.Flag:
                var check = new CheckBox { Content = field.Info.Label, IsChecked = (bool)field.Value, Foreground = (Brush)FindResource("Ink") };
                check.Click += (_, _) => Report(field.Set(check.IsChecked == true));
                return check;
            case ParameterKind.Choice:
                var choice = new ComboBox { ItemsSource = field.Info.Choices, SelectedItem = field.Value, Width = 190 };
                choice.SelectionChanged += (_, _) => Report(choice.SelectedItem is string text ? field.Set(text) : null);
                editor = choice;
                break;
            default:
                var box = new TextBox { Text = field.Text, Width = 190, Background = (Brush)FindResource("Raised"), Foreground = (Brush)FindResource("Ink"), BorderBrush = (Brush)FindResource("Line"), Padding = new Thickness(4, 2, 4, 2) };
                box.TextChanged += (_, _) => Report(field.SetText(box.Text));
                editor = box;
                break;
        }

        DockPanel.SetDock(editor, Dock.Right);
        row.Children.Add(editor);
        row.Children.Add(new TextBlock { Text = field.Info.Label, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("Muted") });
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(problem);
        return stack;
    }

    // Code at once, the preview right after; the status says how long both took.
    private void ShowState()
    {
        var clock = Stopwatch.StartNew();
        if (_session.Problems.Count > 0)
        {
            _status.Text = string.Join(" ", _session.Problems);
            _status.Foreground = Brushes.OrangeRed;
            return;
        }

        _preview.Configure(_session.Apply);
        var selected = _code.SelectedIndex;
        _code.Items.Clear();
        foreach (var (name, text) in _session.Code)
        {
            _code.Items.Add(new TabItem
            {
                Header = name,
                Content = new TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12.5,
                    Background = (Brush)FindResource("Panel"),
                    Foreground = (Brush)FindResource("Ink"),
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(10),
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                },
            });
        }

        _code.SelectedIndex = Math.Max(0, selected);
        _status.Foreground = (Brush)FindResource("Muted");
        _status.Text = string.Create(CultureInfo.InvariantCulture, $"Code in {_session.LastUpdate.TotalMilliseconds:F1} ms, preview described in {clock.Elapsed.TotalMilliseconds:F1} ms.");
    }
}

/// <summary>Starts the configurator.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var screenshot = Array.IndexOf(args, "--screenshot");
        var path = screenshot >= 0 && screenshot + 1 < args.Length ? args[screenshot + 1] : null;
        if (path is not null)
        {
            // Pictures draw on the CPU, so that the chart is part of the picture of the window.
            Chart.Renderer = Rendering.RendererPreference.Skia;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        SampleStyle.Apply(application);
        application.Run(new ConfiguratorWindow { ScreenshotPath = path });
    }
}
