using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>The planes for the GPU, one per column of a matrix. The order follows clip.wgsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct CutPlanes
{
    public Vector4 First;
    public Vector4 Second;
    public Vector4 Third;
    public Vector4 Fourth;

    /// <summary>Gets the planes of a scene. A plane that keeps everything fills the free places.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The planes.</returns>
    public static CutPlanes From(Scene3D scene)
    {
        Span<Vector4> planes = [Vector4.UnitW, Vector4.UnitW, Vector4.UnitW, Vector4.UnitW];
        var used = 0;
        foreach (var plane in scene.Planes)
        {
            if (plane.Hides)
            {
                planes[used++] = new Vector4((float)plane.Normal.X, (float)plane.Normal.Y, (float)plane.Normal.Z, (float)-plane.Offset);
            }
        }

        return new CutPlanes { First = planes[0], Second = planes[1], Third = planes[2], Fourth = planes[3] };
    }
}

/// <summary>Takes a plane by its outline in the picture and moves it along its normal.</summary>
internal static class PlaneGrip
{
    private const float Reach = 8;
    private const double Probe = 0.1;

    /// <summary>Finds the plane whose outline runs nearest to a place.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="pane">The picture.</param>
    /// <param name="position">The place.</param>
    /// <returns>The plane, if one lies within reach.</returns>
    public static ClipPlane3D? Find(Scene3D scene, DipRect pane, Vector2 position)
    {
        var (found, best) = ((ClipPlane3D?)null, Reach);
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var corners = new List<Point3D>(6);
        foreach (var plane in scene.Planes)
        {
            plane.AddOutline(corners);
            for (var i = 0; i < corners.Count; i++)
            {
                var (a, b) = (ToDips(matrix, pane, corners[i]), ToDips(matrix, pane, corners[(i + 1) % corners.Count]));
                var distance = DistanceToPiece(position, a, b);
                if (distance < best)
                {
                    (found, best) = (plane, distance);
                }
            }
        }

        return found;
    }

    /// <summary>Moves a plane by a way of the pointer, so that its outline follows the pointer.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="plane">The plane.</param>
    /// <param name="pane">The picture.</param>
    /// <param name="way">The way of the pointer.</param>
    public static void Move(Scene3D scene, ClipPlane3D plane, DipRect pane, Vector2 way)
    {
        var matrix = scene.Camera.CreateMatrix(pane.Width / pane.Height);
        var place = plane.Normal * plane.Offset;
        var step = ToDips(matrix, pane, place + (plane.Normal * Probe)) - ToDips(matrix, pane, place);
        if (step.LengthSquared() >= 1)
        {
            plane.Offset += Vector2.Dot(way, step) / step.LengthSquared() * Probe;
        }
    }

    private static Vector2 ToDips(in Matrix3D matrix, DipRect pane, in Point3D point)
    {
        var (x, y, _) = matrix.Project(point);
        return new Vector2((float)(pane.X + ((x + 1) / 2 * pane.Width)), (float)(pane.Y + ((1 - y) / 2 * pane.Height)));
    }

    private static float DistanceToPiece(Vector2 position, Vector2 a, Vector2 b)
    {
        var piece = b - a;
        var share = piece.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(position - a, piece) / piece.LengthSquared(), 0, 1) : 0;
        return Vector2.Distance(position, a + (piece * share));
    }
}
