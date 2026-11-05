# Axes

```csharp
.XAxis(a => a.DateTime("time").Title("Clock time"))
.YAxis(a => a.Numeric("temp").Title("°C").Range(0, 100))
.YAxis(a => a.Log("pressure").Position(AxisPosition.Right).Limits(1, 1e6))
```

| Method | Meaning |
|---|---|
| `Numeric`, `DateTime`, `Log`, `Category` | Kind of axis |
| `Title` | Title |
| `Position` | Side |
| `Range` | Fixed range |
| `Limits` | Bounds for zoom and pan |
| `AutoRange` | The range follows the data |
| `Inverted` | Reversed direction |
| `Hidden` | No labels and no space |

Series choose their axis by name. Several Y axes are allowed. `LayoutPane` describes stacked panes.

For labels instead of numbers use `.Category(["Jan", "Feb"])` or `CategoryAxis`, with rotated labels and automatic thinning.

Log axes define what happens to values ≤ 0 (`NonPositiveValues`).

## Breaks and gaps

A numeric axis can leave ranges out, for example to show a high peak and the small values beside it:

```csharp
c.YAxis(a => a.Numeric("y").Range(0, 1010).Breaks(new DoubleRange(45, 960)));
```

A break keeps only a narrow band of 1 % of its length, so a line that passes through it crosses steeply. A faint line across the plot and two slanted strokes on the axis mark it; no tick lies inside it.

A time axis hides times with a `TimeGapMap`, for example weekends or nights:

```csharp
c.XAxis(a => a.DateTime("time").AutoRange().Gaps(TimeGapMap.Weekends(firstDay, lastDay, TimeZoneInfo.Local)));
```

Hidden times take no room at all; candles on working days stand evenly. Zooming, panning and the value under the pointer go across breaks and gaps, and series draw as fast as without them, also with 100 million points.

In code, set `NumericAxis.Breaks` and `DateTimeAxis.Gaps`. In the showroom: *Scale break*, *Candles without weekends*.

## Axes through the plot

```csharp
c.XAxis(a => a.Numeric("x").Range(-10, 10).CrossesAt(0))
 .YAxis(a => a.Numeric("y").Range(-6, 6).CrossesAt(0));
```

`CrossesAt` lets an axis run through the plot where the other axis has that value, here both in the origin, as functions are drawn on paper. Such an axis takes no room at the edge; when the value leaves the view, the axis stays at the nearest edge. Where both axes cross, only the horizontal one writes its label. Series, zooming and the value under the pointer are the same as with axes at the edge.

In the showroom: *Axes in the origin*.

## X axis down

```csharp
c.XDown()
 .XAxis(a => a.Numeric("depth").Title("Depth in m").Range(0, 1500))
 .YAxis(a => a.Numeric("value").Title("Temperature in °C").Range(0, 120))
 .AddLine(temperature);
```

`XDown` turns the chart a quarter, as for a borehole log or a depth profile: the X axis runs down along the left side and the Y axis along the bottom. Every series stays as it is, also areas, bars, error bars, heatmaps and candles; the chart turns their picture on the graphics card or in the CPU fallback. Zooming, panning, the crosshair, tooltips, selections, annotations and drawing tools follow the turn; `ChartDirection.X` still means the X axis, which now runs up and down.

- `Inverted()` on the X axis lets it run up instead.
- Positions turn with the chart: an X axis at the top stands on the right, a Y axis on the right lies at the top.
- A turned chart has one pane. A pie stays upright; a Gantt chart shows its bars and the task under the pointer, without labels in the bars and without arrows.

In the showroom: *Depth profile*.
