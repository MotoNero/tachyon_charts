# Surfaces, maps and meshes

## Surface

A surface takes a regular grid of heights, a `HeatmapData` as for 2D heatmaps, and the ranges it covers on the first two axes. Up to 4096 × 4096 values turn smoothly.

```csharp
var surface = new Surface3D(heights, new DoubleRange(0, 6), new DoubleRange(0, 6))
{
    Colormap = Colormaps.Viridis,
    ColorRange = new DoubleRange(-1, 1),                         // without it, the range of the z axis
    Contours = Contours3D.Even(new DoubleRange(-1.5, 2), 13) with { OnWalls = true },
};
chart.Configure(c => c.XAxis(0, 6, "x").YAxis(0, 6, "y").ZAxis(-1.5, 2, "z").AddSurface(surface));
```

`Contours3D` draws height lines on the surface, with `OnFloor` and `OnWalls` also projected onto the floor and the walls of the box. `Even(range, count)` spreads them evenly, `new Contours3D([0, 0.5])` takes chosen levels; `Color` and `Width` set their look. Missing values (`NaN`) leave a hole. When the data change, call `SetValues` again: the surface follows in the next frame.

## Map from measured points

Measurements rarely lie on a grid. `MeasuredMap3D` connects scattered points into triangles:

```csharp
var map = new MeasuredMap3D(x, y, z) { ShowsPoints = true, LongestEdge = 0.4 };
```

`LongestEdge` leaves out triangles longer than this, so that gaps between groups of points stay open. `ShowsPoints` marks the measured points.

## Mesh and light

`Mesh3D` draws any surface made of triangles, with a color per corner. `Material3D` sets the light: `IsLit`, `Gloss`, `Sharpness` and `Opacity`. Surfaces, maps, columns and volumes take a `Material` too.

```csharp
var mesh = new Mesh3D(vertices, indices) { Material = new Material3D { Gloss = 0.6, Opacity = 0.8 } };
chart.Configure(c => c.AddMesh(mesh));
```

With an `Opacity` below 1, what lies behind a mesh shows through.

## Planes, limits and flat shapes

`Shapes3D` makes flat shapes as meshes, so they take the light, cut planes and the pointer like any mesh. With an `Opacity` below 1 they show what lies behind them, without sorting.

```csharp
var limit = Shapes3D.Limit(xRange, yRange, 0.9, new Rgba8(255, 70, 70, 255));      // transparent level plane
var zone = Shapes3D.Rectangle(corner, across, up, blue, new Material3D { Opacity = 0.25 });
var area = Shapes3D.Polygon(polygon, origin, across, up, green);                      // a 2D polygon, holes included, on a plane in space
var roof = Shapes3D.Polygon(corners, green);                                          // corners in space, on the plane that fits them best
chart.Configure(c => c.AddSurface(surface).AddMesh(limit).AddMesh(zone).AddMesh(area));
```

- **Limit:** a level plane at a value of the third axis, transparent and without highlights by default (`Shapes3D.LimitMaterial`). The pointer shows the limit. Where a surface rises above it, the crossing shows as an edge; for a clear line add the limit as a height line: `Contours = new Contours3D([0.9]) { Color = red, OnFloor = false }`.
- **Rectangle:** a corner and two sides, at any angle, for example a test zone standing upright.
- **Polygon:** either a 2D `Polygon` with holes placed on a plane (a corner (x, y) lies at origin + x · across + y · up), or corners in space.

In the showroom: *Height lines*, *Surface 4096 × 4096*, *Map from measurements*, *Surface with light*, *Planes and limits*.
