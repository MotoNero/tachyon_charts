# Mouse and keyboard

Interactions are building blocks:

```csharp
.Modifiers(m => m.ZoomPan().RubberBand().AxisDrag().Crosshair().Tooltip().Legend().Keyboard()
                 .Selection(SelectionMode.Lasso).MeasureCursors())
```

| Block | Operation |
|---|---|
| `ZoomPan` | Wheel zooms, dragging pans |
| `RubberBand` | Ctrl + drag zooms into a rectangle, double click shows everything again |
| `AxisDrag` | Wheel and drag over an axis |
| `Crosshair` | Crosshair, with `snap: true` on the nearest point |
| `Rollover`, `Tooltip` | Values under the mouse |
| `Selection` | Shift + drag selects points |
| `MeasureCursors` | Alt + drag measures distances |
| `Keyboard` | Arrows pan, plus and minus zoom, Home shows everything, Ctrl+Z and Ctrl+Y walk through the history |
| `Legend` | Legend, a click hides a series |
| `AnnotationEdit` | Move annotations |
| `Diagnostics` | F12 shows frame times |
| `Custom` | Your own `ChartModifier` |

## Selecting points

Selecting picks measured points from a chart so that you can work with exactly those: export them, or analyze them in your application. Hold Shift and drag around the points. Esc removes the selection.

Set `SelectionModifier.ModifierKey` to `InputModifiers.None` to select with a plain drag, for example behind a button in your toolbar. `SelectionChanged` delivers the selection, `ExportCsvAsync(selection)` writes it, and `ChartSurface.ReadPoints` reads the values. Above 50,000 points an even share is marked in the chart, the selection itself stays complete.

## Context menu

Set the standard `ContextMenu` property of the chart. A right click opens it, also on the GPU, where WPF would not see the click. See also [Export](export.md).
