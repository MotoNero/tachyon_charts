using Tachyon.Charts.Core.Annotations;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>Drawings on the prices of <see cref="CandleDemo"/>, fixed to days and prices.</summary>
public static class DrawingDemo
{
    /// <summary>How to operate the drawing tools.</summary>
    public const string Hint = "Keys 1 to 9: trend line, Fibonacci, extension, channel, pitchfork, Elliott, XABCD, head and shoulders, brush | Click: place points | Drag: move a drawing or its point | Delete | Ctrl+Z: undo | Esc, 0: stop";

    /// <summary>Creates the prices the drawings refer to.</summary>
    /// <returns>The candles.</returns>
    public static CandleData Prices() => CandleDemo.Create(160, TimeSpan.FromDays(1), seed: 5);

    /// <summary>Retracement, channel, pitchfork and a harmonic pattern.</summary>
    /// <param name="prices">The candles.</param>
    /// <param name="layer">Receives the drawings.</param>
    public static void First(CandleData prices, DrawingLayer layer)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Add(new Drawing(DrawingKind.FibonacciRetracement, [At(prices, 10, Low(prices, 10)), At(prices, 45, High(prices, 45))]));
        layer.Add(new Drawing(DrawingKind.TrendChannel, [At(prices, 60, Low(prices, 60)), At(prices, 95, Low(prices, 95)), At(prices, 75, High(prices, 75))]) { Color = new(76, 201, 240, 255) });
        layer.Add(new Drawing(DrawingKind.Pitchfork, [At(prices, 100, Low(prices, 100)), At(prices, 112, High(prices, 112)), At(prices, 122, Low(prices, 122))]) { Color = new(6, 214, 160, 255) });
        layer.Add(new Drawing(DrawingKind.Xabcd, [At(prices, 125, High(prices, 125)), At(prices, 132, Low(prices, 132)), At(prices, 139, High(prices, 139)), At(prices, 146, Low(prices, 146)), At(prices, 155, High(prices, 155))]) { Color = new(247, 37, 133, 255) });
    }

    /// <summary>Elliott waves, extension, head and shoulders and a free line.</summary>
    /// <param name="prices">The candles.</param>
    /// <param name="layer">Receives the drawings.</param>
    public static void Second(CandleData prices, DrawingLayer layer)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Add(new Drawing(DrawingKind.ElliottWave, [.. new[] { 2, 10, 16, 28, 34, 44 }.Select((day, i) => At(prices, day, i % 2 == 0 ? Low(prices, day) : High(prices, day)))]) { Color = new(76, 201, 240, 255) });
        layer.Add(new Drawing(DrawingKind.FibonacciExtension, [At(prices, 50, Low(prices, 50)), At(prices, 62, High(prices, 62)), At(prices, 70, Low(prices, 70))]));
        layer.Add(new Drawing(DrawingKind.HeadAndShoulders, [.. new[] { 85, 92, 98, 106, 114, 121, 128 }.Select((day, i) => At(prices, day, i % 2 == 0 ? Low(prices, day) : High(prices, day)))]) { Color = new(247, 37, 133, 255) });
        layer.Add(new Drawing(DrawingKind.Brush, [.. Enumerable.Range(135, 22).Select(day => At(prices, day, High(prices, day) + 1.5 + Math.Sin(day * 0.6)))]) { Color = new(6, 214, 160, 255) });
    }

    private static DrawingPoint At(CandleData prices, int day, double price) => new(prices[day].Time, price);

    private static double Low(CandleData prices, int day) => prices[day].Low;

    private static double High(CandleData prices, int day) => prices[day].High;
}
