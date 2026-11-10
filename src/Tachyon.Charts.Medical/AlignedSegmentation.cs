using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

internal sealed class AlignedSegmentation
{
    private readonly ushort[] _labels;

    public AlignedSegmentation(MedicalVolume target, MedicalSegmentation source)
    {
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
