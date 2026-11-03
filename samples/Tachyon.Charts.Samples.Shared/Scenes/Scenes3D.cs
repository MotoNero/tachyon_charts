using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Rendering.Skia;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Antenna3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Clouds3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Columns3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Cuts3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Paths3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Surfaces3D;
using static Tachyon.Charts.Samples.Shared.Examples.Common.Volumes3D;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.AntennaPatternPage;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.CutPlanesPage;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.MapFromMeasurementsPage;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.MaximumProjection512Page;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.ObjectsInsideAVolumePage;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.OnePlaneAllKindsPage;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.PathOf200000PointsPage;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.SurfaceOfEqualValue512Page;
using static Tachyon.Charts.Samples.Shared.Examples.ThreeD.VolumeSlices512Page;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>A 3D scene. It needs a GPU, so its reference pictures exist for WebGPU only.</summary>
/// <param name="name">The snapshot name.</param>
/// <param name="configure">Describes the chart.</param>
public sealed class Scene3DPicture(string name, Action<Chart3DBuilder> configure) : IScene
{
    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public PixelSize Size { get; } = new(900, 640);

    /// <inheritdoc />
    public void Render(IRenderDevice device, IRenderTarget target)
    {
        var scene = new Chart3DBuilder();
        configure(scene);
        var builder = new ChartBuilder();
        scene.Apply(builder);
        var chart = builder.ToDefinition().Build(SkiaTextEngine.Default).Surface;
        chart.Render(device, target);
        chart.Render(device, target);
        device.WaitForIdle();
        foreach (var series in chart.Series)
        {
            (series as IDisposable)?.Dispose();
        }
    }
}

/// <summary>The scenes of the 3D package.</summary>
public static class Scenes3D
{
    /// <summary>Gets the scenes.</summary>
    public static IReadOnlyList<IScene> All { get; } =
    [
        new Scene3DPicture("Box3D", Box),
        new Scene3DPicture("Box3DBehind", c =>
        {
            Box(c);
            c.Scene.Camera.Orbit(180, 0);
        }),
        new Scene3DPicture("Box3DTop", c =>
        {
            Box(c);
            c.Scene.Camera.Show(CameraView.Top);
        }),
        new Scene3DPicture("Box3DBelow", c =>
        {
            Box(c);
            c.Scene.Camera.Orbit(30, -50);
        }),
        new Scene3DPicture("Box3DOrthographic", c =>
        {
            Box(c);
            c.Projection(CameraProjection.Orthographic);
        }),
        new Scene3DPicture("Surface3D", c => Field(c, withGap: false, 256)),
        new Scene3DPicture("Surface3DGap", c => Field(c, withGap: true, 256)),
        new Scene3DPicture("Contours3D", c => Field(c, withGap: false, 256, Contours3D.Even(new DoubleRange(-1.5, 2), 13))),
        new Scene3DPicture("Contours3DWalls", c => Field(c, withGap: true, 256, Contours3D.Even(new DoubleRange(-1.5, 2), 13) with { OnWalls = true })),
        new Scene3DPicture("Contours3DChosen", c => Field(c, withGap: false, 256, new Contours3D([0, 0.5]) { Color = new Rgba8(255, 80, 80, 255), Width = 2, OnFloor = false })),
        new Scene3DPicture("Map3D", Map),
        new Scene3DPicture("Line3D", Trajectory),
        new Scene3DPicture("Scatter3D", c => Cloud(c, 3000)),
        new Scene3DPicture("Plane3D", CutHill),
        new Scene3DPicture("Plane3DTypes", CutTypes),
        new Scene3DPicture("Cloud3D", c => Landscape(c, 500_000)),
        new Scene3DPicture("Cloud3DLoading", CloudOnItsWay),
        new Scene3DPicture("Volume3DSlices", c => Slices(c, 128)),
        new Scene3DPicture("Volume3DThrough", c => LookThrough(c, 128)),
        new Scene3DPicture("Volume3DMixed", VolumeWithObjects),
        new Scene3DPicture("Volume3DMaximum", c => Maximum(c, 128)),
        new Scene3DPicture("Volume3DSurface", c => IsoSurface(c, 128)),
        new Scene3DPicture("Coupled3D", Coupled),
        new Scene3DPicture("Bars3D", c => Columns(c, 16)),
        new Scene3DPicture("Bars3DParallel", c => Columns(c.Projection(CameraProjection.Orthographic), 16)),
        new Scene3DPicture("Line3DBends", Bends),
        new Scene3DPicture("Antenna3D", AntennaChart),
        new Scene3DPicture("Antenna3DPole", c =>
        {
            AntennaChart(c);
            c.Scene.Camera.Show(CameraView.Top);
        }),
        new Scene3DPicture("Waterfall3D", c => Waterfall(c, WaterfallStyle.Surface)),
        new Scene3DPicture("Waterfall3DLines", c => Waterfall(c, WaterfallStyle.Lines)),
        new Scene3DPicture("Waterfall3DMask", SpectrumWaterfall),
        new Scene3DPicture("Mesh3DLit", c => Hill(c, new Material3D())),
        new Scene3DPicture("Mesh3DFlat", c => Hill(c, new Material3D { IsLit = false })),
        new Scene3DPicture("Mesh3DTransparent", Crossing),
        new Scene3DPicture("Box3DLogAxis", c =>
        {
            Box(c);
            c.ZAxis(new LogAxis("z3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(1, 10000), Title = "Power in W" });
        }),
    ];

    // A waterfall behind a spectrum: the mask stands as a red fence, the markers lie on the floor with their numbers.
    private static void SpectrumWaterfall(Chart3DBuilder chart)
    {
        var display = new SpectrumDisplay { Mask = new FrequencyMask([0, 4e6, 4.5e6, 1e7], [-30, -30, -60, -60]) };
        display.SetView(0, 1e7, -10, 10);
        var frequencies = new double[1024];
        var levels = new float[frequencies.Length];
        for (var time = 0; time < 300; time++)
        {
            var center = 2e6 + (1e4 * time);
            for (var i = 0; i < frequencies.Length; i++)
            {
                frequencies[i] = i * 1e7 / (frequencies.Length - 1);
                var peak = Math.Exp(-Math.Pow((frequencies[i] - center) / 2e5, 2));
                var second = 0.5 * Math.Exp(-Math.Pow((frequencies[i] - 7e6) / 1.5e5, 2));
                levels[i] = (float)(-85 + (65 * Math.Max(peak, second)) + (1.5 * Math.Sin((i * 0.37) + (time * 0.5))));
            }

            display.SetTrace(frequencies, levels);
        }

        display.MarkerFrequency = 7e6;
        display.AddMarker(4.6e6);
        chart.Title("Waterfall with mask and markers")
            .XAxis(new NumericAxis("x3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 1e7), Title = "Frequency", LabelFormat = new NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz" } })
            .YAxis(0, 400, "Traces ago")
            .ZAxis(-90, -10, "Level in dBm")
            .AddWaterfall(Waterfall3D.From(display, new DoubleRange(0, 400)));
    }

    private static void Waterfall(Chart3DBuilder chart, WaterfallStyle style)
    {
        const int Columns = 512;
        const int Rows = 200;
        var data = new SpectrogramData(Columns, Rows);
        var row = new float[Columns];

        // More rows than the ring holds, so that the picture starts in the middle of the ring.
        for (var time = 0; time < Rows + 37; time++)
        {
            var center = 0.3 + (0.4 * time / (Rows + 37));
            for (var column = 0; column < Columns; column++)
            {
                var place = column / (double)(Columns - 1);
                var peak = Math.Exp(-Math.Pow((place - center) / 0.03, 2));
                var second = 0.6 * Math.Exp(-Math.Pow((place - 0.8) / 0.02, 2));
                row[column] = (float)(-80 + (70 * Math.Max(peak, second)) + (0.5 * Math.Sin((column * 0.13) + (time * 0.21))));
            }

            data.AppendRow(row);
        }

        chart.Title("Waterfall")
            .XAxis(0, 10, "Frequency in MHz")
            .YAxis(0, 20, "Age in s")
            .ZAxis(-90, 0, "Level in dBm")
            .AddWaterfall(new Waterfall3D(data, new DoubleRange(0, 10), new DoubleRange(0, 20)) { Style = style });
    }

    /// <summary>Describes a surface whose first axis a 2D chart has narrowed, with the cursor of the group as a frame.</summary>
    /// <param name="chart">The chart.</param>
    public static void Coupled(Chart3DBuilder chart)
    {
        chart.Title("Narrowed by a 2D chart").XAxis(-0.5, 0.5, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddSurface(CreateHillSurface());
        chart.Scene.Cursor = 0.2;
    }

    /// <summary>Gets the cut along the hill, as the data of a 2D chart.</summary>
    /// <returns>The curve.</returns>
    public static CutCurve HillCut()
    {
        var chart = new Chart3DBuilder().XAxis(-1, 1).YAxis(-1, 1).ZAxis(-1, 1);
        return new ClipPlane3D(new Point3D(0, 1, 0), 0.25).Cut(CreateHillSurface(), chart.Scene);
    }

    /// <summary>Describes a cloud whose points are still on their way, which shows the progress in the chart.</summary>
    /// <param name="chart">The chart.</param>
    public static void CloudOnItsWay(Chart3DBuilder chart)
    {
        var cloud = new PointCloud3D(_ =>
        {
            Thread.Sleep(3000);
            return new PointCloudArrays([0], [0], [0]);
        });
        chart.Title("Scanned landscape").XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddCloud(cloud);
    }

    /// <summary>Describes sharp bends, a gap and two lines that cross, with wide lines that show the corners.</summary>
    /// <param name="chart">The chart.</param>
    public static void Bends(Chart3DBuilder chart)
    {
        double[] x = [-0.9, -0.6, -0.5, -0.2, -0.15, 0.1, double.NaN, 0.3, 0.6, 0.9];
        double[] y = [-0.5, 0.5, -0.5, 0.5, -0.5, 0.5, double.NaN, 0.5, -0.5, 0.5];
        double[] z = [-0.5, -0.5, -0.5, -0.5, -0.5, -0.5, double.NaN, -0.5, -0.5, -0.5];
        chart.Title("Bends, gap and crossing")
            .XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z")
            .AddLine(new Line3D(Points3D.From(x, y, z)) { Width = 9 })
            .AddLine(new Line3D(Points3D.From([-1, 1], [0, 0], [-0.3, -0.7])) { Width = 9, Color = new Tachyon.Charts.Rendering.Rgba8(240, 150, 60, 255) });
    }

    private static void Crossing(Chart3DBuilder chart) => chart
        .Title("Transparent surfaces")
        .XAxis(-1, 1, "x")
        .YAxis(-1, 1, "y")
        .ZAxis(-1, 1, "z")
        .AddMesh(CreateSurface((_, y) => y * 0.8, _ => new Rgba8(240, 90, 90, 255), new Material3D { Opacity = 0.5 }))
        .AddMesh(CreateSurface((_, y) => -y * 0.8, _ => new Rgba8(80, 170, 240, 255), new Material3D { Opacity = 0.5 }))
        .AddMesh(CreateSurface((x, y) => (0.25 * Math.Cos(x * 4) * Math.Cos(y * 4)) - 0.2, _ => new Rgba8(255, 200, 90, 255), new Material3D()));

    private static void Box(Chart3DBuilder chart) => chart
        .Title("Axis box")
        .XAxis(0, 10, "Time in s")
        .YAxis(-5, 5, "Offset in mm")
        .ZAxis(0, 100, "Level in dB");
}
