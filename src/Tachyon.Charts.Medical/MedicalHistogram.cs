using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

/// <summary>
/// The distribution of the values of a region or of the whole slice, as a 2D chart beside the image, with the grayscale
/// window as a band. The bins span all values of the image, so that histograms of different regions compare directly;
/// the view shows the window and half its width on each side, and zooming shows the rest. The histogram follows the
/// region while it is drawn or moved, the slice while it changes, and band and view follow the window.
/// Without a chosen <see cref="Region"/> it shows the region last touched, or the whole slice when there is none.
/// </summary>
public sealed class MedicalHistogram
{
    private readonly MedicalMeasurementLayer _layer;
    private readonly double[] _edges;
    private readonly double[] _counts;
    private readonly List<BarSeries> _bars = [];
    private readonly List<BandAnnotation> _bands = [];
    private readonly List<Core.Axes.AxisBase> _axes = [];
    private MedicalMeasurement? _region;

    /// <summary>Initializes a histogram of a single image.</summary>
    /// <param name="display">The image.</param>
    /// <param name="bins">The number of bins.</param>
    public MedicalHistogram(MedicalImageDisplay display, int bins = 512)
        : this((display ?? throw new ArgumentNullException(nameof(display))).Measurements, bins)
    {
    }

    /// <summary>Initializes a histogram of one plane of a volume.</summary>
    /// <param name="slice">The plane.</param>
    /// <param name="bins">The number of bins.</param>
    public MedicalHistogram(MedicalMprSlice slice, int bins = 512)
        : this((slice ?? throw new ArgumentNullException(nameof(slice))).Measurements, bins)
    {
    }

    private MedicalHistogram(MedicalMeasurementLayer layer, int bins)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bins, 2);
        _layer = layer;
        var range = layer.Source.ValueRange;
        var (low, high) = range.Length > 0 && double.IsFinite(range.Length) ? (range.Min, range.Max) : (range.Min - 0.5, range.Min + 0.5);
        _edges = new double[bins + 1];
        for (var i = 0; i <= bins; i++)
        {
            _edges[i] = low + ((high - low) * i / bins);
        }

        _counts = new double[bins];
        layer.Changed += _ => Update();
        layer.Source.SliceChanged += Update;
        layer.Source.WindowChanged += ShowWindow;
        Update();
    }

    /// <summary>Gets or sets the region to show, or <see langword="null"/> for the one last touched or the whole slice.</summary>
    /// <exception cref="ArgumentException">The measurement is no region.</exception>
    public MedicalMeasurement? Region
    {
        get => _region;
        set
        {
            if (value is { IsRegion: false })
            {
                throw new ArgumentException("A histogram shows a circle, a rectangle or a freehand region.", nameof(value));
            }

            _region = value;
            Update();
        }
    }

    /// <summary>Gets the region shown now, or <see langword="null"/> for the whole slice.</summary>
    public MedicalMeasurement? Shown { get; private set; }

    /// <summary>Gets the histogram shown now.</summary>
    public Histogram Current { get; private set; } = new([0, 1], [0]);

    /// <summary>Adds the histogram chart to a chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure(ChartBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.Title("Histogram")
            .XAxis(axis => axis.Numeric("medical-histogram-x").Title(_layer.Unit).Range(View(_layer.Source.Window).Min, View(_layer.Source.Window).Max).Limits(_edges[0], _edges[^1]))
            .YAxis(axis => axis.Numeric("medical-histogram-y").Title("Pixels").AutoRange())
            .AddSeries((x, y) =>
            {
                var bars = BarSeries.FromHistogram(Current, x, y);
                _bars.Add(bars);
                return bars;
            })
            .AddOverlay(surface =>
            {
                var band = new BandAnnotation(surface.HorizontalAxes[0]) { Color = new Rgba8(80, 220, 255, 45) };
                band.SetRange(_layer.Source.Window.Minimum, _layer.Source.Window.Maximum);
                _bands.Add(band);
                _axes.Add(surface.HorizontalAxes[0]);
                return band;
            })
            .Modifiers(modifiers => modifiers.ZoomPan().Tooltip());
    }

    // The values around the window matter for windowing, so the view shows the window and half its width on each side.
    private void ShowWindow(MedicalWindow window)
    {
        foreach (var band in _bands)
        {
            band.SetRange(window.Minimum, window.Maximum);
        }

        foreach (var axis in _axes)
        {
            axis.VisibleRange = View(window);
        }
    }

    private DoubleRange View(MedicalWindow window)
    {
        var low = Math.Max(window.Minimum - (window.Width / 2), _edges[0]);
        var high = Math.Min(window.Maximum + (window.Width / 2), _edges[^1]);
        return high > low ? new DoubleRange(low, high) : new DoubleRange(_edges[0], _edges[^1]);
    }

    // Counts the pixels of the region, or of the whole slice, into the fixed bins.
    private void Update()
    {
        Shown = _region ?? (_layer.Selected is { IsRegion: true } selected ? selected : null);
        Array.Clear(_counts);
        var source = _layer.Source;
        if (Shown is { } region)
        {
            MedicalMeasurement.ForEachInside(region.OutlineSnapshot, region.Slice, source, Count);
        }
        else
        {
            var slice = source.Slice;
            for (var row = 0; row < source.Height; row++)
            {
                for (var column = 0; column < source.Width; column++)
                {
                    Count(source.Read(slice, column, row));
                }
            }
        }

        Current = new Histogram((double[])_edges.Clone(), Array.ConvertAll(_counts, count => (long)count));
        foreach (var bars in _bars)
        {
            bars.SetValues([(double[])_counts.Clone()]);
        }
    }

    private void Count(double value)
    {
        if (!double.IsFinite(value))
        {
            return;
        }

        var bins = _counts.Length;
        var index = (int)Math.Floor((value - _edges[0]) / (_edges[^1] - _edges[0]) * bins);
        _counts[Math.Clamp(index, 0, bins - 1)]++;
    }
}
