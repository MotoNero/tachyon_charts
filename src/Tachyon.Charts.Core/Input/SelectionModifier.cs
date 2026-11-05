using System.Numerics;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Input;

/// <summary>How points are selected.</summary>
public enum SelectionMode
{
    /// <summary>A click selects the nearest point.</summary>
    Point,

    /// <summary>Dragging selects the points in a rectangle.</summary>
    Rectangle,

    /// <summary>Dragging draws a free outline around the points.</summary>
    Lasso,
}

/// <summary>The selected points of one series.</summary>
/// <param name="Series">The series.</param>
/// <param name="Indices">The numbers of the points.</param>
public sealed record SeriesSelection(ChartSeries Series, IReadOnlyList<long> Indices);

/// <summary>
/// Marks selected points and shows the outline while dragging.
/// </summary>
public sealed class SelectionOverlay : ChartOverlay
{
    private const float MarkerSize = 3;
    private const int MaximumMarkers = 50_000;

    private readonly List<(ChartSeries Series, double X, double Y)> _points = [];
    private Vector2[] _outline = [];

    /// <summary>Gets the color of the marks.</summary>
    public Rgba8 Color { get; set; } = new(255, 215, 90, 255);

    internal int MarkedPoints => _points.Count;

    internal double LastMarkedX => _points[^1].X;

    /// <summary>Sets the selected points in units of the axes.</summary>
    /// <param name="points">The points. Beyond 50 000 an even share is marked, so that the marks cover all of them.</param>
    public void SetPoints(IEnumerable<(ChartSeries Series, double X, double Y)> points)
    {
        var all = points as IReadOnlyList<(ChartSeries Series, double X, double Y)> ?? [.. points];
        var step = Math.Max((long)all.Count / MaximumMarkers, 1) + (all.Count % MaximumMarkers != 0 && all.Count > MaximumMarkers ? 1 : 0);
        _points.Clear();
        for (long i = 0; i < all.Count; i += step)
        {
            _points.Add(all[(int)i]);
        }

        OnChanged();
    }

    /// <summary>Sets the outline in chart coordinates.</summary>
    /// <param name="outline">The outline, empty for none.</param>
    public void SetOutline(Vector2[] outline)
    {
        _outline = outline;
        OnChanged();
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        canvas.Clip = canvas.Layout.PlotArea;
        foreach (var (series, x, y) in _points)
        {
            if (canvas.TryGetPoint(series.XAxis, series.YAxis, x, y, out var point))
            {
                canvas.DrawRectangle(new DipRect(point.X - MarkerSize, point.Y - MarkerSize, MarkerSize * 2, MarkerSize * 2), Color);
            }
        }

        for (var i = 1; i < _outline.Length; i++)
        {
            canvas.DrawLine(_outline[i - 1], _outline[i], 1.5f, canvas.AccentColor);
        }

        if (_outline.Length > 2)
        {
            canvas.DrawLine(_outline[^1], _outline[0], 1.5f, canvas.AccentColor);
        }
    }
}

/// <summary>
/// Selects points by click, rectangle or lasso. Holding the key keeps the plain drag free for panning.
/// </summary>
public sealed class SelectionModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above panning.</summary>
    public const int DefaultPriority = 22;

    private readonly SelectionOverlay _overlay = new();
    private readonly List<Vector2> _outline = [];
    private Vector2 _start;
    private long _pointerId;
    private bool _isDragging;
    private bool _isCanceled;

    /// <summary>Initializes a new instance of the <see cref="SelectionModifier"/> class.</summary>
    public SelectionModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets or sets how points are selected.</summary>
    public SelectionMode Mode { get; set; } = SelectionMode.Rectangle;

    /// <summary>Gets or sets the keys to hold.</summary>
    public InputModifiers ModifierKey { get; set; } = InputModifiers.Shift;

    /// <summary>Gets or sets how far a click reaches in device-independent units.</summary>
    public double ClickDistance { get; set; } = 10;

    /// <summary>Gets the overlay.</summary>
    public SelectionOverlay Overlay => _overlay;

    /// <summary>Gets the current selection.</summary>
    public IReadOnlyList<SeriesSelection> Selection { get; private set; } = [];

    /// <summary>Occurs on the UI thread when the selection changed.</summary>
    public event Action<IReadOnlyList<SeriesSelection>>? SelectionChanged;

    /// <inheritdoc />
    protected internal override void OnAttached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.AddOverlay(_overlay));

    /// <inheritdoc />
    protected internal override void OnDetached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.RemoveOverlay(_overlay));

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (_isDragging)
        {
            return input.PointerId == _pointerId ? ContinueDrag(input) : ModifierResult.Ignored;
        }

        if (input.Kind != PointerEventKind.Pressed
            || input.ChangedButton != PointerButton.Primary
            || input.Modifiers != ModifierKey
            || Interaction is not { } interaction
            || !interaction.TryHitPane(input.Position, out _))
        {
            return ModifierResult.Ignored;
        }

        _isDragging = true;
        _isCanceled = false;
        _pointerId = input.PointerId;
        _start = input.Position;
        _outline.Clear();
        _outline.Add(input.Position);
        return ModifierResult.Capture;
    }

    /// <summary>Removes the selection.</summary>
    public void Clear()
    {
        Selection = [];
        var overlay = _overlay;
        Interaction?.Dispatch(() => overlay.SetPoints([]));
        SelectionChanged?.Invoke(Selection);
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (input.Key != ChartKey.Escape || input.Kind != KeyEventKind.Pressed)
        {
            return ModifierResult.Ignored;
        }

        if (!_isDragging && Selection.Count > 0)
        {
            Clear();
            return ModifierResult.Handled;
        }

        if (!_isDragging || _isCanceled)
        {
            return ModifierResult.Ignored;
        }

        _isCanceled = true;
        ShowOutline([]);
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture)
    {
        _isDragging = false;
        ShowOutline([]);
    }

    private static Vector2[] RectangleOutline(Vector2 a, Vector2 b) => [a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y)];

    private ModifierResult ContinueDrag(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                if (!_isCanceled)
                {
                    _outline.Add(input.Position);
                    Vector2[] shape = Mode switch
                    {
                        SelectionMode.Rectangle => RectangleOutline(_start, input.Position),
                        SelectionMode.Lasso => [.. _outline],
                        _ => [],
                    };
                    ShowOutline(shape);
                }

                return ModifierResult.Handled;
            case PointerEventKind.Released:
                _isDragging = false;
                ShowOutline([]);
                if (!_isCanceled)
                {
                    Finish(input.Position);
                }

                return ModifierResult.Release;
            case PointerEventKind.Canceled:
                _isDragging = false;
                ShowOutline([]);
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void ShowOutline(Vector2[] outline)
    {
        var overlay = _overlay;
        Interaction?.Dispatch(() => overlay.SetOutline(outline));
    }

    private void Finish(Vector2 end)
    {
        if (Interaction is not { } interaction)
        {
            return;
        }

        var chart = interaction.Chart;
        var selection = new List<SeriesSelection>();
        if (Mode == SelectionMode.Point)
        {
            if (chart.TryHitTest(end, ClickDistance, HitTestMode.Nearest, out var hit))
            {
                selection.Add(new SeriesSelection(hit.Series, [hit.Index]));
            }
        }
        else
        {
            var polygon = Mode == SelectionMode.Rectangle ? RectangleOutline(_start, end) : [.. _outline];
            selection.AddRange(chart.SelectPoints(polygon));
        }

        Selection = selection;
        var points = chart.ReadPoints(selection);
        var overlay = _overlay;
        interaction.Dispatch(() => overlay.SetPoints(points));
        SelectionChanged?.Invoke(selection);
    }
}
