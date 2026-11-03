# Lines

```csharp
c.AddLine(data, s => s.Name("Temperature").Stroke(new Rgba8(240, 130, 90, 255)).Thickness(1.5));
```

Lines with 100 million points stay fluid because each pixel column only draws its minimum and maximum. Peaks are never lost. Zooming in brings back the single points.

In XAML the properties are `Stroke`, `StrokeThickness`, `StrokeDashArray` and `IsStep`. `IsStep` turns the line into stairs, `StrokeDashArray` dashes it.

Thinner than one pixel works: the line then gets fainter. With very many series that gives a calm picture.

With `.Spline()` the line runs as a smooth curve through its points without overshooting the measured values. This only applies while all points are drawn, and not to ring buffers.

The glow comes from the theme, see [Themes](themes.md).
