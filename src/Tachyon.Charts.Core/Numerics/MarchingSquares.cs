using System.Numerics;

namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// A piece of an isoline in grid coordinates: one unit is the distance of two samples.
/// </summary>
/// <param name="Start">The first point.</param>
/// <param name="End">The second point.</param>
public readonly record struct ContourSegment(Vector2 Start, Vector2 End);

/// <summary>
/// Finds isolines in a grid of samples. This is the reference for contour.wgsl and the way of
/// devices without compute shaders, so both follow the same rules.
/// </summary>
public static class MarchingSquares
{
    /// <summary>
    /// Finds the segments of all levels.
    /// </summary>
    /// <param name="values">The samples row by row. A cell with a NaN corner has no segments.</param>
    /// <param name="width">The number of columns.</param>
    /// <param name="height">The number of rows.</param>
    /// <param name="levels">The levels.</param>
    /// <param name="segments">Receives the segments.</param>
    /// <exception cref="ArgumentException">The length does not match the grid.</exception>
    public static void Generate(ReadOnlySpan<float> values, int width, int height, ReadOnlySpan<float> levels, List<ContourSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (values.Length != (long)width * height)
        {
            throw new ArgumentException("The length does not match the grid.", nameof(values));
        }

        for (var y = 0; y + 1 < height; y++)
        {
            var low = values.Slice(y * width, width);
            var high = values.Slice((y + 1) * width, width);
            for (var x = 0; x + 1 < width; x++)
            {
                var cell = new Cell(new Vector2(x, y), low[x], low[x + 1], high[x + 1], high[x]);
                if (float.IsNaN(cell.A) || float.IsNaN(cell.B) || float.IsNaN(cell.C) || float.IsNaN(cell.D))
                {
                    continue;
                }

                foreach (var level in levels)
                {
                    March(cell, level, segments);
                }
            }
        }
    }

    private static void March(in Cell cell, float level, List<ContourSegment> segments)
    {
        var mask = (cell.A >= level ? 1 : 0) | (cell.B >= level ? 2 : 0) | (cell.C >= level ? 4 : 0) | (cell.D >= level ? 8 : 0);
        switch (mask)
        {
            case 1 or 14:
                Add(cell, 3, 0, level, segments);
                break;
            case 2 or 13:
                Add(cell, 0, 1, level, segments);
                break;
            case 3 or 12:
                Add(cell, 3, 1, level, segments);
                break;
            case 4 or 11:
                Add(cell, 1, 2, level, segments);
                break;
            case 6 or 9:
                Add(cell, 0, 2, level, segments);
                break;
            case 7 or 8:
                Add(cell, 3, 2, level, segments);
                break;
            case 5 or 10:
                // Two opposite corners: the middle decides which of them are connected.
                var centerIsHigh = (cell.A + cell.B + cell.C + cell.D) / 4 >= level;
                if ((mask == 5) == centerIsHigh)
                {
                    Add(cell, 0, 1, level, segments);
                    Add(cell, 2, 3, level, segments);
                }
                else
                {
                    Add(cell, 3, 0, level, segments);
                    Add(cell, 1, 2, level, segments);
                }

                break;
        }
    }

    private static void Add(in Cell cell, int first, int second, float level, List<ContourSegment> segments) =>
        segments.Add(new ContourSegment(cell.EdgePoint(first, level), cell.EdgePoint(second, level)));

    // The corners run around the cell: A at the origin, B to the right, C opposite, D above A.
    private readonly record struct Cell(Vector2 Origin, float A, float B, float C, float D)
    {
        public Vector2 EdgePoint(int edge, float level) => edge switch
        {
            0 => Origin + new Vector2((level - A) / (B - A), 0),
            1 => Origin + new Vector2(1, (level - B) / (C - B)),
            2 => Origin + new Vector2((level - D) / (C - D), 1),
            _ => Origin + new Vector2(0, (level - A) / (D - A)),
        };
    }
}
