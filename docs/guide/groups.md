# Groups and several charts

Charts with the same group name stay in step, for zooming and for the crosshair:

```csharp
.Group("measurement")
```

In XAML this is `GroupName`. An `OverviewModifier` shows the visible part of another chart in a second chart.

All charts share one render thread and one graphics device.
