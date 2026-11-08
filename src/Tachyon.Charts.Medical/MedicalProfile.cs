using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;

namespace Tachyon.Charts.Medical;

/// <summary>
/// The values along a distance measurement, as a 2D chart beside the image: the distance from the start of the line in
/// millimetres across, the value of the image upwards. The profile follows the line while it is drawn or moved; without
/// a chosen <see cref="Line"/> it shows the distance last touched, or else the last one on the slice shown.
/// </summary>
public sealed class MedicalProfile
{
    private const int MostSamples = 4096;

    private readonly MedicalMeasurementLayer _layer;
    private readonly DataSource<double, double> _data = new(MostSamples);
    private MedicalMeasurement? _line;
    private MedicalMeasurement? _shown;

    /// <summary>Initializes a profile of a single image.</summary>
    /// <param name="display">The image.</param>
    public MedicalProfile(MedicalImageDisplay display)
        : this((display ?? throw new ArgumentNullException(nameof(display))).Measurements)
    {
    }

    /// <summary>Initializes a profile of one plane of a volume.</summary>
    /// <param name="slice">The plane.</param>
    public MedicalProfile(MedicalMprSlice slice)
        : this((slice ?? throw new ArgumentNullException(nameof(slice))).Measurements)
    {
    }

    private MedicalProfile(MedicalMeasurementLayer layer)
    {
        _layer = layer;
        _layer.Changed += _ => Update();
        _layer.Source.SliceChanged += Update;
        Update();
    }

    /// <summary>Gets or sets the distance measurement to follow, or <see langword="null"/> for the one last touched.</summary>
    /// <exception cref="ArgumentException">The measurement is no distance.</exception>
    public MedicalMeasurement? Line
    {
        get => _line;
        set
        {
            if (value is { Kind: not MedicalMeasurementKind.Distance })
            {
                throw new ArgumentException("A profile follows a distance measurement.", nameof(value));
            }

            _line = value;
            Update();
        }
    }

    /// <summary>Gets the distance measurement shown now, or <see langword="null"/> when there is none.</summary>
    public MedicalMeasurement? Shown => _shown;

    /// <summary>Gets the samples: the distance from the start of the line in millimetres and the value there.</summary>
    public DataSource<double, double> Data => _data;

    /// <summary>Adds the profile chart to a chart description.</summary>
    /// <param name="chart">The chart description.</param>
    public void Configure(ChartBuilder chart)
    {
        ArgumentNullException.ThrowIfNull(chart);
        chart.Title("Profile")
            .XAxis(axis => axis.Numeric("medical-profile-x").Title("mm").AutoRange())
            .YAxis(axis => axis.Numeric("medical-profile-y").Title(_layer.Unit).AutoRange())
            .AddLine(_data, line => line.Name(_layer.Unit).Thickness(1.5))
            .Modifiers(modifiers => modifiers.ZoomPan().Crosshair(values: true));
    }

    // Samples at half the smaller pixel size, blended from the four nearest pixels.
    private void Update()
    {
        _shown = _line ?? Pick();
        _data.Clear();
        if (_shown is not { } line || line.Slice != _layer.Source.Slice && _line is null)
        {
            return;
        }

        var source = _layer.Source;
        var (start, end) = (line.Snapshot[0], line.Snapshot[1]);
        var length = Math.Sqrt(((end.X - start.X) * (end.X - start.X)) + ((end.Y - start.Y) * (end.Y - start.Y)));
        var step = Math.Min(source.Spacing.Column, source.Spacing.Row) / 2;
        var count = Math.Clamp((int)Math.Ceiling(length / step) + 1, 2, MostSamples);
        var (along, values) = (new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            var share = i / (double)(count - 1);
            along[i] = share * length;
            values[i] = Sample(source, line.Slice, start.X + ((end.X - start.X) * share), start.Y + ((end.Y - start.Y) * share));
        }

        _data.Append(along, values);
    }

    private MedicalMeasurement? Pick()
    {
        if (_layer.Selected is { Kind: MedicalMeasurementKind.Distance } selected)
        {
            return selected;
        }

        var measurements = _layer.Measurements;
        for (var i = measurements.Count - 1; i >= 0; i--)
        {
            if (measurements[i].Kind == MedicalMeasurementKind.Distance && measurements[i].Slice == _layer.Source.Slice)
            {
                return measurements[i];
            }
        }

        return null;
    }

    /// <summary>Reads a value at a place in millimetres, blended from the four nearest pixel middles; NaN outside the image.</summary>
    internal static double Sample(IMedicalMeasurementSource source, int slice, double x, double y)
    {
        var column = (x / source.Spacing.Column) - 0.5;
        var row = (y / source.Spacing.Row) - 0.5;
        if (column < -0.5 || row < -0.5 || column > source.Width - 0.5 || row > source.Height - 0.5)
        {
            return double.NaN;
        }

        var (left, top) = (Math.Clamp((int)Math.Floor(column), 0, source.Width - 1), Math.Clamp((int)Math.Floor(row), 0, source.Height - 1));
        var (right, bottom) = (Math.Min(left + 1, source.Width - 1), Math.Min(top + 1, source.Height - 1));
        var (u, v) = (Math.Clamp(column - left, 0, 1), Math.Clamp(row - top, 0, 1));
        var upper = (source.Read(slice, left, top) * (1 - u)) + (source.Read(slice, right, top) * u);
        var lower = (source.Read(slice, left, bottom) * (1 - u)) + (source.Read(slice, right, bottom) * u);
        return (upper * (1 - v)) + (lower * v);
    }
}
