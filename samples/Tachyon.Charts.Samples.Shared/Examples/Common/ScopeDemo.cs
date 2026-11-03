using Tachyon.Charts.Core.Instruments;
using Tachyon.Charts.Samples.Shared.Scenes;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A running oscilloscope for the sample apps: new acquisitions from a thread of their own.</summary>
public sealed class ScopeDemo
{
    /// <summary>Initializes a new instance of the <see cref="ScopeDemo"/> class.</summary>
    public ScopeDemo() => Trigger = new ScopeTrigger(Display);

    private const int AcquisitionsPerSecond = 30;

    private Thread? _thread;
    private volatile bool _runs;

    /// <summary>Gets the display.</summary>
    public ScopeDisplay Display { get; } = OscilloscopeScene.CreateDisplay();

    /// <summary>Gets the trigger.</summary>
    public ScopeTrigger Trigger { get; }

    /// <summary>Starts the acquisitions, if they do not run yet.</summary>
    public void Start()
    {
        if (_runs)
        {
            return;
        }

        _thread?.Join();
        _runs = true;
        _thread = new Thread(Acquire) { IsBackground = true, Name = "Oscilloscope" };
        _thread.Start();
    }

    /// <summary>Stops the acquisitions.</summary>
    public void Stop() => _runs = false;

    // The signals run free, with a new phase each time. Only the trigger makes the picture stand still.
    private void Acquire()
    {
        const int Count = 20_000;
        const double Interval = 1e-6;
        var random = new Random(1);
        var channels = Enumerable.Range(0, Display.Channels.Count).Select(_ => new float[Count]).ToArray();
        while (_runs)
        {
            var phase = random.NextDouble() * Math.Tau;
            for (var c = 0; c < channels.Length; c++)
            {
                for (var i = 0; i < Count; i++)
                {
                    channels[c][i] = (float)OscilloscopeScene.Signal(c, ((i * Interval) - 0.01) + (phase / (2 * Math.PI * 1000)), 0);
                }
            }

            Trigger.Process(Interval, channels);
            Thread.Sleep(1000 / AcquisitionsPerSecond);
        }
    }
}
