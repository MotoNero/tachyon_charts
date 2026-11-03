using System.Numerics;
using Tachyon.Charts.Core.Layout;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>A point of a transfer function.</summary>
/// <param name="Share">The place in the value range, from 0 to 1.</param>
/// <param name="Color">The color.</param>
/// <param name="Opacity">How much it covers, from 0 to 1.</param>
public readonly record struct TransferPoint3D(double Share, Rgba8 Color, double Opacity);

/// <summary>Says which color and opacity each value gets when looking through a volume. Colors and opacity run straight between the points.</summary>
public sealed class TransferFunction3D
{
    /// <summary>The number of entries that the GPU reads.</summary>
    internal const int TableSize = 256;

    private readonly Lock _gate = new();
    private readonly TransferPoint3D[] _points;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="TransferFunction3D"/> class.</summary>
    /// <param name="points">At least two points, sorted by their share.</param>
    public TransferFunction3D(params TransferPoint3D[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Length < 2)
        {
            throw new ArgumentException("A transfer function needs at least two points.", nameof(points));
        }

        for (var i = 1; i < points.Length; i++)
        {
            if (points[i].Share < points[i - 1].Share)
            {
                throw new ArgumentException("The points must be sorted by their share.", nameof(points));
            }
        }

        _points = [.. points];
    }

    /// <summary>Gets the points.</summary>
    public IReadOnlyList<TransferPoint3D> Points
    {
        get
        {
            lock (_gate)
            {
                return [.. _points];
            }
        }
    }

    /// <summary>Copies the points without a new list, for the picture of each frame.</summary>
    /// <param name="points">Receives the points.</param>
    internal void ReadPoints(List<TransferPoint3D> points)
    {
        lock (_gate)
        {
            points.Clear();
            points.AddRange(_points);
        }
    }

    /// <summary>Gets a number that changes with the points.</summary>
    internal long Version => Interlocked.Read(ref _version);

    /// <summary>Moves a point. It stays between its neighbours, and the ends keep their share.</summary>
    /// <param name="index">The point.</param>
    /// <param name="share">The new place in the value range.</param>
    /// <param name="opacity">The new opacity.</param>
    public void Move(int index, double share, double opacity)
    {
        lock (_gate)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)_points.Length);
            var isEnd = index == 0 || index == _points.Length - 1;
            var place = isEnd ? _points[index].Share : Math.Clamp(share, _points[index - 1].Share, _points[index + 1].Share);
            _points[index] = _points[index] with { Share = place, Opacity = Math.Clamp(opacity, 0, 1) };
        }

        Interlocked.Increment(ref _version);
    }

    /// <summary>Gets the opacity at a place, without the color.</summary>
    /// <param name="share">The place in the value range.</param>
    /// <returns>The opacity.</returns>
    public double OpacityAt(double share) => Sample(share).W;

    /// <summary>Writes the table for the GPU.</summary>
    /// <param name="table">Receives color and opacity per entry.</param>
    internal void WriteTable(Vector4[] table)
    {
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = Sample(i / (table.Length - 1.0));
        }
    }

    private static Vector4 ToVector(in TransferPoint3D point) =>
        new(point.Color.R / 255f, point.Color.G / 255f, point.Color.B / 255f, (float)point.Opacity);

    private Vector4 Sample(double share)
    {
        lock (_gate)
        {
            if (share <= _points[0].Share)
            {
                return ToVector(_points[0]);
            }

            for (var i = 1; i < _points.Length; i++)
            {
                if (share <= _points[i].Share)
                {
                    var (a, b) = (_points[i - 1], _points[i]);
                    var step = b.Share - a.Share;
                    return Vector4.Lerp(ToVector(a), ToVector(b), step > 0 ? (float)((share - a.Share) / step) : 1);
                }
            }

            return ToVector(_points[^1]);
        }
    }
}

/// <summary>The small editor of a transfer function in the corner of the picture.</summary>
internal static class TransferGrip
{
    private const float Reach = 9;
    private const double Width = 180;
    private const double Height = 84;
    private const double Margin = 14;

    /// <summary>Gets the area of the editor.</summary>
    /// <param name="pane">The picture.</param>
    /// <returns>The area.</returns>
    public static DipRect Area(DipRect pane) => new(pane.X + pane.Width - Width - Margin, pane.Y + Margin, Width, Height);

    /// <summary>Gets the place of a point on the screen.</summary>
    /// <param name="area">The editor.</param>
    /// <param name="point">The point.</param>
    /// <returns>The place.</returns>
    public static Vector2 ToDips(DipRect area, in TransferPoint3D point) =>
        new((float)(area.X + (point.Share * area.Width)), (float)(area.Y + ((1 - point.Opacity) * area.Height)));

    /// <summary>Finds the point nearest to a place.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="pane">The picture.</param>
    /// <param name="position">The place.</param>
    /// <returns>The function and its point, if one lies within reach.</returns>
    public static (TransferFunction3D Function, int Index)? Find(Scene3D scene, DipRect pane, Vector2 position)
    {
        var area = Area(pane);
        foreach (var volume in scene.Volumes)
        {
            if (volume.Transfer is not { } transfer)
            {
                continue;
            }

            var points = transfer.Points;
            for (var i = 0; i < points.Count; i++)
            {
                if (Vector2.Distance(position, ToDips(area, points[i])) <= Reach)
                {
                    return (transfer, i);
                }
            }
        }

        return null;
    }

    /// <summary>Finds the slider of a surface within reach of a place.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="pane">The picture.</param>
    /// <param name="position">The place.</param>
    /// <returns>The volume, if its slider lies there.</returns>
    public static Volume3D? FindLevel(Scene3D scene, DipRect pane, Vector2 position)
    {
        var area = Area(pane);
        foreach (var volume in scene.Volumes)
        {
            var x = area.X + (volume.IsoShare * area.Width);
            if (volume.Mode == VolumeMode.Surface && Math.Abs(position.X - x) <= Reach && position.Y >= area.Y - Reach && position.Y <= area.Y + area.Height + Reach)
            {
                return volume;
            }
        }

        return null;
    }

    /// <summary>Moves the slider of a surface to a place.</summary>
    /// <param name="volume">The volume.</param>
    /// <param name="pane">The picture.</param>
    /// <param name="position">The place.</param>
    public static void MoveLevel(Volume3D volume, DipRect pane, Vector2 position)
    {
        var area = Area(pane);
        volume.IsoShare = (position.X - area.X) / area.Width;
    }

    /// <summary>Moves a point to a place.</summary>
    /// <param name="function">The function.</param>
    /// <param name="index">The point.</param>
    /// <param name="pane">The picture.</param>
    /// <param name="position">The place.</param>
    public static void Move(TransferFunction3D function, int index, DipRect pane, Vector2 position)
    {
        var area = Area(pane);
        function.Move(index, (position.X - area.X) / area.Width, 1 - ((position.Y - area.Y) / area.Height));
    }
}

/// <summary>Chooses the length of the steps through a volume: long while the view moves, short once it rests.</summary>
internal sealed class MarchQuality
{
    private const long RestAfter = 150;

    private long _lastMove = long.MinValue / 2;
    private bool _isCoarse;

    /// <summary>Gets a value indicating whether the last picture was the coarse one.</summary>
    public bool IsCoarse => _isCoarse;

    /// <summary>Takes note of a frame.</summary>
    /// <param name="hasMoved">Whether the view changed.</param>
    /// <param name="now">The time in milliseconds.</param>
    /// <returns>Whether this frame is coarse.</returns>
    public bool Update(bool hasMoved, long now)
    {
        _lastMove = hasMoved ? now : _lastMove;
        _isCoarse = now - _lastMove < RestAfter;
        return _isCoarse;
    }

    /// <summary>Gets a value indicating whether a coarse picture should give way to a fine one.</summary>
    /// <param name="now">The time in milliseconds.</param>
    /// <returns>Whether it should.</returns>
    public bool WantsFinePicture(long now) => _isCoarse && now - _lastMove >= RestAfter;
}
