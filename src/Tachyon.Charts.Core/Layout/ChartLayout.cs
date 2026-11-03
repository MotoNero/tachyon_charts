using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Layout;

/// <summary>
/// A rectangle in device-independent units.
/// </summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// Gets the right edge.
    /// </summary>
    public double Right => X + Width;

    /// <summary>
    /// Gets the bottom edge.
    /// </summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// Converts to physical pixels, with the edges on whole pixels so that neighbors share them.
    /// </summary>
    /// <param name="dpiScale">Physical pixels per device-independent unit.</param>
    /// <returns>The rectangle in pixels.</returns>
    public PixelRect ToPixels(double dpiScale)
    {
        // Not Math.Round: rounding to even would collapse a line from 115.5 to 116.5 to no height.
        var left = Math.Floor((X * dpiScale) + 0.5);
        var top = Math.Floor((Y * dpiScale) + 0.5);
        var right = Math.Floor((Right * dpiScale) + 0.5);
        var bottom = Math.Floor((Bottom * dpiScale) + 0.5);
        return new PixelRect((float)left, (float)top, (float)(right - left), (float)(bottom - top));
    }
}

/// <summary>
/// One of the Y areas stacked in a chart, for example a channel of an oscilloscope.
/// </summary>
/// <param name="Axes">The vertical axes, the first of each side next to the plot area.</param>
/// <param name="Weight">The share of the height, relative to the other panes, positive.</param>
public sealed record LayoutPane(IReadOnlyList<AxisBase> Axes, double Weight = 1);

/// <summary>
/// The distances of a layout in device-independent units.
/// </summary>
public sealed record ChartLayoutOptions
{
    /// <summary>
    /// Gets the free space around the chart.
    /// </summary>
    public double Padding { get; init; } = 8;

    /// <summary>
    /// Gets the length of tick marks.
    /// </summary>
    public double TickLength { get; init; } = 5;

    /// <summary>
    /// Gets the distance between tick marks and labels.
    /// </summary>
    public double LabelGap { get; init; } = 3;

    /// <summary>
    /// Gets the distance between neighboring axes.
    /// </summary>
    public double AxisGap { get; init; } = 4;

    /// <summary>
    /// Gets the distance between stacked panes. It grows to the line height if necessary, because
    /// the last label of a pane reaches half a line beyond it, as does the first label of the next.
    /// </summary>
    public double PaneGap { get; init; } = 8;

    /// <summary>
    /// Gets the distance below the chart title.
    /// </summary>
    public double TitleGap { get; init; } = 6;

    /// <summary>
    /// Gets the smallest distance of the plot from the left edge, so that plots of several charts line up.
    /// </summary>
    public double MinimumPlotLeft { get; init; }

    /// <summary>
    /// Gets the smallest distance of the plot from the right edge.
    /// </summary>
    public double MinimumPlotRight { get; init; }
}

/// <summary>
/// Where an axis sits.
/// </summary>
/// <param name="Axis">The axis.</param>
/// <param name="Bounds">The area of its ticks, labels and title.</param>
public readonly record struct AxisPlacement(AxisBase Axis, DipRect Bounds);

/// <summary>
/// The arranged parts of a chart.
/// </summary>
/// <param name="Title">The area of the chart title, empty if there is none.</param>
/// <param name="PlotArea">The area from the first to the last pane.</param>
/// <param name="Panes">The areas of the panes, top first.</param>
/// <param name="Axes">The places of all axes.</param>
public sealed record ChartLayoutResult(DipRect Title, DipRect PlotArea, IReadOnlyList<DipRect> Panes, IReadOnlyList<AxisPlacement> Axes);

/// <summary>
/// Arranges titles, axes and panes in the size of a chart.
/// </summary>
/// <remarks>
/// The height of horizontal axes does not depend on the width, so one pass suffices: horizontal axes
/// give the pane heights, those give the ticks and widths of the vertical axes, and those the plot width.
/// </remarks>
public static class ChartLayout
{
    private const string ReservedColorLabel = "-0.0000";

    /// <summary>
    /// Calculates a layout.
    /// </summary>
    /// <param name="size">The size of the chart. If it is too small, the plot area shrinks to nothing.</param>
    /// <param name="title">The chart title, or <see langword="null"/>.</param>
    /// <param name="horizontalAxes">The axes at the top and bottom, the first of each side next to the plot area.</param>
    /// <param name="panes">The panes, top first, at least one.</param>
    /// <param name="measurer">The measurement of text.</param>
    /// <param name="options">The distances, or <see langword="null"/> for the defaults.</param>
    /// <returns>The layout.</returns>
    /// <exception cref="ArgumentException">An axis is on the wrong side, or there is no valid pane.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is negative or not finite.</exception>
    public static ChartLayoutResult Arrange(
        DipSize size,
        string? title,
        IReadOnlyList<AxisBase> horizontalAxes,
        IReadOnlyList<LayoutPane> panes,
        ITextMeasurer measurer,
        ChartLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(horizontalAxes);
        ArgumentNullException.ThrowIfNull(panes);
        ArgumentNullException.ThrowIfNull(measurer);
        options ??= new ChartLayoutOptions();
        Validate(size, horizontalAxes, panes);

        var left = options.Padding;
        var top = options.Padding;
        var right = size.Width - options.Padding;
        var bottom = size.Height - options.Padding;

        var titleBounds = default(DipRect);
        if (!string.IsNullOrEmpty(title))
        {
            titleBounds = new DipRect(left, top, Math.Max(right - left, 0), measurer.LineHeight);
            top += measurer.LineHeight + options.TitleGap;
        }

        var topAxes = horizontalAxes.Where(axis => axis.Position == AxisPosition.Top).ToArray();
        var bottomAxes = horizontalAxes.Where(axis => axis.Position == AxisPosition.Bottom).ToArray();
        var topThicknesses = Array.ConvertAll(topAxes, axis => HorizontalThickness(axis, measurer, options));
        var bottomThicknesses = Array.ConvertAll(bottomAxes, axis => HorizontalThickness(axis, measurer, options));

        // Vertical labels center on their tick, the outermost must stay inside the chart.
        var halfLine = measurer.LineHeight / 2;
        var paneGap = Math.Max(options.PaneGap, measurer.LineHeight);
        var plotTop = Math.Max(top + Stacked(topThicknesses, options), halfLine);
        var plotBottom = Math.Max(Math.Min(bottom - Stacked(bottomThicknesses, options), size.Height - halfLine), plotTop);
        var paneHeights = DistributeHeights(plotBottom - plotTop, panes, paneGap);

        var leftColumns = MeasureColumns(panes, paneHeights, AxisPosition.Left, measurer, options);
        var rightColumns = MeasureColumns(panes, paneHeights, AxisPosition.Right, measurer, options);
        var plotLeft = left + Stacked(leftColumns, options);
        var plotRight = Math.Max(right - Stacked(rightColumns, options), plotLeft);
        (plotLeft, plotRight) = KeepLabelsInside(horizontalAxes, plotLeft, plotRight, size.Width, measurer);
        plotLeft = Math.Max(plotLeft, options.MinimumPlotLeft);
        plotRight = Math.Max(Math.Min(plotRight, size.Width - options.MinimumPlotRight), plotLeft);
        var plotWidth = plotRight - plotLeft;

        var paneBounds = new DipRect[panes.Count];
        var placements = new List<AxisPlacement>();
        var paneTop = plotTop;
        for (var i = 0; i < panes.Count; i++)
        {
            paneBounds[i] = new DipRect(plotLeft, paneTop, plotWidth, paneHeights[i]);
            PlaceVertical(panes[i], paneBounds[i], leftColumns, rightColumns, options, placements);
            paneTop += paneHeights[i] + paneGap;
        }

        var plotArea = new DipRect(plotLeft, plotTop, plotWidth, plotBottom - plotTop);
        PlaceHorizontal(topAxes, topThicknesses, plotArea, isTop: true, options, placements);
        PlaceHorizontal(bottomAxes, bottomThicknesses, plotArea, isTop: false, options, placements);

        return new ChartLayoutResult(titleBounds, plotArea, paneBounds, placements);
    }

    private static void Validate(DipSize size, IReadOnlyList<AxisBase> horizontalAxes, IReadOnlyList<LayoutPane> panes)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width < 0 || size.Height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The size must be finite and not negative.");
        }

        if (panes.Count == 0)
        {
            throw new ArgumentException("A chart needs at least one pane.", nameof(panes));
        }

        foreach (var axis in horizontalAxes)
        {
            if (!axis.IsHorizontal)
            {
                throw new ArgumentException($"Axis '{axis.Id}' is not horizontal.", nameof(horizontalAxes));
            }
        }

        foreach (var pane in panes)
        {
            if (!double.IsFinite(pane.Weight) || pane.Weight <= 0)
            {
                throw new ArgumentException($"The pane weight {pane.Weight} is not positive.", nameof(panes));
            }

            foreach (var axis in pane.Axes)
            {
                if (axis.IsHorizontal)
                {
                    throw new ArgumentException($"Axis '{axis.Id}' is not vertical.", nameof(panes));
                }
            }
        }
    }

    private static double Stacked(double[] thicknesses, ChartLayoutOptions options)
    {
        var total = 0.0;
        foreach (var thickness in thicknesses)
        {
            // A hidden axis takes no room, not even the gap.
            total += thickness > 0 ? thickness + options.AxisGap : 0;
        }

        return total;
    }

    private static double TitleThickness(AxisBase axis, ITextMeasurer measurer, ChartLayoutOptions options) =>
        string.IsNullOrEmpty(axis.Title) ? 0 : measurer.LineHeight + options.LabelGap;

    private static double HorizontalThickness(AxisBase axis, ITextMeasurer measurer, ChartLayoutOptions options) => axis.IsHidden ? 0 :
        options.TickLength + options.LabelGap + axis.MeasureLabelHeight(measurer) + TitleThickness(axis, measurer, options);

    private static double VerticalThickness(AxisBase axis, double length, ITextMeasurer measurer, ChartLayoutOptions options)
    {
        if (axis.IsHidden)
        {
            return 0;
        }

        var labelWidth = 0.0;
        foreach (var tick in axis.GenerateTicks(length, measurer).Major)
        {
            labelWidth = Math.Max(labelWidth, measurer.MeasureWidth(tick.Label));
        }

        // The plot must not move while a color range changes. Room for longer labels is there from the
        // start, and the width stays frozen until the axis follows the data again.
        if (axis is ColorAxis color)
        {
            labelWidth = !color.IsAutoRange && color.StableLabelLength == length
                ? color.StableLabelWidth
                : Math.Max(labelWidth, measurer.MeasureWidth(ReservedColorLabel));
            color.StableLabelWidth = labelWidth;
            color.StableLabelLength = length;
        }

        return axis.BandThickness + options.TickLength + options.LabelGap + labelWidth + TitleThickness(axis, measurer, options);
    }

    /// <summary>
    /// Narrows the plot until the centered labels at the ends of the horizontal axes fit into the chart.
    /// </summary>
    private static (double Left, double Right) KeepLabelsInside(
        IReadOnlyList<AxisBase> axes,
        double left,
        double right,
        double chartWidth,
        ITextMeasurer measurer)
    {
        var overhangLeft = 0.0;
        var overhangRight = 0.0;
        foreach (var axis in axes)
        {
            if (axis.IsHidden)
            {
                continue;
            }

            var ticks = axis.GenerateTicks(right - left, measurer).Major;
            if (ticks.Count == 0)
            {
                continue;
            }

            var transform = axis.CreateTransform(left, right - left);
            foreach (var tick in new[] { ticks[0], ticks[^1] })
            {
                var position = transform.ToPixel(tick.Value);
                var halfWidth = measurer.MeasureWidth(tick.Label) / 2;
                overhangLeft = Math.Max(overhangLeft, halfWidth - position);
                overhangRight = Math.Max(overhangRight, position + halfWidth - chartWidth);
            }
        }

        var narrowedLeft = left + overhangLeft;
        return (narrowedLeft, Math.Max(right - overhangRight, narrowedLeft));
    }

    private static double[] DistributeHeights(double available, IReadOnlyList<LayoutPane> panes, double paneGap)
    {
        var free = Math.Max(available - ((panes.Count - 1) * paneGap), 0);
        var totalWeight = panes.Sum(pane => pane.Weight);
        var heights = new double[panes.Count];
        var remaining = free;
        for (var i = 0; i < heights.Length - 1; i++)
        {
            heights[i] = free * panes[i].Weight / totalWeight;
            remaining -= heights[i];
        }

        // The rest instead of a share: rounding must not change the sum.
        heights[^1] = Math.Max(remaining, 0);
        return heights;
    }

    /// <summary>
    /// Finds the width per column of a side. Axes in one column of different panes share it, so that the panes align.
    /// </summary>
    private static double[] MeasureColumns(
        IReadOnlyList<LayoutPane> panes,
        double[] heights,
        AxisPosition side,
        ITextMeasurer measurer,
        ChartLayoutOptions options)
    {
        var columns = new List<double>();
        for (var pane = 0; pane < panes.Count; pane++)
        {
            var column = 0;
            foreach (var axis in panes[pane].Axes)
            {
                if (axis.Position != side)
                {
                    continue;
                }

                var thickness = VerticalThickness(axis, heights[pane], measurer, options);
                if (column == columns.Count)
                {
                    columns.Add(thickness);
                }
                else
                {
                    columns[column] = Math.Max(columns[column], thickness);
                }

                column++;
            }
        }

        return [.. columns];
    }

    private static void PlaceVertical(
        LayoutPane pane,
        DipRect bounds,
        double[] leftColumns,
        double[] rightColumns,
        ChartLayoutOptions options,
        List<AxisPlacement> placements)
    {
        var leftEdge = bounds.X;
        var rightEdge = bounds.Right;
        var leftColumn = 0;
        var rightColumn = 0;
        foreach (var axis in pane.Axes)
        {
            if (axis.Position == AxisPosition.Left)
            {
                var width = leftColumns[leftColumn++];
                leftEdge -= width + options.AxisGap;
                placements.Add(new AxisPlacement(axis, new DipRect(leftEdge, bounds.Y, width, bounds.Height)));
            }
            else
            {
                var width = rightColumns[rightColumn++];
                placements.Add(new AxisPlacement(axis, new DipRect(rightEdge + options.AxisGap, bounds.Y, width, bounds.Height)));
                rightEdge += width + options.AxisGap;
            }
        }
    }

    private static void PlaceHorizontal(
        AxisBase[] axes,
        double[] thicknesses,
        DipRect plotArea,
        bool isTop,
        ChartLayoutOptions options,
        List<AxisPlacement> placements)
    {
        var edge = isTop ? plotArea.Y : plotArea.Bottom;
        for (var i = 0; i < axes.Length; i++)
        {
            if (isTop)
            {
                edge -= thicknesses[i] + options.AxisGap;
                placements.Add(new AxisPlacement(axes[i], new DipRect(plotArea.X, edge, plotArea.Width, thicknesses[i])));
            }
            else
            {
                placements.Add(new AxisPlacement(axes[i], new DipRect(plotArea.X, edge + options.AxisGap, plotArea.Width, thicknesses[i])));
                edge += thicknesses[i] + options.AxisGap;
            }
        }
    }
}
