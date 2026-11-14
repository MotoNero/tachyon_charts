namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>How the depth of a 3D chart is shown.</summary>
public enum CameraProjection
{
    /// <summary>Far things look smaller.</summary>
    Perspective,

    /// <summary>Parallel lines stay parallel, so that values can be compared across the depth.</summary>
    Orthographic,
}

/// <summary>A prepared direction of view.</summary>
public enum CameraView
{
    /// <summary>From the front, the right and above.</summary>
    Default,

    /// <summary>Along the Y axis.</summary>
    Front,

    /// <summary>Along the X axis.</summary>
    Side,

    /// <summary>From above.</summary>
    Top,
}

/// <summary>
/// The view into the box of a 3D chart, which spans from -1 to 1 on each axis. The UI thread changes it, the
/// render thread reads it, so every change replaces the whole state.
/// </summary>
public sealed class Camera3D
{
    private const double FieldOfView = Math.PI / 4;

    // The width to height at and above which the box with its labels fits across. A narrower picture keeps that width of
    // view and sees more above and below, so that the box does not reach out at the sides.
    private const double FittingAspect = 1.2;
    private const double MaximumElevation = 89.5;
    private const double MinimumDistance = 0.5;
    private const double MaximumDistance = 20;
    private const double MaximumShift = 2;
    private const double BoxRadius = 1.8;
    private const double DefaultAzimuth = -55;
    private const double DefaultElevation = 25;
    private const double DefaultDistance = 4.9;

    private State _state = new(DefaultAzimuth, DefaultElevation, DefaultDistance, default, CameraProjection.Perspective);
    private long _version;

    /// <summary>Gets the direction of view around the vertical axis in degrees.</summary>
    public double Azimuth => Current.Azimuth;

    /// <summary>Gets the height of the eye in degrees, at most just short of the poles.</summary>
    public double Elevation => Current.Elevation;

    /// <summary>Gets the distance from the center of the view.</summary>
    public double Distance => Current.Distance;

    /// <summary>Gets the center of the view.</summary>
    public Point3D Target => Current.Target;

    /// <summary>Gets or sets how the depth is shown.</summary>
    public CameraProjection Projection
    {
        get => Current.Projection;
        set => Change(Current with { Projection = value });
    }

    /// <summary>Gets the position of the eye.</summary>
    public Point3D Eye
    {
        get
        {
            var state = Current;
            return state.Target + (ToDirection(state.Azimuth, state.Elevation) * state.Distance);
        }
    }

    /// <summary>Gets a number that grows with each change.</summary>
    public long Version => Volatile.Read(ref _version);

    private State Current => Volatile.Read(ref _state);

    /// <summary>Turns the view. Elevation stops short of the poles, so the picture never rolls over.</summary>
    /// <param name="azimuth">The change around the vertical axis in degrees.</param>
    /// <param name="elevation">The change of height in degrees.</param>
    public void Orbit(double azimuth, double elevation)
    {
        if (double.IsFinite(azimuth) && double.IsFinite(elevation))
        {
            var state = Current;
            Change(state with
            {
                Azimuth = Math.IEEERemainder(state.Azimuth + azimuth, 360),
                Elevation = Math.Clamp(state.Elevation + elevation, -MaximumElevation, MaximumElevation),
            });
        }
    }

    /// <summary>Moves the view sideways.</summary>
    /// <param name="right">The way to the right, in shares of the distance.</param>
    /// <param name="up">The way up, in shares of the distance.</param>
    public void Pan(double right, double up)
    {
        if (double.IsFinite(right) && double.IsFinite(up))
        {
            var state = Current;
            var back = ToDirection(state.Azimuth, state.Elevation);
            var side = Point3D.Cross(new Point3D(0, 0, 1), back).Normalized();
            var top = Point3D.Cross(back, side);
            var target = state.Target + (((side * right) + (top * up)) * state.Distance);
            Change(state with
            {
                Target = new Point3D(
                    Math.Clamp(target.X, -MaximumShift, MaximumShift),
                    Math.Clamp(target.Y, -MaximumShift, MaximumShift),
                    Math.Clamp(target.Z, -MaximumShift, MaximumShift)),
            });
        }
    }

    /// <summary>Changes the distance.</summary>
    /// <param name="factor">The factor, below 1 moves closer.</param>
    public void Zoom(double factor)
    {
        if (double.IsFinite(factor) && factor > 0)
        {
            var state = Current;
            Change(state with { Distance = Math.Clamp(state.Distance * factor, MinimumDistance, MaximumDistance) });
        }
    }

    /// <summary>Goes to a prepared view. The kind of projection stays.</summary>
    /// <param name="view">The view.</param>
    public void Show(CameraView view)
    {
        var (azimuth, elevation) = view switch
        {
            CameraView.Front => (-90.0, 0.0),
            CameraView.Side => (0.0, 0.0),
            CameraView.Top => (-90.0, MaximumElevation),
            _ => (DefaultAzimuth, DefaultElevation),
        };
        Change(Current with { Azimuth = azimuth, Elevation = elevation, Distance = DefaultDistance, Target = default });
    }

    /// <summary>Creates the ray from the eye through a place of the picture.</summary>
    /// <param name="aspect">The width of the picture divided by its height.</param>
    /// <param name="x">The place in the picture from -1 to 1.</param>
    /// <param name="y">The place in the picture from -1 to 1.</param>
    /// <returns>The ray through this place.</returns>
    internal Ray3D CreateRay(double aspect, double x, double y)
    {
        var matrix = CreateMatrix(aspect);
        var start = matrix.Unproject(x, y, 0);
        return new Ray3D(start, (matrix.Unproject(x, y, 1) - start).Normalized());
    }

    /// <summary>Calculates the matrix from the box to the clip space.</summary>
    /// <param name="aspect">The width of the picture divided by its height.</param>
    /// <returns>The matrix.</returns>
    internal Matrix3D CreateMatrix(double aspect)
    {
        var state = Current;
        var safeAspect = double.IsFinite(aspect) && aspect > 0 ? aspect : 1;
        var eye = state.Target + (ToDirection(state.Azimuth, state.Elevation) * state.Distance);
        var view = Matrix3D.LookAt(eye, state.Target, new Point3D(0, 0, 1));
        var near = Math.Max(state.Distance - BoxRadius - state.Target.Length, state.Distance * 0.01);
        var far = state.Distance + BoxRadius + state.Target.Length;
        var widen = Math.Max(1, FittingAspect / safeAspect);
        var projection = state.Projection == CameraProjection.Perspective
            ? Matrix3D.Perspective(2 * Math.Atan(Math.Tan(FieldOfView / 2) * widen), safeAspect, near, far)
            : Matrix3D.Orthographic(2 * state.Distance * Math.Tan(FieldOfView / 2) * widen, safeAspect, near, far);
        return projection * view;
    }

    private static Point3D ToDirection(double azimuth, double elevation)
    {
        var (a, e) = (azimuth * Math.PI / 180, elevation * Math.PI / 180);
        return new Point3D(Math.Cos(e) * Math.Cos(a), Math.Cos(e) * Math.Sin(a), Math.Sin(e));
    }

    private void Change(State state)
    {
        if (state != Current)
        {
            Volatile.Write(ref _state, state);
            Interlocked.Increment(ref _version);
        }
    }

    private sealed record State(double Azimuth, double Elevation, double Distance, Point3D Target, CameraProjection Projection);
}
