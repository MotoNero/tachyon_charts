# Axes

```csharp
.XAxis(a => a.DateTime("time").Title("Clock time"))
.YAxis(a => a.Numeric("temp").Title("°C").Range(0, 100))
.YAxis(a => a.Log("pressure").Position(AxisPosition.Right).Limits(1, 1e6))
```

| Method | Meaning |
|---|---|
| `Numeric`, `DateTime`, `Log`, `Category` | Kind of axis |
| `Title` | Title |
| `Position` | Side |
| `Range` | Fixed range |
| `Limits` | Bounds for zoom and pan |
| `AutoRange` | The range follows the data |
| `Inverted` | Reversed direction |
| `Hidden` | No labels and no space |

Series choose their axis by name. Several Y axes are allowed. `LayoutPane` describes stacked panes.

For labels instead of numbers use `.Category(["Jan", "Feb"])` or `CategoryAxis`, with rotated labels and automatic thinning.

Log axes define what happens to values ≤ 0 (`NonPositiveValues`).
