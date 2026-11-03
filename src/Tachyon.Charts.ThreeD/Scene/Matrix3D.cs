using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>A point or direction in the space of a 3D chart.</summary>
/// <param name="X">The first coordinate.</param>
/// <param name="Y">The second coordinate.</param>
/// <param name="Z">The third coordinate, which points up.</param>
public readonly record struct Point3D(double X, double Y, double Z)
{
    /// <summary>Gets the length of the vector.</summary>
    public double Length => Math.Sqrt(Dot(this, this));

    /// <summary>Adds two vectors.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The sum.</returns>
    public static Point3D operator +(Point3D a, Point3D b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Subtracts two vectors.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The difference.</returns>
    public static Point3D operator -(Point3D a, Point3D b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Scales a vector.</summary>
    /// <param name="a">The vector.</param>
    /// <param name="factor">The factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Point3D operator *(Point3D a, double factor) => new(a.X * factor, a.Y * factor, a.Z * factor);

    /// <summary>Calculates the dot product.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The dot product.</returns>
    public static double Dot(Point3D a, Point3D b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>Calculates the cross product.</summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The cross product.</returns>
    public static Point3D Cross(Point3D a, Point3D b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    /// <summary>Gets the vector with length 1.</summary>
    /// <returns>The vector, or this one if its length is zero.</returns>
    public Point3D Normalized()
    {
        var length = Length;
        return length > 0 ? this * (1 / length) : this;
    }
}

/// <summary>A 4 × 4 matrix in double precision for column vectors. Clip depth runs from 0 to 1, as in WebGPU.</summary>
internal readonly struct Matrix3D
{
    private readonly Numbers _m; // NOSONAR: CopyTo fills it through a span

    private Matrix3D(ReadOnlySpan<double> rows) => rows.CopyTo(_m);

    public double this[int row, int column] => _m[(row * 4) + column];

    public static Matrix3D operator *(Matrix3D a, Matrix3D b)
    {
        Span<double> result = stackalloc double[16];
        result.Clear();
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                for (var k = 0; k < 4; k++)
                {
                    result[(row * 4) + column] += a[row, k] * b[k, column];
                }
            }
        }

        return new Matrix3D(result);
    }

    public static Matrix3D LookAt(Point3D eye, Point3D target, Point3D up)
    {
        var back = (eye - target).Normalized();
        var right = Point3D.Cross(up, back).Normalized();
        var top = Point3D.Cross(back, right);
        return new Matrix3D(
        [
            right.X, right.Y, right.Z, -Point3D.Dot(right, eye),
            top.X, top.Y, top.Z, -Point3D.Dot(top, eye),
            back.X, back.Y, back.Z, -Point3D.Dot(back, eye),
            0, 0, 0, 1,
        ]);
    }

    public static Matrix3D Perspective(double verticalAngle, double aspect, double near, double far)
    {
        var scale = 1 / Math.Tan(verticalAngle / 2);
        var depth = far / (near - far);
        return new Matrix3D(
        [
            scale / aspect, 0, 0, 0,
            0, scale, 0, 0,
            0, 0, depth, near * depth,
            0, 0, -1, 0,
        ]);
    }

    public static Matrix3D Orthographic(double height, double aspect, double near, double far)
    {
        var depth = 1 / (near - far);
        return new Matrix3D(
        [
            2 / (height * aspect), 0, 0, 0,
            0, 2 / height, 0, 0,
            0, 0, depth, near * depth,
            0, 0, 0, 1,
        ]);
    }

    private static void Eliminate(Span<double> rows, int column)
    {
        const int Width = 5;
        var pivot = column;
        for (var row = column + 1; row < 4; row++)
        {
            if (Math.Abs(rows[(row * Width) + column]) > Math.Abs(rows[(pivot * Width) + column]))
            {
                pivot = row;
            }
        }

        for (var i = 0; pivot != column && i < Width; i++)
        {
            (rows[(pivot * Width) + i], rows[(column * Width) + i]) = (rows[(column * Width) + i], rows[(pivot * Width) + i]);
        }

        for (var row = 0; row < 4; row++)
        {
            var factor = rows[(row * Width) + column] / rows[(column * Width) + column];
            for (var i = column; row != column && i < Width; i++)
            {
                rows[(row * Width) + i] -= factor * rows[(column * Width) + i];
            }
        }
    }

    /// <summary>Projects a point and keeps w, which shows whether it lies in front of the eye.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The place in the picture and w.</returns>
    public (double X, double Y, double W) ProjectWithW(in Point3D point)
    {
        var x = (this[0, 0] * point.X) + (this[0, 1] * point.Y) + (this[0, 2] * point.Z) + this[0, 3];
        var y = (this[1, 0] * point.X) + (this[1, 1] * point.Y) + (this[1, 2] * point.Z) + this[1, 3];
        var w = (this[3, 0] * point.X) + (this[3, 1] * point.Y) + (this[3, 2] * point.Z) + this[3, 3];
        return (x / w, y / w, w);
    }

    public (double X, double Y, double Z) Project(Point3D point)
    {
        var x = (this[0, 0] * point.X) + (this[0, 1] * point.Y) + (this[0, 2] * point.Z) + this[0, 3];
        var y = (this[1, 0] * point.X) + (this[1, 1] * point.Y) + (this[1, 2] * point.Z) + this[1, 3];
        var z = (this[2, 0] * point.X) + (this[2, 1] * point.Y) + (this[2, 2] * point.Z) + this[2, 3];
        var w = (this[3, 0] * point.X) + (this[3, 1] * point.Y) + (this[3, 2] * point.Z) + this[3, 3];
        return (x / w, y / w, z / w);
    }

    /// <summary>Finds the point that lands on a place of the picture, the reverse of <see cref="Project"/>.</summary>
    /// <param name="x">The place from -1 to 1.</param>
    /// <param name="y">The place from -1 to 1.</param>
    /// <param name="z">The depth from 0 to 1.</param>
    /// <returns>The point.</returns>
    public Point3D Unproject(double x, double y, double z)
    {
        // Solves the matrix against the place by elimination, which needs no inverse matrix.
        const int Width = 5;
        Span<double> rows = stackalloc double[4 * Width];
        Span<double> target = [x, y, z, 1];
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                rows[(row * Width) + column] = this[row, column];
            }

            rows[(row * Width) + 4] = target[row];
        }

        for (var column = 0; column < 4; column++)
        {
            Eliminate(rows, column);
        }

        var w = rows[(3 * Width) + 4] / rows[(3 * Width) + 3];
        return new Point3D(rows[4] / rows[0] / w, rows[Width + 4] / rows[Width + 1] / w, rows[(2 * Width) + 4] / rows[(2 * Width) + 2] / w);
    }

    /// <summary>Gets the matrix for the GPU. WGSL stores columns, System.Numerics rows, so the rows here are the columns there.</summary>
    /// <returns>The matrix in float precision.</returns>
    public Matrix4x4 ToGpu() => new(
        (float)this[0, 0], (float)this[1, 0], (float)this[2, 0], (float)this[3, 0],
        (float)this[0, 1], (float)this[1, 1], (float)this[2, 1], (float)this[3, 1],
        (float)this[0, 2], (float)this[1, 2], (float)this[2, 2], (float)this[3, 2],
        (float)this[0, 3], (float)this[1, 3], (float)this[2, 3], (float)this[3, 3]);

    [InlineArray(16)]
    private struct Numbers
    {
        private double _first; // NOSONAR: only gives the element type of the inline array
    }
}
