# Moving from OxyPlot

This page is for you if your application draws its charts with OxyPlot, in WPF or Avalonia, with its own renderer or with the SkiaSharp renderer. It maps the concepts you know to Tachyon Charts and shows the same chart in both. OxyPlot is a trademark of its owners; this page only names it so that you can find your way.

## What changes in principle

| You know | In Tachyon Charts |
|---|---|
| `PlotModel` holds axes, series and annotations | `ChartBuilder` describes the chart, `Chart` (WPF or Avalonia control) shows it |
| `PlotView` (also the one of the SkiaSharp renderer) | `Chart` from `Tachyon.Charts.Wpf` or `Tachyon.Charts.Avalonia` |
| Choosing a renderer package | Not needed: the GPU draws, without a suitable GPU a software adapter, then Skia on the CPU ([renderer](renderer.md)) |
| `Points.Add(new DataPoint(x, y))` plus `InvalidatePlot(true)` | `data.Append(x, y)`, the chart redraws by itself |
| `lock (model.SyncRoot)` when writing from another thread | Not needed: data sources take writes from any thread |
| `ItemsSource` with `DataFieldX`/`DataFieldY` or `Mapping` | `ItemsSource` with `XPath`/`YPath`, or `ItemsDataSource` |
| `PlotController` and its input bindings | Modifiers: `ZoomPan`, `RubberBand`, `Crosshair`, `Tooltip` … ([interaction](interaction.md)) |
| Some thousand points per series stay fluid | Millions of points per series, streaming included |

You do not need a SkiaSharp package of your own. Tachyon brings its Skia fallback along.

## The same chart in both

Before:

```csharp
var model = new PlotModel { Title = "Sine" };
model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Time in s" });
model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Value" });
var series = new LineSeries { Title = "Signal", StrokeThickness = 2 };
for (var i = 0; i < 1000; i++)
{
    series.Points.Add(new DataPoint(i * 0.01, Math.Sin(i * 0.01)));
}

model.Series.Add(series);
plotView.Model = model;
```

After:

```csharp
var data = new DataSource<double, double>();
for (var i = 0; i < 1000; i++)
{
    data.Append(i * 0.01, Math.Sin(i * 0.01));
}

var chart = new Chart().Configure(c => c
    .Title("Sine")
    .XAxis(a => a.Numeric("x").Title("Time in s").AutoRange())
    .YAxis(a => a.Numeric("y").Title("Value").AutoRange())
    .AddLine(data, s => s.Name("Signal").Thickness(2))
    .Modifiers(m => m.ZoomPan().RubberBand().Crosshair().Tooltip().Legend()));
```

Zoom, pan and the legend are switched on explicitly. In XAML the chart looks as in [Getting started](getting-started.md).

## Live data

Before, a timer adds points, removes old ones and calls `InvalidatePlot(true)` under the lock of the model. After, a ring buffer does all of it:

```csharp
var ring = new FifoDataSource<double, float>(1_000_000, singleProducer: true);
ring.Append(xBlock, yBlock); // from your measuring thread, no lock, no invalidate
```

Blocks are faster than single points. More in [data](data.md).

## Axes

| You know | In Tachyon Charts |
|---|---|
| `LinearAxis` | `a.Numeric("id")` |
| `LogarithmicAxis` | `a.Log("id")` |
| `DateTimeAxis` with `DateTimeAxis.ToDouble` | `a.DateTime("id")`, the times stay exact also after years |
| `Position = AxisPosition.Right` | `.Position(AxisPosition.Right)` |
| `Minimum`/`Maximum` | `.Range(min, max)`; `.Limits(min, max)` bounds zooming |
| `Key` on the axis, `YAxisKey` on the series | The id of the axis, the series names it when there are several |

More in [axes](axes.md).

## Series

| You know | In Tachyon Charts |
|---|---|
| `LineSeries`, `StairStepSeries` | `AddLine` ([lines](lines.md)) |
| `ScatterSeries` | `AddScatter`, also with density coloring ([scatter](scatter.md)) |
| `AreaSeries`, two-line areas | `AreaSeries`, `BandSeries`, `StackedAreaSeries` ([areas and bars](areas-bars.md)) |
| `BarSeries`, `ColumnSeries` | `BarSeries` ([areas and bars](areas-bars.md)) |
| `BoxPlotSeries`, `HistogramSeries` | `BoxPlotSeries`, histogram, violin ([statistics](statistics.md)) |
| `ErrorBarSeries`, `StemSeries` | Error bars and stems ([measurements](measurements.md)) |
| `HeatMapSeries` | `AddHeatmap`, `AddSpectrogram` for rows that keep arriving ([rasters](rasters.md)) |
| `ContourSeries` | `AddContour` ([rasters](rasters.md)) |
| `FunctionSeries` | Fill a `DataSource` with the function values |
| Polar plots with `AngleAxis`/`MagnitudeAxis` | Polar and radar ([polar](polar.md)) |
| `CandleStickSeries`, `HighLowSeries` | Not yet; candlestick and OHLC are planned |

Surfaces, waterfalls, point clouds and volumes in 3D are listed in the [README](../../README.md).

## Annotations

| You know | In Tachyon Charts |
|---|---|
| `LineAnnotation` (horizontal or vertical) | `HorizontalLineAnnotation`, `VerticalLineAnnotation` |
| `RectangleAnnotation` | `BoxAnnotation`, `BandAnnotation` |
| `TextAnnotation`, `ArrowAnnotation`, `ImageAnnotation` | `TextAnnotation`, `ArrowAnnotation`, `ImageAnnotation` |
| `PointAnnotation` | `MarkerAnnotation` |

The positions bind like any other property, and with the `AnnotationEdit` modifier the user moves them with the mouse. More in [annotations](annotations.md).

## Look and export

| You know | In Tachyon Charts |
|---|---|
| Colors and fonts on the model | Themes: `c.Theme(BuiltInThemes.Paper)` or from resources ([themes](themes.md)) |
| `Legends.Add(new Legend { … })` | The `Legend` modifier, a click hides a series |
| `PngExporter`, `SvgExporter`, `PdfExporter` | `ExportPngAsync`, `ExportSvgAsync`, `ExportPdfAsync`, plus CSV, clipboard and print ([export](export.md)) |

## Step by step

1. Replace the package references with `Tachyon.Charts.Wpf` or `Tachyon.Charts.Avalonia`, and the plot view with `Chart`.
2. Move the data into `DataSource` or `FifoDataSource`. Remove `InvalidatePlot` calls and locks on the model.
3. Translate axes and series with the tables above, one chart at a time.
4. Choose the modifiers your users need; nothing is on without being asked for.
5. Check the result: the diagnostics overlay shows the frame time and the points drawn ([renderer](renderer.md)).
