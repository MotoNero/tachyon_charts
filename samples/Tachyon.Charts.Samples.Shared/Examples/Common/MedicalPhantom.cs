using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Creates a synthetic CT-like image without personal or external data.</summary>
public static class MedicalPhantom
{
    /// <summary>Creates an axial image.</summary>
    public static MedicalImageDisplay Axial() => Create(new PatientOrientation(new Point3D(1, 0, 0), new Point3D(0, 1, 0)));

    /// <summary>Creates a coronal image.</summary>
    public static MedicalImageDisplay Coronal() => Create(new PatientOrientation(new Point3D(1, 0, 0), new Point3D(0, 0, -1)));

    /// <summary>Creates a sagittal image.</summary>
    public static MedicalImageDisplay Sagittal() => Create(new PatientOrientation(new Point3D(0, 1, 0), new Point3D(0, 0, -1)));

    /// <summary>Creates a small synthetic CT-like head volume for linked orthogonal views.</summary>
    public static MedicalVolume Volume()
    {
        const int width = 128;
        const int height = 112;
        const int depth = 96;
        var values = new float[width * height * depth];
        for (var slice = 0; slice < depth; slice++)
        {
            var z = (slice - (depth / 2.0)) / (depth * 0.46);
            for (var row = 0; row < height; row++)
            {
                var y = (row - (height / 2.0)) / (height * 0.43);
                for (var column = 0; column < width; column++)
                {
                    var x = (column - (width / 2.0)) / (width * 0.36);
                    var radius = (x * x) + (y * y) + (z * z);
                    var value = -1000f;
                    if (radius < 1)
                    {
                        value = radius > 0.82 ? 1050 : 38;
                        if (Ellipse(x - 0.16, y, 0.11, 0.24) && Math.Abs(z) < 0.55
                            || Ellipse(x + 0.16, y, 0.11, 0.24) && Math.Abs(z) < 0.55)
                        {
                            value = 8;
                        }
                        else if ((((x + 0.45) * (x + 0.45)) + ((y + 0.12) * (y + 0.12)) + (z * z)) < 0.035)
                        {
                            value = 68;
                        }
                        else
                        {
                            value += (float)(10 * Math.Sin((x * 8) + (y * 5) + (z * 4)));
                        }
                    }

                    values[(slice * width * height) + (row * width) + column] = value;
                }
            }
        }

        var orientation = new PatientOrientation(new Point3D(1, 0, 0), new Point3D(0, 1, 0));
        return new MedicalVolume(new VolumeData(values, width, height, depth), new ImageSpacing(0.8, 0.8), 1.2, default, orientation, 40, 80, "CT");
    }

    /// <summary>Creates two synthetic structures aligned with a head volume.</summary>
    public static MedicalSegmentation Segmentation(MedicalVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        var data = volume.Data;
        var labels = new ushort[checked(data.Width * data.Height * data.Depth)];
        for (var z = 0; z < data.Depth; z++)
        {
            var nz = z / (double)(data.Depth - 1);
            for (var y = 0; y < data.Height; y++)
            {
                var ny = y / (double)(data.Height - 1);
                for (var x = 0; x < data.Width; x++)
                {
                    var nx = x / (double)(data.Width - 1);
                    var focus = Square((nx - 0.32) / 0.11) + Square((ny - 0.46) / 0.14) + Square((nz - 0.52) / 0.16) < 1;
                    var cavity = Square((nx - 0.56) / 0.12) + Square((ny - 0.5) / 0.19) + Square((nz - 0.5) / 0.28) < 1;
                    labels[((z * data.Height) + y) * data.Width + x] = focus ? (ushort)1 : cavity ? (ushort)2 : (ushort)0;
                }
            }
        }

        return new MedicalSegmentation(
            new VolumeData(labels, data.Width, data.Height, data.Depth),
            new ImageSpacing(volume.VoxelSpacing.Y, volume.VoxelSpacing.X),
            volume.VoxelSpacing.Z,
            volume.Origin,
            volume.Orientation,
            [
                new MedicalSegment(1, "Focus", new Rgba8(245, 70, 80, 255)) { Opacity = 0.62 },
                new MedicalSegment(2, "Cavity", new Rgba8(45, 205, 235, 255)) { Opacity = 0.48 },
            ]);
    }

    private static MedicalImageDisplay Create(PatientOrientation orientation)
    {
        const int width = 320;
        const int height = 256;
        var values = new float[width * height];
        for (var row = 0; row < height; row++)
        {
            var y = (row - (height / 2.0)) / (height * 0.42);
            for (var column = 0; column < width; column++)
            {
                var x = (column - (width / 2.0)) / (width * 0.34);
                var radius = (x * x) + (y * y);
                var value = -1000f;
                if (radius < 1)
                {
                    value = radius > 0.82 ? 1050 : 38;
                    if (Ellipse(x + 0.18, y, 0.13, 0.28) || Ellipse(x - 0.18, y, 0.13, 0.28))
                    {
                        value = 8;
                    }
                    else if (Ellipse(x + 0.48, y + 0.15, 0.18, 0.22))
                    {
                        value = 65;
                    }
                    else
                    {
                        value += (float)(12 * Math.Sin((x * 8) + (y * 5)));
                    }
                }

                values[(row * width) + column] = value;
            }
        }

        var display = new MedicalImageDisplay(values, width, height, new ImageSpacing(0.7, 0.7), orientation, "HU");
        display.ApplyPreset(CtWindowPresets.Brain);
        return display;
    }

    private static bool Ellipse(double x, double y, double horizontal, double vertical) =>
        ((x * x) / (horizontal * horizontal)) + ((y * y) / (vertical * vertical)) < 1;

    private static double Square(double value) => value * value;
}
