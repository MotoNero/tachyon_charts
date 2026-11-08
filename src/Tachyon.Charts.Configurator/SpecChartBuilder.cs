using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Fluent;
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
    public static void Apply(ChartSpec spec, ChartCatalog catalog, ChartBuilder chart)
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

        if (spec.Theme is { } theme)
        {
            chart.Theme(Theme(theme));
        }

        if (spec.XDown)
        {
            chart.XDown();
        }

        foreach (var axis in spec.XAxes)
        {
            chart.XAxis(builder => Axis(builder, axis, AxisPosition.Top));
        }

        foreach (var axis in spec.YAxes)
        {
            chart.YAxis(builder => Axis(builder, axis, AxisPosition.Right));
        }

        foreach (var series in spec.Series)
        {
            var type = catalog.FindType(series.Type)!;
            var data = catalog.FindSample(series.Data.Sample)!.Create(series.Data.Count);
            type.Apply(chart, series, data, new SeriesAxes(series.XAxis ?? spec.XAxes[0].Id, series.YAxis ?? spec.YAxes[0].Id));
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

        if (spec.Legend is { } legend)
        {
            chart.Legend(builder =>
            {
                if (legend.Title.Length > 0)
                {
                    builder.Title(legend.Title);
                }
            });
        }

        if (spec.Group is { Length: > 0 } group)
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

    private static void Axis(AxisBuilder builder, AxisSpec axis, AxisPosition opposite)
    {
        _ = axis.Kind switch
        {
            AxisKind.Logarithmic => builder.Log(axis.Id),
            AxisKind.DateTime => builder.DateTime(axis.Id),
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
    }
}
