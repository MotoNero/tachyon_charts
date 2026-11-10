using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Themes;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Builds the chart of a <see cref="ChartSpec"/> for the preview, with sample data. The code generator writes the same
/// steps as C#, so preview and code show the same chart.
/// </summary>
public static class SpecChartBuilder
{
    /// <summary>Adds everything a description holds to a chart description.</summary>
    /// <param name="spec">The description of the configurator.</param>
    /// <param name="catalog">The types, samples and interactions.</param>
    /// <param name="chart">The chart description to fill.</param>
    /// <exception cref="InvalidOperationException">The description has problems; see <see cref="ChartCatalog.Check"/>.</exception>
    public static void Apply(ChartSpec spec, ChartCatalog catalog, ChartBuilder chart) => Apply(spec, catalog, chart, null);

    /// <summary>Writes a description into a chart builder and starts its live data.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="catalog">The types, samples and interactions.</param>
    /// <param name="chart">The builder.</param>
    /// <param name="running">
    /// Receives what keeps live data coming, to dispose when the chart is gone; <see langword="null"/> leaves live data
    /// empty, as for a description that is only checked.
    /// </param>
    /// <exception cref="InvalidOperationException">The description has problems.</exception>
    public static void Apply(ChartSpec spec, ChartCatalog catalog, ChartBuilder chart, ICollection<IDisposable>? running)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(chart);

        if (catalog.Check(spec) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        if (spec.Title.Length > 0)
        {
            chart.Title(spec.Title);
        }

        // A scene sets its own theme, so the chosen one follows it.
        var scene = catalog.SceneOf(spec);
        if (spec.Theme is { } theme && scene is null)
        {
            chart.Theme(Theme(theme));
        }

        if (spec.XDown)
        {
            chart.XDown();
        }

        if (!catalog.OwnsAxes(spec))
        {
            foreach (var axis in spec.XAxes)
            {
                chart.XAxis(builder => Axis(builder, axis, AxisPosition.Top));
            }

            foreach (var axis in spec.YAxes)
            {
                chart.YAxis(builder => Axis(builder, axis, AxisPosition.Right));
            }
        }

        if (scene is not null)
        {
            scene.Apply(chart, spec, builder =>
            {
                foreach (var series in spec.Series)
                {
                    catalog.FindType(series.Type)!.SceneApply!(builder, series, Data(catalog, series, running));
                }
            });
            if (spec.Theme is { } chosen)
            {
                chart.Theme(Theme(chosen));
            }
        }
        else
        {
            foreach (var series in spec.Series)
            {
                catalog.FindType(series.Type)!.Apply(chart, series, Data(catalog, series, running), new SeriesAxes(series.XAxis ?? spec.XAxes[0].Id, series.YAxis ?? spec.YAxes[0].Id));
            }
        }

        if (spec.Modifiers.Count > 0)
        {
            chart.Modifiers(modifiers =>
            {
                foreach (var modifier in spec.Modifiers)
                {
                    catalog.FindModifier(modifier.Type)!.Apply(modifiers, modifier.Parameters);
                }
            });
        }

        foreach (var annotation in spec.Annotations)
        {
            var info = ChartCatalog.FindAnnotation(annotation.Type)!;
            chart.AddOverlay(surface => info.Create(surface, annotation.Parameters));
        }

        foreach (var legend in (ReadOnlySpan<LegendSpec?>)[spec.Legend, spec.SecondLegend])
        {
            if (legend is not null)
            {
                chart.Legend(builder => Legend(builder, legend));
            }
        }

        if (spec.Group is { Length: > 0 } group && scene is null)
        {
            chart.Group(group);
        }
    }

    /// <summary>Gets a built-in theme by name.</summary>
    /// <param name="name">One of <see cref="ChartCatalog.Themes"/>.</param>
    /// <returns>The theme.</returns>
    public static Theme Theme(string name) => name switch
    {
        "TachyonLight" => BuiltInThemes.TachyonLight,
        "HighContrast" => BuiltInThemes.HighContrast,
        "Neon" => BuiltInThemes.Neon,
        "Instrument" => BuiltInThemes.Instrument,
        "Signal" => BuiltInThemes.Signal,
        "Paper" => BuiltInThemes.Paper,
        _ => BuiltInThemes.TachyonDark,
    };

    /// <summary>Gets the weekends of this century, the gaps of a time axis without weekends; the code writes the same.</summary>
    internal static TimeGapMap Weekends { get; } = TimeGapMap.Weekends(new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc);

    private static void Axis(AxisBuilder builder, AxisSpec axis, AxisPosition opposite)
    {
        _ = axis.Kind switch
        {
            AxisKind.Logarithmic => builder.Log(axis.Id),
            AxisKind.DateTime => builder.DateTime(axis.Id),
            AxisKind.Category => builder.Category(axis.ReadCategories(), axis.Id),
            _ => builder.Numeric(axis.Id),
        };
        if (axis.Title.Length > 0)
        {
            builder.Title(axis.Title);
        }

        if (axis.Minimum is { } minimum && axis.Maximum is { } maximum)
        {
            builder.Range(minimum, maximum);
        }
        else
        {
            builder.AutoRange();
        }

        if (axis.IsInverted)
        {
            builder.Inverted();
        }

        if (axis.IsOpposite)
        {
            builder.Position(opposite);
        }

        if (axis.SkipsWeekends && axis.Kind == AxisKind.DateTime)
        {
            builder.Gaps(Weekends);
        }

        if (axis.CrossesZero)
        {
            builder.CrossesAt(0);
        }

        if (axis.ReadBreaks() is { Count: > 0 } breaks)
        {
            builder.Breaks([.. breaks.Select(range => new DoubleRange(range.From, range.To))]);
        }

        if (axis.Pane > 0 || axis.PaneWeight != 1)
        {
            builder.Pane(axis.Pane, axis.PaneWeight);
        }

        if (axis.IsHidden)
        {
            builder.Hidden();
        }
    }

    private static void Legend(LegendBuilder builder, LegendSpec legend)
    {
        if (legend.Title.Length > 0)
        {
            builder.Title(legend.Title);
        }

        if (legend.Placement != "TopRight")
        {
            builder.Place(Enum.Parse<LegendPlacement>(legend.Placement));
        }

        if (legend.ReadSeries() is { Length: > 0 } names)
        {
            builder.Series(names);
        }
    }

    private static object Data(ChartCatalog catalog, SeriesSpec series, ICollection<IDisposable>? running)
    {
        var type = catalog.FindType(series.Type)!;
        if (series.Data.Kind != DataKind.Sample)
        {
            var loaded = DataSources.Load(series, type, catalog, running);
            type.Prepare?.Invoke(series, loaded);
            return loaded;
        }

        var sample = catalog.FindSample(series.Data.Sample)!;
        var data = sample.Create(Math.Max(sample.MinimumCount, series.Data.Count));
        catalog.FindType(series.Type)!.Prepare?.Invoke(series, data);
        return data;
    }
}
