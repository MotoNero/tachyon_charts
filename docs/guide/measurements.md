# Error bars and stems

Add both series with `AddSeries`, the same way in WPF and Avalonia.

```csharp
c.AddSeries((x, y) => new ErrorBarSeries(values, lower, upper, x, y) { Thickness = 1.5, CapWidth = 8 });
c.AddSeries((x, y) => new StemSeries(spectrum, x, y) { Baseline = 0, HeadSize = 6 });
```

`lower` and `upper` say per point how far the bar reaches from the value. The automatic axis range includes the bars and the baseline. `NaN` skips a point.

**Limit:** neither has a hit test. Tooltip, lasso and CSV export do not find them.
