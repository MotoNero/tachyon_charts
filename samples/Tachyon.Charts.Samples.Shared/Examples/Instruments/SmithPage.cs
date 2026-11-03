using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>The instrument page „Smith (interaktiv)“ of the showroom.</summary>
public static class SmithPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Smith (interaktiv)";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Click or drag: the marker follows the curve";

    /// <summary>Builds the chart.</summary>
    /// <param name="builder">The chart.</param>
    public static void Configure(ChartBuilder builder)
    {
        const int Count = 200;
        var resistance = new double[Count];
        var reactance = new double[Count];
        for (var i = 0; i < Count; i++)
        {
            var detuning = (i - (Count / 2.0)) / (Count / 2.0);
            resistance[i] = 20 + (10 * detuning * detuning);
            reactance[i] = 120 * detuning;
        }

        builder.Smith(resistance, reactance, Count / 2).Theme(BuiltInThemes.Instrument);
    }
}
