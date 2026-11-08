using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Creates a synthetic CT-like image without personal or external data.</summary>
public static class MedicalPhantom
{
    /// <summary>Creates an axial image through the lesion of the synthetic head.</summary>
    public static MedicalImageDisplay Axial() => Cut(MedicalMprPlane.Axial, new Point3D(0, 0, 97));

    /// <summary>Creates a coronal image through the lesion.</summary>
    public static MedicalImageDisplay Coronal() => Cut(MedicalMprPlane.Coronal, new Point3D(0, 81.5, 0));

    /// <summary>Creates a sagittal image through the left ventricle.</summary>
    public static MedicalImageDisplay Sagittal() => Cut(MedicalMprPlane.Sagittal, new Point3D(86.5, 0, 0));

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

    // One plane of the synthetic head as a single image, the way a scanner would store it.
    private static MedicalImageDisplay Cut(MedicalMprPlane plane, Point3D position)
    {
        var display = new MedicalMprDisplay(MedicalPhantoms.Head());
        display.SetPosition(position);
        var slice = plane switch
        {
            MedicalMprPlane.Axial => display.Axial,
            MedicalMprPlane.Coronal => display.Coronal,
            _ => display.Sagittal,
        };
        var values = new float[slice.Width * slice.Height];
        slice.CopyValues(values);

        var image = new MedicalImageDisplay(values, slice.Width, slice.Height, slice.Spacing, slice.Orientation, "HU");
        image.ApplyPreset(CtWindowPresets.Brain);
        return image;
    }

    private static double Square(double value) => value * value;
}
