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
        for (var z = 0; z < Depth; z++)
        {
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var local = new Point3D(x * targetSpacing.X, y * targetSpacing.Y, z * targetSpacing.Z);
                    var patient = Geometry.ToPatient(local, target.Origin, target.Orientation);
                    var inSource = Geometry.FromPatient(patient, source.Origin, source.Orientation);
                    var sx = Nearest(inSource.X, sourceSpacing.X, source.Data.Width);
                    var sy = Nearest(inSource.Y, sourceSpacing.Y, source.Data.Height);
                    var sz = Nearest(inSource.Z, sourceSpacing.Z, source.Data.Depth);
                    if (sx >= 0 && sy >= 0 && sz >= 0)
                    {
                        _labels[((z * Height) + y) * Width + x] = (ushort)source.Data[sx, sy, sz];
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
