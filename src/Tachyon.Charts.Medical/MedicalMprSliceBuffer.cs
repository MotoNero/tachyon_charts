using Tachyon.Charts.Core.Data;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

/// <summary>An orthogonal view through a medical volume.</summary>
public enum MedicalMprPlane
{
    /// <summary>A view across the slice axis.</summary>
    Axial,

    /// <summary>A view from the front across columns and slices.</summary>
    Coronal,

    /// <summary>A view from the side across rows and slices.</summary>
    Sagittal,
}

internal sealed class MedicalMprSliceBuffer
{
    private readonly MedicalVolume _volume;
    private readonly float[] _values;

    public MedicalMprSliceBuffer(MedicalVolume volume, MedicalMprPlane plane)
    {
        _volume = volume ?? throw new ArgumentNullException(nameof(volume));
        Plane = plane;
        var data = volume.Data;
        (Width, Height, Spacing, Orientation) = plane switch
        {
            MedicalMprPlane.Axial => (data.Width, data.Height, new ImageSpacing(volume.VoxelSpacing.Y, volume.VoxelSpacing.X), volume.Orientation),
            MedicalMprPlane.Coronal => (data.Width, data.Depth, new ImageSpacing(volume.VoxelSpacing.Z, volume.VoxelSpacing.X),
                new PatientOrientation(volume.Orientation.Across, volume.Orientation.Normal * -1)),
            MedicalMprPlane.Sagittal => (data.Height, data.Depth, new ImageSpacing(volume.VoxelSpacing.Z, volume.VoxelSpacing.Y),
                new PatientOrientation(volume.Orientation.Down, volume.Orientation.Normal * -1)),
            _ => throw new ArgumentOutOfRangeException(nameof(plane), plane, null),
        };
        _values = new float[checked(Width * Height)];
        Data = new HeatmapData(Width, Height);
    }

    public MedicalMprPlane Plane { get; }

    public int Width { get; }

    public int Height { get; }

    public ImageSpacing Spacing { get; }

    public PatientOrientation Orientation { get; }

    public HeatmapData Data { get; }

    public int FixedIndex { get; private set; } = -1;

    public void Update(Point3D position)
    {
        var data = _volume.Data;
        var spacing = _volume.VoxelSpacing;
        var fixedIndex = Plane switch
        {
            MedicalMprPlane.Axial => Index(position.Z, spacing.Z, data.Depth),
            MedicalMprPlane.Coronal => Index(position.Y, spacing.Y, data.Height),
            MedicalMprPlane.Sagittal => Index(position.X, spacing.X, data.Width),
            _ => throw new InvalidOperationException(),
        };
        if (fixedIndex == FixedIndex)
        {
            return;
        }

        FixedIndex = fixedIndex;
        Fill(data, fixedIndex);
        Data.SetValues(_values);
    }

    public Point3D ToVolumePosition(double x, double y, Point3D current)
    {
        var maximumZ = (_volume.Data.Depth - 1) * _volume.VoxelSpacing.Z;
        var column = x - (Spacing.Column / 2);
        var row = y - (Spacing.Row / 2);
        return Plane switch
        {
            MedicalMprPlane.Axial => new Point3D(column, row, current.Z),
            MedicalMprPlane.Coronal => new Point3D(column, current.Y, maximumZ - row),
            MedicalMprPlane.Sagittal => new Point3D(current.X, column, maximumZ - row),
            _ => throw new InvalidOperationException(),
        };
    }

    public (double X, double Y) FromVolumePosition(Point3D position)
    {
        var maximumZ = (_volume.Data.Depth - 1) * _volume.VoxelSpacing.Z;
        return Plane switch
        {
            MedicalMprPlane.Axial => (position.X + (Spacing.Column / 2), position.Y + (Spacing.Row / 2)),
            MedicalMprPlane.Coronal => (position.X + (Spacing.Column / 2), maximumZ - position.Z + (Spacing.Row / 2)),
            MedicalMprPlane.Sagittal => (position.Y + (Spacing.Column / 2), maximumZ - position.Z + (Spacing.Row / 2)),
            _ => throw new InvalidOperationException(),
        };
    }

    private static int Index(double position, double spacing, int count)
    {
        if (!double.IsFinite(position))
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "The position must be finite.");
        }

        var voxel = position / spacing;
        if (voxel <= 0)
        {
            return 0;
        }

        if (voxel >= count - 1)
        {
            return count - 1;
        }

        return (int)Math.Floor(voxel + 0.5);
    }

    private void Fill(VolumeData data, int fixedIndex)
    {
        for (var row = 0; row < Height; row++)
        {
            for (var column = 0; column < Width; column++)
            {
                var value = Plane switch
                {
                    MedicalMprPlane.Axial => data[column, row, fixedIndex],
                    MedicalMprPlane.Coronal => data[column, fixedIndex, data.Depth - 1 - row],
                    MedicalMprPlane.Sagittal => data[fixedIndex, column, data.Depth - 1 - row],
                    _ => throw new InvalidOperationException(),
                };
                _values[(row * Width) + column] = (float)value;
            }
        }
    }
}
