namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>Finds what lies at a place of the picture without a chart, for applications and measurements.</summary>
public static class Pick3DProbe
{
    /// <summary>Finds what lies at a place.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="x">The place from -1 to 1.</param>
    /// <param name="y">The place from -1 to 1.</param>
    /// <returns>The hit on a surface or waterfall, if any.</returns>
    public static Pick3D? Find(Scene3D scene, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return new ScenePicker(scene).Find(scene.Camera.CreateRay(16.0 / 9, x, y));
    }
}
