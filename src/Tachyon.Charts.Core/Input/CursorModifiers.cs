using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Overlays;

namespace Tachyon.Charts.Core.Input;

/// <summary>
/// Shows a crosshair at the pointer. It only observes, so other modifiers keep receiving the input.
/// </summary>
public sealed class CrosshairModifier : ChartModifier
{
    private readonly CrosshairOverlay _overlay = new();

    /// <summary>Initializes a new instance of the <see cref="CrosshairModifier"/> class.</summary>
    public CrosshairModifier()
        : base(-10)
    {
    }

    /// <summary>Gets or sets a value indicating whether the crosshair jumps to the nearest point.</summary>
    public bool SnapToData { get; set; }

    /// <summary>Gets or sets how far snapping reaches, in device-independent units.</summary>
    public double SnapDistance { get; set; } = 30;

    /// <summary>Gets the overlay, for example to connect it to other charts.</summary>
    public CrosshairOverlay Overlay => _overlay;

    /// <summary>Occurs on the UI thread when the pointer moves over the chart.</summary>
    public event Action<CrosshairModifier, double?>? Moved;

    /// <inheritdoc />
    protected internal override void OnAttached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.AddOverlay(_overlay));

    /// <inheritdoc />
    protected internal override void OnDetached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.RemoveOverlay(_overlay));

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        var overlay = _overlay;
        if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled || !interaction.TryHitPane(input.Position, out _))
        {
            interaction.Dispatch(overlay.Hide);
            Moved?.Invoke(this, null);
            return ModifierResult.Ignored;
        }

        var position = input.Position;
        HitResult? snapped = null;
        if (SnapToData && interaction.Chart.TryHitTest(position, SnapDistance, HitTestMode.Nearest, out var hit))
        {
            snapped = hit;
        }

        interaction.Dispatch(() => overlay.Show(position, snapped));
        Moved?.Invoke(this, interaction.ToXValue(position));
        return ModifierResult.Ignored;
    }
}

/// <summary>
/// Shows a vertical line at the pointer with the values of all series there.
/// </summary>
public sealed class RolloverModifier : ChartModifier
{
    private readonly RolloverOverlay _overlay = new();
    private readonly List<HitResult> _hits = [];

    /// <summary>Initializes a new instance of the <see cref="RolloverModifier"/> class.</summary>
    public RolloverModifier()
        : base(-10)
    {
    }

    /// <summary>Gets or sets how far from the line a point still counts, in device-independent units.</summary>
    public double MaximumDistance { get; set; } = 40;

    /// <summary>Gets the overlay.</summary>
    public RolloverOverlay Overlay => _overlay;

    /// <inheritdoc />
    protected internal override void OnAttached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.AddOverlay(_overlay));

    /// <inheritdoc />
    protected internal override void OnDetached(ChartInteraction interaction) =>
        interaction.Dispatch(() => interaction.Chart.RemoveOverlay(_overlay));

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        var overlay = _overlay;
        if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled || !interaction.TryHitPane(input.Position, out _))
        {
            interaction.Dispatch(overlay.Hide);
            return ModifierResult.Ignored;
        }

        _hits.Clear();
        interaction.Chart.HitTestAll(input.Position, MaximumDistance, HitTestMode.NearestX, _hits);
        var hits = _hits.ToArray();
        var axis = interaction.Chart.FirstXAxis;
        var value = interaction.ToXValue(input.Position);
        interaction.Dispatch(() => overlay.Show(axis, value, hits));
        return ModifierResult.Ignored;
    }
}

/// <summary>The content of a tooltip.</summary>
/// <param name="SeriesName">The name of the series.</param>
/// <param name="XText">The formatted X value.</param>
/// <param name="YText">The formatted Y value.</param>
/// <param name="X">The X value in units of the axis.</param>
/// <param name="Y">The Y value.</param>
/// <param name="Index">The index of the point.</param>
/// <param name="Color">The color of the series.</param>
/// <param name="Position">The pointer position in chart coordinates.</param>
public readonly record struct TooltipData(string? SeriesName, string XText, string YText, double X, double Y, long Index, Rendering.Rgba8 Color, System.Numerics.Vector2 Position);

/// <summary>
/// Finds the nearest point at the pointer and reports it. How it looks is up to the host.
/// </summary>
public sealed class TooltipModifier : ChartModifier
{
    /// <summary>Initializes a new instance of the <see cref="TooltipModifier"/> class.</summary>
    public TooltipModifier()
        : base(-10)
    {
    }

    /// <summary>Occurs on the UI thread with new content, or <see langword="null"/> to hide the tooltip.</summary>
    public event Action<TooltipData?>? Changed;

    /// <summary>Gets or sets how far a point still counts, in device-independent units.</summary>
    public double MaximumDistance { get; set; } = 30;

    /// <inheritdoc />
    protected internal override void OnDetached(ChartInteraction interaction) => Changed?.Invoke(null);

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture) => Changed?.Invoke(null);

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind is PointerEventKind.Exited or PointerEventKind.Canceled
            || !interaction.Chart.TryHitTest(input.Position, MaximumDistance, HitTestMode.Nearest, out var hit))
        {
            Changed?.Invoke(null);
            return ModifierResult.Ignored;
        }

        var series = hit.Series;
        Changed?.Invoke(new TooltipData(
            series.Name,
            series.XAxis.FormatCursorValue(hit.X),
            hit.FormatY(),
            hit.X,
            hit.Y,
            hit.Index,
            series.AccentColor,
            input.Position));
        return ModifierResult.Ignored;
    }
}
