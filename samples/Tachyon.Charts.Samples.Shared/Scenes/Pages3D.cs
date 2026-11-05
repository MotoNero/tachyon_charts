using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Samples.Shared.Examples.ThreeD;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Scenes;

/// <summary>An operable 3D page.</summary>
/// <param name="Name">The English name of the page.</param>
/// <param name="Hint">The operation.</param>
/// <param name="Configure">Describes the chart.</param>
/// <param name="Run">Starts and stops a live source, if the page has one.</param>
/// <param name="Companion">A 2D chart below the 3D chart, in the same group, if any.</param>
public sealed record Page3D(string Name, string Hint, Action<Chart3DBuilder> Configure, Action<bool>? Run = null, Action<ChartBuilder>? Companion = null);

/// <summary>The 3D pages of the sample apps.</summary>
public static class Pages3D
{

    /// <summary>Gets the pages.</summary>
    public static IReadOnlyList<Page3D> All { get; } =
    [
        new(WaterfallLivePage.Name, WaterfallLivePage.Hint, WaterfallLivePage.Configure, WaterfallLivePage.Run, Companion: WaterfallLivePage.Companion),
        new(PathLivePage.Name, PathLivePage.Hint, PathLivePage.Configure, PathLivePage.Run),
        new(PathOf200000PointsPage.Name, PathOf200000PointsPage.Hint, PathOf200000PointsPage.Configure),
        new(ScatterOf3000PointsPage.Name, ScatterOf3000PointsPage.Hint, ScatterOf3000PointsPage.Configure),
        new(ScatterOf10MillionPointsPage.Name, ScatterOf10MillionPointsPage.Hint, ScatterOf10MillionPointsPage.Configure),
        new(CutPlanesPage.Name, CutPlanesPage.Hint, CutPlanesPage.Configure),
        new(OnePlaneAllKindsPage.Name, OnePlaneAllKindsPage.Hint, OnePlaneAllKindsPage.Configure),
        new(CloudLivePage.Name, CloudLivePage.Hint, CloudLivePage.Configure, CloudLivePage.Run),
        new(CloudOf20MillionPointsPage.Name, CloudOf20MillionPointsPage.Hint, CloudOf20MillionPointsPage.Configure),
        new(CloudOf100MillionPointsPage.Name, CloudOf100MillionPointsPage.Hint, CloudOf100MillionPointsPage.Configure),
        new(VolumeSlices512Page.Name, VolumeSlices512Page.Hint, VolumeSlices512Page.Configure),
        new(ThroughAVolume512Page.Name, ThroughAVolume512Page.Hint, ThroughAVolume512Page.Configure),
        new(ObjectsInsideAVolumePage.Name, ObjectsInsideAVolumePage.Hint, ObjectsInsideAVolumePage.Configure),
        new(MaximumProjection512Page.Name, MaximumProjection512Page.Hint, MaximumProjection512Page.Configure),
        new(SurfaceOfEqualValue512Page.Name, SurfaceOfEqualValue512Page.Hint, SurfaceOfEqualValue512Page.Configure),
        new(ColumnsPage.Name, ColumnsPage.Hint, ColumnsPage.Configure),
        new(N1MillionColumnsPage.Name, N1MillionColumnsPage.Hint, N1MillionColumnsPage.Configure),
        new(CutLivePage.Name, CutLivePage.Hint, CutLivePage.Configure, Companion: CutLivePage.Companion),
        new(ModelWithMeasuringPointsPage.Name, ModelWithMeasuringPointsPage.Hint, ModelWithMeasuringPointsPage.Configure),
        new(ModelOf1MillionTrianglesPage.Name, ModelOf1MillionTrianglesPage.Hint, ModelOf1MillionTrianglesPage.Configure),
        new(AntennaPatternPage.Name, AntennaPatternPage.Hint, AntennaPatternPage.Configure),
        new(MapFromMeasurementsPage.Name, MapFromMeasurementsPage.Hint, MapFromMeasurementsPage.Configure),
        new(HeightLinesPage.Name, HeightLinesPage.Hint, HeightLinesPage.Configure),
        new(Surface40964096Page.Name, Surface40964096Page.Hint, Surface40964096Page.Configure),
        new(SurfaceWithLightPage.Name, SurfaceWithLightPage.Hint, SurfaceWithLightPage.Configure),
        new(AxisBoxPage.Name, AxisBoxPage.Hint, AxisBoxPage.Configure),
        new(VideoRecordingPage.Name, VideoRecordingPage.Hint, VideoRecordingPage.Configure),
    ];

    /// <summary>Finds a page.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The page, or <see langword="null"/>.</returns>
    public static Page3D? Find(string name) => All.FirstOrDefault(page => page.Name == name);
}
