using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>The instrument page „Network (interaktiv)“ of the showroom.</summary>
public static class NetworkPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Network (interaktiv)";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag a marker: move it | Ctrl+click: add or remove a marker | Wheel: zoom | Drag: pan";

    /// <summary>Builds the chart.</summary>
    /// <param name="builder">The chart.</param>
    public static void Configure(ChartBuilder builder)
    {
        var display = Common.NetworkData.CreateDisplay(passing: true);
        builder.NetworkAnalyzer(display, 1e9, 2e9, -70, 5)
            .Theme(BuiltInThemes.Instrument)
            .Modifiers(m => m.ZoomPan().Custom(() => new NetworkMarkerModifier(display)));
    }
}
