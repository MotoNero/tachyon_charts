# Columns

Columns stand on a grid: one column per cell of a `HeatmapData`, its height the value. A million columns turn smoothly.

```csharp
var values = new HeatmapData(16, 16);
values.SetValues(heights);
var bars = new Bars3D(values, new DoubleRange(0, 16), new DoubleRange(0, 16))
{
    Width = 0.8,          // share of a cell
    Baseline = 0,         // where the columns start
    Colormap = Colormaps.Viridis,
};
chart.Configure(c => c.XAxis(0, 16, "x").YAxis(0, 16, "y").ZAxis(0, 1.2, "Value").AddBars(bars));
```

The columns rise from the `Baseline`. `ColorRange` fixes the colors, `Material` the light. A cut plane through the columns gives a 2D curve, see [cuts](3d-cuts.md).

In the showroom: *Columns*, *1 million columns*.
