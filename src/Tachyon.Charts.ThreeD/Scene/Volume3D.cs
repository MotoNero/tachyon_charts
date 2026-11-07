using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>Values on a raster in space. The arrays stay with the application, there is no copy.</summary>
public sealed class VolumeData
{
    private readonly byte[]? _bytes;
    private readonly ushort[]? _words;
    private readonly float[]? _floats;
    private readonly Lazy<DoubleRange> _range;

    /// <summary>Initializes a new instance of the <see cref="VolumeData"/> class with 8 bits per value.</summary>
    /// <param name="values">The values, row by row and slice by slice.</param>
    /// <param name="width">The number of values along the first axis.</param>
    /// <param name="height">The number along the second axis.</param>
    /// <param name="depth">The number along the third axis.</param>
    public VolumeData(byte[] values, int width, int height, int depth)
        : this(width, height, depth, (values ?? throw new ArgumentNullException(nameof(values))).Length)
    {
        _bytes = values;
    }

    /// <summary>Initializes a new instance of the <see cref="VolumeData"/> class with 16 bits per value.</summary>
    /// <param name="values">The values, row by row and slice by slice.</param>
    /// <param name="width">The number of values along the first axis.</param>
    /// <param name="height">The number along the second axis.</param>
    /// <param name="depth">The number along the third axis.</param>
    public VolumeData(ushort[] values, int width, int height, int depth)
        : this(width, height, depth, (values ?? throw new ArgumentNullException(nameof(values))).Length)
    {
        _words = values;
    }

    /// <summary>Initializes a new instance of the <see cref="VolumeData"/> class with floating point values.</summary>
    /// <param name="values">The values, row by row and slice by slice. NaN counts as the lowest value.</param>
    /// <param name="width">The number of values along the first axis.</param>
    /// <param name="height">The number along the second axis.</param>
    /// <param name="depth">The number along the third axis.</param>
    public VolumeData(float[] values, int width, int height, int depth)
        : this(width, height, depth, (values ?? throw new ArgumentNullException(nameof(values))).Length)
    {
        _floats = values;
    }

    private VolumeData(int width, int height, int depth, int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 2);
        ArgumentOutOfRangeException.ThrowIfLessThan(depth, 2);
        if ((long)width * height * depth != length)
        {
            throw new ArgumentException($"The sizes ask for {(long)width * height * depth} values, the list holds {length}.");
        }

        (Width, Height, Depth) = (width, height, depth);
        _range = new Lazy<DoubleRange>(FindRange);
    }

    /// <summary>Gets the number of values along the first axis.</summary>
    public int Width { get; }

    /// <summary>Gets the number of values along the second axis.</summary>
    public int Height { get; }

    /// <summary>Gets the number of values along the third axis.</summary>
    public int Depth { get; }

    /// <summary>Gets the smallest and largest value. The first access walks through all values.</summary>
    public DoubleRange ValueRange => _range.Value;

    /// <summary>Gets a value indicating whether the GPU stores 8 bits per value.</summary>
    internal bool IsBytes => _bytes is not null;

    /// <summary>Gets the number of values.</summary>
    internal long Count => (long)Width * Height * Depth;

    /// <summary>Gets a value.</summary>
    /// <param name="x">The place along the first axis.</param>
    /// <param name="y">The place along the second axis.</param>
    /// <param name="z">The place along the third axis.</param>
    /// <returns>The value.</returns>
    public double this[int x, int y, int z] => Read(((long)z * Width * Height) + ((long)y * Width) + x);

    /// <summary>Gets what a volume needs on the graphics card.</summary>
    /// <param name="width">The number of values along the first axis.</param>
    /// <param name="height">The number along the second axis.</param>
    /// <param name="depth">The number along the third axis.</param>
    /// <param name="bytesPerValue">One for 8 bits, four for 16 bits and floating point, which stay exact on the GPU as 32 bits.</param>
    /// <returns>The bytes.</returns>
    public static long EstimateMemory(int width, int height, int depth, int bytesPerValue) => (long)width * height * depth * bytesPerValue;

    /// <summary>Writes a slice as shares from 0 to 1 of the value range, which the GPU keeps exact for all kinds.</summary>
    /// <param name="z">The slice.</param>
    /// <param name="target">Receives one share per value.</param>
    internal void WriteShares(int z, float[] target)
    {
        var range = ValueRange;
        var (low, scale) = (range.Min, range.Max > range.Min ? 1 / (range.Max - range.Min) : 0);
        var (start, width) = ((long)z * Width * Height, Width);
        Parallel.For(0, Height, row =>
        {
            var first = start + ((long)row * width);
            for (var i = 0; i < width; i++)
            {
                var value = Read(first + i);
                target[(row * width) + i] = double.IsNaN(value) ? 0 : (float)((value - low) * scale);
            }
        });
    }

    /// <summary>Copies a slice of 8-bit values.</summary>
    /// <param name="z">The slice.</param>
    /// <returns>The bytes.</returns>
    internal ReadOnlySpan<byte> GetByteSlice(int z) => _bytes.AsSpan((int)((long)z * Width * Height), Width * Height);

    private double Read(long at)
    {
        if (_bytes is not null)
        {
            return _bytes[at];
        }

        return _words is not null ? _words[at] : _floats![at];
    }

    private DoubleRange FindRange()
    {
        var gate = new Lock();
        var (low, high) = (double.PositiveInfinity, double.NegativeInfinity);
        Parallel.For(0, Depth, z =>
        {
            var (partLow, partHigh) = (double.PositiveInfinity, double.NegativeInfinity);
            var first = (long)z * Width * Height;
            for (var i = first; i < first + ((long)Width * Height); i++)
            {
                var value = Read(i);
                if (double.IsFinite(value))
                {
                    (partLow, partHigh) = (Math.Min(partLow, value), Math.Max(partHigh, value));
                }
            }

            lock (gate)
            {
                (low, high) = (Math.Min(low, partLow), Math.Max(high, partHigh));
            }
        });
        return double.IsFinite(low) ? new DoubleRange(low, high) : new DoubleRange(0, 1);
    }
}

/// <summary>How a volume shows beyond its slices.</summary>
public enum VolumeMode
{
    /// <summary>Looking through, with color and opacity from <see cref="Volume3D.Transfer"/>. Without one only the slices show.</summary>
    Transfer,

    /// <summary>The highest value along each ray, colored through the colormap.</summary>
    Maximum,

    /// <summary>The surface where the values cross <see cref="Volume3D.IsoValue"/>, with light and gloss.</summary>
    Surface,
}

/// <summary>
/// A volume with three slices that show as colored pictures and can be dragged by their outlines. The data reach the
/// GPU slice by slice over several frames, so the surface of the application never waits.
/// </summary>
/// <param name="data">The values.</param>
/// <param name="x">The range of the first axis.</param>
/// <param name="y">The range of the second axis.</param>
/// <param name="z">The range of the third axis.</param>
public sealed class Volume3D(VolumeData data, DoubleRange x, DoubleRange y, DoubleRange z)
{
    private const double HalfCovered = 0.5;

    private long _stamp;
    private double _isoShare = 0.5;
    private DoubleRange? _colorRange;

    /// <summary>Gets the values.</summary>
    public VolumeData Data { get; } = data ?? throw new ArgumentNullException(nameof(data));

    /// <summary>Gets the range of the first axis.</summary>
    public DoubleRange X { get; } = x;

    /// <summary>Gets the range of the second axis.</summary>
    public DoubleRange Y { get; } = y;

    /// <summary>Gets the range of the third axis.</summary>
    public DoubleRange Z { get; } = z;

    /// <summary>Gets the colors of the slices.</summary>
    public Colormap Colormap { get; init; } = Colormaps.Viridis;

    /// <summary>Gets or sets the values mapped across the colormap, or <see langword="null"/> for the complete data range.</summary>
    public DoubleRange? ColorRange
    {
        get => _colorRange;
        set
        {
            if (value is { IsEmpty: true })
            {
                throw new ArgumentException("The color range must not be empty.", nameof(value));
            }

            if (_colorRange == value)
            {
                return;
            }

            _colorRange = value;
            Touch();
        }
    }

    /// <summary>Gets the slice across the first axis.</summary>
    public ClipPlane3D SliceX { get; } = new(new Point3D(1, 0, 0)) { Hides = false };

    /// <summary>Gets the slice across the second axis.</summary>
    public ClipPlane3D SliceY { get; } = new(new Point3D(0, 1, 0)) { Hides = false };

    /// <summary>Gets the slice across the third axis.</summary>
    public ClipPlane3D SliceZ { get; } = new(new Point3D(0, 0, 1)) { Hides = false };

    /// <summary>Gets the function for looking through the volume, or <see langword="null"/> for slices only.</summary>
    public TransferFunction3D? Transfer { get; init; }

    /// <summary>Gets how the volume shows beyond its slices.</summary>
    public VolumeMode Mode { get; init; }

    /// <summary>Gets or sets the value of the surface in <see cref="VolumeMode.Surface"/>. The chart also moves it with a slider.</summary>
    public double IsoValue
    {
        get
        {
            var range = Data.ValueRange;
            return range.Min + (IsoShare * (range.Max - range.Min));
        }

        set
        {
            var range = Data.ValueRange;
            IsoShare = range.Max > range.Min ? (value - range.Min) / (range.Max - range.Min) : 0.5;
        }
    }

    /// <summary>Gets how the surface takes the light.</summary>
    public Material3D Material { get; init; } = new() { Gloss = 0.35 };

    /// <summary>Gets or sets the value of the surface as a share of the value range.</summary>
    internal double IsoShare
    {
        get => Volatile.Read(ref _isoShare);
        set
        {
            Volatile.Write(ref _isoShare, Math.Clamp(value, 0, 1));
            Touch();
        }
    }

    /// <summary>Gets a value indicating whether rays run through the volume.</summary>
    internal bool Marches => Mode != VolumeMode.Transfer || Transfer is not null;

    /// <summary>Gets a value indicating whether the three slices show.</summary>
    public bool ShowsSlices { get; init; } = true;

    /// <summary>Gets the share of the value range below which a slice stays clear, from 0 to 1.</summary>
    public double SliceThreshold { get; init; }

    /// <summary>Gets a value indicating whether all values have reached the GPU.</summary>
    public bool IsReady { get; internal set; }

    /// <summary>Gets a number that changes as new slices arrive.</summary>
    internal long Stamp => Interlocked.Read(ref _stamp);

    /// <summary>Gets the color range as shares of the stored data range.</summary>
    internal (double Minimum, double Maximum) ColorShares
    {
        get
        {
            var data = Data.ValueRange;
            var range = ColorRange ?? data;
            var scale = data.Length > 0 ? 1 / data.Length : 0;
            var minimum = (range.Min - data.Min) * scale;
            var maximum = (range.Max - data.Min) * scale;
            return maximum > minimum ? (minimum, maximum) : (minimum, minimum + 1e-12);
        }
    }

    /// <summary>Asks for a new picture.</summary>
    internal void Touch() => Interlocked.Increment(ref _stamp);

    /// <summary>Finds the slice that a ray meets first.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance, which becomes the distance of the hit.</param>
    /// <param name="box">The place of the hit in the box.</param>
    /// <param name="place">The place in the raster.</param>
    /// <returns>Whether a slice lies on the ray.</returns>
    internal bool FindOnSlice(in Ray3D ray, Scene3D scene, double near, ref double far, out Point3D box, out (int X, int Y, int Z) place)
    {
        (box, place) = (default, default);
        var (low, high) = GetBox(scene);
        var isFound = false;
        foreach (var slice in (ReadOnlySpan<ClipPlane3D>)[SliceX, SliceY, SliceZ])
        {
            var climb = Point3D.Dot(ray.Direction, slice.Normal);
            var along = climb == 0 ? double.NaN : (slice.Offset - Point3D.Dot(ray.Start, slice.Normal)) / climb;
            if (along >= near && along <= far && Contains(ray.At(along), low, high) && ToRaster(ray.At(along), low, high) is var raster && IsShown(raster))
            {
                (far, box, place, isFound) = (along, ray.At(along), raster, true);
            }
        }

        return isFound;
    }

    /// <summary>Follows a ray through the volume, adding up opacity until it covers half of what lies behind.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance, which becomes the distance of the hit.</param>
    /// <param name="box">The place of the hit in the box.</param>
    /// <param name="place">The place in the raster.</param>
    /// <returns>Whether the ray ends in the volume.</returns>
    internal bool FindInside(in Ray3D ray, Scene3D scene, double near, ref double far, out Point3D box, out (int X, int Y, int Z) place)
    {
        (box, place) = (default, default);
        if (!Marches)
        {
            return false;
        }

        var (low, high) = GetBox(scene);
        var (from, to) = (near, far);
        if (!Ray3D.Limit1(ray.Start.X, ray.Direction.X, low.X, high.X, ref from, ref to)
            || !Ray3D.Limit1(ray.Start.Y, ray.Direction.Y, low.Y, high.Y, ref from, ref to)
            || !Ray3D.Limit1(ray.Start.Z, ray.Direction.Z, low.Z, high.Z, ref from, ref to))
        {
            return false;
        }

        var step = (high.X - low.X) / Data.Width;
        var range = Data.ValueRange;
        var scale = range.Max > range.Min ? 1 / (range.Max - range.Min) : 0;
        if (Mode == VolumeMode.Maximum)
        {
            return FindMaximum(ray, (low, high), (from, to), step, ref far, out box, out place);
        }

        if (Mode == VolumeMode.Surface)
        {
            return FindSurface(ray, (low, high), (from, to), step, ref far, out box, out place);
        }

        var covered = 0.0;
        for (var along = from; along <= to; along += step)
        {
            var raster = ToRaster(ray.At(along), low, high);
            covered += (1 - covered) * Transfer!.OpacityAt((Data[raster.X, raster.Y, raster.Z] - range.Min) * scale);
            if (covered >= HalfCovered)
            {
                (far, box, place) = (along, ray.At(along), raster);
                return true;
            }
        }

        return false;
    }

    // The first step that crosses the value of the surface, from either side.
    private bool FindSurface(in Ray3D ray, (Point3D Low, Point3D High) corners, (double From, double To) span, double step, ref double far, out Point3D box, out (int X, int Y, int Z) place)
    {
        var level = IsoValue;
        place = ToRaster(ray.At(span.From), corners.Low, corners.High);
        var below = Data[place.X, place.Y, place.Z] < level;
        for (var along = span.From + step; along <= span.To; along += step)
        {
            var raster = ToRaster(ray.At(along), corners.Low, corners.High);
            if (Data[raster.X, raster.Y, raster.Z] < level != below)
            {
                (far, box, place) = (along, ray.At(along), raster);
                return true;
            }
        }

        box = default;
        return false;
    }

    // The place of the highest value along the ray, as the picture of the maximum projection shows it.
    private bool FindMaximum(in Ray3D ray, (Point3D Low, Point3D High) corners, (double From, double To) span, double step, ref double far, out Point3D box, out (int X, int Y, int Z) place)
    {
        var (best, bestAlong) = (double.NegativeInfinity, double.NaN);
        (box, place) = (default, default);
        for (var along = span.From; along <= span.To; along += step)
        {
            var raster = ToRaster(ray.At(along), corners.Low, corners.High);
            var value = Data[raster.X, raster.Y, raster.Z];
            if (value > best)
            {
                (best, bestAlong, place) = (value, along, raster);
            }
        }

        var range = Data.ValueRange;
        var share = range.Max > range.Min ? (best - range.Min) / (range.Max - range.Min) : 0;
        if (double.IsNaN(bestAlong) || share <= SliceThreshold)
        {
            return false;
        }

        (far, box) = (bestAlong, ray.At(bestAlong));
        return true;
    }

    /// <summary>Gets the corners of the volume in the box.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The lower and upper corner.</returns>
    internal (Point3D Low, Point3D High) GetBox(Scene3D scene)
    {
        var map = new BoxMap(scene);
        var (a, b) = (map.ToBox(new Point3D(X.Min, Y.Min, Z.Min)), map.ToBox(new Point3D(X.Max, Y.Max, Z.Max)));
        return (new Point3D(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)), new Point3D(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));
    }

    /// <summary>Turns a place in the raster into the units of the axes.</summary>
    /// <param name="place">The place.</param>
    /// <returns>The point.</returns>
    internal Point3D ToData((int X, int Y, int Z) place) => new(
        X.Min + ((X.Max - X.Min) * place.X / (Data.Width - 1)),
        Y.Min + ((Y.Max - Y.Min) * place.Y / (Data.Height - 1)),
        Z.Min + ((Z.Max - Z.Min) * place.Z / (Data.Depth - 1)));

    private static bool Contains(in Point3D place, in Point3D low, in Point3D high) =>
        place.X >= low.X && place.X <= high.X && place.Y >= low.Y && place.Y <= high.Y && place.Z >= low.Z && place.Z <= high.Z;

    private static int ToIndex(double place, double low, double high, int count) =>
        Math.Clamp((int)Math.Round((place - low) / (high - low) * (count - 1)), 0, count - 1);

    private (int X, int Y, int Z) ToRaster(in Point3D place, in Point3D low, in Point3D high) =>
        (ToIndex(place.X, low.X, high.X, Data.Width), ToIndex(place.Y, low.Y, high.Y, Data.Height), ToIndex(place.Z, low.Z, high.Z, Data.Depth));

    private bool IsShown((int X, int Y, int Z) raster)
    {
        var range = Data.ValueRange;
        var share = range.Max > range.Min ? (Data[raster.X, raster.Y, raster.Z] - range.Min) / (range.Max - range.Min) : 0;
        return share >= SliceThreshold;
    }
}
