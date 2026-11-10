using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.Medical;

internal sealed class MedicalSegmentationLayer
{
    private readonly Dictionary<int, MedicalSegment> _segments;
    private readonly Dictionary<MedicalSegment, Volume3D> _surfaces = [];
    private readonly Dictionary<int, byte> _compact = [];
    private readonly TransferFunction3D? _transfer;
    private readonly Volume3D? _volume;
    private readonly List<Volume3D> _objects = [];

    public MedicalSegmentationLayer(MedicalVolume target, MedicalSegmentation source)
    {
        Source = source;
        Aligned = AlignedSegmentation.For(target, source);
        _segments = source.Segments.ToDictionary(segment => segment.Label);
        var present = Aligned.Present;
        if (source.ThreeDMode == MedicalSegmentation3DMode.Volume && present.Length <= byte.MaxValue)
        {
            for (var i = 0; i < present.Length; i++) _compact[present[i]] = Aligned.CompactValue(i);
            _transfer = new TransferFunction3D(new TransferPoint3D(0, default, 0), new TransferPoint3D(1, default, 0)) { ShowsEditor = false };
            _volume = CreateVolume(target, Aligned.Compact, VolumeMode.Transfer, _transfer);
            _objects.Add(_volume);
        }

        if (source.ThreeDMode == MedicalSegmentation3DMode.Surface)
        {
            AddSurfaces(target, present);
        }

        Refresh3D();
    }

    // A lit boundary surface per label, from a volume that is full inside the label and empty elsewhere.
    private void AddSurfaces(MedicalVolume target, ushort[] present)
    {
        foreach (var label in present)
        {
            if (!_segments.TryGetValue(label, out var segment)) continue;
            var surface = CreateVolume(target, Aligned.Binary(label), VolumeMode.Surface, null);
            surface.IsoValue = byte.MaxValue / 2.0;
            _surfaces[segment] = surface;
            _objects.Add(surface);
        }
    }

    public MedicalSegmentation Source { get; }
    public AlignedSegmentation Aligned { get; }

    public bool TryGetSegment(int label, out MedicalSegment segment) => _segments.TryGetValue(label, out segment!);

    public void AddTo(Scene3D scene)
    {
        foreach (var volume in _objects) scene.Add(volume);
    }

    public bool Owns(object source) => _objects.Any(volume => ReferenceEquals(volume, source));

    public void Refresh3D()
    {
        if (Source.ThreeDMode == MedicalSegmentation3DMode.Volume && _volume is null)
            throw new InvalidOperationException("The 3D volume mode supports at most 255 labels at once. Use surface mode for this segmentation.");

        if (_volume is not null && _transfer is not null)
        {
            _transfer.Replace(CreateTransfer());
            _volume.SetAppearance(Colormaps.Grayscale, new Material3D { IsLit = false }, true);
        }

        foreach (var pair in _surfaces)
        {
            var segment = pair.Key;
            var color = segment.Color;
            pair.Value.SetAppearance(
                Colormap.FromColors($"Segment {segment.Label}", [color, color]),
                new Material3D { Opacity = segment.Opacity, Gloss = 0.15 },
                segment.IsVisible && segment.Opacity > 0);
        }
    }

    private TransferPoint3D[] CreateTransfer()
    {
        var points = new List<TransferPoint3D>(2 + (_compact.Count * 2)) { new(0, default, 0) };
        var previous = new TransferPoint3D(0, default, 0);
        var previousShare = 0.0;
        foreach (var pair in _compact.OrderBy(pair => pair.Value))
        {
            var share = pair.Value / 255.0;
            var boundary = (previousShare + share) / 2;
            points.Add(previous with { Share = boundary });
            var segment = _segments[pair.Key];
            previous = new TransferPoint3D(boundary, segment.Color, segment.IsVisible ? segment.Opacity : 0);
            points.Add(previous);
            previousShare = share;
        }

        points.Add(previous with { Share = 1 });
        return [.. points];
    }

    private static Volume3D CreateVolume(MedicalVolume target, VolumeData data, VolumeMode mode, TransferFunction3D? transfer)
    {
        var spacing = target.VoxelSpacing;
        return new Volume3D(
            data,
            new DoubleRange(0, (data.Width - 1) * spacing.X),
            new DoubleRange(0, (data.Height - 1) * spacing.Y),
            new DoubleRange(0, (data.Depth - 1) * spacing.Z))
        {
            Mode = mode,
            Transfer = transfer,
            ShowsSlices = false,
            ShowsEditor = false,
            IsInterpolated = mode == VolumeMode.Surface,
        };
    }
}
