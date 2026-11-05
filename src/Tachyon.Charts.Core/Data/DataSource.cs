using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// A growing data source with separate X and Y arrays.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// Writers from any thread serialize on a short lock. Readers never take it: they copy the published
/// state under a sequence number and retry if a writer got in between. New points are complete before
/// they are published and published points never change, so a snapshot stays valid.
/// </remarks>
public sealed class DataSource<TX, TY> : IDataSource<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private const int MinimumCapacity = 16;

    private readonly Lock _writeGate = new();
    private MetadataScanner<TX, TY> _scanner = new();
    private PublishedState _state;
    private PublishedState _writerState;
    private long _sequence;
    private bool _isWriting;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSource{TX, TY}"/> class.
    /// </summary>
    /// <param name="capacity">The number of points to reserve, which avoids copies while growing.</param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is negative or too large.</exception>
    public DataSource(int capacity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, Array.MaxLength);

        _state = new PublishedState(new TX[capacity], new TY[capacity], 0, _scanner.ToMetadata(0), 0);
    }

    /// <inheritdoc />
    public long Version => Volatile.Read(ref _sequence) >> 1;

    /// <inheritdoc />
    public DataMetadata Metadata => ReadState(out _).Metadata;

    /// <inheritdoc />
    public DataSnapshot<TX, TY> GetSnapshot()
    {
        var state = ReadState(out var version);
        return new DataSnapshot<TX, TY>(state.X.AsMemory(0, state.Count), state.Y.AsMemory(0, state.Count), version, state.Metadata, state.Epoch);
    }

    /// <summary>
    /// Appends one point.
    /// </summary>
    public void Append(TX x, TY y) => Append(new ReadOnlySpan<TX>(in x), new ReadOnlySpan<TY>(in y));

    /// <summary>
    /// Appends points. The spans are copied.
    /// </summary>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    /// <exception cref="InvalidOperationException">The source cannot grow further.</exception>
    public void Append(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("X and Y must have the same length.", nameof(y));
        }

        using var scope = _writeGate.EnterScope();
        ThrowIfWriting();

        var state = EnsureCapacity(x.Length);
        x.CopyTo(state.X.AsSpan(state.Count));
        y.CopyTo(state.Y.AsSpan(state.Count));
        Publish(state, x, y);
    }

    /// <summary>
    /// Reserves space for writing directly, without an intermediate copy. Other writers wait until the
    /// writer is disposed, readers do not.
    /// </summary>
    /// <param name="count">The number of points to write.</param>
    /// <returns>The writer. Disposing it publishes the points.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The count is negative.</exception>
    /// <exception cref="InvalidOperationException">This thread is already writing.</exception>
    public DataWriter<TX, TY> BeginWrite(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _writeGate.Enter();
        try
        {
            ThrowIfWriting();
            _writerState = EnsureCapacity(count);
            _isWriting = true;
            return new DataWriter<TX, TY>(
                this,
                _writerState.X.AsSpan(_writerState.Count, count),
                _writerState.Y.AsSpan(_writerState.Count, count));
        }
        catch
        {
            _writeGate.Exit();
            throw;
        }
    }

    /// <summary>
    /// Removes all points. Existing snapshots keep theirs, because the arrays are replaced.
    /// </summary>
    public void Clear()
    {
        using var scope = _writeGate.EnterScope();
        ThrowIfWriting();

        _scanner = new MetadataScanner<TX, TY>();
        WriteState(new PublishedState([], [], 0, _scanner.ToMetadata(0), _state.Epoch + 1));
    }

    internal void EndWrite(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        Publish(_writerState, x, y);
        CancelWrite();
    }

    internal void CancelWrite()
    {
        _writerState = default;
        _isWriting = false;
        _writeGate.Exit();
    }

    private void Publish(in PublishedState state, ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        _scanner.Add(x, y);
        var count = state.Count + x.Length;
        WriteState(new PublishedState(state.X, state.Y, count, _scanner.ToMetadata(count), state.Epoch));
    }

    private PublishedState EnsureCapacity(int additional)
    {
        var state = _state;
        var required = (long)state.Count + additional;
        if (required <= state.X.Length)
        {
            return state;
        }

        if (required > Array.MaxLength)
        {
            throw new InvalidOperationException("The data source cannot hold that many points.");
        }

        var capacity = (int)Math.Min(Math.Max(required, Math.Max(state.X.Length * 2L, MinimumCapacity)), Array.MaxLength);
        var x = new TX[capacity];
        var y = new TY[capacity];
        state.X.AsSpan(0, state.Count).CopyTo(x);
        state.Y.AsSpan(0, state.Count).CopyTo(y);

        // Not published yet: until the next publish, readers stay on the old arrays.
        return new PublishedState(x, y, state.Count, state.Metadata, state.Epoch);
    }

    private void ThrowIfWriting()
    {
        if (_isWriting)
        {
            throw new InvalidOperationException("A writer of this data source is still open.");
        }
    }

    private void WriteState(in PublishedState state)
    {
        // An odd sequence marks a write in progress.
        var sequence = _sequence;
        Volatile.Write(ref _sequence, sequence + 1);
        Interlocked.MemoryBarrier();
        _state = state;
        Volatile.Write(ref _sequence, sequence + 2);
    }

    /// <inheritdoc />
    public bool TryGetLatest(out TY y)
    {
        var state = ReadState(out _);
        y = state.Count > 0 ? state.Y[state.Count - 1] : default;
        return state.Count > 0;
    }

    private PublishedState ReadState(out long version)
    {
        var spinner = default(SpinWait);
        while (true)
        {
            var sequence = Volatile.Read(ref _sequence);
            if ((sequence & 1) == 0)
            {
                var state = _state;
                Interlocked.MemoryBarrier();
                if (Volatile.Read(ref _sequence) == sequence)
                {
                    version = sequence >> 1;
                    return state;
                }
            }

            spinner.SpinOnce();
        }
    }

    private readonly struct PublishedState
    {
        public PublishedState(TX[] x, TY[] y, int count, DataMetadata metadata, long epoch)
        {
            Epoch = epoch;
            X = x;
            Y = y;
            Count = count;
            Metadata = metadata;
        }

        public long Epoch { get; }

        public TX[] X { get; }

        public TY[] Y { get; }

        public int Count { get; }

        public DataMetadata Metadata { get; }
    }
}
