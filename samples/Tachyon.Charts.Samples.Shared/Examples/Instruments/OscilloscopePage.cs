using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>A running oscilloscope with four channels and a trigger.</summary>
public static class OscilloscopePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Oszilloskop (interaktiv)";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag at the left: channel | at the right: trigger | else: time | Wheel: time/div | Left, on V/div or Ctrl+wheel: volts/div";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    /// <param name="demo">The running measurement.</param>
    public static void Configure(ChartBuilder c, ScopeDemo demo) => c.Oscilloscope(demo.Display).Theme(BuiltInThemes.Instrument);
}
