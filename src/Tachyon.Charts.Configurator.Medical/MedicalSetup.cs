using Tachyon.Charts.Medical;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD;
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

    // The measurements the settings made, as against those drawn by hand in the preview.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MedicalMeasurement, object> _made = [];

    private static void Made(MedicalMeasurement measurement) => _made.AddOrUpdate(measurement, _made);

    /// <summary>
    /// Adds a distance, an angle, a round region and a drawn region, placed by the size of the image in millimetres; on
    /// the head phantom, 192 mm wide, they lie across the skull, on the ventricles and on the focus.
    /// </summary>
    /// <param name="layer">The measurements.</param>
    /// <param name="width">The width of the image.</param>
    /// <param name="height">The height of the image.</param>
    public static void ExampleMeasurements(MedicalMeasurementLayer layer, double width, double height)
    {
        Made(layer.Add(MedicalMeasurementKind.Distance, [new(33 / 192.0 * width, 140 / 192.0 * height), new(159 / 192.0 * width, 140 / 192.0 * height)], name: "Width"));
        Made(layer.Add(MedicalMeasurementKind.Angle, [new(87 / 192.0 * width, 72 / 192.0 * height), new(96 / 192.0 * width, 122 / 192.0 * height), new(105 / 192.0 * width, 72 / 192.0 * height)]));
        Made(layer.Add(MedicalMeasurementKind.Circle, [new(62.5 / 192 * width, 82 / 192.0 * height), new(68.5 / 192 * width, 82 / 192.0 * height)], name: "Focus"));
        Made(layer.Add(MedicalMeasurementKind.Freehand, [new(100 / 192.0 * width, 70 / 192.0 * height), new(110 / 192.0 * width, 72 / 192.0 * height), new(112 / 192.0 * width, 95 / 192.0 * height), new(110 / 192.0 * width, 120 / 192.0 * height), new(101 / 192.0 * width, 121 / 192.0 * height), new(99 / 192.0 * width, 95 / 192.0 * height)], name: "Ventricle"));
    }

    /// <summary>
    /// Takes the measurements of an image over into the same image made anew for a change: those drawn by hand, and the
    /// new places of those the settings made; the tool stays when the settings did not change it.
    /// </summary>
    /// <param name="before">The measurements before.</param>
    /// <param name="after">The measurements made anew.</param>
    /// <param name="keepsTool">Whether the tool chosen in the image stays.</param>
    public static void TakeOver(MedicalMeasurementLayer before, MedicalMeasurementLayer after, bool keepsTool)
    {
        foreach (var measurement in before.Measurements.ToList())
        {
            if (_made.TryGetValue(measurement, out _))
            {
                // Made by the settings: it is there anew unless the settings left it out; only a moved one goes over.
                if (after.Measurements.FirstOrDefault(fresh => _made.TryGetValue(fresh, out _) && fresh.Name == measurement.Name && fresh.Kind == measurement.Kind) is not { } counterpart
                    || counterpart.Points.SequenceEqual(measurement.Points))
                {
                    continue;
                }

                after.Remove(counterpart);
                Made(after.Add(measurement.Kind, [.. measurement.Points], measurement.Slice, measurement.Name));
                continue;
            }

            after.Add(measurement.Kind, [.. measurement.Points], measurement.Slice, measurement.Name);
        }

        if (keepsTool)
        {
            after.Tool = before.Tool;
        }
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

    // The oblique plane of a display, one for all its charts, so that the 2D chart and the 3D outline show the same plane.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MedicalMprDisplay, MedicalObliqueSlice> _obliques = [];

    /// <summary>Gets a measurement of a plane by its name.</summary>
    /// <param name="slice">The plane.</param>
    /// <param name="name">The name.</param>
    /// <returns>The measurement.</returns>
    public static MedicalMeasurement Named(MedicalMprSlice slice, string name) => slice.Measurements.Measurements.First(measurement => measurement.Name == name);

    /// <summary>Adds a line across the middle of a plane and a round region on its left, where the phantom has its focus; in shares of the plane.</summary>
    /// <param name="slice">The plane.</param>
    public static void AnalysisMeasurements(MedicalMprSlice slice)
    {
        var (width, height) = (slice.Width * slice.Spacing.Column, slice.Height * slice.Spacing.Row);
        Made(slice.Measurements.Add(MedicalMeasurementKind.Distance, [new(width * 0.12, height * 0.5), new(width * 0.88, height * 0.5)], name: "Profile line"));
        Made(slice.Measurements.Add(MedicalMeasurementKind.Circle, [new(width * 0.323, height * 0.43), new(width * 0.358, height * 0.43)], name: "Focus"));
    }

    /// <summary>Adds a line for the oblique plane from lower left to upper right and an arc along the front of the body; in shares of the plane.</summary>
    /// <param name="slice">The plane.</param>
    public static void ReformatMeasurements(MedicalMprSlice slice)
    {
        var (width, height) = (slice.Width * slice.Spacing.Column, slice.Height * slice.Spacing.Row);
        Made(slice.Measurements.Add(MedicalMeasurementKind.Distance, [new(width * 0.156, height * 0.73), new(width * 0.86, height * 0.35)], name: "Oblique plane"));
        var arc = new MedicalPoint[13];
        for (var i = 0; i < arc.Length; i++)
        {
            var angle = Math.PI + 0.45 + ((Math.PI - 0.9) * i / (arc.Length - 1));
            arc[i] = new(width * (0.5 + (0.396 * Math.Cos(angle))), height * (0.5 + (0.285 * Math.Sin(angle))));
        }

        Made(slice.Measurements.Add(MedicalMeasurementKind.Freehand, arc, name: "Arc"));
    }

    /// <summary>Gets the oblique plane of a display; it follows the line on the axial plane.</summary>
    /// <param name="display">The display.</param>
    /// <returns>The oblique plane, the same for every call with the display.</returns>
    public static MedicalObliqueSlice Oblique(MedicalMprDisplay display) => _obliques.GetValue(display, shown =>
    {
        var oblique = new MedicalObliqueSlice(shown);
        oblique.FollowLine(shown.Axial);
        return oblique;
    });

    /// <summary>Describes the volume in 3D with the outline of the oblique plane, which then can face the view.</summary>
    /// <param name="display">The display.</param>
    /// <param name="chart">The 3D chart.</param>
    public static void WithOblique(MedicalMprDisplay display, Chart3DBuilder chart)
    {
        display.Configure3D(chart);
        var oblique = Oblique(display);
        oblique.Configure3D(chart);
        oblique.ViewCamera = chart.Scene.Camera;
    }

    private const string NamedCode = """

            // Gets a measurement of a plane by its name.
            private static MedicalMeasurement Named(MedicalMprSlice slice, string name) => slice.Measurements.Measurements.First(measurement => measurement.Name == name);

        """;

    /// <summary>Gets <see cref="Named"/> and <see cref="AnalysisMeasurements"/> as C#.</summary>
    public const string AnalysisMeasurementsCode = NamedCode + """

            // Adds a line across the middle of a plane and a round region on its left, where the phantom has its focus; in shares of the plane.
            private static void AnalysisMeasurements(MedicalMprSlice slice)
            {
                var (width, height) = (slice.Width * slice.Spacing.Column, slice.Height * slice.Spacing.Row);
                slice.Measurements.Add(MedicalMeasurementKind.Distance, [new(width * 0.12, height * 0.5), new(width * 0.88, height * 0.5)], name: "Profile line");
                slice.Measurements.Add(MedicalMeasurementKind.Circle, [new(width * 0.323, height * 0.43), new(width * 0.358, height * 0.43)], name: "Focus");
            }

        """;

    /// <summary>Gets <see cref="Named"/>, <see cref="ReformatMeasurements"/>, <see cref="Oblique"/> and <see cref="WithOblique"/> as C#.</summary>
    public const string ReformatCode = NamedCode + """

            // Adds a line for the oblique plane from lower left to upper right and an arc along the front of the body; in shares of the plane.
            private static void ReformatMeasurements(MedicalMprSlice slice)
            {
                var (width, height) = (slice.Width * slice.Spacing.Column, slice.Height * slice.Spacing.Row);
                slice.Measurements.Add(MedicalMeasurementKind.Distance, [new(width * 0.156, height * 0.73), new(width * 0.86, height * 0.35)], name: "Oblique plane");
                var arc = new MedicalPoint[13];
                for (var i = 0; i < arc.Length; i++)
                {
                    var angle = Math.PI + 0.45 + ((Math.PI - 0.9) * i / (arc.Length - 1));
                    arc[i] = new(width * (0.5 + (0.396 * Math.Cos(angle))), height * (0.5 + (0.285 * Math.Sin(angle))));
                }

                slice.Measurements.Add(MedicalMeasurementKind.Freehand, arc, name: "Arc");
            }

            // The oblique plane of a display, one for all its charts, so that the 2D chart and the 3D outline show the same plane.
            private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MedicalMprDisplay, MedicalObliqueSlice> _obliques = [];

            // Gets the oblique plane of a display; it follows the line on the axial plane.
            private static MedicalObliqueSlice Oblique(MedicalMprDisplay display) => _obliques.GetValue(display, shown =>
            {
                var oblique = new MedicalObliqueSlice(shown);
                oblique.FollowLine(shown.Axial);
                return oblique;
            });

            // Describes the volume in 3D with the outline of the oblique plane, which then can face the view.
            private static void WithOblique(MedicalMprDisplay display, Chart3DBuilder chart)
            {
                display.Configure3D(chart);
                var oblique = Oblique(display);
                oblique.Configure3D(chart);
                oblique.ViewCamera = chart.Scene.Camera;
            }

        """;

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

            // Adds a distance, an angle, a round region and a drawn region, placed by the size of the image in millimetres.
            private static void ExampleMeasurements(MedicalMeasurementLayer layer, double width, double height)
            {
                layer.Add(MedicalMeasurementKind.Distance, [new(33 / 192.0 * width, 140 / 192.0 * height), new(159 / 192.0 * width, 140 / 192.0 * height)], name: "Width");
                layer.Add(MedicalMeasurementKind.Angle, [new(87 / 192.0 * width, 72 / 192.0 * height), new(96 / 192.0 * width, 122 / 192.0 * height), new(105 / 192.0 * width, 72 / 192.0 * height)]);
                layer.Add(MedicalMeasurementKind.Circle, [new(62.5 / 192 * width, 82 / 192.0 * height), new(68.5 / 192 * width, 82 / 192.0 * height)], name: "Focus");
                layer.Add(MedicalMeasurementKind.Freehand, [new(100 / 192.0 * width, 70 / 192.0 * height), new(110 / 192.0 * width, 72 / 192.0 * height), new(112 / 192.0 * width, 95 / 192.0 * height), new(110 / 192.0 * width, 120 / 192.0 * height), new(101 / 192.0 * width, 121 / 192.0 * height), new(99 / 192.0 * width, 95 / 192.0 * height)], name: "Ventricle");
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
