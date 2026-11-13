# Models from STL and OBJ

A model from a CAD program or a scanner shows in the 3D chart like any other surface: lit, cut open by cut planes, with values under the pointer. Put measured values on it, and the model becomes the chart: the temperature of a part, the deviation of a scanned piece from its drawing, the load on a bracket.

```csharp
using Tachyon.Charts.ThreeD.Files;

var model = ModelFile.Read("bracket.stl");          // STL as text or binary, or OBJ
chart.Configure(c => model.AddToScale(c, model.ToMesh(new Rgba8(190, 196, 204, 255))));
```

`AddToScale` gives all three axes the length of the longest side, centered on the model, so that it keeps its proportions. The axes take the coordinates of the file as they are; a model whose height runs along y shows lying on its side.

## Values on the model

`ToMesh` with one value per point colors the model and shows the value under the pointer, blended from the corners of the triangle:

```csharp
var values = new double[model.Positions.Length];
for (var i = 0; i < values.Length; i++)
{
    values[i] = temperature(model.Positions[i]);   // from a simulation, or spread from sensors
}

var mesh = model.ToMesh(values, Colormaps.Turbo, new DoubleRange(55, 115), null, "°C");
```

The color scale shows beside the box, with the range at its ends and the title above. A model whose file already holds colors from a colormap takes the scale along: `model.ToMesh(null, material, new ColorScale3D(Colormaps.Turbo, new DoubleRange(55, 115), "°C"))`. `Mesh3D.Values` and `Mesh3D.ColorScale` do the same for a mesh you build yourself. Measuring points next to the model are a `Scatter3D`; a `ClipPlane3D` cuts the model open and shows the wall inside.

## What the readers do

- **STL:** text and binary, told apart by the length of the file. Equal corners of neighboring triangles are joined.
- **OBJ:** points (`v`, optionally with red, green and blue from 0 to 1) and faces (`f`, also with texture and normal numbers and counting back with negative numbers). Faces with more than three corners become a fan of triangles. Materials, textures and groups are passed over.
- **Light:** the chart calculates the direction of the surface itself. Where faces meet at more than 40°, the edge stays sharp; below, the light runs smoothly over it.
- **Errors:** a damaged file throws a `PointFileException` that names the line or the triangle, for example a file that ends after 10 of 12 triangles or a face that uses a point the file does not hold.

## Size

A model of a million triangles turns at the frame rate of the screen. Reading it and building the tree that finds the triangle under the pointer happens once; the tree is built in the background, so the chart shows the model before the pointer finds values on it.

In the showroom: *Model with measuring points*, *Model of 1 million triangles*.
