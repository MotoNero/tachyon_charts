using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>Prices and plans: candles or OHLC bars with volume, market profile and drawing tools, and Gantt charts.</summary>
internal static class FinanceTypes
{
    public static IReadOnlyList<ChartTypeInfo> Create() => [Candles(), Gantt()];

    public static IReadOnlyList<SampleInfo> Samples() => [Prices(), Plan()];

    private static ChartTypeInfo Candles() => new(
        "candles",
        "Candles",
        ChartCategory.TwoD,
        DataShape.Candles,
        "prices",
        [
            new("style", "Style", ParameterKind.Choice, nameof(CandleStyle.Candles)) { Choices = Enum.GetNames<CandleStyle>() },
            new("volume", "Volume below", ParameterKind.Flag, true),
            new("profile", "Market profile", ParameterKind.Flag, false) { Description = "The volume per price level of the visible days, at the right." },
            new("levels", "Price levels of the profile", ParameterKind.Integer, 40.0) { Minimum = 2, Maximum = 1000 },
            new("drawings", "Drawing tools", ParameterKind.Flag, false) { Description = "Trend lines, channels and more, drawn with the mouse." },
            CoreModule.ValuesParameter,
        ],
        ApplyCandles,
        CandlesCode)
    {
        Usings = ["Tachyon.Charts.Core.Series", "Tachyon.Charts.Core.Annotations"],
        UsesTime = true,
    };

    private static ChartTypeInfo Gantt() => new(
        "gantt",
        "Gantt",
        ChartCategory.TwoD,
        DataShape.Gantt,
        "plan",
        [new("barHeight", "Height of the bars", ParameterKind.Number, 0.6) { Minimum = 0.05, Maximum = 1 }],
        (chart, series, data, _) => chart
            .XAxis(axis => axis.DateTime("time").AutoRange())
            .Gantt((GanttData)data, gantt => (gantt.Name, gantt.BarHeight) = (series.Name, series.Parameters.GetNumber("barHeight", 0.6))),
        (series, data, _) => $".XAxis(a => a.DateTime(\"time\").AutoRange()).Gantt({data}, g => (g.Name, g.BarHeight) = ({CodeText.String(series.Name)}, {CodeText.Number(series.Parameters.GetNumber("barHeight", 0.6))}))")
    {
        Usings = ["Tachyon.Charts.Core.Series"],
        OwnsAxes = true,
        TakesInteractions = true,
    };

    // The candles on the axes of the series; volume in a pane of its own below, the profile on the price axis.
    private static void ApplyCandles(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var (prices, values) = ((CandleData)data, series.Parameters);
        chart.Candles(
            prices,
            candles =>
            {
                candles.Name = series.Name;
                candles.Style = Enum.Parse<CandleStyle>(values.GetText("style", nameof(CandleStyle.Candles)));
                candles.ShowsValues = values.GetFlag("values", false);
            },
            axes.Y);
        if (values.GetFlag("volume", true))
        {
            chart.YAxis(axis => axis.Numeric("volume").Title("Volume").AutoRange().Pane(1, 0.3)).Volume(prices, "volume");
        }

        if (values.GetFlag("profile", false))
        {
            var levels = (int)values.GetNumber("levels", 40);
            chart.VolumeProfile(prices, profile => profile.Levels = levels, axes.Y);
        }

        if (values.GetFlag("drawings", false))
        {
            chart.Drawings();
        }
    }

    private static string CandlesCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var code = $".Candles({data}, s => (s.Name, s.Style, s.ShowsValues) = ({CodeText.String(series.Name)}, CandleStyle.{values.GetText("style", nameof(CandleStyle.Candles))}, {CodeText.Flag(values.GetFlag("values", false))}), {CodeText.String(axes.Y)})";
        if (values.GetFlag("volume", true))
        {
            code += $".YAxis(a => a.Numeric(\"volume\").Title(\"Volume\").AutoRange().Pane(1, 0.3)).Volume({data}, \"volume\")";
        }

        if (values.GetFlag("profile", false))
        {
            code += $".VolumeProfile({data}, p => p.Levels = {(int)values.GetNumber("levels", 40)}, {CodeText.String(axes.Y)})";
        }

        return code + (values.GetFlag("drawings", false) ? ".Drawings()" : string.Empty);
    }

    // Daily prices of working days from Monday, 5 January 2026: waves, a slow rise and the volume of each day.
    private static SampleInfo Prices() => new(
        "prices",
        "Prices of working days",
        DataShape.Candles,
        120,
        count =>
        {
            var data = new CandleData(count);
            var (day, close) = (new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100.0);
            for (var i = 0; i < count; day = day.AddDays(1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var open = close;
                close = 100 + (10 * Math.Sin(i * 0.11)) + (4 * Math.Sin(i * 0.37)) + (i * 0.05);
                var high = Math.Max(open, close) + 0.8 + (0.6 * Math.Abs(Math.Sin(i * 1.7)));
                var low = Math.Min(open, close) - 0.8 - (0.5 * Math.Abs(Math.Cos(i * 1.3)));
                data.Append(day.Ticks, open, high, low, close, 1000 + (600 * Math.Abs(Math.Sin(i * 0.9))));
                i++;
            }

            return data;
        },
        "CandleData",
        method => SampleCatalog.Header("prices of working days", method, "CandleData", "The number of days.") + """
                {
                    var data = new CandleData(count);
                    var (day, close) = (new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc), 100.0);
                    for (var i = 0; i < count; day = day.AddDays(1))
                    {
                        if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                        {
                            continue;
                        }

                        var open = close;
                        close = 100 + (10 * Math.Sin(i * 0.11)) + (4 * Math.Sin(i * 0.37)) + (i * 0.05);
                        var high = Math.Max(open, close) + 0.8 + (0.6 * Math.Abs(Math.Sin(i * 1.7)));
                        var low = Math.Min(open, close) - 0.8 - (0.5 * Math.Abs(Math.Cos(i * 1.3)));
                        data.Append(day.Ticks, open, high, low, close, 1000 + (600 * Math.Abs(Math.Sin(i * 0.9))));
                        i++;
                    }

                    return data;
                }

            """)
    { MinimumCount = 1 };

    // A test plan from Monday, 2 March 2026: up to three stations, each with steps one after the other.
    private static SampleInfo Plan() => new(
        "plan",
        "Test plan",
        DataShape.Gantt,
        3,
        count =>
        {
            var data = new GanttData();
            var start = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;
            for (var station = 0; station < Math.Min(count, 3); station++)
            {
                var previous = -1;
                for (var step = 0; step < 4; step++)
                {
                    var from = (station * 2.5) + (step * 2.0);
                    var row = data.AddRow("Step " + (step + 1), "Station " + (station + 1));
                    var task = data.Add(new GanttTask(row, start + (long)(from * TimeSpan.TicksPerDay), start + (long)((from + 1.5 + (step % 2)) * TimeSpan.TicksPerDay), "Step " + (step + 1)));
                    if (previous >= 0)
                    {
                        data.Link(previous, task);
                    }

                    previous = task;
                }
            }

            return data;
        },
        "GanttData",
        method => SampleCatalog.Header("a test plan", method, "GanttData", "The number of stations, at most three.") + """
                {
                    var data = new GanttData();
                    var start = new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc).Ticks;
                    for (var station = 0; station < Math.Min(count, 3); station++)
                    {
                        var previous = -1;
                        for (var step = 0; step < 4; step++)
                        {
                            var from = (station * 2.5) + (step * 2.0);
                            var row = data.AddRow("Step " + (step + 1), "Station " + (station + 1));
                            var task = data.Add(new GanttTask(row, start + (long)(from * TimeSpan.TicksPerDay), start + (long)((from + 1.5 + (step % 2)) * TimeSpan.TicksPerDay), "Step " + (step + 1)));
                            if (previous >= 0)
                            {
                                data.Link(previous, task);
                            }

                            previous = task;
                        }
                    }

                    return data;
                }

            """)
    { MinimumCount = 1 };
}
