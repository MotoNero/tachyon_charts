using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Files;

/// <summary>Reads the points and faces of OBJ. Everything else (normals, textures, materials, groups) is passed over.</summary>
internal static class ObjReader
{
    public static ModelFileData Read(Stream stream)
    {
        var model = new Model();
        try
        {
            TextLines.ForEach(stream, model.ReadLine);
        }
        catch (OutOfMemoryException exception)
        {
            throw new PointFileException("There is not enough memory for the model.", exception);
        }

        return model.Finish();
    }

    /// <summary>What the lines read so far hold.</summary>
    private sealed class Model
    {
        private readonly List<Point3D> _positions = [];
        private readonly List<Rgba8> _colors = [];
        private readonly List<int> _triangles = [];
        private readonly List<int> _corners = [];
        private int _highest = -1;
        private long _highestLine;

        public void ReadLine(ReadOnlySpan<byte> line, long number)
        {
            if (line.Length < 2 || line[1] is not ((byte)' ' or (byte)'\t'))
            {
                return;
            }

            var rest = line[2..];
            if (line[0] == (byte)'v')
            {
                AddPoint(rest, number);
            }
            else if (line[0] == (byte)'f')
            {
                AddFace(rest, number);
            }
        }

        public ModelFileData Finish()
        {
            if (_triangles.Count == 0)
            {
                throw new PointFileException("The file holds no faces.");
            }

            // Faces may name points that follow later, so the check waits for the end.
            if (_highest >= _positions.Count)
            {
                throw new PointFileException($"The face in line {_highestLine} uses point {_highest + 1}, but the file holds only {_positions.Count} points.");
            }

            var hasColors = _colors.Count == _positions.Count && _colors.Count > 0;
            return Creases.Split([.. _positions], [.. _triangles], hasColors ? [.. _colors] : null);
        }

        private void AddPoint(ReadOnlySpan<byte> rest, long number)
        {
            if (!TextLines.TryTake(ref rest, out var x) || !TextLines.TryTake(ref rest, out var y) || !TextLines.TryTake(ref rest, out var z)
                || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                throw new PointFileException($"Line {number} needs three finite numbers after 'v'.");
            }

            _positions.Add(new Point3D(x, y, z));
            if (TextLines.TryTake(ref rest, out var red) && TextLines.TryTake(ref rest, out var green) && TextLines.TryTake(ref rest, out var blue))
            {
                _colors.Add(new Rgba8(ToByte(red), ToByte(green), ToByte(blue), 255));
            }
        }

        private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value * 255), 0, 255);

        // A face with more than three corners becomes a fan of triangles around its first corner.
        private void AddFace(ReadOnlySpan<byte> rest, long number)
        {
            ReadCorners(rest, number);
            for (var i = 1; i + 1 < _corners.Count; i++)
            {
                _triangles.Add(_corners[0]);
                _triangles.Add(_corners[i]);
                _triangles.Add(_corners[i + 1]);
            }

            var most = _corners.Max();
            if (most > _highest)
            {
                (_highest, _highestLine) = (most, number);
            }
        }

        private void ReadCorners(ReadOnlySpan<byte> rest, long number)
        {
            _corners.Clear();
            rest = rest.TrimStart(" \t"u8);
            while (!rest.IsEmpty)
            {
                var end = rest.IndexOfAny(" \t"u8);
                var word = end < 0 ? rest : rest[..end];
                rest = end < 0 ? default : rest[end..].TrimStart(" \t"u8);
                _corners.Add(ToCorner(word, number));
            }

            if (_corners.Count < 3)
            {
                throw new PointFileException($"The face in line {number} has fewer than three corners.");
            }
        }

        // A corner is "i", "i/t", "i//n" or "i/t/n"; only the number of the point counts.
        private int ToCorner(ReadOnlySpan<byte> word, long number)
        {
            var slash = word.IndexOf((byte)'/');
            if (!int.TryParse(slash < 0 ? word : word[..slash], out var index) || index == 0)
            {
                throw new PointFileException($"Line {number} holds a corner that is not a point number.");
            }

            // Negative numbers count back from the last point read so far.
            var corner = index > 0 ? index - 1 : _positions.Count + index;
            if (corner < 0)
            {
                throw new PointFileException($"Line {number} counts back {-index} points, but only {_positions.Count} are read so far.");
            }

            return corner;
        }
    }
}
