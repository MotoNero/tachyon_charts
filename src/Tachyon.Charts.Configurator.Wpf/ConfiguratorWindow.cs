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
    // Data, checks and code are made away from the window, which only shows the result.
    private readonly ConfiguratorSession _session = new(ChartCatalog.CreateWithAddOns(), new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
    private readonly Chart _preview = new();

    // The charts of the preview: one, or one per linked view.
    private readonly System.Windows.Controls.Primitives.UniformGrid _previews = new() { Columns = 1 };
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
        // Settings, preview and code, with splitters between them to share the width as needed.
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380), MinWidth = 260 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 240 });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(560), MinWidth = 240 });
        foreach (var column in new[] { 1, 3 })
        {
            var splitter = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = (Brush)FindResource("Line"), ResizeBehavior = GridResizeBehavior.PreviousAndNext, Cursor = System.Windows.Input.Cursors.SizeWE };
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
        SourceInitialized += (_, _) => SampleStyle.DarkenTitleBar(this);
        _session.Changed += ShowState;
        _session.UpdateStarted += () => (_status.Text, _status.Foreground) = ("Updating …", (Brush)FindResource("Muted"));
        _session.FieldsChanged += Fill;
        Fill();
        ShowState();
    }

    /// <summary>Saves a picture of the window once it shows, then closes it.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Gets the identifier of the type to start with, or <see langword="null"/> for the first.</summary>
    public string? StartType { get; init; }

    /// <summary>Gets a saved description to start with, or <see langword="null"/>.</summary>
    public string? StartSpec { get; init; }

    private ComboBox? _types;

    private void SelectStartType()
    {
        var index = _session.Catalog.Types.ToList().FindIndex(type => type.Id == StartType);
        if (index >= 0 && _types is not null)
        {
            _types.SelectedIndex = index;
        }
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        SelectStartType();
        if (StartSpec is { } saved)
        {
            _session.Load(ChartSpecJson.Read(await File.ReadAllTextAsync(saved)));
        }

        if (ScreenshotPath is { } path)
        {
            // The picture waits for the work away from the window, then gives the charts time to draw: on the CPU a
            // chart in 3D holds the render thread long enough that the charts beside it draw a while later.
            for (var waited = 0; waited < 200 && _session.IsUpdating; waited++)
            {
                await Task.Delay(50);
            }

            await Task.Delay(3000);
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

    private TextBox CodeBox() => new()
    {
        IsReadOnly = true,
        IsReadOnlyCaretVisible = true,
        TextWrapping = TextWrapping.NoWrap,
        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
        FontSize = 12.5,
        Background = (Brush)FindResource("Panel"),
        Foreground = (Brush)FindResource("Ink"),
        SelectionBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x86, 0xFF)),
        BorderThickness = new Thickness(0),
        Padding = new Thickness(10),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 14, 0, 6) };

    private UIElement CreateSettings()
    {
        var types = _types = new ComboBox { Margin = new Thickness(0, 0, 0, 8), ItemsSource = _session.Catalog.Types.Select(type => type.Name).ToList(), SelectedIndex = 0 };
        types.SelectionChanged += (_, _) =>
        {
            if (!_isFilling && types.SelectedIndex >= 0)
            {
                _session.NewChart(_session.Catalog.Types[types.SelectedIndex].Id);
            }
        };
        // Start from a template, open a saved description or save this one.
        var templates = new ComboBox { ItemsSource = new[] { "Start from a template …" }.Concat(_session.Catalog.Templates.Select(template => template.Name)).ToList(), SelectedIndex = 0, MinWidth = 170, Margin = new Thickness(0, 0, 6, 0) };
        templates.SelectionChanged += (_, _) =>
        {
            if (templates.SelectedIndex > 0)
            {
                _session.UseTemplate(_session.Catalog.Templates[templates.SelectedIndex - 1].Id);
                templates.SelectedIndex = 0;
            }
        };
        var file = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        file.Children.Add(templates);
        file.Children.Add(Action("Open …", OpenDescription));
        file.Children.Add(Action("Save …", SaveDescription));
        var add = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ItemsSource = _session.Catalog.Types.Select(type => type.Name).ToList(), SelectedIndex = 0, MinWidth = 120 };
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        buttons.Children.Add(add);
        buttons.Children.Add(Action("+ Series", () => _session.AddSeries(_session.Catalog.Types[add.SelectedIndex].Id)));
        buttons.Children.Add(Action("− Series", _session.RemoveSeries));
        buttons.Children.Add(Action("+ Y axis", _session.AddYAxis));
        var kinds = new ComboBox { Margin = new Thickness(0, 0, 6, 0), ItemsSource = ConfiguratorSession.AnnotationKinds.Select(kind => kind.Name).ToList(), SelectedIndex = 0, MinWidth = 120 };
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
        top.Children.Add(new TextBlock { Text = "Configurator", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        top.Children.Add(file);
        top.Children.Add(Heading("Chart type"));
        top.Children.Add(types);
        top.Children.Add(buttons);
        top.Children.Add(notes);
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

    // A description saved before; medical files are never in it and are chosen again.
    private void OpenDescription()
    {
        var dialog = new OpenFileDialog { Filter = "Chart description|*.json|All files|*.*" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _session.Load(ChartSpecJson.Read(File.ReadAllText(dialog.FileName)));
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            (_status.Text, _status.Foreground) = (exception.Message, Brushes.OrangeRed);
        }
    }

    private void SaveDescription()
    {
        var dialog = new SaveFileDialog { FileName = _session.Options.ProjectName + ".chart.json", Filter = "Chart description|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            File.WriteAllText(dialog.FileName, ChartSpecJson.Write(_session.Spec));
            _status.Text = "Saved: " + dialog.FileName + (_session.Spec.Series.Exists(series => series.Data.Kind == DataKind.MedicalFile) ? ". Medical files are not saved; choose them again after opening." : string.Empty);
        }
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
        if (_types is not null)
        {
            // A loaded description or a template shows its type without starting a new chart.
            _types.SelectedIndex = _session.Catalog.Types.ToList().FindIndex(type => type.Id == _session.Spec.Series[0].Type);
        }

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

            _fields.Children.Add(Control(field));
        }
    }

    private FrameworkElement Control(FieldModel field)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), ToolTip = field.Info.Description.Length > 0 ? field.Info.Description : null };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 70 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star), MinWidth = 110 });
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
                    var option = new CheckBox { Content = new TextBlock { Text = item }, Tag = item, IsChecked = chosen.Contains(item), Margin = new Thickness(0, 0, 10, 2), Foreground = (Brush)FindResource("Ink") };
                    option.Click += (_, _) => Report(field.Set(string.Join(",", boxes.Children.OfType<CheckBox>().Where(box => box.IsChecked == true).Select(box => (string)box.Tag))));
                    boxes.Children.Add(option);
                }

                editor = boxes;
                break;
            case ParameterKind.File:
                var path = new TextBox { Text = field.Text, Background = (Brush)FindResource("Raised"), Foreground = (Brush)FindResource("Ink"), BorderBrush = (Brush)FindResource("Line"), Padding = new Thickness(4, 2, 4, 2) };
                path.TextChanged += (_, _) => Report(field.SetText(path.Text));
                var browse = new Button { Content = "…", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 0, 8, 0) };
                browse.Click += (_, _) =>
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Data files|*.csv;*.txt;*.tsv;*.h5;*.hdf5;*.xyz;*.ply;*.las;*.nii;*.nii.gz;*.raw;*.stl;*.obj|All files|*.*" };
                    if (dialog.ShowDialog(this) == true)
                    {
                        path.Text = dialog.FileName;
                    }
                };
                var file = new DockPanel();
                DockPanel.SetDock(browse, Dock.Right);
                file.Children.Add(browse);
                file.Children.Add(path);
                editor = file;
                break;
            default:
                var box = new TextBox { Text = field.Text, Background = (Brush)FindResource("Raised"), Foreground = (Brush)FindResource("Ink"), BorderBrush = (Brush)FindResource("Line"), Padding = new Thickness(4, 2, 4, 2) };
                box.TextChanged += (_, _) => Report(field.SetText(box.Text));
                editor = box;
                break;
        }

        // The label in a column of its own that wraps, a gap, then the editor, which grows and shrinks with the panel.
        var label = new TextBlock { Text = field.Info.Label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted") };
        Grid.SetColumn(editor, 2);
        row.Children.Add(label);
        row.Children.Add(editor);
        var stack = new StackPanel();
        stack.Children.Add(row);
        stack.Children.Add(problem);
        return stack;
    }

    // The menu the generated window gets with the export option, here on the preview.
    private ContextMenu ExportMenu()
    {
        var menu = new ContextMenu();
        AddExport(menu, "Save as PNG …", "PNG image|*.png", () => _preview.ExportPngAsync());
        AddExport(menu, "Save as SVG …", "SVG drawing|*.svg", () => _preview.ExportSvgAsync());
        AddExport(menu, "Save as PDF …", "PDF document|*.pdf", () => _preview.ExportPdfAsync());
        AddExport(menu, "Save data as CSV …", "CSV table|*.csv", () => _preview.ExportCsvAsync());
        var video = new MenuItem { Header = _preview.IsRecording ? "Stop recording" : "Record video …" };
        video.Click += async (_, _) =>
        {
            if (_preview.IsRecording)
            {
                await _preview.StopRecordingAsync();
                video.Header = "Record video …";
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "MP4 video|*.mp4" };
            if (dialog.ShowDialog(this) == true)
            {
                _preview.StartRecording(dialog.FileName);
                video.Header = "Stop recording";
            }
        };
        menu.Items.Add(video);
        return menu;
    }

    private void AddExport(ContextMenu menu, string header, string filter, Func<Task<byte[]>> export)
    {
        var item = new MenuItem { Header = header };
        item.Click += async (_, _) =>
        {
            var dialog = new Microsoft.Win32.SaveFileDialog { Filter = filter };
            if (dialog.ShowDialog(this) == true)
            {
                await File.WriteAllBytesAsync(dialog.FileName, await export());
            }
        };
        menu.Items.Add(item);
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
    private void ShowState()
    {
        var clock = Stopwatch.StartNew();
        if (_session.Problems.Count > 0)
        {
            _status.Text = string.Join(" ", _session.Problems);
            _status.Foreground = Brushes.OrangeRed;
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
            _status.Foreground = Brushes.OrangeRed;
            return;
        }
        // The boxes stay while the tabs keep their names, so selection and scrolling survive a change.
        var selected = _code.SelectedIndex;
        if (!_session.Code.Keys.SequenceEqual(_code.Items.Cast<TabItem>().Select(tab => (string)tab.Header)))
        {
            _code.Items.Clear();
            foreach (var name in _session.Code.Keys)
            {
                _code.Items.Add(new TabItem { Header = name, Content = CodeBox() });
            }
        }

        foreach (TabItem tab in _code.Items)
        {
            var box = (TextBox)tab.Content;
            if (box.Text != _session.Code[(string)tab.Header])
            {
                box.Text = _session.Code[(string)tab.Header];
            }
        }

        _code.SelectedIndex = Math.Max(0, selected);
        _status.Foreground = (Brush)FindResource("Muted");
        _status.Text = string.Create(CultureInfo.InvariantCulture, $"Data and code in {_session.LastUpdate.TotalMilliseconds:F0} ms, preview in {clock.Elapsed.TotalMilliseconds:F0} ms.");
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
        var type = Array.IndexOf(args, "--type");
        var spec = Array.IndexOf(args, "--spec");
        if (path is not null)
        {
            // Pictures draw on the CPU, so that the chart is part of the picture of the window.
            Chart.Renderer = Rendering.RendererPreference.Skia;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        SampleStyle.Apply(application);
        application.Run(new ConfiguratorWindow { ScreenshotPath = path, StartType = type >= 0 && type + 1 < args.Length ? args[type + 1] : null, StartSpec = spec >= 0 && spec + 1 < args.Length ? args[spec + 1] : null });
    }
}
