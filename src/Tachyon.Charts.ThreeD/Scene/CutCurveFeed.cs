using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>A marker of the 3D chart as the 2D chart of a cut shows it.</summary>
/// <param name="Number">The number of the marker, from 1, as in the 3D chart.</param>
/// <param name="Along">The place along the cut, in the units of <see cref="CutCurveFeed.Data"/>.</param>
/// <param name="Value">The value of the marker.</param>
public readonly record struct CutMarker(int Number, double Along, double Value);

/// <summary>
/// Keeps a 2D data source on the cut of a plane through a surface: when the plane moves or the surface changes, the
/// curve follows in the next frame of the 3D chart. A 2D chart shows the source as a line and needs nothing of 3D.
/// </summary>
/// <param name="plane">The plane, which must stand upright.</param>
/// <param name="surface">The surface.</param>
public sealed class CutCurveFeed(ClipPlane3D plane, Surface3D surface)
{
    private long _seen = -1;
    private long _curveVersion;
    private Cut _cut = new(new CutCurve([], [], [], []), []);
    private Projection _projection = new(-1, []);

    /// <summary>Gets the plane.</summary>
    public ClipPlane3D Plane { get; } = plane ?? throw new ArgumentNullException(nameof(plane));

    /// <summary>Gets the surface.</summary>
    public Surface3D Surface { get; } = surface ?? throw new ArgumentNullException(nameof(surface));

    /// <summary>
    /// Gets the curve: over the first axis for a plane across the second axis, over the second axis for a plane across
    /// the first one, and over the way along the cut otherwise.
    /// </summary>
    public DataSource<double, double> Data { get; } = new();

    /// <summary>
    /// Gets the markers of the 3D chart, placed on the cut: a marker beside the plane shows where the cut passes
    /// nearest to it, with its own value.
    /// </summary>
    public IReadOnlyList<CutMarker> Markers
    {
        get
        {
            var projection = Volatile.Read(ref _projection);
            var version = MarkerVersion;
            if (projection.Version != version)
            {
                projection = new Projection(version, Project(Scene?.Markers ?? [], Volatile.Read(ref _cut)));
                Volatile.Write(ref _projection, projection);
            }

            return projection.Markers;
        }
    }

    /// <summary>Gets a number that changes when the markers or the cut change.</summary>
    internal long MarkerVersion => (Scene?.MarkerVersion ?? 0) + Volatile.Read(ref _curveVersion);

    /// <summary>Gets or sets the scene the cut belongs to.</summary>
    internal Scene3D? Scene { get; set; }

    /// <summary>Sets a marker on the cut, which the 3D chart shows too.</summary>
    /// <param name="along">The place along the cut.</param>
    /// <returns>Whether the cut has a value there.</returns>
    public bool AddMarker(double along)
    {
        if (Scene is not { } scene || Find(Volatile.Read(ref _cut), along) is not { } place)
        {
            return false;
        }

        scene.AddMarker(new Pick3D(place, Surface) { Box = new BoxMap(scene).ToBox(place) });
        return true;
    }

    /// <summary>Removes the marker next to a place along the cut.</summary>
    /// <param name="along">The place along the cut.</param>
    /// <param name="reach">How far away the marker may be.</param>
    /// <returns>Whether a marker was removed.</returns>
    public bool RemoveMarkerNear(double along, double reach)
    {
        var markers = Markers;
        var (nearest, distance) = (-1, reach);
        for (var i = 0; i < markers.Count; i++)
        {
            if (Math.Abs(markers[i].Along - along) <= distance)
            {
                (nearest, distance) = (markers[i].Number - 1, Math.Abs(markers[i].Along - along));
            }
        }

        if (nearest < 0 || Scene is not { } scene)
        {
            return false;
        }

        scene.RemoveMarker(nearest);
        return true;
    }

    /// <summary>Removes the newest marker.</summary>
    public void RemoveNewestMarker() => Scene?.RemoveLastMarker();

    /// <summary>Brings the curve up to date.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>Whether the curve changed.</returns>
    internal bool Update(Scene3D scene)
    {
        var version = Plane.Version + Surface.Data.Version + scene.XAxis.Version + scene.YAxis.Version;
        if (version == _seen)
        {
            return false;
        }

        _seen = version;
        var curve = Plane.Cut(Surface, scene);
        var normal = Plane.Normal;
        var along = Math.Abs(normal.X) < 1e-9 ? curve.X : Math.Abs(normal.Y) < 1e-9 ? curve.Y : curve.Along; // NOSONAR: three cases of one choice
        Data.Clear();
        Data.Append(along, curve.Values);
        Volatile.Write(ref _cut, new Cut(curve, along));
        Interlocked.Increment(ref _curveVersion);
        return true;
    }

    // The place on the cut, or null where it has no value.
    internal static Point3D? Find(Cut cut, double along)
    {
        var places = cut.Along;
        for (var i = 1; i < places.Length; i++)
        {
            var (low, high) = (Math.Min(places[i - 1], places[i]), Math.Max(places[i - 1], places[i]));
            if (along < low || along > high)
            {
                continue;
            }

            var share = high > low ? (along - places[i - 1]) / (places[i] - places[i - 1]) : 0;
            var curve = cut.Curve;
            var value = curve.Values[i - 1] + (share * (curve.Values[i] - curve.Values[i - 1]));
            if (double.IsFinite(value))
            {
                return new Point3D(
                    curve.X[i - 1] + (share * (curve.X[i] - curve.X[i - 1])),
                    curve.Y[i - 1] + (share * (curve.Y[i] - curve.Y[i - 1])),
                    value);
            }
        }

        return null;
    }

    // Each marker goes where the cut passes nearest to it on the floor.
    internal static CutMarker[] Project(IReadOnlyList<Pick3D> markers, Cut cut)
    {
        var curve = cut.Curve;
        var found = new List<CutMarker>(markers.Count);
        for (var i = 0; i < markers.Count; i++)
        {
            var position = markers[i].Position;
            var (nearest, distance) = (-1, double.PositiveInfinity);
            for (var j = 0; j < cut.Along.Length; j++)
            {
                var (dx, dy) = (curve.X[j] - position.X, curve.Y[j] - position.Y);
                if ((dx * dx) + (dy * dy) < distance)
                {
                    (nearest, distance) = (j, (dx * dx) + (dy * dy));
                }
            }

            if (nearest >= 0)
            {
                // Along an axis the marker keeps its own place, between the samples of the cut.
                var along = ReferenceEquals(cut.Along, curve.X) ? position.X : ReferenceEquals(cut.Along, curve.Y) ? position.Y : cut.Along[nearest]; // NOSONAR: three cases of one choice
                found.Add(new CutMarker(i + 1, along, position.Z));
            }
        }

        return [.. found];
    }

    /// <summary>The last cut with the places along it that the 2D chart uses.</summary>
    internal sealed record Cut(CutCurve Curve, double[] Along);

    private sealed record Projection(long Version, CutMarker[] Markers);
}
