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
            new("examples", "Example drawings", ParameterKind.Flag, false) { Description = "A Fibonacci retracement, a channel, a pitchfork and an XABCD pattern on the prices, to start from; with 156 days or more." },
            new("patterns", "Example patterns", ParameterKind.Flag, false) { Description = "An Elliott wave, a Fibonacci extension, a head and shoulders and a brush stroke on the prices; with 157 days or more." },
            new("rising", "Rising color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." },
            new("falling", "Falling color", ParameterKind.Color, string.Empty) { Description = "Empty for the color of the theme." },
            new("hollow", "Rising hollow", ParameterKind.Flag, false),
            new("volumeShare", "Height of the volume", ParameterKind.Number, 0.3) { Minimum = 0.05, Maximum = 0.9, Description = "The share of the chart the volume takes below." },
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
        [
            new("barHeight", "Height of the bars", ParameterKind.Number, 0.6) { Minimum = 0.05, Maximum = 1 },
            new("collapsed", "Collapsed groups", ParameterKind.Text, string.Empty) { Description = "Names of groups that start collapsed, separated by commas." },
        ],
        (chart, series, data, _) =>
        {
            foreach (var group in ParameterInfo.Split(series.Parameters.GetText("collapsed", string.Empty)))
            {
                ((GanttData)data).SetCollapsed(group, true);
            }

            chart.XAxis(axis => axis.DateTime("time").AutoRange())
                .Gantt((GanttData)data, gantt => (gantt.Name, gantt.BarHeight) = (series.Name, series.Parameters.GetNumber("barHeight", 0.6)));
        },
        (series, data, _) => $".XAxis(a => a.DateTime(\"time\").AutoRange()).Gantt({data}, g => {{ (g.Name, g.BarHeight) = ({CodeText.String(series.Name)}, {CodeText.Number(series.Parameters.GetNumber("barHeight", 0.6))}); {string.Concat(ParameterInfo.Split(series.Parameters.GetText("collapsed", string.Empty)).Select(group => $"{data}.SetCollapsed({CodeText.String(group)}, true); "))}}})")
    {
        Usings = ["Tachyon.Charts.Core.Series"],
        OwnsAxes = true,
        TakesInteractions = true,
    };

    // The colors and hollow candles in the code, empty when the theme decides.
    private static string Look(SpecValues values)
    {
        var look = string.Empty;
        if (SpecColor.TryParse(values.GetText("rising", string.Empty), out var rising))
        {
            look += $"s.Rising = new CandleLook({CodeText.Color(rising)}, {CodeText.Color(rising)}); ";
        }

        if (SpecColor.TryParse(values.GetText("falling", string.Empty), out var falling))
        {
            look += $"s.Falling = new CandleLook({CodeText.Color(falling)}, {CodeText.Color(falling)}); ";
        }

        return values.GetFlag("hollow", false) ? look + "s.Rising = s.Rising with { IsHollow = true };" : look.TrimEnd();
    }

    // Drawings to start from, at fixed days of the prices: a Fibonacci retracement, a channel, a pitchfork and an XABCD pattern.
    private static void ExampleDrawings(CandleData prices, DrawingLayer layer)
    {
        if (prices.Count < 156)
        {
            return;
        }

        DrawingPoint At(int day, bool high) => new(prices[day].Time, high ? prices[day].High : prices[day].Low);
        layer.Add(new Drawing(DrawingKind.FibonacciRetracement, [At(10, false), At(45, true)]));
        layer.Add(new Drawing(DrawingKind.TrendChannel, [At(60, false), At(95, false), At(75, true)]) { Color = new(76, 201, 240, 255) });
        layer.Add(new Drawing(DrawingKind.Pitchfork, [At(100, false), At(112, true), At(122, false)]) { Color = new(6, 214, 160, 255) });
        layer.Add(new Drawing(DrawingKind.Xabcd, [At(125, true), At(132, false), At(139, true), At(146, false), At(155, true)]) { Color = new(247, 37, 133, 255) });
    }

    // Drawings of patterns, at fixed days of the prices: an Elliott wave, a Fibonacci extension, a head and shoulders and a brush stroke.
    private static void ExamplePatterns(CandleData prices, DrawingLayer layer)
    {
        if (prices.Count < 157)
        {
            return;
        }

        DrawingPoint At(int day, bool high) => new(prices[day].Time, high ? prices[day].High : prices[day].Low);
        layer.Add(new Drawing(DrawingKind.ElliottWave, [.. new[] { 2, 10, 16, 28, 34, 44 }.Select((day, i) => At(day, i % 2 != 0))]) { Color = new(76, 201, 240, 255) });
        layer.Add(new Drawing(DrawingKind.FibonacciExtension, [At(50, false), At(62, true), At(70, false)]));
        layer.Add(new Drawing(DrawingKind.HeadAndShoulders, [.. new[] { 85, 92, 98, 106, 114, 121, 128 }.Select((day, i) => At(day, i % 2 != 0))]) { Color = new(247, 37, 133, 255) });
        layer.Add(new Drawing(DrawingKind.Brush, [.. Enumerable.Range(135, 22).Select(day => new DrawingPoint(prices[day].Time, prices[day].High + 1.5 + Math.Sin(day * 0.6)))]) { Color = new(6, 214, 160, 255) });
    }

    private static string ExampleDrawingsCode(string data, bool examples, bool patterns) =>
        $".Drawings(layer => {{ DrawingPoint At(int day, bool high) => new({data}[day].Time, high ? {data}[day].High : {data}[day].Low); "
        + (examples
            ? $"if ({data}.Count >= 156) {{ "
                + "layer.Add(new Drawing(DrawingKind.FibonacciRetracement, [At(10, false), At(45, true)])); "
                + "layer.Add(new Drawing(DrawingKind.TrendChannel, [At(60, false), At(95, false), At(75, true)]) { Color = new(76, 201, 240, 255) }); "
                + "layer.Add(new Drawing(DrawingKind.Pitchfork, [At(100, false), At(112, true), At(122, false)]) { Color = new(6, 214, 160, 255) }); "
                + "layer.Add(new Drawing(DrawingKind.Xabcd, [At(125, true), At(132, false), At(139, true), At(146, false), At(155, true)]) { Color = new(247, 37, 133, 255) }); } "
            : string.Empty)
        + (patterns
            ? $"if ({data}.Count >= 157) {{ "
                + "layer.Add(new Drawing(DrawingKind.ElliottWave, [.. new[] { 2, 10, 16, 28, 34, 44 }.Select((day, i) => At(day, i % 2 != 0))]) { Color = new(76, 201, 240, 255) }); "
                + "layer.Add(new Drawing(DrawingKind.FibonacciExtension, [At(50, false), At(62, true), At(70, false)])); "
                + "layer.Add(new Drawing(DrawingKind.HeadAndShoulders, [.. new[] { 85, 92, 98, 106, 114, 121, 128 }.Select((day, i) => At(day, i % 2 != 0))]) { Color = new(247, 37, 133, 255) }); "
                + $"layer.Add(new Drawing(DrawingKind.Brush, [.. Enumerable.Range(135, 22).Select(day => new DrawingPoint({data}[day].Time, {data}[day].High + 1.5 + Math.Sin(day * 0.6)))]) {{ Color = new(6, 214, 160, 255) }}); }} "
            : string.Empty)
        + "})";

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
                if (SpecColor.TryParse(values.GetText("rising", string.Empty), out var rising))
                {
                    candles.Rising = new CandleLook(rising, rising);
                }

                if (SpecColor.TryParse(values.GetText("falling", string.Empty), out var falling))
                {
                    candles.Falling = new CandleLook(falling, falling);
                }

                if (values.GetFlag("hollow", false))
                {
                    candles.Rising = candles.Rising with { IsHollow = true };
                }
            },
            axes.Y);
        if (values.GetFlag("volume", true))
        {
            var share = values.GetNumber("volumeShare", 0.3);
            chart.YAxis(axis => axis.Numeric("volume").Title("Volume").AutoRange().Pane(1, share)).Volume(prices, "volume");
        }

        if (values.GetFlag("profile", false))
        {
            var levels = (int)values.GetNumber("levels", 40);
            chart.VolumeProfile(prices, profile => profile.Levels = levels, axes.Y);
        }

        var (examples, patterns) = (values.GetFlag("examples", false), values.GetFlag("patterns", false));
        if (values.GetFlag("drawings", false) || examples || patterns)
        {
            chart.Drawings(examples || patterns
                ? layer =>
                {
                    if (examples)
                    {
                        ExampleDrawings(prices, layer);
                    }

                    if (patterns)
                    {
                        ExamplePatterns(prices, layer);
                    }
                }
            : null);
        }
    }

    private static string CandlesCode(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var look = Look(values);
        var code = look.Length == 0
            ? $".Candles({data}, s => (s.Name, s.Style, s.ShowsValues) = ({CodeText.String(series.Name)}, CandleStyle.{values.GetText("style", nameof(CandleStyle.Candles))}, {CodeText.Flag(values.GetFlag("values", false))}), {CodeText.String(axes.Y)})"
            : $".Candles({data}, s => {{ (s.Name, s.Style, s.ShowsValues) = ({CodeText.String(series.Name)}, CandleStyle.{values.GetText("style", nameof(CandleStyle.Candles))}, {CodeText.Flag(values.GetFlag("values", false))}); {look} }}, {CodeText.String(axes.Y)})";
        if (values.GetFlag("volume", true))
        {
            code += $".YAxis(a => a.Numeric(\"volume\").Title(\"Volume\").AutoRange().Pane(1, {CodeText.Number(values.GetNumber("volumeShare", 0.3))})).Volume({data}, \"volume\")";
        }

        if (values.GetFlag("profile", false))
        {
            code += $".VolumeProfile({data}, p => p.Levels = {(int)values.GetNumber("levels", 40)}, {CodeText.String(axes.Y)})";
        }

        var (examples, patterns) = (values.GetFlag("examples", false), values.GetFlag("patterns", false));
        return code + (examples || patterns ? ExampleDrawingsCode(data, examples, patterns) : values.GetFlag("drawings", false) ? ".Drawings()" : string.Empty);
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
