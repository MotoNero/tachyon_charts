using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
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

    // Data, checks and code are made away from the window, which only shows the result.
    private readonly ConfiguratorSession _session = new(ChartCatalog.CreateWithAddOns(), SynchronizationContext.Current);
    private readonly Chart _preview = new();

    // The charts of the preview: one, or one per linked view.
    private readonly global::Avalonia.Controls.Primitives.UniformGrid _previews = new() { Columns = 1 };
    private readonly StackPanel _fields = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _series = new() { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TabControl _code = new();
    private List<TabItem> _tabs = [];
    private readonly TextBlock _status = new() { Margin = new Thickness(12, 6, 12, 8), TextWrapping = TextWrapping.Wrap };
    private bool _isFilling;

    public ConfiguratorWindow()
    {
        Title = "Tachyon Charts Configurator";
        Width = 1600;
        Height = 950;
        Background = _shell;
        Foreground = _ink;
        // Settings, preview and code, with splitters between them to share the width as needed.
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("380,Auto,*,Auto,560") };
        root.ColumnDefinitions[0].MinWidth = 260;
        root.ColumnDefinitions[2].MinWidth = 240;
        root.ColumnDefinitions[4].MinWidth = 240;
        foreach (var column in new[] { 1, 3 })
        {
            var splitter = new GridSplitter { Width = 6, Background = new SolidColorBrush(Color.Parse("#232B37")), ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(splitter, column);
            root.Children.Add(splitter);
        }

        root.Children.Add(CreateSettings());
        var middle = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        middle.Children.Add(_status);
        _previews.Children.Add(_preview);
        middle.Children.Add(new Border { Child = _previews, Margin = new Thickness(8), CornerRadius = new CornerRadius(8), ClipToBounds = true });
        Grid.SetColumn(middle, 2);
        root.Children.Add(middle);
        var code = CreateCode();
        Grid.SetColumn(code, 4);
        root.Children.Add(code);
        Content = root;
        _session.Changed += ShowState;
        _session.UpdateStarted += () => (_status.Text, _status.Foreground) = ("Updating …", _muted);
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
        var kinds = new ComboBox { Margin = new Thickness(0, 0, 6, 6), ItemsSource = ConfiguratorSession.AnnotationKinds.Select(kind => kind.Name).ToList(), SelectedIndex = 0, MinWidth = 120 };
        var notes = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        notes.Children.Add(kinds);
        notes.Children.Add(Action("+ Annotation", () => _session.AddAnnotation(ConfiguratorSession.AnnotationKinds[kinds.SelectedIndex].Id)));
        notes.Children.Add(Action("− Annotation", _session.RemoveAnnotation));
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
        top.Children.Add(notes);
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
        foreach (var field in _session.SeriesFields.Concat(_session.ChartFields).Concat(_session.AxisFields).Concat(_session.ModifierFields).Concat(_session.AnnotationFields))
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
                var choice = new ComboBox { ItemsSource = field.Info.Choices, SelectedItem = field.Value, HorizontalAlignment = HorizontalAlignment.Stretch };
                choice.SelectionChanged += (_, _) => Report(choice.SelectedItem is string text ? field.Set(text) : null);
                editor = choice;
                break;
            case ParameterKind.Choices:
                // A box per choice; the value lists the checked ones in the order of the choices.
                var chosen = ParameterInfo.Split((string)field.Value);
                var boxes = new WrapPanel();
                foreach (var item in field.Info.Choices)
                {
                    var option = new CheckBox { Content = new TextBlock { Text = item }, Tag = item, IsChecked = chosen.Contains(item), Margin = new Thickness(0, 0, 10, 2) };
                    option.IsCheckedChanged += (_, _) => Report(field.Set(string.Join(",", boxes.Children.OfType<CheckBox>().Where(box => box.IsChecked == true).Select(box => (string)box.Tag!))));
                    boxes.Children.Add(option);
                }

                editor = boxes;
                break;
            case ParameterKind.File:
                var path = new TextBox { Text = field.Text };
                path.TextChanged += (_, _) => Report(field.SetText(path.Text ?? string.Empty));
                var browse = new Button { Content = "…", Margin = new Thickness(4, 0, 0, 0) };
                browse.Click += async (_, _) =>
                {
                    if (TopLevel.GetTopLevel(browse) is { } top)
                    {
                        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
                        if (files.Count > 0 && files[0].TryGetLocalPath() is { } chosen)
                        {
                            path.Text = chosen;
                        }
                    }
                };
                var file = new DockPanel();
                DockPanel.SetDock(browse, Dock.Right);
                file.Children.Add(browse);
                file.Children.Add(path);
                editor = file;
                break;
            default:
                var box = new TextBox { Text = field.Text, HorizontalAlignment = HorizontalAlignment.Stretch };
                box.TextChanged += (_, _) => Report(field.SetText(box.Text ?? string.Empty));
                editor = box;
                break;
        }

        // The label in a column of its own that wraps, a gap, then the editor, which grows and shrinks with the panel.
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ColumnDefinitions = new ColumnDefinitions("*,12,1.2*") };
        row.ColumnDefinitions[0].MinWidth = 70;
        row.ColumnDefinitions[2].MinWidth = 110;
        if (field.Info.Description.Length > 0)
        {
            ToolTip.SetTip(row, field.Info.Description);
        }

        Grid.SetColumn(editor, 2);
        row.Children.Add(new TextBlock { Text = field.Info.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = _muted });
        row.Children.Add(editor);
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(problem);
        return stack;
    }

    // The menu the generated window gets with the export option, here on the preview.
    private ContextMenu ExportMenu()
    {
        var items = new List<MenuItem>
        {
            ExportItem("Save as PNG …", "chart.png", () => _preview.ExportPngAsync()),
            ExportItem("Save as SVG …", "chart.svg", () => _preview.ExportSvgAsync()),
            ExportItem("Save as PDF …", "chart.pdf", () => _preview.ExportPdfAsync()),
            ExportItem("Save data as CSV …", "chart.csv", () => _preview.ExportCsvAsync()),
        };
        var video = new MenuItem { Header = _preview.IsRecording ? "Stop recording" : "Record video …" };
        video.Click += async (_, _) =>
        {
            if (_preview.IsRecording)
            {
                await _preview.StopRecordingAsync();
                video.Header = "Record video …";
                return;
            }

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = "chart.mp4" });
            if (file?.TryGetLocalPath() is { } path)
            {
                _preview.StartRecording(path);
                video.Header = "Stop recording";
            }
        };
        items.Add(video);
        return new ContextMenu { ItemsSource = items };
    }

    private MenuItem ExportItem(string header, string name, Func<Task<byte[]>> export)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) =>
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = name });
            if (file is not null)
            {
                await using var stream = await file.OpenWriteAsync();
                await stream.WriteAsync(await export());
            }
        };
        return item;
    }

    // One chart per linked view, in the columns of the description; the first is the one the export menu saves.
    private void ShowViews()
    {
        var views = _session.Views;
        while (_previews.Children.Count > views.Count)
        {
            _previews.Children.RemoveAt(_previews.Children.Count - 1);
        }

        while (_previews.Children.Count < views.Count)
        {
            _previews.Children.Add(new Chart { Margin = new Thickness(2) });
        }

        _previews.Columns = _session.ViewColumns;
        for (var i = 0; i < views.Count; i++)
        {
            var view = i;
            ((Chart)_previews.Children[i]).Configure(chart => _session.Apply(view, chart));
        }
    }

    // Code at once, the preview right after; the status says how long both took.
    private static TextBox CodeBox()
    {
        var box = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"),
            FontSize = 12.5,
            Background = _panel,
            BorderThickness = new Thickness(0),
            SelectionBrush = new SolidColorBrush(Color.Parse("#3A86FF")),
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(box, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(box, global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        return box;
    }

    private void ShowState()
    {
        var clock = Stopwatch.StartNew();
        if (_session.Problems.Count > 0)
        {
            _status.Text = string.Join(" ", _session.Problems);
            _status.Foreground = _problem;
            return;
        }

        try
        {
            ShowViews();
            _preview.ContextMenu = _session.Spec.ExportMenu ? ExportMenu() : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            // The session checks the description first; whatever still fails shows here instead of closing the app.
            _status.Text = exception.Message;
            _status.Foreground = _problem;
            return;
        }
        // The boxes stay while the tabs keep their names, so selection and scrolling survive a change.
        var selected = _code.SelectedIndex;
        if (!_session.Code.Keys.SequenceEqual(_tabs.Select(tab => (string)tab.Header!)))
        {
            _tabs = [.. _session.Code.Keys.Select(name => new TabItem { Header = name, FontSize = 13, Content = CodeBox() })];
            _code.ItemsSource = _tabs;
        }

        foreach (var tab in _tabs)
        {
            var box = (TextBox)tab.Content!;
            if (box.Text != _session.Code[(string)tab.Header!])
            {
                box.Text = _session.Code[(string)tab.Header!];
            }
        }

        _code.SelectedIndex = Math.Max(0, selected);
        _status.Foreground = _muted;
        _status.Text = string.Create(CultureInfo.InvariantCulture, $"Data and code in {_session.LastUpdate.TotalMilliseconds:F0} ms, preview in {clock.Elapsed.TotalMilliseconds:F0} ms.");
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
