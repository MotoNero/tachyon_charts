using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// Synthetic CT volumes for trying the package, made of simple shapes with typical Hounsfield units. They hold no data
/// of any person and need no download, so they fit tests, demos and first steps.
/// </summary>
public static class MedicalPhantoms
{
    private const double ThoraxHalfWidth = 192;
    private const double ThoraxHalfHeight = 144;
    private const double ThoraxLength = 297.5;
    private static readonly PatientOrientation _axial = new(new Point3D(1, 0, 0), new Point3D(0, 1, 0));
    private static readonly Lazy<(Point3D Start, Point3D End, double Radius)[]> _vessels = new(GrowVessels);

    /// <summary>
    /// Creates a head of 128 × 112 × 96 values at 0.8 mm across and 1.2 mm between slices: skull, brain with two
    /// ventricles and a round lesion. Window 40/80 HU.
    /// </summary>
    /// <returns>The volume.</returns>
    public static MedicalVolume Head()
    {
        const int Width = 128;
        const int Height = 112;
        const int Depth = 96;
        var values = new float[Width * Height * Depth];
        Parallel.For(0, Depth, slice =>
        {
            var z = (slice - (Depth / 2.0)) / (Depth * 0.46);
            for (var row = 0; row < Height; row++)
            {
                var y = (row - (Height / 2.0)) / (Height * 0.43);
                for (var column = 0; column < Width; column++)
                {
                    var x = (column - (Width / 2.0)) / (Width * 0.36);
                    values[(slice * Width * Height) + (row * Width) + column] = HeadValue(x, y, z);
                }
            }
        });

        return new MedicalVolume(new VolumeData(values, Width, Height, Depth), new ImageSpacing(0.8, 0.8), 1.2, default, _axial, 40, 80, "CT");
    }

    /// <summary>
    /// Creates a thorax of 256 × 192 × 120 values at 1.5 mm across and 2.5 mm between slices, stored as 16 bits with an
    /// intercept of −1024 as a scanner does: lungs with vessels and a nodule, heart, aorta, spine, ribs and sternum in
    /// fat and muscle. Window 40/400 HU; for the lungs −600/1500.
    /// </summary>
    /// <returns>The volume.</returns>
    public static MedicalVolume Thorax()
    {
        const int Width = 256;
        const int Height = 192;
        const int Depth = 120;
        var stored = new ushort[Width * Height * Depth];
        Parallel.For(0, Depth, slice =>
        {
            // From the top of the chest (0) to the diaphragm (1).
            var z = slice / (double)(Depth - 1);
            for (var row = 0; row < Height; row++)
            {
                var y = (row - (Height / 2.0)) / (Height / 2.0);
                for (var column = 0; column < Width; column++)
                {
                    var x = (column - (Width / 2.0)) / (Width / 2.0);
                    var hu = ThoraxValue(x, y, z);
                    stored[(slice * Width * Height) + (row * Width) + column] = (ushort)Math.Clamp(Math.Round(hu + 1024), 0, ushort.MaxValue);
                }
            }
        });

        return new MedicalVolume(new VolumeData(stored, Width, Height, Depth, 1, -1024), new ImageSpacing(1.5, 1.5), 2.5, default, _axial, 40, 400, "CT");
    }

    private static float HeadValue(double x, double y, double z)
    {
        var radius = (x * x) + (y * y) + (z * z);
        if (radius >= 1)
        {
            return -1000f;
        }

        if ((Ellipse(x - 0.16, y, 0.11, 0.24) || Ellipse(x + 0.16, y, 0.11, 0.24)) && Math.Abs(z) < 0.55)
        {
            return 8;
        }

        if ((((x + 0.45) * (x + 0.45)) + ((y + 0.12) * (y + 0.12)) + (z * z)) < 0.035)
        {
            return 68;
        }

        var value = radius > 0.82 ? 1050f : 38f;
        return value + (float)(10 * Math.Sin((x * 8) + (y * 5) + (z * 4)));
    }

    // x to the left of the patient, y backwards, z downwards; all in shares of the half width.
    private static double ThoraxValue(double x, double y, double z)
    {
        var body = Square(x / 0.92) + Square(y / 0.66);
        if (body >= 1)
        {
            return -1000;
        }

        // Ribs: a band inside the skin, one rib every few slices.
        if (body > 0.72 && body < 0.8 && Math.Sin(z * 45) > 0.35 && y < 0.45)
        {
            return 900;
        }

        if (body > 0.86)
        {
            return -100 + (10 * Math.Sin(x * 30));
        }

        // The spine behind, with a denser rim, and the spinal canal.
        var vertebra = Square(x / 0.13) + Square((y - 0.45) / 0.12);
        if (vertebra < 1)
        {
            return Square(x / 0.05) + Square((y - 0.5) / 0.04) < 1 ? 15 : vertebra > 0.7 ? 1100 : 350;
        }

        if (Math.Abs(x) < 0.06 && y < -0.5 && y > -0.6)
        {
            return 600;
        }

        // The heart sits low and to the left; the aorta descends beside the spine.
        var heart = Square((x - 0.12) / 0.27) + Square((y + 0.08) / 0.24) + Square((z - 0.7) / 0.35);
        if (heart < 1)
        {
            return heart < 0.35 ? 180 : 45;
        }

        if (Square((x + 0.12) / 0.07) + Square((y - 0.28) / 0.07) < 1)
        {
            return 200;
        }

        foreach (var side in (ReadOnlySpan<double>)[-1, 1])
        {
            // A dome from the apex near the top to the diaphragm, which rises under the heart.
            var lung = Square((x - (side * 0.45)) / 0.33) + Square((y + 0.02) / 0.46) + Square((z - 0.5) / 0.45);
            if (lung < 1 && z < 0.9 - (0.12 * Square((x - (side * 0.45)) / 0.33)))
            {
                if (side < 0 && Square((x + 0.5) / 0.035) + Square((y + 0.1) / 0.035) + Square((z - 0.4) / 0.04) < 1)
                {
                    return 40;
                }

                // Vessels branch from the hilum; distances in millimetres.
                return IsVessel(x * ThoraxHalfWidth, y * ThoraxHalfHeight, z * ThoraxLength) ? 35 : -860 + (25 * Math.Sin((x * 60) + (y * 50)));
            }
        }

        return 45 + (8 * Math.Sin((x * 12) + (y * 9) + (z * 6)));
    }

    private static bool IsVessel(double x, double y, double z)
    {
        foreach (var vessel in _vessels.Value)
        {
            var (dx, dy, dz) = (vessel.End.X - vessel.Start.X, vessel.End.Y - vessel.Start.Y, vessel.End.Z - vessel.Start.Z);
            var (px, py, pz) = (x - vessel.Start.X, y - vessel.Start.Y, z - vessel.Start.Z);
            var along = Math.Clamp(((px * dx) + (py * dy) + (pz * dz)) / ((dx * dx) + (dy * dy) + (dz * dz)), 0, 1);
            if (Square(px - (along * dx)) + Square(py - (along * dy)) + Square(pz - (along * dz)) < vessel.Radius * vessel.Radius)
            {
                return true;
            }
        }

        return false;
    }

    // Five trunks per lung, each splitting four times into two thinner, shorter branches.
    private static (Point3D Start, Point3D End, double Radius)[] GrowVessels()
    {
        var vessels = new List<(Point3D, Point3D, double)>();
        foreach (var side in (ReadOnlySpan<double>)[-1, 1])
        {
            var hilum = new Point3D(side * 45, 0, 0.5 * ThoraxLength);
            foreach (var (dx, dy, dz) in (ReadOnlySpan<(double, double, double)>)[(0.4, 0, -1), (1, 0.2, 0), (0.4, 0.1, 1), (0.5, -1, 0.2), (0.5, 1, 0.2)])
            {
                Grow(vessels, hilum, new Point3D(side * dx, dy, dz), 62, 4.5, 4);
            }
        }

        return [.. vessels];
    }

    private static void Grow(List<(Point3D, Point3D, double)> vessels, Point3D start, Point3D direction, double length, double radius, int levels)
    {
        var size = Math.Sqrt(Point3D.Dot(direction, direction));
        var end = new Point3D(start.X + (direction.X / size * length), start.Y + (direction.Y / size * length), start.Z + (direction.Z / size * length));
        vessels.Add((start, end, radius));
        if (levels == 0)
        {
            return;
        }

        // Two children, bent apart across the direction.
        var (u, v) = (new Point3D(-direction.Y, direction.X, 0.3), new Point3D(0.2, -direction.Z, direction.Y));
        Grow(vessels, end, new Point3D((direction.X / size) + (0.7 * u.X), (direction.Y / size) + (0.7 * u.Y), (direction.Z / size) + (0.7 * u.Z)), length * 0.62, radius * 0.62, levels - 1);
        Grow(vessels, end, new Point3D((direction.X / size) - (0.6 * v.X), (direction.Y / size) - (0.6 * v.Y), (direction.Z / size) - (0.6 * v.Z)), length * 0.62, radius * 0.62, levels - 1);
    }

    private static bool Ellipse(double x, double y, double radiusX, double radiusY) => Square(x / radiusX) + Square(y / radiusY) < 1;

    private static double Square(double value) => value * value;
}
