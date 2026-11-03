# Histogram, box plot, violin

`Histogram` computes bins from raw values, with Sturges, Freedman-Diaconis, a fixed count or a fixed width. A `BarSeries` draws the result.

`BoxPlotSeries` and `ViolinSeries` enter the chart through `AddSeries`:

```csharp
c.AddSeries((x, y) => new BoxPlotSeries(groups, x, y));
```

**Limit:** neither has a hit test.
