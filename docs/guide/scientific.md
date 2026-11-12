# Smith chart, ternary plot, parallel coordinates

All three are complete chart types with their own builder method, the same way in WPF and Avalonia.

## Smith chart

```csharp
chart.Configure(c => c.Smith(resistance, reactance, markerIndex).Theme(BuiltInThemes.Instrument));
```

Resistance and reactance are in ohms, the reference impedance is 50 ohms unless you pass another. Clicking or dragging moves the marker along the curve. The marker shows the impedance, the reflection and the VSWR.

## Ternary plot

```csharp
chart.Configure(c => c.Ternary(sand, silt, clay, ("Sand", "Silt", "Clay")));
```

Three shares per point, with the names of the three corners.

## Parallel coordinates

```csharp
chart.Configure(c => c.ParallelCoordinates(columns, ["V", "A", "°C"]));
// or in a color of their own, with little opacity so that many lines show where they gather:
chart.Configure(c => c.ParallelCoordinates(columns, ["V", "A", "°C"], new Rgba8(80, 170, 240, 50)));
```

`columns` holds one array per measure. Each row becomes a line across the axes. Rows above 2,000 get more transparent on their own, so that the density stays visible.

| Gesture | Effect |
|---|---|
| Drag an axis at its name | Move the axis |
| Drag along an axis | Keep only the rows of that range |
| Double click anywhere | Show all rows again |

In code the same is `MoveAxis`, `SetFilter` and `ClearFilters` of `ParallelCoordinatesSeries`.
