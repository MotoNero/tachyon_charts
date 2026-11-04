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

## Cells of their own sizes

Give the edges of the columns and rows instead of a start and a step, for example logarithmically spaced frequencies:

```csharp
double[] columns = [.. Enumerable.Range(0, 4097).Select(i => Math.Pow(10, i * 4.0 / 4096))];   // 1 Hz to 10 kHz
c.XAxis(a => a.Log("frequency").Range(1, 10_000))
 .AddHeatmap(values, s => s.Edges(columns, rows));
```

There is one edge more than cells in each direction, and the edges must rise. Uneven cells of 4096 × 4096 zoom at 60 frames per second; the value under the pointer names the right cell, also on a logarithmic axis.

## Labelled contour lines

```csharp
c.AddContour(field, s => s.Filled().Labels().Levels(-1, -0.5, 0, 0.5, 1));
```

`Labels()` writes the value of each line along the line, upright, on a plate that hides the line beneath. Labels never overlap, and labels of the same line keep their distance; zooming and panning place them anew. In code, set `ContourSeries.ShowsLabels`.

In the showroom: *Uneven heatmap 4096²*, *Labelled contours*.
