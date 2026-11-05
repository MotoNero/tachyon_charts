using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Prices of an invented share as candles, and a market that keeps trading.</summary>
public static class CandleDemo
{
    /// <summary>The first day of the prices.</summary>
    public static readonly DateTime Start = new(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Lock _gate = new();
    private static readonly Random _random = new(7); // NOSONAR: makes sample data, no security
    private static CandleData? _live;
    private static Timer? _market;

    /// <summary>Creates candles from a random walk.</summary>
    /// <param name="count">The number of candles.</param>
    /// <param name="period">The length of one candle.</param>
    /// <param name="seed">The start of the random numbers, the same seed gives the same prices.</param>
    /// <returns>The candles.</returns>
    public static CandleData Create(int count, TimeSpan period, int seed = 7)
    {
        var data = new CandleData(count);
        var random = new Random(seed); // NOSONAR: makes sample data, no security
        var price = 100.0;
        for (var i = 0; i < count; i++)
        {
            var open = price;
            var high = open;
            var low = open;
            for (var step = 0; step < 8; step++)
            {
                price *= 1 + ((random.NextDouble() - 0.5) * 0.012);
                high = Math.Max(high, price);
                low = Math.Min(low, price);
            }

            var volume = Math.Round(5000 + (random.NextDouble() * 20000 * (1 + (Math.Abs(price - open) / open * 50))));
            data.Append((Start + (period * i)).Ticks, open, high, low, price, volume);
        }

        return data;
    }

    /// <summary>Gets daily candles whose last one keeps changing; from time to time a new day begins.</summary>
    /// <returns>The candles.</returns>
    public static CandleData Live()
    {
        lock (_gate)
        {
            if (_live is not null)
            {
                return _live;
            }

            _live = Create(250, TimeSpan.FromDays(1));
            _market ??= new Timer(_ => Trade(), null, 200, 200);
            return _live;
        }
    }

    private static void Trade()
    {
        var data = _live!;
        var last = data[data.Count - 1];
        var price = last.Close * (1 + ((_random.NextDouble() - 0.5) * 0.004));
        data.AddTick(price, Math.Round(_random.NextDouble() * 400));
        if (_random.NextDouble() < 0.04)
        {
            data.Append(last.Time + TimeSpan.FromDays(1).Ticks, price, price, price, price, 0);
        }
    }
}
