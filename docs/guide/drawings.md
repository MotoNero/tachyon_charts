# Drawing tools

```csharp
DrawingLayer? drawings = null;
chart.Configure(c => c
    .XAxis(a => a.DateTime("time"))
    .YAxis(a => a.Numeric("price").AutoRange())
    .Candles(data)
    .Drawings(layer => drawings = layer)
    .Modifiers(m => m.ZoomPan().Keyboard()));   // Keyboard brings Ctrl+Z and Ctrl+Y

drawings!.Tool = DrawingKind.FibonacciRetracement;   // for example from a toolbar
```

| Tool | Points | What it shows |
|---|---:|---|
| `TrendLine` | 2 | a line |
| `FibonacciRetracement` | 2 | levels 0, 23.6, 38.2, 50, 61.8, 78.6 and 100 % between low and high, with prices |
| `FibonacciExtension` | 3 | the move A to B projected from C at 61.8, 100, 127.2, 161.8 and 261.8 % |
| `TrendChannel` | 3 | a trend line, its parallel through the third point and the middle |
| `Pitchfork` | 3 | the median line from the pivot through the middle of the other two, with parallels, to the edge |
| `ElliottWave` | 6 | the waves 0 to 5 |
| `Xabcd` | 5 | X, A, B, C, D with the ratios AB/XA and CD/BC |
| `HeadAndShoulders` | 7 | left shoulder, head, right shoulder and the neckline |
| `Brush` | 2 or more | a free line along the pointer |

## Operation

With a tool set, each click places the next point; the last point finishes the drawing and the tool goes back to editing. A brush draws while the button is down. Without a tool, dragging a line moves the drawing, dragging one of its points moves that point; Delete removes the selected drawing and Escape stops a tool. The number keys 1 to 9 choose the tools in the order of the table, 0 none; `DrawingModifier.UsesNumberKeys` switches that off.

Every finished drawing, move and removal is a step of the undo chain of the chart, the same as zooming and panning: Ctrl+Z takes back the last of either.

## Values, not pixels

Each point is a value of the axes: a time and a price. Drawings stay on their candles when you zoom, pan or resize. On a time axis the X value is the absolute time, `DateTime.Ticks` by default.

```csharp
drawings.Add(new Drawing(DrawingKind.TrendLine, [new(start.Ticks, 98.2), new(end.Ticks, 104.5)]) { Color = new Rgba8(76, 201, 240, 255) });
```

## Saving

```csharp
File.WriteAllText("drawings.json", drawings.Save());
drawings.Load(File.ReadAllText("drawings.json"));
```

```json
{
  "version": 1,
  "drawings": [
    { "kind": "TrendLine", "color": "#4CC9F0FF", "thickness": 1.5, "points": [ { "x": 638409024000000000, "y": 98.2 }, { "x": 638418528000000000, "y": 104.5 } ] }
  ]
}
```

`Load` throws a `FormatException` that says what is wrong and keeps the drawings it had.

In the showroom: *Drawing tools*.
