using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
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
using Tachyon.Charts.Samples.Shared.Spikes;
using Tachyon.Charts.Wpf;

namespace Tachyon.Charts.Samples.Wpf;

/// <summary>
/// The view model of the sample: a live signal and the bound range of the Y axis.
/// </summary>
internal sealed class SampleModel : INotifyPropertyChanged, IDisposable
{
    private DoubleRange _yRange = new(-2, 2);

    public event PropertyChangedEventHandler? PropertyChanged;

    public LiveSignal Signal { get; } = new();

    public DoubleRange YRange
    {
        get => _yRange;
        set
        {
            _yRange = value;
            Notify();
        }
    }

    /// <summary>Gets or sets a value indicating whether data flow. A hidden page needs none, and each new point costs a picture.</summary>
    public bool IsRunning
    {
        get => Signal.IsRunning;
        set => Signal.IsRunning = value;
    }

    public void Dispose() => Signal.Dispose();

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}

/// <summary>
/// The showroom: a live chart with all interactions, and all scenes of the catalog.
/// </summary>
internal sealed class SampleWindow : Window
{
    private const string LiveEntry = "Live-Chart (interaktiv)";
    private const string GroupEntry = "Gruppe (3 Charts)";
    private const string GroupName = "showroom";
    private const string RasterEntry = "Raster (interaktiv)";
    private const string ScopeEntry = "Oszilloskop (interaktiv)";
    private const string SpectrumEntry = "Spektrum (interaktiv)";
    private const int RasterRows = RastersPage.SpectrogramRows;
    private const int SpectrogramRowsPerSecond = 60;

    private readonly SampleModel _model = new();
    private volatile bool _showsRaster;

    private System.Threading.Timer? _turning;

    public static bool TurnsView { get; set; }

    public static bool ZoomsIn { get; set; }

    /// <summary>Gets or sets a value indicating whether the diagnostics are on from the start, as after F12.</summary>
    public static bool ShowsDiagnosticsAtStart { get; set; }

    /// <summary>Gets or sets a value indicating whether the code of the start page opens at once, as after F11.</summary>
    public static bool ShowsSourceAtStart { get; set; }

    /// <summary>Gets or sets a value indicating whether a recording of six seconds runs at the start, to measure it without a mouse.</summary>
    public static bool RecordsAtStart { get; set; }

    /// <summary>Gets or sets the theme chosen at the start, by its name, as if picked in the list.</summary>
    public static string? ThemeAtStart { get; set; }
    private readonly Chart _chart = new();
    private readonly Chart _overview = new() { Height = 110 };
    private readonly DockPanel _livePanel = new();
    private readonly UniformGrid _groupPanel = new() { Columns = 1, Visibility = Visibility.Collapsed };
    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer _sceneView = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ListBox _list = new() { Margin = new Thickness(10, 0, 6, 10) };
    private readonly ComboBox _theme = new() { Width = 170 };
    private readonly ComboBox _colormap = new() { Width = 130, Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _pageTitle = new() { FontSize = 24, FontWeight = FontWeights.SemiBold };
    private readonly ComboBox _trigger = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _persistence = new() { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
    private readonly ComboBox _language = new() { Width = 120, Margin = new Thickness(8, 0, 0, 0) };
    private readonly StackPanel _liveControls = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel _scopeControls = new() { Orientation = Orientation.Horizontal };
    private readonly List<(Button Button, string Text)> _actions = [];
    private readonly List<(MenuItem Item, string Text)> _menuItems = [];
    private readonly Dictionary<string, FrameworkElement> _views3D = [];
    private readonly Dictionary<string, Tachyon.Charts.ThreeD.Wpf.Chart3D> _hosts3D = [];
    private readonly Dictionary<string, List<Chart>> _companions = [];
    private readonly Tachyon.Charts.Core.Input.SelectionModifier _selection = new() { Mode = Tachyon.Charts.Core.Input.SelectionMode.Lasso };
    private readonly Button _select = new() { Margin = new Thickness(0, 0, 6, 0) };
    private bool _isSelecting;
    private bool _showsDiagnostics = ShowsDiagnosticsAtStart;
    private readonly Button _record = new() { Margin = new Thickness(0, 0, 6, 0), Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)) };
    private readonly System.Windows.Threading.DispatcherTimer _recordingClock = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
    private IChartRecording? _recording;
    private string? _recordingPath;
    private readonly TextBlock _stillInfo = new() { FontSize = 12, Margin = new Thickness(8), Padding = new Thickness(6, 3, 6, 3), Background = new SolidColorBrush(Color.FromArgb(200, 20, 24, 32)), Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
    private readonly Grid _stage;
    private readonly IReadOnlyList<SamplePage> _pageList;
    private bool _isSwitching;
    private readonly UniformGrid _rasterPanel = new() { Columns = 2, Visibility = Visibility.Collapsed };
    private readonly ScopeDemo _scopeDemo = new();
    private readonly Chart _scope = new() { Visibility = Visibility.Collapsed };
    private readonly SpectrumDemo _spectrumDemo = new();
    private readonly Dictionary<string, Chart> _pages = [];
    private readonly Chart _spectrum = new();
    private readonly Chart _spectrumHistory = new();
    private readonly UniformGrid _spectrumPanel = new() { Columns = 1, Visibility = Visibility.Collapsed };
    private readonly SpectrogramData _spectrogram = new(1024, RasterRows);
    private readonly float[] _spectrogramRow = new float[1024];
    private Thread? _spectrogramProducer;
    private volatile bool _spectrogramRuns;
    private readonly TextBlock _hint = new() { FontSize = 12.5, Opacity = 0.65, Margin = new Thickness(0, 3, 12, 0), TextTrimming = TextTrimming.CharacterEllipsis };

    public SampleWindow(string? startPage)
    {
        Title = "Tachyon Charts – Showroom";
        Width = 1280;
        Height = 800;
        DataContext = _model;
        _chart.ThemeTransition = TimeSpan.FromMilliseconds(400);

        ConfigureChart();
        ConfigureOverview();
        ConfigureGroup();

        _sceneView.Content = new Grid { Children = { _image, _stillInfo } };
        _sceneView.Visibility = Visibility.Collapsed;

        _pageList = SampleText.CreatePages(hasLinkedCharts: true);
        _list.ItemsSource = _pageList;

        FillThemes();
        var reset = CreateAction("Reset Y range", () =>
        {
            _model.YRange = new DoubleRange(-1.5, 1.5);
            return Task.CompletedTask;
        });
        _liveControls.Children.Add(reset);
        _select.Content = SampleText.T("Select points: off");
        _select.Click += (_, _) => ToggleSelect();
        _selection.SelectionChanged += ShowSelected;
        _liveControls.Children.Add(_select);

        _colormap.ItemsSource = Colormaps.All.Select(map => map.Name).ToArray();
        _colormap.SelectedIndex = 0;
        _colormap.SelectionChanged += (_, _) => ApplyColormap();

        _trigger.SelectionChanged += (_, _) =>
        {
            if (_trigger.SelectedIndex >= 0)
            {
                _scopeDemo.Trigger.Mode = (TriggerMode)_trigger.SelectedIndex;
                _scopeDemo.Trigger.Arm();
            }
        };
        _persistence.SelectionChanged += (_, _) =>
        {
            if (_persistence.SelectedIndex >= 0)
            {
                _scopeDemo.Display.Persistence = (ScopePersistence)_persistence.SelectedIndex;
            }
        };
        _scopeControls.Children.Add(_trigger);
        _scopeControls.Children.Add(_persistence);
        _language.ItemsSource = SampleText.Languages;
        _language.SelectedIndex = 0;
        _language.SelectionChanged += (_, _) =>
        {
            ChartText.Language = (ChartLanguage)_language.SelectedIndex;
            ApplyLanguage();
        };

        var bar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        bar.Children.Add(_liveControls);
        bar.Children.Add(_colormap);
        bar.Children.Add(_scopeControls);
        bar.Children.Add(CreateAction("Source code (F11)", () =>
        {
            ShowSource();
            return Task.CompletedTask;
        }));
        AddExportButtons(bar);

        var content = new Grid();
        _stage = content;
        DockPanel.SetDock(_overview, Dock.Bottom);
        _livePanel.Children.Add(_overview);
        _livePanel.Children.Add(_chart);
        content.Children.Add(_livePanel);
        content.Children.Add(_groupPanel);
        content.Children.Add(_rasterPanel);
        _scope.Configure(c => OscilloscopePage.Configure(c, _scopeDemo));
        content.Children.Add(_scope);
        _spectrum.Configure(c => SpectrumAnalyzerPage.Configure(c, _spectrumDemo));
        _spectrumHistory.Configure(c => SpectrumAnalyzerPage.ConfigureHistory(c, _spectrumDemo));
        _spectrumPanel.Children.Add(_spectrum);
        _spectrumPanel.Children.Add(_spectrumHistory);
        content.Children.Add(_spectrumPanel);

        content.Children.Add(_sceneView);

        Content = BuildShell(bar, content);
        foreach (var chart in _groupPanel.Children.Cast<Chart>().Append(_chart).Append(_scope).Append(_spectrum).Append(_spectrumHistory))
        {
            chart.ContextMenu = CreateMenu();
        }

        SourceInitialized += (_, _) => SampleStyle.DarkenTitleBar(this);
        ApplyLanguage();

        _list.SelectionChanged += (_, _) => ShowSelection();
        _theme.SelectionChanged += (_, _) => ApplyTheme();
        PreviewKeyDown += OnPreviewKey;
        Loaded += (_, _) =>
        {
            _list.SelectedItem = _pageList.FirstOrDefault(page => page.Id == startPage && !page.IsHeader) ?? _pageList[1];
            if (ShowsSourceAtStart)
            {
                ShowSource();
            }

            if (RecordsAtStart)
            {
                _ = RecordAtStart();
            }

            if (ThemeAtStart is { } themeName && BuiltInThemes.All.ToList().FindIndex(item => item.Name.Equals(themeName, StringComparison.OrdinalIgnoreCase)) is >= 0 and var chosen)
            {
                _theme.SelectedIndex = chosen + 1;
            }
        };
    }

    private static string Hint(string name) => InstrumentPages.TryGet(name, out var page) ? page.Hint : Pages3D.Find(name)?.Hint ?? string.Empty;

    private string? SelectedId => (_list.SelectedItem as SamplePage)?.Id;

    private FrameworkElement BuildShell(FrameworkElement bar, FrameworkElement content)
    {
        var logo = new StackPanel { Margin = new Thickness(20, 22, 20, 18) };
        logo.Children.Add(new TextBlock { Text = "TACHYON", FontSize = 22, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Accent") });
        logo.Children.Add(new TextBlock { Text = "CHARTS", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Muted") });
        var sidebar = new DockPanel { Background = (Brush)FindResource("Sidebar") };
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

        var toolbar = new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 12, 0, 0),
            Child = bar,
        };

        // The shadow lies next to the charts: an effect above their native windows would hide them.
        var card = new Grid();
        card.Children.Add(new Border
        {
            Background = (Brush)FindResource("Panel"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black },
        });
        content.Margin = new Thickness(10);
        card.Children.Add(content);

        var main = new DockPanel { Margin = new Thickness(20, 16, 20, 16) };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Bottom);
        main.Children.Add(header);
        main.Children.Add(toolbar);
        main.Children.Add(card);

        var root = new Grid { Background = (Brush)FindResource("Shell") };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        root.ColumnDefinitions.Add(new ColumnDefinition());
        TextElement.SetForeground(root, (Brush)FindResource("Ink"));
        Grid.SetColumn(main, 1);
        root.Children.Add(sidebar);
        root.Children.Add(main);
        return root;
    }

    private void FillThemes()
    {
        var index = Math.Max(_theme.SelectedIndex, 0);
        _theme.ItemsSource = new[] { SampleText.T("Scene default") }.Concat(BuiltInThemes.All.Select(theme => theme.Name)).ToArray();
        _theme.SelectedIndex = index;
    }

    private static void Fill(ComboBox box, params string[] texts)
    {
        var index = Math.Max(box.SelectedIndex, 0);
        box.ItemsSource = texts.Select(SampleText.T).ToArray();
        box.SelectedIndex = index;
    }

    private void ApplyLanguage()
    {
        _isSwitching = true;
        FillThemes();
        Fill(_trigger, "Trigger Auto", "Trigger Normal", "Trigger Single");
        Fill(_persistence, "Persistence off", "Variable", "Infinite", "Color grade");
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
        _chart.Title = SampleText.T("Live: 50,000 points per second");
        if (_chart.XAxes.Count > 0)
        {
            _chart.XAxes[0].Title = SampleText.T("Time in s");
            _chart.YAxes[0].Title = SampleText.T("Value");
        }

        string[] rasters = ["Heatmap 4096 × 4096", "Spectrogram, 60 rows per second", "Contour, filled", "5 million points: zoom in for markers"];
        for (var i = 0; i < _rasterPanel.Children.Count; i++)
        {
            ((Chart)_rasterPanel.Children[i]).Title = SampleText.T(rasters[i]);
        }

        System.Windows.Data.CollectionViewSource.GetDefaultView(_list.ItemsSource).Refresh();
        if (SelectedId is not null)
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

            if (_spectrumPanel.Visibility == Visibility.Visible)
            {
                return _spectrum;
            }

            if (_scope.Visibility == Visibility.Visible)
            {
                return _scope;
            }

            if (_rasterPanel.Visibility == Visibility.Visible)
            {
                return (Chart)_rasterPanel.Children[0];
            }

            return _chart;
        }
    }

    // The code of both ways lives in Examples/ThreeD/VideoRecordingPage.cs, which the source window shows.
    private async Task ExportVideo()
    {
        try
        {
            _hint.Text = SampleText.T("Recording the video …");
            var path = await VideoRecordingPage.RecordTurnAsync((file, options, step) => ChartAreaRecorder.ExportVideoAsync(_stage, file, options, step), SelectedId is { } id && _hosts3D.TryGetValue(id, out var host) ? host.Scene.Camera : null);
            _hint.Text = SampleText.T("Saved: ") + path;
            SavedDialog.Show(this, path);
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
                _recording = VideoRecordingPage.StartRecording(file => ChartAreaRecorder.Start(_stage, file), out var path);
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
            SavedDialog.Show(this, _recordingPath!);
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
            SavedDialog.Show(this, path);
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
        var button = new Button { Content = SampleText.T(text), Margin = new Thickness(0, 0, 6, 0) };
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
        bar.Children.Add(CreateAction("Print", () => Run(chart => chart.PrintAsync("Tachyon Charts"), "Printed or canceled")));
        bar.Children.Add(CreateAction("Download data", DownloadData));
    }

    // A hidden 3D page would keep its points on the graphics card, and a few large ones fill it up.
    private void Release3DPages()
    {
        var isReleased = false;
        foreach (var name in _hosts3D.Keys.Where(name => name != SelectedId).ToArray())
        {
            Release3D(name);
            isReleased = true;
        }

        // Arrays of this size wait long for a collection, and the next large page needs the memory at once.
        if (isReleased)
        {
            Dispatcher.BeginInvoke(() => GC.Collect(), System.Windows.Threading.DispatcherPriority.ApplicationIdle); // NOSONAR: arrays of several GB wait long for a collection, see the comment above
        }
    }

    private void Release3D(string name)
    {
        Pages3D.Find(name)?.Run?.Invoke(false);
        _stage.Children.Remove(_views3D[name]);
        _views3D.Remove(name);
        _hosts3D.Remove(name);
        _companions.Remove(name);
        _pages.Remove(name);
    }

    // Real data come only on request; afterwards the page is built anew with them.
    private async Task DownloadData()
    {
        if (SelectedId is not { } id || Pages3D.Find(id) is not { Download: { } download })
        {
            _hint.Text = SampleText.T("This page has no data to download.");
            return;
        }

        try
        {
            await download(new Progress<string>(line => _hint.Text = line));
            if (_hosts3D.ContainsKey(id))
            {
                Release3D(id);
            }

            ShowSelection();
            _hint.Text = SampleText.T("Downloaded. The page shows the real data.");
        }
        catch (Exception exception) when (exception is System.Net.Http.HttpRequestException or InvalidDataException or IOException or TaskCanceledException)
        {
            _hint.Text = SampleText.T("Failed: ") + exception.Message;
        }
    }

    private Chart Find3D(Page3D page)
    {
        if (!_hosts3D.TryGetValue(page.Name, out var host))
        {
            host = new Tachyon.Charts.ThreeD.Wpf.Chart3D().Configure(page.Configure);
            host.Surface.ContextMenu = CreateMenu();
            if (SelectedTheme is { } theme)
            {
                host.ChartTheme = theme;
            }

            _hosts3D[page.Name] = host;
            var view = (FrameworkElement)host;
            if (page.Companion is { } companion)
            {
                // The 2D chart sits below in the same group, so ranges and cursor stay in step.
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });
                var below = new Chart().Configure(companion);
                if (SelectedTheme is { } chosen)
                {
                    below.Theme = chosen;
                }

                _companions[page.Name] = [below];
                Grid.SetRow(below, 1);
                grid.Children.Add(host);
                grid.Children.Add(below);
                view = grid;
            }
            else if (page.GridCompanions is { Count: 3 } companions)
            {
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var charts = new List<Chart>(companions.Count);
                for (var index = 0; index < companions.Count; index++)
                {
                    var chart = new Chart().Configure(companions[index]);
                    if (SelectedTheme is { } chosen)
                    {
                        chart.Theme = chosen;
                    }

                    Grid.SetRow(chart, (index + 1) / 2);
                    Grid.SetColumn(chart, (index + 1) % 2);
                    charts.Add(chart);
                    grid.Children.Add(chart);
                }

                _companions[page.Name] = charts;
                grid.Children.Add(host);
                view = grid;
            }

            _pages[page.Name] = host.Surface;
            _views3D[page.Name] = view;
            _stage.Children.Add(view);
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

        // Built on the first visit: a chart holds a window and a swapchain, and there are many pages.
        if (!_pages.TryGetValue(name, out var chart))
        {
            chart = new Chart().Configure(page.Configure);
            chart.Theme = SelectedTheme ?? BuiltInThemes.Instrument;
            chart.ContextMenu = CreateMenu();
            _pages[name] = chart;
            _stage.Children.Add(chart);
        }

        return chart;
    }

    private Theme? SelectedTheme => _theme.SelectedIndex > 0 ? BuiltInThemes.All[_theme.SelectedIndex - 1] : null;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _spectrogramRuns = false;
        _model.Dispose();
    }

    // Built on the first visit: the grids and clouds take a moment and a lot of memory.
    private void ConfigureRaster()
    {
        if (_rasterPanel.Children.Count > 0)
        {
            return;
        }

        var heatmap = new Chart();
        heatmap.Configure(RastersPage.ConfigureHeatmap);

        var spectrogram = new Chart();
        spectrogram.Configure(c => RastersPage.ConfigureSpectrogram(c, _spectrogram));

        var contour = new Chart();
        contour.Configure(RastersPage.ConfigureContour);

        var density = new Chart();
        density.Configure(RastersPage.ConfigureDensity);

        _rasterPanel.Children.Add(heatmap);
        _rasterPanel.Children.Add(spectrogram);
        _rasterPanel.Children.Add(contour);
        _rasterPanel.Children.Add(density);
        foreach (var chart in _rasterPanel.Children.Cast<Chart>())
        {
            chart.Theme = SelectedTheme ?? BuiltInThemes.TachyonDark;
        }
    }

    // A thread of its own at a fixed rate: a timer of the UI thread comes late and unevenly.
    private void StartSpectrogram()
    {
        // A theme change comes here too. Waiting for a thread that keeps running would hang the window.
        if (_spectrogramRuns)
        {
            return;
        }

        _spectrogramProducer?.Join();
        _spectrogramRuns = true;
        _spectrogramProducer = new Thread(() =>
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var written = 0L;
            while (_spectrogramRuns)
            {
                var due = (long)(clock.Elapsed.TotalSeconds * SpectrogramRowsPerSecond);
                written = _showsRaster ? written : due;
                for (; written < due; written++)
                {
                    SpectrogramSignal.AppendSignal(_spectrogram, _spectrogram.TotalRows, _spectrogramRow, withPauses: false);
                }

                Thread.Sleep(1);
            }
        })
        { IsBackground = true, Name = "Spectrogram" };
        _spectrogramProducer.Start();
    }

    private void ApplyColormap()
    {
        var map = Colormaps.All[_colormap.SelectedIndex];
        foreach (Chart chart in _rasterPanel.Children)
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

    private void ConfigureChart()
    {
        _chart.Configure(c => LiveChartPage.Configure(c, _model.Signal, _selection));

        BindingOperations.SetBinding(_chart.YAxes[0], ChartAxis.VisibleRangeProperty, new Binding(nameof(SampleModel.YRange)) { Mode = BindingMode.TwoWay });
    }

    private void ConfigureOverview()
    {
        _overview.Configure(c => c
            .Theme(BuiltInThemes.TachyonDark)
            .XAxis(a => a.Numeric("x").AutoRange())
            .YAxis(a => a.Numeric("y").AutoRange())
            .AddLine(_model.Signal.Fixed, s => s.Stroke(new Rgba8(80, 170, 240, 255)).Thickness(1))
            .AddLine(_model.Signal.Live, s => s.Stroke(new Rgba8(240, 130, 90, 255)).Thickness(1)));
        _overview.Modifiers.Add(new OverviewModifier { Target = _chart });
    }

    private void ConfigureGroup()
    {
        foreach (var (title, scale) in LinkedChartsPage.Charts)
        {
            var chart = new Chart();
            chart.Configure(c => LinkedChartsPage.Configure(c, title, scale, GroupName));
            _groupPanel.Children.Add(chart);
        }
    }

    // With the keyboard focus outside of the chart, F12 arrives here.
    private void OnPreviewKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ShowSource();
            e.Handled = true;
        }

        // F12 holds for every page and stays on when the page changes.
        if (e.Key == Key.F12)
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

        new SourceWindow(_pageTitle.Text, files) { Owner = this }.Show();
    }

    // Every chart of the window takes the state, also those on hidden pages and those built later.
    private void ApplyDiagnostics()
    {
        Apply(this);
        _stillInfo.Visibility = Show(_showsDiagnostics);

        void Apply(DependencyObject parent)
        {
            if (parent is Chart chart)
            {
                chart.ShowsDiagnostics = _showsDiagnostics;
            }

            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is DependencyObject element)
                {
                    Apply(element);
                }
            }
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
        chart.ExportCsvAsync(chart == _chart && _selection.Selection.Count > 0 ? _selection.Selection : null);

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
        _chart.Theme = theme ?? BuiltInThemes.TachyonDark;
        _overview.Theme = theme ?? BuiltInThemes.TachyonDark;
        foreach (var chart in _groupPanel.Children.Cast<Chart>().Concat(_rasterPanel.Children.Cast<Chart>()))
        {
            chart.Theme = theme ?? BuiltInThemes.TachyonDark;
        }

        foreach (var chart in _pages.Values.Concat(_companions.Values.SelectMany(charts => charts)).Append(_scope).Append(_spectrum).Append(_spectrumHistory))
        {
            chart.Theme = theme ?? BuiltInThemes.Instrument;
        }

        ShowSelection();
    }

    private void ShowSelection()
    {
        // A page that goes away ends its recording, and the file is completed.
        if (_recording is not null)
        {
            _ = ToggleRecording();
        }

        var isLive = SelectedId is LiveEntry;
        var isGroup = SelectedId is GroupEntry;
        var isRaster = SelectedId is RasterEntry;
        var isScope = SelectedId is ScopeEntry;
        var isSpectrum = SelectedId is SpectrumEntry;
        var extra = FindPage();
        foreach (var page in _pages.Values)
        {
            page.Visibility = page == extra ? Visibility.Visible : Visibility.Collapsed;
        }

        Release3DPages();
        foreach (var name3D in _hosts3D.Keys)
        {
            _views3D[name3D].Visibility = Show(name3D == SelectedId);
            Pages3D.Find(name3D)?.Run?.Invoke(name3D == SelectedId);
        }

        _model.IsRunning = isLive || isGroup;
        _showsRaster = isRaster;
        var isScene = !isLive && !isGroup && !isRaster && !isScope && !isSpectrum && extra is null;

        RunDemos(isSpectrum, isScope, isRaster);
        _rasterPanel.Visibility = Show(isRaster);
        _colormap.Visibility = Show(isRaster);
        _livePanel.Visibility = isLive ? Visibility.Visible : Visibility.Hidden;
        _groupPanel.Visibility = Show(isGroup);
        _scope.Visibility = Show(isScope);
        _spectrumPanel.Visibility = Show(isSpectrum);
        _sceneView.Visibility = Show(isScene);
        _hint.Text = SampleText.T(extra is not null ? Hint(SelectedId!) : FixedHint(isSpectrum, isScope, isLive, isRaster, isGroup));
        _pageTitle.Text = _list.SelectedItem?.ToString() ?? string.Empty;
        _liveControls.Visibility = Show(isLive);
        _scopeControls.Visibility = Show(isScope);

        if (isScene && SelectedId is { } name && SceneCatalog.Find(name) is { } scene)
        {
            RenderScene(scene);
        }

        ApplyDiagnostics();
    }

    private static Visibility Show(bool isVisible) => isVisible ? Visibility.Visible : Visibility.Collapsed;

    private static string FixedHint(bool isSpectrum, bool isScope, bool isLive, bool isRaster, bool isGroup) => true switch
    {
        _ when isSpectrum => "Wheel: frequency | Left: dB/div | Ctrl+wheel: reference | Drag: pan | Click: marker | Ctrl+click: add or remove a marker | Double click: peak",
        _ when isScope => "Drag at the left: channel | at the right: trigger | else: time | Wheel: time/div | Left, on V/div or Ctrl+wheel: volts/div",
        _ when isLive => "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Shift+drag: select points | Alt+drag: measure | Double click: all | Right click: menu",
        _ when isRaster => "Wheel: zoom | Drag: pan | Wheel over the color bar: color range | List: colormap",
        _ when isGroup => "Zoom and crosshair apply to all three charts",
        _ => "Still image of the scene. The pages above it are interactive.",
    };

    private void RunDemos(bool isSpectrum, bool isScope, bool isRaster)
    {
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
            StartSpectrogram();
        }
        else
        {
            _spectrogramRuns = false;
        }
    }

    // Scenes with a chart take the theme, the others show their fixed colors.
    private void RenderScene(IScene scene)
    {
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var size = new PixelSize((int)(scene.Size.Width * scale), (int)(scene.Size.Height * scale));
            using var device = RendererCatalog.Select().Device;
            using var target = device.CreateOffscreenTarget(size, scale);
            RenderInto(scene, device, target);

            var pixels = new Rgba8[size.PixelCount];
            target.ReadPixels(pixels);
            var bytes = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4] = pixels[i].B;
                bytes[(i * 4) + 1] = pixels[i].G;
                bytes[(i * 4) + 2] = pixels[i].R;
                bytes[(i * 4) + 3] = pixels[i].A;
            }

            _image.Source = BitmapSource.Create(size.Width, size.Height, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, bytes, size.Width * 4);
            _stillInfo.Text = ShowroomDiagnostics.DescribeStill(clock.Elapsed.TotalMilliseconds, size, device.Capabilities.BackendName);
            SkiaTextEngine.Default.ReleaseAtlas(device);
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            _hint.Text = SampleText.T("Failed: ") + exception.Message;
        }
    }

    private void RenderInto(IScene scene, IRenderDevice device, IRenderTarget target)
    {
        if (scene is not LineScene lineScene)
        {
            ChartSurface.ThemeOverride = SelectedTheme;
            try
            {
                scene.Render(device, target);
            }
            finally
            {
                ChartSurface.ThemeOverride = null;
            }

            return;
        }

        var (chart, series) = lineScene.CreateChart();
        chart.Theme = SelectedTheme ?? BuiltInThemes.TachyonDark;
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var item in series)
        {
            item.Dispose();
        }
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var application = new Application();
        SampleStyle.Apply(application);
        SampleWindow.TurnsView = args.Contains("--turn");
        SampleWindow.ZoomsIn = args.Contains("--near");
        SampleWindow.ShowsDiagnosticsAtStart = args.Contains("--diagnostics");
        SampleWindow.ShowsSourceAtStart = args.Contains("--source");
        SampleWindow.RecordsAtStart = args.Contains("--record");
        var theme = Array.IndexOf(args, "--theme");
        SampleWindow.ThemeAtStart = theme >= 0 && theme + 1 < args.Length ? args[theme + 1] : null;
        application.Run(new SampleWindow(args.FirstOrDefault()));
    }
}
