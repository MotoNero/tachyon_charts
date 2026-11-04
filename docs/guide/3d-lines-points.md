# Lines and points

Lines and points keep their positions in a `Points3D` ring: it has a fixed capacity, new points push out the oldest ones, and any thread may write.

```csharp
var path = new Points3D(capacity: 100_000);
chart.Configure(c => c.XAxis(-25, 25, "x").YAxis(-30, 30, "y").ZAxis(0, 55, "z")
    .AddLine(new Line3D(path) { Width = 2 }));

path.Append(x, y, z, color);   // one point, from a measuring thread
path.AppendGap();              // the line breaks here
```

`Points3D.From(x, y, z, colors)` makes a ring from arrays. Without colors, `Line3D.Color` paints the line.

## Points

```csharp
var cloud = Points3D.From(x, y, z, colors);
chart.Configure(c => c.AddScatter(new Scatter3D(cloud) { Size = 4 }));
```

`Size` is in pixels; `Append(x, y, z, color, size)` gives a point a size of its own. A scatter draws up to `PointBudget` points per frame, 15 million by default, and leaves out an even share of the rest, so that large clouds keep turning smoothly. Appending millions of points per second works: the GPU gets only what is new.

In the showroom: *Path, live*, *Path of 200 000 points*, *Scatter of 3000 points*, *Scatter of 10 million points*, *Cloud, live*.
