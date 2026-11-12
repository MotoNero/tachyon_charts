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

    public static IReadOnlyList<ChartTypeInfo> Create() => [Oscilloscope(), Spectrum(), Network(), Monitor()];

    public static IReadOnlyList<SampleInfo> Samples() => [ScopeSample(), SpectrumSample(), CarriersSample(), DriftSample(), NetworkSample(), MonitorSample(), PatientSample()];

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
            new("live", "Live acquisitions", ParameterKind.Flag, false) { Description = "New acquisitions keep coming, each at a random phase as from a free-running signal; the trigger holds the picture still." },
            new("rate", "Acquisitions per second", ParameterKind.Number, 30.0) { Minimum = 1, Maximum = 200 },
            new("noise", "Noise in V", ParameterKind.Number, 0.0) { Minimum = 0, Description = "With live acquisitions: noise on the signals, which also lets their amplitude vary." },
        ],
        (chart, _, data, _) => chart.Oscilloscope((ScopeDisplay)data),
        (_, data, _) => $".Oscilloscope({data}Display)")
    {
        Usings = ["Tachyon.Charts.Core.Instruments"],
        OwnsAxes = true,
        Make = (series, data) =>
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

            return scope;
        },
        Start = (series, data) => series.Parameters.GetFlag("live", false)
            ? ScopeFeed.Start((ScopeDisplay)data, [.. Channels(series.Parameters).Select(channel => channel.Signal)], series.Parameters.GetNumber("noise", 0), series.Parameters.GetNumber("rate", 30))
            : null,
        Helpers = ScopeFeed.Code,
        Region = (series, _) => (new ScopeDisplay { TimePerDivision = series.Parameters.GetNumber("timePerDivision", 5e-4), TimeOffset = series.Parameters.GetNumber("timeOffset", 0) }.GetTimeRange(), Level(Channels(series.Parameters)[0])),
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
            if (values.GetFlag("live", false))
            {
                lines.Add($"StartAcquisitions({data}Display, [{string.Join(", ", Channels(values).Select(channel => channel.Signal.ToString(System.Globalization.CultureInfo.InvariantCulture)))}], {CodeText.Number(values.GetNumber("noise", 0))}, {CodeText.Number(values.GetNumber("rate", 30))});");
            }

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

    // The levels a channel shows: its divisions above and below the middle, moved by its position.
    private static DoubleRange Level((int Signal, double Volts, double Position) channel)
    {
        var half = ScopeDisplay.VerticalDivisions / 2.0;
        return new DoubleRange((-half - channel.Position) * channel.Volts, (half - channel.Position) * channel.Volts);
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
            .. SpectrumSettings(),
            new("history", "History only", ParameterKind.Flag, false) { Description = "The spectra over time as a waterfall instead of the trace." },
            new("historyBelow", "History below the trace", ParameterKind.Flag, false) { Description = "A second chart below the trace with the spectra over time, both from the same measurement." },
            new("live", "Live spectra", ParameterKind.Flag, false) { Description = "New spectra keep coming, as from a measurement." },
            new("rate", "Spectra per second", ParameterKind.Number, 30.0) { Minimum = 1, Maximum = 200 },
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
        Region = (_, data) => SpectrumRegion((SpectrumDisplay)data),
        Prepare = (series, data) => PrepareSpectrum(series.Parameters, (SpectrumDisplay)data),
        PrepareCode = (series, data) =>
        [
            .. PrepareSpectrumCode(series.Parameters, data),
            .. series.Parameters.GetFlag("live", false)
                ? [series.Data.Sample == "drift" ? $"StartFeed({data}, {CodeText.Number(series.Parameters.GetNumber("rate", 30))});" : $"StartSpectra({data}, {LiveFrequencies(series).ToString(System.Globalization.CultureInfo.InvariantCulture)}, {CodeText.Number(series.Parameters.GetNumber("rate", 30))});"]
                : Array.Empty<string>(),
        ],
        Views = series => series.Parameters.GetFlag("historyBelow", false)
            ?
            [
                new ViewInfo("Spectrum", (chart, _, data) => chart.Spectrum((SpectrumDisplay)data), (_, data) => $"chart.Spectrum({data});"),
                new ViewInfo("History", (chart, _, data) => chart.SpectrumHistory((SpectrumDisplay)data), (_, data) => $"chart.SpectrumHistory({data});"),
            ]
            : [],
        Columns = _ => 1,
        Start = (series, data) => !series.Parameters.GetFlag("live", false) ? null
            : series.Data.Sample == "drift" ? SpectrumFeed.Start((SpectrumDisplay)data, series.Parameters.GetNumber("rate", 30))
            : CarrierFeed.Start((SpectrumDisplay)data, LiveFrequencies(series), series.Parameters.GetNumber("rate", 30)),
        Helpers = SpectrumFeed.StartCode + CarrierFeed.StartCode,
    };

    // The frequencies of live spectra: as many as the sample has.
    private static int LiveFrequencies(SeriesSpec series) => Math.Max(16, series.Data.Count);

    /// <summary>Gets the settings of a spectrum analyzer that other types with a spectrum share.</summary>
    /// <returns>The parameters.</returns>
    internal static ParameterInfo[] SpectrumSettings() =>
    [
        new("reference", "Reference level in dBm", ParameterKind.Number, 0.0),
        new("perDivision", "dB per division", ParameterKind.Number, 10.0) { Minimum = 0.01 },
        new("maxHold", "Max hold", ParameterKind.Flag, false),
        new("density", "Density", ParameterKind.Flag, true),
        new("mask", "Limit mask", ParameterKind.Flag, true),
        new("maskLevel", "Mask above 3.2 MHz in dBm", ParameterKind.Number, -25.0) { Description = "The limit of the mask beyond the carrier at 2 MHz; below 3 MHz it stays at -5 dBm." },
        new("marker", "Marker in Hz", ParameterKind.Number, 2e6) { Description = "A negative value for none." },
        new("axes", "Axes with numbers", ParameterKind.Flag, false) { Description = "The frequency below with its unit and the level in dBm on the left; without, the readings in the corners name them." },
    ];

    /// <summary>Gets the frequencies and levels a spectrum shows.</summary>
    /// <param name="spectrum">The spectrum.</param>
    /// <returns>The frequencies across and the levels from the bottom to the reference level.</returns>
    internal static (DoubleRange X, DoubleRange Y) SpectrumRegion(SpectrumDisplay spectrum) =>
        (new DoubleRange(spectrum.Start, spectrum.Stop), new DoubleRange(spectrum.ReferenceLevel - (ScopeDisplay.VerticalDivisions * spectrum.PerDivision), spectrum.ReferenceLevel));

    /// <summary>Sets a spectrum up from the settings of <see cref="SpectrumSettings"/>.</summary>
    /// <param name="values">The settings.</param>
    /// <param name="spectrum">The spectrum.</param>
    internal static void PrepareSpectrum(SpecValues values, SpectrumDisplay spectrum)
    {
        spectrum.SetView(spectrum.Start, spectrum.Stop, values.GetNumber("reference", 0), values.GetNumber("perDivision", 10));
        spectrum.ShowsMaxHold = values.GetFlag("maxHold", false);
        spectrum.ShowsDensity = values.GetFlag("density", true);
        spectrum.Mask = values.GetFlag("mask", true) ? new FrequencyMask(_maskFrequencies, MaskLevels(values)) : null;
        spectrum.MarkerFrequency = values.GetNumber("marker", 2e6) >= 0 ? values.GetNumber("marker", 2e6) : null;
        spectrum.ShowsAxes = values.GetFlag("axes", false);
    }

    private static double[] MaskLevels(SpecValues values)
    {
        var level = values.GetNumber("maskLevel", -25);
        return [-5, -5, level, level];
    }

    /// <summary>Writes <see cref="PrepareSpectrum"/> as C# statements.</summary>
    /// <param name="values">The settings.</param>
    /// <param name="data">The name of the spectrum.</param>
    /// <returns>The statements.</returns>
    internal static IEnumerable<string> PrepareSpectrumCode(SpecValues values, string data)
    {
        var lines = new List<string>
        {
            $"{data}.SetView({data}.Start, {data}.Stop, {CodeText.Number(values.GetNumber("reference", 0))}, {CodeText.Number(values.GetNumber("perDivision", 10))});",
            $"{data}.ShowsMaxHold = {CodeText.Flag(values.GetFlag("maxHold", false))};",
            $"{data}.ShowsDensity = {CodeText.Flag(values.GetFlag("density", true))};",
            values.GetFlag("mask", true)
                ? $"{data}.Mask = new FrequencyMask([{string.Join(", ", _maskFrequencies.Select(CodeText.Number))}], [{string.Join(", ", MaskLevels(values).Select(CodeText.Number))}]);"
                : $"{data}.Mask = null;",
        };
        lines.Add(values.GetNumber("marker", 2e6) >= 0 ? $"{data}.MarkerFrequency = {CodeText.Number(values.GetNumber("marker", 2e6))};" : $"{data}.MarkerFrequency = null;");
        if (values.GetFlag("axes", false))
        {
            lines.Add($"{data}.ShowsAxes = true;");
        }

        return lines;
    }

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
            new("failing", "Limits the filter fails", ParameterKind.Flag, false) { Description = "Stricter limits of the stop band and a wider one of the reflection, which the filter misses." },
            new("marker", "Marker in Hz", ParameterKind.Number, 1.5e9) { Description = "A negative value for none." },
            new("secondMarker", "Second marker in Hz", ParameterKind.Number, -1.0) { Description = "A negative value for none." },
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
        Region = (series, _) => (new DoubleRange(1e9, 2e9), new DoubleRange(series.Parameters.GetNumber("minimum", -70), series.Parameters.GetNumber("maximum", 5))),
        Prepare = (series, data) =>
        {
            var (network, values) = ((NetworkDisplay)data, series.Parameters);
            if (values.GetFlag("limits", true))
            {
                foreach (var (trace, limit) in Limits(values.GetFlag("failing", false)))
                {
                    network.Traces[trace].AddLimit(limit);
                }
            }

            foreach (var marker in (ReadOnlySpan<double>)[values.GetNumber("marker", 1.5e9), values.GetNumber("secondMarker", -1)])
            {
                if (marker >= 0)
                {
                    network.AddMarker(marker);
                }
            }
        },
        PrepareCode = (series, data) =>
        {
            var values = series.Parameters;
            var lines = new List<string>();
            if (values.GetFlag("limits", true))
            {
                lines.AddRange(Limits(values.GetFlag("failing", false)).Select(pair =>
                    $"{data}.Traces[{pair.Trace}].AddLimit(new LimitLine(LimitKind.{pair.Limit.Kind}, {CodeText.Number(pair.Limit.StartFrequency)}, {CodeText.Number(pair.Limit.StartLevel)}, {CodeText.Number(pair.Limit.StopFrequency)}, {CodeText.Number(pair.Limit.StopLevel)}));"));
            }

            foreach (var marker in new[] { values.GetNumber("marker", 1.5e9), values.GetNumber("secondMarker", -1) }.Where(marker => marker >= 0))
            {
                lines.Add($"{data}.AddMarker({CodeText.Number(marker)});");
            }

            return lines;
        },
    };

    // The limits of the band pass: the pass band, the stop band below it and the reflection in the pass band; the
    // failing ones are stricter in the stop band and wider in the reflection.
    private static (int Trace, LimitLine Limit)[] Limits(bool failing) =>
    [
        (0, new LimitLine(LimitKind.Minimum, 1.45e9, -1, 1.55e9, -1)),
        (0, failing ? new LimitLine(LimitKind.Maximum, 1e9, -40, 1.25e9, -40) : new LimitLine(LimitKind.Maximum, 1e9, -20, 1.15e9, -20)),
        (1, failing ? new LimitLine(LimitKind.Maximum, 1.4e9, -20, 1.6e9, -20) : new LimitLine(LimitKind.Maximum, 1.47e9, -20, 1.53e9, -20)),
    ];

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
            new("ranges", "Range per channel", ParameterKind.Text, string.Empty) { Description = "From..to per channel, separated by commas, such as -0.6..1.4; a missing one is -1.5..1.5." },
            new("live", "Live samples", ParameterKind.Flag, false) { Description = "New samples keep coming after the newest one." },
            new("rate", "Samples per second", ParameterKind.Number, 1000.0) { Minimum = 1, Maximum = 100_000 },
        ],
        (chart, series, data, _) => chart.Sweep((SweepData)data, Ranges(series, ((SweepData)data).Channels), Names(series)),
        (series, data, _) => series.Parameters.GetText("ranges", string.Empty).Trim().Length == 0
            ? $".Sweep({data}, Enumerable.Repeat(new DoubleRange(-1.5, 1.5), {data}.Channels).ToArray(), [{string.Join(", ", Names(series).Select(CodeText.String))}])"
            : $".Sweep({data}, new[] {{ {string.Join(", ", Ranges(series, 0).Select(range => $"new DoubleRange({CodeText.Number(range.Min)}, {CodeText.Number(range.Max)})"))} }}.Concat(Enumerable.Repeat(new DoubleRange(-1.5, 1.5), {data}.Channels)).Take({data}.Channels).ToArray(), [{string.Join(", ", Names(series).Select(CodeText.String))}])")
    {
        Usings = ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Numerics", "Tachyon.Charts.Core.Series", "System.Diagnostics"],
        OwnsAxes = true,
        Region = (series, data) => (new DoubleRange(0, ((SweepData)data).Window), Ranges(series, 1)[0]),
        Start = (series, data) => series.Parameters.GetFlag("live", false) ? SweepFeed.Start((SweepData)data, series.Parameters.GetNumber("rate", 1000)) : null,
        Helpers = SweepFeed.Code,
        Prepare = (series, data) =>
        {
            var monitor = (SweepData)data;
            monitor.Mode = Enum.Parse<SweepMode>(series.Parameters.GetText("mode", nameof(SweepMode.Sweep)));
            monitor.GapShare = series.Parameters.GetNumber("gap", 0.04);
        },
        PrepareCode = (series, data) =>
        [
            $"{data}.Mode = SweepMode.{series.Parameters.GetText("mode", nameof(SweepMode.Sweep))};",
            $"{data}.GapShare = {CodeText.Number(series.Parameters.GetNumber("gap", 0.04))};",
            .. series.Parameters.GetFlag("live", false) ? [$"StartSamples({data}, {CodeText.Number(series.Parameters.GetNumber("rate", 1000))});"] : Array.Empty<string>(),
        ],
    };

    // The range of each channel as the list gives it; a missing or unreadable one is -1.5..1.5. With a count of 0, as many
    // as the list has.
    private static DoubleRange[] Ranges(SeriesSpec series, int count)
    {
        var parts = series.Parameters.GetText("ranges", string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var ranges = new DoubleRange[count > 0 ? count : parts.Length];
        for (var c = 0; c < ranges.Length; c++)
        {
            var ends = c < parts.Length ? parts[c].Split("..") : [];
            ranges[c] = ends.Length == 2
                && double.TryParse(ends[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var from)
                && double.TryParse(ends[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var to)
                && from < to
                    ? new DoubleRange(from, to)
                    : new DoubleRange(-1.5, 1.5);
        }

        return ranges;
    }

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
    // A carrier that drifts up in frequency beside a steady one; 300 spectra so far, so that a waterfall shows its way.
    private static SampleInfo DriftSample() => new(
        "drift",
        "Drifting carrier with history",
        DataShape.Spectrum,
        1024,
        count =>
        {
            var display = new SpectrumDisplay();
            display.SetView(0, 1e7, 0);
            var (frequencies, levels) = (new double[count], new float[count]);
            for (var step = 0; step < 300; step++)
            {
                SpectrumFeed.Drift(display, step, frequencies, levels);
            }

            return display;
        },
        "SpectrumDisplay",
        method => SampleCatalog.Header("a carrier drifting beside a steady one, with 300 spectra so far", method, "SpectrumDisplay", "The number of frequencies.") + """
                {
                    var display = new SpectrumDisplay();
                    display.SetView(0, 1e7, 0);
                    var (frequencies, levels) = (new double[count], new float[count]);
                    for (var step = 0; step < 300; step++)
                    {
                        Drift(display, step, frequencies, levels);
                    }

                    return display;
                }

            """ + SpectrumFeed.DriftCode)
    { MinimumCount = 16 };

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

    // Three carriers in noise as a measurement delivers them: the last of 60 spectra.
    private static SampleInfo CarriersSample() => new(
        "noisyCarriers",
        "Three carriers in noise",
        DataShape.Spectrum,
        1500,
        count =>
        {
            var display = new SpectrumDisplay();
            display.SetView(0, 1e7, 0);
            var (frequencies, levels, random) = (new double[count], new float[count], new Random(1)); // NOSONAR: makes sample data, no security
            for (var sweep = 0; sweep < 60; sweep++)
            {
                CarrierFeed.Deliver(display, frequencies, levels, random);
            }

            return display;
        },
        "SpectrumDisplay",
        method => SampleCatalog.Header("three carriers in noise, the last of 60 spectra", method, "SpectrumDisplay", "The number of frequencies.") + """
                {
                    var display = new SpectrumDisplay();
                    display.SetView(0, 1e7, 0);
                    var (frequencies, levels, random) = (new double[count], new float[count], new Random(1));
                    for (var sweep = 0; sweep < 60; sweep++)
                    {
                        Deliver(display, frequencies, levels, random);
                    }

                    return display;
                }

            """ + CarrierFeed.DeliverCode)
    { MinimumCount = 16 };

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

    // Four channels of a patient monitor at 25 000 samples per second over a window of 6 s: two leads of a heartbeat, a
    // pulse wave and breathing.
    private static SampleInfo PatientSample() => new(
        "patient",
        "Patient monitor, four channels",
        DataShape.Sweep,
        210_000,
        count =>
        {
            var data = new SweepData(4, 175_000, 6);
            var values = new float[4];
            for (var i = 0; i < count; i++)
            {
                SweepFeed.Values(i / 25_000.0, values);
                data.Append(i / 25_000.0, values);
            }

            return data;
        },
        "SweepData",
        method => SampleCatalog.Header("two leads of a heartbeat, a pulse wave and breathing, 25 000 samples per second", method, "SweepData", "The number of samples per channel.") + """
                {
                    var data = new SweepData(4, 175_000, 6);
                    var values = new float[4];
                    for (var i = 0; i < count; i++)
                    {
                        Values(i / 25_000.0, values);
                        data.Append(i / 25_000.0, values);
                    }

                    return data;
                }

                // The values of the four channels at a time: two leads of a heartbeat, a pulse wave and breathing.
                private static void Values(double time, float[] values)
                {
                    var beat = time * 72 / 60 % 1;
                    var ecg = (0.12 * Bell(beat, 0.18, 0.025)) - (0.15 * Bell(beat, 0.36, 0.008)) + Bell(beat, 0.39, 0.010) - (0.25 * Bell(beat, 0.42, 0.010)) + (0.3 * Bell(beat, 0.62, 0.045));
                    values[0] = (float)ecg;
                    values[1] = (float)((ecg * 0.8) + (0.05 * Math.Sin(time * 2 * Math.PI * 0.3)));
                    var pulse = (beat + 0.85) % 1;
                    values[2] = (float)((0.9 * Bell(pulse, 0.25, 0.07)) + (0.3 * Bell(pulse, 0.5, 0.06)));
                    values[3] = (float)Math.Sin(time * 2 * Math.PI * 0.25);
                }

                private static double Bell(double x, double center, double width) => Math.Exp(-((x - center) * (x - center)) / (2 * width * width));

            """)
    { MinimumCount = 2 };
}
