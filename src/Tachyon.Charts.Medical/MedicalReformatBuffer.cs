using System.Runtime.InteropServices;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>
/// A picture sampled from a volume along any plane or along a curve, blended from the eight nearest voxels. Places are
/// in millimetres of the volume, with the middle of voxel (0, 0, 0) at the origin, as the orthogonal planes use them.
/// </summary>
internal sealed class MedicalReformatBuffer : IMedicalPixels
{
    private readonly MedicalVolume _volume;
    private readonly object _gate = new();
    private readonly float[] _values;
    private long _version;

    public MedicalReformatBuffer(MedicalVolume volume, int width, int height, ImageSpacing spacing)
    {
        _volume = volume;
        (Width, Height, Spacing) = (width, height, spacing);
        _values = new float[checked(width * height)];
        Array.Fill(_values, float.NaN);
    }

    public int Width { get; }

    public int Height { get; }

    public ImageSpacing Spacing { get; }

    public long Version => Interlocked.Read(ref _version);

    /// <summary>Samples a plane: the middle of pixel (c, r) lies at origin + across × (c + ½) × column spacing + down × (r + ½) × row spacing.</summary>
    /// <param name="origin">The corner of the picture.</param>
    /// <param name="across">The unit direction of the columns.</param>
    /// <param name="down">The unit direction of the rows.</param>
    public void FillPlane(Point3D origin, Point3D across, Point3D down)
    {
        lock (_gate)
        {
            Parallel.For(0, Height, row =>
            {
                var start = origin + (down * ((row + 0.5) * Spacing.Row));
                for (var column = 0; column < Width; column++)
                {
                    _values[(row * Width) + column] = (float)Sample(start + (across * ((column + 0.5) * Spacing.Column)));
                }
            });
            Interlocked.Increment(ref _version);
        }
    }

    /// <summary>
    /// Samples along a curve: column c lies at the length (c + ½) × column spacing along the path, row r at
    /// top + (r + ½) × row spacing in the direction down. Columns past the end of the path stay empty.
    /// </summary>
    /// <param name="path">The corners of the path.</param>
    /// <param name="down">The unit direction of the rows.</param>
    /// <param name="top">Where the first row starts along down.</param>
    /// <returns>The length of the path.</returns>
    public double FillCurve(IReadOnlyList<Point3D> path, Point3D down, double top)
    {
        var places = new Point3D?[Width];
        var length = Walk(path, places, Spacing.Column);
        lock (_gate)
        {
            Parallel.For(0, Height, row =>
            {
                var offset = down * (top + ((row + 0.5) * Spacing.Row));
                for (var column = 0; column < Width; column++)
                {
                    _values[(row * Width) + column] = places[column] is { } place ? (float)Sample(place + offset) : float.NaN;
                }
            });
            Interlocked.Increment(ref _version);
        }

        return length;
    }

    /// <summary>Empties the picture.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Array.Fill(_values, float.NaN);
            Interlocked.Increment(ref _version);
        }
    }

    public float Read(int column, int row)
    {
        lock (_gate)
        {
            return _values[(row * Width) + column];
        }
    }

    public void Write(IGpuTexture texture)
    {
        lock (_gate)
        {
            texture.Write(MemoryMarshal.AsBytes(_values.AsSpan()));
        }
    }

    /// <summary>Reads the volume at a place, blended from the eight nearest voxels; NaN outside.</summary>
    /// <param name="place">The place in millimetres of the volume.</param>
    /// <returns>The value.</returns>
    public double Sample(Point3D place)
    {
        var data = _volume.Data;
        var spacing = _volume.VoxelSpacing;
        var (u, v, w) = (place.X / spacing.X, place.Y / spacing.Y, place.Z / spacing.Z);
        const double Edge = 1e-9;
        if (!(u >= -Edge && v >= -Edge && w >= -Edge && u <= data.Width - 1 + Edge && v <= data.Height - 1 + Edge && w <= data.Depth - 1 + Edge))
        {
            return double.NaN;
        }

        var (x, y, z) = (Corner(u, data.Width), Corner(v, data.Height), Corner(w, data.Depth));
        var (a, b, c) = (Math.Clamp(u - x, 0, 1), Math.Clamp(v - y, 0, 1), Math.Clamp(w - z, 0, 1));
        var near = Mix(Mix(data[x, y, z], data[x + 1, y, z], a), Mix(data[x, y + 1, z], data[x + 1, y + 1, z], a), b);
        var far = Mix(Mix(data[x, y, z + 1], data[x + 1, y, z + 1], a), Mix(data[x, y + 1, z + 1], data[x + 1, y + 1, z + 1], a), b);
        return Mix(near, far, c);
    }

    // The places along the path every step, from half a step on; the rest stay null.
    private static double Walk(IReadOnlyList<Point3D> path, Point3D?[] places, double step)
    {
        var (column, travelled, next) = (0, 0.0, step / 2);
        for (var i = 1; i < path.Count && column < places.Length; i++)
        {
            var (from, to) = (path[i - 1], path[i]);
            var part = (to - from).Length;
            while (part > 0 && next <= travelled + part && column < places.Length)
            {
                places[column++] = from + ((to - from) * ((next - travelled) / part));
                next += step;
            }

            travelled += part;
        }

        return travelled;
    }

    private static int Corner(double position, int count) => Math.Clamp((int)Math.Floor(position), 0, count - 2);

    private static double Mix(double first, double second, double share) => first + ((second - first) * share);
}
