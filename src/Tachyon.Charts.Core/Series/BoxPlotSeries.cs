using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// What a box plot shows of a sample. Quartiles follow NumPy, whiskers reach the outermost values within
/// 1.5 interquartile ranges.
/// </summary>
/// <param name="FirstQuartile">The 25th percentile.</param>
/// <param name="Median">The median.</param>
/// <param name="ThirdQuartile">The 75th percentile.</param>
/// <param name="LowerWhisker">The lowest value inside the fences.</param>
/// <param name="UpperWhisker">The highest value inside the fences.</param>
/// <param name="Outliers">The values outside of the fences, rising.</param>
public sealed record BoxStatistics(double FirstQuartile, double Median, double ThirdQuartile, double LowerWhisker, double UpperWhisker, double[] Outliers)
{
    /// <summary>Computes the statistics. NaN and infinities are left out.</summary>
    /// <param name="values">The sample.</param>
    /// <returns>The statistics, or <see langword="null"/> without any value.</returns>
    public static BoxStatistics? Compute(ReadOnlySpan<double> values)
    {
        var sorted = new List<double>(values.Length);
        foreach (var value in values)
        {
            if (double.IsFinite(value))
            {
                sorted.Add(value);
            }
        }

        if (sorted.Count == 0)
        {
            return null;
        }

        sorted.Sort();
        var first = Histogram.Quantile(sorted, 0.25);
        var median = Histogram.Quantile(sorted, 0.5);
        var third = Histogram.Quantile(sorted, 0.75);
        var fence = 1.5 * (third - first);
        var inside = sorted.Where(value => value >= first - fence && value <= third + fence).ToArray();
        var outliers = sorted.Where(value => value < first - fence || value > third + fence).ToArray();
        return new BoxStatistics(first, median, third, inside[0], inside[^1], outliers);
    }
}

/// <summary>
/// A kernel density estimate with a Gaussian kernel and the bandwidth of Scott.
/// </summary>
public static class KernelDensity
{
    /// <summary>Finds the bandwidth: the standard deviation times n^(-1/5).</summary>
    /// <param name="values">The sample.</param>
    /// <returns>The bandwidth, zero with fewer than two different values.</returns>
    public static double Bandwidth(ReadOnlySpan<double> values)
    {
        if (values.Length < 2)
        {
            return 0;
        }

        var mean = 0.0;
        foreach (var value in values)
        {
            mean += value;
        }

        mean /= values.Length;
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += (value - mean) * (value - mean);
        }

        return Math.Sqrt(sum / (values.Length - 1)) * Math.Pow(values.Length, -0.2);
    }

    /// <summary>Estimates the density at a position.</summary>
    /// <param name="values">The sample.</param>
    /// <param name="bandwidth">The bandwidth, positive.</param>
    /// <param name="position">The position.</param>
    /// <returns>The density.</returns>
    public static double Estimate(ReadOnlySpan<double> values, double bandwidth, double position)
    {
        var sum = 0.0;
        foreach (var value in values)
        {
            var distance = (position - value) / bandwidth;
            sum += Math.Exp(-0.5 * distance * distance);
        }

        return sum / (values.Length * bandwidth * Math.Sqrt(2 * Math.PI));
    }
}

/// <summary>
/// Samples at the places 0, 1, 2 and so on, for a <see cref="CategoryAxis"/> or numbers.
/// </summary>
public abstract class SampleSeries : RibbonSeries
{
    private double[]? _medians;
    private readonly double[][] _samples;
    private double _width = 0.6;
    private SeriesFill _fill = new SolidFill(new Rgba8(76, 201, 240, 170));
    private Rgba8 _lineColor = new(235, 240, 245, 255);

    /// <summary>Initializes a new instance of the <see cref="SampleSeries"/> class.</summary>
    /// <param name="samples">The samples.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    private protected SampleSeries(IReadOnlyList<double[]> samples, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _samples = [.. samples];
    }

    /// <summary>Gets or sets the share of a place the shapes fill, from 0 to 1.</summary>
    public double Width
    {
        get => _width;
        set
        {
            _width = Math.Clamp(value, 0.05, 1);
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the fill.</summary>
    public SeriesFill Fill
    {
        get => _fill;
        set
        {
            _fill = value ?? throw new ArgumentNullException(nameof(value));
            OnAppearanceChanged();
        }
    }

    /// <summary>Gets or sets the color of the lines.</summary>
    public Rgba8 LineColor
    {
        get => _lineColor;
        set
        {
            _lineColor = value;
            OnAppearanceChanged();
        }
    }

    /// <inheritdoc />
    protected internal override void CollectValueLabels(DoubleRange x, DoubleRange y, List<ValueLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        // The groups do not change, so their medians are computed once.
        _medians ??= [.. _samples.Select(sample => BoxStatistics.Compute(sample)?.Median ?? double.NaN)];
        for (var i = 0; i < _medians.Length; i++)
        {
            if (double.IsFinite(_medians[i]) && x.Contains(i) && y.Contains(_medians[i]))
            {
                labels.Add(new ValueLabel(i + (Width / 2), _medians[i], _medians[i], ValueLabelPlace.Right));
            }
        }
    }

    /// <summary>Gets the samples.</summary>
    protected IReadOnlyList<double[]> Samples => _samples;

    /// <inheritdoc />
    protected internal override long Version => AppearanceVersion;

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = _samples.Length == 0 ? DoubleRange.Empty : new DoubleRange(-0.5, _samples.Length - 0.5);
        y = DoubleRange.Empty;
        foreach (var sample in _samples)
        {
            foreach (var value in sample)
            {
                if (double.IsFinite(value))
                {
                    y = y.Union(new DoubleRange(value, value));
                }
            }
        }

        return !y.IsEmpty;
    }

    /// <inheritdoc />
    protected override ReadOnlyMemory<double> ReadX() => ReadOnlyMemory<double>.Empty;

    /// <summary>Adds a line of whole pixels.</summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="mesh">Receives the triangles.</param>
    /// <param name="left">The left end in pixels.</param>
    /// <param name="right">The right end in pixels.</param>
    /// <param name="top">The upper end in pixels.</param>
    /// <param name="bottom">The lower end in pixels.</param>
    protected void AddLine(in SeriesFrame frame, List<MeshVertex> mesh, double left, double right, double top, double bottom)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var thickness = (float)Math.Max(Math.Round(frame.DpiScale), 1);
        var x = (float)Math.Round(left);
        var y = (float)Math.Round(top);
        var width = Math.Max((float)Math.Round(right) - x, thickness);
        var height = Math.Max((float)Math.Round(bottom) - y, thickness);
        MeshShapes.AddRoundedRectangle(mesh, x, y, width, height, 0, frame.Bounds, new SolidFill(_lineColor), 0);
    }
}

/// <summary>
/// Box plots: quartiles as a box, the median as a line, whiskers and outliers.
/// </summary>
public sealed class BoxPlotSeries : SampleSeries
{
    private const double OutlierSize = 2;

    /// <summary>Initializes a new instance of the <see cref="BoxPlotSeries"/> class.</summary>
    /// <param name="samples">The samples, one box each.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public BoxPlotSeries(IReadOnlyList<double[]> samples, AxisBase xAxis, AxisBase yAxis)
        : base(samples, xAxis, yAxis)
    {
    }

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        for (var i = 0; i < Samples.Count; i++)
        {
            if (BoxStatistics.Compute(Samples[i]) is not { } statistics)
            {
                continue;
            }

            var center = frame.X.ToPixel(i);
            var half = (frame.X.ToPixel(i + Width) - center) / 2;
            var top = frame.Y.ToPixel(statistics.ThirdQuartile);
            var bottom = frame.Y.ToPixel(statistics.FirstQuartile);
            var median = frame.Y.ToPixel(statistics.Median);
            var upper = frame.Y.ToPixel(statistics.UpperWhisker);
            var lower = frame.Y.ToPixel(statistics.LowerWhisker);

            AddLine(frame, mesh, center, center, upper, top);
            AddLine(frame, mesh, center, center, bottom, lower);
            AddLine(frame, mesh, center - (half / 2), center + (half / 2), upper, upper);
            AddLine(frame, mesh, center - (half / 2), center + (half / 2), lower, lower);
            var left = (float)Math.Round(center - half);
            var boxTop = (float)Math.Round(Math.Min(top, bottom));
            MeshShapes.AddRoundedRectangle(mesh, left, boxTop, (float)Math.Round(center + half) - left, (float)Math.Round(Math.Max(top, bottom)) - boxTop, 0, frame.Bounds, Fill, statistics.Median);
            AddLine(frame, mesh, center - half, center + half, median, median);

            foreach (var outlier in statistics.Outliers)
            {
                var y = frame.Y.ToPixel(outlier);
                AddLine(frame, mesh, center - OutlierSize, center + OutlierSize, y - OutlierSize, y + OutlierSize);
            }
        }
    }
}

/// <summary>
/// Violin plots: the mirrored density of each sample, with a line at the median.
/// </summary>
public sealed class ViolinSeries : SampleSeries
{
    private const int Steps = 64;
    private const double TailBandwidths = 3;

    /// <summary>Initializes a new instance of the <see cref="ViolinSeries"/> class.</summary>
    /// <param name="samples">The samples, one violin each.</param>
    /// <param name="xAxis">The horizontal axis.</param>
    /// <param name="yAxis">The vertical axis.</param>
    public ViolinSeries(IReadOnlyList<double[]> samples, AxisBase xAxis, AxisBase yAxis)
        : base(samples, xAxis, yAxis)
    {
    }

    /// <inheritdoc />
    protected override void Build(in SeriesFrame frame, List<MeshVertex> mesh)
    {
        for (var i = 0; i < Samples.Count; i++)
        {
            var values = Samples[i].Where(double.IsFinite).ToArray();
            var bandwidth = KernelDensity.Bandwidth(values);
            if (bandwidth <= 0)
            {
                continue;
            }

            var center = frame.X.ToPixel(i);
            var half = (frame.X.ToPixel(i + Width) - center) / 2;
            var start = values.Min() - (TailBandwidths * bandwidth);
            var end = values.Max() + (TailBandwidths * bandwidth);
            var densities = new double[Steps + 1];
            for (var step = 0; step <= Steps; step++)
            {
                densities[step] = KernelDensity.Estimate(values, bandwidth, start + ((end - start) * step / Steps));
            }

            var peak = densities.Max();
            for (var step = 0; step < Steps; step++)
            {
                var low = start + ((end - start) * step / Steps);
                var high = start + ((end - start) * (step + 1) / Steps);
                var a = new Vector2((float)(center - (half * densities[step] / peak)), (float)frame.Y.ToPixel(low));
                var b = new Vector2((float)(center + (half * densities[step] / peak)), a.Y);
                var c = new Vector2((float)(center + (half * densities[step + 1] / peak)), (float)frame.Y.ToPixel(high));
                var d = new Vector2((float)(center - (half * densities[step + 1] / peak)), c.Y);
                var lower = Fill.GetColor((a.Y - frame.Bounds.Y) / frame.Bounds.Height, low);
                var upper = Fill.GetColor((c.Y - frame.Bounds.Y) / frame.Bounds.Height, high);
                mesh.Add(new MeshVertex(a, lower));
                mesh.Add(new MeshVertex(b, lower));
                mesh.Add(new MeshVertex(c, upper));
                mesh.Add(new MeshVertex(a, lower));
                mesh.Add(new MeshVertex(c, upper));
                mesh.Add(new MeshVertex(d, upper));
            }

            var median = frame.Y.ToPixel(BoxStatistics.Compute(values)!.Median);
            AddLine(frame, mesh, center - (half * 0.4), center + (half * 0.4), median, median);
        }
    }
}
