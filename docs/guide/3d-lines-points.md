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

## Bubbles

`Bubbles3D` draws lit spheres, each with a diameter of its own: the size is a value of the data. Unlike points, bubbles cut into each other and into surfaces where they really meet, and they take the light like a surface.

```csharp
var diameters = Bubbles3D.ScaleToVolume(yields, smallest: 0.15, largest: 0.9);
var bubbles = new Bubbles3D(x, y, z, diameters) { Colors = colors, Values = yields };
chart.Configure(c => c.XAxis(0, 10).YAxis(0, 10).ZAxis(0, 8).AddBubbles(bubbles));
```

- **Size:** `SizeUnit = BubbleSizeUnit.Data` (the default) gives diameters in units of the first axis; the bubbles grow when the view zooms in and stay round when the other axes have other scales. `BubbleSizeUnit.Screen` gives diameters in screen points that stay the same at any zoom.
- **From values:** `ScaleToVolume` makes the volume of a bubble grow with its value, which is what the eye reads in 3D. Zero, negative and missing values give no bubble.
- **Pointer:** shows the place and, with `Values`, the value of the bubble. Cut planes hide bubbles on their far side.
- **Light:** `Material` sets gloss and sharpness. Bubbles are always opaque.

A million bubbles turn at the frame rate of the screen: each one is a single square that faces the eye, and the graphics card finds the sphere inside it per pixel.

In the showroom: *Path, live*, *Path of 200 000 points*, *Scatter of 3000 points*, *Scatter of 10 million points*, *Cloud, live*, *Bubbles*, *1 million bubbles*.
