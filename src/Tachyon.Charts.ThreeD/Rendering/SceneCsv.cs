using System.Globalization;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>
/// Writes the data of a scene that lies inside the axes as CSV, one line per point with object, x, y, z and value:
/// markers, lines, points, surfaces, meshes, maps, columns, waterfalls, point clouds of up to five million points and
/// antenna patterns (azimuth, elevation and gain in x, y and z). Volumes and larger clouds stay out: they hold up to
/// hundreds of millions of values, which no table program opens.
/// </summary>
internal static class SceneCsv
{
    /// <summary>The largest cloud that goes into the table.</summary>
    public const int MostCloudPoints = 5_000_000;

    /// <summary>Writes the table.</summary>
    /// <param name="scene">The scene.</param>
    /// <param name="writer">Receives the text.</param>
    public static void Write(Scene3D scene, TextWriter writer)
    {
        var area = new Area(scene.XAxis.VisibleRange, scene.YAxis.VisibleRange, scene.ZAxis.VisibleRange);
        writer.Write("object,x,y,z,value\r\n");
        var markers = scene.Markers;
        for (var i = 0; i < markers.Count; i++)
        {
            Row(writer, area, "marker " + (i + 1).ToString(CultureInfo.InvariantCulture), markers[i].Position, markers[i].Value ?? double.NaN);
        }

        for (var i = 0; i < scene.Lines.Count; i++)
        {
            Points(writer, area, "line " + (i + 1).ToString(CultureInfo.InvariantCulture), scene.Lines[i].Points);
        }

        for (var i = 0; i < scene.Scatters.Count; i++)
        {
            Points(writer, area, "points " + (i + 1).ToString(CultureInfo.InvariantCulture), scene.Scatters[i].Points);
        }

        for (var i = 0; i < scene.Surfaces.Count; i++)
        {
            var surface = scene.Surfaces[i];
            var name = "surface " + (i + 1).ToString(CultureInfo.InvariantCulture);
            var (columns, rows) = (surface.Data.Width, surface.Data.Height);
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    double value = surface.Data[column, row];
                    var place = new Point3D(ScenePicker.Across(surface.X.Min, surface.X.Max, column, columns), ScenePicker.Across(surface.Y.Min, surface.Y.Max, row, rows), value);
                    Row(writer, area, name, place, value);
                }
            }
        }

        for (var i = 0; i < scene.Maps.Count; i++)
        {
            var map = scene.Maps[i];
            var name = "map " + (i + 1).ToString(CultureInfo.InvariantCulture);
            for (var j = 0; j < map.X.Length; j++)
            {
                Row(writer, area, name, new Point3D(map.X[j], map.Y[j], map.Z[j]), map.Z[j]);
            }
        }

        for (var i = 0; i < scene.Meshes.Count; i++)
        {
            var name = "mesh " + (i + 1).ToString(CultureInfo.InvariantCulture);
            var vertices = scene.Meshes[i].Vertices;
            for (var j = 0; j < vertices.Count; j++)
            {
                Row(writer, area, name, vertices[j].Position, vertices[j].Position.Z);
            }
        }

        for (var i = 0; i < scene.Waterfalls.Count; i++)
        {
            Waterfall(writer, area, "waterfall " + (i + 1).ToString(CultureInfo.InvariantCulture), scene.Waterfalls[i]);
        }

        for (var i = 0; i < scene.Clouds.Count; i++)
        {
            var cloud = scene.Clouds[i];
            if (!cloud.IsLoaded || cloud.Count > MostCloudPoints)
            {
                continue;
            }

            var name = "cloud " + (i + 1).ToString(CultureInfo.InvariantCulture);
            for (var j = 0; j < cloud.Count; j++)
            {
                Row(writer, area, name, new Point3D(cloud.X[j], cloud.Y[j], cloud.Z[j]), double.NaN);
            }
        }

        // An antenna pattern has its own coordinates: the angles and the gain, not places in the box.
        for (var i = 0; i < scene.Antennas.Count; i++)
        {
            var antenna = scene.Antennas[i];
            var name = "antenna " + (i + 1).ToString(CultureInfo.InvariantCulture);
            for (var e = 0; e < antenna.Elevations.Length; e++)
            {
                for (var a = 0; a < antenna.Azimuths.Length; a++)
                {
                    var gain = antenna.Gains[e, a];
                    Row(writer, Area.Everything, name, new Point3D(antenna.Azimuths[a], antenna.Elevations[e], gain), gain);
                }
            }
        }

        for (var i = 0; i < scene.Bars.Count; i++)
        {
            var bars = scene.Bars[i];
            var name = "columns " + (i + 1).ToString(CultureInfo.InvariantCulture);
            var (columns, rows) = (bars.Data.Width, bars.Data.Height);
            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    double value = bars.Data[column, row];
                    var x = bars.X.Min + ((column + 0.5) * (bars.X.Max - bars.X.Min) / columns);
                    var y = bars.Y.Min + ((row + 0.5) * (bars.Y.Max - bars.Y.Min) / rows);
                    Row(writer, area, name, new Point3D(x, y, value), value);
                }
            }
        }
    }

    // The rows of the ring that the waterfall shows, the oldest first.
    private static void Waterfall(TextWriter writer, in Area area, string name, Waterfall3D waterfall)
    {
        var (columns, rows) = (waterfall.Data.Columns, waterfall.Data.Rows);
        var first = waterfall.Data.TotalRows - rows;
        for (var row = 0; row < rows; row++)
        {
            var y = ScenePicker.Across(waterfall.Y.Min, waterfall.Y.Max, row, rows);
            for (var column = 0; column < columns; column++)
            {
                if (waterfall.Data.TryRead(first + row, column, out var value))
                {
                    Row(writer, area, name, new Point3D(ScenePicker.Across(waterfall.X.Min, waterfall.X.Max, column, columns), y, value), value);
                }
            }
        }
    }

    // The points of a ring, the oldest first.
    private static void Points(TextWriter writer, in Area area, string name, Points3D points)
    {
        var (total, count) = (points.Total, points.Count);
        var first = total > points.Capacity ? (int)(total % points.Capacity) : 0;
        for (var i = 0; i < count; i++)
        {
            var place = points.GetSlot((first + i) % points.Capacity);
            Row(writer, area, name, place, double.NaN);
        }
    }

    private static void Row(TextWriter writer, in Area area, string name, in Point3D place, double value)
    {
        if (!area.Contains(place))
        {
            return;
        }

        writer.Write(name);
        writer.Write(',');
        writer.Write(Number(place.X));
        writer.Write(',');
        writer.Write(Number(place.Y));
        writer.Write(',');
        writer.Write(Number(place.Z));
        writer.Write(',');
        writer.Write(double.IsNaN(value) ? string.Empty : Number(value));
        writer.Write("\r\n");
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private readonly record struct Area(DoubleRange X, DoubleRange Y, DoubleRange Z)
    {
        public static Area Everything { get; } = new(new DoubleRange(double.MinValue, double.MaxValue), new DoubleRange(double.MinValue, double.MaxValue), new DoubleRange(double.MinValue, double.MaxValue));

        public bool Contains(in Point3D place) => Inside(X, place.X) && Inside(Y, place.Y) && Inside(Z, place.Z);

        private static bool Inside(DoubleRange range, double value) =>
            value >= Math.Min(range.Min, range.Max) && value <= Math.Max(range.Min, range.Max);
    }
}
