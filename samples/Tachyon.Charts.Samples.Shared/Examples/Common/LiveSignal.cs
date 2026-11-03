using Tachyon.Charts.Core.Data;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>
/// A fixed curve and a measurement that keeps arriving: 50,000 points per second from a thread of its own, into a ring
/// buffer that keeps the last 20 seconds.
/// </summary>
public sealed class LiveSignal : IDisposable
{
    /// <summary>The points per second.</summary>
    public const int PointsPerSecond = 50_000;

    private readonly Thread _producer;
    private volatile bool _isStopping;
    private volatile bool _isRunning = true;

    /// <summary>Initializes a new instance of the <see cref="LiveSignal"/> class and starts the measurement.</summary>
    public LiveSignal()
    {
        for (var i = 0; i < 2000; i++)
        {
            var x = i * 0.01;
            Fixed.Append(x, Math.Sin(x) + (0.3 * Math.Sin(x * 7)));
        }

        _producer = new Thread(Produce) { IsBackground = true, Name = "Live signal" };
        _producer.Start();
    }

    /// <summary>Gets the fixed curve.</summary>
    public DataSource<double, double> Fixed { get; } = new();

    /// <summary>Gets the measurement.</summary>
    public FifoDataSource<double, float> Live { get; } = new(PointsPerSecond * 20, singleProducer: true);

    /// <summary>Gets or sets a value indicating whether points arrive. A paused measurement goes on where it stopped.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => _isRunning = value;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _isStopping = true;
        _producer.Join();
    }

    // Blocks of 500 points: blocks are much cheaper than single points.
    private void Produce()
    {
        var x = new double[500];
        var y = new float[500];
        var start = DateTime.UtcNow;
        var written = 0L;
        while (!_isStopping)
        {
            if (!_isRunning)
            {
                start = DateTime.UtcNow.AddSeconds(-written / (double)PointsPerSecond);
                Thread.Sleep(20);
                continue;
            }

            while (written + x.Length <= (DateTime.UtcNow - start).TotalSeconds * PointsPerSecond)
            {
                for (var i = 0; i < x.Length; i++)
                {
                    x[i] = (written + i) / (double)PointsPerSecond;
                    y[i] = (float)(Math.Sin(x[i] * 2) * Math.Cos(x[i] * 0.3)) + ((Random.Shared.NextSingle() - 0.5f) * 0.2f);
                }

                Live.Append(x, y);
                written += x.Length;
            }

            Thread.Sleep(5);
        }
    }
}
