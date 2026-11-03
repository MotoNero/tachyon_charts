using System.Numerics;
using System.Runtime.InteropServices;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Instruments;

/// <summary>How old waveforms stay on the display.</summary>
public enum ScopePersistence
{
    /// <summary>Only the newest waveform.</summary>
    Off,

    /// <summary>Old waveforms fade within <see cref="ScopeDisplay.PersistenceSeconds"/>.</summary>
    Variable,

    /// <summary>Old waveforms stay until they are cleared.</summary>
    Infinite,

    /// <summary>Old waveforms stay, and the color tells how often a pixel was hit.</summary>
    ColorGrade,
}

/// <summary>Counts how often a waveform hit each pixel.</summary>
/// <param name="width">The width in pixels.</param>
/// <param name="height">The height in pixels.</param>
public sealed class PersistenceBuffer(int width, int height)
{
    private readonly float[] _hits = new float[width * height];
    private readonly int[] _marks = new int[width * height];
    private int _pass;

    /// <summary>Gets the width in pixels.</summary>
    public int Width { get; } = width;

    /// <summary>Gets the height in pixels.</summary>
    public int Height { get; } = height;

    /// <summary>Gets the counts, row by row from the top.</summary>
    public ReadOnlySpan<float> Hits => _hits;

    /// <summary>Gets the highest count.</summary>
    public float Maximum { get; private set; }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        Array.Clear(_hits);
        Maximum = 0;
    }

    /// <summary>Lets the counts fade.</summary>
    /// <param name="factor">What remains of each count.</param>
    public void Fade(float factor)
    {
        var hits = _hits.AsSpan();
        var size = Vector<float>.Count;
        var i = 0;
        for (; i <= hits.Length - size; i += size)
        {
            (new Vector<float>(hits.Slice(i, size)) * factor).CopyTo(hits.Slice(i, size));
        }

        for (; i < hits.Length; i++)
        {
            hits[i] *= factor;
        }
        Maximum *= factor;
    }

    /// <summary>Adds a waveform. A pixel counts once per waveform, however often the line passes it.</summary>
    /// <param name="points">The points in pixels of this buffer, ascending in X. NaN interrupts the line.</param>
    public void Add(ReadOnlySpan<Vector2> points)
    {
        _pass++;
        var hasPrevious = false;
        var previous = default(Vector2);
        foreach (var point in points)
        {
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y))
            {
                hasPrevious = false;
                continue;
            }

            if (hasPrevious)
            {
                AddSegment(previous, point);
            }

            previous = point;
            hasPrevious = true;
        }
    }

    private void AddSegment(Vector2 from, Vector2 to)
    {
        var firstColumn = Math.Max((int)MathF.Floor(MathF.Min(from.X, to.X)), 0);
        var lastColumn = Math.Min((int)MathF.Floor(MathF.Max(from.X, to.X)), Width - 1);
        var run = to.X - from.X;
        for (var column = firstColumn; column <= lastColumn; column++)
        {
            // The part of the segment inside this column.
            var start = run == 0 ? 0 : Math.Clamp((column - from.X) / run, 0, 1);
            var end = run == 0 ? 1 : Math.Clamp((column + 1 - from.X) / run, 0, 1);
            var y0 = from.Y + ((to.Y - from.Y) * start);
            var y1 = from.Y + ((to.Y - from.Y) * end);
            var firstRow = Math.Max((int)MathF.Floor(MathF.Min(y0, y1)), 0);
            var lastRow = Math.Min((int)MathF.Floor(MathF.Max(y0, y1)), Height - 1);
            for (var row = firstRow; row <= lastRow; row++)
            {
                var index = (row * Width) + column;
                if (_marks[index] != _pass)
                {
                    _marks[index] = _pass;
                    var value = _hits[index] + 1;
                    _hits[index] = value;
                    Maximum = MathF.Max(Maximum, value);
                }
            }
        }
    }
}

/// <summary>Shows the history of one channel behind its line, computed on the CPU for all backends.</summary>
internal sealed class ScopePersistenceSeries(ScopeDisplay display, ScopeChannel channel, AxisBase xAxis, AxisBase yAxis) : ChartSeries(xAxis, yAxis), IDisposable
{
    private const int MaximumWidth = 1920;
    private const int MaximumHeight = 1080;

    private readonly ColormapTexture _table = new();
    private readonly ColorAxis _scale = new("persistence") { BelowColor = default(Rgba8) };
    private PersistenceBuffer? _buffer;
    private Vector2[] _points = [];
    private IGpuTexture? _texture;
    private IGpuTexture? _colors;
    private long _acquisition = -1;
    private long _scaleVersion = -1;
    private ScopePersistence _mode;
    private long _lastTime;

    public override Rgba8 AccentColor => channel.Color;

    protected internal override long Version => display.Version;

    public void Dispose()
    {
        _table.Dispose();
        _texture?.Dispose();
        _texture = null;
        _colors = null;
        _acquisition = -1;
    }

    protected internal override void ReleaseResources() => Dispose();

    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = default;
        y = default;
        return false;
    }

    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(device);
        var mode = display.Persistence;
        if (mode == ScopePersistence.Off)
        {
            _buffer = null;
            return;
        }

        var width = Math.Clamp((int)frame.Bounds.Width, 1, MaximumWidth);
        var height = Math.Clamp((int)frame.Bounds.Height, 1, MaximumHeight);
        if (_buffer is null || _buffer.Width != width || _buffer.Height != height || mode != _mode || _scaleVersion != display.ScaleVersion)
        {
            _buffer = new PersistenceBuffer(width, height);
            _texture?.Dispose();
            _texture = device.CreateTexture(new TextureDesc(new PixelSize(width, height), TextureFormat.R32Float));
            _mode = mode;
            _scaleVersion = display.ScaleVersion;
        }

        var acquisition = channel.AcquisitionCount;
        if (acquisition != _acquisition)
        {
            _acquisition = acquisition;
            Accumulate(mode);
            _texture!.Write(MemoryMarshal.AsBytes(_buffer.Hits));
        }

        _colors = _table.Update(device, mode == ScopePersistence.ColorGrade ? ScopeColormaps.Grade : channel.FadeColormap);
    }

    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (display.Persistence != ScopePersistence.Off && _texture is not null && _colors is not null && _buffer is { Maximum: > 0 })
        {
            _scale.VisibleRange = new DoubleRange(0.05, Math.Max(_buffer.Maximum, 1));
            commands.DrawRaster(_texture, _colors, frame.Bounds, _scale.CreateRasterStyle());
        }
    }

    private void Accumulate(ScopePersistence mode)
    {
        var buffer = _buffer!;
        if (mode == ScopePersistence.Variable)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var seconds = _lastTime == 0 ? 0 : System.Diagnostics.Stopwatch.GetElapsedTime(_lastTime, now).TotalSeconds;
            _lastTime = now;
            buffer.Fade((float)Math.Exp(-seconds / display.PersistenceSeconds));
        }

        var snapshot = channel.Data.GetSnapshot();
        var time = snapshot.X.Span;
        var values = snapshot.Y.Span;
        if (_points.Length < time.Length)
        {
            _points = new Vector2[time.Length];
        }

        var range = display.GetTimeRange();
        var volts = channel.GetRange();
        var scaleX = (float)(buffer.Width / (range.Max - range.Min));
        var scaleY = (float)(buffer.Height / (volts.Max - volts.Min));
        for (var i = 0; i < time.Length; i++)
        {
            _points[i] = new Vector2((float)((time[i] - range.Min) * scaleX), (float)((volts.Max - values[i]) * scaleY));
        }

        buffer.Add(_points.AsSpan(0, time.Length));
    }
}

/// <summary>The colors of persistence.</summary>
public static class ScopeColormaps
{
    /// <summary>Seven steps from rare to frequent, as on an instrument.</summary>
    public static Colormap Grade { get; } = CreateGrade();

    /// <summary>Creates the fading colors of a channel.</summary>
    /// <param name="color">The color of the channel.</param>
    /// <returns>Colors from transparent to the full color.</returns>
    public static Colormap Fade(Rgba8 color) => Colormap.FromColors("Fade", [color with { A = 0 }, color with { A = 150 }]);

    private static Colormap CreateGrade()
    {
        ReadOnlySpan<Rgba8> steps =
        [
            new(40, 60, 200, 255), new(40, 150, 220, 255), new(40, 200, 110, 255), new(190, 220, 50, 255),
            new(250, 170, 40, 255), new(240, 70, 50, 255), new(255, 240, 240, 255),
        ];
        var stops = new Rgba8[Colormap.EntryCount];
        for (var i = 0; i < stops.Length; i++)
        {
            stops[i] = steps[i * steps.Length / stops.Length];
        }

        return Colormap.FromColors("Grade", stops);
    }
}
