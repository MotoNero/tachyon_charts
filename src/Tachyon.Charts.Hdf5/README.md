# Tachyon.Charts.Hdf5

Converts a one-dimensional HDF5 dataset into a column file of Tachyon Charts, block by block, so that a dataset of any
size opens at once afterwards and draws through a pyramid of its extremes.

```csharp
Hdf5ColumnConverter.Convert("measurement.h5", "/channel1", "channel1.tcol", xStart: 0, xStep: 1e-6);
using var file = ColumnFile.Open("channel1.tcol");
```

Floats, doubles and 16- and 32-bit integers keep their type; other number types become doubles.
