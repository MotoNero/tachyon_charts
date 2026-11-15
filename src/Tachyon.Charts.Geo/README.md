# Tachyon.Charts.Geo

Maps without the internet: the countries of the world from Natural Earth (public domain) in three levels of detail,
map projections, and a reader of ESRI shapefiles.

```csharp
chart.Configure(c => c
    .Map(GeoProjection.EqualEarth, map => map.SetColors(colorsByCountryCode))
    .AddScatter(cities, s => s.Size(6)));               // places projected with GeoProjection.Project

var file = ShapefileReader.Read("districts.shp");      // the .dbf beside it gives the names
var districts = GeoMap.FromShapefile(file, nameField: "NAME", codeField: "ID");
```
