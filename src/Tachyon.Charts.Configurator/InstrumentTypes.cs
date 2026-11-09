using System.Numerics;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The instruments: oscilloscope, spectrum analyzer, network analyzer and the sweeping monitor. Their controls are
/// parameters; the code sets them on the display before the chart describes it.
/// </summary>
internal static class InstrumentTypes
{
    private static readonly double[] _maskFrequencies = [0, 3e6, 3.2e6, 1e7];
    private static readonly double[] _maskLevels = [-5, -5, -25, -25];

    public static IReadOnlyList<ChartTypeInfo> Create() => [Oscilloscope(), Spectrum(), Network(), Monitor()];

    public static IReadOnlyList<SampleInfo> Samples() => [ScopeSample(), SpectrumSample(), NetworkSample(), MonitorSample()];

    private static readonly string[] _signals = ["Sine", "Square", "Burst", "Triangle"];

    // Any number of channels: one signal, scale and position per channel, given as lists; the code adds them one by one.
    private static ChartTypeInfo Oscilloscope() => new(
        "oscilloscope",
        "Oscilloscope",
        ChartCategory.Instrument,
        DataShape.Scope,
        "scope",
        [
            new("channels", "Number of channels", ParameterKind.Integer, 2.0) { Minimum = 1, Maximum = 8 },
            new("signals", "Signal per channel", ParameterKind.Text, "Sine, Square") { Description = "Sine, Square, Burst or Triangle, separated by commas; missing ones repeat the list." },
            new("volts", "Volts per division per channel", ParameterKind.Text, "1, 0.5") { Description = "Separated by commas; a missing one takes the last." },
            new("positions", "Position per channel in divisions", ParameterKind.Text, "2, -2") { Description = "From -4 to 4, separated by commas; without any the channels spread over the screen." },
            new("timePerDivision", "Time per division in s", ParameterKind.Number, 5e-4) { Minimum = 1e-12 },
            new("timeOffset", "Time offset in s", ParameterKind.Number, 0.0),
            new("trigger", "Trigger level in V", ParameterKind.Number, 0.5),
            new("persistence", "Persistence", ParameterKind.Choice, nameof(ScopePersistence.Off)) { Choices = Enum.GetNames<ScopePersistence>() },
        ],
        (chart, series, data, _) =>
        {
            var ((time, waves), values) = (((double[], float[][]))data, series.Parameters);
            var scope = new ScopeDisplay
            {
                TimePerDivision = values.GetNumber("timePerDivision", 5e-4),
                TimeOffset = values.GetNumber("timeOffset", 0),
                TriggerLevel = values.GetNumber("trigger", 0.5),
                Persistence = Enum.Parse<ScopePersistence>(values.GetText("persistence", nameof(ScopePersistence.Off))),
            };
            foreach (var (signal, volts, position) in Channels(values))
            {
                var channel = scope.AddChannel();
                (channel.VoltsPerDivision, channel.Position) = (volts, position);
                channel.SetWaveform(time, waves[signal]);
            }

            chart.Oscilloscope(scope);
        },
        (_, data, _) => $".Oscilloscope({data}Display)")
    {
        Usings = ["Tachyon.Charts.Core.Instruments"],
        OwnsAxes = true,
        PrepareCode = (series, data) =>
        {
            var values = series.Parameters;
            var lines = new List<string>
            {
                $"var {data}Display = new ScopeDisplay {{ TimePerDivision = {CodeText.Number(values.GetNumber("timePerDivision", 5e-4))}, TimeOffset = {CodeText.Number(values.GetNumber("timeOffset", 0))}, TriggerLevel = {CodeText.Number(values.GetNumber("trigger", 0.5))}, Persistence = ScopePersistence.{values.GetText("persistence", nameof(ScopePersistence.Off))} }};",
                $"foreach (var (signal, volts, position) in new (int Signal, double Volts, double Position)[] {{ {string.Join(", ", Channels(values).Select(channel => $"({channel.Signal}, {CodeText.Number(channel.Volts)}, {CodeText.Number(channel.Position)})"))} }})",
                "{",
                $"    var channel = {data}Display.AddChannel();",
                "    (channel.VoltsPerDivision, channel.Position) = (volts, position);",
                $"    channel.SetWaveform({data}.Time, {data}.Waves[signal]);",
                "}",
            };
            return lines;
        },
    };

    // The channels as the lists give them: missing signals repeat the list, a missing scale takes the last, missing
    // positions spread the channels evenly from top to bottom.
    private static List<(int Signal, double Volts, double Position)> Channels(SpecValues values)
    {
        var count = (int)values.GetNumber("channels", 2);
        var signals = values.GetText("signals", "Sine, Square").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(name => Array.FindIndex(_signals, known => known.Equals(name, StringComparison.OrdinalIgnoreCase))).Where(index => index >= 0).ToArray();
        var volts = Numbers(values.GetText("volts", "1, 0.5"));
        var positions = Numbers(values.GetText("positions", "2, -2"));
        var channels = new List<(int, double, double)>(count);
        for (var c = 0; c < count; c++)
        {
            var signal = signals.Length > 0 ? signals[c % signals.Length] : c % _signals.Length;
            var scale = volts.Length > 0 ? volts[Math.Min(c, volts.Length - 1)] : 1;
            var position = c < positions.Length ? Math.Clamp(positions[c], -4, 4) : count == 1 ? 0 : 3 - (c * 6.0 / (count - 1));
            channels.Add((signal, scale > 0 ? scale : 1, position));
        }

        return channels;
    }

    private static double[] Numbers(string text) =>
        [.. text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : double.NaN)
            .Where(double.IsFinite)];

    private static ChartTypeInfo Spectrum() => new(
        "spectrum",
        "Spectrum analyzer",
        ChartCategory.Instrument,
        DataShape.Spectrum,
        "spectrum",
        [
            new("reference", "Reference level in dBm", ParameterKind.Number, 0.0),
            new("perDivision", "dB per division", ParameterKind.Number, 10.0) { Minimum = 0.01 },
            new("maxHold", "Max hold", ParameterKind.Flag, false),
            new("density", "Density", ParameterKind.Flag, true),
            new("mask", "Limit mask", ParameterKind.Flag, true),
            new("marker", "Marker in Hz", ParameterKind.Number, 2e6) { Description = "A negative value for none." },
            new("history", "History below", ParameterKind.Flag, false) { Description = "The spectra over time as a waterfall below the trace." },
        ],
        (chart, series, data, _) =>
        {
            if (series.Parameters.GetFlag("history", false))
            {
                chart.SpectrumHistory((SpectrumDisplay)data);
            }
            else
            {
                chart.Spectrum((SpectrumDisplay)data);
            }
        },
        (series, data, _) => series.Parameters.GetFlag("history", false) ? $".SpectrumHistory({data})" : $".Spectrum({data})")
    {
        Usings = ["Tachyon.Charts.Core.Instruments"],
        OwnsAxes = true,
        Prepare = (series, data) =>
        {
            var (spectrum, values) = ((SpectrumDisplay)data, series.Parameters);
            spectrum.SetView(spectrum.Start, spectrum.Stop, values.GetNumber("reference", 0), values.GetNumber("perDivision", 10));
            spectrum.ShowsMaxHold = values.GetFlag("maxHold", false);
            spectrum.ShowsDensity = values.GetFlag("density", true);
            spectrum.Mask = values.GetFlag("mask", true) ? new FrequencyMask(_maskFrequencies, _maskLevels) : null;
            spectrum.MarkerFrequency = values.GetNumber("marker", 2e6) >= 0 ? values.GetNumber("marker", 2e6) : null;
        },
        PrepareCode = (series, data) =>
        {
            var values = series.Parameters;
            var lines = new List<string>
            {
                $"{data}.SetView({data}.Start, {data}.Stop, {CodeText.Number(values.GetNumber("reference", 0))}, {CodeText.Number(values.GetNumber("perDivision", 10))});",
                $"{data}.ShowsMaxHold = {CodeText.Flag(values.GetFlag("maxHold", false))};",
                $"{data}.ShowsDensity = {CodeText.Flag(values.GetFlag("density", true))};",
                values.GetFlag("mask", true)
                    ? $"{data}.Mask = new FrequencyMask([{string.Join(", ", _maskFrequencies.Select(CodeText.Number))}], [{string.Join(", ", _maskLevels.Select(CodeText.Number))}]);"
                    : $"{data}.Mask = null;",
            };
            lines.Add(values.GetNumber("marker", 2e6) >= 0 ? $"{data}.MarkerFrequency = {CodeText.Number(values.GetNumber("marker", 2e6))};" : $"{data}.MarkerFrequency = null;");

            return lines;
        },
    };

    private static ChartTypeInfo Network() => new(
        "network",
        "Network analyzer",
        ChartCategory.Instrument,
        DataShape.Network,
        "filter",
        [
            new("minimum", "Lowest level in dB", ParameterKind.Number, -70.0),
            new("maximum", "Highest level in dB", ParameterKind.Number, 5.0),
            new("limits", "Limit lines", ParameterKind.Flag, true),
            new("marker", "Marker in Hz", ParameterKind.Number, 1.5e9) { Description = "A negative value for none." },
        ],
        (chart, series, data, _) =>
        {
            var network = (NetworkDisplay)data;
            chart.NetworkAnalyzer(network, 1e9, 2e9, series.Parameters.GetNumber("minimum", -70), series.Parameters.GetNumber("maximum", 5))
                .Modifiers(m => m.ZoomPan().Custom(() => new NetworkMarkerModifier(network)));
        },
        (series, data, _) => $".NetworkAnalyzer({data}, 1E9, 2E9, {CodeText.Number(series.Parameters.GetNumber("minimum", -70))}, {CodeText.Number(series.Parameters.GetNumber("maximum", 5))}).Modifiers(m => m.ZoomPan().Custom(() => new NetworkMarkerModifier({data})))")
    {
        Usings = ["Tachyon.Charts.Core.Instruments", "System.Numerics"],
        OwnsAxes = true,
        Prepare = (series, data) =>
        {
            var (network, values) = ((NetworkDisplay)data, series.Parameters);
            if (values.GetFlag("limits", true))
            {
                network.Traces[0].AddLimit(new LimitLine(LimitKind.Minimum, 1.45e9, -1, 1.55e9, -1));
                network.Traces[0].AddLimit(new LimitLine(LimitKind.Maximum, 1e9, -20, 1.15e9, -20));
            }

            if (values.GetNumber("marker", 1.5e9) >= 0)
            {
                network.AddMarker(values.GetNumber("marker", 1.5e9));
            }
        },
        PrepareCode = (series, data) =>
        {
            var values = series.Parameters;
            var lines = new List<string>();
            if (values.GetFlag("limits", true))
            {
                lines.Add($"{data}.Traces[0].AddLimit(new LimitLine(LimitKind.Minimum, 1.45E9, -1, 1.55E9, -1));");
                lines.Add($"{data}.Traces[0].AddLimit(new LimitLine(LimitKind.Maximum, 1E9, -20, 1.15E9, -20));");
            }

            if (values.GetNumber("marker", 1.5e9) >= 0)
            {
                lines.Add($"{data}.AddMarker({CodeText.Number(values.GetNumber("marker", 1.5e9))});");
            }

            return lines;
        },
    };

    private static ChartTypeInfo Monitor() => new(
        "monitor",
        "Sweep monitor",
        ChartCategory.Instrument,
        DataShape.Sweep,
        "channels",
        [
            new("mode", "Mode", ParameterKind.Choice, nameof(SweepMode.Sweep)) { Choices = Enum.GetNames<SweepMode>() },
            new("gap", "Gap as share of the width", ParameterKind.Number, 0.04) { Minimum = 0, Maximum = 0.5 },
            new("names", "Names of the channels", ParameterKind.Text, "ECG, Pleth, Resp") { Description = "One per channel, separated by commas." },
        ],
        (chart, series, data, _) => chart.Sweep((SweepData)data, [new DoubleRange(-1.5, 1.5), new DoubleRange(-1.5, 1.5), new DoubleRange(-1.5, 1.5)], Names(series)),
        (series, data, _) => $".Sweep({data}, [new DoubleRange(-1.5, 1.5), new DoubleRange(-1.5, 1.5), new DoubleRange(-1.5, 1.5)], [{string.Join(", ", Names(series).Select(CodeText.String))}])")
    {
        Usings = ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Numerics", "Tachyon.Charts.Core.Series"],
        OwnsAxes = true,
        Prepare = (series, data) =>
        {
            var monitor = (SweepData)data;
            monitor.Mode = Enum.Parse<SweepMode>(series.Parameters.GetText("mode", nameof(SweepMode.Sweep)));
            monitor.GapShare = series.Parameters.GetNumber("gap", 0.04);
        },
        PrepareCode = (series, data) => [$"{data}.Mode = SweepMode.{series.Parameters.GetText("mode", nameof(SweepMode.Sweep))};", $"{data}.GapShare = {CodeText.Number(series.Parameters.GetNumber("gap", 0.04))};"],
    };

    private static string[] Names(SeriesSpec series) =>
        series.Parameters.GetText("names", string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // Four signals of 1 kHz over 10 ms around the trigger: sine, square, a decaying burst and a triangle.
    private static SampleInfo ScopeSample()
    {
        const string Type = "(double[] Time, float[][] Waves)";
        return new(
            "scope",
            "Sine, square, burst and triangle",
            DataShape.Scope,
            5000,
            count =>
            {
                var (time, waves) = (new double[count], new float[4][]);
                for (var k = 0; k < 4; k++)
                {
                    waves[k] = new float[count];
                }

                for (var i = 0; i < count; i++)
                {
                    time[i] = (i * 0.01 / count) - 0.005;
                    var angle = 2 * Math.PI * 1000 * time[i];
                    waves[0][i] = (float)(1.5 * Math.Sin(angle));
                    waves[1][i] = Math.Sin(angle) >= 0 ? 1 : -1;
                    waves[2][i] = (float)(2 * Math.Exp(-Math.Abs((time[i] + 0.01) * 1000 % 1 * 6)) * Math.Cos(angle * 12));
                    waves[3][i] = (float)(2 / Math.PI * Math.Asin(Math.Sin(angle)));
                }

                return (time, waves);
            },
            Type,
            method => SampleCatalog.Header("a sine, a square wave, a burst and a triangle", method, Type, "The number of samples per signal.") + """
                    {
                        var (time, waves) = (new double[count], new float[4][]);
                        for (var k = 0; k < 4; k++)
                        {
                            waves[k] = new float[count];
                        }

                        for (var i = 0; i < count; i++)
                        {
                            time[i] = (i * 0.01 / count) - 0.005;
                            var angle = 2 * Math.PI * 1000 * time[i];
                            waves[0][i] = (float)(1.5 * Math.Sin(angle));
                            waves[1][i] = Math.Sin(angle) >= 0 ? 1 : -1;
                            waves[2][i] = (float)(2 * Math.Exp(-Math.Abs((time[i] + 0.01) * 1000 % 1 * 6)) * Math.Cos(angle * 12));
                            waves[3][i] = (float)(2 / Math.PI * Math.Asin(Math.Sin(angle)));
                        }

                        return (time, waves);
                    }

                """)
        { MinimumCount = 2 };
    }

    // Noise around -67 dBm and three carriers from 0 to 10 MHz.
    private static SampleInfo SpectrumSample() => new(
        "spectrum",
        "Three carriers",
        DataShape.Spectrum,
        2001,
        count =>
        {
            var display = new SpectrumDisplay();
            display.SetView(0, 1e7, 0);
            var (frequencies, levels) = (new double[count], new float[count]);
            for (var i = 0; i < count; i++)
            {
                frequencies[i] = i * 1e7 / (count - 1);
                var level = -67 + (3 * Math.Sin(i * 1.37));
                foreach (var (center, height) in new[] { (2e6, -12.0), (4.5e6, -30.0), (7e6, -22.0) })
                {
                    var distance = (frequencies[i] - center) / 8e4;
                    level = Math.Max(level, height - (distance * distance * 6));
                }

                levels[i] = (float)level;
            }

            display.SetTrace(frequencies, levels);
            return display;
        },
        "SpectrumDisplay",
        method => SampleCatalog.Header("three carriers", method, "SpectrumDisplay", "The number of frequencies.") + """
                {
                    var display = new SpectrumDisplay();
                    display.SetView(0, 1e7, 0);
                    var (frequencies, levels) = (new double[count], new float[count]);
                    for (var i = 0; i < count; i++)
                    {
                        frequencies[i] = i * 1e7 / (count - 1);
                        var level = -67 + (3 * Math.Sin(i * 1.37));
                        foreach (var (center, height) in new[] { (2e6, -12.0), (4.5e6, -30.0), (7e6, -22.0) })
                        {
                            var distance = (frequencies[i] - center) / 8e4;
                            level = Math.Max(level, height - (distance * distance * 6));
                        }

                        levels[i] = (float)level;
                    }

                    display.SetTrace(frequencies, levels);
                    return display;
                }

            """)
    { MinimumCount = 2 };

    // A band pass around 1.5 GHz: transmission S21 and reflection S11 from 1 to 2 GHz.
    private static SampleInfo NetworkSample() => new(
        "filter",
        "Band pass filter",
        DataShape.Network,
        801,
        count =>
        {
            var display = new NetworkDisplay();
            var (transmission, reflection) = (display.AddTrace("S21"), display.AddTrace("S11"));
            var (frequencies, through, back) = (new double[count], new Complex[count], new Complex[count]);
            for (var i = 0; i < count; i++)
            {
                frequencies[i] = 1e9 + (i * 1e9 / (count - 1));
                var detuning = (frequencies[i] - 1.5e9) / 1.2e8;
                var response = 1 / (1 + Math.Pow(detuning, 6));
                (through[i], back[i]) = (Math.Max(Math.Sqrt(response), 1e-4), Math.Max(Math.Sqrt(1 - response), 0.03));
            }

            transmission.SetSweep(frequencies, through);
            reflection.SetSweep(frequencies, back);
            return display;
        },
        "NetworkDisplay",
        method => SampleCatalog.Header("a band pass filter", method, "NetworkDisplay", "The number of frequencies.") + """
                {
                    var display = new NetworkDisplay();
                    var (transmission, reflection) = (display.AddTrace("S21"), display.AddTrace("S11"));
                    var (frequencies, through, back) = (new double[count], new Complex[count], new Complex[count]);
                    for (var i = 0; i < count; i++)
                    {
                        frequencies[i] = 1e9 + (i * 1e9 / (count - 1));
                        var detuning = (frequencies[i] - 1.5e9) / 1.2e8;
                        var response = 1 / (1 + Math.Pow(detuning, 6));
                        (through[i], back[i]) = (Math.Max(Math.Sqrt(response), 1e-4), Math.Max(Math.Sqrt(1 - response), 0.03));
                    }

                    transmission.SetSweep(frequencies, through);
                    reflection.SetSweep(frequencies, back);
                    return display;
                }

            """)
    { MinimumCount = 2 };

    // Three channels over a window of 4 s, written up to 3 s: a heartbeat, a pulse wave and breathing.
    private static SampleInfo MonitorSample() => new(
        "channels",
        "Heartbeat, pulse and breathing",
        DataShape.Sweep,
        3000,
        count =>
        {
            var data = new SweepData(3, count, 4);
            var (times, values) = (new double[count], new float[count * 3]);
            for (var i = 0; i < count; i++)
            {
                var t = times[i] = i * 3.0 / count;
                var beat = (t * 1.2) % 1;
                values[i * 3] = (float)((Math.Exp(-((beat - 0.3) * (beat - 0.3)) / 0.0004) * 1.2) - (0.2 * Math.Exp(-((beat - 0.35) * (beat - 0.35)) / 0.001)));
                values[(i * 3) + 1] = (float)(Math.Sin(Math.PI * Math.Min(beat / 0.4, 1)) * 0.8);
                values[(i * 3) + 2] = (float)Math.Sin(2 * Math.PI * t / 4);
            }

            data.Append(times, values);
            return data;
        },
        "SweepData",
        method => SampleCatalog.Header("a heartbeat, a pulse wave and breathing", method, "SweepData", "The number of samples per channel.") + """
                {
                    var data = new SweepData(3, count, 4);
                    var (times, values) = (new double[count], new float[count * 3]);
                    for (var i = 0; i < count; i++)
                    {
                        var t = times[i] = i * 3.0 / count;
                        var beat = (t * 1.2) % 1;
                        values[i * 3] = (float)((Math.Exp(-((beat - 0.3) * (beat - 0.3)) / 0.0004) * 1.2) - (0.2 * Math.Exp(-((beat - 0.35) * (beat - 0.35)) / 0.001)));
                        values[(i * 3) + 1] = (float)(Math.Sin(Math.PI * Math.Min(beat / 0.4, 1)) * 0.8);
                        values[(i * 3) + 2] = (float)Math.Sin(2 * Math.PI * t / 4);
                    }

                    data.Append(times, values);
                    return data;
                }

            """)
    { MinimumCount = 2 };
}
