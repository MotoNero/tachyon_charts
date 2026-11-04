using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Export;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;

namespace Tachyon.Charts.Wpf;

/// <summary>
/// A GPU-accelerated chart.
/// </summary>
/// <remarks>
/// The chart renders into its own child window. WPF elements therefore cannot lie on top of it;
/// a <see cref="System.Windows.Controls.Primitives.Popup"/> can, because it is a window of its own.
/// Without a usable swapchain the chart falls back to copying its image into WPF, which removes this
/// limit at the price of speed.
/// </remarks>
[ContentProperty(nameof(Series))]
public sealed class Chart : FrameworkElement, IRecordableChart
{
    private const string NotShown = "The chart is not shown yet.";

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(Chart), new PropertyMetadata(null, OnTitleChanged));

    /// <summary>Identifies the <see cref="GroupName"/> property.</summary>
    public static readonly DependencyProperty GroupNameProperty = DependencyProperty.Register(
        nameof(GroupName), typeof(string), typeof(Chart), new PropertyMetadata(null, OnGroupNameChanged));

    /// <summary>Identifies the <see cref="Theme"/> property.</summary>
    public static readonly DependencyProperty ThemeProperty = DependencyProperty.Register(
        nameof(Theme), typeof(Theme), typeof(Chart), new PropertyMetadata(null, OnThemeChanged));

    private readonly List<object> _logicalChildren = [];
    private readonly DispatcherTimer _readbackTimer;
    private readonly Lock _readbackGate = new();
    private readonly DateTime _start = DateTime.UtcNow;

    private const double PrintDpi = 300;

    private ChartRuntime? _runtime;
    private IReadOnlyList<Func<Core.Pipeline.ChartSurface, Core.Overlays.ChartOverlay>> _overlayFactories = [];
    private bool _showsDiagnostics;
    private ChartController? _controller;
    private IChartRecording? _recording;
    private FrameworkElement? _presenter;
    private ChartWindowHost? _host;
    private ISurfaceRenderTarget? _surfaceTarget;
    private IOffscreenRenderTarget? _readbackTarget;
    private Image? _image;
    private WriteableBitmap? _bitmap;
    private Rgba8[] _pixels = [];
    private byte[] _bytes = [];
    private long _shownFrame = -1;
    private PixelSize _size = new(1, 1);
    private double _scale = 1;

    /// <summary>Initializes a new instance of the <see cref="Chart"/> class.</summary>
    public Chart()
    {
        Focusable = true;
        _readbackTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(16) };
        _readbackTimer.Tick += (_, _) => ShowReadback();
        Track(XAxes, OnAxesChanged);
        Track(YAxes, OnAxesChanged);
        Track(Series, () => _controller?.OnSeriesChanged());
        Track(Modifiers, () => _controller?.OnModifiersChanged());
        Track(Annotations, () => _controller?.OnAnnotationsChanged());
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    /// <summary>
    /// Gets or sets the renderer of charts that start from now on. All charts of a process share one,
    /// so a change only takes effect when no chart is running.
    /// </summary>
    public static RendererPreference Renderer { get; set; } =
        RendererSelector.ParsePreference(Environment.GetEnvironmentVariable("TACHYON_RENDERER"));

    /// <summary>Gets or sets the chart title.</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the name of a group of charts that share their X range, cursor and axis widths.
    /// </summary>
    public string? GroupName
    {
        get => (string?)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    /// <summary>Gets or sets the theme.</summary>
    public Theme? Theme
    {
        get => (Theme?)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    /// <summary>Gets or sets how long a change of the theme blends its colors.</summary>
    public TimeSpan ThemeTransition { get; set; }

    /// <summary>Gets the horizontal axes, at least one.</summary>
    public ChartElementCollection<ChartAxis> XAxes { get; } = [];

    /// <summary>Gets the vertical axes, at least one.</summary>
    public ChartElementCollection<ChartAxis> YAxes { get; } = [];

    /// <summary>Gets the series.</summary>
    public ChartElementCollection<ChartSeriesElement> Series { get; } = [];

    /// <summary>Gets the interactions.</summary>
    public ChartElementCollection<ChartModifierElement> Modifiers { get; } = [];

    /// <summary>Gets the annotations.</summary>
    public ChartElementCollection<ChartAnnotationElement> Annotations { get; } = [];

    /// <summary>Gets a value indicating whether the chart copies its image into WPF instead of using a swapchain.</summary>
    public bool IsReadback => _readbackTarget is not null;

    /// <summary>Gets the running chart of the engine, for diagnostics and tests.</summary>
    internal ChartController? Controller => _controller;

    /// <summary>Gets the connection to the running chart. Safe from any thread.</summary>
    internal ChartInteraction? Interaction => _controller?.Pipeline.Interaction;

    /// <summary>Gets the shared device and render thread while the chart runs.</summary>
    internal ChartRuntime? Runtime => _runtime;

    /// <inheritdoc />
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ChartAutomationPeer(this);

    /// <summary>Writes the visible points as CSV.</summary>
    /// <param name="selection">The points, or <see langword="null"/> for all in the visible area.</param>
    /// <returns>The file as UTF-8.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public Task<byte[]> ExportCsvAsync(IReadOnlyList<Core.Input.SeriesSelection>? selection = null)
    {
        var controller = _controller ?? throw new InvalidOperationException(NotShown);
        return controller.ExportAsync((surface, _) => ChartExporter.ToCsv(surface, selection));
    }

    /// <summary>Puts a picture of the chart, as large as on the screen, on the clipboard.</summary>
    /// <param name="theme">A theme for this picture only.</param>
    /// <returns>When the picture is there.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public async Task CopyImageAsync(Core.Themes.Theme? theme = null)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerInchX;
        var png = await ExportPngAsync(new ExportOptions { Width = ActualWidth, Height = ActualHeight, Dpi = dpi, Theme = theme });
        var data = new DataObject();
        data.SetImage(ToBitmap(png));
        data.SetData("PNG", new MemoryStream(png));
        Clipboard.SetDataObject(data, true);
    }

    /// <summary>Puts the visible points as CSV text on the clipboard.</summary>
    /// <returns>When the text is there.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public async Task CopyDataAsync() => Clipboard.SetText(System.Text.Encoding.UTF8.GetString(await ExportCsvAsync()));

    /// <summary>Asks for a printer and prints the chart on a full page, with 300 dpi.</summary>
    /// <param name="description">The name of the job in the printer queue.</param>
    /// <param name="theme">A theme for the print only.</param>
    /// <returns>Whether it was printed. The user can cancel.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public async Task<bool> PrintAsync(string description, Core.Themes.Theme? theme = null)
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        var size = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
        var png = await ExportPngAsync(new ExportOptions { Width = size.Width, Height = size.Height, Dpi = PrintDpi, Theme = theme });
        var page = new DrawingVisual();
        using (var drawing = page.RenderOpen())
        {
            drawing.DrawImage(ToBitmap(png), new Rect(size));
        }

        dialog.PrintVisual(page, description);
        return true;
    }

    private static BitmapSource ToBitmap(byte[] png)
    {
        var bitmap = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Writes what the chart shows into an SVG file. Text and rasters are embedded as pictures.</summary>
    /// <param name="options">The size and the theme, or <see langword="null"/> for the defaults.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public Task<byte[]> ExportSvgAsync(ExportOptions? options = null)
    {
        var controller = _controller ?? throw new InvalidOperationException(NotShown);
        return controller.ExportAsync((surface, _) => ChartExporter.ToSvg(surface, options ?? new ExportOptions()));
    }

    /// <summary>Writes what the chart shows into a PDF file. Text and rasters are embedded as pictures.</summary>
    /// <param name="options">The size and the theme, or <see langword="null"/> for the defaults.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public Task<byte[]> ExportPdfAsync(ExportOptions? options = null)
    {
        var controller = _controller ?? throw new InvalidOperationException(NotShown);
        return controller.ExportAsync((surface, _) => ChartExporter.ToPdf(surface, options ?? new ExportOptions()));
    }

    /// <summary>Renders what the chart shows into a PNG file, in any size and with its own theme.</summary>
    /// <param name="options">The size, the resolution and the theme, or <see langword="null"/> for the defaults.</param>
    /// <returns>The file.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public Task<byte[]> ExportPngAsync(ExportOptions? options = null)
    {
        var controller = _controller ?? throw new InvalidOperationException(NotShown);
        return controller.ExportAsync((surface, device) => ChartExporter.ToPng(surface, device, options ?? new ExportOptions()));
    }

    /// <summary>
    /// Records the chart as an MP4 video, frame by frame at fixed times, so that it plays evenly. On Windows Media
    /// Foundation encodes it, elsewhere an FFmpeg the user installed.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="options">The size, length and encoding, or <see langword="null"/> for the defaults.</param>
    /// <param name="step">Called before each frame with its number and time, to move a camera or to add data. It runs on the render thread.</param>
    /// <returns>When the file is written.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet, or the encoder failed.</exception>
    /// <exception cref="FileNotFoundException">FFmpeg is needed and was not found.</exception>
    public Task ExportVideoAsync(string path, VideoOptions? options = null, Action<int, TimeSpan>? step = null)
    {
        var controller = _controller ?? throw new InvalidOperationException(NotShown);
        return controller.ExportAsync((surface, device) =>
        {
            ChartVideo.Write(surface, device, path, options ?? new VideoOptions(), step);
            return true;
        });
    }

    /// <inheritdoc />
    public bool IsRecording => _recording is not null;

    /// <inheritdoc />
    public TimeSpan RecordingTime => _recording?.Elapsed ?? TimeSpan.Zero;

    /// <summary>
    /// Starts to record what the chart shows while the user works with it, like a video recorder. Without options the
    /// video has the size of the chart and 30 frames per second.
    /// </summary>
    /// <param name="path">The MP4 file.</param>
    /// <param name="options">The size, frame rate and encoding, or <see langword="null"/>. The length comes from the recording.</param>
    /// <exception cref="InvalidOperationException">The chart is not shown yet, or a recording runs already.</exception>
    /// <exception cref="FileNotFoundException">FFmpeg is needed and was not found.</exception>
    public void StartRecording(string path, VideoOptions? options = null)
    {
        var controller = _controller ?? throw new InvalidOperationException(NotShown);
        if (_recording is not null)
        {
            throw new InvalidOperationException("A recording runs already.");
        }

        var size = options is null ? _size : new PixelSize(options.Width, options.Height);
        _recording = AreaRecording.Start([(controller, new PixelRect(0, 0, size.Width, size.Height))], size, path, options);
    }

    /// <summary>Stops the recording and completes the file.</summary>
    /// <returns>When the file is written.</returns>
    /// <exception cref="InvalidOperationException">No recording runs, or the encoder failed.</exception>
    public Task StopRecordingAsync()
    {
        var recording = _recording ?? throw new InvalidOperationException("No recording runs.");
        _recording = null;
        return recording.StopAsync();
    }

    /// <summary>Gets the hosted window, if there is one.</summary>
    internal ChartWindowHost? Host => _host;

    /// <summary>
    /// Describes the chart in code. This replaces all axes, series and modifiers, with the same elements
    /// that XAML would declare.
    /// </summary>
    /// <param name="configure">Describes the chart.</param>
    /// <returns>This chart.</returns>
    public Chart Configure(Action<ChartBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new ChartBuilder();
        configure(builder);
        var definition = builder.ToDefinition();

        Title = definition.Title;
        Theme = definition.Theme;
        GroupName = definition.GroupName;
        _overlayFactories = definition.Overlays;
        Replace(XAxes, definition.XAxes.Select(ChartElementFactory.Create));
        Replace(YAxes, definition.YAxes.Select(ChartElementFactory.Create));
        Replace(Series, definition.Series.Select(ChartElementFactory.Create));
        Replace(Modifiers, definition.Modifiers.Select(ChartElementFactory.Create));
        return this;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the chart shows the diagnostics overlay: frame rate, frame times, CPU
    /// time, points, uploads and the renderer. A chart that is not running yet shows it as soon as it starts.
    /// </summary>
    public bool ShowsDiagnostics
    {
        get => _controller?.ShowsDiagnostics ?? _showsDiagnostics;
        set
        {
            _showsDiagnostics = value;
            if (_controller is { } controller)
            {
                controller.ShowsDiagnostics = value;
            }
        }
    }

    /// <summary>Switches the diagnostics overlay of the chart on or off.</summary>
    public void ToggleDiagnostics()
    {
        if (_controller?.Pipeline.Interaction is { } interaction)
        {
            interaction.ToggleDiagnostics();
        }
    }

    /// <inheritdoc />
    protected override IEnumerator LogicalChildren => _logicalChildren.GetEnumerator();

    /// <inheritdoc />
    protected override int VisualChildrenCount => _presenter is null ? 0 : 1;

    /// <inheritdoc />
    protected override Visual GetVisualChild(int index) =>
        _presenter ?? throw new ArgumentOutOfRangeException(nameof(index));

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        _presenter?.Measure(availableSize);
        return default;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        _presenter?.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <inheritdoc />
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (IsReadback)
        {
            Resize((int)Math.Ceiling(ActualWidth * _scale), (int)Math.Ceiling(ActualHeight * _scale), _scale);
        }
    }

    /// <inheritdoc />
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        if (IsReadback)
        {
            Resize((int)Math.Ceiling(ActualWidth * newDpi.DpiScaleX), (int)Math.Ceiling(ActualHeight * newDpi.DpiScaleX), newDpi.DpiScaleX);
        }
    }

    /// <inheritdoc />
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        CaptureMouse();
        ProcessMouse(e, PointerEventKind.Pressed, WpfInput.ToButton(e.ChangedButton), e.ClickCount);
    }

    /// <inheritdoc />
    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        ProcessMouse(e, PointerEventKind.Released, WpfInput.ToButton(e.ChangedButton), 0);
        if (WpfInput.ToButtons(e.MouseDevice) == PointerButtons.None)
        {
            ReleaseMouseCapture();
        }
    }

    /// <inheritdoc />
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        ProcessMouse(e, PointerEventKind.Moved, PointerButton.None, 0);
    }

    /// <inheritdoc />
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var position = e.GetPosition(this);
        var input = new WheelInputEvent(
            new System.Numerics.Vector2((float)position.X, (float)position.Y),
            new System.Numerics.Vector2(0, e.Delta / 120f),
            WpfInput.ToModifiers(Keyboard.Modifiers),
            Timestamp);
        e.Handled |= _controller?.Pipeline.Process(input) == true;
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        ProcessKey(e, KeyEventKind.Pressed);
    }

    /// <inheritdoc />
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        ProcessKey(e, KeyEventKind.Released);
    }

    /// <inheritdoc />
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _controller?.Pipeline.CancelAllCaptures();
    }

    private static void OnTitleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Chart)sender)._controller?.SetTitle((string?)args.NewValue);

    private static void OnGroupNameChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((Chart)sender)._controller?.SetGroup((string?)args.NewValue);

    private static void OnThemeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var chart = (Chart)sender;
        chart._controller?.SetTheme((Theme?)args.NewValue, chart.ThemeTransition);
    }

    private TimeSpan Timestamp => DateTime.UtcNow - _start;

    private static void Replace<T>(ChartElementCollection<T> elements, IEnumerable<T> replacements)
        where T : ChartElement
    {
        elements.Clear();
        foreach (var element in replacements)
        {
            elements.Add(element);
        }
    }

    private void Track<T>(ChartElementCollection<T> elements, Action changed)
        where T : ChartElement
    {
        ElementCollections.Track(
            elements,
            element =>
            {
                _logicalChildren.Add(element);
                AddLogicalChild(element);
                ((ChartElement)element).Host = _controller;
            },
            element =>
            {
                _logicalChildren.Remove(element);
                RemoveLogicalChild(element);
                ((ChartElement)element).Host = null;
            },
            changed);
    }

    private void OnAxesChanged() => _controller?.OnAxesChanged();

    private void Start()
    {
        if (_runtime is not null)
        {
            return;
        }

        _runtime = ChartRuntime.Acquire(Renderer);
        _runtime.DeviceLost += OnDeviceLost;
        _controller = new ChartController(
            _runtime.Scheduler,
            SkiaTextEngine.Default,
            Dispatcher,
            XAxes,
            YAxes,
            Series,
            Modifiers,
            Annotations);
        _controller.OverlayFactories = _overlayFactories;
        _controller.ShowsDiagnostics = _showsDiagnostics;
        foreach (var element in _logicalChildren)
        {
            ((ChartElement)element).Host = _controller;
        }

        _scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        _size = new PixelSize(Math.Max((int)Math.Ceiling(ActualWidth * _scale), 1), Math.Max((int)Math.Ceiling(ActualHeight * _scale), 1));

        _controller.SetGroup(GroupName);
        _controller.Start(CreateTarget(), Title, Theme);
    }

    // On the render thread. Stopping waits for it, so the rest happens on the UI thread.
    private void OnDeviceLost() => Dispatcher.BeginInvoke(() =>
    {
        if (_runtime is not null)
        {
            Stop();
            Start();
        }
    });

    private IRenderTarget CreateTarget()
    {
        var runtime = _runtime!;
        if (runtime.Device.Capabilities.BackendName.StartsWith("WebGPU", StringComparison.Ordinal))
        {
            var host = new ChartWindowHost();
            SetPresenter(host);
            host.PhysicalSizeChanged += (width, height) => Resize(width, height, _scale);
            host.ScaleChanged += newScale => Resize(_size.Width, _size.Height, newScale);
            host.Pointer += input =>
            {
                _controller?.Pipeline.Process(input);
                OpenMenu(input);
            };
            host.Wheel += input => _controller?.Pipeline.Process(input);
            host.KeyInput += input =>
            {
                // The keyboard stays in the child window, Esc would not reach the open menu.
                if (input.Key == ChartKey.Escape && ContextMenu is { IsOpen: true } menu)
                {
                    menu.IsOpen = false;
                    return;
                }

                _controller?.Pipeline.Process(input);
            };
            host.CaptureLost += () => _controller?.Pipeline.CancelAllCaptures();

            // Builds the child window now, the swapchain needs it.
            UpdateLayout();
            if (host.Handle != 0 && TryCreateSurface(host))
            {
                _host = host;
                return _surfaceTarget!;
            }

            SetPresenter(null);
            host.Dispose();
        }

        return CreateReadback();
    }

    private bool TryCreateSurface(ChartWindowHost host)
    {
        var runtime = _runtime!;
        try
        {
            runtime.Scheduler.Invoke(() =>
            {
                _surfaceTarget = runtime.Device.CreateSurfaceTarget(new NativeSurfaceHandle(NativeSurfaceKind.Win32Window, host.Handle), _size, _scale);
                runtime.AddSurface(_surfaceTarget);
            });
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private IRenderTarget CreateReadback()
    {
        var runtime = _runtime!;
        runtime.Scheduler.Invoke(() => _readbackTarget = runtime.Device.CreateOffscreenTarget(_size, _scale));
        _image = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.NearestNeighbor);
        SetPresenter(_image);
        PrepareBitmap();
        _readbackTimer.Start();
        return _readbackTarget!;
    }

    private void PrepareBitmap()
    {
        _pixels = new Rgba8[_size.PixelCount];
        _bytes = new byte[_size.PixelCount * 4];
        _bitmap = new WriteableBitmap(_size.Width, _size.Height, 96 * _scale, 96 * _scale, PixelFormats.Bgra32, null);
        _image!.Source = _bitmap;
        _shownFrame = -1;
    }

    private void SetPresenter(FrameworkElement? presenter)
    {
        if (_presenter is { } old)
        {
            RemoveVisualChild(old);
            RemoveLogicalChild(old);
        }

        _presenter = presenter;
        if (presenter is not null)
        {
            AddVisualChild(presenter);
            AddLogicalChild(presenter);
        }

        InvalidateMeasure();
    }

    private void Resize(int width, int height, double scale)
    {
        // A minimized window has no size. The chart keeps its target until it comes back.
        var size = new PixelSize(width, height);
        if (width <= 0 || height <= 0 || (size == _size && scale == _scale) || _runtime is not { } runtime)
        {
            return;
        }

        _size = size;
        _scale = scale;
        if (_surfaceTarget is { } surface)
        {
            runtime.Scheduler.Post(() => surface.Resize(size, scale));
        }
        else if (_readbackTarget is { } old)
        {
            runtime.Scheduler.Invoke(() =>
            {
                var target = runtime.Device.CreateOffscreenTarget(size, scale);
                _controller?.ReplaceTarget(target);
                lock (_readbackGate)
                {
                    _readbackTarget = target;
                }

                old.Dispose();
            });
            PrepareBitmap();
        }
    }

    // The image travels through the render thread, because the target belongs to it.
    private void ShowReadback()
    {
        if (_readbackTarget is not { } target
            || _bitmap is not { } bitmap
            || _runtime is not { } runtime
            || _controller?.Surface is not { } surface
            || surface.FrameCount == _shownFrame
            || target.Size != _size)
        {
            return;
        }

        _shownFrame = surface.FrameCount;
        runtime.Scheduler.Invoke(() => target.ReadPixels(_pixels));
        for (var i = 0; i < _pixels.Length; i++)
        {
            _bytes[i * 4] = _pixels[i].B;
            _bytes[(i * 4) + 1] = _pixels[i].G;
            _bytes[(i * 4) + 2] = _pixels[i].R;
            _bytes[(i * 4) + 3] = _pixels[i].A;
        }

        bitmap.WritePixels(new Int32Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight), _bytes, bitmap.PixelWidth * 4, 0);
    }

    // The child window keeps the right button from WPF, which would open the menu by itself otherwise.
    private void OpenMenu(in PointerInputEvent input)
    {
        if (input.Kind == PointerEventKind.Released && input.ChangedButton == PointerButton.Secondary && ContextMenu is { } menu)
        {
            menu.PlacementTarget = this;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            menu.IsOpen = true;
        }
    }

    private void ProcessMouse(MouseEventArgs e, PointerEventKind kind, PointerButton button, int clicks)
    {
        var position = e.GetPosition(this);
        var input = new PointerInputEvent(
            kind,
            1,
            new System.Numerics.Vector2((float)position.X, (float)position.Y),
            WpfInput.ToButtons(e.MouseDevice),
            button,
            WpfInput.ToModifiers(Keyboard.Modifiers),
            Timestamp,
            PointerDeviceKind.Mouse,
            clicks);
        e.Handled |= _controller?.Pipeline.Process(input) == true;
    }

    private void ProcessKey(KeyEventArgs e, KeyEventKind kind)
    {
        var key = WpfInput.ToChartKey(e.Key);
        if (key != ChartKey.None)
        {
            var input = new KeyInputEvent(kind, key, WpfInput.ToModifiers(Keyboard.Modifiers), e.IsRepeat, Timestamp);
            e.Handled |= _controller?.Pipeline.Process(input) == true;
        }
    }

    private void Stop()
    {
        _readbackTimer.Stop();
        if (_runtime is not { } runtime)
        {
            return;
        }

        runtime.DeviceLost -= OnDeviceLost;
        foreach (var element in _logicalChildren)
        {
            ((ChartElement)element).Host = null;
        }

        _controller?.Dispose();
        _controller = null;

        var surface = _surfaceTarget;
        var readback = _readbackTarget;
        runtime.Scheduler.Invoke(() =>
        {
            runtime.Device.WaitForIdle();
            surface?.Dispose();
            readback?.Dispose();
        });

        if (surface is not null)
        {
            runtime.RemoveSurface();
        }

        _surfaceTarget = null;
        _readbackTarget = null;
        SetPresenter(null);
        _host?.Dispose();
        _host = null;
        _image = null;
        _bitmap = null;
        _runtime = null;
        runtime.Release();
    }
}
