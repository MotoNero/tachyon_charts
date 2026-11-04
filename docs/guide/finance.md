# Candles, OHLC bars and volume

```csharp
var data = new CandleData();
data.Append(DateTime.UtcNow.Date.Ticks, open: 100, high: 104, low: 99, close: 103, volume: 18_000);

chart.Configure(c => c
    .XAxis(a => a.DateTime("time"))
    .YAxis(a => a.Numeric("price").Title("Price").AutoRange())
    .YAxis(a => a.Numeric("volume").Title("Volume").AutoRange().Pane(1, 0.3))   // a pane below, 30 % of the height
    .Candles(data, yAxis: "price")
    .Volume(data, yAxis: "volume")
    .Modifiers(m => m.ZoomPan().Crosshair()));
```

`CandleData` holds the candles in the order of time; times are in the units of the time axis, `DateTime.Ticks` by default. Writing is safe from any thread. Only the last candle changes after it was added:

```csharp
data.AddTick(price: 103.4, volume: 250);   // the trade becomes the close and widens high and low
data.UpdateLast(new Candle(time, 100, 105, 99, 104, 21_000));
```

## Look

```csharp
c.Candles(data, s =>
{
    s.Style = CandleStyle.Ohlc;                                        // or Candles
    s.Rising = new CandleLook(Body: green, Line: green, IsHollow: true);
    s.Falling = new CandleLook(Body: red, Line: red);
    s.BodyWidth = 0.6;                                                 // share of the room of one period
});
```

The volume bars take `RisingColor` and `FallingColor` of the `VolumeSeries`.

## Many candles

When several periods share a pixel column, the column draws them as one candle: the first open, the highest high, the lowest low and the last close, with the summed volume. A million minute candles zoom at 60 frames per second in a full-screen window.

Under the pointer the top left of the chart shows date, open, high, low, close and volume of the candle; without a pointer, of the last candle.

Gaps for weekends and trading pauses come with axes that skip ranges, which are planned.

In the showroom: *Candles* (the last day trades live), *OHLC bars*, *1 million candles*.
