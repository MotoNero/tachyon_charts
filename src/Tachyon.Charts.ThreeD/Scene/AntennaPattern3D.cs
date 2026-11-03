using System.Globalization;
using Tachyon.Charts.Core.Colors;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>The main lobe of an antenna.</summary>
/// <param name="Azimuth">The direction in degrees.</param>
/// <param name="Elevation">The height of the direction in degrees.</param>
/// <param name="Gain">The highest gain in dB.</param>
/// <param name="AzimuthWidth">The width in degrees between the points 3 dB below the peak, or NaN when the data does not reach them.</param>
/// <param name="ElevationWidth">The same width across the elevation.</param>
public readonly record struct AntennaLobe(double Azimuth, double Elevation, double Gain, double AzimuthWidth, double ElevationWidth);

/// <summary>A cut through a pattern, ready for a <see cref="Core.Series.PolarSeries"/>.</summary>
/// <param name="Angles">The angles in degrees from 0 to 360.</param>
/// <param name="Values">The gain in dB above the floor, so that the center of a polar chart is the floor.</param>
public readonly record struct AntennaCut(double[] Angles, double[] Values);

/// <summary>
/// The gain of an antenna over azimuth and elevation as a shape around the center: the distance from the center is
/// the gain in dB above the floor, and the axes count in the same dB.
/// </summary>
public sealed class AntennaPattern3D
{
    private const double Tolerance = 1e-6;
    private const double FullCircle = 360;
    private const double Pole = 90;
    private const double HalfPower = 3;
    private const double MarkReach = 1.15;
    private const double RingLift = 1.02;
    private const int RingSteps = 48;

    private readonly int _columns;
    private readonly bool _isRound;
    private AntennaLobe? _lobe;

    /// <summary>Initializes a new instance of the <see cref="AntennaPattern3D"/> class.</summary>
    /// <param name="azimuths">The azimuths in degrees, rising. A last value one turn after the first counts as the same direction.</param>
    /// <param name="elevations">The elevations in degrees from -90 to 90, rising.</param>
    /// <param name="gains">The gains in dB, first index the elevation, second index the azimuth. NaN marks a missing value.</param>
    public AntennaPattern3D(double[] azimuths, double[] elevations, double[,] gains)
    {
        Azimuths = azimuths ?? throw new ArgumentNullException(nameof(azimuths));
        Elevations = elevations ?? throw new ArgumentNullException(nameof(elevations));
        Gains = gains ?? throw new ArgumentNullException(nameof(gains));
        if (gains.GetLength(0) != elevations.Length || gains.GetLength(1) != azimuths.Length)
        {
            throw new ArgumentException("The gains need one row per elevation and one column per azimuth.", nameof(gains));
        }

        _columns = azimuths.Length;
        if (azimuths.Length >= 2)
        {
            var span = azimuths[^1] - azimuths[0];
            _isRound = span + (span / (azimuths.Length - 1)) >= FullCircle - Tolerance;
            _columns -= span >= FullCircle - Tolerance ? 1 : 0;
        }
    }

    /// <summary>Gets the azimuths in degrees.</summary>
    public double[] Azimuths { get; }

    /// <summary>Gets the elevations in degrees.</summary>
    public double[] Elevations { get; }

    /// <summary>Gets the gains in dB.</summary>
    public double[,] Gains { get; }

    /// <summary>Gets the colors from the floor to the peak.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Viridis;

    /// <summary>Gets how the shape takes the light.</summary>
    public Material3D Material { get; init; } = new();

    /// <summary>Gets how many dB below the peak the floor lies. Weaker directions shrink to the center.</summary>
    public double DynamicRange { get; init; } = 40;

    /// <summary>Gets the gain at the center of the shape in dB.</summary>
    public double Floor => MainLobe.Gain - DynamicRange;

    /// <summary>Gets the main lobe.</summary>
    public AntennaLobe MainLobe => _lobe ??= FindLobe();

    /// <summary>Gets the distance from the center that the axes need to reach.</summary>
    internal double Reach => double.IsFinite(MainLobe.Gain) ? DynamicRange * MarkReach * RingLift : 1;

    /// <summary>Gets the end of the mark over the peak.</summary>
    internal Point3D MarkTip => ToPoint(MainLobe.Azimuth, MainLobe.Elevation, DynamicRange * MarkReach);

    /// <summary>Gets the text at the mark.</summary>
    internal string LobeText => string.Create(CultureInfo.InvariantCulture, $"{MainLobe.Gain:0.0} dB, {MainLobe.AzimuthWidth:0.0}° × {MainLobe.ElevationWidth:0.0}°");

    /// <summary>Gets the top of the scale.</summary>
    internal string PeakText => string.Create(CultureInfo.InvariantCulture, $"{MainLobe.Gain:0.#} dB");

    /// <summary>Gets the bottom of the scale.</summary>
    internal string FloorText => string.Create(CultureInfo.InvariantCulture, $"{Floor:0.#} dB");

    private static Point3D ToPoint(double azimuth, double elevation, double radius)
    {
        var (a, e) = (double.DegreesToRadians(azimuth), double.DegreesToRadians(elevation));
        return new Point3D(radius * Math.Cos(e) * Math.Cos(a), radius * Math.Cos(e) * Math.Sin(a), radius * Math.Sin(e));
    }

    private static double Side(double[] angles, double[] values, int peak, int direction, bool isRound, double level)
    {
        var (index, reach) = (peak, 0.0);
        for (var i = 1; i < angles.Length; i++)
        {
            var next = index + direction;
            if (isRound)
            {
                next = (next + angles.Length) % angles.Length;
            }
            else if (next < 0 || next >= angles.Length)
            {
                return double.NaN;
            }

            var step = ((direction * (angles[next] - angles[index]) % FullCircle) + FullCircle) % FullCircle;
            if (values[next] < level)
            {
                return reach + (step * (values[index] - level) / (values[index] - values[next]));
            }

            (index, reach) = (next, reach + step);
        }

        return double.NaN;
    }

    private static int FindNearest(double[] angles, int count, double angle, bool isRound)
    {
        var (best, found) = (double.PositiveInfinity, 0);
        for (var i = 0; i < count; i++)
        {
            var distance = Math.Abs(angles[i] - angle);
            distance = isRound ? Math.Abs(((distance + (FullCircle / 2)) % FullCircle) - (FullCircle / 2)) : distance;
            if (distance < best)
            {
                (best, found) = (distance, i);
            }
        }

        return found;
    }

    private static double ToTurn(double angle) => ((angle % FullCircle) + FullCircle) % FullCircle;

    /// <summary>Gets the cut at one elevation, around the azimuth.</summary>
    /// <param name="elevation">The elevation in degrees. The nearest measured one is taken.</param>
    /// <returns>The cut, closed when the data goes all around.</returns>
    public AntennaCut AzimuthCut(double elevation)
    {
        var row = FindNearest(Elevations, Elevations.Length, elevation, false);
        var count = _columns + (_isRound ? 1 : 0);
        var (angles, values) = (new double[count], new double[count]);
        for (var i = 0; i < count; i++)
        {
            var column = i % _columns;
            (angles[i], values[i]) = (ToTurn(Azimuths[column]), AboveFloor(Gains[row, column]));
        }

        return new AntennaCut(angles, values);
    }

    /// <summary>Gets the great circle through one azimuth and the two poles.</summary>
    /// <param name="azimuth">The azimuth in degrees. The nearest measured one is taken, and the opposite one closes the circle.</param>
    /// <returns>The cut. The angle counts the elevation, so the front lies at 0° and the back at 180°.</returns>
    public AntennaCut ElevationCut(double azimuth)
    {
        var front = FindNearest(Azimuths, _columns, azimuth, true);
        var back = FindNearest(Azimuths, _columns, azimuth + (FullCircle / 2), true);
        var rows = Elevations.Length;
        var (angles, values) = (new double[2 * rows], new double[2 * rows]);
        for (var i = 0; i < rows; i++)
        {
            (angles[i], values[i]) = (ToTurn(Elevations[i]), AboveFloor(Gains[i, front]));
            var turned = rows - 1 - i;
            (angles[rows + i], values[rows + i]) = (ToTurn((FullCircle / 2) - Elevations[turned]), AboveFloor(Gains[turned, back]));
        }

        return new AntennaCut(angles, values);
    }

    /// <summary>
    /// Creates the shape. Each pole is one point with the mean of its ring, so that it can neither form a needle nor
    /// a hole, and the shape closes over the seam of the azimuth.
    /// </summary>
    /// <returns>The surface in dB above the floor.</returns>
    internal Mesh3D CreateMesh()
    {
        if (_columns < 2 || Elevations.Length < 2 || !double.IsFinite(MainLobe.Gain))
        {
            return new Mesh3D([], []) { Material = Material };
        }

        var first = _isRound && Elevations[0] <= -Pole + Tolerance ? 1 : 0;
        var last = Elevations.Length - (_isRound && Elevations[^1] >= Pole - Tolerance ? 2 : 1);
        var rows = last - first + 1;
        var vertices = new List<Vertex3D>((rows * _columns) + 2);
        var indices = new List<int>(6 * rows * _columns);
        for (var row = first; row <= last; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                vertices.Add(CreateVertex(Azimuths[column], Elevations[row], Gains[row, column]));
            }
        }

        AddBands(indices, rows);
        if (_isRound)
        {
            AddCap(vertices, indices, 0, -Pole, 0);
            AddCap(vertices, indices, rows - 1, Pole, Elevations.Length - 1);
        }

        return new Mesh3D([.. vertices], [.. indices]) { Material = Material };
    }

    private void AddBands(List<int> indices, int rows)
    {
        var seam = _columns - (_isRound ? 0 : 1);
        for (var row = 0; row < rows - 1; row++)
        {
            for (var column = 0; column < seam; column++)
            {
                var (a, b) = ((row * _columns) + column, (row * _columns) + ((column + 1) % _columns));
                indices.AddRange([a, b, b + _columns, a, b + _columns, a + _columns]);
            }
        }
    }

    /// <summary>Adds the mark over the peak and the ring through the half-power points, each as pairs of line ends.</summary>
    /// <param name="ends">The list that receives the line ends.</param>
    internal void AddMarks(List<Point3D> ends)
    {
        var lobe = MainLobe;
        if (!double.IsFinite(lobe.Gain))
        {
            return;
        }

        ends.Add(ToPoint(lobe.Azimuth, lobe.Elevation, DynamicRange));
        ends.Add(MarkTip);
        if (!double.IsFinite(lobe.AzimuthWidth) || !double.IsFinite(lobe.ElevationWidth))
        {
            return;
        }

        var radius = (DynamicRange - HalfPower) * RingLift;
        for (var i = 0; i < RingSteps; i++)
        {
            ends.Add(RingPoint(lobe, radius, i));
            ends.Add(RingPoint(lobe, radius, i + 1));
        }
    }

    private static Point3D RingPoint(in AntennaLobe lobe, double radius, int step)
    {
        var turn = Math.Tau * step / RingSteps;
        return ToPoint(lobe.Azimuth + (lobe.AzimuthWidth / 2 * Math.Cos(turn)), lobe.Elevation + (lobe.ElevationWidth / 2 * Math.Sin(turn)), radius);
    }

    private double AboveFloor(double gain) => double.IsNaN(gain) ? double.NaN : Math.Max(gain - Floor, 0);

    private Vertex3D CreateVertex(double azimuth, double elevation, double gain)
    {
        var radius = double.IsPositiveInfinity(gain) ? double.NaN : AboveFloor(gain);
        var color = double.IsNaN(radius) ? default : Colormap.Sample(radius / DynamicRange);
        return new Vertex3D(ToPoint(azimuth, elevation, radius), color);
    }

    // The first and last row are the ring of the pole itself when measured, otherwise the outermost measured ring.
    private void AddCap(List<Vertex3D> vertices, List<int> indices, int ring, double pole, int row)
    {
        var (sum, count) = (0.0, 0);
        for (var column = 0; column < _columns; column++)
        {
            if (double.IsFinite(Gains[row, column]))
            {
                (sum, count) = (sum + Gains[row, column], count + 1);
            }
        }

        var tip = vertices.Count;
        vertices.Add(CreateVertex(0, pole, count > 0 ? sum / count : double.NaN));
        for (var column = 0; column < _columns; column++)
        {
            var (a, b) = ((ring * _columns) + column, (ring * _columns) + ((column + 1) % _columns));
            indices.AddRange(pole > 0 ? [a, b, tip] : [tip, b, a]);
        }
    }

    private AntennaLobe FindLobe()
    {
        var (best, bestRow, bestColumn) = (double.NegativeInfinity, -1, -1);
        for (var row = 0; row < Elevations.Length; row++)
        {
            for (var column = 0; column < _columns; column++)
            {
                if (double.IsFinite(Gains[row, column]) && Gains[row, column] > best)
                {
                    (best, bestRow, bestColumn) = (Gains[row, column], row, column);
                }
            }
        }

        if (bestRow < 0)
        {
            return new AntennaLobe(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        var (across, along) = (new double[_columns], new double[Elevations.Length]);
        for (var column = 0; column < _columns; column++)
        {
            across[column] = Gains[bestRow, column];
        }

        for (var row = 0; row < along.Length; row++)
        {
            along[row] = Gains[row, bestColumn];
        }

        var level = best - HalfPower;
        var azimuths = Azimuths[.._columns];
        return new AntennaLobe(
            Azimuths[bestColumn],
            Elevations[bestRow],
            best,
            Side(azimuths, across, bestColumn, 1, _isRound, level) + Side(azimuths, across, bestColumn, -1, _isRound, level),
            Side(Elevations, along, bestRow, 1, false, level) + Side(Elevations, along, bestRow, -1, false, level));
    }
}
