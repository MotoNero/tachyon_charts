using System.Globalization;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// The share prices of the showroom pages as sample data: a random walk from 100 with eight steps per period, the same
/// on every run for a seed; and the market that trades on the last one when the data are live.
/// </summary>
internal static class ShowroomPrices
{
    private static readonly DateTime _start = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    public static IReadOnlyList<SampleInfo> Create() =>
    [
        Walk("dailyWalk", "Daily prices, a random walk", 250, TimeSpan.FromDays(1), "TimeSpan.FromDays(1)", 7),
        Walk("dailyWalk3", "Daily prices, another walk", 120, TimeSpan.FromDays(1), "TimeSpan.FromDays(1)", 3),
        Walk("dailyWalk5", "Daily prices for drawings", 160, TimeSpan.FromDays(1), "TimeSpan.FromDays(1)", 5),
        Walk("dailyWalk11", "Daily prices over a year", 400, TimeSpan.FromDays(1), "TimeSpan.FromDays(1)", 11),
        Walk("minuteWalk", "Minute prices", 1_000_000, TimeSpan.FromMinutes(1), "TimeSpan.FromMinutes(1)", 7),
        Workdays(),
    ];

    /// <summary>Makes the prices of a walk.</summary>
    /// <param name="count">The number of periods.</param>
    /// <param name="period">The length of a period.</param>
    /// <param name="seed">The seed of the walk.</param>
    /// <returns>The prices.</returns>
    public static CandleData Prices(int count, TimeSpan period, int seed)
    {
        var data = new CandleData(count);
        var random = new Random(seed); // NOSONAR: makes sample data, no security
        var price = 100.0;
        for (var i = 0; i < count; i++)
        {
            var (open, high, low) = (price, price, price);
            for (var step = 0; step < 8; step++)
            {
                price *= 1 + ((random.NextDouble() - 0.5) * 0.012);
                high = Math.Max(high, price);
                low = Math.Min(low, price);
            }

            var volume = Math.Round(5000 + (random.NextDouble() * 20000 * (1 + (Math.Abs(price - open) / open * 50))));
            data.Append((_start + (period * i)).Ticks, open, high, low, price, volume);
        }

        return data;
    }

    /// <summary>Starts trading on the last period of live prices, five times a second; now and then a new day begins.</summary>
    /// <param name="data">The prices.</param>
    /// <returns>What stops the market.</returns>
    public static IDisposable Trade(CandleData data)
    {
        var random = new Random(7); // NOSONAR: makes sample data, no security
        return new Timer(
            _ =>
            {
                lock (data)
                {
                    var last = data[data.Count - 1];
                    var price = last.Close * (1 + ((random.NextDouble() - 0.5) * 0.004));
                    data.AddTick(price, Math.Round(random.NextDouble() * 400));
                    if (random.NextDouble() < 0.04)
                    {
                        data.Append(last.Time + TimeSpan.FromDays(1).Ticks, price, price, price, price, 0);
                    }
                }
            },
            null,
            200,
            200);
    }

    /// <summary>Writes the market of live prices as code: a method that makes the prices and lets them trade.</summary>
    /// <param name="method">The name of the method.</param>
    /// <param name="prices">The name of the method of the sample that makes the prices.</param>
    /// <returns>The code.</returns>
    public static string TradeCode(string method, string prices) => $$"""

            /// <summary>Makes live prices: the last period trades five times a second, now and then a new day begins.</summary>
            /// <param name="count">The number of periods at the start.</param>
            /// <returns>The prices, which keep changing.</returns>
            public static CandleData {{method}}(int count)
            {
                var data = {{prices}}(count);
                var random = new Random(7);
                _timers.Add(new Timer(_ =>
                {
                    lock (data)
                    {
                        var last = data[data.Count - 1];
                        var price = last.Close * (1 + ((random.NextDouble() - 0.5) * 0.004));
                        data.AddTick(price, Math.Round(random.NextDouble() * 400));
                        if (random.NextDouble() < 0.04)
                        {
                            data.Append(last.Time + TimeSpan.FromDays(1).Ticks, price, price, price, price, 0);
                        }
                    }
                }, null, 200, 200));
                return data;
            }

        """;

    private static SampleInfo Walk(string id, string name, int count, TimeSpan period, string periodCode, int seed) => new(
        id,
        name,
        DataShape.Candles,
        count,
        n => Prices(n, period, seed),
        "CandleData",
        method => SampleCatalog.Header(name.ToLowerInvariant(), method, "CandleData", "The number of periods.") + WalkCode(periodCode, seed));

    // The working days of a walk: the weekends of a daily walk are left out.
    private static SampleInfo Workdays() => new(
        "workdayWalk",
        "Prices of working days",
        DataShape.Candles,
        60,
        n =>
        {
            var all = Prices((n * 7 / 5) + 7, TimeSpan.FromDays(1), 9);
            var data = new CandleData(n);
            for (var i = 0; i < all.Count && data.Count < n; i++)
            {
                if (new DateTime((long)all[i].Time, DateTimeKind.Utc).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    data.Append(all[i]);
                }
            }

            return data;
        },
        "CandleData",
        method => SampleCatalog.Header("prices of working days", method, "CandleData", "The number of days.") + """
                {
                    var all = WalkOfDays((count * 7 / 5) + 7);
                    var data = new CandleData(count);
                    for (var i = 0; i < all.Count && data.Count < count; i++)
                    {
                        if (new DateTime((long)all[i].Time, DateTimeKind.Utc).DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                        {
                            data.Append(all[i]);
                        }
                    }

                    return data;
                }

                private static CandleData WalkOfDays(int count)

            """ + WalkCode("TimeSpan.FromDays(1)", 9));

    private static string WalkCode(string period, int seed) => """
                {
                    var data = new CandleData(count);
                    var random = new Random(SEED);
                    var (start, price) = (new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), 100.0);
                    for (var i = 0; i < count; i++)
                    {
                        var (open, high, low) = (price, price, price);
                        for (var step = 0; step < 8; step++)
                        {
                            price *= 1 + ((random.NextDouble() - 0.5) * 0.012);
                            high = Math.Max(high, price);
                            low = Math.Min(low, price);
                        }

                        var volume = Math.Round(5000 + (random.NextDouble() * 20000 * (1 + (Math.Abs(price - open) / open * 50))));
                        data.Append((start + (PERIOD * i)).Ticks, open, high, low, price, volume);
                    }

                    return data;
                }

            """.Replace("SEED", seed.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("PERIOD", period, StringComparison.Ordinal);
}
