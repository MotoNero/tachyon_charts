using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Input;
using Avalonia.Input.GestureRecognizers;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Metadata;
using Avalonia.Platform;
using Avalonia.Threading;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Export;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using AvaloniaPixelSize = Avalonia.PixelSize;
using IRenderTarget = Tachyon.Charts.Rendering.IRenderTarget;
using PixelFormat = Avalonia.Platform.PixelFormat;
using PixelSize = Tachyon.Charts.Rendering.PixelSize;
using PointerDeviceKind = Tachyon.Charts.Core.Input.PointerDeviceKind;
using Vector = Avalonia.Vector;

namespace Tachyon.Charts.Avalonia;

/// <summary>
/// A GPU-accelerated chart.
/// </summary>
/// <remarks>
/// Under Windows the chart renders into a native child window. Controls cannot lie on top of it;
/// popups can, because they are windows of their own. Elsewhere, and without a usable swapchain, it
/// copies its image into a bitmap, which removes this limit at the price of speed.
/// </remarks>
public sealed class Chart : Control, IRecordableChart
{
    private const string NotShown = "The chart is not shown yet.";

    /// <summary>Identifies the <see cref="Title"/> property.</summary>
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<Chart, string?>(nameof(Title));

    /// <summary>Identifies the <see cref="GroupName"/> property.</summary>
    public static readonly StyledProperty<string?> GroupNameProperty = AvaloniaProperty.Register<Chart, string?>(nameof(GroupName));

    /// <summary>Identifies the <see cref="ChartTheme"/> property.</summary>
    public static readonly StyledProperty<Theme?> ChartThemeProperty = AvaloniaProperty.Register<Chart, Theme?>(nameof(ChartTheme));

    private readonly DispatcherTimer _readbackTimer;
    private readonly DateTime _start = DateTime.UtcNow;
    private readonly ClickCounter _clicks = new(TimeSpan.FromMilliseconds(500), 4);

    private ChartRuntime? _runtime;
    private IReadOnlyList<Func<Core.Pipeline.ChartSurface, Core.Overlays.ChartOverlay>> _overlayFactories = [];
    private bool _showsDiagnostics;
    private ChartController? _controller;
    private IChartRecording? _recording;
    private ChartWindowHost? _host;
    private ISurfaceRenderTarget? _surfaceTarget;
    private IOffscreenRenderTarget? _readbackTarget;
    private WriteableBitmap? _bitmap;
    private Rgba8[] _pixels = [];
    private long _shownFrame = -1;
    private PixelSize _size = new(1, 1);
    private double _scale = 1;
    private double _lastPinch = 1;
    private TopLevel? _topLevel;

    /// <summary>Initializes a new instance of the <see cref="Chart"/> class.</summary>
    public Chart()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
        GestureRecognizers.Add(new PinchGestureRecognizer());
        AddHandler(InputElement.PinchEvent, OnPinch);
        AddHandler(InputElement.PinchEndedEvent, OnPinchEnded);
        _readbackTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => ShowReadback());
        _readbackTimer.Stop();
        Track(XAxes, () => _controller?.OnAxesChanged());
        Track(YAxes, () => _controller?.OnAxesChanged());
        Track(Series, () => _controller?.OnSeriesChanged());
        Track(Modifiers, () => _controller?.OnModifiersChanged());
    }

    /// <summary>
    /// Gets or sets the renderer of charts that start from now on. All charts of a process share one,
    /// so a change only takes effect when no chart is running.
    /// </summary>
    public static RendererPreference Renderer { get; set; } =
        RendererSelector.ParsePreference(Environment.GetEnvironmentVariable("TACHYON_RENDERER"));

    /// <summary>
    /// Gets or sets the most frames per second of all charts, or <see langword="null"/> for the refresh rate of the
    /// fastest screen, which is the default: at 120 Hz charts draw up to 120 frames per second, at 60 Hz up to 60. A
    /// fixed limit saves power, for example 30 for a display that changes slowly.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is below 1.</exception>
    public static int? MaximumFramesPerSecond
    {
        get => ChartRuntime.FixedFrameRate;
        set
        {
            if (value is < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The limit must be at least 1.");
            }

            ChartRuntime.FixedFrameRate = value;
        }
    }

    /// <summary>Gets or sets the chart title.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets or sets the name of a group of charts that share their X range, cursor and axis widths.</summary>
    public string? GroupName
    {
        get => GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    /// <summary>Gets or sets the theme of the chart. Avalonia uses the name Theme for control themes.</summary>
    public Theme? ChartTheme
    {
        get => GetValue(ChartThemeProperty);
        set => SetValue(ChartThemeProperty, value);
    }

    /// <summary>Gets or sets how long a change of the theme blends its colors.</summary>
    public TimeSpan ThemeTransition { get; set; }

    /// <summary>Gets the horizontal axes, at least one.</summary>
    public ChartElementCollection<ChartAxis> XAxes { get; } = [];

    /// <summary>Gets the vertical axes, at least one.</summary>
    public ChartElementCollection<ChartAxis> YAxes { get; } = [];

    /// <summary>Gets the series.</summary>
    [Content]
    public ChartElementCollection<ChartSeriesElement> Series { get; } = [];

    /// <summary>Gets the interactions.</summary>
    public ChartElementCollection<ChartModifierElement> Modifiers { get; } = [];

    /// <summary>Gets a value indicating whether the chart copies its image instead of using a swapchain.</summary>
    public bool IsReadback => _readbackTarget is not null;

    /// <summary>Gets the running chart of the engine, for diagnostics and tests.</summary>
    internal ChartController? Controller => _controller;

    /// <summary>Gets the connection to the running chart. Safe from any thread.</summary>
    internal ChartInteraction? Interaction => _controller?.Pipeline.Interaction;

    /// <summary>Gets the shared device and render thread while the chart runs.</summary>
    internal ChartRuntime? Runtime => _runtime;

    /// <inheritdoc />
    protected override global::Avalonia.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new ChartAutomationPeer(this);

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
    /// <exception cref="InvalidOperationException">The chart is not shown yet or has no clipboard.</exception>
    public async Task CopyImageAsync(Core.Themes.Theme? theme = null)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("The chart has no clipboard.");
        var png = await ExportPngAsync(new ExportOptions { Width = Bounds.Width, Height = Bounds.Height, Theme = theme });
        using var stream = new MemoryStream(png);
        using var bitmap = new global::Avalonia.Media.Imaging.Bitmap(stream);
        await clipboard.SetBitmapAsync(bitmap);
        await clipboard.FlushAsync();
    }

    /// <summary>Puts the visible points as CSV text on the clipboard.</summary>
    /// <returns>When the text is there.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet or has no clipboard.</exception>
    public async Task CopyDataAsync()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard ?? throw new InvalidOperationException("The chart has no clipboard.");
        await clipboard.SetTextAsync(System.Text.Encoding.UTF8.GetString(await ExportCsvAsync()));
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
        if (_controller is null)
        {
            throw new InvalidOperationException(NotShown);
        }

        if (_recording is not null)
        {
            throw new InvalidOperationException("A recording runs already.");
        }

        var size = options is null ? _size : new PixelSize(options.Width, options.Height);
        _recording = AreaRecording.Start([(() => _controller, new Tachyon.Charts.Rendering.PixelRect(0, 0, size.Width, size.Height))], size, path, options);
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

    /// <summary>Gets the last image shown, for tests.</summary>
    internal WriteableBitmap? Bitmap => _bitmap;

    private TimeSpan Timestamp => DateTime.UtcNow - _start;

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
        ChartTheme = definition.Theme;
        GroupName = definition.GroupName;
        _overlayFactories = definition.Overlays;

        // A running chart builds itself anew with the new elements below; it must take the new overlays along.
        if (_controller is not null)
        {
            _controller.OverlayFactories = _overlayFactories;
        }

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
    public void ToggleDiagnostics() => _controller?.Pipeline.Interaction?.ToggleDiagnostics();

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        // Only what is drawn receives input: without this the swapchain would leave the chart deaf.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (_bitmap is { } bitmap)
        {
            context.DrawImage(bitmap, new Rect(Bounds.Size));
        }
    }

    /// <summary>Shows the newest image of a readback chart. A timer calls this, tests do so directly.</summary>
    internal void ShowReadback()
    {
        if (_readbackTarget is not { } target
            || _runtime is not { } runtime
            || _controller?.Surface is not { } surface
            || surface.FrameCount == _shownFrame
            || target.Size != _size)
        {
            return;
        }

        _shownFrame = surface.FrameCount;
        if (_pixels.Length != _size.PixelCount)
        {
            _pixels = new Rgba8[_size.PixelCount];
        }

        // The image travels through the render thread, because the target belongs to it.
        runtime.Scheduler.Invoke(() => target.ReadPixels(_pixels));
        CopyToBitmap();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnLoaded(global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Start();
    }

    /// <inheritdoc />
    protected override void OnUnloaded(global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        Stop();
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Resize(_scale);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty)
        {
            _controller?.SetTitle(Title);
        }
        else if (change.Property == GroupNameProperty)
        {
            _controller?.SetGroup(GroupName);
        }
        else if (change.Property == ChartThemeProperty)
        {
            _controller?.SetTheme(ChartTheme, ThemeTransition);
        }
        else if (change.Property == IsVisibleProperty && !IsVisible)
        {
            // No event tells a hidden control that the pointer is gone, and a popup would stay open.
            _controller?.Pipeline.Process(new PointerInputEvent(PointerEventKind.Exited, 0, default, Timestamp: Timestamp));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        e.Pointer.Capture(this);
        var point = e.GetCurrentPoint(this);

        // The main window loses its count when the pointer seems to leave it between two clicks.
        var position = new System.Numerics.Vector2((float)point.Position.X, (float)point.Position.Y);
        var clicks = _host is null ? e.ClickCount : _clicks.Click(position, Timestamp);
        e.Handled |= Process(e, point, PointerEventKind.Pressed, AvaloniaInput.ToButton(point.Properties.PointerUpdateKind), clicks);
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var point = e.GetCurrentPoint(this);
        e.Handled |= Process(e, point, PointerEventKind.Released, AvaloniaInput.ToButton(point.Properties.PointerUpdateKind), 0);
        if (AvaloniaInput.ToButtons(point.Properties) == PointerButtons.None)
        {
            e.Pointer.Capture(null);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        e.Handled |= Process(e, e.GetCurrentPoint(this), PointerEventKind.Moved, PointerButton.None, 0);
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        // The main window thinks the pointer left it as soon as it rests on the child window.
        if (_host?.ContainsCursor() != true)
        {
            Process(e, e.GetCurrentPoint(this), PointerEventKind.Exited, PointerButton.None, 0);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _controller?.Pipeline.CancelAllCaptures();
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var position = e.GetPosition(this);
        var input = new WheelInputEvent(
            new System.Numerics.Vector2((float)position.X, (float)position.Y),
            new System.Numerics.Vector2((float)e.Delta.X, (float)e.Delta.Y),
            AvaloniaInput.ToModifiers(e.KeyModifiers),
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
        var known = new List<T>();
        elements.CollectionChanged += (_, _) =>
        {
            foreach (var element in known.Except(elements).ToArray())
            {
                known.Remove(element);
                LogicalChildren.Remove(element);
                element.Host = null;
            }

            foreach (var element in elements.Except(known).ToArray())
            {
                known.Add(element);
                LogicalChildren.Add(element);
                element.Host = _controller;
            }

            changed();
        };
    }

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
            action => Dispatcher.UIThread.Post(action, DispatcherPriority.Loaded),
            action => Dispatcher.UIThread.Post(action, DispatcherPriority.Background),
            XAxes,
            YAxes,
            Series,
            Modifiers,
            []);
        _controller.OverlayFactories = _overlayFactories;
        _controller.ShowsDiagnostics = _showsDiagnostics;
        foreach (var element in LogicalChildren.OfType<ChartElement>())
        {
            element.Host = _controller;
        }

        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.ScalingChanged += OnScalingChanged;
        }

        _scale = _topLevel?.RenderScaling ?? 1;
        _size = ToPixels(_scale);
        _controller.SetGroup(GroupName);
        _controller.Start(CreateTarget(), Title, ChartTheme);
    }

    // On the render thread. Stopping waits for it, so the rest happens on the UI thread.
    private void OnDeviceLost() => Dispatcher.UIThread.Post(() =>
    {
        if (_runtime is not null)
        {
            Stop();
            Start();
        }
    });

    private PixelSize ToPixels(double scale) => new(
        Math.Max((int)Math.Ceiling(Bounds.Width * scale), 1),
        Math.Max((int)Math.Ceiling(Bounds.Height * scale), 1));

    private IRenderTarget CreateTarget()
    {
        var runtime = _runtime!;
        if (runtime.Device.Capabilities.BackendName.StartsWith("WebGPU", StringComparison.Ordinal)
            && _topLevel?.TryGetPlatformHandle()?.HandleDescriptor == "HWND")
        {
            var host = new ChartWindowHost { IsHitTestVisible = false };
            VisualChildren.Add(host);

            // It arrives after the layout pass, which would leave it without a size.
            InvalidateMeasure();
            if (host.Handle != 0 && TryCreateSurface(host))
            {
                _host = host;
                return _surfaceTarget!;
            }

            VisualChildren.Remove(host);
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
        _readbackTimer.Start();
        return _readbackTarget!;
    }

    private unsafe void CopyToBitmap()
    {
        if (_bitmap is null || _bitmap.PixelSize.Width != _size.Width || _bitmap.PixelSize.Height != _size.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new AvaloniaPixelSize(_size.Width, _size.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        }

        using var target = _bitmap.Lock();
        var rowBytes = _size.Width * 4;
        fixed (Rgba8* source = _pixels)
        {
            for (var row = 0; row < _size.Height; row++)
            {
                Buffer.MemoryCopy((byte*)source + (row * (long)rowBytes), (byte*)target.Address + (row * (long)target.RowBytes), rowBytes, rowBytes);
            }
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        if (_topLevel is { } topLevel)
        {
            Resize(topLevel.RenderScaling);
        }
    }

    private void Resize(double scale)
    {
        // A minimized window has no size. The chart keeps its target until it comes back.
        var size = ToPixels(scale);
        if (Bounds.Width <= 0 || Bounds.Height <= 0 || (size == _size && scale == _scale) || _runtime is not { } runtime)
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
                _readbackTarget = target;
                old.Dispose();
            });
            _shownFrame = -1;
        }
    }

    private bool Process(PointerEventArgs e, PointerPoint point, PointerEventKind kind, PointerButton button, int clicks)
    {
        var input = new PointerInputEvent(
            kind,
            point.Pointer.Id,
            new System.Numerics.Vector2((float)point.Position.X, (float)point.Position.Y),
            AvaloniaInput.ToButtons(point.Properties),
            button,
            AvaloniaInput.ToModifiers(e.KeyModifiers),
            Timestamp,
            point.Pointer.Type switch
            {
                PointerType.Touch => PointerDeviceKind.Touch,
                PointerType.Pen => PointerDeviceKind.Pen,
                _ => PointerDeviceKind.Mouse,
            },
            clicks);
        return _controller?.Pipeline.Process(input) == true;
    }

    private void ProcessKey(KeyEventArgs e, KeyEventKind kind)
    {
        var key = AvaloniaInput.ToChartKey(e.Key);
        if (key != ChartKey.None)
        {
            var input = new KeyInputEvent(kind, key, AvaloniaInput.ToModifiers(e.KeyModifiers), false, Timestamp);
            e.Handled |= _controller?.Pipeline.Process(input) == true;
        }
    }

    private void OnPinch(object? sender, PinchEventArgs e)
    {
        var position = new System.Numerics.Vector2((float)(e.ScaleOrigin.X * Bounds.Width), (float)(e.ScaleOrigin.Y * Bounds.Height));
        var input = new GestureInputEvent(GestureKind.Pinch, GesturePhase.Changed, position, default, (float)(e.Scale / _lastPinch), InputModifiers.None, Timestamp);
        _lastPinch = e.Scale;
        e.Handled |= _controller?.Pipeline.Process(input) == true;
    }

    private void OnPinchEnded(object? sender, PinchEndedEventArgs e) => _lastPinch = 1;

    private void Stop()
    {
        _readbackTimer.Stop();
        if (_runtime is not { } runtime)
        {
            return;
        }

        if (_topLevel is { } topLevel)
        {
            topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
        }

        runtime.DeviceLost -= OnDeviceLost;
        foreach (var element in LogicalChildren.OfType<ChartElement>())
        {
            element.Host = null;
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
        if (_host is { } host)
        {
            VisualChildren.Remove(host);
            _host = null;
        }

        _bitmap?.Dispose();
        _bitmap = null;
        _shownFrame = -1;
        _runtime = null;
        runtime.Release();
    }
}
