# Pie and donut

```csharp
chart.Configure(c => c.Title("Power mix").Pie(
    [32, 18, 15, 14, 9, 6, 3, 1.5, 1, 0.5],
    ["Wind", "Solar", "Gas", "Coal", "Biomass", "Hydro", "Nuclear", "Oil", "Other", "Geothermal"]));
```

The slices run clockwise from the top. Values that are zero, negative, `NaN` or infinite get no slice and count for nothing in the sum; a single value is a full circle.

Set further properties in the third argument:

```csharp
c.Pie(values, names, pie =>
{
    pie.InnerRadius = 0.55;           // a donut; 0 is a pie
    pie.ExplodedIndex = 1;            // pull the second slice out
    pie.Labels = PieLabels.Percent;   // None, Name, Percent, Value or NameAndPercent
    pie.Colors = [new Rgba8(0, 114, 178, 255), new Rgba8(213, 94, 0, 255)];
});
```

Without `Colors` the slices take the palette of the theme. Labels sit inside slices that are wide enough. Small slices get their label outside on a line; the labels of each side keep at least one line apart, so they never overlap.

Under the pointer a slice lights up and shows its name, value and share. A click pulls it out, a second click puts it back. `SetValues` replaces the values from any thread, for live data.

## In 3D

With the 3D add-on, a pie or donut becomes a lit body that the user can turn:

```csharp
var pie = new Pie3D(values, names) { ExplodedIndex = 1 };          // InnerRadius = 0.55 for a donut
var chart = new Chart3D().Configure(c => c.Title("Power mix").AddPie(pie));
```

`AddPie` puts the pie in the middle of the box, hides the axes and moves the view closer. Slices run clockwise from the top as in 2D; zero, negative and missing values get no slice. The pointer shows name, value and share of a slice, and a click pulls it out or back. `Height`, `Gap`, `ExplodeDistance`, `Colors` and `Material` set the look; without `Colors` the slices take the palette of the theme.

In the showroom: *Pie*, *Donut*, *Pie 3D*, *Donut 3D*.
