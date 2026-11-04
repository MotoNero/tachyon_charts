namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Shares for the pie examples, with a few small slices that need lines to their labels.</summary>
public static class PieData
{
    /// <summary>The sources of a power mix in percent.</summary>
    public static double[] Values { get; } = [32, 18, 15, 14, 9, 6, 3, 1.5, 1, 0.5];

    /// <summary>The names of the sources.</summary>
    public static string[] Names { get; } = ["Wind", "Solar", "Gas", "Coal", "Biomass", "Hydro", "Nuclear", "Oil", "Other", "Geothermal"];

    /// <summary>How to operate a pie.</summary>
    public const string Hint = "Pointer: name, value and share | Click: pull a slice out, again: back";
}
