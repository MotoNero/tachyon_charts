using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Input;

/// <summary>
/// What two cursors measure, in units of the axes.
/// </summary>
/// <param name="X1">The X value of the first cursor.</param>
/// <param name="Y1">The Y value of the first cursor.</param>
/// <param name="X2">The X value of the second cursor.</param>
/// <param name="Y2">The Y value of the second cursor.</param>
public readonly record struct MeasureResult(double X1, double Y1, double X2, double Y2)
{
    /// <summary>Gets the distance in X.</summary>
    public double DeltaX => X2 - X1;

    /// <summary>Gets the distance in Y.</summary>
    public double DeltaY => Y2 - Y1;

    /// <summary>Gets 1/ΔX, the frequency of a period, or <see langword="null"/> without a distance.</summary>
    public double? Frequency => DeltaX == 0 ? null : 1 / DeltaX;

    /// <summary>Gets ΔY/ΔX, or <see langword="null"/> without a distance in X.</summary>
    public double? Slope => DeltaX == 0 ? null : DeltaY / DeltaX;
}

/// <summary>
/// Two cursors with their distances.
/// </summary>
public sealed class MeasureCursorOverlay : ChartOverlay
{
    private AxisBase? _xAxis;
    private AxisBase? _yAxis;
    private MeasureResult? _result;

    /// <summary>Gets the current measurement.</summary>
    public MeasureResult? Result => _result;

    /// <summary>Gets or sets the color of the first cursor.</summary>
    public Rgba8 FirstColor { get; set; } = new(240, 130, 90, 255);

    /// <summary>Gets or sets the color of the second cursor.</summary>
    public Rgba8 SecondColor { get; set; } = new(110, 210, 130, 255);

    /// <summary>Shows the cursors.</summary>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    /// <param name="result">The measurement.</param>
    public void Show(AxisBase xAxis, AxisBase yAxis, MeasureResult result)
    {
        _xAxis = xAxis;
        _yAxis = yAxis;
        _result = result;
        OnChanged();
    }

    /// <summary>Removes the cursors.</summary>
    public void Hide()
    {
        if (_result is not null)
        {
            _result = null;
            OnChanged();
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        if (_result is not { } result
            || _xAxis is not { } xAxis
            || _yAxis is not { } yAxis
            || !canvas.TryGetPosition(xAxis, result.X1, out var x1)
            || !canvas.TryGetPosition(yAxis, result.Y1, out var y1)
            || !canvas.TryGetPosition(xAxis, result.X2, out var x2)
            || !canvas.TryGetPosition(yAxis, result.Y2, out var y2))
        {
            return;
        }

        var plot = canvas.Layout.PlotArea;
        canvas.Clip = plot;
        DrawCursor(canvas, plot, x1, y1, FirstColor);
        DrawCursor(canvas, plot, x2, y2, SecondColor);
        canvas.Clip = null;

        var culture = CultureInfo.InvariantCulture;
        string?[] lines =
        [
            string.Format(culture, "ΔX {0:G5}", result.DeltaX),
            string.Format(culture, "ΔY {0:G5}", result.DeltaY),
            result.Frequency is { } frequency ? string.Format(culture, "1/ΔX {0:G5}", frequency) : null,
            result.Slope is { } slope ? string.Format(culture, "ΔY/ΔX {0:G5}", slope) : null,
        ];

        var left = Math.Max(x1, x2) + 8;
        var top = Math.Min(y1, y2);
        var height = canvas.GetLineHeight(canvas.LabelSize) + 4;
        foreach (var line in lines)
        {
            if (line is not null)
            {
                canvas.DrawLabel(line, left, top, canvas.AccentColor, canvas.BackgroundColor);
                top += height;
            }
        }
    }

    private static void DrawCursor(OverlayCanvas canvas, DipRect plot, double x, double y, Rgba8 color)
    {
        canvas.DrawLine(new Vector2((float)x, (float)plot.Y), new Vector2((float)x, (float)plot.Bottom), 1, color);
        canvas.DrawLine(new Vector2((float)plot.X, (float)y), new Vector2((float)plot.Right, (float)y), 1, color);
    }
}

/// <summary>
/// Places two measuring cursors: dragging with the key spans them, and either can be dragged again.
/// Escape removes them.
/// </summary>
public sealed class MeasureCursorModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above panning.</summary>
    public const int DefaultPriority = 23;

    private readonly MeasureCursorOverlay _overlay = new();
    private PaneHit _pane;
    private MeasureResult? _result;
    private long _pointerId;
    private bool _isDragging;
    private bool _movesFirst;

    /// <summary>Initializes a new instance of the <see cref="MeasureCursorModifier"/> class.</summary>
    public MeasureCursorModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets or sets the keys to hold.</summary>
    public InputModifiers ModifierKey { get; set; } = InputModifiers.Alt;

    /// <summary>Gets or sets how near a cursor must be for dragging it, in device-independent units.</summary>
    public double GrabDistance { get; set; } = 6;

    /// <summary>Gets the current measurement.</summary>
    public MeasureResult? Result => _result;

    /// <summary>Occurs on the UI thread when the measurement changed.</summary>
    public event Action<MeasureResult?>? Changed;

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
            || !interaction.TryHitPane(input.Position, out var pane)
            || pane.Axes.Count == 0)
        {
            return ModifierResult.Ignored;
        }

        _pane = pane;
        var (x, y) = ToValues(interaction, input.Position);
        if (_result is { } result && IsNear(interaction, x, result.X1))
        {
            _movesFirst = true;
        }
        else if (_result is { } other && IsNear(interaction, x, other.X2))
        {
            _movesFirst = false;
        }
        else
        {
            _movesFirst = false;
            Publish(new MeasureResult(x, y, x, y));
        }

        _isDragging = true;
        _pointerId = input.PointerId;
        return ModifierResult.Capture;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnKey(in KeyInputEvent input)
    {
        if (_result is null || input.Key != ChartKey.Escape || input.Kind != KeyEventKind.Pressed)
        {
            return ModifierResult.Ignored;
        }

        Publish(null);
        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => _isDragging = false;

    private (double X, double Y) ToValues(ChartInteraction interaction, Vector2 position)
    {
        var x = interaction.ToAxisValue(position.X);
        var axis = _pane.Axes[0];
        return (x, axis.CreateTransform(_pane.Bounds.Y, _pane.Bounds.Height).ToData(position.Y));
    }

    private bool IsNear(ChartInteraction interaction, double x, double cursor) =>
        Math.Abs(interaction.ToAxisDistance(interaction.Chart.HorizontalAxes[0], GrabDistance)) >= Math.Abs(x - cursor);

    private ModifierResult ContinueDrag(in PointerInputEvent input)
    {
        switch (input.Kind)
        {
            case PointerEventKind.Moved:
                if (Interaction is { } interaction && _result is { } result)
                {
                    var (x, y) = ToValues(interaction, input.Position);
                    Publish(_movesFirst ? result with { X1 = x, Y1 = y } : result with { X2 = x, Y2 = y });
                }

                return ModifierResult.Handled;
            case PointerEventKind.Released:
            case PointerEventKind.Canceled:
                _isDragging = false;
                return ModifierResult.Release;
            default:
                return ModifierResult.Handled;
        }
    }

    private void Publish(MeasureResult? result)
    {
        _result = result;
        if (Interaction is { } interaction)
        {
            var overlay = _overlay;
            var xAxis = interaction.Chart.HorizontalAxes[0];
            var yAxis = _pane.Axes?.FirstOrDefault();
            interaction.Dispatch(() =>
            {
                if (result is { } value && yAxis is not null)
                {
                    overlay.Show(xAxis, yAxis, value);
                }
                else
                {
                    overlay.Hide();
                }
            });
        }

        Changed?.Invoke(result);
    }
}
