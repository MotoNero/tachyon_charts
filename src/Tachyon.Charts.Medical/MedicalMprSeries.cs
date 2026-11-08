using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Medical;

internal sealed class MedicalMprSeries : ChartSeries, IDisposable
{
    private readonly ColormapTexture _table = new();
    private readonly MedicalMprDisplay _display;
    private readonly IMedicalPixels _buffer;
    private readonly ColorAxis _scale;
    private readonly DoubleRange _xRange;
    private readonly DoubleRange _yRange;
    private IGpuTexture? _texture;
    private IGpuTexture? _colors;
    private long _uploadedVersion = -1;
    private PixelRect _bounds;
    private Vector2 _sectionOrigin;
    private Vector2 _sectionSize;

    public MedicalMprSeries(MedicalMprDisplay display, IMedicalPixels buffer, AxisBase xAxis, AxisBase yAxis)
        : this(display, buffer, xAxis, yAxis, CreateColorAxis(display))
    {
    }

    private MedicalMprSeries(MedicalMprDisplay display, IMedicalPixels buffer, AxisBase xAxis, AxisBase yAxis, ColorAxis scale)
        : base(xAxis, yAxis)
    {
        _display = display;
        _buffer = buffer;
        _scale = scale;
        _xRange = new DoubleRange(0, buffer.Width * buffer.Spacing.Column);
        _yRange = new DoubleRange(0, buffer.Height * buffer.Spacing.Row);
        Name = display.Unit;
    }

    public override Rgba8 AccentColor => Colormaps.Grayscale.Sample(0.75);

    protected override long Version => _buffer.Version;

    protected override int DrawnPointCount => checked(_buffer.Width * _buffer.Height);

    protected override long SourcePointCount => (long)_buffer.Width * _buffer.Height;

    protected override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = _xRange;
        y = _yRange;
        return true;
    }

    protected override bool TryGetValueRange(out DoubleRange range)
    {
        range = _display.Source.Data.ValueRange;
        return !range.IsEmpty;
    }

    protected override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        _colors = _table.Update(device, Colormaps.Grayscale);
        if (_texture is null)
        {
            _texture = device.CreateTexture(new TextureDesc(new PixelSize(_buffer.Width, _buffer.Height), TextureFormat.R32Float));
            _uploadedVersion = -1;
        }

        var version = _buffer.Version;
        if (_uploadedVersion != version)
        {
            _buffer.Write(_texture);
            _uploadedVersion = version;
        }

        var window = _display.Window;
        _scale.VisibleRange = new DoubleRange(window.Minimum, window.Maximum);
        (_bounds, _sectionOrigin, _sectionSize) = Place(frame, _xRange, _yRange, Vector2.One);
    }

    protected override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_texture is null || _colors is null || _bounds.Width <= 0 || _bounds.Height <= 0)
        {
            return;
        }

        var style = _scale.CreateRasterStyle() with
        {
            SectionOrigin = _sectionOrigin,
            SectionSize = _sectionSize,
            IsInterpolated = true,
        };
        commands.DrawRaster(_texture, _colors, _bounds, style);
    }

    protected override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        var column = (int)Math.Floor(frame.X.ToData(pixel.X) / _buffer.Spacing.Column);
        var row = (int)Math.Floor(frame.Y.ToData(pixel.Y) / _buffer.Spacing.Row);
        if ((uint)column >= (uint)_buffer.Width || (uint)row >= (uint)_buffer.Height)
        {
            index = -1;
            x = 0;
            y = 0;
            return false;
        }

        index = ((long)row * _buffer.Width) + column;
        x = (column + 0.5) * _buffer.Spacing.Column;
        y = (row + 0.5) * _buffer.Spacing.Row;
        return float.IsFinite(_buffer.Read(column, row));
    }

    protected override bool TryReadPoint(long index, out double x, out double y)
    {
        if (index < 0 || index >= SourcePointCount)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = ((index % _buffer.Width) + 0.5) * _buffer.Spacing.Column;
        y = ((index / _buffer.Width) + 0.5) * _buffer.Spacing.Row;
        return true;
    }

    protected override bool TryReadValue(long index, out double value)
    {
        if (index < 0 || index >= SourcePointCount)
        {
            value = double.NaN;
            return false;
        }

        value = _buffer.Read((int)(index % _buffer.Width), (int)(index / _buffer.Width));
        return double.IsFinite(value);
    }

    protected override void ReleaseResources()
    {
        _texture?.Dispose();
        _texture = null;
        _table.Dispose();
        _colors = null;
        _uploadedVersion = -1;
    }

    public void Dispose()
    {
        ReleaseResources();
        GC.SuppressFinalize(this);
    }

    private static (PixelRect Bounds, Vector2 Origin, Vector2 Size) Place(in SeriesFrame frame, DoubleRange x, DoubleRange y, Vector2 filled)
    {
        var bounds = Clip(frame, x, y);
        var startX = frame.X.ToPixel(x.Min);
        var startY = frame.Y.ToPixel(y.Min);
        var perPixelX = filled.X / (frame.X.ToPixel(x.Max) - startX);
        var perPixelY = filled.Y / (frame.Y.ToPixel(y.Max) - startY);
        return (
            bounds,
            new Vector2((float)((bounds.X - startX) * perPixelX), (float)((bounds.Y - startY) * perPixelY)),
            new Vector2((float)(bounds.Width * perPixelX), (float)(bounds.Height * perPixelY)));
    }

    private static PixelRect Clip(in SeriesFrame frame, DoubleRange x, DoubleRange y)
    {
        var pane = frame.Bounds;
        var (left, right) = Clip(frame.X.ToPixel(x.Min), frame.X.ToPixel(x.Max), pane.X, pane.Width);
        var (top, bottom) = Clip(frame.Y.ToPixel(y.Min), frame.Y.ToPixel(y.Max), pane.Y, pane.Height);
        return new PixelRect(left, top, Math.Max(right - left, 0), Math.Max(bottom - top, 0));
    }

    private static (float Start, float End) Clip(double first, double second, float paneStart, float paneLength)
    {
        var missing = double.IsNaN(first) || double.IsNaN(second);
        var low = missing ? paneStart : Math.Max(Math.Min(first, second), paneStart);
        var high = missing ? paneStart + paneLength : Math.Min(Math.Max(first, second), paneStart + paneLength);
        return ((float)low, (float)high);
    }

    private static ColorAxis CreateColorAxis(MedicalMprDisplay display) => new("medical-mpr-values")
    {
        Colormap = Colormaps.Grayscale,
        VisibleRange = new DoubleRange(display.Window.Minimum, display.Window.Maximum),
        Title = display.Unit,
    };
}
