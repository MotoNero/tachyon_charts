namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// Threads that do the work of the series of many charts at once, such as resampling, while the render thread waits:
/// each takes the next chart until none is left, the render thread takes part. The threads stay for the life of the
/// scheduler and allocate nothing per pass.
/// </summary>
internal sealed class PrepareWorkers : IDisposable
{
    private readonly Thread[] _threads;
    private readonly SemaphoreSlim _start = new(0);
    private readonly ManualResetEventSlim _done = new(false);
    private ChartSurface[] _charts = [];
    private Exception?[] _errors = [];
    private int _count;
    private int _next;
    private int _running;
    private volatile bool _stopping;

    /// <summary>Initializes a new instance of the <see cref="PrepareWorkers"/> class.</summary>
    /// <param name="threads">The threads beside the calling one.</param>
    public PrepareWorkers(int threads)
    {
        _threads = new Thread[Math.Max(1, threads)];
        for (var i = 0; i < _threads.Length; i++)
        {
            _threads[i] = new Thread(Loop) { IsBackground = true, Name = "Tachyon prepare thread" };
            _threads[i].Start();
        }
    }

    /// <summary>Gets the number of charts of the last pass.</summary>
    public int Count => _count;

    /// <summary>Gets the error of a chart of the last pass, or <see langword="null"/>.</summary>
    /// <param name="index">The index of the chart.</param>
    /// <returns>The error.</returns>
    public Exception? ErrorAt(int index) => _errors[index];

    /// <summary>Gets a chart of the last pass.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The chart.</returns>
    public ChartSurface ChartAt(int index) => _charts[index];

    /// <summary>Clears the charts of a new pass.</summary>
    public void Clear() => _count = 0;

    /// <summary>Adds a chart to the pass.</summary>
    /// <param name="chart">The chart.</param>
    public void Add(ChartSurface chart)
    {
        if (_count == _charts.Length)
        {
            Array.Resize(ref _charts, Math.Max(16, _count * 2));
            Array.Resize(ref _errors, _charts.Length);
        }

        (_charts[_count], _errors[_count]) = (chart, null);
        _count++;
    }

    /// <summary>Does the work of all charts of the pass and returns when it is done.</summary>
    public void Run()
    {
        if (_count == 0)
        {
            return;
        }

        Volatile.Write(ref _next, -1);
        var helpers = Math.Min(_threads.Length, _count - 1);
        Volatile.Write(ref _running, helpers);
        _done.Reset();
        if (helpers > 0)
        {
            _start.Release(helpers);
        }

        Work();
        if (helpers > 0)
        {
            _done.Wait();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stopping = true;
        _start.Release(_threads.Length);
        foreach (var thread in _threads)
        {
            thread.Join();
        }

        _start.Dispose();
        _done.Dispose();
    }

    private void Loop()
    {
        while (true)
        {
            _start.Wait();
            if (_stopping)
            {
                return;
            }

            Work();
            if (Interlocked.Decrement(ref _running) == 0)
            {
                _done.Set();
            }
        }
    }

    private void Work()
    {
        for (var index = Interlocked.Increment(ref _next); index < _count; index = Interlocked.Increment(ref _next))
        {
            try
            {
                _charts[index].PrepareData();
            }
            catch (Exception exception)
            {
                _errors[index] = exception;
            }
        }
    }
}
