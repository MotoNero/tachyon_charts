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

## Recording for hours

`ColumnRecorder<T>` writes live values into column files on the disk as they arrive, one file after another. With a
limit of files it is a ring: the oldest file leaves as a new one comes, and the disk holds a fixed share of the latest
values. Without a limit the record grows and every file stays. Each file is a column file of its own that opens later
with `ColumnFile.Open`.

```csharp
using var recorder = new ColumnRecorder<float>(
    folder, "pressure",
    xStart: 0, xStep: 1e-7,         // ten million values per second
    maxFiles: 20,                   // a ring of 20 files; 0 keeps all
    fileValues: 1 << 26);           // 64 million values per file
chart.AddSeries(new ColumnLineSeries<float>(recorder, recorder, xAxis, yAxis));

// On the thread of the measurement:
recorder.Append(chunk);
```

- The recorder is the column and its pyramid at once: the extremes of each block of 256 values, and of ever larger
  blocks, grow as the values come, so the line draws its whole span at once, also looking back over hours.
- One thread appends, any thread reads. Appending in chunks of thousands of values is much cheaper than single values.
- The memory stays the same for hours: the values live in the files, the process keeps only the extremes of the blocks,
  about 1 % of the size of the values.
- In a ring, `XStart` moves on as old values leave; an axis with an automatic range rolls with the record. `Recorded`
  counts all values since the start, `Count` those still on the disk.
- `Dispose` writes the number of values into the last file; all files stay on the disk.

The showroom page „Recording“ records ten million values per second into a ring of 17 files, about 1.1 billion values
and 4.6 GB beside the local data of Tachyon Charts, and pauses while the page is not shown. The configurator offers it
as the sample „A recording of 10 million values per second into a ring on the disk“ of the type „Line from a large
file“.

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

## Scatter from files

A scatter of a billion points shows as a density: each pixel tells how many points fall into it. The points come from
two columns of a column file, X and Y, and are sorted once into tiles beside it, about 8 bytes per point.

```csharp
using var file = ColumnFile.Open("cloud.tcol");    // X in the first column, Y in the second
var tiles = PointTiles.Build(file.Column<float>(0), file.Column<float>(1), "cloud.tcol.tpts");

chart.AddPointDensity(tiles, s => s.Name("Points").ColorAxis(a => a.Colormap(Colormaps.Inferno).Log()))
     .AddOverlay(surface => new RefiningOverlay(surface));
```

- The tiles are built on all cores in the background (a billion points in about 16 seconds) and taken again next time
  when they belong to the same points.
- Wide views add up a grid of 4096 × 4096 counts, each cell shared out over the pixels it covers.
- Narrow views count the points of the visible tiles, at most `PointBudget` per frame (4 million by default): a dense
  tile gives a fair share of its points, weighted to its full count, a sparse one all of them, so that a single point
  far out stays visible. Deep views read every point and are exact.

The showroom page „Scatter from a file“ shows 200 million points; the configurator offers the type „Scatter from a file“,
with the test file or a column file of your own.

## Heatmaps and spectrograms from files

A raster file holds a grid of floats row by row, far larger than memory: a heatmap of 64k × 64k cells, or a spectrogram
of millions of rows. Its stages are built once beside it; each halves the directions longer than 1024 cells that are
not far shorter than the other, so a long spectrogram keeps all its bins.

```csharp
using (var writer = new RasterFileWriter("field.tras", width: 65_536, xStart: 0, xStep: 1, yStart: 0, yStep: 1))
{
    writer.Append(row);              // a whole number of rows at a time
}

var raster = RasterFile.Open("field.tras");
var stages = RasterPyramid.Build(raster, "field.tras.tmip", RasterReduction.Maximum);
chart.AddFileRaster(stages, s => s.ColorAxis(a => a.Colormap(Colormaps.Turbo)));
```

- `RasterReduction.Mean` shows smooth fields; `Maximum` keeps a single hot cell or a narrow peak of a spectrogram
  visible however far out the view is.
- Each frame reads the stage with half a cell or more per pixel, and of it only the cells in view.
- `RowsAlongX()` lets the rows of the file run along the X axis, as the rows of a spectrogram written over time.
- The stages hold about a third of a square raster, about as much again for a long spectrogram.

The format: a header of 64 bytes (`TRAS`, version 1, the width as int32 at offset 8, the height as int64 at offset 16,
then the left edge and width of a column and the lower edge and height of a row as doubles at 24, 32, 40 and 48), then
the rows as little-endian floats.

The showroom page „Heatmap from a file“ shows 16384 × 16384 cells; the configurator offers the type „Heatmap from a
file“, with the test raster or a raster file of your own.

## 3D point clouds from files

A point cloud of a billion points does not fit onto a graphics card. Its points come from three columns of a column
file, X, Y and Z, and are sorted once into pages beside it, about 12 bytes per point: each page a small cube of space
of at most 32,768 points, in a random order.

```csharp
using var file = ColumnFile.Open("scan.tcol");    // X, Y and Z in the first three columns
var pages = CloudPages.Build(file.Column<float>(0), file.Column<float>(1), file.Column<float>(2), "scan.tcol.tcld");

chart3D.AddCloud(new PointCloud3D(pages) { HeightColors = Colormaps.Viridis, PointSize = 2 });
```

- A sample of 64 points of every page stays on the GPU, so the whole cloud shows at once; the pages a view needs finer
  come into a cache on the GPU (`PageMemory`, 1 GB by default for 64 million points), the ones longest unused leave first.
- Each frame takes as many points of each page as its size on screen asks for, within `PointBudget`.
- `HeightColors` colors the points by their height; without it they take `Color`.

The showroom page „Cloud from a file“ shows 200 million points; the configurator offers the type „Point cloud from a
file“.

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
