# Point clouds

A point cloud holds up to 100 million points, for example from a scanner. It sorts them into a tree, so that the points in view come first: a cloud of 100 million points opens without freezing, shows a progress bar while it loads and turns at 60 frames per second.

```csharp
var cloud = new PointCloud3D(x, y, z, colors) { PointSize = 1.5 };
chart.Configure(c => c.XAxis(-1, 1, "x").YAxis(-1, 1, "y").ZAxis(-1, 1, "z").AddCloud(cloud));
```

## Loading in the background

Give a cloud a function instead of arrays, and it loads on a thread of its own while the chart already shows the first points:

```csharp
var cloud = new PointCloud3D(progress =>
{
    var data = PointFile.Read("scan.las");    // or build the arrays yourself, reporting progress
    return new PointCloudArrays(data.X, data.Y, data.Z, data.Colors);
});
```

`Progress`, `IsReady` and `Count` tell how far it is.

## Files

`Tachyon.Charts.ThreeD.Files` reads XYZ (text), PLY (text and binary) and LAS with code of its own, no further package:

```csharp
var cloud = PointFile.Read("scan.ply").ToCloud();
```

Broken files throw a `PointFileException` that says what is wrong.

## Memory

`PointCloud3D.EstimateMemory(count)` tells beforehand what a cloud takes on the CPU and on the graphics card; 100 million points need about 4 GB of memory and 2 GB on the card. A cloud draws at most `PointBudget` points per frame, 30 million by default, and lowers this number by itself when the graphics card needs longer than a frame has. Leaving a page with a cloud frees its memory on the card when the chart is disposed.

In the showroom: *Cloud of 20 million points*, *Cloud of 100 million points*.
