using Tachyon.Charts.Core.Annotations;
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
    public static void Apply(ChartSpec spec, ChartCatalog catalog, ChartBuilder chart, ICollection<IDisposable>? running) => Apply(spec, catalog, chart, running, null);

    /// <summary>
    /// Makes the data of every series and sets them up, the slow part of a description, so that it can run away from the
    /// window; live data are left out, they start with the chart.
    /// </summary>
    /// <param name="spec">The description.</param>
    /// <param name="catalog">The types, samples and interactions.</param>
    /// <returns>The data in the order <see cref="Apply(ChartSpec, ChartCatalog, ChartBuilder, ICollection{IDisposable}?, IReadOnlyList{object?}?, Action{int, Annotation}?)"/> takes them; <see langword="null"/> for live data.</returns>
    internal static IReadOnlyList<object?> Prepare(ChartSpec spec, ChartCatalog catalog) =>
        [.. Drawn(spec, catalog).Select(series => series.Data.Kind == DataKind.Live ? null : Data(catalog, series, null))];

    /// <summary>Describes one of the linked charts of a description with data made before by <see cref="Prepare"/>.</summary>
    /// <param name="spec">The description; its one series has a type with views.</param>
    /// <param name="catalog">The types, samples and interactions.</param>
    /// <param name="chart">The builder.</param>
    /// <param name="view">The number of the view.</param>
    /// <param name="prepared">The data, shared by all views.</param>
    internal static void ApplyView(ChartSpec spec, ChartCatalog catalog, ChartBuilder chart, int view, IReadOnlyList<object?> prepared)
    {
        var (views, _) = catalog.ViewsOf(spec) ?? throw new InvalidOperationException("The description has no linked charts.");
        if (spec.Theme is { } theme)
        {
            chart.Theme(Theme(theme));
        }

        views[view].Apply(chart, spec.Series[0], prepared[0]!);
    }

    // The series in the order they are drawn: a scene takes them as they are, a chart splits CSV columns into lines.
    private static IEnumerable<SeriesSpec> Drawn(ChartSpec spec, ChartCatalog catalog) =>
        catalog.SceneOf(spec) is null ? DataSources.Expanded(spec.Series, catalog) : spec.Series;

    /// <summary>Writes a description into a chart builder with data made before by <see cref="Prepare"/>.</summary>
    /// <param name="spec">The description.</param>
    /// <param name="catalog">The types, samples and interactions.</param>
    /// <param name="chart">The builder.</param>
    /// <param name="running">Receives what keeps live data coming, or <see langword="null"/>.</param>
    /// <param name="prepared">The data of the series, or <see langword="null"/> to make them now.</param>
    /// <param name="edited">
    /// For an editor: makes every annotation editable with the mouse and tells, on the thread of the chart, its index
    /// when a drag ended; <see langword="null"/> to follow the description.
    /// </param>
    internal static void Apply(ChartSpec spec, ChartCatalog catalog, ChartBuilder chart, ICollection<IDisposable>? running, IReadOnlyList<object?>? prepared, Action<int, Annotation>? edited = null)
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
                var index = 0;
                foreach (var series in spec.Series)
                {
                    catalog.FindType(series.Type)!.SceneApply!(builder, series, prepared?[index] ?? Data(catalog, series, running));
                    index++;
                }
            });
            if (spec.Theme is { } chosen)
            {
                chart.Theme(Theme(chosen));
            }
        }
        else
        {
            var index = 0;
            foreach (var series in DataSources.Expanded(spec.Series, catalog))
            {
                catalog.FindType(series.Type)!.Apply(chart, series, prepared?[index] ?? Data(catalog, series, running), new SeriesAxes(series.XAxis ?? spec.XAxes[0].Id, series.YAxis ?? spec.YAxes[0].Id));
                index++;
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

        var isEditable = spec.Modifiers.Exists(modifier => modifier.Type == "annotationedit");
        if (edited is not null && !isEditable && spec.Annotations.Count > 0)
        {
            chart.Modifiers(modifiers => modifiers.AnnotationEdit());
        }

        for (var i = 0; i < spec.Annotations.Count; i++)
        {
            var (index, values) = (i, spec.Annotations[i].Parameters);
            var info = ChartCatalog.FindAnnotation(spec.Annotations[i].Type)!;
            chart.AddOverlay(surface =>
            {
                var overlay = info.Create(surface, values);
                if (overlay is Annotation annotation && (isEditable || edited is not null))
                {
                    annotation.IsEditable = true;
                    if (edited is not null)
                    {
                        annotation.EditFinished += finished => edited(index, finished);
                    }
                }

                return overlay;
            });
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
