# Point clouds

```csharp
c.AddScatter(points, s => s.Name("Points").Size(4));
```

## Density instead of single points

```csharp
c.AddScatter(points, s => s.Size(4).Density(1, a => a.Colormap(Colormaps.Inferno)));
```

When more points are in view than the threshold per pixel allows, the chart shows the density as color. Zooming in turns it back into markers. Both directions work on their own.

The color bar reacts to the mouse, see [Rasters](rasters.md#color-bar). A range you set stays when the chart switches between density and markers.

## Bubbles

`.Sizes(diameters)` gives each point its own size, in units of 1/96 inch.

## Own colors

`ScatterSeries.PointColors` takes one color per point. 10 million points with own colors are a measured target.
