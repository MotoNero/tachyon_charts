using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Data for axes with breaks: a measurement with one high peak, and prices on working days only.</summary>
public static class BreakDemo
{
    /// <summary>How to operate a chart with breaks.</summary>
    public const string Hint = "Wheel: zoom | Drag: pan across the break | Pointer: values | Double click: all";

    /// <summary>Creates a measurement with small values and one peak a hundred times higher.</summary>
    /// <returns>The points.</returns>
    public static DataSource<double, double> Peak()
    {
        var data = new DataSource<double, double>(2000);
        for (var i = 0; i < 2000; i++)
        {
            var x = i / 100.0;
            var y = 20 + (10 * Math.Sin(x * 3)) + (5 * Math.Sin(x * 11)) + (960 * Math.Exp(-((x - 12) * (x - 12)) / 0.02));
            data.Append(x, y);
        }

        return data;
    }

    /// <summary>Creates daily candles from Monday to Friday, without Saturdays and Sundays.</summary>
    /// <param name="days">The number of working days.</param>
    /// <returns>The candles.</returns>
    public static CandleData Workdays(int days)
    {
        var all = CandleDemo.Create(days * 7 / 5 + 7, TimeSpan.FromDays(1), seed: 9);
        var data = new CandleData(days);
        for (var i = 0; i < all.Count && data.Count < days; i++)
        {
            var candle = all[i];
            var day = new DateTime((long)candle.Time, DateTimeKind.Utc).DayOfWeek;
            if (day is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                data.Append(candle);
            }
        }

        return data;
    }

    /// <summary>Gets the weekends of the time the candles cover.</summary>
    /// <param name="data">The candles.</param>
    /// <returns>The gaps.</returns>
    public static TimeGapMap Weekends(CandleData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var first = new DateTime((long)data[0].Time, DateTimeKind.Utc);
        var last = new DateTime((long)data[data.Count - 1].Time, DateTimeKind.Utc);
        return TimeGapMap.Weekends(first, last, TimeZoneInfo.Utc);
    }
}
