namespace Tachyon.Charts.Configurator;

/// <summary>
/// The pages of the showroom as descriptions, so that "Open in configurator" opens each example completely: the same
/// charts, series, axes, interactions and data of the same form.
/// </summary>
internal static class ShowroomPages
{
    /// <summary>Gets the pages of 2D charts and of the interactive part.</summary>
    public static IReadOnlyList<PageInfo> Core { get; } =
    [
        new("Live-Chart (interaktiv)", LiveChart),
    ];

    // A fixed curve, 50,000 new points per second and an overview below, with all interactions of the page.
    private static ChartSpec LiveChart(ChartCatalog catalog)
    {
        var spec = new ChartSpec
        {
            Title = "Live: 50,000 points per second",
            Theme = "TachyonDark",
            Overview = true,
            XAxes = { new AxisSpec { Id = "x", Title = "Time in s" } },
            YAxes = { new AxisSpec { Id = "y", Title = "Value", Minimum = -2, Maximum = 2 } },
        };
        spec.Series.Add(Line("Fixed", "liveFixed", 2000, "#50AAF0", 2));
        var live = Line("Live", "liveNoisy", 1_000_000, "#F0825A", 1);
        live.Data.Kind = DataKind.Live;
        live.Data.Options.Set("rate", 50_000.0);
        spec.Series.Add(live);
        foreach (var modifier in new[] { "zoompan", "rubberband", "axisdrag", "crosshair", "tooltip", "legend", "keyboard", "selection", "measure", "diagnostics" })
        {
            spec.Modifiers.Add(new ModifierSpec { Type = modifier });
        }

        spec.Modifiers.Single(modifier => modifier.Type == "selection").Parameters.Set("lasso", true);
        return spec;
    }

    private static SeriesSpec Line(string name, string sample, int count, string color, double thickness)
    {
        var series = new SeriesSpec { Type = "line", Name = name, Data = new DataSpec { Sample = sample, Count = count } };
        series.Parameters.Set("color", color);
        series.Parameters.Set("thickness", thickness);
        return series;
    }
}
