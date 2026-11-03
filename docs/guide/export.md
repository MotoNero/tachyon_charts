# Export, clipboard, printing

All ways show what the chart shows at this moment, with annotations. The display does not change.

```csharp
var options = new ExportOptions { Width = 1200, Height = 800, Dpi = 300, Theme = BuiltInThemes.Paper };
await File.WriteAllBytesAsync("chart.png", await chart.ExportPngAsync(options));
await File.WriteAllBytesAsync("chart.svg", await chart.ExportSvgAsync(options));
await File.WriteAllBytesAsync("chart.pdf", await chart.ExportPdfAsync(options));
await File.WriteAllBytesAsync("data.csv", await chart.ExportCsvAsync());
```

`Width` and `Height` count in units of 1/96 inch. `Dpi` gives more pixels with the same layout. `Theme` only applies to the image.

| Format | Content |
|---|---|
| PNG | Pixels from the active backend, the resolution is stored in the file |
| SVG, PDF | Lines and areas as vectors, text and rasters as images of at least 288 dpi |
| CSV | `series,x,y`, numbers with a dot, visible or selected points |

## Clipboard and printing

```csharp
await chart.CopyImageAsync();
await chart.CopyDataAsync();
await chart.PrintAsync("My report"); // WPF only
```

In Avalonia, print with `ExportPdfAsync` and hand the file to the system.

## Limits

- Text in SVG and PDF cannot be selected.
- Bars, areas and rasters have no CSV rows.
- A chart must be visible, otherwise you get an `InvalidOperationException`.
- SVG and PDF cost one rebuild of the graphics buffers. The display stays identical but can stutter briefly.
- Many markers without density mode make SVG and PDF large.
