using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Tachyon.Charts.Avalonia;

namespace Tachyon.Charts.Configurator.Avalonia;

/// <summary>
/// The configurator: settings on the left, the chart in the middle, the code on the right. Every change shows at once in
/// both, since preview and code come from the same description.
/// </summary>
internal sealed class ConfiguratorWindow : Window
{
    private static readonly IBrush _ink = new SolidColorBrush(Color.Parse("#E8EDF4"));
    private static readonly IBrush _muted = new SolidColorBrush(Color.Parse("#8B97A8"));
    private static readonly IBrush _panel = new SolidColorBrush(Color.Parse("#161B23"));
    private static readonly IBrush _sidebar = new SolidColorBrush(Color.Parse("#141922"));
    private static readonly IBrush _shell = new SolidColorBrush(Color.Parse("#0D1016"));
    private static readonly IBrush _problem = new SolidColorBrush(Color.Parse("#FF7043"));

    private readonly ConfiguratorSession _session = new(new ChartCatalog());
    private readonly Chart _preview = new();
    private readonly StackPanel _fields = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _series = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TabControl _code = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 6, 12, 8), TextWrapping = TextWrapping.Wrap };
    private bool _isFilling;

    public ConfiguratorWindow()
    {
        Title = "Tachyon Charts Configurator";
        Width = 1600;
        Height = 950;
        Background = _shell;
        Foreground = _ink;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("380,*,560") };
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
        _session.Changed += ShowState;
        _session.FieldsChanged += Fill;
        Fill();
        ShowState();
    }

    /// <summary>Saves a picture of the window once it shows, then closes it.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Gets the identifier of the type to start with, or <see langword="null"/> for the first.</summary>
    public string? StartType { get; init; }

    private ComboBox? _types;

    private void SelectStartType()
    {
        var index = _session.Catalog.Types.ToList().FindIndex(type => type.Id == StartType);
        if (index >= 0 && _types is not null)
        {
            _types.SelectedIndex = index;
        }
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        SelectStartType();
        if (ScreenshotPath is { } path && Content is Control content)
        {
            await Task.Delay(2000);
            var size = new PixelSize((int)content.Bounds.Width, (int)content.Bounds.Height);
            using var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(content);
            bitmap.Save(path, new PngBitmapEncoderOptions());
            Close();
        }
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, FontSize = 13, Margin = new Thickness(0, 14, 0, 6) };

    private static Button Action(string text, Action action)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(7) };
        button.Click += (_, _) => action();
        return button;
    }

    private Control CreateSettings()
    {
        var names = _session.Catalog.Types.Select(type => type.Name).ToList();
        var types = _types = new ComboBox { Margin = new Thickness(0, 0, 0, 8), ItemsSource = names, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        types.SelectionChanged += (_, _) => _session.NewChart(_session.Catalog.Types[types.SelectedIndex].Id);
        var add = new ComboBox { Margin = new Thickness(0, 0, 6, 6), ItemsSource = names, SelectedIndex = 0, MinWidth = 120 };
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
        top.Children.Add(new TextBlock { Text = "Configurator", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        top.Children.Add(Heading("Chart type"));
        top.Children.Add(types);
        top.Children.Add(buttons);
        top.Children.Add(Heading("Series"));
        top.Children.Add(_series);
        var panel = new DockPanel { Background = _sidebar };
        DockPanel.SetDock(top, Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(new ScrollViewer { Content = _fields, Padding = new Thickness(16, 0, 8, 16) });
        return panel;
    }

    private Control CreateCode()
    {
        var framework = new ComboBox { ItemsSource = new[] { "WPF", "Avalonia" }, SelectedIndex = 1, MinWidth = 110, Margin = new Thickness(0, 0, 6, 6) };
        _session.Options = _session.Options with { Framework = UiFramework.Avalonia };
        framework.SelectionChanged += (_, _) => _session.Options = _session.Options with { Framework = framework.SelectedIndex == 0 ? UiFramework.Wpf : UiFramework.Avalonia };
        var bar = new WrapPanel { Margin = new Thickness(8, 12, 8, 8) };
        bar.Children.Add(framework);
        bar.Children.Add(Action("Copy", () => _ = CopyAsync()));
        bar.Children.Add(Action("Save project", SaveProject));
        var panel = new DockPanel { Background = _panel };
        DockPanel.SetDock(bar, Dock.Top);
        panel.Children.Add(bar);
        panel.Children.Add(_code);
        return panel;
    }

    private async Task CopyAsync()
    {
        if (_code.SelectedItem is TabItem { Content: TextBox box } && GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(box.Text);
            _status.Text = "The code is on the clipboard.";
        }
    }

    // Into the documents of the user, without a dialog, the same on every platform.
    private void SaveProject()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Tachyon Configurator");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, _session.Options.ProjectName + ".zip");
        using (var file = File.Create(path))
        {
            new CodeGenerator(_session.Catalog).WriteZip(_session.Spec, _session.Options, file);
        }

        _status.Text = "Saved: " + path;
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

            _fields.Children.Add(Editor(field));
        }
    }

    private static Control Editor(FieldModel field)
    {
        var problem = new TextBlock { Foreground = _problem, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        void Report(string? text) => (problem.Text, problem.IsVisible) = (text ?? string.Empty, text is not null);
        Control editor;
        switch (field.Info.Kind)
        {
            case ParameterKind.Flag:
                var check = new CheckBox { Content = field.Info.Label, IsChecked = (bool)field.Value };
                check.IsCheckedChanged += (_, _) => Report(field.Set(check.IsChecked == true));
                return check;
            case ParameterKind.Choice:
                var choice = new ComboBox { ItemsSource = field.Info.Choices, SelectedItem = field.Value, Width = 190 };
                choice.SelectionChanged += (_, _) => Report(choice.SelectedItem is string text ? field.Set(text) : null);
                editor = choice;
                break;
            default:
                var box = new TextBox { Text = field.Text, Width = 190 };
                box.TextChanged += (_, _) => Report(field.SetText(box.Text ?? string.Empty));
                editor = box;
                break;
        }

        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        if (field.Info.Description.Length > 0)
        {
            ToolTip.SetTip(row, field.Info.Description);
        }

        DockPanel.SetDock(editor, Dock.Right);
        row.Children.Add(editor);
        row.Children.Add(new TextBlock { Text = field.Info.Label, VerticalAlignment = VerticalAlignment.Center, Foreground = _muted });
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
            _status.Foreground = _problem;
            return;
        }

        _preview.Configure(_session.Apply);
        var selected = _code.SelectedIndex;
        var tabs = new List<TabItem>();
        foreach (var (name, text) in _session.Code)
        {
            tabs.Add(new TabItem
            {
                Header = name,
                FontSize = 13,
                Content = new TextBox
                {
                    Text = text,
                    IsReadOnly = true,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
                    FontSize = 12.5,
                    Background = _panel,
                    BorderThickness = new Thickness(0),
                    AcceptsReturn = true,
                },
            });
        }

        _code.ItemsSource = tabs;
        _code.SelectedIndex = Math.Max(0, selected);
        _status.Foreground = _muted;
        _status.Text = string.Create(CultureInfo.InvariantCulture, $"Code in {_session.LastUpdate.TotalMilliseconds:F1} ms, preview described in {clock.Elapsed.TotalMilliseconds:F1} ms.");
    }
}

/// <summary>The application.</summary>
internal sealed class ConfiguratorApplication : Application
{
    public static string? ScreenshotPath { get; set; }

    public static string? StartType { get; set; }

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new ConfiguratorWindow { ScreenshotPath = ScreenshotPath, StartType = StartType };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>Starts the configurator.</summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var type = Array.IndexOf(args, "--type");
        if (type >= 0 && type + 1 < args.Length)
        {
            ConfiguratorApplication.StartType = args[type + 1];
        }

        var screenshot = Array.IndexOf(args, "--screenshot");
        if (screenshot >= 0 && screenshot + 1 < args.Length)
        {
            // Pictures draw on the CPU, so that the chart is part of the picture of the window.
            ConfiguratorApplication.ScreenshotPath = args[screenshot + 1];
            Chart.Renderer = Rendering.RendererPreference.Skia;
        }

        return AppBuilder.Configure<ConfiguratorApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
