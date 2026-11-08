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
    public static MedicalVolume Volume() => MedicalPhantoms.Head();

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
                    labels[((z * data.Height) + y) * data.Width + x] = Label(x / (double)(data.Width - 1), ny, nz);
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

    // 1 inside the focus, 2 inside the cavity, 0 elsewhere; places in shares of the volume.
    private static ushort Label(double x, double y, double z)
    {
        if (Square((x - 0.32) / 0.11) + Square((y - 0.46) / 0.14) + Square((z - 0.52) / 0.16) < 1)
        {
            return 1;
        }

        return Square((x - 0.56) / 0.12) + Square((y - 0.5) / 0.19) + Square((z - 0.5) / 0.28) < 1 ? (ushort)2 : (ushort)0;
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
                values[(row * width) + column] = Value((column - (width / 2.0)) / (width * 0.34), y);
            }
        }

        var display = new MedicalImageDisplay(values, width, height, new ImageSpacing(0.7, 0.7), orientation, "HU");
        display.ApplyPreset(CtWindowPresets.Brain);
        return display;
    }

    // Air around a skull, two ventricles and a round lesion in the brain.
    private static float Value(double x, double y)
    {
        var radius = (x * x) + (y * y);
        if (radius >= 1)
        {
            return -1000f;
        }

        if (Ellipse(x + 0.18, y, 0.13, 0.28) || Ellipse(x - 0.18, y, 0.13, 0.28))
        {
            return 8;
        }

        if (Ellipse(x + 0.48, y + 0.15, 0.18, 0.22))
        {
            return 65;
        }

        var value = radius > 0.82 ? 1050f : 38f;
        return value + (float)(12 * Math.Sin((x * 8) + (y * 5)));
    }

    private static bool Ellipse(double x, double y, double horizontal, double vertical) =>
        ((x * x) / (horizontal * horizontal)) + ((y * y) / (vertical * vertical)) < 1;

    private static double Square(double value) => value * value;
}
