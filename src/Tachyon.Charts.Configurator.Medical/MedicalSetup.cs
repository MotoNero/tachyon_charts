using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Configurator.Medical;

/// <summary>
/// The steps the preview takes on a medical display. Each keeps its C# twice, as code for the preview and as text for the
/// generated project, side by side, so that both take the same steps.
/// </summary>
internal static class MedicalSetup
{
    /// <summary>Moves the shared position along the normal of a plane to a share of the volume; the others stay where they are.</summary>
    /// <param name="display">The display.</param>
    /// <param name="plane">The plane.</param>
    /// <param name="share">The share, 0 to 1.</param>
    public static void MoveTo(MedicalMprDisplay display, MedicalMprPlane plane, double share)
    {
        var (data, spacing, at) = (display.Source.Data, display.Source.VoxelSpacing, display.Position);
        display.SetPosition(plane switch
        {
            MedicalMprPlane.Axial => new Point3D(at.X, at.Y, share * (data.Depth - 1) * spacing.Z),
            MedicalMprPlane.Coronal => new Point3D(at.X, share * (data.Height - 1) * spacing.Y, at.Z),
            _ => new Point3D(share * (data.Width - 1) * spacing.X, at.Y, at.Z),
        });
    }

    /// <summary>Adds a distance, an angle and a round region, placed by the size of the image in millimetres.</summary>
    /// <param name="layer">The measurements.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    public static void ExampleMeasurements(MedicalMeasurementLayer layer, double width, double height)
    {
        layer.Add(MedicalMeasurementKind.Distance, [new(0.17 * width, 0.73 * height), new(0.83 * width, 0.73 * height)], name: "Width");
        layer.Add(MedicalMeasurementKind.Angle, [new(0.45 * width, 0.38 * height), new(0.5 * width, 0.64 * height), new(0.55 * width, 0.38 * height)]);
        layer.Add(MedicalMeasurementKind.Circle, [new(0.33 * width, 0.43 * height), new(0.36 * width, 0.43 * height)], name: "Region");
    }

    // The labels of a phantom take a while to make; the preview keeps them per volume.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MedicalVolume, VolumeData> _labels = [];

    /// <summary>Makes a segmentation of a phantom: a focus and a cavity, placed in shares of the volume.</summary>
    /// <param name="volume">The volume.</param>
    /// <param name="mode">How the segments show in 3D.</param>
    /// <param name="opacity">The opacity of the segments.</param>
    /// <returns>The segmentation.</returns>
    public static MedicalSegmentation PhantomSegmentation(MedicalVolume volume, MedicalSegmentation3DMode mode, double opacity) => new(
        _labels.GetValue(volume, Labels),
        new ImageSpacing(volume.VoxelSpacing.Y, volume.VoxelSpacing.X),
        volume.VoxelSpacing.Z,
        volume.Origin,
        volume.Orientation,
        [new MedicalSegment(1, "Focus", new Rgba8(245, 70, 80, 255)) { Opacity = opacity }, new MedicalSegment(2, "Cavity", new Rgba8(45, 205, 235, 255)) { Opacity = opacity }])
    { ThreeDMode = mode };

    private static VolumeData Labels(MedicalVolume volume)
    {
        var data = volume.Data;
        var labels = new ushort[data.Width * data.Height * data.Depth];
        for (var z = 0; z < data.Depth; z++)
        {
            for (var y = 0; y < data.Height; y++)
            {
                for (var x = 0; x < data.Width; x++)
                {
                    var (nx, ny, nz) = (x / (data.Width - 1.0), y / (data.Height - 1.0), z / (data.Depth - 1.0));
                    labels[(((z * data.Height) + y) * data.Width) + x] =
                        Square((nx - 0.32) / 0.11) + Square((ny - 0.46) / 0.14) + Square((nz - 0.52) / 0.16) < 1 ? (ushort)1
                        : Square((nx - 0.56) / 0.12) + Square((ny - 0.5) / 0.19) + Square((nz - 0.5) / 0.28) < 1 ? (ushort)2
                        : (ushort)0;
                }
            }
        }

        return new VolumeData(labels, data.Width, data.Height, data.Depth);
    }

    /// <summary>Gives a segmentation read from a file the way it shows in 3D and the opacity of its segments.</summary>
    /// <param name="read">The segmentation as read.</param>
    /// <param name="mode">How the segments show in 3D.</param>
    /// <param name="opacity">The opacity of the segments.</param>
    /// <returns>The segmentation.</returns>
    public static MedicalSegmentation WithMode(MedicalSegmentation read, MedicalSegmentation3DMode mode, double opacity)
    {
        foreach (var segment in read.Segments)
        {
            segment.Opacity = opacity;
        }

        return new MedicalSegmentation(
            read.Data,
            new ImageSpacing(read.VoxelSpacing.Y, read.VoxelSpacing.X),
            read.VoxelSpacing.Z,
            read.Origin,
            read.Orientation,
            read.Segments)
        { ThreeDMode = mode };
    }

    private static double Square(double value) => value * value;

    /// <summary>Gets the same methods as C#, for the generated code.</summary>
    public const string MoveToCode = """

            // Moves the shared position along the normal of a plane to a share of the volume; the others stay where they are.
            private static void MoveTo(MedicalMprDisplay display, MedicalMprPlane plane, double share)
            {
                var (data, spacing, at) = (display.Source.Data, display.Source.VoxelSpacing, display.Position);
                display.SetPosition(plane switch
                {
                    MedicalMprPlane.Axial => new Point3D(at.X, at.Y, share * (data.Depth - 1) * spacing.Z),
                    MedicalMprPlane.Coronal => new Point3D(at.X, share * (data.Height - 1) * spacing.Y, at.Z),
                    _ => new Point3D(share * (data.Width - 1) * spacing.X, at.Y, at.Z),
                });
            }

        """;

    /// <summary>Gets <see cref="ExampleMeasurements"/> as C#.</summary>
    public const string ExampleMeasurementsCode = """

            // Adds a distance, an angle and a round region, placed by the size of the image in millimetres.
            private static void ExampleMeasurements(MedicalMeasurementLayer layer, double width, double height)
            {
                layer.Add(MedicalMeasurementKind.Distance, [new(0.17 * width, 0.73 * height), new(0.83 * width, 0.73 * height)], name: "Width");
                layer.Add(MedicalMeasurementKind.Angle, [new(0.45 * width, 0.38 * height), new(0.5 * width, 0.64 * height), new(0.55 * width, 0.38 * height)]);
                layer.Add(MedicalMeasurementKind.Circle, [new(0.33 * width, 0.43 * height), new(0.36 * width, 0.43 * height)], name: "Region");
            }

        """;

    /// <summary>Gets <see cref="PhantomSegmentation"/> as C#.</summary>
    public const string PhantomSegmentationCode = """

            // Makes a segmentation of a phantom: a focus and a cavity, placed in shares of the volume.
            private static MedicalSegmentation PhantomSegmentation(MedicalVolume volume, MedicalSegmentation3DMode mode, double opacity)
            {
                var data = volume.Data;
                var labels = new ushort[data.Width * data.Height * data.Depth];
                for (var z = 0; z < data.Depth; z++)
                {
                    for (var y = 0; y < data.Height; y++)
                    {
                        for (var x = 0; x < data.Width; x++)
                        {
                            var (nx, ny, nz) = (x / (data.Width - 1.0), y / (data.Height - 1.0), z / (data.Depth - 1.0));
                            labels[(((z * data.Height) + y) * data.Width) + x] =
                                Square((nx - 0.32) / 0.11) + Square((ny - 0.46) / 0.14) + Square((nz - 0.52) / 0.16) < 1 ? (ushort)1
                                : Square((nx - 0.56) / 0.12) + Square((ny - 0.5) / 0.19) + Square((nz - 0.5) / 0.28) < 1 ? (ushort)2
                                : (ushort)0;
                        }
                    }
                }

                return new MedicalSegmentation(
                    new VolumeData(labels, data.Width, data.Height, data.Depth),
                    new ImageSpacing(volume.VoxelSpacing.Y, volume.VoxelSpacing.X),
                    volume.VoxelSpacing.Z,
                    volume.Origin,
                    volume.Orientation,
                    [new MedicalSegment(1, "Focus", new Rgba8(245, 70, 80, 255)) { Opacity = opacity }, new MedicalSegment(2, "Cavity", new Rgba8(45, 205, 235, 255)) { Opacity = opacity }])
                { ThreeDMode = mode };
            }

            private static double Square(double value) => value * value;

        """;

    /// <summary>Gets <see cref="WithMode"/> as C#.</summary>
    public const string WithModeCode = """

            // Gives a segmentation read from a file the way it shows in 3D and the opacity of its segments.
            private static MedicalSegmentation WithMode(MedicalSegmentation read, MedicalSegmentation3DMode mode, double opacity)
            {
                foreach (var segment in read.Segments)
                {
                    segment.Opacity = opacity;
                }

                return new MedicalSegmentation(
                    read.Data,
                    new ImageSpacing(read.VoxelSpacing.Y, read.VoxelSpacing.X),
                    read.VoxelSpacing.Z,
                    read.Origin,
                    read.Orientation,
                    read.Segments)
                { ThreeDMode = mode };
            }

        """;
}
