using PureHDF;
using PureHDF.Selections;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Hdf5;

/// <summary>
/// Turns a one-dimensional HDF5 dataset into a <see cref="ColumnFile"/>, reading it in blocks, so that datasets far
/// larger than memory convert once and then open at once. HDF5 stores its data in chunks, often compressed, which cannot
/// be mapped into memory as they are; the column file can.
/// </summary>
public static class Hdf5ColumnConverter
{
    /// <summary>The number of values read at once.</summary>
    public const int BlockValues = 1 << 20;

    /// <summary>Converts a dataset.</summary>
    /// <param name="hdf5Path">The HDF5 file.</param>
    /// <param name="datasetPath">The path of the dataset in the file, such as <c>/channel1</c>.</param>
    /// <param name="columnPath">The column file to write; one that is there is replaced.</param>
    /// <param name="xStart">The X of the first value.</param>
    /// <param name="xStep">The X step between two values.</param>
    /// <param name="progress">Receives the share converted, from 0 to 1.</param>
    /// <param name="cancellation">Stops the conversion; the column file is then incomplete.</param>
    /// <returns>The number of values and the type they are stored in.</returns>
    /// <exception cref="InvalidDataException">The dataset is not one-dimensional or holds no numbers.</exception>
    public static (long Count, ColumnType Type) Convert(string hdf5Path, string datasetPath, string columnPath, double xStart = 0, double xStep = 1, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(hdf5Path);
        ArgumentException.ThrowIfNullOrEmpty(datasetPath);
        ArgumentException.ThrowIfNullOrEmpty(columnPath);
        using var file = H5File.OpenRead(hdf5Path);
        var dataset = file.Dataset(datasetPath);
        var dimensions = dataset.Space.Dimensions;
        if (dimensions.Length != 1)
        {
            throw new InvalidDataException($"{datasetPath} in {hdf5Path} has {dimensions.Length} dimensions; a column has one.");
        }

        var count = (long)dimensions[0];
        var type = dataset.Type;
        var (typeClass, size, signed) = (type.Class, type.Size, type.Class == H5DataTypeClass.FixedPoint && type.FixedPoint.IsSigned);
        return (typeClass, size, signed) switch
        {
            (H5DataTypeClass.FloatingPoint, 4, _) => (Copy<float, float>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Single),
            (H5DataTypeClass.FloatingPoint, 8, _) => (Copy<double, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            (H5DataTypeClass.FixedPoint, 2, true) => (Copy<short, short>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Int16),
            (H5DataTypeClass.FixedPoint, 4, true) => (Copy<int, int>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Int32),
            (H5DataTypeClass.FixedPoint, 1, true) => (Copy<sbyte, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            (H5DataTypeClass.FixedPoint, 1, false) => (Copy<byte, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            (H5DataTypeClass.FixedPoint, 2, false) => (Copy<ushort, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            (H5DataTypeClass.FixedPoint, 4, false) => (Copy<uint, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            (H5DataTypeClass.FixedPoint, 8, true) => (Copy<long, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            (H5DataTypeClass.FixedPoint, 8, false) => (Copy<ulong, double>(dataset, count, columnPath, xStart, xStep, progress, cancellation), ColumnType.Double),
            _ => throw new InvalidDataException($"{datasetPath} in {hdf5Path} holds {typeClass} of {size} bytes, not numbers."),
        };
    }

    // Reads the dataset block by block in its own type and writes it in the type of the column.
    private static long Copy<TIn, TOut>(IH5Dataset dataset, long count, string columnPath, double xStart, double xStep, IProgress<double>? progress, CancellationToken cancellation)
        where TIn : unmanaged, System.Numerics.INumber<TIn>
        where TOut : unmanaged, System.Numerics.INumber<TOut>
    {
        using var writer = new ColumnFileWriter<TOut>(columnPath, 1, xStart, xStep);
        var converted = typeof(TIn) == typeof(TOut) ? null : new TOut[BlockValues];
        for (long start = 0; start < count; start += BlockValues)
        {
            cancellation.ThrowIfCancellationRequested();
            var length = (ulong)Math.Min(BlockValues, count - start);
            var block = dataset.Read<TIn[]>(fileSelection: new HyperslabSelection((ulong)start, length));
            if (converted is null)
            {
                writer.Append(System.Runtime.InteropServices.MemoryMarshal.Cast<TIn, TOut>(block.AsSpan()));
            }
            else
            {
                for (var i = 0; i < block.Length; i++)
                {
                    converted[i] = TOut.CreateTruncating(block[i]);
                }

                writer.Append(converted.AsSpan(0, block.Length));
            }

            progress?.Report((double)(start + (long)length) / count);
        }

        progress?.Report(1);
        return count;
    }
}
