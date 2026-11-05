using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// The root of a chart: axes, panes and series, drawn in the passes of a frame.
/// </summary>
/// <remarks>
/// Layout, ticks, grid and text are expensive and change rarely, so they stay cached until an axis, the
/// title or the size changes. Frames in between allocate nothing. Once attached to a
/// <see cref="RenderScheduler"/>, the chart belongs to the render thread: change it with
/// <see cref="RenderScheduler.Post"/>. Only <see cref="Invalidate"/> is safe from any thread.
/// </remarks>
public sealed class ChartSurface
{
    private const double AxisTitleInset = 7;
    private const float DefaultLabelSize = 11;
    private const float DefaultTitleSize = 14;
    private const double AutoRangePadding = 0.05;
    private const int ColorBarSegments = 64;
    private const double DiagnosticsMargin = 6;

    private static readonly Rgba8 _defaultTick = new(150, 160, 175, 255);
    private static readonly Rgba8 _defaultText = new(200, 208, 220, 255);

    private readonly AxisBase[] _horizontalAxes;
    private readonly LayoutPane[] _panes;
    private readonly AxisBase[] _axes;
    private readonly ITextEngine _textEngine;
    private readonly TextEngineMeasurer _measurer;
    private ChartLayoutOptions _options;

    private readonly List<PixelRect> _paneRects = [];
    private readonly List<PixelRect> _bandRects = [];
    private readonly List<PixelRect> _minorGridRects = [];
    private readonly List<PixelRect> _gridRects = [];
    private readonly List<PixelRect> _paneBorderRects = [];
    private readonly List<PixelRect> _tickRects = [];
    private readonly object _transitionTarget = new();
    private bool _effectsEnabled = true;
    private double _glowRadius;
    private double _glowIntensity;
    private readonly List<(PixelRect Bounds, Rgba8 Top, Rgba8 Bottom)> _colorBars = [];
    [ThreadStatic]
    private static Theme? _themeOverride;

    private IRenderDevice? _lastDevice;
    private FramePhases _phases;
    private GlyphInstance[] _glyphs = new GlyphInstance[512];
    private int _glyphCount;
    private SeriesFrame[] _seriesFrames = [];
    private volatile HitTestState? _hitTestState;

    private ChartSeries[] _series = [];
    private bool _usedCanvas;
    private bool _drawsLastValues;
    private readonly Tachyon.Charts.Core.Overlays.LastValueMarks _lastValues = new();
    private ChartOverlay[] _overlays = [];
    private long _renderedOverlayVersions = -1;
    private readonly OverlayCanvas _canvas = null!;
    private string? _title;
    private ChartSurfaceStyle _style = ChartSurfaceStyle.Default;
    private Theme? _theme;
    private Rgba8 _tickColor = _defaultTick;
    private Rgba8 _labelColor = _defaultText;
    private Rgba8 _titleColor = _defaultText;
    private float _labelSize = DefaultLabelSize;
    private float _titleSize = DefaultTitleSize;
    private long _structureVersion;
    private readonly FrameDiagnostics _diagnostics = new();
    private readonly DiagnosticsText _diagnosticLines = new();
    private GlyphInstance[] _diagnosticGlyphs = new GlyphInstance[256];
    private int _diagnosticGlyphCount;
    private PixelRect _diagnosticBounds;
    private bool _showDiagnostics;
    private long _overlayVersion;
    private long _renderedOverlay = -1;
    private DipRect? _zoomRectangle;
    private ChartLayoutResult? _currentLayout;
    private int _invalidated = 1;
    private long _frameCount;

    private long _renderedStructure = -1;
    private long _renderedAxes = -1;
    private long _renderedSeries = -1;
    private long _layoutStructure = -1;
    private long _layoutAxes = -1;
    private PixelSize _renderedSize;
    private double _renderedScale;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChartSurface"/> class.
    /// </summary>
    /// <param name="horizontalAxes">The axes at the top and bottom.</param>
    /// <param name="panes">The panes, top first, at least one.</param>
    /// <param name="textEngine">The text engine, which the caller keeps owning.</param>
    /// <param name="options">The distances, or <see langword="null"/> for the defaults.</param>
    public ChartSurface(
        IReadOnlyList<AxisBase> horizontalAxes,
        IReadOnlyList<LayoutPane> panes,
        ITextEngine textEngine,
        ChartLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(horizontalAxes);
        ArgumentNullException.ThrowIfNull(panes);
        _textEngine = textEngine ?? throw new ArgumentNullException(nameof(textEngine));
        _horizontalAxes = [.. horizontalAxes];
        _panes = [.. panes];
        _axes = [.. horizontalAxes, .. panes.SelectMany(pane => pane.Axes)];
        _measurer = new TextEngineMeasurer(textEngine, DefaultLabelSize);
        _options = options ?? new ChartLayoutOptions();
        _canvas = new OverlayCanvas(textEngine);
    }

    /// <summary>
    /// Gets or sets the chart title.
    /// </summary>
    public string? Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                _structureVersion++;
            }
        }
    }

    /// <summary>
    /// Gets or sets the colors of the background and grid passes.
    /// </summary>
    public ChartSurfaceStyle Style
    {
        get => _style;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_style != value)
            {
                _style = value;
                _structureVersion++;
            }
        }
    }

    /// <summary>
    /// Gets or sets a theme for all charts rendered on this thread, whatever they set themselves. Tools
    /// such as a gallery use it to show ready-made charts in another theme.
    /// </summary>
    public static Theme? ThemeOverride
    {
        get => _themeOverride;
        set => _themeOverride = value;
    }

    /// <summary>
    /// Gets or sets the resolved theme. Setting a theme applies its surface, grid, axis and text tokens.
    /// Setting <see langword="null"/> restores the original frame-pipeline colors and sizes.
    /// </summary>
    public Theme? Theme
    {
        get => _theme;
        set // NOSONAR: the setter stores the resolved theme through ApplyTheme
        {
            var resolved = value?.Resolve();
            if (_theme != resolved)
            {
                ApplyTheme(resolved);
                ApplyPalette();
                _structureVersion++;
            }
        }
    }

    /// <summary>Gets the animations of this chart, which run on the render thread.</summary>
    public ChartAnimator Animator { get; init; } = new();

    /// <summary>Gets or sets how long a zoom to an area takes. Zero, the default, jumps at once.</summary>
    public TimeSpan ZoomAnimationDuration { get; set; }

    /// <summary>
    /// Changes the theme with a blend of the surface colors. Text changes at once.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <param name="duration">The duration of the blend.</param>
    public void TransitionTheme(Theme? theme, TimeSpan duration)
    {
        var from = _style;
        Theme = theme;
        var to = _style;
        if (duration <= TimeSpan.Zero || from == to)
        {
            return;
        }

        Style = from;
        Animator.Animate(_transitionTarget, duration, Easing.CubicOut, progress => Style = progress >= 1 ? to : ChartSurfaceStyle.Blend(from, to, progress));
    }

    /// <summary>
    /// Gets or sets a value indicating whether effects of the theme are drawn. Off is the profile
    /// "Performance". Effects set on a series itself stay.
    /// </summary>
    public bool EffectsEnabled
    {
        get => _effectsEnabled;
        set
        {
            if (_effectsEnabled != value)
            {
                _effectsEnabled = value;
                _structureVersion++;
            }
        }
    }

    /// <summary>
    /// Gets or sets the preview of a zoom in device-independent units, or <see langword="null"/> for none.
    /// </summary>
    public DipRect? ZoomRectangle
    {
        get => _zoomRectangle;
        set
        {
            if (_zoomRectangle != value)
            {
                _zoomRectangle = value;
                _overlayVersion++;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plot shows frame rate, CPU time per frame, draw calls,
    /// points, uploads and the renderer. While it is on, the chart renders a few times per second.
    /// </summary>
    public bool ShowDiagnostics
    {
        get => _showDiagnostics;
        set
        {
            if (_showDiagnostics != value)
            {
                _showDiagnostics = value;
                _diagnosticGlyphCount = 0;
                _overlayVersion++;
            }
        }
    }

    /// <summary>
    /// Gets the layout of the last frame, or <see langword="null"/> before the first one. Safe from any
    /// thread: a layout never changes, a new one replaces it.
    /// </summary>
    public ChartLayoutResult? CurrentLayout => Volatile.Read(ref _currentLayout);

    /// <summary>
    /// Gets the axes at the top and bottom. Safe from any thread.
    /// </summary>
    public IReadOnlyList<AxisBase> HorizontalAxes => _horizontalAxes;

    /// <summary>
    /// Gets the panes, top first. Safe from any thread.
    /// </summary>
    public IReadOnlyList<LayoutPane> Panes => _panes;

    /// <summary>
    /// Gets the series, drawn in this order.
    /// </summary>
    public IReadOnlyList<ChartSeries> Series => _series;

    /// <summary>
    /// Gets the number of frames rendered so far.
    /// </summary>
    public long FrameCount => Interlocked.Read(ref _frameCount);

    /// <summary>Gets or sets a value indicating whether <see cref="Phases"/> counts. Off by default.</summary>
    public bool MeasuresPhases { get; set; }

    /// <summary>Gets the time spent in the steps of all frames while <see cref="MeasuresPhases"/> is on.</summary>
    public FramePhases Phases => _phases;

    /// <summary>
    /// Gets the number of points drawn in the last frame.
    /// </summary>
    public long DrawnPointCount { get; private set; }

    /// <summary>
    /// Gets the group this chart belongs to.
    /// </summary>
    public ChartGroup? Group { get; internal set; }

    /// <summary>Lays the chart out again, for example when an overlay outside the plot needs other room.</summary>
    internal void InvalidateLayout() => _structureVersion++;

    // Overlays outside the plot, such as legends, take room at the edges.
    private ChartLayoutOptions WithRoom(ChartLayoutOptions options)
    {
        var (left, top, right, bottom) = (0.0, 0.0, 0.0, 0.0);
        foreach (var overlay in _overlays)
        {
            if (overlay is Input.LegendOverlay legend)
            {
                var room = legend.Room(_measurer);
                (left, top, right, bottom) = (left + room.Left, top + room.Top, right + room.Right, bottom + room.Bottom);
            }
        }

        return left + top + right + bottom == 0 ? options : options with { Reserved = (left, top, right, bottom) };
    }

    /// <summary>
    /// Sets the smallest distances of the plot from the edges of the chart.
    /// </summary>
    /// <param name="left">The distance from the left edge in device-independent units.</param>
    /// <param name="right">The distance from the right edge in device-independent units.</param>
    public void SetPlotInsets(double left, double right)
    {
        if (_options.MinimumPlotLeft != left || _options.MinimumPlotRight != right)
        {
            _options = _options with { MinimumPlotLeft = left, MinimumPlotRight = right };
            _structureVersion++;
        }
    }

    /// <summary>
    /// Gets the overlays, drawn in this order above the series.
    /// </summary>
    public IReadOnlyList<ChartOverlay> Overlays => _overlays;

    /// <summary>
    /// Adds an overlay.
    /// </summary>
    /// <param name="overlay">The overlay.</param>
    public void AddOverlay(ChartOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!_overlays.Contains(overlay))
        {
            _overlays = [.. _overlays, overlay];
            _overlayVersion++;
        }
    }

    /// <summary>
    /// Removes an overlay.
    /// </summary>
    /// <param name="overlay">The overlay.</param>
    /// <returns>Whether it was part of this chart.</returns>
    public bool RemoveOverlay(ChartOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!_overlays.Contains(overlay))
        {
            return false;
        }

        _overlays = [.. _overlays.Where(item => item != overlay)];
        _overlayVersion++;
        return true;
    }

    /// <summary>
    /// Adds a series.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <exception cref="ArgumentException">An axis of the series is not part of this chart.</exception>
    public void AddSeries(ChartSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!_horizontalAxes.Contains(series.XAxis))
        {
            throw new ArgumentException("The X axis is not a horizontal axis of this chart.", nameof(series));
        }

        if (!Array.Exists(_panes, pane => pane.Axes.Contains(series.YAxis)))
        {
            throw new ArgumentException("The Y axis is not in a pane of this chart.", nameof(series));
        }

        if (series.ColorAxis is { } colorAxis && !Array.Exists(_panes, pane => pane.Axes.Contains(colorAxis)))
        {
            throw new ArgumentException("The color axis is not in a pane of this chart.", nameof(series));
        }

        _series = [.. _series, series];
        _structureVersion++;
        ApplyPalette();
    }

    /// <summary>
    /// Removes a series.
    /// </summary>
    /// <param name="series">The series.</param>
    /// <returns>Whether the series was part of this chart.</returns>
    public bool RemoveSeries(ChartSeries series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (!_series.Contains(series))
        {
            return false;
        }

        _series = [.. _series.Where(item => item != series)];
        _structureVersion++;
        return true;
    }

    /// <summary>
    /// Finds all points inside an outline. Safe from any thread: it reads the state of the last frame.
    /// </summary>
    /// <param name="outline">The closed outline in device-independent units.</param>
    /// <returns>The points, one entry per series that has any.</returns>
    public IReadOnlyList<SeriesSelection> SelectPoints(ReadOnlySpan<Vector2> outline)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(outline.Length, 3);
        var result = new List<SeriesSelection>();
        if (_hitTestState is not { } state)
        {
            return result;
        }

        var pixels = new Vector2[outline.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = outline[i] * (float)state.DpiScale;
        }

        var area = SelectionArea.FromPolygon(pixels);
        for (var i = 0; i < state.Series.Length; i++)
        {
            if (!state.Series[i].IsVisible)
            {
                continue;
            }

            var indices = new List<long>();
            state.Series[i].CollectPoints(state.Frames[i], area, indices);
            if (indices.Count > 0)
            {
                result.Add(new SeriesSelection(state.Series[i], indices));
            }
        }

        return result;
    }

    /// <summary>
    /// Reads the selected points in units of the axes. Safe from any thread.
    /// </summary>
    /// <param name="selection">The selection.</param>
    /// <returns>The points that still exist.</returns>
    public IReadOnlyList<(ChartSeries Series, double X, double Y)> ReadPoints(IReadOnlyList<SeriesSelection> selection) // NOSONAR: public API of the instance
    {
        ArgumentNullException.ThrowIfNull(selection);
        var points = new List<(ChartSeries Series, double X, double Y)>();
        foreach (var item in selection)
        {
            foreach (var index in item.Indices)
            {
                if (item.Series.TryReadPoint(index, out var x, out var y))
                {
                    points.Add((item.Series, x, y));
                }
            }
        }

        return points;
    }

    /// <summary>
    /// Finds the nearest point of each series. Safe from any thread.
    /// </summary>
    /// <param name="position">The position in device-independent units.</param>
    /// <param name="maximumDistance">The largest distance in device-independent units.</param>
    /// <param name="mode">What counts as nearest.</param>
    /// <param name="hits">Receives one point per series that has one close enough.</param>
    public void HitTestAll(Vector2 position, double maximumDistance, HitTestMode mode, List<HitResult> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (_hitTestState is not { } state)
        {
            return;
        }

        var pixel = position * (float)state.DpiScale;
        for (var i = 0; i < state.Series.Length; i++)
        {
            if (TryFind(state, i, pixel, maximumDistance * state.DpiScale, mode, out var hit))
            {
                hits.Add(hit);
            }
        }
    }

    /// <summary>
    /// Finds the nearest point of all series. Safe from any thread: it reads the state of the last frame.
    /// </summary>
    /// <param name="position">The position in device-independent units.</param>
    /// <param name="maximumDistance">The largest distance in device-independent units.</param>
    /// <param name="mode">What counts as nearest.</param>
    /// <param name="hit">Receives the point.</param>
    /// <returns>Whether a point is close enough.</returns>
    public bool TryHitTest(Vector2 position, double maximumDistance, HitTestMode mode, out HitResult hit)
    {
        hit = default;
        if (_hitTestState is not { } state)
        {
            return false;
        }

        var pixel = position * (float)state.DpiScale;
        var found = false;
        for (var i = 0; i < state.Series.Length; i++)
        {
            if (TryFind(state, i, pixel, maximumDistance * state.DpiScale, mode, out var candidate) && (!found || candidate.Distance < hit.Distance))
            {
                hit = candidate;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Requests a new frame. Safe from any thread.
    /// </summary>
    public void Invalidate() => Volatile.Write(ref _invalidated, 1);

    /// <summary>
    /// Renders one frame on the calling thread. Without a scheduler, this thread owns the device.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="target">A render target of this device.</param>
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(target);

        if (_themeOverride is { } overrideTheme)
        {
            Theme = overrideTheme;
        }

        Group?.OnFrameStarting(this);
        Animator.Tick();

        var frameStart = Stopwatch.GetTimestamp();
        var drawCallsBefore = device.DrawCallCount;
        var uploadsBefore = device.UploadedByteCount;

        // The versions come first: a change during the frame then leads to another frame, none gets lost.
        Volatile.Write(ref _invalidated, 0);
        _renderedStructure = _structureVersion;
        _renderedOverlay = _overlayVersion;
        _renderedOverlayVersions = SumOverlayVersions();
        _renderedSeries = SumSeriesVersions();

        foreach (var item in _series)
        {
            item.TakeSnapshot();
        }

        FitAxes();
        _renderedAxes = SumAxisVersions();

        if (_layoutStructure != _renderedStructure || _layoutAxes != _renderedAxes || _renderedSize != target.Size || _renderedScale != target.DpiScale)
        {
            Arrange(target);
        }

        var prepareStart = Stopwatch.GetTimestamp();
        var arrangeTicks = prepareStart - frameStart;
        PrepareSeries(device);

        // Preparing can reveal new extremes. The axes then differ and lead to one more frame.
        FitAxes();

        if (_showDiagnostics && frameStart >= _diagnostics.NextUpdate)
        {
            UpdateDiagnostics(device, target.DpiScale, frameStart);
        }

        var atlas = _textEngine.GetAtlas(device);
        var drawStart = Stopwatch.GetTimestamp();
        using var frame = device.BeginFrame(target);

        // Background pass.
        frame.Clear(_style.BackgroundColor);
        FillPaneBackgrounds(frame);
        Fill(frame, _bandRects, _style.BandColor);

        // Grid pass.
        Fill(frame, _minorGridRects, _style.MinorGridColor);
        Fill(frame, _gridRects, _style.MajorGridColor);
        Fill(frame, _paneBorderRects, _style.BorderColor);

        // Series pass.
        var points = DrawSeries(frame);

        // The effects pass is still empty. The overlay pass holds the tick marks and the zoom preview.
        Fill(frame, _tickRects, _tickColor);
        foreach (var (bounds, top, bottom) in CollectionsMarshal.AsSpan(_colorBars))
        {
            frame.FillLinearGradient(bounds, top, bottom, vertical: true);
        }

        DrawZoomRectangle(frame, target.DpiScale);
        DrawOverlays(device, frame, target.DpiScale, target.Size.Width / target.DpiScale);

        DrawText(device, frame, atlas);
        var submitStart = Stopwatch.GetTimestamp();
        frame.Submit();
        if (MeasuresPhases)
        {
            var end = Stopwatch.GetTimestamp();
            _phases += new FramePhases(arrangeTicks, drawStart - prepareStart, submitStart - drawStart, end - submitStart);
        }


        if (_showDiagnostics)
        {
            _diagnostics.Record(frameStart, Stopwatch.GetTimestamp() - frameStart, device.DrawCallCount - drawCallsBefore, device.UploadedByteCount - uploadsBefore);
        }

        DrawnPointCount = points;
        Interlocked.Increment(ref _frameCount);
    }

    private void PrepareSeries(IRenderDevice device)
    {
        var series = _series;
        if (_lastDevice != device)
        {
            for (var i = 0; _lastDevice is not null && i < series.Length; i++)
            {
                series[i].ReleaseResources();
            }

            _lastDevice = device;
        }

        for (var i = 0; i < series.Length; i++)
        {
            series[i].Prepare(device, _seriesFrames[i]);
        }
    }

    private long DrawSeries(ICommandList frame)
    {
        var series = _series;
        var points = 0L;
        for (var i = 0; i < series.Length; i++)
        {
            if (series[i].IsVisible)
            {
                series[i].Draw(frame, _seriesFrames[i]);
                points += series[i].DrawnPointCount;
            }
        }

        return points;
    }

    // The placeholders stand in for vertical titles until there is rotated text.
    private void DrawText(IRenderDevice device, ICommandList frame, IGpuTexture atlas)
    {
        frame.DrawGlyphs(atlas, _glyphs.AsSpan(0, _glyphCount));
        if (_drawsLastValues && _currentLayout is { } layout)
        {
            _lastValues.Draw(_canvas, layout, _series);
        }

        if (_usedCanvas)
        {
            // Overlays may have asked for new glyphs, which reach the texture only with this call.
            frame.DrawGlyphs(_textEngine.GetAtlas(device), _canvas.Glyphs);
        }

        if (_showDiagnostics && _diagnosticGlyphCount > 0)
        {
            frame.FillRectangle(_diagnosticBounds, _style.BackgroundColor with { A = 225 });
            frame.DrawGlyphs(atlas, _diagnosticGlyphs.AsSpan(0, _diagnosticGlyphCount));
        }
    }

    /// <summary>
    /// Tests whether anything changed since the last frame.
    /// </summary>
    internal bool HasChanges(IRenderTarget target) =>
        Volatile.Read(ref _invalidated) != 0
        || Animator.IsActive
        || _renderedStructure != _structureVersion
        || _renderedOverlay != _overlayVersion
        || _renderedOverlayVersions != SumOverlayVersions()
        || (_showDiagnostics && Stopwatch.GetTimestamp() >= _diagnostics.NextUpdate)
        || _renderedAxes != SumAxisVersions()
        || _renderedSeries != SumSeriesVersions()
        || _renderedSize != target.Size
        || _renderedScale != target.DpiScale;

    private static bool TryFind(HitTestState state, int index, Vector2 pixel, double limit, HitTestMode mode, out HitResult hit)
    {
        hit = default;
        ref readonly var frame = ref state.Frames[index];
        var bounds = frame.Bounds;
        if (pixel.X < bounds.X || pixel.X > bounds.X + bounds.Width || pixel.Y < bounds.Y || pixel.Y > bounds.Y + bounds.Height)
        {
            return false;
        }

        var series = state.Series[index];
        if (!series.IsVisible || !series.TryFindPoint(frame, pixel, limit, mode, out var point, out var x, out var y))
        {
            return false;
        }

        var dx = frame.X.ToPixel(x) - pixel.X;
        var dy = mode == HitTestMode.NearestX ? 0 : frame.Y.ToPixel(y) - pixel.Y;
        hit = new HitResult(series, point, x, y, Math.Sqrt((dx * dx) + (dy * dy)) / state.DpiScale);
        if (series.TryReadValue(point, out var value))
        {
            hit = hit with { Value = value };
        }

        return true;
    }

    private static void Fit(AxisBase axis, DoubleRange range, double padding)
    {
        if (range.IsEmpty)
        {
            return;
        }

        if (axis is DateTimeAxis time)
        {
            var origin = time.OriginTicks;
            time.FitToTime(origin + (long)range.Min, origin + (long)range.Max, padding);
        }
        else
        {
            axis.FitTo(range, padding);
        }
    }

    private static void Fill(ICommandList frame, List<PixelRect> rectangles, Rgba8 color)
    {
        if (color.A == 0)
        {
            return;
        }

        foreach (var rectangle in CollectionsMarshal.AsSpan(rectangles))
        {
            frame.FillRectangle(rectangle, color);
        }
    }

    private void UpdateDiagnostics(IRenderDevice device, double scale, long now)
    {
        var sourcePoints = 0L;
        foreach (var series in _series)
        {
            sourcePoints += series.SourcePointCount;
        }

        var lines = _diagnosticLines;
        lines.Clear();
        _diagnostics.Update(now, DrawnPointCount, sourcePoints, device.Capabilities.BackendName, lines);
        foreach (var series in _series)
        {
            series.WriteDiagnostics(lines);
            if (series.DiagnosticWarning is { } warning)
            {
                lines.Add(warning);
            }
        }

        var plot = CurrentLayout?.PlotArea ?? default;
        var lineHeight = _textEngine.GetLineHeight(_labelSize);
        var width = 0f;
        _diagnosticGlyphCount = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var required = _diagnosticGlyphCount + lines[i].Length;
            if (required > _diagnosticGlyphs.Length)
            {
                Array.Resize(ref _diagnosticGlyphs, required * 2);
            }

            var baseline = plot.Y + DiagnosticsMargin + (lineHeight * (i + 1));
            var position = new Vector2((float)((plot.X + (DiagnosticsMargin * 2)) * scale), (float)(baseline * scale));
            _diagnosticGlyphCount += _textEngine.Layout(lines[i], position, (float)(_labelSize * scale), _labelColor, _diagnosticGlyphs.AsSpan(_diagnosticGlyphCount));
            width = Math.Max(width, _textEngine.MeasureWidth(lines[i], _labelSize));
        }

        _diagnosticBounds = new DipRect(
            plot.X + DiagnosticsMargin,
            plot.Y + DiagnosticsMargin,
            width + (DiagnosticsMargin * 2),
            (lineHeight * lines.Count) + DiagnosticsMargin).ToPixels(scale);
    }

    private long SumOverlayVersions()
    {
        var sum = 0L;
        foreach (var overlay in _overlays)
        {
            sum += overlay.Version + overlay.ExternalVersion;
        }

        return sum;
    }

    private void DrawOverlays(IRenderDevice device, ICommandList frame, double scale, double chartWidth)
    {
        _usedCanvas = false;
        _drawsLastValues = false;
        if (_currentLayout is not { } layout)
        {
            return;
        }

        var hasBreaks = Tachyon.Charts.Core.Overlays.AxisBreakMarks.Any(layout);
        var hasLabels = Tachyon.Charts.Core.Overlays.ContourLabels.Any(_series);
        var hasLastValues = Tachyon.Charts.Core.Overlays.LastValueMarks.Any(_series);
        if (_overlays.Length == 0 && !hasBreaks && !hasLabels && !hasLastValues)
        {
            return;
        }

        _canvas.Begin(device, frame, layout, scale, chartWidth, _tickColor, _labelColor, _style.BackgroundColor, _labelSize);
        _canvas.ChartHeight = _renderedSize.Height / scale;
        _usedCanvas = true;
        if (hasBreaks)
        {
            Tachyon.Charts.Core.Overlays.AxisBreakMarks.Draw(_canvas, layout);
        }

        if (hasLabels)
        {
            Tachyon.Charts.Core.Overlays.ContourLabels.Draw(_canvas, layout, _series);
        }

        // The marks of the last values come after the labels of the axes, which they cover; see DrawText.
        _drawsLastValues = hasLastValues;

        foreach (var overlay in _overlays)
        {
            overlay.Draw(_canvas);
            _canvas.Clip = null;
        }
    }

    private void DrawZoomRectangle(ICommandList frame, double scale)
    {
        if (_zoomRectangle is not { } area)
        {
            return;
        }

        var pixels = area.ToPixels(scale);
        var border = _style.ZoomRectangleBorderColor;
        frame.FillRectangle(pixels, _style.ZoomRectangleColor);
        frame.FillRectangle(new PixelRect(pixels.X, pixels.Y, pixels.Width, 1), border);
        frame.FillRectangle(new PixelRect(pixels.X, pixels.Y + pixels.Height - 1, pixels.Width, 1), border);
        frame.FillRectangle(new PixelRect(pixels.X, pixels.Y, 1, pixels.Height), border);
        frame.FillRectangle(new PixelRect(pixels.X + pixels.Width - 1, pixels.Y, 1, pixels.Height), border);
    }

    private void FillPaneBackgrounds(ICommandList frame)
    {
        var start = _style.PlotGradientStart;
        var end = _style.PlotGradientEnd;
        foreach (var rectangle in CollectionsMarshal.AsSpan(_paneRects))
        {
            if (start == end)
            {
                frame.FillRectangle(rectangle, start);
            }
            else
            {
                frame.FillLinearGradient(rectangle, start, end, vertical: true);
            }
        }
    }

    private void ApplyPalette()
    {
        if (_theme?.Palette is not { Length: > 0 } palette)
        {
            return;
        }

        var colors = new Rgba8[palette.Length];
        for (var i = 0; i < colors.Length; i++)
        {
            colors[i] = palette[i].Value;
        }

        foreach (var series in _series)
        {
            series.OnPalette(colors);
        }
    }

    private void ApplyTheme(Theme? theme)
    {
        var labelSize = DefaultLabelSize;
        if (theme?.Text?.Size is double size)
        {
            if (!double.IsFinite(size) || size <= 0 || size > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(theme), size, "The theme text size must be positive and finite.");
            }

            labelSize = (float)size;
        }

        if (theme is null)
        {
            _style = ChartSurfaceStyle.Default;
            _tickColor = _defaultTick;
            _labelColor = _defaultText;
            _titleColor = _defaultText;
        }
        else
        {
            _style = theme.ToSurfaceStyle();
            _tickColor = theme.Axis?.Tick?.Value ?? _defaultTick;
            _labelColor = theme.Axis?.Label?.Value ?? _defaultText;
            _titleColor = theme.Axis?.Title?.Value ?? _defaultText;
        }

        _theme = theme;
        var glow = theme?.Effects?.Glow;
        _glowRadius = glow?.Enabled == true ? glow.Radius ?? 0 : 0;
        _glowIntensity = _glowRadius > 0 ? glow!.Intensity ?? 0 : 0;
        _labelSize = labelSize;
        _titleSize = theme?.Text?.Size is null ? DefaultTitleSize : labelSize + 3;
        _measurer.Size = _labelSize;
    }

    private void FitAxes()
    {
        foreach (var axis in _axes)
        {
            if (axis.IsAutoRange)
            {
                FitAxis(axis);
            }
        }
    }

    private void FitAxis(AxisBase axis)
    {
        var range = DoubleRange.Empty;
        var isFlush = axis.IsHorizontal || axis is ColorAxis;
        foreach (var series in _series)
        {
            if (!series.IsVisible)
            {
                continue;
            }

            if (axis is ColorAxis)
            {
                range = series.ColorAxis == axis && series.TryGetValueRange(out var values) ? range.Union(values) : range;
            }
            else
            {
                // A raster fills its area, a margin would only show a gap.
                isFlush |= TryUnionData(series, axis, ref range) && series.ColorAxis is not null;
            }
        }

        Fit(axis, range, isFlush ? 0 : AutoRangePadding);
    }

    private static bool TryUnionData(ChartSeries series, AxisBase axis, ref DoubleRange range)
    {
        if ((series.XAxis != axis && series.YAxis != axis) || !series.TryGetDataRange(out var x, out var y))
        {
            return false;
        }

        range = range.Union(series.XAxis == axis ? x : y);
        return true;
    }

    // The versions only grow, so a sum changes whenever one of them does.
    // The labels of the axes follow the language of the charts, so a change of language lays them out anew.
    private long SumAxisVersions()
    {
        var sum = Instruments.ChartText.Version;
        foreach (var axis in _axes)
        {
            sum += axis.Version;
        }

        return sum;
    }

    private long SumSeriesVersions()
    {
        var sum = 0L;
        foreach (var series in _series)
        {
            sum += series.Version + series.StateVersion;
        }

        return sum;
    }

    private void Arrange(IRenderTarget target)
    {
        var scale = target.DpiScale;
        var size = target.Size.ToDips(scale);
        if (Group is { } group)
        {
            // What the chart needs by itself, before the group asks for more.
            var own = ChartLayout.Arrange(size, _title, _horizontalAxes, _panes, _measurer, WithRoom(_options) with { MinimumPlotLeft = 0, MinimumPlotRight = 0 });
            group.OnMeasured(this, own.PlotArea.X, size.Width - own.PlotArea.Right);
            _renderedStructure = _structureVersion;
        }

        var layout = ChartLayout.Arrange(size, _title, _horizontalAxes, _panes, _measurer, WithRoom(_options));

        _paneRects.Clear();
        _bandRects.Clear();
        _minorGridRects.Clear();
        _gridRects.Clear();
        _paneBorderRects.Clear();
        _tickRects.Clear();
        _colorBars.Clear();
        _glyphCount = 0;

        foreach (var pane in layout.Panes)
        {
            var pixels = pane.ToPixels(scale);
            _paneRects.Add(pixels);
            AddBorder(pixels);
        }

        if (!string.IsNullOrEmpty(_title))
        {
            var left = layout.Title.X + ((layout.Title.Width - _textEngine.MeasureWidth(_title, _titleSize)) / 2);
            AddText(_title, left, layout.Title.Bottom - 2, _titleSize, _titleColor, scale);
        }

        foreach (var placement in layout.Axes)
        {
            ArrangeAxis(placement, layout, scale);
        }

        ArrangeSeries(layout, scale);

        Volatile.Write(ref _currentLayout, layout);
        _layoutStructure = _renderedStructure;
        _layoutAxes = _renderedAxes;
        _renderedSize = target.Size;
        _renderedScale = scale;
    }

    private void ArrangeAxis(AxisPlacement placement, ChartLayoutResult layout, double scale)
    {
        var axis = placement.Axis;
        if (axis.IsHidden)
        {
            return;
        }

        var bounds = placement.Bounds;
        var isHorizontal = axis.IsHorizontal;
        var origin = Origin(axis, layout);
        if (axis.CrossesAt is { } crossing)
        {
            bounds = Crossing(axis, crossing, bounds, layout, scale);
        }

        var length = isHorizontal ? bounds.Width : bounds.Height;
        var transform = axis.CreateTransform(isHorizontal ? bounds.X : bounds.Y, length);
        var drawsGrid = OwnsGrid(axis);

        // Ticks and labels start after a color bar.
        if (axis is ColorAxis colorAxis)
        {
            var band = axis.BandThickness;
            ArrangeColorBar(colorAxis, new DipRect(axis.Position == AxisPosition.Left ? bounds.Right - band : bounds.X, bounds.Y, band, bounds.Height), scale);
            bounds = axis.Position == AxisPosition.Left ? bounds with { Width = bounds.Width - band } : bounds with { X = bounds.X + band, Width = bounds.Width - band };
        }

        var ticks = axis.GenerateTicks(length, _measurer);
        foreach (var tick in ticks.Major)
        {
            var position = transform.ToPixel(tick.Value);

            // Where both axes cross, only the horizontal one writes its label.
            var label = Math.Abs(position - origin) < 3 ? string.Empty : tick.Label;
            ArrangeTick(axis, label, position, bounds, scale);
            if (drawsGrid)
            {
                AddGridLine(_gridRects, position, isHorizontal, bounds, layout, scale);
            }
        }

        if (drawsGrid)
        {
            AddMinorGrid(ticks, transform, isHorizontal, bounds, layout, scale);
        }

        if (axis.Title is { } title)
        {
            ArrangeAxisTitle(title, axis, bounds, scale);
        }
    }

    // Moves an axis onto the line through the plot where the other axis has the value, and draws that line.
    private DipRect Crossing(AxisBase axis, double value, DipRect bounds, ChartLayoutResult layout, double scale)
    {
        var plot = layout.PlotArea;
        if (Other(axis, layout) is not { } other)
        {
            return bounds;
        }

        var otherBounds = other.Bounds;
        var transform = other.Axis.CreateTransform(axis.IsHorizontal ? otherBounds.Y : otherBounds.X, axis.IsHorizontal ? otherBounds.Height : otherBounds.Width);
        var at = transform.ToPixel(value);
        if (axis.IsHorizontal)
        {
            at = double.IsFinite(at) ? Math.Clamp(at, plot.Y, plot.Bottom) : plot.Bottom;
            _tickRects.Add(new DipRect(bounds.X, at, bounds.Width, 1).ToPixels(scale));
            return axis.Position == AxisPosition.Top ? bounds with { Y = at - bounds.Height } : bounds with { Y = at };
        }

        at = double.IsFinite(at) ? Math.Clamp(at, plot.X, plot.Right) : plot.X;
        _tickRects.Add(new DipRect(at, bounds.Y, 1, bounds.Height).ToPixels(scale));
        return axis.Position == AxisPosition.Right ? bounds with { X = at } : bounds with { X = at - bounds.Width };
    }

    // The place of a horizontal axis that runs through the plot, for a vertical axis that must leave its label out there.
    private static double Origin(AxisBase axis, ChartLayoutResult layout)
    {
        if (axis.IsHorizontal || Other(axis, layout) is not { Axis.CrossesAt: { } value } other)
        {
            return double.NaN;
        }

        var axes = layout.Axes;
        for (var i = 0; i < axes.Count; i++)
        {
            if (axes[i].Axis == axis)
            {
                return axis.CreateTransform(axes[i].Bounds.Y, axes[i].Bounds.Height).ToPixel(value);
            }
        }

        return double.NaN;
    }

    // The first axis of the other direction: the first horizontal one, or the first vertical one of the pane.
    private static AxisPlacement? Other(AxisBase axis, ChartLayoutResult layout)
    {
        var axes = layout.Axes;
        for (var i = 0; i < axes.Count; i++)
        {
            if (axes[i].Axis.IsHorizontal != axis.IsHorizontal && axes[i].Axis is not ColorAxis)
            {
                return axes[i];
            }
        }

        return null;
    }

    private void ArrangeTick(AxisBase axis, string label, double position, DipRect bounds, double scale)
    {
        var tickLength = _options.TickLength;
        var labelDistance = tickLength + _options.LabelGap;
        var width = _measurer.MeasureWidth(label);
        var (mark, labelX, baseline) = axis.Position switch
        {
            AxisPosition.Bottom => (new DipRect(position, bounds.Y, 1, tickLength), position - (width / 2), bounds.Y + labelDistance + _labelSize),
            AxisPosition.Top => (new DipRect(position, bounds.Bottom - tickLength, 1, tickLength), position - (width / 2), bounds.Bottom - labelDistance - 1),
            AxisPosition.Left => (new DipRect(bounds.Right - tickLength, position, tickLength, 1), bounds.Right - labelDistance - width, position + (_labelSize * 0.36)),
            _ => (new DipRect(bounds.X, position, tickLength, 1), bounds.X + labelDistance, position + (_labelSize * 0.36)),
        };

        _tickRects.Add(mark.ToPixels(scale));
        if (axis.LabelRotation != 0 && axis.IsHorizontal)
        {
            // The text ends at its tick and turns around that point.
            var pivot = axis.Position == AxisPosition.Bottom ? bounds.Y + labelDistance : bounds.Bottom - labelDistance;
            AddRotatedText(label, position, pivot, width, axis.LabelRotation, scale);
        }
        else
        {
            AddText(label, labelX, baseline, _labelSize, _labelColor, scale);
        }
    }

    private void ArrangeColorBar(ColorAxis axis, DipRect bar, double scale)
    {
        for (var i = 0; i < ColorBarSegments; i++)
        {
            // The bar is linear in the scale of the axis, like the raster, and the top is the end of the map.
            var top = 1 - (i / (double)ColorBarSegments);
            var bottom = 1 - ((i + 1) / (double)ColorBarSegments);
            var segment = new DipRect(bar.X, bar.Y + ((1 - top) * bar.Height), bar.Width, bar.Height / ColorBarSegments);
            _colorBars.Add((segment.ToPixels(scale), axis.Colormap.Sample(top), axis.Colormap.Sample(bottom)));
        }
    }

    private void ArrangeAxisTitle(string title, AxisBase axis, DipRect bounds, double scale)
    {
        var width = _measurer.MeasureWidth(title);
        switch (axis.Position)
        {
            case AxisPosition.Bottom:
                AddText(title, bounds.X + ((bounds.Width - width) / 2), bounds.Bottom - 2, _labelSize, _titleColor, scale);
                break;
            case AxisPosition.Top:
                AddText(title, bounds.X + ((bounds.Width - width) / 2), bounds.Y + _labelSize, _labelSize, _titleColor, scale);
                break;
            case AxisPosition.Left:
                AddVerticalTitle(title, bounds.X + AxisTitleInset, bounds.Y + (bounds.Height / 2), width, -90, scale);
                break;
            default:
                AddVerticalTitle(title, bounds.Right - AxisTitleInset, bounds.Y + (bounds.Height / 2), width, 90, scale);
                break;
        }
    }

    // The first horizontal axis and the first axis of each pane draw the grid.
    private bool OwnsGrid(AxisBase axis)
    {
        if (axis.IsHorizontal)
        {
            return _horizontalAxes[0] == axis;
        }

        return Array.Exists(_panes, pane => pane.Axes.Count > 0 && pane.Axes[0] == axis);
    }

    private static void AddGridLine(
        List<PixelRect> rectangles,
        double position,
        bool isHorizontal,
        DipRect axisBounds,
        ChartLayoutResult layout,
        double scale)
    {
        foreach (var pane in layout.Panes)
        {
            if (isHorizontal)
            {
                rectangles.Add(new DipRect(position, pane.Y, 1, pane.Height).ToPixels(scale));
            }
            else if (pane.Y >= axisBounds.Y - 0.5 && pane.Bottom <= axisBounds.Bottom + 0.5)
            {
                rectangles.Add(new DipRect(pane.X, position, pane.Width, 1).ToPixels(scale));
            }
        }
    }

    private void AddMinorGrid(TickSet ticks, in LinearAxisTransform transform, bool isHorizontal, DipRect bounds, ChartLayoutResult layout, double scale)
    {
        foreach (var value in ticks.Minor)
        {
            AddGridLine(_minorGridRects, transform.ToPixel(value), isHorizontal, bounds, layout, scale);
        }

        if (isHorizontal)
        {
            AddBands(ticks.Major, transform, layout, scale);
        }
    }

    private void AddBands(
        IReadOnlyList<AxisTick> ticks,
        LinearAxisTransform transform,
        ChartLayoutResult layout,
        double scale)
    {
        if (_style.BandColor.A == 0)
        {
            return;
        }

        for (var i = 0; i + 1 < ticks.Count; i += 2)
        {
            var first = transform.ToPixel(ticks[i].Value);
            var second = transform.ToPixel(ticks[i + 1].Value);
            var left = Math.Min(first, second);
            var width = Math.Abs(second - first);
            foreach (var pane in layout.Panes)
            {
                _bandRects.Add(new DipRect(left, pane.Y, width, pane.Height).ToPixels(scale));
            }
        }
    }

    private void AddBorder(PixelRect pane)
    {
        _paneBorderRects.Add(new PixelRect(pane.X, pane.Y, pane.Width, 1));
        _paneBorderRects.Add(new PixelRect(pane.X, pane.Y + pane.Height - 1, pane.Width, 1));
        _paneBorderRects.Add(new PixelRect(pane.X, pane.Y, 1, pane.Height));
        _paneBorderRects.Add(new PixelRect(pane.X + pane.Width - 1, pane.Y, 1, pane.Height));
    }

    private void ArrangeSeries(ChartLayoutResult layout, double scale)
    {
        // A new array each time: hit tests on other threads keep reading the one they have.
        var series = _series;
        _seriesFrames = new SeriesFrame[series.Length];
        for (var i = 0; i < series.Length; i++)
        {
            var yAxis = series[i].YAxis;
            var paneIndex = Array.FindIndex(_panes, pane => pane.Axes.Contains(yAxis));
            var bounds = layout.Panes[paneIndex].ToPixels(scale);
            _seriesFrames[i] = new SeriesFrame(
                bounds,
                series[i].XAxis.CreateTransform(bounds.X, bounds.Width),
                yAxis.CreateTransform(bounds.Y, bounds.Height),
                scale,
                _effectsEnabled ? _glowRadius : 0,
                _glowIntensity);
        }

        _hitTestState = new HitTestState(series, _seriesFrames, scale);
    }

    private sealed record HitTestState(ChartSeries[] Series, SeriesFrame[] Frames, double DpiScale);

    private void AddVerticalTitle(string text, double x, double y, double width, double degrees, double scale)
    {
        var first = _glyphCount;
        AddText(text, x - (width / 2), y + (_labelSize * 0.36), _labelSize, _titleColor, scale);
        var pivot = new Vector2((float)(x * scale), (float)(y * scale));
        var radians = (float)(degrees * Math.PI / 180);
        for (var i = first; i < _glyphCount; i++)
        {
            _glyphs[i] = _glyphs[i] with { Rotation = radians, Pivot = pivot };
        }
    }

    private void AddRotatedText(string text, double x, double y, double width, double degrees, double scale)
    {
        var first = _glyphCount;
        var inwards = degrees < 0 ? 1 : -1;
        AddText(text, x - width, y + (_labelSize * 0.36 * inwards), _labelSize, _labelColor, scale);
        var pivot = new Vector2((float)(x * scale), (float)(y * scale));
        var radians = (float)(degrees * Math.PI / 180);
        for (var i = first; i < _glyphCount; i++)
        {
            _glyphs[i] = _glyphs[i] with { Rotation = radians, Pivot = pivot };
        }
    }

    private void AddText(string text, double x, double baseline, float size, Rgba8 color, double scale)
    {
        if (_glyphCount + text.Length > _glyphs.Length)
        {
            Array.Resize(ref _glyphs, Math.Max(_glyphs.Length * 2, _glyphCount + text.Length));
        }

        var position = new Vector2((float)(x * scale), (float)(baseline * scale));
        _glyphCount += _textEngine.Layout(text, position, (float)(size * scale), color, _glyphs.AsSpan(_glyphCount));
    }
}
