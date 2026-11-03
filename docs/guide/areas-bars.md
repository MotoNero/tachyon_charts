# Areas and bars

Add these series with `AddSeries`, the same way in WPF and Avalonia. In XAML use `CustomSeries`.

```csharp
c.AddSeries((x, y) => new AreaSeries(data, x, y));
c.AddSeries((x, y) => new StackedAreaSeries([first, second], x, y));
c.AddSeries((x, y) => new BandSeries(low, high, x, y));
c.AddSeries((x, y) => new BarSeries([[1, 3, 2], [2, 1, 4]], x, y));
```

`BarSeries` knows grouped, stacked, 100 % and horizontal, plus round corners and shadows. For labels below bars there is the `CategoryAxis`, see [Axes](axes.md).

Fills can follow a gradient by position or value (`SeriesFill`).

**Limit:** areas and bars have no hit test. Tooltip, lasso and CSV export do not find them.
