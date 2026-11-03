using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Resampling;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Series;

/// <summary>
/// A line in pixels, rebuilt from a resampled snapshot when data, axes or appearance change. The GPU
/// buffer stays between frames, so an unchanged line uploads nothing.
/// </summary>
internal sealed class SnapshotLinePath<TX, TY>(IDataSource<TX, TY> source) : LinePath
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>, IMinMaxValue<TY>
{
    private readonly SeriesResampler<TX, TY> _resampler = new(source);
    private TY[] _minima = [];
    private TY[] _maxima = [];
    private int[] _indices = [];
    private Vector2[] _vertices = new Vector2[256];
    private Vector2[] _dashed = [];
    private int _count;
    private int _runLength;
    private long _builtDataVersion = -1;
    private long _builtOptionsVersion = -1;
    private SeriesFrame _builtFrame;

    public override int DrawnPointCount => _count;

    protected override void OnBufferReleased() => _builtDataVersion = -1;

    public override bool TryGetDataRange(AxisBase xAxis, out DoubleRange x, out DoubleRange y)
    {
        var metadata = source.Metadata;
        var offset = double.CreateTruncating(GetOrigin<TX>(xAxis));
        x = metadata.XRange.IsEmpty ? metadata.XRange : new DoubleRange(metadata.XRange.Min - offset, metadata.XRange.Max - offset);
        y = metadata.YRange;
        return metadata.Count > 0;
    }

    public override void Update(IRenderDevice device, in SeriesFrame frame, AxisBase xAxis, AxisBase yAxis, in LineOptions options)
    {
        var version = source.Version;
        if (version == _builtDataVersion && options.Version == _builtOptionsVersion && frame == _builtFrame)
        {
            return;
        }

        _builtDataVersion = version;
        _builtOptionsVersion = options.Version;
        _builtFrame = frame;
        _count = 0;
        _runLength = 0;

        var columns = Math.Max((int)MathF.Ceiling(frame.Bounds.Width), 1);
        if (_minima.Length != columns)
        {
            _minima = new TY[columns];
            _maxima = new TY[columns];
            _indices = new int[columns * 2];
        }

        var origin = GetOrigin<TX>(xAxis);
        var offset = double.CreateTruncating(origin);
        var visible = new DoubleRange(xAxis.VisibleRange.Min + offset, xAxis.VisibleRange.Max + offset);
        var result = _resampler.Resample(visible, options.Resampling, _minima, _maxima, _indices);

        // Taken after resampling: its indices are valid here, the source only grows behind them.
        var snapshot = source.GetSnapshot();
        var x = snapshot.X.Span;
        var y = snapshot.Y.Span;
        switch (result.Mode)
        {
            case ResamplingMode.MinMax:
                AddColumns(frame);
                break;
            case ResamplingMode.Lttb:
                AddSelection(x, y, origin, frame, options.IsStep, result.SelectedCount);
                break;
            default:
                AddRange(x, y, origin, frame, options.IsStep, result.Start, Math.Min(result.End, x.Length));
                if (options.IsSpline && !options.IsStep)
                {
                    SmoothRuns();
                }

                break;
        }

        EndRun();
        var vertices = _vertices.AsSpan(0, _count);
        if (options.DashPattern is { } pattern && result.Mode != ResamplingMode.MinMax)
        {
            _count = DashExpander.Expand(vertices, pattern, (float)frame.DpiScale, frame.Bounds, ref _dashed);
            vertices = _dashed.AsSpan(0, _count);
        }

        if (_count > 0)
        {
            EnsureBuffer(device, _count * 8L, keepSize: false);
            Buffer!.Write<Vector2>(0, vertices);
        }
    }

    public override void Draw(ICommandList commands, in SeriesFrame frame, AxisBase xAxis, in PolylineStyle style)
    {
        if (_count >= 2 && Buffer is not null)
        {
            commands.DrawPolyline(Buffer, 0, _count, style);
        }
    }

    private static float ToPixelX(TX x, TX origin, in SeriesFrame frame) => (float)frame.X.ToPixel(double.CreateTruncating(x - origin));

    private void AddRange(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in SeriesFrame frame, bool isStep, int start, int end)
    {
        // One point more on each side, so that the line leaves the plot area instead of ending inside.
        var first = Math.Max(start - 1, 0);
        var last = Math.Min(end + 1, x.Length);
        for (var i = first; i < last; i++)
        {
            AddPoint(x[i], y[i], i > first ? y[i - 1] : y[i], origin, frame, isStep);
        }
    }

    // Replaces the straight runs between gaps by curves.
    private void SmoothRuns()
    {
        var curve = new List<Vector2>(_count * 4);
        var start = 0;
        for (var i = 0; i <= _count; i++)
        {
            if (i == _count || _vertices[i].X >= PolylineStyle.Gap)
            {
                MonotoneSpline.Add(_vertices.AsSpan(start, i - start), curve);
                if (i < _count)
                {
                    curve.Add(_vertices[i]);
                }

                start = i + 1;
            }
        }

        if (curve.Count > _vertices.Length)
        {
            _vertices = new Vector2[curve.Count];
        }

        curve.CopyTo(_vertices);
        _count = curve.Count;
    }

    private void AddSelection(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y, TX origin, in SeriesFrame frame, bool isStep, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var index = _indices[i];
            AddPoint(x[index], y[index], i > 0 ? y[_indices[i - 1]] : y[index], origin, frame, isStep);
        }
    }

    private void AddPoint(TX x, TY y, TY previousY, TX origin, in SeriesFrame frame, bool isStep)
    {
        var value = double.CreateTruncating(y);
        var pixelX = ToPixelX(x, origin, frame);

        // A value without a place on a logarithmic axis is a gap as well.
        if (!double.IsFinite(value) || !double.IsFinite(frame.Y.ToPixel(value)) || !float.IsFinite(pixelX))
        {
            AddGap();
            return;
        }

        var previous = double.CreateTruncating(previousY);
        if (isStep && _runLength > 0 && double.IsFinite(previous) && double.IsFinite(frame.Y.ToPixel(previous)))
        {
            AddVertex(new Vector2(pixelX, (float)frame.Y.ToPixel(previous)));
        }

        AddVertex(new Vector2(pixelX, (float)frame.Y.ToPixel(value)));
    }

    private void AddColumns(in SeriesFrame frame)
    {
        for (var column = 0; column < _minima.Length; column++)
        {
            var minimum = double.CreateTruncating(_minima[column]);
            var maximum = double.CreateTruncating(_maxima[column]);
            if (minimum > maximum)
            {
                AddGap();
                continue;
            }

            var x = frame.Bounds.X + column + 0.5f;
            var top = frame.Y.ToPixel(maximum);
            var bottom = frame.Y.ToPixel(minimum);
            if (!double.IsFinite(top))
            {
                AddGap();
                continue;
            }

            AddVertex(new Vector2(x, (float)(double.IsFinite(bottom) ? bottom : frame.Bounds.Y + frame.Bounds.Height)));
            AddVertex(new Vector2(x, (float)top));
        }
    }

    private void AddVertex(Vector2 vertex)
    {
        if (_count == _vertices.Length)
        {
            Array.Resize(ref _vertices, _count * 2);
        }

        _vertices[_count++] = vertex;
        _runLength++;
    }

    private void AddGap()
    {
        if (_runLength > 0)
        {
            EndRun();
            AddVertex(new Vector2(PolylineStyle.Gap, 0));
            _runLength = 0;
        }
    }

    // A point between two gaps has no segment. A segment of no length shows it as a dot.
    private void EndRun()
    {
        if (_runLength == 1)
        {
            AddVertex(_vertices[_count - 1]);
        }
    }
}
