# Polygons and areas

```csharp
var island = new Polygon(
    new PolygonRing(outerX, outerY),
    [new PolygonRing(lakeX, lakeY)])          // holes, any number
{
    Name = "Island",
};

chart.Configure(c => c
    .XAxis(a => a.Numeric("x").Range(0, 10))
    .YAxis(a => a.Numeric("y").Range(0, 10))
    .Polygons([island, star, ring], s => s.Thickness = 1.5)
    .Modifiers(m => m.ZoomPan().Tooltip()));
```

A ring needs at least three corners; the last connects to the first. The direction of the corners does not matter. Without `Polygon.Color` the polygons take the palette of the theme; `FillAlpha` sets the opacity of the fill, 0 draws only the outlines, `Stroke` and `Thickness` the outlines. Under the pointer the polygon is found, not its holes.

Each polygon is cut into triangles once; a frame only places the corners. 10 000 polygons zoom at 60 frames per second.

## A heatmap inside an area

```csharp
c.AddHeatmap(values, s => s.Cells(0, 0.025, 0, 0.025).Mask([region]))
 .Polygons([region], s => s.FillAlpha = 0);    // the outline on top
```

`Mask` shows only the cells whose middle lies inside the polygons; holes stay clear. The cells outside have no value under the pointer either. In code, set `HeatmapSeries.Mask`.

In the showroom: *Polygons*, *10 000 polygons*, *Heatmap in an area*.
