# Getting started

You need .NET 10. Reference `Tachyon.Charts.Wpf` or `Tachyon.Charts.Avalonia` as a project.

## The shortest chart

```csharp
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Wpf; // or Tachyon.Charts.Avalonia

var data = new DataSource<double, double>();
for (var i = 0; i < 1000; i++)
{
    data.Append(i * 0.01, Math.Sin(i * 0.01));
}

var chart = new Chart().Configure(c => c
    .Title("Sine")
    .XAxis(a => a.Numeric("x").Title("Time in s").AutoRange())
    .YAxis(a => a.Numeric("y").Title("Value").AutoRange())
    .AddLine(data, s => s.Name("Signal").Thickness(2))
    .Modifiers(m => m.ZoomPan().RubberBand().Crosshair().Tooltip()));
```

Put `chart` into your window like any other control.

## The same in XAML (WPF)

```xml
<Window xmlns:t="urn:tachyon-charts">
  <t:Chart Title="Sine">
    <t:Chart.XAxes><t:NumericAxis Title="Time in s" IsAutoRange="True" /></t:Chart.XAxes>
    <t:Chart.YAxes><t:NumericAxis Title="Value" IsAutoRange="True" /></t:Chart.YAxes>
    <t:Chart.Series><t:LineSeries DataSource="{Binding Signal}" StrokeThickness="2" /></t:Chart.Series>
    <t:Chart.Modifiers><t:ZoomPanModifier /></t:Chart.Modifiers>
  </t:Chart>
</Window>
```

## Try it

```bash
dotnet run --project samples/Tachyon.Charts.Samples.Wpf -c Release
dotnet run --project samples/Tachyon.Charts.Samples.Avalonia -c Release
```

The sample apps list the interactive pages, the instruments and all gallery scenes on the left. Theme and language are at the top right and apply to every page. **Source code (F11)** opens the code of the page in a window of its own, a tab for each file, ready to copy. F12 shows the diagnostics on every page.

Continue with [Data](data.md).
