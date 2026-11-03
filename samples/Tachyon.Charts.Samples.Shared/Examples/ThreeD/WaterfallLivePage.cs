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
    public static void Configure(Chart3DBuilder c) => c.Title("Waterfall of the spectrum")
        .XAxis(new NumericAxis("x3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 10_000_000), Title = "Frequency", LabelFormat = new NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz" } })
        .YAxis(0, 400, "Traces ago")
        .ZAxis(-90, 0, "Level in dBm")
        .AddWaterfall(new Tachyon.Charts.ThreeD.Scene.Waterfall3D(_spectrum.Display.History, new DoubleRange(0, 10_000_000), new DoubleRange(0, 400)) { Display = _spectrum.Display })
        .Group(SpectrumGroup);

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
