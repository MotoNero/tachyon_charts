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
- While the pyramid grows, the automatic range of the value axis widens with each piece read.

A plate in the lower left corner of the plot tells that the picture still refines, and goes when it is complete:

```csharp
chart.AddOverlay(new RefiningOverlay(chart) { Format = "Refining {0:P0}" });
```

With the fluent API: `.AddOverlay(surface => new RefiningOverlay(surface))`.

The showroom page „Large file“ shows a billion floats from a file of 4 GB. Its first visit writes the file once beside
the local data of Tachyon Charts (`%LOCALAPPDATA%\Tachyon\Samples` on Windows), in a few seconds; later visits only
open it. The configurator offers the same as the type „Line from a large file“, with the test file or a column file of
your own.

## Digital channels

Signals of two levels, such as logic channels, switches or bus lines, need far less than numbers. `DigitalSeries` draws
levels and edges: each pixel column asks only for its level and whether the level changes inside it, so no edge is lost,
not even of a single point, and narrow views draw every change.

```csharp
// Kept as the places where the level changes: a billion points with a thousand changes take a few kilobytes.
var clock = new DigitalTransitions(xStart: 0, xStep: 1e-9);
clock.Append(false, 500);            // points of one level
clock.AppendBits(words, bitCount);   // or packed bits, bit 0 first; safe while other threads draw

// Many channels one above the other: low and high at two values of the Y axis.
chart.AddSeries(new DigitalSeries(clock, xAxis, yAxis) { Low = 15 - 0.3, High = 15 + 0.3, Name = "CLK" });
```

Signals that change at almost every point keep one bit per point instead, in memory or in a column file of the type
`Bit`, which `BitFileWriter` writes:

```csharp
using (var writer = new BitFileWriter("bus.tcol", xStart: 0, xStep: 1e-9))
{
    writer.Append(true, 1000);
    writer.AppendBits(words, bitCount);
}

using var file = ColumnFile.Open("bus.tcol");
var bits = PackedBits.Open(file);    // or PackedBits.FromWords(words, count) for bits in memory
```

An index of one bit per 4096 points tells where the level changes; it is built on all cores in the background (a file of
12.5 GB, a hundred billion points, in under two seconds from the cache). Until it is complete, narrow views are exact and
wide views show what is indexed so far, with the `RefiningOverlay`.

The showroom page „Digital channels“ shows sixteen channels of a logic analyzer, a billion points each; the
configurator offers the type „Digital channels“.

## From CSV

```csharp
var summary = ColumnFileConverter.FromCsv("table.csv", "table.tcol",
    new CsvColumns(new CsvFormat(';', DecimalComma: true), XColumn: 0),
    new Progress<double>(share => Console.WriteLine($"{share:P0}")));
```

The CSV file is read in pieces and parsed on all cores, never held in memory as a whole: about 1.2 to 1.4 GB of CSV per
second, a 5 GB file in about 4 seconds. It must be UTF-8 or ASCII with one row per line. The
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
