# Heatmap, spectrogram, contour

```csharp
c.AddHeatmap(grid, s => s.Cells(0, 0.5, 0, 0.5).ColorAxis(a => a.Colormap(Colormaps.Inferno)));
c.AddSpectrogram(rows, s => s.ColorAxis(a => a.Colormap(Colormaps.Viridis)));
c.AddContour(field, s => s.Filled().Levels(-1, 0, 1));
```

`HeatmapData` holds the raster, `SpectrogramData` a ring of rows: `AppendRow` writes from any thread. `Edges` allows cells of different widths. `NaN` shows the color for missing values.

The GPU computes colors and contour lines itself where it can. Otherwise the CPU takes over, with the same picture.

## Color bar

Every chart with a color scale reacts to the mouse on its own, even without `AxisDrag`:

| Gesture over the color bar | Effect |
|---|---|
| Wheel | Zoom the range |
| Drag | Move the range |
| Double click | The range follows the data again |

The plot axes stay where they are.
