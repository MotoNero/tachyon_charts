using System.Buffers.Binary;
using System.Numerics;

namespace Tachyon.Charts.Rendering.Skia.Text;

/// <summary>
/// Reads glyph outlines directly from the <c>glyf</c> table. Skia asks the font system of the operating
/// system, whose outlines differ slightly. This makes the atlas identical on all platforms.
/// </summary>
internal sealed class TrueTypeOutlines
{
    private const int MaximumCompositeDepth = 6;
    private const int OnCurve = 0x01;
    private const int XShort = 0x02;
    private const int YShort = 0x04;
    private const int RepeatFlag = 0x08;
    private const int XSameOrPositive = 0x10;
    private const int YSameOrPositive = 0x20;
    private const int ArgumentsAreWords = 0x0001;
    private const int ArgumentsAreOffsets = 0x0002;
    private const int HasScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HasXYScale = 0x0040;
    private const int HasMatrix = 0x0080;

    private readonly byte[] _font;
    private readonly int _glyf;
    private readonly int _loca;
    private readonly bool _isLongLoca;
    private readonly int _unitsPerEm;
    private readonly int _curveSegments;

    private TrueTypeOutlines(byte[] font, int glyf, int loca, bool isLongLoca, int unitsPerEm, int curveSegments)
    {
        _font = font;
        _glyf = glyf;
        _loca = loca;
        _isLongLoca = isLongLoca;
        _unitsPerEm = unitsPerEm;
        _curveSegments = curveSegments;
    }

    /// <summary>
    /// Opens the outlines of a font.
    /// </summary>
    /// <returns>The outlines, or <see langword="null"/> if the font has no <c>glyf</c> table.</returns>
    public static TrueTypeOutlines? TryCreate(byte[] font, int curveSegments)
    {
        if (font.Length < 12)
        {
            return null;
        }

        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        int glyf = 0, loca = 0, head = 0;
        for (var i = 0; i < tableCount; i++)
        {
            var entry = font.AsSpan(12 + (i * 16));
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            switch (System.Text.Encoding.ASCII.GetString(entry[..4]))
            {
                case "glyf":
                    glyf = offset;
                    break;
                case "loca":
                    loca = offset;
                    break;
                case "head":
                    head = offset;
                    break;
            }
        }

        if (glyf == 0 || loca == 0 || head == 0)
        {
            return null;
        }

        var unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(head + 18));
        var isLongLoca = BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(head + 50)) != 0;
        return new TrueTypeOutlines(font, glyf, loca, isLongLoca, unitsPerEm, curveSegments);
    }

    /// <summary>
    /// Reads a glyph as closed contours of flattened curves.
    /// </summary>
    /// <param name="glyph">The glyph id.</param>
    /// <param name="emSize">The em size of the result. Y grows downwards.</param>
    /// <returns>The contours, empty for glyphs without outline.</returns>
    public List<List<Vector2[]>> GetContours(ushort glyph, float emSize)
    {
        var points = new List<List<OutlinePoint>>();
        Collect(glyph, Matrix3x2.CreateScale(emSize / _unitsPerEm, -emSize / _unitsPerEm), points, 0);

        var contours = new List<List<Vector2[]>>(points.Count);
        foreach (var contour in points)
        {
            var curves = BuildCurves(contour);
            if (curves.Count > 0)
            {
                contours.Add(curves);
            }
        }

        return contours;
    }

    private (int Start, int Length) Locate(ushort glyph)
    {
        var location = _font.AsSpan(_loca);
        int start;
        int end;
        if (_isLongLoca)
        {
            start = (int)BinaryPrimitives.ReadUInt32BigEndian(location[(glyph * 4)..]);
            end = (int)BinaryPrimitives.ReadUInt32BigEndian(location[((glyph + 1) * 4)..]);
        }
        else
        {
            start = BinaryPrimitives.ReadUInt16BigEndian(location[(glyph * 2)..]) * 2;
            end = BinaryPrimitives.ReadUInt16BigEndian(location[((glyph + 1) * 2)..]) * 2;
        }

        return (_glyf + start, end - start);
    }

    private void Collect(ushort glyph, Matrix3x2 transform, List<List<OutlinePoint>> contours, int depth)
    {
        var (start, length) = Locate(glyph);
        if (length == 0 || depth > MaximumCompositeDepth)
        {
            return;
        }

        var contourCount = BinaryPrimitives.ReadInt16BigEndian(_font.AsSpan(start));
        var body = start + 10;
        if (contourCount >= 0)
        {
            CollectSimple(body, contourCount, transform, contours);
        }
        else
        {
            CollectComposite(body, transform, contours, depth);
        }
    }

    private void CollectSimple(int offset, int contourCount, Matrix3x2 transform, List<List<OutlinePoint>> contours)
    {
        var data = _font.AsSpan();
        var ends = new int[contourCount];
        for (var i = 0; i < contourCount; i++)
        {
            ends[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + (i * 2))..]);
        }

        var pointCount = contourCount == 0 ? 0 : ends[^1] + 1;
        var position = offset + (contourCount * 2);
        position += 2 + BinaryPrimitives.ReadUInt16BigEndian(data[position..]);

        var flags = new byte[pointCount];
        for (var i = 0; i < pointCount; i++)
        {
            var flag = data[position++];
            flags[i] = flag;
            if ((flag & RepeatFlag) != 0)
            {
                var repeat = data[position++];
                for (var r = 0; r < repeat; r++)
                {
                    flags[++i] = flag; // NOSONAR: repeat flags of the font format
                }
            }
        }

        var xs = ReadCoordinates(flags, XShort, XSameOrPositive, ref position);
        var ys = ReadCoordinates(flags, YShort, YSameOrPositive, ref position);

        var first = 0;
        foreach (var end in ends)
        {
            var contour = new List<OutlinePoint>(end - first + 1);
            for (var i = first; i <= end; i++)
            {
                contour.Add(new OutlinePoint(Vector2.Transform(new Vector2(xs[i], ys[i]), transform), (flags[i] & OnCurve) != 0));
            }

            contours.Add(contour);
            first = end + 1;
        }
    }

    private int[] ReadCoordinates(byte[] flags, int shortFlag, int sameFlag, ref int position)
    {
        var values = new int[flags.Length];
        var value = 0;
        for (var i = 0; i < flags.Length; i++)
        {
            var flag = flags[i];
            if ((flag & shortFlag) != 0)
            {
                var delta = _font[position++];
                value += (flag & sameFlag) != 0 ? delta : -delta;
            }
            else if ((flag & sameFlag) == 0)
            {
                value += BinaryPrimitives.ReadInt16BigEndian(_font.AsSpan(position));
                position += 2;
            }

            values[i] = value;
        }

        return values;
    }

    private void CollectComposite(int offset, Matrix3x2 transform, List<List<OutlinePoint>> contours, int depth)
    {
        int flags;
        do
        {
            var data = _font.AsSpan(offset);
            flags = BinaryPrimitives.ReadUInt16BigEndian(data);
            var component = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
            offset += 4;

            float dx;
            float dy;
            if ((flags & ArgumentsAreWords) != 0)
            {
                dx = BinaryPrimitives.ReadInt16BigEndian(data[4..]);
                dy = BinaryPrimitives.ReadInt16BigEndian(data[6..]);
                offset += 4;
            }
            else
            {
                dx = (sbyte)data[4];
                dy = (sbyte)data[5];
                offset += 2;
            }

            if ((flags & ArgumentsAreOffsets) == 0)
            {
                // Point matching places a component by outline points, which text fonts do not use.
                dx = dy = 0;
            }

            var local = ReadComponentScale(flags, ref offset);
            local.M31 = dx;
            local.M32 = dy;
            Collect(component, local * transform, contours, depth + 1);
        }
        while ((flags & MoreComponents) != 0);
    }

    private Matrix3x2 ReadComponentScale(int flags, ref int offset)
    {
        float Next(ref int position)
        {
            var value = BinaryPrimitives.ReadInt16BigEndian(_font.AsSpan(position)) / 16384f;
            position += 2;
            return value;
        }

        var matrix = Matrix3x2.Identity;
        if ((flags & HasScale) != 0)
        {
            matrix.M11 = matrix.M22 = Next(ref offset);
        }
        else if ((flags & HasXYScale) != 0)
        {
            matrix.M11 = Next(ref offset);
            matrix.M22 = Next(ref offset);
        }
        else if ((flags & HasMatrix) != 0)
        {
            matrix.M11 = Next(ref offset);
            matrix.M12 = Next(ref offset);
            matrix.M21 = Next(ref offset);
            matrix.M22 = Next(ref offset);
        }

        return matrix;
    }

    /// <summary>
    /// Turns the points of a contour into curves. Two control points in a row imply a point between them.
    /// </summary>
    private List<Vector2[]> BuildCurves(List<OutlinePoint> contour)
    {
        var curves = new List<Vector2[]>();
        if (contour.Count < 2)
        {
            return curves;
        }

        var startIndex = contour.FindIndex(point => point.IsOnCurve);
        var start = startIndex >= 0
            ? contour[startIndex].Position
            : Vector2.Lerp(contour[^1].Position, contour[0].Position, 0.5f);
        var offset = startIndex >= 0 ? startIndex + 1 : 0;
        var steps = startIndex >= 0 ? contour.Count - 1 : contour.Count;

        var current = start;
        Vector2? control = null;
        for (var i = 0; i < steps; i++)
        {
            var point = contour[(offset + i) % contour.Count];
            if (point.IsOnCurve)
            {
                AddSegment(curves, current, control, point.Position);
                current = point.Position;
                control = null;
            }
            else
            {
                if (control is { } previous)
                {
                    var implied = Vector2.Lerp(previous, point.Position, 0.5f);
                    AddSegment(curves, current, previous, implied);
                    current = implied;
                }

                control = point.Position;
            }
        }

        AddSegment(curves, current, control, start);
        return curves;
    }

    private void AddSegment(List<Vector2[]> curves, Vector2 from, Vector2? control, Vector2 to)
    {
        if (Vector2.DistanceSquared(from, to) <= 1e-8f)
        {
            return;
        }

        if (control is not { } middle)
        {
            curves.Add([from, to]);
            return;
        }

        var points = new Vector2[_curveSegments + 1];
        for (var i = 0; i <= _curveSegments; i++)
        {
            var t = i / (float)_curveSegments;
            var u = 1 - t;
            points[i] = (u * u * from) + (2 * u * t * middle) + (t * t * to);
        }

        curves.Add(points);
    }

    private readonly record struct OutlinePoint(Vector2 Position, bool IsOnCurve);
}
