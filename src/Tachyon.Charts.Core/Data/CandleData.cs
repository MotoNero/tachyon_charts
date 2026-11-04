using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>One period of a price: when it began, the first, highest, lowest and last price, and the volume.</summary>
/// <param name="Time">The start of the period in units of the time axis.</param>
/// <param name="Open">The first price.</param>
/// <param name="High">The highest price.</param>
/// <param name="Low">The lowest price.</param>
/// <param name="Close">The last price.</param>
/// <param name="Volume">The traded volume.</param>
public readonly record struct Candle(double Time, double Open, double High, double Low, double Close, double Volume)
{
    /// <summary>Gets a value indicating whether the period closed at or above its open.</summary>
    public bool IsRising => Close >= Open;
}

/// <summary>
/// Candles in the order of time. Writers from any thread serialize on a short lock; readers take the published
/// arrays and count without a lock. Only the last candle changes after it was added, through
/// <see cref="UpdateLast"/> or <see cref="AddTick"/>.
/// </summary>
public sealed class CandleData
{
    private const int MinimumCapacity = 64;

    private readonly Lock _gate = new();
    private State _state;
    private long _version;

    /// <summary>Initializes a new instance of the <see cref="CandleData"/> class.</summary>
    /// <param name="capacity">The number of candles to reserve, which avoids copies while growing.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is negative.</exception>
    public CandleData(int capacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _state = State.Create(Math.Max(capacity, MinimumCapacity));
    }

    /// <summary>Gets the number of candles.</summary>
    public int Count => Volatile.Read(ref _state).Count;

    /// <summary>Gets a number that grows with every change.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Gets a candle.</summary>
    /// <param name="index">The number of the candle.</param>
    /// <returns>The candle.</returns>
    /// <exception cref="ArgumentOutOfRangeException">There is no such candle.</exception>
    public Candle this[int index]
    {
        get
        {
            var state = Volatile.Read(ref _state);
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, state.Count);
            return state.Get(index);
        }
    }

    internal State Current => Volatile.Read(ref _state);

    /// <summary>Adds a candle after the last one.</summary>
    /// <param name="candle">The candle.</param>
    /// <exception cref="ArgumentException">The time lies before the last candle, or a price is not finite.</exception>
    public void Append(in Candle candle)
    {
        Check(candle);
        lock (_gate)
        {
            var state = _state;
            if (state.Count > 0 && candle.Time < state.Time[state.Count - 1])
            {
                throw new ArgumentException("Candles must come in the order of time.", nameof(candle));
            }

            if (state.Count == state.Time.Length)
            {
                state = state.Grow();
            }

            state.Set(state.Count, candle);
            Publish(state with { Count = state.Count + 1, LowestLow = Math.Min(state.LowestLow, candle.Low), HighestHigh = Math.Max(state.HighestHigh, candle.High), MostVolume = Math.Max(state.MostVolume, candle.Volume) });
        }
    }

    /// <summary>Adds a candle after the last one.</summary>
    /// <param name="time">The start of the period.</param>
    /// <param name="open">The first price.</param>
    /// <param name="high">The highest price.</param>
    /// <param name="low">The lowest price.</param>
    /// <param name="close">The last price.</param>
    /// <param name="volume">The volume.</param>
    public void Append(double time, double open, double high, double low, double close, double volume = 0) =>
        Append(new Candle(time, open, high, low, close, volume));

    /// <summary>Replaces the last candle, for example while its period still runs. The time stays.</summary>
    /// <param name="candle">The new prices and volume.</param>
    /// <exception cref="InvalidOperationException">There is no candle yet.</exception>
    public void UpdateLast(in Candle candle)
    {
        Check(candle);
        lock (_gate)
        {
            var state = _state;
            if (state.Count == 0)
            {
                throw new InvalidOperationException("There is no candle to update.");
            }

            state.Set(state.Count - 1, candle with { Time = state.Time[state.Count - 1] });
            Publish(state with { LowestLow = Math.Min(state.LowestLow, candle.Low), HighestHigh = Math.Max(state.HighestHigh, candle.High), MostVolume = Math.Max(state.MostVolume, candle.Volume) });
        }
    }

    /// <summary>Adds a trade to the last candle: the price becomes its close and widens its range, the volume adds up.</summary>
    /// <param name="price">The price of the trade.</param>
    /// <param name="volume">The volume of the trade.</param>
    /// <exception cref="InvalidOperationException">There is no candle yet.</exception>
    public void AddTick(double price, double volume = 0)
    {
        Candle last;
        lock (_gate)
        {
            var state = _state;
            if (state.Count == 0)
            {
                throw new InvalidOperationException("There is no candle to add the trade to.");
            }

            last = state.Get(state.Count - 1);
        }

        UpdateLast(last with { High = Math.Max(last.High, price), Low = Math.Min(last.Low, price), Close = price, Volume = last.Volume + volume });
    }

    /// <summary>Removes all candles.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            Publish(State.Create(MinimumCapacity));
        }
    }

    private static void Check(in Candle candle)
    {
        if (!double.IsFinite(candle.Time) || !double.IsFinite(candle.Open) || !double.IsFinite(candle.High) || !double.IsFinite(candle.Low) || !double.IsFinite(candle.Close) || !double.IsFinite(candle.Volume))
        {
            throw new ArgumentException("A candle needs finite numbers.", nameof(candle));
        }

        if (candle.Low > candle.High)
        {
            throw new ArgumentException("The low lies above the high.", nameof(candle));
        }
    }

    private void Publish(State state)
    {
        Volatile.Write(ref _state, state);
        Interlocked.Increment(ref _version);
    }

    /// <summary>The arrays and how many of them hold candles. Growing copies into new arrays, so old states stay valid.</summary>
    internal sealed record State(double[] Time, double[] Open, double[] High, double[] Low, double[] Close, double[] Volume, int Count)
    {
        /// <summary>Gets the lowest low of all candles.</summary>
        public double LowestLow { get; init; } = double.PositiveInfinity;

        /// <summary>Gets the highest high of all candles.</summary>
        public double HighestHigh { get; init; } = double.NegativeInfinity;

        /// <summary>Gets the largest volume of all candles.</summary>
        public double MostVolume { get; init; }

        public DoubleRange PriceRange => Count > 0 ? new DoubleRange(LowestLow, HighestHigh) : new DoubleRange(0, 1);

        public static State Create(int capacity) =>
            new(new double[capacity], new double[capacity], new double[capacity], new double[capacity], new double[capacity], new double[capacity], 0);

        public Candle Get(int i) => new(Time[i], Open[i], High[i], Low[i], Close[i], Volume[i]);

        public void Set(int i, in Candle candle)
        {
            Time[i] = candle.Time;
            Open[i] = candle.Open;
            High[i] = candle.High;
            Low[i] = candle.Low;
            Close[i] = candle.Close;
            Volume[i] = candle.Volume;
        }

        public State Grow()
        {
            var size = Math.Min(Array.MaxLength, Time.Length * 2);
            return this with
            {
                Time = Copy(Time, size),
                Open = Copy(Open, size),
                High = Copy(High, size),
                Low = Copy(Low, size),
                Close = Copy(Close, size),
                Volume = Copy(Volume, size),
            };
        }

        /// <summary>Finds the first candle that starts at or after a time.</summary>
        public int FindFirst(double time)
        {
            var index = Array.BinarySearch(Time, 0, Count, time);
            if (index < 0)
            {
                return ~index;
            }

            while (index > 0 && Time[index - 1] == time)
            {
                index--;
            }

            return index;
        }

        private static double[] Copy(double[] source, int size)
        {
            var copy = new double[size];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
