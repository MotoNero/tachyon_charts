using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.ThreeD.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD;

/// <summary>Describes a 3D chart.</summary>
public sealed class Chart3DBuilder
{
    private string? _title;
    private Theme _theme = BuiltInThemes.Instrument;
    private string? _group;

    /// <summary>Gets the scene that this builder fills.</summary>
    public Scene3D Scene { get; } = new();

    /// <summary>Sets the title.</summary>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder Title(string title)
    {
        _title = title;
        return this;
    }

    /// <summary>Sets the theme.</summary>
    /// <param name="theme">The theme.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder Theme(Theme theme)
    {
        _theme = theme;
        return this;
    }

    /// <summary>Sets the kind of projection.</summary>
    /// <param name="projection">The projection.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder Projection(CameraProjection projection)
    {
        Scene.Camera.Projection = projection;
        return this;
    }

    /// <summary>Sets the range of the first axis.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder XAxis(double minimum, double maximum, string? title = null) => XAxis(Create("x3d", minimum, maximum, title));

    /// <summary>Sets the first axis, for example a time axis or a logarithmic one.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder XAxis(AxisBase axis)
    {
        Scene.XAxis = axis;
        return this;
    }

    /// <summary>Sets the range of the second axis.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder YAxis(double minimum, double maximum, string? title = null) => YAxis(Create("y3d", minimum, maximum, title));

    /// <summary>Sets the second axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder YAxis(AxisBase axis)
    {
        Scene.YAxis = axis;
        return this;
    }

    /// <summary>Sets the range of the third axis, which points up.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="title">The title.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder ZAxis(double minimum, double maximum, string? title = null) => ZAxis(Create("z3d", minimum, maximum, title));

    /// <summary>Sets the third axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder ZAxis(AxisBase axis)
    {
        Scene.ZAxis = axis;
        return this;
    }

    /// <summary>Adds a surface.</summary>
    /// <param name="mesh">The surface.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddMesh(Mesh3D mesh)
    {
        Scene.Add(mesh);
        return this;
    }

    /// <summary>Adds a raster surface.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddSurface(Surface3D surface)
    {
        Scene.Add(surface);
        return this;
    }

    /// <summary>Adds a waterfall.</summary>
    /// <param name="waterfall">The waterfall.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddWaterfall(Waterfall3D waterfall)
    {
        Scene.Add(waterfall);
        return this;
    }

    /// <summary>Adds a map from measured points.</summary>
    /// <param name="map">The map.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddMap(MeasuredMap3D map)
    {
        Scene.Add(map);
        return this;
    }

    /// <summary>Adds a line.</summary>
    /// <param name="line">The line.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddLine(Line3D line)
    {
        Scene.Add(line);
        return this;
    }

    /// <summary>Adds a cloud of points.</summary>
    /// <param name="scatter">The cloud.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddScatter(Scatter3D scatter)
    {
        Scene.Add(scatter);
        return this;
    }

    /// <summary>Adds columns.</summary>
    /// <param name="bars">The columns.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddBars(Bars3D bars)
    {
        Scene.Add(bars);
        return this;
    }

    /// <summary>Adds bubbles.</summary>
    /// <param name="bubbles">The bubbles.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddBubbles(Bubbles3D bubbles)
    {
        Scene.Add(bubbles);
        return this;
    }

    /// <summary>Adds a pie in the middle of the box, sets the axes around it, hides them and moves the view closer.</summary>
    /// <param name="pie">The pie.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddPie(Pie3D pie)
    {
        ArgumentNullException.ThrowIfNull(pie);
        var reach = 1 + pie.ExplodeDistance + 0.05;
        XAxis(-reach, reach).YAxis(-reach, reach).ZAxis(-reach, reach);
        Scene.ShowsAxes = false;

        // Without the box the view comes closer, so that the pie fills the chart.
        Scene.Camera.Zoom(0.62);
        Scene.Add(pie);
        return this;
    }

    /// <summary>
    /// Joins a group of charts: the first axis of the 3D chart shares its range with the X axes of the 2D charts, and
    /// the cursor of the 2D charts shows as a plane in the box, while the value under the pointer in 3D shows as a
    /// cursor in 2D. The 2D charts need nothing of 3D for this.
    /// </summary>
    /// <param name="name">The name of the group, as the 2D charts give it.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder Group(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _group = name;
        return this;
    }

    /// <summary>Adds a curve that follows a cut through a surface, as data for a 2D chart.</summary>
    /// <param name="feed">The curve.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddCut(CutCurveFeed feed)
    {
        Scene.Add(feed);
        return this;
    }

    /// <summary>Adds a plane that cuts the scene.</summary>
    /// <param name="plane">The plane.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddPlane(ClipPlane3D plane)
    {
        Scene.Add(plane);
        return this;
    }

    /// <summary>Adds a very large cloud of points.</summary>
    /// <param name="cloud">The cloud.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddCloud(PointCloud3D cloud)
    {
        Scene.Add(cloud);
        return this;
    }

    /// <summary>Adds a volume.</summary>
    /// <param name="volume">The volume.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddVolume(Volume3D volume)
    {
        Scene.Add(volume);
        return this;
    }

    /// <summary>Adds an antenna pattern and sets all three axes to dB around its center.</summary>
    /// <param name="antenna">The pattern.</param>
    /// <returns>This builder.</returns>
    public Chart3DBuilder AddAntenna(AntennaPattern3D antenna)
    {
        ArgumentNullException.ThrowIfNull(antenna);
        Scene.Add(antenna);
        var reach = antenna.Reach;
        return XAxis(-reach, reach, "X in dB").YAxis(-reach, reach, "Y in dB").ZAxis(-reach, reach, "Z in dB");
    }

    /// <summary>Writes the chart into the builder of a 2D chart, which carries the picture, the text and the input.</summary>
    /// <param name="builder">The builder of the 2D chart.</param>
    public void Apply(ChartBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        Scene3DSeries? series = null;
        var picker = new ScenePicker(Scene);
        var pointer = new PointerPick3D(Scene, picker);
        if (_title is not null)
        {
            builder.Title(_title);
        }

        builder.Theme(_theme)
            .XAxis(a => a.Numeric("x").Range(0, 1).Hidden())
            .YAxis(a => a.Numeric("y").Range(0, 1).Hidden())
            .AddSeries((x, y) => series = new Scene3DSeries(Scene, picker, x, y))
            .AddOverlay(chart =>
            {
                series?.Bind(chart, _group is { } name ? ChartGroup.Get(name) : null);
                return new AxisLabels3D(Scene);
            })
            .AddOverlay(_ => new PickLabels3D(Scene, pointer))
            .Modifiers(m => m.Custom(() => new Orbit3DModifier(Scene, pointer)));
    }

    private static NumericAxis Create(string id, double minimum, double maximum, string? title) =>
        new(id, AxisPosition.Bottom) { VisibleRange = new DoubleRange(minimum, maximum), Title = title };
}

/// <summary>Puts a 3D chart into the description of a 2D chart.</summary>
public static class Chart3DExtensions
{
    /// <summary>
    /// Describes a 3D chart inside the description of a 2D chart, which then carries its picture, text and input, as a
    /// <c>Chart3D</c> control does. The 3D chart sets the theme; set another one after this call.
    /// </summary>
    /// <param name="builder">The description of the 2D chart.</param>
    /// <param name="configure">Describes the 3D chart.</param>
    /// <returns>The description of the 2D chart.</returns>
    public static ChartBuilder Scene3D(this ChartBuilder builder, Action<Chart3DBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var scene = new Chart3DBuilder();
        configure(scene);
        scene.Apply(builder);
        return builder;
    }
}
