using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Interactive;

/// <summary>The live chart of the showroom: a fixed curve and 50,000 new points per second.</summary>
public static class LiveChartPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Live-Chart (interaktiv)";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan | Ctrl+drag: zoom into a rectangle | Shift+drag: select points | Alt+drag: measure | Double click: all | Right click: menu";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    /// <param name="signal">The data.</param>
    /// <param name="selection">Selects points with the lasso.</param>
    public static void Configure(ChartBuilder c, LiveSignal signal, ChartModifier selection) => c
        .Title(SampleText.T("Live: 50,000 points per second"))
        .Theme(BuiltInThemes.TachyonDark)
        .XAxis(a => a.Numeric("x").Title(SampleText.T("Time in s")).AutoRange())
        .YAxis(a => a.Numeric("y").Title(SampleText.T("Value")).Range(-2, 2))
        .AddLine(signal.Fixed, s => s.Name(SampleText.T("Fixed")).Stroke(new Rgba8(80, 170, 240, 255)).Thickness(2))
        .AddLine(signal.Live, s => s.Name("Live").Stroke(new Rgba8(240, 130, 90, 255)).Thickness(1))
        .Modifiers(m => m.ZoomPan().RubberBand().AxisDrag().Crosshair().Tooltip().Legend().Keyboard().Custom(() => selection).MeasureCursors().Diagnostics());
}
