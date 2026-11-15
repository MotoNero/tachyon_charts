# Tachyon.Charts.Icons

Material Symbols (Outlined, version 2.978, Apache-2.0) as signs of annotations in Tachyon Charts. Each symbol is a glyph of
the font and goes through the glyph atlas of the charts: sharp at every size, in any color.

```csharp
chart.AddOverlay(new HorizontalLineAnnotation(y)
{
    Y = 80,
    Label = "Too hot",
    Appearance = new AnnotationAppearance { LabelSymbol = MaterialSymbols.Symbol("thermostat"), LabelIconColor = new Rgba8(240, 90, 90, 255) },
});
```

`MaterialSymbols.Names` lists all 4 304 names. Without this package the built-in signs of `LabelIcon` remain.
