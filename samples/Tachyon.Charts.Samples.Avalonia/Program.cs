using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Tachyon.Charts.Avalonia;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Export;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.Samples.Shared;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.Samples.Shared.Examples.Gallery;
using Tachyon.Charts.Samples.Shared.Examples.Instruments;
using Tachyon.Charts.Samples.Shared.Examples.Interactive;
using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.Samples.Shared.Scenes;
using AvaloniaPixelSize = Avalonia.PixelSize;
using PixelFormat = Avalonia.Platform.PixelFormat;
using PixelSize = Tachyon.Charts.Rendering.PixelSize;
using SelectionMode = Tachyon.Charts.Core.Input.SelectionMode;
using Vector = Avalonia.Vector;

namespace Tachyon.Charts.Samples.Avalonia;

/// <summary>
/// The showroom with the same scenes as the WPF sample.
/// </summary>
internal sealed class SampleWindow : Window
{
    private const string LiveEntry = "Live-Chart (interaktiv)";
    private const string RasterEntry = "Raster (interaktiv)";
    private const string ScopeEntry = "Oszilloskop (interaktiv)";
    private const string SpectrumEntry = "Spektrum (interaktiv)";
    private const int SpectrogramColumns = 1024;
    private const int SpectrogramRows = 1024;
    private const int SpectrogramRowsPerSecond = 60;

    private static readonly Color _ink = Color.Parse("#E8EDF4");
    private readonly ListBox _list = new() { Background = Brushes.Transparent, Margin = new Thickness(10, 0, 6, 10) };
    private readonly Grid _content = new();
    private readonly Chart _live = new();
    private readonly UniformGrid _raster = new() { Columns = 2, IsVisible = false };
    private readonly ScopeDemo _scopeDemo = new();
    private readonly Chart _scope = new() { IsVisible = false };
    private readonly SpectrumDemo _spectrumDemo = new();
    private readonly Dictionary<string, Chart> _pages = [];
    private readonly Chart _spectrum = new();
    private readonly Chart _spectrumHistory = new();
    private readonly UniformGrid _spectrumPanel = new() { Columns = 1, IsVisible = false };
    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _sceneView = new() { IsVisible = false };
    private readonly TextBlock _hint = new() { FontSize = 12.5, Opacity = 0.65, Margin = new Thickness(0, 3, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _pageTitle = new() { FontSize = 24, FontWeight = FontWeight.SemiBold };
    private readonly ComboBox _theme = new() { Width = 170 };
    private readonly ComboBox _language = new() { Width = 120, Margin = new Thickness(8, 0, 0, 0) };
    private readonly ComboBox _trigger = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _persistence = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    private readonly StackPanel _scopeControls = new() { Orientation = Orientation.Horizontal, IsVisible = false };
    private readonly List<(Button Button, string Text)> _actions = [];
    private readonly List<(MenuItem Item, string Text)> _menuItems = [];
    private readonly Dictionary<string, Control> _views3D = [];
    private readonly Dictionary<string, Tachyon.Charts.ThreeD.Avalonia.Chart3D> _hosts3D = [];
    private readonly Dictionary<string, Chart> _companions = [];
    private readonly Tachyon.Charts.Core.Input.SelectionModifier _selection = new() { Mode = SelectionMode.Lasso };
    private readonly Button _select = new() { Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(7) };
    private bool _isSelecting;
    private bool _showsDiagnostics = ShowsDiagnosticsAtStart;
    private readonly Button _record = new() { Margin = new Thickness(0, 0, 6, 0), Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) };
    private readonly global::Avalonia.Threading.DispatcherTimer _recordingClock = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
    private IChartRecording? _recording;
    private string? _recordingPath;
    private readonly TextBlock _stillInfo = new() { FontSize = 12, Margin = new Thickness(8), Padding = new Thickness(6, 3, 6, 3), Background = new SolidColorBrush(Color.FromArgb(200, 20, 24, 32)), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsVisible = false };
    private readonly IReadOnlyList<SamplePage> _pageList;
    private SamplePage? _selected;
    private bool _isSwitching;
    private readonly ComboBox _colormap = new() { Width = 130, Margin = new Thickness(0, 0, 8, 0), IsVisible = false };
    private readonly LiveSignal _signal = new();
    private readonly SpectrogramData _spectrogram = new(SpectrogramColumns, SpectrogramRows);
    private readonly Thread _producer;

    private System.Threading.Timer? _turning;

    public static bool TurnsView { get; set; }

    /// <summary>Gets or sets a value indicating whether the diagnostics are on from the start, as after F12.</summary>
    public static bool ShowsDiagnosticsAtStart { get; set; }

    /// <summary>Gets or sets a value indicating whether the code of the start page opens at once, as after F11.</summary>
    public static bool ShowsSourceAtStart { get; set; }

    /// <summary>Gets or sets a value indicating whether a recording of six seconds runs at the start, to measure it without a mouse.</summary>
    public static bool RecordsAtStart { get; set; }

    /// <summary>Gets or sets the theme chosen at the start, by its name, as if picked in the list.</summary>
    public static string? ThemeAtStart { get; set; }

    public static bool ZoomsIn { get; set; }
    private volatile bool _closing;
    private volatile bool _showsRaster;

    private void ApplyTrigger()
    {
        if (_trigger.SelectedIndex >= 0)
        {
            _scopeDemo.Trigger.Mode = (TriggerMode)_trigger.SelectedIndex;
            _scopeDemo.Trigger.Arm();
        }
    }

    private void ApplyPersistence()
    {
        if (_persistence.SelectedIndex >= 0)
        {
            _scopeDemo.Display.Persistence = (ScopePersistence)_persistence.SelectedIndex;
        }
    }

    private void SelectLanguage()
    {
        if (_language.SelectedIndex >= 0)
        {
            ChartText.Language = (ChartLanguage)_language.SelectedIndex;
            ApplyLanguage();
        }
    }

    public SampleWindow(string? startPage)
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Width = 1280;
        Height = 800;

        _live.Configure(c => LiveChartPage.Configure(c, _signal, _selection));

        _sceneView.Content = new Grid { Children = { _image, _stillInfo } };
        _content.Children.Add(_live);
        _content.Children.Add(_raster);
        _scope.Configure(c => OscilloscopePage.Configure(c, _scopeDemo));
        _content.Children.Add(_scope);
        _spectrum.Configure(c => SpectrumAnalyzerPage.Configure(c, _spectrumDemo));
        _spectrumHistory.Configure(c => SpectrumAnalyzerPage.ConfigureHistory(c, _spectrumDemo));
        _spectrumPanel.Children.Add(_spectrum);
        _spectrumPanel.Children.Add(_spectrumHistory);
        _content.Children.Add(_spectrumPanel);

        _content.Children.Add(_sceneView);

        _pageList = SampleText.CreatePages(hasLinkedCharts: false);
        _list.ItemTemplate = new FuncDataTemplate<SamplePage?>((page, _) => CreateListText(page));
        _list.ItemsSource = _pageList;

        _colormap.ItemsSource = Colormaps.All.Select(map => map.Name).ToArray();
        _colormap.SelectedIndex = 0;
        _colormap.SelectionChanged += (_, _) => ApplyColormap();

        _trigger.SelectionChanged += (_, _) => ApplyTrigger();
        _persistence.SelectionChanged += (_, _) => ApplyPersistence();
        _scopeControls.Children.Add(_trigger);
        _scopeControls.Children.Add(_persistence);

        _theme.SelectionChanged += (_, _) => ApplyTheme();
        _language.ItemsSource = SampleText.Languages;
        _language.SelectedIndex = 0;
        _language.SelectionChanged += (_, _) => SelectLanguage();

        var bar = new WrapPanel();
        _select.Content = SampleText.T("Select points: off");
        _select.Click += (_, _) => ToggleSelect();
        _selection.SelectionChanged += ShowSelected;
        bar.Children.Add(_select);
        bar.Children.Add(_colormap);
        bar.Children.Add(_scopeControls);
        bar.Children.Add(CreateAction("Source code (F11)", () =>
        {
            ShowSource();
            return Task.CompletedTask;
        }));
        AddExportButtons(bar);

        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#0F1319"), 0), new GradientStop(Color.Parse("#0B0E13"), 1) },
        };
        Content = BuildShell(bar);
        AddHandler(KeyDownEvent, OnPreviewKey, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        foreach (var chart in new[] { _live, _scope, _spectrum, _spectrumHistory })
        {
            chart.ContextMenu = CreateMenu();
        }

        ApplyLanguage();

        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is SamplePage { IsHeader: true })
            {
                _list.SelectedItem = _selected;
            }
            else if (!_isSwitching && _list.SelectedItem is SamplePage page)
            {
                _selected = page;
                ShowSelection();
            }
        };
        _list.SelectedItem = _pageList.FirstOrDefault(page => page.Id == startPage && !page.IsHeader) ?? _pageList[1];
        if (ShowsSourceAtStart)
        {
            Opened += (_, _) => ShowSource();
        }

        if (RecordsAtStart)
        {
            Opened += (_, _) => _ = RecordAtStart();
        }

        if (ThemeAtStart is { } themeName && BuiltInThemes.All.ToList().FindIndex(item => item.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase)) is >= 0 and var chosen)
        {
            _theme.SelectedIndex = chosen + 1;
        }

        _producer = new Thread(Produce) { IsBackground = true, Name = "Sample data" };
        _producer.Start();
    }

    private static string Hint(string name) => InstrumentPages.TryGet(name, out var page) ? page.Hint : Pages3D.Find(name)?.Hint ?? string.Empty;

    private string? SelectedId => _selected?.Id;

    private Theme? SelectedTheme => _theme.SelectedIndex > 0 ? BuiltInThemes.All[_theme.SelectedIndex - 1] : null;

    // Avalonia also builds the template without an item while the list changes.
    private static TextBlock CreateListText(SamplePage? page)
    {
        if (page is null)
        {
            return new TextBlock();
        }

        return page.IsHeader
        ? new TextBlock { Text = page.ToString(), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#5F6C7E")), Margin = new Thickness(0, 10, 0, 0) }
        : new TextBlock { Text = page.ToString(), Foreground = new SolidColorBrush(Color.Parse("#C4CDD9")) };
    }

    private static Border CreateCard(Control child, Thickness padding, double radius) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#161B23")),
        BorderBrush = new SolidColorBrush(Color.Parse("#262E3A")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(radius),
        Padding = padding,
        Child = child,
    };

    private Control BuildShell(Control bar)
    {
        var logo = new StackPanel { Margin = new Thickness(20, 22, 20, 18) };
        logo.Children.Add(new TextBlock
        {
            Text = "TACHYON",
            FontSize = 22,
            FontWeight = FontWeight.Bold,
            Foreground = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#38BDF8"), 0), new GradientStop(Color.Parse("#6366F1"), 1) },
            },
        });
        logo.Children.Add(new TextBlock { Text = "CHARTS", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = new SolidColorBrush(Color.Parse("#8B97A8")) });
        var sidebar = new DockPanel
        {
            Width = 250,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#171D26"), 0), new GradientStop(Color.Parse("#10141B"), 1) },
            },
        };
        DockPanel.SetDock(logo, Dock.Top);
        sidebar.Children.Add(logo);
        sidebar.Children.Add(_list);

        var settings = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        settings.Children.Add(_theme);
        settings.Children.Add(_language);
        var titles = new StackPanel();
        titles.Children.Add(_pageTitle);
        titles.Children.Add(_hint);
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(settings, Dock.Right);
        header.Children.Add(settings);
        header.Children.Add(titles);

        var toolbar = CreateCard(bar, new Thickness(8), 10);
        toolbar.Margin = new Thickness(0, 12, 0, 0);
        var card = CreateCard(_content, new Thickness(10), 12);
        var main = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Bottom);
        main.Children.Add(header);
        main.Children.Add(toolbar);
        main.Children.Add(card);

        var root = new DockPanel();
        DockPanel.SetDock(sidebar, Dock.Left);
        root.Children.Add(sidebar);
        root.Children.Add(main);
        return root;
    }

    private static void Fill(ComboBox box, IEnumerable<string> texts)
    {
        var index = Math.Max(box.SelectedIndex, 0);
        box.ItemsSource = texts.ToArray();
        box.SelectedIndex = index;
    }

    private void ApplyLanguage()
    {
        _isSwitching = true;
        Fill(_theme, new[] { SampleText.T("Scene default") }.Concat(BuiltInThemes.All.Select(theme => theme.Name)));
        Fill(_trigger, new[] { "Trigger Auto", "Trigger Normal", "Trigger Single" }.Select(SampleText.T));
        Fill(_persistence, new[] { "Persistence off", "Variable", "Infinite", "Color grade" }.Select(SampleText.T));
        _list.ItemsSource = null;
        _list.ItemsSource = _pageList;
        _list.SelectedItem = _selected;
        _isSwitching = false;
        foreach (var (button, text) in _actions)
        {
            button.Content = SampleText.T(text);
        }

        foreach (var (item, text) in _menuItems)
        {
            item.Header = SampleText.T(text);
        }

        _select.Content = SampleText.T(_isSelecting ? "Select points: on" : "Select points: off");

        Title = "Tachyon Charts – " + SampleText.T("Showroom");
        _live.Title = SampleText.T("Live: 50,000 points per second");
        _live.XAxes[0].Title = SampleText.T("Time in s");
        _live.YAxes[0].Title = SampleText.T("Value");
        string[] rasters = ["Heatmap 4096 × 4096", "Spectrogram, 60 rows per second", "Contour, filled", "5 million points: zoom in for markers"];
        for (var i = 0; i < _raster.Children.Count; i++)
        {
            ((Chart)_raster.Children[i]).Title = SampleText.T(rasters[i]);
        }

        if (_selected is not null)
        {
            ShowSelection();
        }
    }

    private void ToggleSelect()
    {
        _isSelecting = !_isSelecting;
        _selection.ModifierKey = _isSelecting ? Tachyon.Charts.Core.Input.InputModifiers.None : Tachyon.Charts.Core.Input.InputModifiers.Shift;
        _select.Content = SampleText.T(_isSelecting ? "Select points: on" : "Select points: off");
        if (_isSelecting)
        {
            _hint.Text = SampleText.T("Circle the points to select with the mouse. Then CSV exports only these.");
        }
    }

    private void ShowSelected(IReadOnlyList<Tachyon.Charts.Core.Input.SeriesSelection> selection)
    {
        var count = selection.Sum(item => (long)item.Indices.Count);
        if (count == 0)
        {
            ShowSelection();
            return;
        }

        _hint.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, SampleText.T("{0:N0} points selected. CSV exports only these. Esc clears the selection."), count);
    }

    private Task<byte[]> ExportCsv(Chart chart) =>
        chart.ExportCsvAsync(chart == _live && _selection.Selection.Count > 0 ? _selection.Selection : null);

    private ContextMenu CreateMenu()
    {
        var options = new ExportOptions { Width = 1200, Height = 800 };
        var menu = new ContextMenu();
        (string Text, Func<Task> Action)[] items =
        [
            ("Copy image", () => Run(chart => chart.CopyImageAsync(), "The image is on the clipboard")),
            ("Copy data", () => Run(chart => chart.CopyDataAsync(), "The CSV is on the clipboard")),
            ("Save as PNG", () => ExportTo("png", chart => chart.ExportPngAsync(options))),
            ("Save as SVG", () => ExportTo("svg", chart => chart.ExportSvgAsync(options))),
            ("Save as PDF", () => ExportTo("pdf", chart => chart.ExportPdfAsync(options))),
            ("Save as CSV", () => ExportTo("csv", ExportCsv)),
        ];
        foreach (var (text, action) in items)
        {
            var item = new MenuItem { Header = SampleText.T(text) };
            item.Click += async (_, _) => await action();
            _menuItems.Add((item, text));
            menu.Items.Add(item);
        }

        return menu;
    }

    private void ApplyTheme()
    {
        if (_isSwitching)
        {
            return;
        }

        var theme = SelectedTheme;
        _live.ChartTheme = theme ?? BuiltInThemes.TachyonDark;
        foreach (var chart in _raster.Children.OfType<Chart>())
        {
            chart.ChartTheme = theme ?? BuiltInThemes.TachyonDark;
        }

        foreach (var chart in _pages.Values.Concat(_companions.Values).Append(_scope).Append(_spectrum).Append(_spectrumHistory))
        {
            chart.ChartTheme = theme ?? BuiltInThemes.Instrument;
        }

        if (_selected is not null)
        {
            ShowSelection();
        }
    }

    private Chart ExportedChart
    {
        get
        {
            if (FindPage() is { } page)
            {
                return page;
            }

            if (_spectrumPanel.IsVisible)
            {
                return _spectrum;
            }

            if (_scope.IsVisible)
            {
                return _scope;
            }

            return _raster.IsVisible ? (Chart)_raster.Children[0] : _live;
        }
    }

    // The code of both ways lives in Examples/ThreeD/VideoRecordingPage.cs, which the source window shows.
    private async Task ExportVideo()
    {
        try
        {
            _hint.Text = SampleText.T("Recording the video …");
            var path = await VideoRecordingPage.RecordTurnAsync((file, options, step) => ChartAreaRecorder.ExportVideoAsync(_content, file, options, step), SelectedId is { } id && _hosts3D.TryGetValue(id, out var host) ? host.Scene.Camera : null);
            _hint.Text = SampleText.T("Saved: ") + path;
            await SavedDialog.Show(this, path);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _hint.Text = SampleText.T("Export failed: ") + exception.Message;
        }
    }

    // --record: two seconds to settle, six seconds of recording, then the dialog.
    private async Task RecordAtStart()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        await ToggleRecording();
        await Task.Delay(TimeSpan.FromSeconds(6));
        await ToggleRecording();
    }

    // Like a video recorder: the first press starts, the second stops. The chart can be operated freely in between.
    private async Task ToggleRecording()
    {
        try
        {
            if (_recording is null)
            {
                // The whole stage: a spectrum with its history, a 3D chart with the 2D chart below it, all rasters.
                _recording = VideoRecordingPage.StartRecording(file => ChartAreaRecorder.Start(_content, file), out var path);
                _recordingPath = path;
                _recordingClock.Start();
                ShowRecording();
                return;
            }

            var recording = _recording;
            _recording = null;
            _recordingClock.Stop();
            ShowRecording();
            await VideoRecordingPage.StopRecordingAsync(recording);
            _hint.Text = SampleText.T("Saved: ") + _recordingPath;
            await SavedDialog.Show(this, _recordingPath!);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _recording = null;
            _recordingClock.Stop();
            ShowRecording();
            _hint.Text = SampleText.T("Export failed: ") + exception.Message;
        }
    }

    // A red circle while ready, a square with the time while recording.
    private void ShowRecording() => _record.Content = _recording is { } recording
        ? $"■ {SampleText.T("Recording ")}{recording.Elapsed:mm\\:ss}"
        : "● " + SampleText.T("Record");


    private async Task ExportTo(string extension, Func<Chart, Task<byte[]>> export)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Tachyon Export");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, $"chart-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");
            await File.WriteAllBytesAsync(path, await export(ExportedChart));
            _hint.Text = SampleText.T("Saved: ") + path;
            await SavedDialog.Show(this, path);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _hint.Text = SampleText.T("Export failed: ") + exception.Message;
        }
    }

    private async Task Run(Func<Chart, Task> action, string done)
    {
        try
        {
            await action(ExportedChart);
            _hint.Text = SampleText.T(done);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.Runtime.InteropServices.ExternalException)
        {
            _hint.Text = SampleText.T("Failed: ") + exception.Message;
        }
    }

    private Button CreateAction(string text, Func<Task> action)
    {
        var button = new Button { Content = SampleText.T(text), Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 6), CornerRadius = new CornerRadius(7), Foreground = new SolidColorBrush(_ink) };
        _actions.Add((button, text));
        button.Click += async (_, _) => await action();
        return button;
    }

    private void AddExportButtons(Panel bar)
    {
        var options = new ExportOptions { Width = 1200, Height = 800 };
        bar.Children.Add(CreateAction("PNG 300 dpi", () => ExportTo("png", chart => chart.ExportPngAsync(options with { Dpi = 300, Theme = BuiltInThemes.Paper }))));
        bar.Children.Add(CreateAction("SVG", () => ExportTo("svg", chart => chart.ExportSvgAsync(options))));
        bar.Children.Add(CreateAction("PDF", () => ExportTo("pdf", chart => chart.ExportPdfAsync(options))));
        bar.Children.Add(CreateAction("CSV", () => ExportTo("csv", ExportCsv)));
        bar.Children.Add(CreateAction("Video", ExportVideo));
        _record.Click += async (_, _) => await ToggleRecording();
        _recordingClock.Tick += (_, _) => ShowRecording();
        ShowRecording();
        bar.Children.Add(_record);
        bar.Children.Add(CreateAction("Copy image", () => Run(chart => chart.CopyImageAsync(), "The image is on the clipboard")));
        bar.Children.Add(CreateAction("Copy data", () => Run(chart => chart.CopyDataAsync(), "The CSV is on the clipboard")));
    }

    // A hidden 3D page would keep its points on the graphics card, and a few large ones fill it up.
    private void Release3DPages()
    {
        var isReleased = false;
        foreach (var name in _hosts3D.Keys.Where(name => name != SelectedId).ToArray())
        {
            Pages3D.Find(name)?.Run?.Invoke(false);
            _content.Children.Remove(_views3D[name]);
            _views3D.Remove(name);
            _hosts3D.Remove(name);
            _companions.Remove(name);
            _pages.Remove(name);
            isReleased = true;
        }

        // Arrays of this size wait long for a collection, and the next large page needs the memory at once.
        if (isReleased)
        {
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => GC.Collect(), global::Avalonia.Threading.DispatcherPriority.Background); // NOSONAR: arrays of several GB wait long for a collection, see the comment above
        }
    }

    private Chart Find3D(Page3D page)
    {
        if (!_hosts3D.TryGetValue(page.Name, out var host))
        {
            host = new Tachyon.Charts.ThreeD.Avalonia.Chart3D().Configure(page.Configure);
            host.Surface.ContextMenu = CreateMenu();
            if (SelectedTheme is { } theme)
            {
                host.ChartTheme = theme;
            }

            _hosts3D[page.Name] = host;
            var view = (Control)host;
            if (page.Companion is { } companion)
            {
                // The 2D chart sits below in the same group, so ranges and cursor stay in step.
                var grid = new Grid { RowDefinitions = new RowDefinitions("3*,2*") };
                var below = new Chart().Configure(companion);
                if (SelectedTheme is { } chosen)
                {
                    below.ChartTheme = chosen;
                }

                _companions[page.Name] = below;
                Grid.SetRow(below, 1);
                grid.Children.Add(host);
                grid.Children.Add(below);
                view = grid;
            }

            _pages[page.Name] = host.Surface;
            _views3D[page.Name] = view;
            _content.Children.Add(view);
            if (TurnsView)
            {
                // --turn measures a window without anybody at the mouse, see TACHYON_FRAME_LOG.
                // From a thread of its own, faster than frames come: a UI timer would set the pace itself.
                var camera = host.Scene.Camera;
                camera.Zoom(ZoomsIn ? 0.25 : 1);
                _turning?.Dispose();
                _turning = new System.Threading.Timer(_ => camera.Orbit(0.1, 0), null, 0, 2);
            }
        }

        return host.Surface;
    }

    private Chart? FindPage()
    {
        if (SelectedId is { } id && Pages3D.Find(id) is { } page3D)
        {
            return Find3D(page3D);
        }

        if (SelectedId is not { } name || !InstrumentPages.TryGet(name, out var page))
        {
            return null;
        }

        // Built on the first visit: there are many pages.
        if (!_pages.TryGetValue(name, out var chart))
        {
            chart = new Chart().Configure(page.Configure);
            chart.ChartTheme = SelectedTheme ?? BuiltInThemes.Instrument;
            chart.ContextMenu = CreateMenu();
            _pages[name] = chart;
            _content.Children.Add(chart);
        }

        return chart;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _closing = true;
        _producer.Join();
    }

    // One thread at a fixed rate for both live signals: a timer of the UI thread comes late and unevenly.
    private void Produce()
    {
        var row = new float[SpectrogramColumns];
        var clock = Stopwatch.StartNew();
        var rows = 0L;
        while (!_closing)
        {
            var time = clock.Elapsed.TotalSeconds;
            for (; _showsRaster && rows < time * SpectrogramRowsPerSecond; rows++)
            {
                SpectrogramSignal.AppendSignal(_spectrogram, _spectrogram.TotalRows, row, withPauses: false);
            }

            if (!_showsRaster)
            {
                rows = (long)(time * SpectrogramRowsPerSecond);
            }

            Thread.Sleep(1);
        }
    }

    // Built on the first visit: the grids and clouds take a moment and a lot of memory.
    private void ConfigureRaster()
    {
        if (_raster.Children.Count > 0)
        {
            return;
        }

        _raster.Children.Add(new Chart().Configure(RastersPage.ConfigureHeatmap));
        _raster.Children.Add(new Chart().Configure(c => RastersPage.ConfigureSpectrogram(c, _spectrogram)));
        _raster.Children.Add(new Chart().Configure(RastersPage.ConfigureContour));
        _raster.Children.Add(new Chart().Configure(RastersPage.ConfigureDensity));
        foreach (var chart in _raster.Children.OfType<Chart>())
        {
            chart.ChartTheme = SelectedTheme ?? BuiltInThemes.TachyonDark;
        }
    }

    private void ApplyColormap()
    {
        var map = Colormaps.All[_colormap.SelectedIndex];
        foreach (var chart in _raster.Children.OfType<Chart>())
        {
            foreach (var series in chart.Series)
            {
                switch (series)
                {
                    case RasterSeriesElement raster:
                        raster.Colormap = map;
                        break;
                    case ScatterSeries scatter:
                        scatter.DensityColormap = map;
                        break;
                }
            }
        }
    }

    // F12 holds for every page and stays on when the page changes, also with the focus outside of a chart.
    private void OnPreviewKey(object? sender, global::Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == global::Avalonia.Input.Key.F11)
        {
            ShowSource();
            e.Handled = true;
        }

        if (e.Key == global::Avalonia.Input.Key.F12)
        {
            _showsDiagnostics = !_showsDiagnostics;
            ApplyDiagnostics();
            e.Handled = true;
        }
    }

    // The code of the page in a window of its own: it holds no chart, so the page keeps its frame rate.
    private void ShowSource()
    {
        if (SelectedId is not { } id || SampleSources.Find(id) is not { Count: > 0 } files)
        {
            return;
        }

        new SourceWindow(_pageTitle.Text ?? id, files).Show(this);
    }

    // Every chart of the window takes the state, also those on hidden pages and those built later.
    private void ApplyDiagnostics()
    {
        foreach (var chart in global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(this).OfType<Chart>())
        {
            chart.ShowsDiagnostics = _showsDiagnostics;
        }

        _stillInfo.IsVisible = _showsDiagnostics;
    }

    private void ShowSelection()
    {
        // A page that goes away ends its recording, and the file is completed.
        if (_recording is not null)
        {
            _ = ToggleRecording();
        }

        var isLive = SelectedId is LiveEntry;
        var isRaster = SelectedId is RasterEntry;
        var isScope = SelectedId is ScopeEntry;
        var isSpectrum = SelectedId is SpectrumEntry;
        var extra = FindPage();
        foreach (var page in _pages.Values)
        {
            page.IsVisible = page == extra;
        }

        Release3DPages();
        foreach (var name3D in _hosts3D.Keys)
        {
            _views3D[name3D].IsVisible = name3D == SelectedId;
            Pages3D.Find(name3D)?.Run?.Invoke(name3D == SelectedId);
        }

        _signal.IsRunning = isLive;

        if (isSpectrum)
        {
            _spectrumDemo.Start();
        }
        else
        {
            _spectrumDemo.Stop();
        }

        if (isScope)
        {
            _scopeDemo.Start();
        }
        else
        {
            _scopeDemo.Stop();
        }

        if (isRaster)
        {
            ConfigureRaster();
        }

        _showsRaster = isRaster;
        _live.IsVisible = isLive;
        _raster.IsVisible = isRaster;
        _scope.IsVisible = isScope;
        _spectrumPanel.IsVisible = isSpectrum;
        _colormap.IsVisible = isRaster;
        _select.IsVisible = isLive;
        _sceneView.IsVisible = !isLive && !isRaster && !isScope && !isSpectrum && extra is null;
        _scopeControls.IsVisible = isScope;
        _pageTitle.Text = _selected?.ToString() ?? string.Empty;
        _hint.Text = SampleText.T(true switch
        {
            _ when extra is not null => Hint(SelectedId!),
            _ when isSpectrum => "Wheel: frequency | Left: dB/div | Ctrl+wheel: reference | Drag: pan | Click: marker | Ctrl+click: add or remove a marker | Double click: peak",
            _ when isScope => "Drag at the left: channel | at the right: trigger | else: time | Wheel: time/div | Left, on V/div or Ctrl+wheel: volts/div",
            _ when isLive => "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Shift+drag: select points | Alt+drag: measure | Double click: all | Right click: menu",
            _ when isRaster => "Wheel: zoom | Drag: pan | Wheel over the color bar: color range | List: colormap",
            _ => "Still image of the scene. The pages above it are interactive.",
        });

        if (SelectedId is { } name && SceneCatalog.Find(name) is { } scene)
        {
            RenderScene(scene);
        }

        ApplyDiagnostics();
    }

    private unsafe void RenderScene(IScene scene)
    {
        var clock = Stopwatch.StartNew();
        using var device = new SkiaRenderDevice();
        using var target = device.CreateOffscreenTarget(scene.Size, 1);
        ChartSurface.ThemeOverride = SelectedTheme;
        try
        {
            scene.Render(device, target);
        }
        finally
        {
            ChartSurface.ThemeOverride = null;
        }

        var pixels = new Rgba8[scene.Size.PixelCount];
        target.ReadPixels(pixels);
        SkiaTextEngine.Default.ReleaseAtlas(device);

        var bitmap = new WriteableBitmap(new AvaloniaPixelSize(scene.Size.Width, scene.Size.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        using (var locked = bitmap.Lock())
        {
            fixed (Rgba8* source = pixels)
            {
                for (var row = 0; row < scene.Size.Height; row++)
                {
                    Buffer.MemoryCopy(source + (row * (long)scene.Size.Width), (byte*)locked.Address + (row * (long)locked.RowBytes), locked.RowBytes, scene.Size.Width * 4L);
                }
            }
        }

        _image.Source = bitmap;
        _stillInfo.Text = ShowroomDiagnostics.DescribeStill(clock.Elapsed.TotalMilliseconds, scene.Size, device.Capabilities.BackendName);
    }
}

internal sealed class SampleApplication : Application
{
    public static string? StartPage { get; set; }

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SampleWindow(StartPage);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        SampleApplication.StartPage = args.FirstOrDefault();
        SampleWindow.TurnsView = args.Contains("--turn");
        SampleWindow.ZoomsIn = args.Contains("--near");
        SampleWindow.ShowsDiagnosticsAtStart = args.Contains("--diagnostics");
        SampleWindow.ShowsSourceAtStart = args.Contains("--source");
        SampleWindow.RecordsAtStart = args.Contains("--record");
        var theme = Array.IndexOf(args, "--theme");
        SampleWindow.ThemeAtStart = theme >= 0 && theme + 1 < args.Length ? args[theme + 1] : null;
        return AppBuilder.Configure<SampleApplication>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }
}
