using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Samples.Shared.Examples.Gallery3D;

/// <summary>The 3D picture „Waterfall3DMask“ of the snapshot tests.</summary>
public static class Waterfall3DMaskPicture
{
    /// <summary>The name of the picture.</summary>
    public const string Name = "Waterfall3DMask";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(Chart3DBuilder c) => SpectrumWaterfall(c);

    // A waterfall behind a spectrum: the mask stands as a red fence, the markers lie on the floor with their numbers.
    private static void SpectrumWaterfall(Chart3DBuilder chart)
    {
        var display = new SpectrumDisplay { Mask = new FrequencyMask([0, 4e6, 4.5e6, 1e7], [-30, -30, -60, -60]) };
        display.SetView(0, 1e7, -10, 10);
        var frequencies = new double[1024];
        var levels = new float[frequencies.Length];
        for (var time = 0; time < 300; time++)
        {
            var center = 2e6 + (1e4 * time);
            for (var i = 0; i < frequencies.Length; i++)
            {
                frequencies[i] = i * 1e7 / (frequencies.Length - 1);
                var peak = Math.Exp(-Math.Pow((frequencies[i] - center) / 2e5, 2));
                var second = 0.5 * Math.Exp(-Math.Pow((frequencies[i] - 7e6) / 1.5e5, 2));
                levels[i] = (float)(-85 + (65 * Math.Max(peak, second)) + (1.5 * Math.Sin((i * 0.37) + (time * 0.5))));
            }

            display.SetTrace(frequencies, levels);
        }

        display.MarkerFrequency = 7e6;
        display.AddMarker(4.6e6);
        chart.Title("Waterfall with mask and markers")
            .XAxis(new NumericAxis("x3d", AxisPosition.Bottom) { VisibleRange = new DoubleRange(0, 1e7), Title = "Frequency", LabelFormat = new NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz" } })
            .YAxis(0, 400, "Traces ago")
            .ZAxis(-90, -10, "Level in dBm")
            .AddWaterfall(Waterfall3D.From(display, new DoubleRange(0, 400)));
    }
}
