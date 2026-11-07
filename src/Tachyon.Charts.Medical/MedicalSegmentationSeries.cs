using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

internal sealed class MedicalSegmentationSeries : ChartSeries, IDisposable
{
    private readonly MedicalMprDisplay _display;
    private readonly MedicalMprSlice _slice;
    private readonly Rgba8[] _pixels;
    private readonly DoubleRange _xRange;
    private readonly DoubleRange _yRange;
    private IGpuTexture? _texture;
    private long _uploadedVersion = -1;
    private PixelRect _bounds;

    public MedicalSegmentationSeries(MedicalMprDisplay display, MedicalMprSlice slice, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis)
    {
        _display = display;
        _slice = slice;
        _pixels = new Rgba8[checked(slice.Width * slice.Height)];
        _xRange = new DoubleRange(0, slice.Width * slice.Spacing.Column);
        _yRange = new DoubleRange(0, slice.Height * slice.Spacing.Row);
        Name = "Segmentation";
    }

    public override Rgba8 AccentColor => new(230, 75, 90, 255);
    protected override long Version => _display.SegmentationVersion + _display.PositionVersion;
    protected override int DrawnPointCount => _pixels.Length;
    protected override long SourcePointCount => _pixels.Length;

    protected override bool TryGetDataRange(out DoubleRange x, out DoubleRange y) { x = _xRange; y = _yRange; return true; }
    protected override bool TryGetValueRange(out DoubleRange range) { range = default; return false; }

    protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        _texture ??= device.CreateTexture(new TextureDesc(new PixelSize(_slice.Width, _slice.Height), TextureFormat.Rgba8Unorm));
        var version = Version;
        if (_uploadedVersion != version)
        {
            Fill();
            _texture.Write(MemoryMarshal.AsBytes(_pixels.AsSpan()));
            _uploadedVersion = version;
        }

        _bounds = Place(frame);
    }

    protected override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is not null && _bounds.Width > 0 && _bounds.Height > 0) commands.DrawImage(_texture, _bounds);
    }

    protected override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    { index = -1; x = 0; y = 0; return false; }
    protected override bool TryReadPoint(long index, out double x, out double y) { x = 0; y = 0; return false; }
    protected override bool TryReadValue(long index, out double value) { value = double.NaN; return false; }

    protected override void ReleaseResources() { _texture?.Dispose(); _texture = null; _uploadedVersion = -1; }
    public void Dispose() { ReleaseResources(); GC.SuppressFinalize(this); }

    private void Fill()
    {
        Array.Clear(_pixels);
        var fixedIndex = _slice.FixedIndex;
        foreach (var layer in _display.SegmentationLayers)
        {
            for (var row = 0; row < _slice.Height; row++)
            {
                for (var column = 0; column < _slice.Width; column++)
                {
                    var label = layer.Aligned.Read(_slice.Plane, column, row, fixedIndex);
                    if (label == 0 || !layer.TryGetSegment(label, out var segment) || !segment.IsVisible || segment.Opacity <= 0) continue;
                    var source = segment.Color;
                    var alpha = Math.Clamp(segment.Opacity * (source.A / 255.0), 0, 1);
                    var at = (row * _slice.Width) + column;
                    _pixels[at] = Over(_pixels[at], source, alpha);
                }
            }
        }
    }

    private PixelRect Place(in SeriesFrame frame)
    {
        var x0 = frame.X.ToPixel(_xRange.Min); var x1 = frame.X.ToPixel(_xRange.Max);
        var y0 = frame.Y.ToPixel(_yRange.Min); var y1 = frame.Y.ToPixel(_yRange.Max);
        return new PixelRect((float)Math.Min(x0, x1), (float)Math.Min(y0, y1), (float)Math.Abs(x1 - x0), (float)Math.Abs(y1 - y0));
    }

    private static Rgba8 Over(Rgba8 destination, Rgba8 source, double alpha)
    {
        var da = destination.A / 255.0;
        var outA = alpha + (da * (1 - alpha));
        if (outA <= 0) return default;
        byte Channel(byte sourceChannel, byte destinationChannel) => (byte)Math.Round(((sourceChannel * alpha) + (destinationChannel * da * (1 - alpha))) / outA);
        return new Rgba8(Channel(source.R, destination.R), Channel(source.G, destination.G), Channel(source.B, destination.B), (byte)Math.Round(outA * 255));
    }
}
