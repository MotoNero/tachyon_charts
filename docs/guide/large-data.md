# Large data

Charts of files far larger than memory: a 21 GB file of 2.7 billion values shows a first picture after a quarter of a
second, is complete after a few seconds, and pans at several hundred frames per second. The data stay in the file; the
chart keeps only a pyramid of their extremes, about 1 % of the file.

## Column files

A column file holds numbers at an even X step, in one or several columns. It is mapped into memory: the operating
system reads what a view needs and keeps it in its cache.

```csharp
// Writing: values as they come, without holding them.
using (var writer = new ColumnFileWriter<double>("pressure.tcol", columns: 1, xStart: 0, xStep: 1e-6))
{
    writer.Append(firstBlock);
    writer.Append(nextBlock);
}

// Reading: a span straight into the file, as many values as lie together.
using var file = ColumnFile.Open("pressure.tcol");
var column = file.Column<double>();
ReadOnlySpan<double> values = column.Read(start: 1_000_000_000, maxLength: 4096);
```

Columns hold `double`, `float`, `short` or `int`. There is no limit of 2^31 values.

## Drawing

The pyramid holds the lowest and highest value of every block of 256 values and of ever larger blocks above. It is built
on all cores in the background and kept beside the data, so the next opening only reads it.

```csharp
var pyramid = ColumnPyramid<double>.Build(column, "pressure.tcol.tpyr");
chart.AddSeries(new ColumnLineSeries<double>(column, pyramid, xAxis, yAxis) { Color = new Rgba8(80, 170, 240, 255) });
```

- While the pyramid grows, the line draws a coarse picture of what is read so far. The pieces are read spread over the
  whole file, and the picture refines with each one. `pyramid.Progress` tells how far it is.
- Each pixel column shows the lowest and the highest value of its range, so no outlier is lost, however far out the
  view is.
- Narrow views draw the values themselves; the parts beside them are read ahead while panning.
- A pyramid file of other or changed data is not taken: it carries a fingerprint of the values.

## From CSV

```csharp
var summary = ColumnFileConverter.FromCsv("table.csv", "table.tcol",
    new CsvColumns(new CsvFormat(';', DecimalComma: true), XColumn: 0),
    new Progress<double>(share => Console.WriteLine($"{share:P0}")));
```

The CSV file is read as a stream, row by row, and never held in memory; about 170 MB of CSV per second on one core. The
X column must step evenly, as column files keep X as start and step; without one the rows are numbered. Values that are
no numbers become gaps. `summary.Names` gives the names of the columns from the header.

## From HDF5

The package `Tachyon.Charts.Hdf5` converts a one-dimensional dataset block by block. HDF5 stores data in chunks, often
compressed, which cannot be mapped as they are; the column file can.

```csharp
Hdf5ColumnConverter.Convert("measurement.h5", "/channel1", "channel1.tcol", xStart: 0, xStep: 1e-6);
```

Floats, doubles and 16- and 32-bit integers keep their type; other number types become doubles.

## The format

All numbers are little-endian. The file begins with a header of 64 bytes:

| Offset | Type | Content |
|---|---|---|
| 0 | 4 bytes | `TCOL` |
| 4 | int32 | Version, 1 |
| 8 | int32 | Type: 1 double, 2 float, 3 int16, 4 int32 |
| 12 | int32 | Number of columns |
| 16 | int64 | Number of rows |
| 24 | double | X of the first row |
| 32 | double | X step |
| 40 | int64 | Rows per block; 0 for one block of all rows (one column) |
| 48 | 16 bytes | Zero |

Then the blocks: each holds its rows of the first column, then of the second and so on. Every block but the last has its
full rows, the last as many as are left.
