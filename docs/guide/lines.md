# Lines

```csharp
c.AddLine(data, s => s.Name("Temperature").Stroke(new Rgba8(240, 130, 90, 255)).Thickness(1.5));
```

Lines with 100 million points stay fluid because each pixel column only draws its minimum and maximum. Peaks are never lost. Zooming in brings back the single points.

In XAML the properties are `Stroke`, `StrokeThickness`, `StrokeDashArray` and `IsStep`. `IsStep` turns the line into stairs, `StrokeDashArray` dashes it.

Thinner than one pixel works: the line then gets fainter. With very many series that gives a calm picture.

With `.Spline()` the line runs as a smooth curve through its points without overshooting the measured values. This only applies while all points are drawn, and not to ring buffers.

The glow comes from the theme, see [Themes](themes.md).

## Last value at the axis

```csharp
c.AddLine(temperature, s => s.Name("Core").LastValue());
```

The newest value of the line shows as a plate in its color at its vertical axis, with a small arrow at the value, and follows live data. Plates of several series move apart so that they never overlap; a value outside the view sticks to the edge. In code, set `ShowsLastValue` on any series; candles show the close of the last candle. The marks allocate nothing per frame.

In the showroom: *Last value at the axis*.

## Values at the points

```csharp
c.AddLine(target, s => s.Name("Target").Values());
c.AddSeries((x, y) => new BarSeries(rows, x, y) { ShowsValues = true });
```

Every series can write its values at its points: `ShowsValues` on any series, `.Values()` on lines, scatters and heatmaps. Bars and histograms get their value on top (horizontal bars beside them), stacked parts and heatmap cells in their middle, box plots and violins their median, areas, bands, stems, error bars and points above each point. A label that would cover another is left out, so dense data stay readable and show only some values; a large heatmap labels an even grid of its cells. In 3D, columns, surfaces, lines, points, bubbles, clouds and measured maps have `ShowsValues` too; pies label their slices with `Labels`. The labels allocate nothing per frame.
