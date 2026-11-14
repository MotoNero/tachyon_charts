using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;
using Tachyon.Charts.Samples.Shared.Examples.Common;
using Tachyon.Charts.ThreeD;

namespace Tachyon.Charts.Samples.Shared.Examples.ThreeD;

/// <summary>The 3D page „Waterfall, live“ of the showroom.</summary>
public static class WaterfallLivePage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Waterfall, live";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Drag the red mask line below: the fence in 3D follows | Click in 3D: marker in both, on a marker removes it | Ctrl+wheel below: level, the box follows | Drag: turn";

    private const string SpectrumGroup = "spectrum-3d";
    private static readonly SpectrumDemo _spectrum = new();

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c)
    {
        ArgumentNullException.ThrowIfNull(c);
        var display = _spectrum.Display;

        // The ten divisions below the reference level, the same as the spectrum shows.
        var levels = new DoubleRange(display.ReferenceLevel - (ScopeDisplay.VerticalDivisions * display.PerDivision), display.ReferenceLevel);
        c.Title("Waterfall")
            .XAxis(new NumericAxis("x3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(display.Start, display.Stop), Title = "Frequency", LabelFormat = new NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz" } })
            .YAxis(0, 400, "Spectra ago")
            .ZAxis(levels.Min, levels.Max, "dBm")
            .AddWaterfall(new Tachyon.Charts.ThreeD.Scene.Waterfall3D(display.History, new DoubleRange(display.HistoryStart, display.HistoryStop), new DoubleRange(0, 400))
            {
                Display = display,
                Colormap = Tachyon.Charts.Core.Colors.Colormaps.Inferno,
                Style = Tachyon.Charts.ThreeD.Scene.WaterfallStyle.Surface,
                ColorRange = levels,
            })
            .Group(SpectrumGroup);
    }

    /// <summary>Starts or stops the live data.</summary>
    /// <param name="isRunning">Whether the page is shown.</param>
    public static void Run(bool isRunning)
    {
        if (isRunning)
        {
            _spectrum.Start();
        }
        else
        {
            _spectrum.Stop();
        }
    }

    /// <summary>Builds the 2D chart below the 3D chart.</summary>
    /// <param name="c">The 2D chart.</param>
    public static void Companion(ChartBuilder c) => c.Spectrum(_spectrum.Display).Theme(BuiltInThemes.Instrument).Group(SpectrumGroup);
}
