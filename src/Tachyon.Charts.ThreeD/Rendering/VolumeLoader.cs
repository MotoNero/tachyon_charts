using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>Brings a volume to the GPU slice by slice, so that no frame has to wait for all of it.</summary>
/// <param name="volume">The volume.</param>
/// <param name="bytesPerFrame">How much may go per frame.</param>
internal sealed class VolumeLoader(Volume3D volume, int bytesPerFrame = 1 << 23)
{
    private float[] _shares = [];
    private int _next;

    /// <summary>Brings the next slices to the GPU.</summary>
    /// <param name="target">The texture.</param>
    public void Load(IVolumeTarget target)
    {
        var data = volume.Data;
        if (_next >= data.Depth)
        {
            return;
        }

        var bytesPerSlice = data.Width * data.Height * (data.IsBytes ? 1 : sizeof(float));
        for (var written = 0; written < bytesPerFrame && _next < data.Depth; _next++)
        {
            // A volume reduced on the GPU skips the slices it does not keep.
            if (target.Keeps(_next))
            {
                Write(data, target, _next);
                written += bytesPerSlice;
            }
        }

        volume.SetLoaded(_next);
        volume.IsReady = _next >= data.Depth;
        volume.Touch();
    }

    private void Write(VolumeData data, IVolumeTarget target, int z)
    {
        if (data.IsBytes)
        {
            target.Write(z, data.GetByteSlice(z));
            return;
        }

        if (_shares.Length == 0)
        {
            _shares = new float[data.Width * data.Height];
        }

        data.WriteShares(z, _shares);
        target.Write(z, _shares);
    }
}
