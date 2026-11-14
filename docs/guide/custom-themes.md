# Your own theme

A theme is a set of colors and a few numbers: background, plot area, grid, axes, text size, the palette of the
instruments and the glow of lines. You write only what differs from a **base theme**; everything else comes from it.
The same theme can be written as JSON or as XAML resources, in WPF and in Avalonia, and it can come from a file next
to the program, from a resource inside it or from the user's folder.

The showroom has a page "Own theme": its chart uses `harbor.json`, and the list of themes holds "Meadow" from
`meadow.xaml`. F11 shows both files. In the configurator, choose **Own** under Theme to set the values with a color
picker and get the code.

## Start from a built-in theme

Write a built-in theme out, change what you like and load it again:

```csharp
File.WriteAllText("my-theme.json", ThemeJson.Serialize(BuiltInThemes.TachyonDark));
```

For XAML, `ThemeResources.ToValues` gives every key with its value:

```csharp
foreach (var (key, value) in ThemeResources.ToValues(BuiltInThemes.TachyonLight))
{
    Console.WriteLine($"{key} = {value}");
}
```

Remove what you do not change and name a base instead; later changes of the base then reach your theme too.

## JSON

```json
{
  "name": "Harbor",
  "base": "dark",
  "surface": {
    "background": "#0B0E16",
    "plotArea": "linear(#202D42, #0C121F)",
    "border": "#2C3A52"
  },
  "grid": {
    "major": "#AABEDC48",
    "minor": "#AABEDC1C",
    "band": "#6EA0DC14"
  },
  "axis": {
    "line": "#4A5A74",
    "tick": "#4A5A74",
    "label": "#A9B8CF",
    "title": "#E3EAF5"
  },
  "palette": ["#5CC8FF", "#FFB454", "#7EE0A1", "#FF7A90"]
}
```

- `name` is shown in lists. `base` names the theme the missing values come from: `dark`, `light` or the name of a
  built-in theme such as `Paper`, `Neon` or `High Contrast`.
- The groups and names are those of the table below: `Tachyon.grid.band` is `"grid": { "band": … }`. Glow sits in
  `"effects": { "glow": { … } }`, the series opacity in `"effects": { "seriesOpacity": … }`.
- Colors are `#RRGGBB` or `#RRGGBBAA`, the alpha **last**: `#6EA0DC14` is a light blue at 8 % opacity.
- `plotArea` is one color or `linear(top, bottom)`, a vertical gradient.
- `palette` is a list of colors.

Load it and give it to a chart:

```csharp
var theme = ThemeJson.Deserialize(File.ReadAllText(path));
chart.Theme = theme;              // WPF
chart.ChartTheme = theme;         // Avalonia
c.Theme(theme);                   // in the fluent description
```

The chart resolves the base itself. To read the values first, call `theme.Resolve()`.

A misspelled property stops loading with a message that names it, such as `bands`. An unknown base stops it too,
with the name of the base.

## XAML in WPF

A resource dictionary of its own, `Themes/Meadow.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=mscorlib">
  <Color x:Key="Tachyon.surface.background">#F6F3EA</Color>
  <LinearGradientBrush x:Key="Tachyon.surface.plotArea" StartPoint="0,0" EndPoint="0,1">
    <GradientStop Color="#FFFDF7" Offset="0" />
    <GradientStop Color="#EEF3E4" Offset="1" />
  </LinearGradientBrush>
  <Color x:Key="Tachyon.surface.border">#C9CDB8</Color>
  <Color x:Key="Tachyon.grid.major">#383C5030</Color>
  <Color x:Key="Tachyon.grid.minor">#183C5030</Color>
  <Color x:Key="Tachyon.grid.band">#1C6E9A50</Color>
  <Color x:Key="Tachyon.axis.tick">#8A927A</Color>
  <Color x:Key="Tachyon.axis.label">#555E48</Color>
  <Color x:Key="Tachyon.axis.title">#2E3526</Color>
  <sys:String x:Key="Tachyon.palette">#2E7D32, #C2185B, #1565C0, #EF6C00</sys:String>
  <sys:Double x:Key="Tachyon.text.size">13</sys:Double>
</ResourceDictionary>
```

Merge it in `App.xaml`:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceDictionary Source="Themes/Meadow.xaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

Read it into a theme, with its name and base:

```csharp
var meadow = ChartThemes.FromResources(Application.Current.Resources, "Meadow", "light");
```

`FromResources` reads the dictionary and the dictionaries merged into it; a value of the dictionary itself wins over a
merged one. A dictionary can also be loaded on its own:

```csharp
var resources = new ResourceDictionary { Source = new Uri("/MyApp;component/Themes/Meadow.xaml", UriKind.Relative) };
var meadow = ChartThemes.FromResources(resources, "Meadow", "light");
```

- Every key starts with `Tachyon.`. Other keys of the dictionary are left alone.
- `Color` and `SolidColorBrush` give colors. XAML writes them as `#AARRGGBB`, the alpha **first**: `#1C6E9A50` is a
  green at 11 % opacity.
- A `LinearGradientBrush` gives the plot area its first and last color.
- `sys:Double` gives numbers, `sys:String` gives text. The palette is a `sys:String` with colors separated by commas,
  written as in JSON with the alpha last.
- A key that starts with `Tachyon.` but is not in the table stops loading with a message that names it and lists the
  known keys. So does a value that cannot be read.

## XAML in Avalonia

The same dictionary as `Themes/Meadow.axaml`, with the namespace of Avalonia and relative gradient points:

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=netstandard">
  <Color x:Key="Tachyon.surface.background">#F6F3EA</Color>
  <LinearGradientBrush x:Key="Tachyon.surface.plotArea" StartPoint="0%,0%" EndPoint="0%,100%">
    <GradientStop Color="#FFFDF7" Offset="0" />
    <GradientStop Color="#EEF3E4" Offset="1" />
  </LinearGradientBrush>
  ...
</ResourceDictionary>
```

Include it in `App.axaml`:

```xml
<Application.Resources>
  <ResourceDictionary>
    <ResourceDictionary.MergedDictionaries>
      <ResourceInclude Source="avares://MyApp/Themes/Meadow.axaml" />
    </ResourceDictionary.MergedDictionaries>
  </ResourceDictionary>
</Application.Resources>
```

and read it as in WPF:

```csharp
var meadow = ChartThemes.FromResources(Application.Current!.Resources, "Meadow", "light");
```

Or load the dictionary on its own; it must be an `AvaloniaResource` of the project:

```csharp
var resources = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri("avares://MyApp/Themes/Meadow.axaml"));
var meadow = ChartThemes.FromResources(resources, "Meadow", "light");
```

Avalonia compiles XAML with the program. A theme that users bring along as a file is therefore best a JSON file.

## Where the files go

**Next to the program.** In the project file:

```xml
<ItemGroup>
  <None Include="Themes/harbor.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

```csharp
var path = Path.Combine(AppContext.BaseDirectory, "Themes", "harbor.json");
var theme = ThemeJson.Deserialize(File.ReadAllText(path));
```

**Inside the program**, so that it cannot get lost:

```xml
<ItemGroup>
  <EmbeddedResource Include="Themes/harbor.json" LogicalName="harbor.json" />
</ItemGroup>
```

```csharp
using var stream = typeof(App).Assembly.GetManifestResourceStream("harbor.json")!;
using var reader = new StreamReader(stream);
var theme = ThemeJson.Deserialize(reader.ReadToEnd());
```

**In the user's folder**, for themes the user changes or adds:

```csharp
var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MyApp", "Themes");
var themes = Directory.Exists(folder)
    ? Directory.EnumerateFiles(folder, "*.json").Select(file => ThemeJson.Deserialize(File.ReadAllText(file))).ToList()
    : [];
```

XAML dictionaries go into the program as shown above: as `Page` in WPF, the default for `.xaml` files, and as
`AvaloniaResource` in Avalonia.

## Switching at run time

Set the theme again; with a transition the colors fade over:

```csharp
chart.ThemeTransition = TimeSpan.FromMilliseconds(400);
chart.Theme = meadow;             // WPF; in Avalonia chart.ChartTheme = meadow
```

For all charts of a window, keep them in a list or walk the window:

```csharp
foreach (var chart in FindCharts(window))
{
    chart.Theme = meadow;
}

static IEnumerable<Chart> FindCharts(DependencyObject parent)
{
    foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
    {
        if (child is Chart chart)
        {
            yield return chart;
        }

        foreach (var inner in FindCharts(child))
        {
            yield return inner;
        }
    }
}
```

In Avalonia, `window.GetVisualDescendants().OfType<Chart>()` from `Avalonia.VisualTree` finds them.

## All keys

The keys of XAML; in JSON the part after `Tachyon.` is the group and the name. The last two columns are the values of
`Tachyon Dark` and `Tachyon Light`, the bases `dark` and `light`. A few values are kept in a theme but not used for
drawing yet; the table says so.

| Key | What it sets | Dark | Light |
| --- | --- | --- | --- |
| `Tachyon.surface.background` | Background of the whole chart. | `#0E1117` | `#F4F6FA` |
| `Tachyon.surface.plotArea` | Fill of the plot area: one color, or linear(top, bottom) for a vertical gradient. | `linear(#141922, #0E1117)` | `linear(#FFFFFF, #EDF1F7)` |
| `Tachyon.surface.border` | Border around the plot area. | `#232A36` | `#C6CDD8` |
| `Tachyon.grid.major` | Major grid lines. | `#FFFFFF14` | `#26364A24` |
| `Tachyon.grid.minor` | Minor grid lines. | `#FFFFFF08` | `#26364A10` |
| `Tachyon.grid.band` | Every second band between major grid lines; fully transparent for none. | `#FFFFFF04` | `#4C78A80A` |
| `Tachyon.axis.line` | Lines of the 3D box; 2D charts draw their axes with the tick color and the border. | `#3A4352` | `#7B8798` |
| `Tachyon.axis.tick` | Tick marks. | `#3A4352` | `#7B8798` |
| `Tachyon.axis.label` | Tick labels. | `#9AA4B2` | `#526071` |
| `Tachyon.axis.title` | Axis titles and the chart title. | `#C9D1D9` | `#202936` |
| `Tachyon.text.fontFamily` | Preferred fonts, separated by commas; not used for drawing yet. | `Roboto, Segoe UI, SF Pro, sans-serif` | `Roboto, Segoe UI, SF Pro, sans-serif` |
| `Tachyon.text.size` | Size of tick labels in device-independent pixels; titles are 3 larger. | `12` | `12` |
| `Tachyon.palette` | Colors of instrument traces, of the series of parallel coordinates, ternary and Smith charts and of 3D series without a color of their own, separated by commas. | `#4CC9F0, #F72585, #7CFC9A, #FFB703, #B388FF, #FF6B6B` | `#0077B6, #D81B60, #218739, #C77D00, #7048B8, #D64545` |
| `Tachyon.colormap` | Name of a colormap; not used for drawing yet, each series sets its own. | `turbo` | `turbo` |
| `Tachyon.crosshair.line` | Crosshair lines; not used for drawing yet, the crosshair has its own colors. | `#FFFFFF66` | `#20293666` |
| `Tachyon.crosshair.labelBackground` | Background of the crosshair labels; not used for drawing yet. | `#4CC9F0` | `#0077B6` |
| `Tachyon.crosshair.labelText` | Text of the crosshair labels; not used for drawing yet. | `#0E1117` | `#FFFFFF` |
| `Tachyon.glow.enabled` | Whether lines glow: true or false. | `false` | `false` |
| `Tachyon.glow.radius` | Radius of the glow in pixels. | `6` | `6` |
| `Tachyon.glow.intensity` | Strength of the glow, 0 to 1. | `0.6` | `0.4` |
| `Tachyon.effects.seriesOpacity` | Opacity of all series, 0 to 1; not used for drawing yet. | `1` | `1` |
| `Tachyon.animation.durationMs` | Duration of animations in milliseconds; not used yet, each animation sets its own. | `250` | `250` |
| `Tachyon.animation.easing` | Easing of animations, such as cubicOut or linear; not used yet. | `cubicOut` | `cubicOut` |

`ThemeResources.Keys` lists the same keys with their meaning, for tools of your own.
