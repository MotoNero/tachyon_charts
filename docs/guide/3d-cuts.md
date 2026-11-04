# Cut planes and 2D charts

## Cut planes

A plane cuts every 3D type: what lies in front of it stays, what lies behind is hidden. Up to four planes cut a scene; drag their orange outlines to move them.

```csharp
var plane = new ClipPlane3D(new Point3D(1, 0, 0), offset: 0.2);   // across the first axis
chart.Configure(c => c.AddSurface(surface).AddPlane(plane));
```

`Hides = false` only shows the outline and hides nothing, for a plane that feeds a cut.

## A cut as a 2D curve

`CutCurveFeed` keeps a 2D data source on the cut of a plane through a surface. When the plane moves or the surface changes, the curve follows in the next frame, and a 2D chart shows it like any other line:

```csharp
var plane = new ClipPlane3D(new Point3D(0, 1, 0)) { Hides = false };
var cut = new CutCurveFeed(plane, surface);
chart3D.Configure(c => c.AddSurface(surface).AddPlane(plane).AddCut(cut).Group("cut"));
chart2D.Configure(c => c
    .AddLine(cut.Data, s => s.Thickness(2))
    .AddOverlay(chart => CutMarkerOverlay.For(cut, chart))
    .Modifiers(m => m.Custom(() => new CutMarkerModifier(cut)).ZoomPan().Crosshair())
    .Group("cut"));
```

`CutMarkerOverlay` shows the markers of the 3D chart on the 2D curve with the same numbers; with `CutMarkerModifier`, Ctrl+click on the curve sets a marker in both charts or removes the one under the pointer, and Delete removes the newest.

## Coupling with 2D charts

`.Group(name)` puts a 3D chart into the same group as 2D charts: zooming and panning a 2D chart narrows the first axis of the 3D chart, the cursor of the 2D charts shows as a frame across the box, and the value under the pointer in 3D shows as a cursor in 2D. See [groups](groups.md).

In the showroom: *Cut planes*, *One plane, all kinds*, *Cut, live*, *Waterfall, live*.
