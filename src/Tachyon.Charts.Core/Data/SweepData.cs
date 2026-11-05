namespace Tachyon.Charts.Core.Data;

/// <summary>How a monitor display runs.</summary>
public enum SweepMode
{
    /// <summary>The trace writes from left to right over the previous one and clears a gap ahead of itself, as on a patient monitor.</summary>
    Sweep,

    /// <summary>The trace fills a page from left to right; at the right edge the next page begins empty.</summary>
    Stepping,
}

/// <summary>
/// Samples of several channels on one time base for a monitor display that does not scroll. A ring keeps the newest
/// samples; all channels share the time of the write head, so they always run in step. Writing is safe from one
/// or more threads.
/// </summary>
public sealed class SweepData
{
    private readonly Lock _gate = new();
    private readonly double[] _times;
    private readonly float[] _values;
    private int _count;
    private int _next;
    private double _window;
    private double _gapShare = 0.03;
    private SweepMode _mode;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="SweepData"/> class.</summary>
    /// <param name="channels">The number of channels.</param>
    /// <param name="capacity">The number of samples per channel the ring keeps; at least one window at the sample rate.</param>
    /// <param name="window">The width of the screen in units of time, for example seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException">A number is not positive.</exception>
    public SweepData(int channels, int capacity, double window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        Channels = channels;
        Capacity = capacity;
        _times = new double[capacity];
        _values = new float[checked(capacity * channels)];
        Window = window;
    }

    /// <summary>Gets the number of channels.</summary>
    public int Channels { get; }

    /// <summary>Gets the number of samples per channel the ring keeps.</summary>
    public int Capacity { get; }

    /// <summary>Gets a number that grows with every change.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Gets or sets the width of the screen in units of time.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double Window
    {
        get => Volatile.Read(ref _window);
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The window must be positive and finite.");
            }

            Volatile.Write(ref _window, value);
            Touch();
        }
    }

    /// <summary>Gets or sets the gap ahead of the write head as a share of the window.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 0 to 0.5.</exception>
    public double GapShare
    {
        get => Volatile.Read(ref _gapShare);
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 0.5);
            Volatile.Write(ref _gapShare, value);
            Touch();
        }
    }

    /// <summary>Gets or sets how the display runs.</summary>
    public SweepMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            Touch();
        }
    }

    /// <summary>Gets the time of the newest sample, or NaN before the first.</summary>
    public double Head
    {
        get
        {
            lock (_gate)
            {
                return _count == 0 ? double.NaN : _times[(_next - 1 + Capacity) % Capacity];
            }
        }
    }

    /// <summary>Adds one sample of every channel.</summary>
    /// <param name="time">The time, at or after the previous one.</param>
    /// <param name="values">One value per channel.</param>
    /// <exception cref="ArgumentException">The number of values differs from the channels, or the time lies before the previous one.</exception>
    public void Append(double time, ReadOnlySpan<float> values)
    {
        if (values.Length != Channels)
        {
            throw new ArgumentException($"A sample needs {Channels} values.", nameof(values));
        }

        lock (_gate)
        {
            Write(time, values);
        }

        Touch();
    }

    /// <summary>Adds a block of samples: one time each, and the values of all channels after each other per sample.</summary>
    /// <param name="times">The times in order.</param>
    /// <param name="values">The values, channel by channel for each sample: <c>times.Length * Channels</c> of them.</param>
    /// <exception cref="ArgumentException">The lengths do not fit, or a time lies before the previous one.</exception>
    public void Append(ReadOnlySpan<double> times, ReadOnlySpan<float> values)
    {
        if (values.Length != times.Length * Channels)
        {
            throw new ArgumentException($"{times.Length} samples need {times.Length * Channels} values.", nameof(values));
        }

        lock (_gate)
        {
            for (var i = 0; i < times.Length; i++)
            {
                Write(times[i], values.Slice(i * Channels, Channels));
            }
        }

        Touch();
    }

    /// <summary>Removes all samples.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _count = 0;
            _next = 0;
        }

        Touch();
    }

    /// <summary>
    /// Collects the lowest and highest value per pixel column of one channel for the trace that the display shows,
    /// under the lock, without allocating. Columns without samples get NaN.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="columns">The number of pixel columns across the window.</param>
    /// <param name="low">Receives the lowest value per column.</param>
    /// <param name="high">Receives the highest value per column.</param>
    /// <returns>The column of the write head, or -1 without samples.</returns>
    internal int Collect(int channel, int columns, Span<float> low, Span<float> high)
    {
        low[..columns].Fill(float.NaN);
        high[..columns].Fill(float.NaN);
        lock (_gate)
        {
            if (_count == 0)
            {
                return -1;
            }

            var window = Window;
            var head = _times[(_next - 1 + Capacity) % Capacity];
            var page = Math.Floor(head / window);
            var start = page * window;
            var headColumn = Math.Min(columns - 1, (int)((head - start) / window * columns));
            var gapColumns = (int)Math.Ceiling(GapShare * columns);

            // The current page up to the head; in sweep mode also the previous page behind the gap.
            var oldest = _mode == SweepMode.Stepping ? start : head - window;
            for (var n = 0; n < _count; n++)
            {
                var index = (_next - 1 - n + Capacity) % Capacity;
                var time = _times[index];
                if (time < oldest)
                {
                    break;
                }

                var isCurrent = time >= start;
                var column = (int)((time - (isCurrent ? start : start - window)) / window * columns);
                column = Math.Clamp(column, 0, columns - 1);
                if (!isCurrent && column <= headColumn + gapColumns)
                {
                    continue;
                }

                Widen(_values[(index * Channels) + channel], ref low[column], ref high[column]);
            }

            return headColumn;
        }
    }

    private static void Widen(float value, ref float low, ref float high)
    {
        if (float.IsNaN(low) || value < low)
        {
            low = value;
        }

        if (float.IsNaN(high) || value > high)
        {
            high = value;
        }
    }

    private void Write(double time, ReadOnlySpan<float> values)
    {
        if (!double.IsFinite(time) || (_count > 0 && time < _times[(_next - 1 + Capacity) % Capacity]))
        {
            throw new ArgumentException("The times must be finite and in order.", nameof(time));
        }

        _times[_next] = time;
        values.CopyTo(_values.AsSpan(_next * Channels, Channels));
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
    }

    private void Touch() => Interlocked.Increment(ref _version);
}
