using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;

namespace Tachyon.Charts.Samples.Shared.Examples.Instruments;

/// <summary>A running spectrum analyzer with mask and markers, and the history of its traces below.</summary>
public static class SpectrumAnalyzerPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Spektrum (interaktiv)";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Wheel: frequency | Left: dB/div | Ctrl+wheel: reference | Drag: pan | Click: marker | Ctrl+click: add or remove a marker | Double click: peak";

    /// <summary>Builds the spectrum.</summary>
    /// <param name="c">The chart.</param>
    /// <param name="demo">The running measurement.</param>
    public static void Configure(ChartBuilder c, SpectrumDemo demo) => c.Spectrum(demo.Display).Theme(BuiltInThemes.Instrument);

    /// <summary>Builds the history below the spectrum.</summary>
    /// <param name="c">The chart.</param>
    /// <param name="demo">The running measurement.</param>
    public static void ConfigureHistory(ChartBuilder c, SpectrumDemo demo) => c.SpectrumHistory(demo.Display).Theme(BuiltInThemes.Instrument);
}
