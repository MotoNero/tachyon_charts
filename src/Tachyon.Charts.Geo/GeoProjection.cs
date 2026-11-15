namespace Tachyon.Charts.Geo;

/// <summary>
/// A map projection: from longitude and latitude in degrees to X and Y on the plane of the chart, and back. The X axis
/// of the chart runs east, the Y axis north.
/// </summary>
public abstract class GeoProjection
{
    private protected GeoProjection()
    {
    }

    /// <summary>Gets longitude and latitude as they are, in degrees: simple and good for small areas and data grids.</summary>
    public static GeoProjection Equirectangular { get; } = new EquirectangularProjection();

    /// <summary>Gets the projection of web maps and their tiles, in units of the earth radius, up to 85.05° north and south.</summary>
    public static GeoProjection WebMercator { get; } = new WebMercatorProjection();

    /// <summary>Gets an equal-area projection of the whole world: areas keep their sizes against each other.</summary>
    public static GeoProjection EqualEarth { get; } = new EqualEarthProjection();

    /// <summary>Gets the name.</summary>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the box of the world on the plane: the widest place lies at the equator, the highest near the poles (Web
    /// Mercator stops at 85°).
    /// </summary>
    public (double West, double South, double East, double North) WorldBounds
    {
        get
        {
            var (west, _) = Project(-180, 0);
            var (east, _) = Project(180, 0);
            var (_, south) = Project(0, -85);
            var (_, north) = Project(0, 85);
            return (west, south, east, north);
        }
    }

    /// <summary>Projects a place.</summary>
    /// <param name="longitude">The longitude in degrees, east positive.</param>
    /// <param name="latitude">The latitude in degrees, north positive.</param>
    /// <returns>The place on the plane.</returns>
    public abstract (double X, double Y) Project(double longitude, double latitude);

    /// <summary>Finds the place of a point on the plane.</summary>
    /// <param name="x">The X on the plane.</param>
    /// <param name="y">The Y on the plane.</param>
    /// <param name="longitude">Receives the longitude in degrees.</param>
    /// <param name="latitude">Receives the latitude in degrees.</param>
    /// <returns>Whether the point lies on the map.</returns>
    public abstract bool TryUnproject(double x, double y, out double longitude, out double latitude);

    /// <summary>Projects many places, such as the points of a data layer.</summary>
    /// <param name="longitudes">The longitudes in degrees.</param>
    /// <param name="latitudes">The latitudes in degrees, as many.</param>
    /// <returns>The places on the plane.</returns>
    public (double[] X, double[] Y) Project(ReadOnlySpan<double> longitudes, ReadOnlySpan<double> latitudes)
    {
        if (longitudes.Length != latitudes.Length)
        {
            throw new ArgumentException($"{longitudes.Length} longitudes, {latitudes.Length} latitudes.", nameof(latitudes));
        }

        var (x, y) = (new double[longitudes.Length], new double[longitudes.Length]);
        for (var i = 0; i < x.Length; i++)
        {
            (x[i], y[i]) = Project(longitudes[i], latitudes[i]);
        }

        return (x, y);
    }

    private static double Radians(double degrees) => degrees * Math.PI / 180;

    private static double Degrees(double radians) => radians * 180 / Math.PI;

    private sealed class EquirectangularProjection : GeoProjection
    {
        public override string Name => "Equirectangular";

        public override (double X, double Y) Project(double longitude, double latitude) => (longitude, latitude);

        public override bool TryUnproject(double x, double y, out double longitude, out double latitude)
        {
            (longitude, latitude) = (x, y);
            return Math.Abs(x) <= 180 && Math.Abs(y) <= 90;
        }
    }

    private sealed class WebMercatorProjection : GeoProjection
    {
        private const double Limit = 85.0511287798066;

        public override string Name => "WebMercator";

        public override (double X, double Y) Project(double longitude, double latitude)
        {
            var phi = Radians(Math.Clamp(latitude, -Limit, Limit));
            return (Radians(longitude), Math.Log(Math.Tan((Math.PI / 4) + (phi / 2))));
        }

        public override bool TryUnproject(double x, double y, out double longitude, out double latitude)
        {
            (longitude, latitude) = (Degrees(x), Degrees((2 * Math.Atan(Math.Exp(y))) - (Math.PI / 2)));
            return Math.Abs(longitude) <= 180 && Math.Abs(latitude) <= Limit;
        }
    }

    // Šavrič, Patterson and Jenny, „The Equal Earth map projection“, 2018; X and Y in units of the earth radius.
    private sealed class EqualEarthProjection : GeoProjection
    {
        private const double A1 = 1.340264;
        private const double A2 = -0.081106;
        private const double A3 = 0.000893;
        private const double A4 = 0.003796;
        private static readonly double _m = Math.Sqrt(3) / 2;

        public override string Name => "EqualEarth";

        public override (double X, double Y) Project(double longitude, double latitude)
        {
            var theta = Math.Asin(_m * Math.Sin(Radians(Math.Clamp(latitude, -90, 90))));
            var (t2, t6) = (theta * theta, Math.Pow(theta, 6));
            var x = 2 * Math.Sqrt(3) * Radians(longitude) * Math.Cos(theta) / (3 * ((9 * A4 * t6 * t2) + (7 * A3 * t6) + (3 * A2 * t2) + A1));
            var y = theta * (A1 + (A2 * t2) + (A3 * t6) + (A4 * t6 * t2));
            return (x, y);
        }

        public override bool TryUnproject(double x, double y, out double longitude, out double latitude)
        {
            // Newton steps on theta for y, then the longitude from x.
            var theta = y;
            for (var step = 0; step < 12; step++)
            {
                var (t2, t6) = (theta * theta, Math.Pow(theta, 6));
                var f = (theta * (A1 + (A2 * t2) + (A3 * t6) + (A4 * t6 * t2))) - y;
                var derivative = A1 + (3 * A2 * t2) + (7 * A3 * t6) + (9 * A4 * t6 * t2);
                theta -= f / derivative;
            }

            var (u2, u6) = (theta * theta, Math.Pow(theta, 6));
            var lambda = 3 * x * ((9 * A4 * u6 * u2) + (7 * A3 * u6) + (3 * A2 * u2) + A1) / (2 * Math.Sqrt(3) * Math.Cos(theta));
            var sine = Math.Sin(theta) / _m;
            (longitude, latitude) = (Degrees(lambda), Math.Abs(sine) <= 1 ? Degrees(Math.Asin(sine)) : double.NaN);
            return Math.Abs(longitude) <= 180 + 1e-9 && double.IsFinite(latitude);
        }
    }
}
