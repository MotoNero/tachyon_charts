using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

internal sealed class AlignedSegmentation
{
    // Aligning labels onto a volume takes a pass over every voxel. A display made anew for the same labels and volume, as
    // an application that rebuilds its charts does, takes the alignment from here; the geometry of both must match.
    private static readonly ConditionalWeakTable<VolumeData, AlignedSegmentation> _known = [];

    private readonly ushort[] _labels;
    private readonly (VolumeData Target, Point3D TargetSpacing, Point3D TargetOrigin, PatientOrientation TargetOrientation, Point3D Spacing, Point3D Origin, PatientOrientation Orientation) _key;
    private readonly ConcurrentDictionary<ushort, VolumeData> _binary = new();
    private ushort[]? _present;
    private VolumeData? _compact;

    /// <summary>Gets the labels of a segmentation aligned onto a volume, aligned once for the pair.</summary>
    /// <param name="target">The volume.</param>
    /// <param name="source">The segmentation.</param>
    /// <returns>The aligned labels.</returns>
    public static AlignedSegmentation For(MedicalVolume target, MedicalSegmentation source)
    {
        if (_known.TryGetValue(source.Data, out var known) && known._key == Key(target, source))
        {
            return known;
        }

        var aligned = new AlignedSegmentation(target, source);
        _known.AddOrUpdate(source.Data, aligned);
        return aligned;
    }

    private static (VolumeData, Point3D, Point3D, PatientOrientation, Point3D, Point3D, PatientOrientation) Key(MedicalVolume target, MedicalSegmentation source) =>
        (target.Data, target.VoxelSpacing, target.Origin, target.Orientation, source.VoxelSpacing, source.Origin, source.Orientation);

    public AlignedSegmentation(MedicalVolume target, MedicalSegmentation source)
    {
        _key = Key(target, source);
        Width = target.Data.Width;
        Height = target.Data.Height;
        Depth = target.Data.Depth;
        _labels = new ushort[checked(Width * Height * Depth)];
        var targetSpacing = target.VoxelSpacing;
        var sourceSpacing = source.VoxelSpacing;

        // Both mappings are rotations and shifts, so a voxel of the target lies in the source at a start plus a step per
        // index; that saves two transforms per voxel of volumes with millions of them.
        Point3D InSource(double x, double y, double z) =>
            Geometry.FromPatient(Geometry.ToPatient(new Point3D(x * targetSpacing.X, y * targetSpacing.Y, z * targetSpacing.Z), target.Origin, target.Orientation), source.Origin, source.Orientation);
        var start = InSource(0, 0, 0);
        var (stepX, stepY, stepZ) = (InSource(1, 0, 0) - start, InSource(0, 1, 0) - start, InSource(0, 0, 1) - start);
        var data = source.Data;
        for (var z = 0; z < Depth; z++)
        {
            for (var y = 0; y < Height; y++)
            {
                var row = start + (stepY * y) + (stepZ * z);
                for (var x = 0; x < Width; x++)
                {
                    var inSource = row + (stepX * x);
                    var sx = Nearest(inSource.X, sourceSpacing.X, data.Width);
                    var sy = Nearest(inSource.Y, sourceSpacing.Y, data.Height);
                    var sz = Nearest(inSource.Z, sourceSpacing.Z, data.Depth);
                    if (sx >= 0 && sy >= 0 && sz >= 0)
                    {
                        _labels[((z * Height) + y) * Width + x] = (ushort)data[sx, sy, sz];
                    }
                }
            }
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int Depth { get; }

    public ushort this[int x, int y, int z] => _labels[((z * Height) + y) * Width + x];

    public ReadOnlySpan<ushort> Labels => _labels;

    /// <summary>Gets the labels other than 0 that occur, in order; a table of all 65 536 labels instead of sorting millions of voxels.</summary>
    public ushort[] Present => LazyInitializer.EnsureInitialized(ref _present, () =>
    {
        var seen = new bool[ushort.MaxValue + 1];
        foreach (var label in _labels) seen[label] = true;
        var present = new List<ushort>();
        for (var label = 1; label < seen.Length; label++)
        {
            if (seen[label]) present.Add((ushort)label);
        }

        return [.. present];
    });

    /// <summary>Gets the share byte of each present label: spread evenly over 1 to 255, so that a transfer function can color each.</summary>
    /// <param name="index">The index of the label in <see cref="Present"/>.</param>
    /// <returns>The byte.</returns>
    public byte CompactValue(int index) => (byte)Math.Round(255.0 * (index + 1) / Present.Length);

    /// <summary>Gets one byte per voxel with the <see cref="CompactValue"/> of its label, made once.</summary>
    public VolumeData Compact => LazyInitializer.EnsureInitialized(ref _compact, () =>
    {
        var table = new byte[ushort.MaxValue + 1];
        var present = Present;
        for (var i = 0; i < present.Length; i++) table[present[i]] = CompactValue(i);
        var values = new byte[_labels.Length];
        for (var i = 0; i < values.Length; i++) values[i] = table[_labels[i]];
        return new VolumeData(values, Width, Height, Depth);
    });

    /// <summary>Gets a volume full inside a label and empty elsewhere, made once per label.</summary>
    /// <param name="label">The label.</param>
    /// <returns>The volume.</returns>
    public VolumeData Binary(ushort label) => _binary.GetOrAdd(label, wanted =>
    {
        var binary = new byte[_labels.Length];
        for (var i = 0; i < binary.Length; i++) binary[i] = _labels[i] == wanted ? byte.MaxValue : (byte)0;
        return new VolumeData(binary, Width, Height, Depth);
    });

    public ushort Read(MedicalMprPlane plane, int column, int row, int fixedIndex) => plane switch
    {
        MedicalMprPlane.Axial => this[column, row, fixedIndex],
        MedicalMprPlane.Coronal => this[column, fixedIndex, Depth - 1 - row],
        MedicalMprPlane.Sagittal => this[fixedIndex, column, Depth - 1 - row],
        _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, null),
    };

    private static int Nearest(double position, double spacing, int count)
    {
        var voxel = position / spacing;
        if (voxel < -0.5 || voxel > count - 0.5) return -1;
        return Math.Clamp((int)Math.Floor(voxel + 0.5), 0, count - 1);
    }
}
