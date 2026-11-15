using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Geo;

/// <summary>A region of a map, such as a country: its name, its code and its areas in degrees of longitude and latitude.</summary>
public sealed class GeoRegion
{
    private readonly (double[] X, double[] Y, int[] Triangles, int[] Rings)?[] _triangles;

    /// <summary>Initializes a new instance of the <see cref="GeoRegion"/> class.</summary>
    /// <param name="name">The name.</param>
    /// <param name="code">The code, such as ISO 3166 alpha-3 for countries.</param>
    /// <param name="polygons">The areas, X the longitude and Y the latitude in degrees.</param>
    public GeoRegion(string name, string code, IReadOnlyList<Polygon> polygons)
    {
        (Name, Code) = (name ?? string.Empty, code ?? string.Empty);
        Polygons = polygons ?? throw new ArgumentNullException(nameof(polygons));
        _triangles = new (double[], double[], int[], int[])?[polygons.Count];
        (double minX, double minY, double maxX, double maxY) = (double.MaxValue, double.MaxValue, double.MinValue, double.MinValue);
        foreach (var polygon in polygons)
        {
            for (var i = 0; i < polygon.Outer.Count; i++)
            {
                (minX, minY, maxX, maxY) = (Math.Min(minX, polygon.Outer.X[i]), Math.Min(minY, polygon.Outer.Y[i]), Math.Max(maxX, polygon.Outer.X[i]), Math.Max(maxY, polygon.Outer.Y[i]));
            }
        }

        Bounds = (minX, minY, maxX, maxY);
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Gets the code.</summary>
    public string Code { get; }

    /// <summary>Gets the areas, X the longitude and Y the latitude in degrees.</summary>
    public IReadOnlyList<Polygon> Polygons { get; }

    /// <summary>Gets the box of the region in degrees: west, south, east, north.</summary>
    public (double West, double South, double East, double North) Bounds { get; }

    /// <summary>Gets the triangles of an area with its corners and the corners of each ring, cut once when first asked for.</summary>
    internal (double[] X, double[] Y, int[] Triangles, int[] Rings) Triangles(int polygon) => _triangles[polygon] ??= Cut(Polygons[polygon]);

    /// <summary>Takes triangles cut before, as a packed map holds them.</summary>
    internal void SetTriangles(int polygon, (double[] X, double[] Y, int[] Triangles, int[] Rings) triangles) => _triangles[polygon] = triangles;

    private static (double[] X, double[] Y, int[] Triangles, int[] Rings) Cut(Polygon polygon) =>
        GeoTriangulator.Triangulate(([.. polygon.Outer.X], [.. polygon.Outer.Y]), [.. polygon.Holes.Select(hole => (hole.X.ToArray(), hole.Y.ToArray()))]);
}

/// <summary>
/// A map: regions with names and codes, such as the countries of the world, in degrees of longitude and latitude. It
/// comes from a shapefile or from <see cref="WorldMap"/>, and keeps in a compact packed form of its own.
/// </summary>
public sealed class GeoMap
{
    private const uint Magic = 0x4F454754; // "TGEO"
    private const int FormatVersion = 1;
    private readonly Dictionary<string, GeoRegion> _byCode;

    /// <summary>Initializes a new instance of the <see cref="GeoMap"/> class.</summary>
    /// <param name="regions">The regions.</param>
    public GeoMap(IReadOnlyList<GeoRegion> regions)
    {
        Regions = regions ?? throw new ArgumentNullException(nameof(regions));
        _byCode = new Dictionary<string, GeoRegion>(StringComparer.OrdinalIgnoreCase);
        foreach (var region in regions)
        {
            if (region.Code.Length > 0)
            {
                _byCode.TryAdd(region.Code, region);
            }
        }
    }

    /// <summary>Gets the regions.</summary>
    public IReadOnlyList<GeoRegion> Regions { get; }

    /// <summary>Finds a region by its code.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The region, or <see langword="null"/>.</returns>
    public GeoRegion? Find(string code) => _byCode.GetValueOrDefault(code ?? string.Empty);

    /// <summary>
    /// Makes a map of the areas of a shapefile in degrees of longitude and latitude: each shape a region, its outer
    /// rings the areas, the rings inside them their holes.
    /// </summary>
    /// <param name="file">The shapefile, of areas.</param>
    /// <param name="nameField">The field of the names.</param>
    /// <param name="codeField">The field of the codes.</param>
    /// <returns>The map.</returns>
    public static GeoMap FromShapefile(Shapefile file, string nameField = "NAME", string codeField = "ISO_A3")
    {
        ArgumentNullException.ThrowIfNull(file);
        if (file.Kind != ShapeKind.Polygon)
        {
            throw new ArgumentException($"A map needs areas; the shapefile holds {file.Kind} shapes.", nameof(file));
        }

        var regions = new List<GeoRegion>(file.Shapes.Count);
        foreach (var shape in file.Shapes)
        {
            regions.Add(new GeoRegion(shape.Attributes.GetValueOrDefault(nameField, string.Empty), shape.Attributes.GetValueOrDefault(codeField, string.Empty), ToPolygons(shape.Parts)));
        }

        return new GeoMap(regions);
    }

    /// <summary>Reads a map in its packed form.</summary>
    /// <param name="stream">The stream, gzip-packed.</param>
    /// <returns>The map, its areas already cut into triangles.</returns>
    /// <exception cref="InvalidDataException">The stream holds no packed map.</exception>
    public static GeoMap Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var unpacked = new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true);
        using var reader = new BinaryReader(unpacked, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion)
        {
            throw new InvalidDataException("The stream holds no packed map of version 1.");
        }

        var regions = new GeoRegion[reader.ReadInt32()];
        for (var r = 0; r < regions.Length; r++)
        {
            var (name, code) = (reader.ReadString(), reader.ReadString());
            var polygons = new Polygon[reader.ReadInt32()];
            var cut = new (double[], double[], int[], int[])[polygons.Length];
            for (var p = 0; p < polygons.Length; p++)
            {
                var rings = new int[reader.ReadInt32()];
                for (var i = 0; i < rings.Length; i++)
                {
                    rings[i] = reader.ReadInt32();
                }

                var corners = rings.Sum();
                var (x, y) = (new double[corners], new double[corners]);
                for (var i = 0; i < corners; i++)
                {
                    (x[i], y[i]) = (reader.ReadSingle(), reader.ReadSingle());
                }

                var triangles = new int[reader.ReadInt32()];
                for (var i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = reader.ReadInt32();
                }

                var start = 0;
                var parts = new PolygonRing[rings.Length];
                for (var i = 0; i < rings.Length; i++)
                {
                    parts[i] = new PolygonRing(x[start..(start + rings[i])], y[start..(start + rings[i])]);
                    start += rings[i];
                }

                polygons[p] = new Polygon(parts[0], parts[1..]);
                cut[p] = (x, y, triangles, rings);
            }

            regions[r] = new GeoRegion(name, code, polygons);
            for (var p = 0; p < polygons.Length; p++)
            {
                regions[r].SetTriangles(p, cut[p]);
            }
        }

        return new GeoMap(regions);
    }

    /// <summary>
    /// Writes the map in its packed form: corners as floats, areas already cut into triangles, gzip-packed; the form
    /// <see cref="WorldMap"/> keeps the countries in.
    /// </summary>
    /// <param name="stream">The stream.</param>
    public void Save(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var packed = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true);
        using var writer = new BinaryWriter(packed, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(Regions.Count);
        foreach (var region in Regions)
        {
            writer.Write(region.Name);
            writer.Write(region.Code);

            // Areas too small to cut are left out.
            var cut = Enumerable.Range(0, region.Polygons.Count).Select(region.Triangles).Where(polygon => polygon.Rings.Length > 0).ToList();
            writer.Write(cut.Count);
            foreach (var (x, y, triangles, rings) in cut)
            {
                // The corners as the triangles count them: the outer ring, then the holes, each turned as the cutting turned it.
                writer.Write(rings.Length);
                foreach (var ring in rings)
                {
                    writer.Write(ring);
                }

                for (var i = 0; i < x.Length; i++)
                {
                    writer.Write((float)x[i]);
                    writer.Write((float)y[i]);
                }

                writer.Write(triangles.Length);
                foreach (var corner in triangles)
                {
                    writer.Write(corner);
                }
            }
        }
    }

    // The rings of a shape: those turning clockwise are outer rings, the others holes of the outer ring around them.
    private static List<Polygon> ToPolygons(IReadOnlyList<(double[] X, double[] Y)> parts)
    {
        var outers = new List<(PolygonRing Ring, List<PolygonRing> Holes)>();
        var holes = new List<PolygonRing>();
        foreach (var (x, y) in parts)
        {
            if (x.Length < 3)
            {
                continue;
            }

            var area = 0.0;
            for (int i = 0, j = x.Length - 1; i < x.Length; j = i++)
            {
                area += (x[j] - x[i]) * (y[j] + y[i]);
            }

            // The sum is negative for a ring turning clockwise, the outer rings of a shapefile.
            var ring = new PolygonRing(x, y);
            if (area < 0)
            {
                outers.Add((ring, []));
            }
            else
            {
                holes.Add(ring);
            }
        }

        foreach (var hole in holes)
        {
            var home = outers.FindIndex(outer => new Polygon(outer.Ring).Contains(hole.X[0], hole.Y[0]));
            if (home >= 0)
            {
                outers[home].Holes.Add(hole);
            }
            else
            {
                // A ring turned the wrong way outside every area is an area of its own.
                outers.Add((hole, []));
            }
        }

        return [.. outers.Select(outer => new Polygon(outer.Ring, outer.Holes))];
    }
}

/// <summary>How fine the countries of <see cref="WorldMap"/> are.</summary>
public enum MapDetail
{
    /// <summary>1:110 million, for the whole world.</summary>
    Coarse,

    /// <summary>1:50 million, for continents.</summary>
    Medium,

    /// <summary>1:10 million, for countries and regions.</summary>
    Fine,
}

/// <summary>
/// The countries of the world from Natural Earth 5.1.1 (public domain), in three levels of detail, inside the package:
/// usable without the internet. Each region has the name and the code (ADM0_A3) of its country.
/// </summary>
public static class WorldMap
{
    private static readonly Lazy<GeoMap>[] _levels =
    [
        new(() => Read("countries-110m.tgeo.gz")),
        new(() => Read("countries-50m.tgeo.gz")),
        new(() => Read("countries-10m.tgeo.gz")),
    ];

    /// <summary>Gets the countries at a level of detail, read once from the package.</summary>
    /// <param name="detail">The level.</param>
    /// <returns>The map.</returns>
    public static GeoMap Countries(MapDetail detail = MapDetail.Medium) => _levels[(int)detail].Value;

    /// <summary>Gets the countries at every level, coarse to fine, for a map that changes its detail with the zoom.</summary>
    public static IReadOnlyList<GeoMap> CountryLevels => [Countries(MapDetail.Coarse), Countries(MapDetail.Medium), Countries(MapDetail.Fine)];

    /// <summary>Gets the notice of the source of the countries, to show with a map.</summary>
    public static string Attribution => "Made with Natural Earth";

    private static GeoMap Read(string name)
    {
        using var stream = typeof(WorldMap).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"The package lacks {name}.");
        return GeoMap.Load(stream);
    }
}
