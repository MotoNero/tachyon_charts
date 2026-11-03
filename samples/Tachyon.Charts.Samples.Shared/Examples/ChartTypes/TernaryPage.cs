using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Samples.Shared.Examples.ChartTypes;

/// <summary>The chart type „Ternary“ of the showroom.</summary>
public static class TernaryPage
{
    /// <summary>The name of the page.</summary>
    public const string Name = "Ternary";

    /// <summary>How to operate the chart.</summary>
    public const string Hint = "Pointer: values";

    /// <summary>Builds the chart.</summary>
    /// <param name="c">The chart.</param>
    public static void Configure(ChartBuilder c)
    {
        var random = new Random(2);
        var a = new double[300];
        var b = new double[300];
        var third = new double[300];
        for (var i = 0; i < a.Length; i++)
        {
            a[i] = random.NextDouble();
            b[i] = random.NextDouble();
            third[i] = random.NextDouble();
        }

        c.Ternary(a, b, third, ("A", "B", "C")).Theme(BuiltInThemes.Instrument);
    }
}
