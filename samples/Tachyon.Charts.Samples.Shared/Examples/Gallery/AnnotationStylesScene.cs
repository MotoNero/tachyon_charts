using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery;

/// <summary>
/// Annotations with styled labels: placed along their line, turned with it, on plates with borders and signs, over
/// dashed and dotted lines.
/// </summary>
public sealed class AnnotationStylesScene : LineScene, Scenes.IChartScene
{
    private static readonly Rgba8 _red = new(240, 90, 90, 255);
    private static readonly Rgba8 _violet = new(150, 110, 230, 255);
    private static readonly Rgba8 _dark = new(30, 30, 30, 255);
    private static readonly Rgba8 _white = new(255, 255, 255, 255);
    private static readonly Rgba8 _clear = new(0, 0, 0, 0);

    /// <inheritdoc />
    public override string Name => "AnnotationStyles";

    /// <summary>The annotations can be moved with the mouse.</summary>
    /// <returns>The interaction.</returns>
    IEnumerable<Core.Input.ChartModifier> Scenes.IChartScene.CreateInteractions() => [new Core.Input.AnnotationEditModifier()];

    /// <inheritdoc />
    protected override IReadOnlyList<IDisposable> AddSeries(ChartSurface chart, NumericAxis x, NumericAxis y)
    {
        var line = new LineSeries<double, double>(CreateData(value => 0.9 * Math.Sin(value * 1.3)), x, y) { Color = Blue, Thickness = 2 };
        chart.AddSeries(line);

        // A limit with a red plate and a warning sign at its end, over a dashed line.
        chart.AddOverlay(new HorizontalLineAnnotation(y)
        {
            Y = 1.1,
            Label = "Upper limit",
            Color = _red,
            IsEditable = true,
            Appearance = new AnnotationAppearance { LabelPlace = LabelPlace.End, LabelIcon = LabelIcon.Warning, LabelColor = _white, Dash = LineDash.Dashed },
        });

        // A trigger whose label runs upright along the line, without a plate.
        chart.AddOverlay(new VerticalLineAnnotation(x)
        {
            X = 6,
            Label = "Trigger",
            Color = Green,
            IsEditable = true,
            Appearance = new AnnotationAppearance { LabelTurn = LabelTurn.AlongLine, LabelFill = _clear, LabelSize = 14, Thickness = 2 },
        });

        // A window with dotted edges and a framed label without a plate.
        var band = new BandAnnotation(x)
        {
            Color = new Rgba8(110, 210, 130, 45),
            IsEditable = true,
            Label = "Window",
            Appearance = new AnnotationAppearance { LabelFill = _clear, LabelBorder = Green, LabelColor = Green, Dash = LineDash.Dotted, Thickness = 1.5f },
        };
        band.SetRange(7.5, 9);
        chart.AddOverlay(band);

        // A box with a dashed border and a label on an orange plate with a dark frame and an information sign.
        var box = new BoxAnnotation(x, y)
        {
            Color = Orange,
            IsEditable = true,
            Label = "Peak",
            Appearance = new AnnotationAppearance { LabelIcon = LabelIcon.Info, LabelBorder = _dark, LabelBorderThickness = 1.5f, Dash = LineDash.Dashed },
        };
        box.SetCorners(1.9, 1.25, 3.6, 0.55);
        chart.AddOverlay(box);

        // A trend whose label follows the slope, after a dotted line.
        var trend = new LineAnnotation(x, y)
        {
            Color = _violet,
            IsEditable = true,
            Label = "Trend",
            Appearance = new AnnotationAppearance { LabelTurn = LabelTurn.AlongLine, LabelPlace = LabelPlace.Middle, LabelIcon = LabelIcon.Arrow, Dash = LineDash.Dotted },
        };
        trend.SetEnds(0.4, -1.3, 4.6, -0.5);
        chart.AddOverlay(trend);

        // A note turned by an angle on a framed plate.
        var note = new TextAnnotation(x, y)
        {
            Text = "Turned note",
            IsEditable = true,
            Color = _red,
            Appearance = new AnnotationAppearance { LabelTurn = LabelTurn.Angle, LabelAngle = 15, LabelFill = new Rgba8(240, 90, 90, 60), LabelBorder = _red, LabelColor = _red },
        };
        note.MoveTo(6.6, -1.05);
        chart.AddOverlay(note);

        return [line];
    }
}
