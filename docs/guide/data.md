# Data

All data stays on the CPU as `double` or `float`. The GPU only receives values relative to an origin, so even timestamps stay exact.

| Type | Use |
|---|---|
| `DataSource<TX, TY>` | The normal case: grows with `Append`, from any thread |
| `ExternalDataSource.Create(x, y, true)` | Your own arrays without a copy, the last value says "sorted" |
| `FifoDataSource<TX, TY>` | Streaming with a fixed capacity, old points drop out |
| `ItemsDataSource` | Objects from a list, for MVVM and small data |

## Streaming

```csharp
var ring = new FifoDataSource<double, float>(1_000_000, singleProducer: true);
ring.Append(xBlock, yBlock); // from your measuring thread
```

Write from any thread. Drawing reads a fixed state and does not block you. Blocks are faster than single points.

## Lists and MVVM

```xml
<t:LineSeries ItemsSource="{Binding Readings}" XPath="Time" YPath="Value" />
```

Paths with a dot reach nested properties. New items at the end are cheap, any other change reads the whole list again. Above 100,000 items the diagnostics overlay warns you: use a `DataSource` then.

## Missing values

`NaN` interrupts a line. That is the way to show gaps in a measurement.

## CSV files

```csharp
var table = CsvTable.Read("measurements.csv", new CsvFormat(';', HasHeader: true, DecimalComma: true));
var data = new DataSource<double, double>(table.RowCount);
data.Append(table.Column("time"), table.Column("pressure"));
```

`CsvTable` reads the columns of a CSV file as numbers: the delimiter, whether the first line names the columns, and whether numbers use a decimal comma. Values in quotes may hold the delimiter. A value that is no number, and one missing at the end of a short line, reads as NaN, which a chart shows as a gap. Columns are found by name or by number from 0.

