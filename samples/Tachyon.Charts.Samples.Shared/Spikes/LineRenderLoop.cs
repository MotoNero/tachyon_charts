using System.Diagnostics;
using System.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared.Spikes;

/// <summary>
/// Renders the line of the first spike on its own thread, which never touches a WPF object.
/// </summary>
public sealed class LineRenderLoop : IDisposable
{
    private const double ZoomPerNotch = 1.25;
    private const double MinimumVisiblePoints = 4;

    private static readonly Rgba8 _background = new(24, 28, 36, 255);
    private static readonly Rgba8 _lineColor = new(80, 220, 120, 255);

    private readonly object _gate = new();
    private readonly ILineOutput _output;
    private readonly Func<IRenderDevice> _createDevice;
    private readonly float[] _values;
    private readonly Thread _thread;
    private readonly List<double> _latencies = [];

    private PixelSize _requestedSize;
    private double _requestedScale;
    private bool _requestedVSync = true;
    private bool _paused;
    private double _viewStart;
    private double _viewLength;
    private long _pendingInputStamp;
    private long _frameCount;
    private double _framesPerSecond;
    private double _frameMilliseconds;
    private volatile bool _stop;

    public LineRenderLoop(ILineOutput output, Func<IRenderDevice> createDevice, float[] values, PixelSize size, double scale)
    {
        _output = output;
        _createDevice = createDevice;
        _values = values;
        _requestedSize = size;
        _requestedScale = scale;
        _viewLength = values.Length;
        output.FrameShown = OnFrameShown;
        _thread = new Thread(Run) { IsBackground = true, Name = "Tachyon render thread" };
        _thread.Start();
    }

    public string BackendName { get; private set; } = "starting";

    public long FrameCount => Interlocked.Read(ref _frameCount);

    public double FramesPerSecond => Volatile.Read(ref _framesPerSecond);

    public double FrameMilliseconds => Volatile.Read(ref _frameMilliseconds);

    public PixelSize RenderedSize { get; private set; }

    public Exception? Failure { get; private set; }

    public void RequestSize(PixelSize size)
    {
        lock (_gate)
        {
            _requestedSize = size;
        }
    }

    public void RequestScale(double scale)
    {
        lock (_gate)
        {
            _requestedScale = scale;
        }
    }

    public void RequestVSync(bool enabled)
    {
        lock (_gate)
        {
            _requestedVSync = enabled;
        }
    }

    /// <summary>
    /// Stops rendering while the window is minimized. A hosted child window keeps its size then,
    /// so the size alone does not tell.
    /// </summary>
    public void RequestPause(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
        }
    }

    public void Zoom(double notches, double anchor)
    {
        lock (_gate)
        {
            var length = Math.Clamp(_viewLength * Math.Pow(ZoomPerNotch, -notches), MinimumVisiblePoints, _values.Length);
            var start = _viewStart + ((_viewLength - length) * anchor);
            _viewLength = length;
            _viewStart = Math.Clamp(start, 0, _values.Length - length);
            if (_pendingInputStamp == 0)
            {
                _pendingInputStamp = Stopwatch.GetTimestamp();
            }
        }
    }

    /// <summary>
    /// Returns the measured times from input to shown frame in milliseconds and clears them.
    /// </summary>
    public double[] TakeLatencies()
    {
        lock (_latencies)
        {
            var result = _latencies.ToArray();
            _latencies.Clear();
            return result;
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join();
        _output.Dispose();
    }

    private void OnFrameShown(long inputStamp)
    {
        if (inputStamp != 0)
        {
            lock (_latencies)
            {
                _latencies.Add(Stopwatch.GetElapsedTime(inputStamp).TotalMilliseconds);
            }
        }
    }

    private void Run()
    {
        try
        {
            using var device = _createDevice();
            BackendName = device.Capabilities.BackendName;
            RenderUntilStopped(device);
        }
        catch (Exception exception)
        {
            Failure = exception;
        }
    }

    private void RenderUntilStopped(IRenderDevice device)
    {
        IGpuBuffer? points = null;
        MinMaxDecimator? decimator = null;
        Vector2[] positions = [];
        var frameWatch = Stopwatch.StartNew();
        var rateWatch = Stopwatch.StartNew();
        var framesSinceRate = 0;

        try
        {
            while (!_stop)
            {
                PixelSize size;
                double scale;
                bool vSync;
                bool paused;
                long start;
                long length;
                long inputStamp;
                lock (_gate)
                {
                    size = _requestedSize;
                    scale = _requestedScale;
                    vSync = _requestedVSync;
                    paused = _paused;
                    start = (long)_viewStart;
                    length = (long)_viewLength;
                    inputStamp = _pendingInputStamp;
                    _pendingInputStamp = 0;
                }

                if (paused || size.Width <= 0 || size.Height <= 0)
                {
                    Thread.Sleep(50);
                    continue;
                }

                var target = _output.Prepare(device, size, scale, vSync);

                if (decimator is null || positions.Length != size.Width * 2)
                {
                    decimator = new MinMaxDecimator(size.Width);
                    positions = new Vector2[decimator.MaxPointCount];
                    points?.Dispose();
                    points = device.CreateBuffer(new BufferDesc(positions.Length * 8L, BufferUsage.Vertex));
                }

                var map = new YMapping(-1.1, 1.1, size.Height);
                var count = decimator.Decimate(_values, start, length, size.Width, map, positions);
                points!.Write<Vector2>(0, positions.AsSpan(0, count));

                using (var frame = device.BeginFrame(target))
                {
                    frame.Clear(_background);
                    if (count >= 2)
                    {
                        frame.DrawPolyline(points, 0, count, 1, _lineColor);
                    }

                    frame.Submit();
                }

                _output.Complete(target, vSync, inputStamp);

                RenderedSize = size;
                Interlocked.Increment(ref _frameCount);
                Volatile.Write(ref _frameMilliseconds, frameWatch.Elapsed.TotalMilliseconds);
                frameWatch.Restart();

                framesSinceRate++;
                if (rateWatch.ElapsedMilliseconds >= 500)
                {
                    Volatile.Write(ref _framesPerSecond, framesSinceRate / rateWatch.Elapsed.TotalSeconds);
                    framesSinceRate = 0;
                    rateWatch.Restart();
                }
            }
        }
        finally
        {
            points?.Dispose();
        }
    }
}
