using Tachyon.Charts.Core.Instruments;

namespace Tachyon.Charts.Samples.Shared.Examples.Common;

/// <summary>A running spectrum analyzer for the sample apps.</summary>
public sealed class SpectrumDemo
{
    private const int TracesPerSecond = 30;

    private Thread? _thread;
    private volatile bool _runs;

    /// <summary>Gets the display.</summary>
    public SpectrumDisplay Display { get; } = SpectrumData.CreateDisplay(passing: true);

    /// <summary>Starts the sweeps, if they do not run yet.</summary>
    public void Start()
    {
        if (_runs)
        {
            return;
        }

        _thread?.Join();
        _runs = true;
        _thread = new Thread(Sweep) { IsBackground = true, Name = "Spectrum" };
        _thread.Start();
    }

    /// <summary>Stops the sweeps.</summary>
    public void Stop() => _runs = false;

    private void Sweep()
    {
        var frequencies = Enumerable.Range(0, 1500).Select(i => i * 1e7 / 1499).ToArray();
        var levels = new float[frequencies.Length];
        var random = new Random(1);
        while (_runs)
        {
            SpectrumData.Deliver(Display, frequencies, levels, random);
            Thread.Sleep(1000 / TracesPerSecond);
        }
    }
}
