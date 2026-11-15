using System.Collections.Concurrent;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Series;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Digital channels: levels and edges of a signal of two levels, one channel per series, at two values of the Y axis so
/// that channels stand one above the other. The sample is the logic analyzer of the showroom page, sixteen channels
/// that keep only their changes.
/// </summary>
internal static class DigitalType
{
    /// <summary>The id of the type.</summary>
    public const string Id = "digital";

    /// <summary>The id of the sample.</summary>
    public const string SampleId = "logicBus";

    /// <summary>The names of the channels of the sample, from top to bottom.</summary>
    public static readonly string[] Names = ["CLK", "CS", "WR", "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "IRQ", "ERR", "PWR", "LED", "SYNC"];

    private static readonly ConcurrentDictionary<int, Lazy<DigitalTransitions[]>> _buses = new();

    /// <summary>Gets the type.</summary>
    public static ChartTypeInfo Type { get; } = new(Id, "Digital channels", ChartCategory.TwoD, DataShape.Digital, SampleId,
        [
            new("channel", "Channel", ParameterKind.Integer, 0.0) { Minimum = 0, Maximum = Names.Length - 1, Description = "The channel of the sample: " + string.Join(", ", Names) + "." },
            new("low", "Low at", ParameterKind.Number, 0.0) { Description = "The value of the Y axis for the low level." },
            new("high", "High at", ParameterKind.Number, 1.0) { Description = "The value of the Y axis for the high level." },
            new("thickness", "Thickness", ParameterKind.Number, 1.0) { Minimum = 0.5, Maximum = 10 },
            new("color", "Color", ParameterKind.Color, string.Empty) { Description = "Empty for the usual green." },
        ], Apply, Code)
    { Usings = ["Tachyon.Charts.Core.Data", "Tachyon.Charts.Core.Series", "Tachyon.Charts.Rendering"] };

    /// <summary>Gets the sample.</summary>
    public static SampleInfo Sample { get; } = new(
        SampleId,
        "Channels of a logic analyzer, one nanosecond per point",
        DataShape.Digital,
        1_000_000_000,
        count => _buses.GetOrAdd(count, n => new(() => Bus(n))).Value,
        "DigitalTransitions[]",
        SampleCode)
    { MinimumCount = 1000, MaximumCount = 2_000_000_000 };

    /// <summary>Makes the channels: the same formula as the showroom page.</summary>
    /// <param name="count">The points per channel.</param>
    /// <returns>The channels, in the order of <see cref="Names"/>.</returns>
    public static DigitalTransitions[] Bus(long count)
    {
        var channels = new DigitalTransitions[16];
        var levels = new bool[16];
        var since = new long[16];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = new DigitalTransitions(0, 1e-9);
        }

        (levels[1], levels[2]) = (true, true);
        void Set(int channel, long at, bool level)
        {
            if (level != levels[channel] && at > since[channel] && at < count)
            {
                channels[channel].Append(levels[channel], at - since[channel]);
                (since[channel], levels[channel]) = (at, level);
            }
        }

        var half = Math.Max(1, count / 200_000);
        var period = Math.Max(1, count / 50);
        var burst = Math.Max(1, count / 500);
        var random = new Random(42); // NOSONAR: makes sample data, no security
        for (var at = half; at < count; at += half)
        {
            var rising = (at / half) % 2 == 1;
            Set(0, at, rising);
            var inBurst = (at + (period / 2)) % period < burst;
            Set(1, at, !inBurst);
            if (inBurst && rising)
            {
                if ((at / half / 2) % 8 == 0)
                {
                    Set(2, at, random.Next(2) == 0);
                }

                var value = random.Next(256);
                for (var bit = 0; bit < 8; bit++)
                {
                    Set(3 + bit, at, ((value >> bit) & 1) != 0);
                }
            }
        }

        foreach (var share in (ReadOnlySpan<double>)[0.13, 0.29, 0.41, 0.58, 0.66, 0.79, 0.93])
        {
            var at = (long)(count * share);
            Set(11, at, true);
            Set(11, at + 1, false);
        }

        Set(12, (long)(count * 0.871), true);
        Set(12, (long)(count * 0.871) + 1, false);
        Set(13, count / 100, true);
        for (var at = count / 10; at < count; at += Math.Max(1, count / 10))
        {
            Set(14, at, !levels[14]);
        }

        for (var at = count / 1000; at < count; at += Math.Max(1, count / 1000))
        {
            Set(15, at, true);
            Set(15, at + 10, false);
        }

        for (var i = 0; i < channels.Length; i++)
        {
            channels[i].Append(levels[i], count - since[i]);
        }

        return channels;
    }

    private static void Apply(ChartBuilder chart, SeriesSpec series, object data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var channels = (DigitalTransitions[])data;
        var channel = Math.Clamp((int)values.GetNumber("channel", 0), 0, channels.Length - 1);
        chart.AddSeries(
            (x, y) =>
            {
                var line = new DigitalSeries(channels[channel], x, y) { Name = series.Name, Low = values.GetNumber("low", 0), High = values.GetNumber("high", 1), Thickness = values.GetNumber("thickness", 1) };
                if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
                {
                    line.Color = color;
                }

                return line;
            },
            axes.X,
            axes.Y);
    }

    private static string Code(SeriesSpec series, string data, SeriesAxes axes)
    {
        var values = series.Parameters;
        var style = $"Name = {CodeText.String(series.Name)}, Low = {CodeText.Number(values.GetNumber("low", 0))}, High = {CodeText.Number(values.GetNumber("high", 1))}, Thickness = {CodeText.Number(values.GetNumber("thickness", 1))}";
        if (SpecColor.TryParse(values.GetText("color", string.Empty), out var color))
        {
            style += $", Color = {CodeText.Color(color)}";
        }

        var channel = Math.Clamp((int)values.GetNumber("channel", 0), 0, Names.Length - 1);
        return $".AddSeries((x, y) => new DigitalSeries({data}[{channel}], x, y) {{ {style} }}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})";
    }

    private static string SampleCode(string method) => SampleCatalog.Header("channels of a logic analyzer at one nanosecond per point, each keeping only its changes", method, "DigitalTransitions[]", "The points per channel.") + """
            {
                // CLK, CS, WR, D0 to D7, IRQ, ERR, PWR, LED, SYNC.
                var channels = new DigitalTransitions[16];
                var levels = new bool[16];
                var since = new long[16];
                for (var i = 0; i < channels.Length; i++)
                {
                    channels[i] = new DigitalTransitions(0, 1e-9);
                }

                (levels[1], levels[2]) = (true, true);
                void Set(int channel, long at, bool level)
                {
                    if (level != levels[channel] && at > since[channel] && at < count)
                    {
                        channels[channel].Append(levels[channel], at - since[channel]);
                        (since[channel], levels[channel]) = (at, level);
                    }
                }

                var half = Math.Max(1, count / 200_000);
                var period = Math.Max(1, count / 50);
                var burst = Math.Max(1, count / 500);
                var random = new Random(42);
                for (long at = half; at < count; at += half)
                {
                    var rising = (at / half) % 2 == 1;
                    Set(0, at, rising);
                    var inBurst = (at + (period / 2)) % period < burst;
                    Set(1, at, !inBurst);
                    if (inBurst && rising)
                    {
                        if ((at / half / 2) % 8 == 0)
                        {
                            Set(2, at, random.Next(2) == 0);
                        }

                        var value = random.Next(256);
                        for (var bit = 0; bit < 8; bit++)
                        {
                            Set(3 + bit, at, ((value >> bit) & 1) != 0);
                        }
                    }
                }

                foreach (var share in new[] { 0.13, 0.29, 0.41, 0.58, 0.66, 0.79, 0.93 })
                {
                    var at = (long)(count * share);
                    Set(11, at, true);
                    Set(11, at + 1, false);
                }

                Set(12, (long)(count * 0.871), true);
                Set(12, (long)(count * 0.871) + 1, false);
                Set(13, count / 100, true);
                for (long at = count / 10; at < count; at += Math.Max(1, count / 10))
                {
                    Set(14, at, !levels[14]);
                }

                for (long at = count / 1000; at < count; at += Math.Max(1, count / 1000))
                {
                    Set(15, at, true);
                    Set(15, at + 10, false);
                }

                for (var i = 0; i < channels.Length; i++)
                {
                    channels[i].Append(levels[i], count - since[i]);
                }

                return channels;
            }

        """;
}
