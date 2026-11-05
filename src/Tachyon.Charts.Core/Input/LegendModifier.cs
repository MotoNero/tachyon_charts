using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Input;

/// <summary>A row of a legend.</summary>
/// <param name="Series">The series.</param>
/// <param name="Bounds">The area of the row in chart coordinates.</param>
public readonly record struct LegendRow(ChartSeries Series, DipRect Bounds);

/// <summary>Where a legend sits: in a corner of the plot, or outside it at an edge of the chart, where it takes room of its own.</summary>
public enum LegendPlacement
{
    /// <summary>Inside the plot, top right.</summary>
    TopRight,

    /// <summary>Inside the plot, top left.</summary>
    TopLeft,

    /// <summary>Inside the plot, bottom right.</summary>
    BottomRight,

    /// <summary>Inside the plot, bottom left.</summary>
    BottomLeft,

    /// <summary>Outside the plot at the right edge, one row per series.</summary>
    Right,

    /// <summary>Outside the plot at the left edge, one row per series.</summary>
    Left,

    /// <summary>Outside the plot between title and plot, the series side by side.</summary>
    Top,

    /// <summary>Outside the plot at the bottom edge, the series side by side.</summary>
    Bottom,
}

/// <summary>
/// The names of series in a box. A chart can have several legends, each with its own series, title and place;
/// the pointer highlights a series and a click switches it on or off, in every legend that shows it.
/// </summary>
/// <param name="chart">The chart.</param>
public sealed class LegendOverlay(ChartSurface chart) : ChartOverlay
{
    private const double Margin = 8;
    private const double Padding = 6;
    private const double SwatchSize = 12;
    private const double SwatchGap = 6;
    private const double ItemGap = 16;

    private volatile LegendRow[] _rows = [];
    private LegendPlacement _placement;
    private string? _title;
    private IReadOnlyList<string>? _seriesNames;

    /// <summary>Gets the rows of the last frame, for hit tests on any thread.</summary>
    public IReadOnlyList<LegendRow> Rows => _rows;

    /// <summary>Gets or sets where the legend sits.</summary>
    public LegendPlacement Placement
    {
        get => _placement;
        set
        {
            _placement = value;
            Changed();
        }
    }

    /// <summary>Gets or sets a title above the rows, or <see langword="null"/> for none.</summary>
    public string? Title
    {
        get => _title;
        set
        {
            _title = value;
            Changed();
        }
    }

    /// <summary>Gets or sets the names of the series this legend shows, or <see langword="null"/> for all.</summary>
    public IReadOnlyList<string>? SeriesNames
    {
        get => _seriesNames;
        set
        {
            _seriesNames = value is null ? null : [.. value];
            Changed();
        }
    }

    /// <summary>Requests a new frame.</summary>
    public void Refresh() => OnChanged();

    /// <summary>Finds the room the legend takes at the edges of the chart; none inside the plot.</summary>
    internal (double Left, double Top, double Right, double Bottom) Room(ITextMeasurer measurer)
    {
        if (_placement is not (LegendPlacement.Right or LegendPlacement.Left or LegendPlacement.Top or LegendPlacement.Bottom))
        {
            return default;
        }

        var series = Shown();
        if (series.Count == 0)
        {
            return default;
        }

        var height = measurer.LineHeight + 4;
        if (_placement is LegendPlacement.Top or LegendPlacement.Bottom)
        {
            var band = height + (Padding * 2) + (_title is null ? 0 : height) + Margin;
            return _placement == LegendPlacement.Top ? (0, band, 0, 0) : (0, 0, 0, band);
        }

        var width = 0.0;
        for (var i = 0; i < series.Count; i++)
        {
            width = Math.Max(width, measurer.MeasureWidth(NameOf(series[i].Series, series[i].Index)));
        }

        width = Math.Max(width + SwatchSize + SwatchGap, _title is null ? 0 : measurer.MeasureWidth(_title)) + (Padding * 2) + Margin;
        return _placement == LegendPlacement.Left ? (width, 0, 0, 0) : (0, 0, width, 0);
    }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var series = Shown();
        if (series.Count == 0)
        {
            _rows = [];
            return;
        }

        var rowHeight = canvas.GetLineHeight(canvas.LabelSize) + 4;
        var titleHeight = _title is null ? 0 : rowHeight;
        var isRow = _placement is LegendPlacement.Top or LegendPlacement.Bottom;
        var widths = new double[series.Count];
        var textWidth = 0.0;
        var rowWidth = 0.0;
        for (var i = 0; i < series.Count; i++)
        {
            widths[i] = canvas.MeasureWidth(NameOf(series[i].Series, series[i].Index), canvas.LabelSize) + SwatchSize + SwatchGap;
            textWidth = Math.Max(textWidth, widths[i]);
            rowWidth += widths[i] + (i > 0 ? ItemGap : 0);
        }

        if (_title is not null)
        {
            textWidth = Math.Max(textWidth, canvas.MeasureWidth(_title, canvas.LabelSize));
            rowWidth = Math.Max(rowWidth, canvas.MeasureWidth(_title, canvas.LabelSize));
        }

        var width = (isRow ? rowWidth : textWidth) + (Padding * 2);
        var height = titleHeight + (isRow ? rowHeight : rowHeight * series.Count) + (Padding * 2);
        var (left, top) = Place(canvas, width, height);
        canvas.FillRectangle(new DipRect(left, top, width, height), canvas.BackgroundColor with { A = 220 });
        if (_title is not null)
        {
            canvas.DrawText(_title, left + Padding, top + Padding + rowHeight - (rowHeight * 0.28), canvas.LabelSize, canvas.TextColor);
        }

        var rows = new LegendRow[series.Count];
        var x = left + Padding;
        for (var i = 0; i < series.Count; i++)
        {
            var item = series[i].Series;
            var row = isRow
                ? new DipRect(x, top + Padding + titleHeight, widths[i], rowHeight)
                : new DipRect(left, top + Padding + titleHeight + (rowHeight * i), width, rowHeight);
            x += widths[i] + ItemGap;
            rows[i] = new LegendRow(item, row);
            DrawRow(canvas, item, NameOf(item, series[i].Index), row, isRow ? row.X : left + Padding);
        }

        _rows = rows;
    }

    private static string NameOf(ChartSeries series, int index) => series.Name is { Length: > 0 } name ? name : $"Series {index + 1}";

    private static void DrawRow(OverlayCanvas canvas, ChartSeries item, string name, DipRect row, double start)
    {
        if (item.IsHighlighted)
        {
            canvas.FillRectangle(row, canvas.TextColor with { A = 40 });
        }

        var swatch = new DipRect(start, row.Y + ((row.Height - SwatchSize) / 2), SwatchSize, SwatchSize);
        if (item.IsVisible)
        {
            canvas.FillRectangle(swatch, item.AccentColor);
        }
        else
        {
            canvas.DrawRectangle(swatch, item.AccentColor);
        }

        var text = item.IsVisible ? canvas.TextColor : canvas.TextColor with { A = 110 };
        canvas.DrawText(name, swatch.Right + SwatchGap, row.Y + row.Height - (row.Height * 0.28), canvas.LabelSize, text);
    }

    private void Changed()
    {
        chart.InvalidateLayout();
        OnChanged();
    }

    // The series this legend shows, with their number among all series for a default name.
    private List<(ChartSeries Series, int Index)> Shown()
    {
        var all = chart.Series;
        var shown = new List<(ChartSeries, int)>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            if (_seriesNames is null || (all[i].Name is { } name && _seriesNames.Contains(name)))
            {
                shown.Add((all[i], i));
            }
        }

        return shown;
    }

    private (double Left, double Top) Place(OverlayCanvas canvas, double width, double height)
    {
        var plot = canvas.Layout.PlotArea;
        return _placement switch
        {
            LegendPlacement.TopLeft => (plot.X + Margin, plot.Y + Margin),
            LegendPlacement.BottomRight => (plot.Right - width - Margin, plot.Bottom - height - Margin),
            LegendPlacement.BottomLeft => (plot.X + Margin, plot.Bottom - height - Margin),
            LegendPlacement.Right => (canvas.ChartWidth - Margin - width, plot.Y),
            LegendPlacement.Left => (Margin, plot.Y),
            LegendPlacement.Top => (plot.X + ((plot.Width - width) / 2), TopOfBand(canvas, height)),
            LegendPlacement.Bottom => (plot.X + ((plot.Width - width) / 2), canvas.ChartHeight - Margin - height),
            _ => (plot.Right - width - Margin, plot.Y + Margin),
        };
    }

    // Right above the plot and the labels of a top axis.
    private static double TopOfBand(OverlayCanvas canvas, double height)
    {
        var top = canvas.Layout.PlotArea.Y;
        foreach (var placement in canvas.Layout.Axes)
        {
            if (placement.Axis.Position == AxisPosition.Top && !placement.Axis.IsHidden)
            {
                top = Math.Min(top, placement.Bounds.Y);
            }
        }

        return top - height - (Margin / 2);
    }
}

/// <summary>
/// Shows legends. A click switches a series on or off, the pointer highlights it. Without a legend of its own in the
/// chart, it adds one at the top right of the plot.
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

    /// <summary>Gets the legend this modifier added, or <see langword="null"/> when the chart had legends of its own.</summary>
    public LegendOverlay? Overlay => _overlay;

    /// <inheritdoc />
    protected internal override void OnAttached(ChartInteraction interaction)
    {
        foreach (var overlay in interaction.Chart.Overlays)
        {
            if (overlay is LegendOverlay)
            {
                return;
            }
        }

        var added = new LegendOverlay(interaction.Chart);
        _overlay = added;
        interaction.Dispatch(() => interaction.Chart.AddOverlay(added));
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
        if (Interaction is not { } interaction)
        {
            return ModifierResult.Ignored;
        }

        var row = input.Kind == PointerEventKind.Exited ? null : Find(interaction.Chart, input.Position);
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
                Refresh(interaction.Chart);
            });
            return ModifierResult.Handled;
        }

        return ModifierResult.Ignored;
    }

    private static ChartSeries? Find(ChartSurface chart, Vector2 position)
    {
        foreach (var overlay in chart.Overlays)
        {
            if (overlay is not LegendOverlay legend)
            {
                continue;
            }

            foreach (var row in legend.Rows)
            {
                if (position.X >= row.Bounds.X && position.X <= row.Bounds.Right && position.Y >= row.Bounds.Y && position.Y <= row.Bounds.Bottom)
                {
                    return row.Series;
                }
            }
        }

        return null;
    }

    private static void Refresh(ChartSurface chart)
    {
        foreach (var overlay in chart.Overlays)
        {
            (overlay as LegendOverlay)?.Refresh();
        }
    }

    private void Highlight(ChartInteraction interaction, ChartSeries? series)
    {
        var previous = _highlighted;
        if (previous == series)
        {
            return;
        }

        _highlighted = series;
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

            Refresh(interaction.Chart);
        });
    }
}
