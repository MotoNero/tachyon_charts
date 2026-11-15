using System.Globalization;
using Tachyon.Charts.Core.Pipeline;

namespace Tachyon.Charts.Core.Overlays;

/// <summary>A series whose picture refines while its data are read, such as a line over a file larger than memory.</summary>
internal interface IRefiningSeries
{
    /// <summary>Gets how far the data are read, from 0 to 1.</summary>
    double Progress { get; }
}

/// <summary>
/// A small plate in the lower left corner of the plot while a series still refines its picture, such as a line over a
/// column file whose pyramid is being built: „Refining 37 %“. It disappears when all are complete.
/// </summary>
/// <param name="chart">The chart whose series it watches.</param>
public sealed class RefiningOverlay(ChartSurface chart) : ChartOverlay
{
    private const double Margin = 8;

    private readonly ChartSurface _chart = chart ?? throw new ArgumentNullException(nameof(chart));
    private string _format = "Refining {0:P0}";
    private string _label = string.Empty;
    private int _labelPercent = -1;

    /// <summary>Gets or sets the text, with <c>{0}</c> for the share read so far, such as <c>Refining {0:P0}</c>.</summary>
    public string Format
    {
        get => _format;
        set
        {
            _format = value ?? throw new ArgumentNullException(nameof(value));
            _labelPercent = -1;
            OnChanged();
        }
    }

    /// <summary>Gets the share read so far of the series that is furthest behind, or 1 when all are complete.</summary>
    public double Progress
    {
        get
        {
            var least = 1.0;
            var series = _chart.Series;
            for (var i = 0; i < series.Count; i++)
            {
                if (series[i] is IRefiningSeries refining && series[i].IsVisible)
                {
                    least = Math.Min(least, refining.Progress);
                }
            }

            return least;
        }
    }

    /// <inheritdoc />
    protected internal override long ExternalVersion => (long)Math.Floor(Progress * 100);

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var progress = Progress;
        if (progress >= 1)
        {
            return;
        }

        // The text changes once per percent, not per frame.
        var percent = (int)Math.Floor(progress * 100);
        if (percent != _labelPercent)
        {
            (_label, _labelPercent) = (string.Format(CultureInfo.CurrentCulture, _format, percent / 100.0), percent);
        }

        var plot = canvas.Layout.PlotArea;
        var height = canvas.GetLineHeight(canvas.LabelSize) + 8;
        canvas.DrawLabel(_label, plot.X + Margin, plot.Bottom - Margin - height, canvas.BackgroundColor with { A = 220 }, canvas.TextColor);
    }
}
