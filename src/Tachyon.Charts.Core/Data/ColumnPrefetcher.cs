using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Reads a range of a column in the background, so that the operating system has its pages in memory before a frame
/// needs them: the parts beside a view that panning or zooming reaches next. Only the newest request counts; one that is
/// replaced while running stops at the next page. Requests allocate nothing.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
internal sealed class ColumnPrefetcher<T>(IColumnSource<T> source) : IThreadPoolWorkItem
    where T : unmanaged, INumber<T>
{
    // A page of the system holds at least this many bytes; touching one value per page reads it.
    private const int PageBytes = 4096;

    private long _start;
    private long _end;
    private long _request;
    private int _isRunning;

    /// <summary>Gets the number of values touched so far, for tests.</summary>
    public long Touched => Interlocked.Read(ref _touched);

    private long _touched;

    // Keeps the sum, so that the reads are not left out.
    private T _sink;

    /// <summary>Asks for a range to be read; a running read moves on to it.</summary>
    /// <param name="start">The first index.</param>
    /// <param name="end">The index behind the last one.</param>
    public void Request(long start, long end)
    {
        start = Math.Max(0, start);
        end = Math.Min(end, source.Count);
        if (start >= end || (Interlocked.Read(ref _start) == start && Interlocked.Read(ref _end) == end))
        {
            return;
        }

        Interlocked.Exchange(ref _start, start);
        Interlocked.Exchange(ref _end, end);
        Interlocked.Increment(ref _request);
        if (Interlocked.Exchange(ref _isRunning, 1) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }
    }

    /// <inheritdoc />
    public void Execute()
    {
        while (true)
        {
            var request = Interlocked.Read(ref _request);
            var (start, end) = (Interlocked.Read(ref _start), Interlocked.Read(ref _end));
            var step = Math.Max(1, PageBytes / System.Runtime.CompilerServices.Unsafe.SizeOf<T>());
            var sum = T.Zero;
            for (var at = start; at < end && request == Interlocked.Read(ref _request); at += step)
            {
                var values = source.Read(at, 1);
                if (!values.IsEmpty)
                {
                    sum += values[0];
                }
            }

            Interlocked.Add(ref _touched, (end - start) / step);
            _sink = sum;

            // Done with the newest request: stop, unless one came in meanwhile.
            Volatile.Write(ref _isRunning, 0);
            if (request == Interlocked.Read(ref _request) || Interlocked.Exchange(ref _isRunning, 1) != 0)
            {
                return;
            }
        }
    }
}
