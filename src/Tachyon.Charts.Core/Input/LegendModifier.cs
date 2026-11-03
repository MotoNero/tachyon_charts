using System.Numerics;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Input;

/// <summary>A row of a legend.</summary>
/// <param name="Series">The series.</param>
/// <param name="Bounds">The area of the row in chart coordinates.</param>
public readonly record struct LegendRow(ChartSeries Series, DipRect Bounds);

/// <summary>
/// The names of the series in a box at the top right of the plot.
/// </summary>
/// <param name="chart">The chart.</param>
public sealed class LegendOverlay(ChartSurface chart) : ChartOverlay
{
    private const double Margin = 8;
    private const double Padding = 6;
    private const double SwatchSize = 12;
    private const double SwatchGap = 6;

    private volatile LegendRow[] _rows = [];

    /// <summary>Gets the rows of the last frame, for hit tests on any thread.</summary>
    public IReadOnlyList<LegendRow> Rows => _rows;

    /// <summary>Requests a new frame.</summary>
    public void Refresh() => OnChanged();

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var series = chart.Series;
        if (series.Count == 0)
        {
            _rows = [];
            return;
        }

        var plot = canvas.Layout.PlotArea;
        var rowHeight = canvas.GetLineHeight(canvas.LabelSize) + 4;
        var textWidth = 0.0;
        for (var i = 0; i < series.Count; i++)
        {
            textWidth = Math.Max(textWidth, canvas.MeasureWidth(NameOf(series[i], i), canvas.LabelSize));
        }

        var width = textWidth + SwatchSize + SwatchGap + (Padding * 2);
        var left = plot.Right - width - Margin;
        var top = plot.Y + Margin;
        canvas.FillRectangle(new DipRect(left, top, width, (rowHeight * series.Count) + (Padding * 2)), canvas.BackgroundColor with { A = 220 });

        var rows = new LegendRow[series.Count];
        for (var i = 0; i < series.Count; i++)
        {
            var item = series[i];
            var y = top + Padding + (rowHeight * i);
            var row = new DipRect(left, y, width, rowHeight);
            rows[i] = new LegendRow(item, row);
            if (item.IsHighlighted)
            {
                canvas.FillRectangle(row, canvas.TextColor with { A = 40 });
            }

            var swatch = new DipRect(left + Padding, y + ((rowHeight - SwatchSize) / 2), SwatchSize, SwatchSize);
            if (item.IsVisible)
            {
                canvas.FillRectangle(swatch, item.AccentColor);
            }
            else
            {
                canvas.DrawRectangle(swatch, item.AccentColor);
            }

            var text = item.IsVisible ? canvas.TextColor : canvas.TextColor with { A = 110 };
            canvas.DrawText(NameOf(item, i), swatch.Right + SwatchGap, y + rowHeight - (rowHeight * 0.28), canvas.LabelSize, text);
        }

        _rows = rows;
    }

    private static string NameOf(ChartSeries series, int index) => series.Name is { Length: > 0 } name ? name : $"Series {index + 1}";
}

/// <summary>
/// Shows a legend. A click switches a series on or off, the pointer highlights it.
/// </summary>
public sealed class LegendModifier : ChartModifier
{
    /// <summary>The default dispatch priority, above everything that acts on the plot.</summary>
    public const int DefaultPriority = 35;

    private LegendOverlay? _overlay;
    private ChartSeries? _highlighted;

    /// <summary>Initializes a new instance of the <see cref="LegendModifier"/> class.</summary>
    public LegendModifier()
        : base(DefaultPriority)
    {
    }

    /// <summary>Gets the overlay while attached.</summary>
    public LegendOverlay? Overlay => _overlay;

    /// <inheritdoc />
    protected internal override void OnAttached(ChartInteraction interaction)
    {
        var overlay = new LegendOverlay(interaction.Chart);
        _overlay = overlay;
        interaction.Dispatch(() => interaction.Chart.AddOverlay(overlay));
    }

    /// <inheritdoc />
    protected internal override void OnDetached(ChartInteraction interaction)
    {
        if (_overlay is { } overlay)
        {
            interaction.Dispatch(() => interaction.Chart.RemoveOverlay(overlay));
            _overlay = null;
        }
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction is not { } interaction || _overlay is not { } overlay)
        {
            return ModifierResult.Ignored;
        }

        var row = input.Kind == PointerEventKind.Exited ? null : Find(overlay, input.Position);
        if (input.Kind is PointerEventKind.Moved or PointerEventKind.Exited)
        {
            Highlight(interaction, row);
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary && row is { } series)
        {
            interaction.Dispatch(() =>
            {
                series.IsVisible = !series.IsVisible;
                overlay.Refresh();
            });
            return ModifierResult.Handled;
        }

        return ModifierResult.Ignored;
    }

    private static ChartSeries? Find(LegendOverlay overlay, Vector2 position)
    {
        foreach (var row in overlay.Rows)
        {
            if (position.X >= row.Bounds.X && position.X <= row.Bounds.Right && position.Y >= row.Bounds.Y && position.Y <= row.Bounds.Bottom)
            {
                return row.Series;
            }
        }

        return null;
    }

    private void Highlight(ChartInteraction interaction, ChartSeries? series)
    {
        var previous = _highlighted;
        if (previous == series)
        {
            return;
        }

        _highlighted = series;
        var overlay = _overlay;
        interaction.Dispatch(() =>
        {
            if (previous is not null)
            {
                previous.IsHighlighted = false;
            }

            if (series is not null)
            {
                series.IsHighlighted = true;
            }

            overlay?.Refresh();
        });
    }
}
