using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A digital signal as levels and edges: low and high at two values of the Y axis, so that many channels stand one
/// above the other. However many points a view holds, each pixel column asks the source only for its level and whether
/// the level changes inside it; a column with a change shows an edge, so no edge is lost. Narrow views draw every change.
/// </summary>
public sealed class DigitalSeries : ChartSeries, IDisposable, Overlays.IRefiningSeries
{
    private readonly IDigitalSource _source;
    private Vector2[] _vertices = new Vector2[4096];
    private byte[] _states = new byte[2048];
    private IGpuBuffer? _buffer;
    private int _count;
    private long _appearanceVersion;
    private long _builtVersion = -1;
    private SeriesFrame _builtFrame;
    private Rgba8 _color = new(80, 200, 120, 255);
    private double _thickness = 1;
    private double _low;
    private double _high = 1;

    /// <summary>Starts a series.</summary>
    /// <param name="source">The signal.</param>
    /// <param name="xAxis">The X axis.</param>
    /// <param name="yAxis">The Y axis.</param>
    public DigitalSeries(IDigitalSource source, AxisBase xAxis, AxisBase yAxis)
        : base(xAxis, yAxis) => _source = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>Gets the signal.</summary>
    public IDigitalSource Source => _source;

    /// <summary>Gets or sets the value of the Y axis for the low level.</summary>
    public double Low
    {
        get => _low;
        set => Change(ref _low, value);
    }

    /// <summary>Gets or sets the value of the Y axis for the high level.</summary>
    public double High
    {
        get => _high;
        set => Change(ref _high, value);
    }

    /// <summary>Gets or sets the color.</summary>
    public Rgba8 Color
    {
        get => _color;
        set
        {
            _color = value;
            Interlocked.Increment(ref _appearanceVersion);
        }
    }

    /// <summary>Gets or sets the thickness in device independent units.</summary>
    public double Thickness
    {
        get => _thickness;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The thickness must be positive and finite.");
            }

            _thickness = value;
            Interlocked.Increment(ref _appearanceVersion);
        }
    }

    /// <summary>Gets how far the index of packed bits is built, from 0 to 1; other signals are complete at once.</summary>
    public double Progress => _source is PackedBits { IsComplete: false } bits ? Math.Min(bits.Progress, 0.99) : 1;

    /// <inheritdoc />
    public override Rgba8 AccentColor => _color;

    /// <inheritdoc />
    protected internal override long Version => _source.Version + Volatile.Read(ref _appearanceVersion);

    /// <inheritdoc />
    protected internal override int DrawnPointCount => _count;

    /// <inheritdoc />
    protected internal override long SourcePointCount => _source.Count;

    /// <summary>Gets the corners of the line of the last frame, in pixels, for tests.</summary>
    internal ReadOnlySpan<Vector2> Vertices => _vertices.AsSpan(0, _count);

    /// <inheritdoc />
    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    /// <inheritdoc />
    protected internal override void ReleaseResources()
    {
        Dispose();
        _builtVersion = -1;
    }

    /// <inheritdoc />
    protected internal override bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        var count = _source.Count;
        var origin = Origin();
        x = count == 0 ? DoubleRange.Empty : new DoubleRange(_source.XStart - origin, _source.XStart + ((count - 1) * _source.XStep) - origin);
        y = new DoubleRange(Math.Min(_low, _high), Math.Max(_low, _high));
        return count > 0;
    }

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y)
    {
        if (index < 0 || index >= _source.Count)
        {
            (x, y) = (0, 0);
            return false;
        }

        x = _source.XStart + (index * _source.XStep) - Origin();
        y = _source.LevelAt(index) ? _high : _low;
        return true;
    }

    /// <inheritdoc />
    /// <remarks>The point at the place of the pointer; with <see cref="HitTestMode.Nearest"/> its level must lie near the pointer too.</remarks>
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        (index, x, y) = (-1, 0, 0);
        var count = _source.Count;
        if (count == 0 || !(_source.XStep > 0))
        {
            return false;
        }

        var nearest = (long)Math.Round((frame.X.ToData(pixel.X) + Origin() - _source.XStart) / _source.XStep);
        if (nearest < 0 || nearest >= count || !TryReadPoint(nearest, out x, out y))
        {
            return false;
        }

        var dy = mode == HitTestMode.NearestX ? 0 : frame.Y.ToPixel(y) - pixel.Y;
        if (Math.Abs(dy) > maximumDistance)
        {
            return false;
        }

        index = nearest;
        return true;
    }

    /// <inheritdoc />
    protected internal override void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
        var version = Version;
        if (version == _builtVersion && frame == _builtFrame)
        {
            return;
        }

        (_builtVersion, _builtFrame, _count) = (version, frame, 0);
        var count = _source.Count;
        if (count > 0 && _source.XStep > 0)
        {
            var origin = Origin();
            var visible = XAxis.VisibleRange;
            var first = Math.Clamp((long)Math.Floor((visible.Min + origin - _source.XStart) / _source.XStep) - 1, 0, count - 1);
            var last = Math.Clamp((long)Math.Ceiling((visible.Max + origin - _source.XStart) / _source.XStep) + 2, first + 1, count);
            var columns = Math.Max((int)MathF.Ceiling(frame.Bounds.Width), 1);
            if (last - first <= columns * 2L)
            {
                AddChanges(frame, first, last, origin);
            }
            else
            {
                AddColumns(frame, first, last, columns, origin);
            }
        }

        if (_count > 0)
        {
            if (_buffer is null || _buffer.SizeInBytes < _count * 8L)
            {
                _buffer?.Dispose();
                _buffer = device.CreateBuffer(new BufferDesc(Math.Max(_count * 8L, 4096), BufferUsage.Vertex));
            }

            _buffer.Write<Vector2>(0, _vertices.AsSpan(0, _count));
        }
    }

    /// <inheritdoc />
    protected internal override void Draw(ICommandList commands, in SeriesFrame frame)
    {
        if (_count >= 2 && _buffer is not null)
        {
            var thickness = IsHighlighted ? _thickness + 1.5 : _thickness;
            commands.DrawPolyline(_buffer, 0, _count, new PolylineStyle((float)(thickness * frame.DpiScale), _color) { Clip = frame.Bounds });
        }
    }

    // Few points: every change as a step.
    private void AddChanges(in SeriesFrame frame, long first, long last, double origin)
    {
        var level = _source.LevelAt(first);
        var (low, high) = Levels(frame);
        AddVertex(X(frame, first, origin), level ? high : low);
        for (var change = _source.NextChange(first + 1, last); change >= 0; change = _source.NextChange(change + 1, last))
        {
            var x = X(frame, change, origin);
            AddVertex(x, level ? high : low);
            level = !level;
            AddVertex(x, level ? high : low);
        }

        AddVertex(X(frame, last - 1, origin), level ? high : low);
    }

    // Many points: each pixel column shows its level, and an edge when the level changes inside it.
    private void AddColumns(in SeriesFrame frame, long first, long last, int columns, double origin)
    {
        if (_states.Length < columns)
        {
            _states = new byte[columns];
        }

        var states = _states.AsSpan(0, columns);
        Describe(first, last, states);
        var (low, high) = Levels(frame);
        var span = last - first;
        var pen = (states[0] & 1) != 0;
        AddVertex(X(frame, first, origin), pen ? high : low);
        for (var column = 0; column < columns; column++)
        {
            var start = first + (span * column / columns);
            var end = first + (span * (column + 1) / columns);
            if (end <= start)
            {
                continue;
            }

            // A change right at the start of the column: the step stands at its left edge.
            var state = states[column];
            var level = (state & 1) != 0;
            if (level != pen)
            {
                var x = X(frame, start, origin);
                AddVertex(x, pen ? high : low);
                AddVertex(x, level ? high : low);
                pen = level;
            }

            if ((state & 2) != 0)
            {
                // Both levels inside: an edge over the column, ending at the level the column ends with.
                var middle = X(frame, start + ((end - start) / 2), origin);
                var ending = (state & 4) != 0;
                if (ending == pen)
                {
                    // A pulse that returns inside the column: a pixel wide, so that the line never folds back on itself.
                    AddVertex(middle - 0.5f, pen ? high : low);
                    AddVertex(middle - 0.5f, pen ? low : high);
                    AddVertex(middle + 0.5f, pen ? low : high);
                    AddVertex(middle + 0.5f, pen ? high : low);
                }
                else
                {
                    AddVertex(middle, pen ? high : low);
                    AddVertex(middle, ending ? high : low);
                }

                pen = ending;
            }
        }

        AddVertex(X(frame, last - 1, origin), pen ? high : low);
    }

    // The level at the start of each column, a change inside it and the level at its end; a list of changes runs a
    // cursor through them, other sources answer column by column.
    private void Describe(long first, long last, Span<byte> states)
    {
        if (_source is DigitalTransitions transitions)
        {
            transitions.DescribeColumns(first, last, states);
            return;
        }

        var quick = _source is PackedBits { IsComplete: false } bits ? bits : null;
        var span = last - first;
        for (var column = 0; column < states.Length; column++)
        {
            var start = first + (span * column / states.Length);
            var end = first + (span * (column + 1) / states.Length);
            if (end <= start)
            {
                states[column] = (byte)(_source.LevelAt(Math.Min(start, _source.Count - 1)) ? 0b101 : 0);
                continue;
            }

            var change = quick is null ? _source.NextChange(start + 1, end) : quick.NextIndexedChange(start + 1, end);
            var startLevel = _source.LevelAt(start);
            var endLevel = change < 0 ? startLevel : _source.LevelAt(end - 1);
            states[column] = (byte)((startLevel ? 1 : 0) | (change >= 0 ? 2 : 0) | (endLevel ? 4 : 0));
        }
    }

    private (float Low, float High) Levels(in SeriesFrame frame) => ((float)frame.Y.ToPixel(_low), (float)frame.Y.ToPixel(_high));

    private float X(in SeriesFrame frame, long index, double origin) => (float)frame.X.ToPixel(_source.XStart + (index * _source.XStep) - origin);

    private void AddVertex(float x, float y)
    {
        if (_count == _vertices.Length)
        {
            Array.Resize(ref _vertices, _count * 2);
        }

        _vertices[_count++] = new Vector2(x, y);
    }

    private double Origin() => XAxis is DateTimeAxis time ? time.OriginTicks : 0;

    private void Change(ref double field, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A level must lie at a finite value.");
        }

        field = value;
        Interlocked.Increment(ref _appearanceVersion);
    }
}
