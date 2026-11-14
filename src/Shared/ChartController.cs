#if !AVALONIA
using System.Windows.Threading;
#endif
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Export;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;

#if AVALONIA
namespace Tachyon.Charts.Avalonia;
#else
namespace Tachyon.Charts.Wpf;
#endif

/// <summary>
/// Turns the declared elements of a chart into a running <see cref="ChartSurface"/> and keeps both in
/// step. Needs no window, which keeps the forwarding of properties testable. Both adapters compile
/// this file, each with its own element types.
/// </summary>
internal sealed class ChartController : IChartElementHost, IDisposable
{
    private readonly RenderScheduler _scheduler;
    private readonly ITextEngine _textEngine;
    private readonly Action<Action> _enqueue;
    private readonly Action<Action> _enqueueIdle;
    private readonly IReadOnlyList<ChartAxis> _xAxes;
    private readonly IReadOnlyList<ChartAxis> _yAxes;
    private readonly IReadOnlyList<ChartSeriesElement> _series;
    private readonly IReadOnlyList<ChartModifierElement> _modifiers;
    private readonly IReadOnlyList<ChartAnnotationElement> _annotations;
    private readonly List<Core.Overlays.ChartOverlay> _builtAnnotations = [];
    private readonly Action _publish;
    private readonly List<ChartSeries> _builtSeries = [];
    private readonly Dictionary<ChartAxis, AxisRangeState> _published = [];

    private IRenderTarget? _target;
    private ChartSurface? _surface;
    private string? _title;
    private Theme? _theme;
    private string? _groupName;
    private ChartGroup? _group;
    private int _isPublishQueued;
    private bool _isRebuildQueued;
    private bool _isSeriesUpdateQueued;
    private bool _isDisposed;

#if !AVALONIA
    public ChartController(
        RenderScheduler scheduler,
        ITextEngine textEngine,
        Dispatcher dispatcher,
        IReadOnlyList<ChartAxis> xAxes,
        IReadOnlyList<ChartAxis> yAxes,
        IReadOnlyList<ChartSeriesElement> series,
        IReadOnlyList<ChartModifierElement> modifiers,
        IReadOnlyList<ChartAnnotationElement> annotations)
        : this(
            scheduler,
            textEngine,
            action => dispatcher.BeginInvoke(DispatcherPriority.Loaded, action),
            action => dispatcher.BeginInvoke(DispatcherPriority.Background, action),
            xAxes,
            yAxes,
            series,
            modifiers,
            annotations)
    {
    }

#endif
    /// <summary>Initializes a new instance of the <see cref="ChartController"/> class.</summary>
    /// <param name="scheduler">The render thread.</param>
    /// <param name="textEngine">The text engine.</param>
    /// <param name="enqueue">Runs an action on the UI thread after the current changes.</param>
    /// <param name="enqueueIdle">Runs an action on the UI thread once input is handled.</param>
    /// <param name="xAxes">The horizontal axes.</param>
    /// <param name="yAxes">The vertical axes.</param>
    /// <param name="series">The series.</param>
    /// <param name="modifiers">The interactions.</param>
    /// <param name="annotations">The annotations.</param>
    public ChartController(
        RenderScheduler scheduler,
        ITextEngine textEngine,
        Action<Action> enqueue,
        Action<Action> enqueueIdle,
        IReadOnlyList<ChartAxis> xAxes,
        IReadOnlyList<ChartAxis> yAxes,
        IReadOnlyList<ChartSeriesElement> series,
        IReadOnlyList<ChartModifierElement> modifiers,
        IReadOnlyList<ChartAnnotationElement> annotations)
    {
        _scheduler = scheduler;
        _textEngine = textEngine;
        _enqueue = enqueue;
        _enqueueIdle = enqueueIdle;
        _xAxes = xAxes;
        _yAxes = yAxes;
        _series = series;
        _modifiers = modifiers;
        _annotations = annotations;
        _publish = PublishOnUiThread;
        _scheduler.PassCompleted += OnPassCompleted;
    }

    /// <summary>Gets or sets what creates the overlays of a chart from its description.</summary>
    public IReadOnlyList<Func<ChartSurface, Core.Overlays.ChartOverlay>> OverlayFactories { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the X axis runs down the side and the values along the bottom.</summary>
    public bool IsXDown { get; set; }

    private TextFont _titleFont;
    private TextFont _axisFont;

    /// <summary>Sets the fonts of the title and of the axes.</summary>
    /// <param name="title">The font of the title.</param>
    /// <param name="axes">The font of the labels and titles of the axes.</param>
    public void SetFonts(TextFont title, TextFont axes)
    {
        (_titleFont, _axisFont) = (title, axes);
        if (_surface is { } surface)
        {
            Post(() => (surface.TitleFont, surface.AxisFont) = (title, axes));
        }
    }

    /// <summary>Gets the input pipeline, used on the UI thread.</summary>
    public ModifierPipeline Pipeline { get; } = new();

    /// <summary>Gets the running chart.</summary>
    public ChartSurface? Surface => _surface;

    /// <summary>Gets or sets a value indicating whether the diagnostics overlay is shown, also for a chart that starts later.</summary>
    public bool ShowsDiagnostics
    {
        get => _surface?.ShowDiagnostics ?? field;
        set
        {
            field = value;
            Pipeline.Interaction?.ShowDiagnostics(value);
        }
    }

    /// <summary>Starts the chart on a target.</summary>
    public void Start(IRenderTarget target, string? title, Theme? theme)
    {
        _target = target;
        _title = title;
        _theme = theme;
        Rebuild();
    }

    /// <summary>Moves the chart to another target, for example after a resize of an offscreen target.</summary>
    public void ReplaceTarget(IRenderTarget target)
    {
        var surface = _surface;
        _target = target;
        _scheduler.Invoke(() =>
        {
            if (surface is not null)
            {
                _scheduler.Detach(surface);
                _scheduler.Attach(surface, target);
            }
        });
    }

    public void Post(Action change) => _scheduler.Post(change);

    /// <summary>Runs an export on the render thread, without blocking the caller.</summary>
    /// <typeparam name="T">The result.</typeparam>
    /// <param name="export">The export.</param>
    /// <returns>The result.</returns>
    /// <exception cref="InvalidOperationException">The chart is not shown yet.</exception>
    public Task<T> ExportAsync<T>(Func<ChartSurface, IRenderDevice, T> export)
    {
        var surface = _surface ?? throw new InvalidOperationException("The chart is not shown yet.");
        return Task.Run(() =>
        {
            var result = default(T);
            _scheduler.Invoke(() => result = export(surface, _scheduler.Device));
            return result!;
        });
    }

    /// <summary>Gets the render thread of the chart, which charts with the same renderer share.</summary>
    public RenderScheduler Scheduler => _scheduler;

    /// <summary>Sets the title.</summary>
    public void SetTitle(string? title)
    {
        _title = title;
        if (_surface is { } surface)
        {
            Post(() => surface.Title = title);
        }
    }

    /// <summary>Sets the theme.</summary>
    /// <param name="theme">The theme.</param>
    /// <param name="transition">The duration of a blend of the colors.</param>
    public void SetTheme(Theme? theme, TimeSpan transition = default)
    {
        _theme = theme;
        if (_surface is { } surface)
        {
            Post(() => surface.TransitionTheme(theme, transition));
        }
    }

    /// <summary>Sets the group of the chart.</summary>
    public void SetGroup(string? name)
    {
        _groupName = name;
        if (_surface is { } surface)
        {
            _scheduler.Invoke(() => JoinGroup(surface));
            ConnectCrosshairs();
        }
    }

    public void OnAxesChanged()
    {
        if (_surface is null || _isRebuildQueued)
        {
            return;
        }

        _isRebuildQueued = true;
        _enqueue(() =>
        {
            _isRebuildQueued = false;
            if (!_isDisposed)
            {
                Rebuild();
            }
        });
    }

    public void OnSeriesChanged()
    {
        if (_surface is null || _isSeriesUpdateQueued)
        {
            return;
        }

        _isSeriesUpdateQueued = true;
        _enqueue(() =>
        {
            _isSeriesUpdateQueued = false;
            if (!_isDisposed)
            {
                ReplaceSeries();
            }
        });
    }

    public void OnAnnotationsChanged()
    {
        if (_surface is not { } surface)
        {
            return;
        }

        var created = BuildAnnotations();
        var old = _builtAnnotations.ToArray();
        _builtAnnotations.Clear();
        _builtAnnotations.AddRange(created);
        Post(() =>
        {
            foreach (var annotation in old)
            {
                surface.RemoveOverlay(annotation);
            }

            foreach (var annotation in created)
            {
                surface.AddOverlay(annotation);
            }
        });
    }

    public void OnModifiersChanged()
    {
        Pipeline.Clear();
        foreach (var modifier in _modifiers)
        {
            Pipeline.Add(modifier.Build());
        }

        // A color bar reacts to the mouse in every chart, without being asked for.
        if (_surface is { } surface && surface.Panes.Any(pane => pane.Axes.Any(axis => axis is Core.Axes.ColorAxis))
            && !Pipeline.Modifiers.OfType<Core.Input.AxisDragModifier>().Any())
        {
            Pipeline.Add(new Core.Input.AxisDragModifier { OnlyColorAxes = true });
        }

        ConnectCrosshairs();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scheduler.PassCompleted -= OnPassCompleted;
        Pipeline.Clear();
        if (_surface is { } surface)
        {
            var axes = _xAxes.Concat(_yAxes).ToArray();
            var states = new AxisRangeState[axes.Length];
            _scheduler.Invoke(() =>
            {
                LeaveGroup(surface);
                _scheduler.Detach(surface);
                DisposeSeries();
                for (var i = 0; i < axes.Length; i++)
                {
                    states[i] = axes[i].Core is { } core ? new AxisRangeState(core.VisibleRange, core.IsAutoRange) : default;
                }
            });

            // The elements can lag behind the engine, and a restart continues from them.
            for (var i = 0; i < axes.Length; i++)
            {
                if (axes[i].Core is not null)
                {
                    axes[i].Publish(states[i].Range, states[i].IsAutoRange);
                }
            }
        }

        _surface = null;
    }

    private static AxisBase Find(IReadOnlyList<ChartAxis> axes, string? id, string kind)
    {
        if (id is null)
        {
            return axes.Count > 0 ? axes[0].Core! : throw new InvalidOperationException($"The chart has no {kind} axis.");
        }

        foreach (var axis in axes)
        {
            if (axis.Core is { } core && core.Id == id)
            {
                return core;
            }
        }

        throw new InvalidOperationException($"The chart has no {kind} axis '{id}'.");
    }

    private void JoinGroup(ChartSurface surface)
    {
        LeaveGroup(surface);
        if (_groupName is { Length: > 0 } name)
        {
            _group = ChartGroup.Get(name);
            _group.Add(surface);
        }
    }

    private void LeaveGroup(ChartSurface surface)
    {
        _group?.Remove(surface);
        _group = null;
    }

    private void ConnectCrosshairs()
    {
        if (_group is { } group)
        {
            foreach (var crosshair in Pipeline.Modifiers.OfType<Core.Input.CrosshairModifier>())
            {
                group.Connect(crosshair);
            }
        }
    }

    private void Rebuild()
    {
        if (_target is not { } target)
        {
            return;
        }

        if (_xAxes.Count == 0 || _yAxes.Count == 0)
        {
            throw new InvalidOperationException("A chart needs an X axis and a Y axis.");
        }

        var old = _surface;
        _published.Clear();
        // As a chart built without a UI: turned with the X axis down, otherwise the vertical axes in their panes.
        var xAxes = new AxisBase[_xAxes.Count];
        for (var i = 0; i < xAxes.Length; i++)
        {
            xAxes[i] = IsXDown ? _xAxes[i].BuildTurned($"x{i}", AxisPosition.Left, isX: true) : _xAxes[i].Build($"x{i}", AxisPosition.Bottom);
        }

        var yAxes = new AxisBase[_yAxes.Count];
        for (var i = 0; i < yAxes.Length; i++)
        {
            yAxes[i] = IsXDown ? _yAxes[i].BuildTurned($"y{i}", AxisPosition.Bottom, isX: false) : _yAxes[i].Build($"y{i}", AxisPosition.Left);
        }

        var colorAxes = new List<AxisBase>();
        for (var i = 0; i < _series.Count; i++)
        {
            if (_series[i].GetColorAxis($"color{i}") is { } colorAxis)
            {
                colorAxes.Add(colorAxis);
            }
        }

        var surface = IsXDown
            ? new ChartSurface(yAxes, [new LayoutPane([.. xAxes, .. colorAxes])], _textEngine) { Title = _title, Theme = _theme, IsXDown = true, TitleFont = _titleFont, AxisFont = _axisFont }
            : new ChartSurface(xAxes, Panes(yAxes, colorAxes), _textEngine) { Title = _title, Theme = _theme, TitleFont = _titleFont, AxisFont = _axisFont };
        var series = BuildSeries();
        var annotations = BuildAnnotations();
        _builtAnnotations.Clear();
        _builtAnnotations.AddRange(annotations);
        _scheduler.Invoke(() =>
        {
            if (old is not null)
            {
                LeaveGroup(old);
                _scheduler.Detach(old);
                DisposeSeries();
            }

            AddSeries(surface, series);
            foreach (var annotation in annotations)
            {
                surface.AddOverlay(annotation);
            }

            foreach (var overlay in OverlayFactories)
            {
                surface.AddOverlay(overlay(surface));
            }

            _surface = surface;
            JoinGroup(surface);
            _scheduler.Attach(surface, target);
        });

        Pipeline.Interaction = new ChartInteraction(surface, _scheduler.Post);
        if (ShowsDiagnostics)
        {
            Pipeline.Interaction.ShowDiagnostics(true);
        }

        OnModifiersChanged();
    }

    private void ReplaceSeries()
    {
        if (_surface is not { } surface)
        {
            return;
        }

        // A new color axis needs another pane, and only a new surface has that.
        for (var i = 0; i < _series.Count; i++)
        {
            if (_series[i].GetColorAxis($"color{i}") is { } colorAxis && !surface.Panes[0].Axes.Contains(colorAxis))
            {
                Rebuild();
                return;
            }
        }

        var created = BuildSeries();
        _scheduler.Invoke(() =>
        {
            foreach (var series in _builtSeries)
            {
                surface.RemoveSeries(series);
            }

            DisposeSeries();
            AddSeries(surface, created);
        });
    }

    // On the UI thread, before anything waits for the render thread: elements belong to this thread.
    // Vertical axes go into the pane they name, from the top, with the weight of the last of them; color axes join the first.
    private LayoutPane[] Panes(AxisBase[] yAxes, List<AxisBase> colorAxes)
    {
        var count = _yAxes.Count == 0 ? 1 : _yAxes.Max(axis => axis.Pane) + 1;
        var panes = new LayoutPane[count];
        for (var pane = 0; pane < count; pane++)
        {
            var axes = new List<AxisBase>();
            var weight = 1.0;
            for (var i = 0; i < yAxes.Length; i++)
            {
                if (_yAxes[i].Pane == pane)
                {
                    axes.Add(yAxes[i]);
                    weight = _yAxes[i].PaneWeight;
                }
            }

            if (pane == 0)
            {
                axes.AddRange(colorAxes);
            }

            panes[pane] = new LayoutPane(axes, weight);
        }

        return panes;
    }

    private List<ChartSeries> BuildSeries()
    {
        var created = new List<ChartSeries>(_series.Count);
        foreach (var element in _series)
        {
            var x = Find(_xAxes, element.XAxisId, "X");
            var y = Find(_yAxes, element.YAxisId, "Y");
            if (element.Build(x, y) is { } series)
            {
                // A series from code may switch these on itself; the element adds what its description asks for.
                series.ShowsLastValue |= element.ShowsLastValue;
                series.ShowsValues |= element.ShowsValues;
                created.Add(series);
            }
        }

        return created;
    }

    private List<Core.Overlays.ChartOverlay> BuildAnnotations()
    {
        var created = new List<Core.Overlays.ChartOverlay>(_annotations.Count);
        foreach (var element in _annotations)
        {
            created.Add(element.Build(Find(_xAxes, element.XAxisId, "X"), Find(_yAxes, element.YAxisId, "Y")));
        }

        return created;
    }

    private void AddSeries(ChartSurface surface, List<ChartSeries> created)
    {
        foreach (var series in created)
        {
            surface.AddSeries(series);
            _builtSeries.Add(series);
        }
    }

    private void DisposeSeries()
    {
        foreach (var series in _builtSeries)
        {
            (series as IDisposable)?.Dispose();
        }

        _builtSeries.Clear();
    }

    // On the render thread, where the axes live. UI elements get copies, bundled to one update at a time.
    private void OnPassCompleted()
    {
        if (_surface is null)
        {
            return;
        }

        var hasChanges = false;
        for (var i = 0; i < _xAxes.Count + _yAxes.Count; i++)
        {
            var element = i < _xAxes.Count ? _xAxes[i] : _yAxes[i - _xAxes.Count];
            if (element.Core is not { } core)
            {
                continue;
            }

            var state = new AxisRangeState(core.VisibleRange, core.IsAutoRange);
            lock (_published)
            {
                if (!_published.TryGetValue(element, out var known) || known != state)
                {
                    _published[element] = state;
                    hasChanges = true;
                }
            }
        }

        if (hasChanges && Interlocked.Exchange(ref _isPublishQueued, 1) == 0)
        {
            _enqueueIdle(_publish);
        }
    }

    private void PublishOnUiThread()
    {
        Volatile.Write(ref _isPublishQueued, 0);
        KeyValuePair<ChartAxis, AxisRangeState>[] states;
        lock (_published)
        {
            states = [.. _published];
        }

        foreach (var (element, state) in states)
        {
            element.Publish(state.Range, state.IsAutoRange);
        }
    }

    private readonly record struct AxisRangeState(DoubleRange Range, bool IsAutoRange);
}
