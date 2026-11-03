using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>Round points in space that keep their size on the screen.</summary>
/// <param name="points">The points. Their colors and sizes win over the ones here.</param>
public sealed class Scatter3D(Points3D points)
{
    /// <summary>Gets the points.</summary>
    public Points3D Points { get; } = points ?? throw new ArgumentNullException(nameof(points));

    /// <summary>Gets the color of the points without one.</summary>
    public Rgba8 Color { get; init; } = new(90, 170, 255, 255);

    /// <summary>Gets the diameter of the points without one, in device-independent units.</summary>
    public double Size { get; init; } = 3;

    /// <summary>
    /// Gets the number of newest points per frame. It keeps a large live cloud smooth. Above 8 million shown points
    /// they become single pixels, as round points of their own size would no longer stay at 60 frames per second.
    /// </summary>
    public int PointBudget { get; init; } = 15_000_000;
}

/// <summary>
/// Sorts points into a raster of cells, so that a ray only looks at the points near it. It forms in the background and
/// shows the state at that time, so a live cloud keeps answering while the next raster forms.
/// </summary>
internal sealed class PointGrid
{
    private const int Side = 128;
    private const double CellSize = 2.0 / Side;

    private readonly int[] _starts = new int[(Side * Side * Side) + 1];
    private readonly int[] _slots;
    private readonly int[] _seen = new int[Side * Side * Side];
    private int _stamp;

    /// <summary>Initializes a new instance of the <see cref="PointGrid"/> class.</summary>
    /// <param name="points">The points.</param>
    /// <param name="map">The way into the box.</param>
    public PointGrid(Points3D points, in BoxMap map)
    {
        Total = points.Total;
        var count = points.Count;
        var cells = new int[count];
        for (var slot = 0; slot < count; slot++)
        {
            var place = map.ToBox(points.GetSlot(slot));
            cells[slot] = CellOf(place);
            if (cells[slot] >= 0)
            {
                _starts[cells[slot] + 1]++;
            }
        }

        for (var i = 1; i < _starts.Length; i++)
        {
            _starts[i] += _starts[i - 1];
        }

        _slots = new int[_starts[^1]];
        var next = _starts[..^1];
        for (var slot = 0; slot < count; slot++)
        {
            if (cells[slot] >= 0)
            {
                _slots[next[cells[slot]]++] = slot;
            }
        }
    }

    /// <summary>Gets how many points had been added when this raster formed.</summary>
    public long Total { get; }

    /// <summary>Finds the front-most point within reach of a ray.</summary>
    /// <param name="ray">The ray.</param>
    /// <param name="reach">How far from the ray a point still counts.</param>
    /// <param name="points">The points.</param>
    /// <param name="map">The way into the box.</param>
    /// <param name="near">The first distance.</param>
    /// <param name="far">The last distance, which becomes the distance of the hit.</param>
    /// <param name="found">The point in the units of the axes and in the box.</param>
    /// <returns>Whether a point lies at the ray.</returns>
    public bool Find(in Ray3D ray, in PickReach reach, Points3D points, in BoxMap map, double near, ref double far, out (Point3D Data, Point3D Box) found)
    {
        found = default;
        _stamp++;
        var isFound = false;
        for (var distance = near; distance <= far + CellSize; distance += CellSize / 2)
        {
            var center = ray.At(Math.Min(distance, far));
            var margin = (int)Math.Ceiling(reach.At(distance) / CellSize);
            var cell = (ToCell(center.X), ToCell(center.Y), ToCell(center.Z));
            isFound |= FindAround(ray, reach, points, map, cell, margin, near, ref far, ref found);
        }

        return isFound;
    }

    private static int ToCell(double place) => Math.Clamp((int)((place + 1) / CellSize), 0, Side - 1);

    private static int CellOf(in Point3D place)
    {
        if (!double.IsFinite(place.X + place.Y + place.Z) || Math.Abs(place.X) > 1 || Math.Abs(place.Y) > 1 || Math.Abs(place.Z) > 1)
        {
            return -1;
        }

        return (ToCell(place.Z) * Side * Side) + (ToCell(place.Y) * Side) + ToCell(place.X);
    }

    private bool FindAround(in Ray3D ray, in PickReach reach, Points3D points, in BoxMap map, (int X, int Y, int Z) cell, int margin, double near, ref double far, ref (Point3D Data, Point3D Box) found)
    {
        var isFound = false;
        for (var z = Math.Max(cell.Z - margin, 0); z <= Math.Min(cell.Z + margin, Side - 1); z++)
        {
            for (var y = Math.Max(cell.Y - margin, 0); y <= Math.Min(cell.Y + margin, Side - 1); y++)
            {
                for (var x = Math.Max(cell.X - margin, 0); x <= Math.Min(cell.X + margin, Side - 1); x++)
                {
                    isFound |= FindInCell(ray, reach, points, map, (z * Side * Side) + (y * Side) + x, near, ref far, ref found);
                }
            }
        }

        return isFound;
    }

    private bool FindInCell(in Ray3D ray, in PickReach reach, Points3D points, in BoxMap map, int cell, double near, ref double far, ref (Point3D Data, Point3D Box) found)
    {
        if (_seen[cell] == _stamp)
        {
            return false;
        }

        _seen[cell] = _stamp;
        var isFound = false;
        for (var i = _starts[cell]; i < _starts[cell + 1]; i++)
        {
            var data = points.GetSlot(_slots[i]);
            var box = map.ToBox(data);
            var along = Point3D.Dot(box - ray.Start, ray.Direction);
            if (along >= near && along <= far && (box - ray.At(along)).Length <= reach.At(along))
            {
                (far, found, isFound) = (along, (data, box), true);
            }
        }

        return isFound;
    }
}
