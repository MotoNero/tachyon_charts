# Accessibility

A chart reports to screen readers and test tools through UI Automation (WPF) and automation peers (Avalonia):

| Property | Content |
|---|---|
| Name | The title of the chart, or `AutomationProperties.Name` if you set one |
| Kind | Image (`Image`) |
| Help text | The axes with their ranges and the number of series |

```xml
<t:Chart AutomationProperties.Name="Temperature of the last hour" />
```

`HighContrast` is a built-in theme for poor viewing conditions.
