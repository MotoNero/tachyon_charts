# Maps

Maps without the internet: the countries of the world inside the package `Tachyon.Charts.Geo`, map projections, layers
of data on top, and a reader of ESRI shapefiles for maps of your own.

## A world map

```csharp
chart.Configure(c => c
    .Map(GeoProjection.EqualEarth, map =>
    {
        map.SetColors(colorsByCountryCode);   // ISO 3166 alpha-3 codes, such as "DEU"
        map.Sea = new Rgba8(196, 216, 232, 255);
    })
    .Modifiers(m => m.ZoomPan().RubberBand()));
```

`Map` sets X and Y axes over the world on the plane of the projection, hidden, and adds the countries. The map keeps
one scale on both axes, so shapes are not stretched however the chart is sized.

The countries come from [Natural Earth](https://www.naturalearthdata.com) 5.1.1, which is in the public domain, in three
levels of detail: 1:110 million for the whole world, 1:50 million for continents, 1:10 million for countries and
regions. The map takes the finer level as the view comes closer and draws only the countries in view, so it stays smooth
from the world down to a city. Each region carries the name and the code (ADM0_A3) of its country; `WorldMap.Countries`
gives one level, `RegionAt(longitude, latitude)` the country under a place.

## Projections

| Projection | Use |
|---|---|
| `GeoProjection.EqualEarth` | The whole world; areas keep their sizes against each other |
| `GeoProjection.WebMercator` | Like web maps and their tiles; up to 85° north and south |
| `GeoProjection.Equirectangular` | Longitude and latitude as they are; small areas and data grids |

`Project` turns longitude and latitude into X and Y, `TryUnproject` turns them back.

## Layers of data

Points, lines, areas and heatmaps go on the same axes as the map; only their places are projected first:

```csharp
var (x, y) = GeoProjection.EqualEarth.Project(longitudes, latitudes);
var cities = new DataSource<double, double>(x.Length);
cities.Append(x, y);
chart.Configure(c => c
    .Map(GeoProjection.EqualEarth)
    .AddScatter(cities, s => s.Name("Cities").Size(6)));
```

Countries colored by a value are a map with `SetColors`; areas of your own are polygons of projected corners.

## Shapefiles

```csharp
var file = ShapefileReader.Read("districts.shp");   // the .dbf beside it gives the values of each record
var districts = GeoMap.FromShapefile(file, nameField: "NAME", codeField: "ID");
chart.Configure(c => c.Map(GeoProjection.Equirectangular, levels: [districts]));
```

The reader takes points, lines, areas and groups of points, also with Z or M values, of which it keeps X and Y. A
damaged file fails with an `InvalidDataException` that names the file, the record and the byte, such as
`districts.shp has a record 12 of 96 bytes that runs past the end (at byte 4180)`. Coordinates must be degrees of
longitude and latitude; the reader does not reproject.

`GeoMap.Save` keeps a map in a compact packed form, its areas already cut into triangles; `GeoMap.Load` reads it back.
