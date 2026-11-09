using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.ThreeD.Scene;

/// <summary>A slice of a pie in 3D, as the pointer finds it.</summary>
/// <param name="Pie">The pie.</param>
/// <param name="Index">The number of the slice.</param>
public sealed record PieSlice3D(Pie3D Pie, int Index)
{
    /// <summary>Gets the name of the slice.</summary>
    public string Name => Pie.Names[Index];

    /// <summary>Gets the value of the slice.</summary>
    public double Value => Pie.Values[Index];

    /// <summary>Gets the share of the slice in the whole, from 0 to 1.</summary>
    public double Share => Pie.GetShare(Index);
}

/// <summary>
/// A pie or donut chart as a lit body: each value is a slice, clockwise from the top. Zero, negative and missing values
/// get no slice and do not count. The pie lies in the middle of the box with a radius of 1 in units of the axes;
/// <see cref="Chart3DBuilder.AddPie"/> sets the axes and hides them. A click on a slice pulls it out or back.
/// </summary>
public sealed class Pie3D
{
    private const int StepsPerTurn = 180;

    private static readonly Rgba8[] _defaultPalette =
    [
        new(76, 201, 240, 255), new(247, 37, 133, 255), new(255, 190, 11, 255), new(114, 9, 183, 255),
        new(58, 134, 255, 255), new(6, 214, 160, 255), new(251, 86, 7, 255), new(131, 56, 236, 255),
    ];

    private readonly Vertex3D[]?[] _vertices;
    private readonly Mesh3D?[] _meshes;
    private int _exploded = -1;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="Pie3D"/> class.</summary>
    /// <param name="values">The values, one per slice.</param>
    /// <param name="names">The names, or <see langword="null"/> for numbers.</param>
    /// <exception cref="ArgumentException">The number of names differs from the number of values.</exception>
    public Pie3D(IReadOnlyList<double> values, IReadOnlyList<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (names is not null && names.Count != values.Count)
        {
            throw new ArgumentException($"There are {names.Count} names for {values.Count} values.", nameof(names));
        }

        Values = [.. values];
        Names = names is null ? [.. Enumerable.Range(1, values.Count).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))] : [.. names];
        foreach (var value in Values)
        {
            Total += Counts(value) ? value : 0;
        }

        _vertices = new Vertex3D[Values.Count][];
        _meshes = new Mesh3D[Values.Count];
    }

    /// <summary>Gets the values.</summary>
    public IReadOnlyList<double> Values { get; }

    /// <summary>Gets the names.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>Gets the sum of all values that get a slice.</summary>
    public double Total { get; }

    /// <summary>Gets the radius of the hole as a share of the radius, from 0 for a pie to 0.9 for a thin ring.</summary>
    public double InnerRadius
    {
        get;
        init => field = Math.Clamp(value, 0, 0.9);
    }

    /// <summary>Gets the height of the body in units of the radius.</summary>
    public double Height { get; init; } = 0.3;

    /// <summary>Gets how far a slice moves out when it is pulled out, in units of the radius.</summary>
    public double ExplodeDistance { get; init; } = 0.15;

    /// <summary>Gets the gap between the slices in units of the radius.</summary>
    public double Gap { get; init; } = 0.012;

    /// <summary>Gets the colors of the slices, or <see langword="null"/> for the palette of the theme.</summary>
    public IReadOnlyList<Rgba8>? Colors { get; init; }

    /// <summary>Gets what the label on each slice shows: name, share, value or nothing, as on a pie in 2D.</summary>
    public PieLabels Labels { get; init; } = PieLabels.NameAndPercent;

    /// <summary>Gets how the slices take the light.</summary>
    public Material3D Material { get; init; } = new() { Gloss = 0.25, Sharpness = 32 };

    /// <summary>Gets or sets the number of the slice that is pulled out, or -1 for none.</summary>
    /// <exception cref="ArgumentOutOfRangeException">There is no such slice.</exception>
    public int ExplodedIndex
    {
        get => Volatile.Read(ref _exploded);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, -1);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, Values.Count);
            if (Interlocked.Exchange(ref _exploded, value) != value)
            {
                Interlocked.Increment(ref _version);
            }
        }
    }

    /// <summary>Gets a number that grows when the look changes.</summary>
    internal long Version => Volatile.Read(ref _version);

    /// <summary>Gets the surface of each slice, or <see langword="null"/> for a value without one. <see cref="Layout"/> fills them.</summary>
    internal IReadOnlyList<Mesh3D?> Meshes => _meshes;

    /// <summary>Gets the share of a slice in the whole.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <returns>The share from 0 to 1, or 0 for a value without a slice.</returns>
    public double GetShare(int index) => Counts(Values[index]) && Total > 0 ? Values[index] / Total : 0;

    /// <summary>Places the corners of all slices. The surfaces stay the same objects, only their corners move.</summary>
    /// <param name="palette">The colors of the theme.</param>
    internal void Layout(ReadOnlySpan<Rgba8> palette)
    {
        if (palette.IsEmpty)
        {
            palette = _defaultPalette;
        }

        var start = 0.0;
        var exploded = ExplodedIndex;
        var corners = new List<Vertex3D>();
        for (var i = 0; i < Values.Count; i++)
        {
            if (!Counts(Values[i]))
            {
                continue;
            }

            var sweep = Values[i] / Total * 2 * Math.PI;
            var color = Colors is { Count: > 0 } colors ? colors[i % colors.Count] : palette[i % palette.Length];
            var steps = Math.Max(2, (int)Math.Ceiling(sweep / (2 * Math.PI) * StepsPerTurn));
            var from = (Math.PI / 2) - start;
            var middle = from - (sweep / 2);
            var direction = new Point3D(Math.Cos(middle), Math.Sin(middle), 0);

            // Moving a slice along its middle by d opens a gap of d · sin(sweep / 2) at each cut.
            var lift = sweep >= (2 * Math.PI) - 1e-9 ? 0 : Math.Min(Gap / 2 / Math.Max(Math.Sin(Math.Min(sweep, Math.PI) / 2), 1e-3), 0.05);
            var shift = direction * (lift + (i == exploded ? ExplodeDistance : 0));
            corners.Clear();
            var triangles = _meshes[i] is null ? new List<int>() : null;
            Build(corners, triangles, from, from - sweep, steps, shift, color);
            if (_meshes[i] is null)
            {
                _vertices[i] = [.. corners];
                _meshes[i] = new Mesh3D(_vertices[i]!, [.. triangles!]) { Material = Material };
            }
            else
            {
                corners.CopyTo(_vertices[i]!);
            }

            start += sweep;
        }
    }

    /// <summary>Gets the place of the label of a slice: the middle of its top, in data units, moved with the slice.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <returns>The place, or <see langword="null"/> for a value without a slice.</returns>
    internal Point3D? LabelPoint(int index) => TopPoint(index, InnerRadius > 0 ? (InnerRadius + 1) / 2 : 0.62);

    /// <summary>Gets a point on the top of a slice along its middle, in data units, moved with the slice.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <param name="radius">The distance from the axis in units of the radius.</param>
    /// <returns>The point, or <see langword="null"/> for a value without a slice.</returns>
    internal Point3D? TopPoint(int index, double radius)
    {
        if (!Counts(Values[index]) || Total <= 0)
        {
            return null;
        }

        var start = 0.0;
        for (var i = 0; i < index; i++)
        {
            start += Counts(Values[i]) ? Values[i] / Total * 2 * Math.PI : 0;
        }

        var sweep = Values[index] / Total * 2 * Math.PI;
        var middle = (Math.PI / 2) - start - (sweep / 2);
        var direction = new Point3D(Math.Cos(middle), Math.Sin(middle), 0);
        var lift = sweep >= (2 * Math.PI) - 1e-9 ? 0 : Math.Min(Gap / 2 / Math.Max(Math.Sin(Math.Min(sweep, Math.PI) / 2), 1e-3), 0.05);
        var place = direction * (radius + lift + (index == ExplodedIndex ? ExplodeDistance : 0));
        return new Point3D(place.X, place.Y, Height / 2);
    }

    /// <summary>Writes the label of a slice as <see cref="Labels"/> asks.</summary>
    /// <param name="index">The number of the slice.</param>
    /// <returns>The text; empty for none.</returns>
    internal string LabelText(int index)
    {
        var share = GetShare(index);
        var percent = (share * 100).ToString(share < 0.1 ? "0.#" : "0", ChartText.Culture) + " %";
        return Labels switch
        {
            PieLabels.None => string.Empty,
            PieLabels.Name => Names[index],
            PieLabels.Percent => percent,
            PieLabels.Value => Values[index].ToString("G4", ChartText.Culture),
            _ => Names[index].Length == 0 ? percent : Names[index] + " " + percent,
        };
    }

    private static bool Counts(double value) => double.IsFinite(value) && value > 0;

    // Top, bottom and sides are bands between two rings, each with corners of its own, so that the edges stay sharp in
    // the light. A pie has its inner ring in the middle; a donut adds the inner side.
    private void Build(List<Vertex3D> corners, List<int>? triangles, double from, double to, int steps, Point3D shift, Rgba8 color)
    {
        var (top, bottom, inner) = (Height / 2, -Height / 2, InnerRadius);
        Band(corners, triangles, from, to, steps, shift, color, (inner, top), (1, top));
        Band(corners, triangles, from, to, steps, shift, color, (1, bottom), (inner, bottom));
        Band(corners, triangles, from, to, steps, shift, color, (1, top), (1, bottom));
        if (inner > 0)
        {
            Band(corners, triangles, from, to, steps, shift, color, (inner, bottom), (inner, top));
        }

        Band(corners, triangles, from, from, 1, shift, color, (inner, top), (inner, bottom), (1, top), (1, bottom));
        Band(corners, triangles, to, to, 1, shift, color, (inner, top), (inner, bottom), (1, top), (1, bottom));
    }

    private static void Band(List<Vertex3D> corners, List<int>? triangles, double from, double to, int steps, Point3D shift, Rgba8 color, (double Radius, double Z) first, (double Radius, double Z) second) =>
        Band(corners, triangles, from, to, steps, shift, color, first, first, second, second);

    // Two rings of steps + 1 corners, from the angle 'from' to 'to'; a ring may change its radius and height along the
    // way, which is what turns a band into a cut face.
    private static void Band(List<Vertex3D> corners, List<int>? triangles, double from, double to, int steps, Point3D shift, Rgba8 color, (double Radius, double Z) firstStart, (double Radius, double Z) firstEnd, (double Radius, double Z) secondStart, (double Radius, double Z) secondEnd)
    {
        var at = corners.Count;
        foreach (var (begin, end) in (ReadOnlySpan<((double Radius, double Z), (double Radius, double Z))>)[(firstStart, firstEnd), (secondStart, secondEnd)])
        {
            for (var s = 0; s <= steps; s++)
            {
                var share = (double)s / steps;
                var angle = from + ((to - from) * share);
                var radius = begin.Radius + ((end.Radius - begin.Radius) * share);
                var z = begin.Z + ((end.Z - begin.Z) * share);
                corners.Add(new Vertex3D(new Point3D(radius * Math.Cos(angle), radius * Math.Sin(angle), z) + shift, color));
            }
        }

        if (triangles is null)
        {
            return;
        }

        var ring = steps + 1;
        for (var s = 0; s < steps; s++)
        {
            triangles.AddRange([at + s, at + s + 1, at + ring + s + 1, at + s, at + ring + s + 1, at + ring + s]);
        }
    }
}
