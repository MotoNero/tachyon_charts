using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// A connected part of the ring arrays of a <see cref="FifoDataSource{TX, TY}"/>.
/// </summary>
/// <param name="Start">The index of the first point in the arrays.</param>
/// <param name="Length">The number of points.</param>
public readonly record struct RingSegment(int Start, int Length);

/// <summary>
/// The points of a <see cref="FifoDataSource{TX, TY}"/> that changed since a version. A wrap around the
/// end of the ring makes two segments, otherwise <see cref="Second"/> is empty.
/// </summary>
/// <param name="Version">The version this describes, to pass to the next query.</param>
/// <param name="FirstIndex">The running number of the oldest point in the segments.</param>
/// <param name="First">The older segment.</param>
/// <param name="Second">The newer segment.</param>
/// <param name="IsComplete">
/// Whether the segments hold all changes. If not, the ring was overwritten since the queried version,
/// and a mirror of it must be rebuilt from these segments.
/// </param>
public readonly record struct FifoChanges(long Version, long FirstIndex, RingSegment First, RingSegment Second, bool IsComplete)
{
    /// <summary>
    /// Gets the number of points in both segments.
    /// </summary>
    public int Count => First.Length + Second.Length;
}

/// <summary>
/// A ring buffer of fixed capacity for streaming data: when full, new points replace the oldest.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <remarks>
/// <para>
/// <see cref="IDataSource.Version"/> is the total number of points ever appended, so the changes since
/// a version follow from arithmetic and appending stays a plain copy.
/// </para>
/// <para>
/// Readers take no lock, so a fast producer can overwrite points while they are read. The producer
/// announces its range before writing. After reading, <see cref="IsIntact"/> tells whether that
/// happened, and the reader queries again.
/// </para>
/// </remarks>
public sealed class FifoDataSource<TX, TY> : IDataSource<TX, TY>
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private const int MetadataAttempts = 3;

    private readonly TX[] _x;
    private readonly TY[] _y;
    private readonly Lock? _writeGate;
    private readonly Lock _metadataGate = new();
    private long _published;
    private long _announced;
    private long _metadataVersion = -1;
    private DataMetadata _metadata;

    /// <summary>
    /// Initializes a new instance of the <see cref="FifoDataSource{TX, TY}"/> class.
    /// </summary>
    /// <param name="capacity">The number of points to keep, positive.</param>
    /// <param name="singleProducer">
    /// Whether only one thread at a time appends. This saves the lock, but parallel appends then corrupt the data.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The capacity is out of range.</exception>
    public FifoDataSource(int capacity, bool singleProducer = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, Array.MaxLength);

        _x = new TX[capacity];
        _y = new TY[capacity];
        _writeGate = singleProducer ? null : new Lock();
    }

    /// <summary>
    /// Gets the number of points the ring keeps.
    /// </summary>
    public int Capacity => _x.Length;

    /// <summary>
    /// Gets the number of points currently in the ring.
    /// </summary>
    public int Count => (int)Math.Min(Volatile.Read(ref _published), Capacity);

    /// <inheritdoc />
    public long Version => Volatile.Read(ref _published);

    /// <summary>
    /// Gets the ring array of the X values, to read with the segments of <see cref="FifoChanges"/>.
    /// </summary>
    public ReadOnlyMemory<TX> X => _x;

    /// <summary>
    /// Gets the ring array of the Y values, to read with the segments of <see cref="FifoChanges"/>.
    /// </summary>
    public ReadOnlyMemory<TY> Y => _y;

    /// <inheritdoc />
    /// <remarks>
    /// Scans the whole ring once per version, in time proportional to <see cref="Count"/>. It does not
    /// belong in the frame path. If the producer keeps overwriting the ring, the result is approximate.
    /// </remarks>
    public DataMetadata Metadata
    {
        get
        {
            using var scope = _metadataGate.EnterScope();
            var version = Version;
            if (version != _metadataVersion)
            {
                _metadata = ScanMetadata(out _metadataVersion);
            }

            return _metadata;
        }
    }

    /// <summary>
    /// Appends one point.
    /// </summary>
    public void Append(TX x, TY y) => Append(new ReadOnlySpan<TX>(in x), new ReadOnlySpan<TY>(in y));

    /// <summary>
    /// Appends points. If there are more than <see cref="Capacity"/>, only the newest remain.
    /// </summary>
    /// <param name="x">The X values.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public void Append(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        if (x.Length != y.Length)
        {
            throw new ArgumentException("X and Y must have the same length.", nameof(y));
        }

        if (x.IsEmpty)
        {
            return;
        }

        if (_writeGate is null)
        {
            Write(x, y);
            return;
        }

        using var scope = _writeGate.EnterScope();
        Write(x, y);
    }

    /// <summary>
    /// Finds the points that changed since a version.
    /// </summary>
    /// <param name="version">A version from an earlier result, or 0 for the whole content.</param>
    /// <returns>The changes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The version is negative or lies in the future.</exception>
    public FifoChanges GetChangesSince(long version)
    {
        var total = Version;
        if (version < 0 || version > total)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, $"The version must be in the range 0 to {total}.");
        }

        var oldest = Math.Max(total - Capacity, 0);
        var firstIndex = Math.Max(version, oldest);
        var count = (int)(total - firstIndex);
        var start = (int)(firstIndex % Capacity);
        var firstLength = Math.Min(count, Capacity - start);

        return new FifoChanges(
            total,
            firstIndex,
            new RingSegment(start, firstLength),
            new RingSegment(0, count - firstLength),
            version >= oldest);
    }

    /// <inheritdoc />
    public bool TryGetLatest(out TY y)
    {
        // The newest slot, then a check that no writer went round the ring meanwhile.
        var total = Version;
        if (total == 0)
        {
            y = default;
            return false;
        }

        y = _y[(int)((total - 1) % Capacity)];
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref _announced) - Capacity <= total - 1;
    }

    /// <summary>
    /// Tests after reading whether the points of the changes were still unchanged during the read.
    /// </summary>
    /// <param name="changes">The changes that were read.</param>
    /// <returns><see langword="false"/> if the producer reached them, then query and read again.</returns>
    public bool IsIntact(in FifoChanges changes)
    {
        Interlocked.MemoryBarrier();
        return Volatile.Read(ref _announced) - Capacity <= changes.FirstIndex;
    }

    /// <summary>
    /// Copies changes in chronological order.
    /// </summary>
    /// <param name="changes">The changes to copy.</param>
    /// <param name="x">Receives the X values, at least <see cref="FifoChanges.Count"/> long.</param>
    /// <param name="y">Receives the Y values, at least <see cref="FifoChanges.Count"/> long.</param>
    /// <returns><see langword="true"/> if the copy is intact, see <see cref="IsIntact"/>.</returns>
    /// <exception cref="ArgumentException">A destination is too short.</exception>
    public bool TryCopy(in FifoChanges changes, Span<TX> x, Span<TY> y)
    {
        if (x.Length < changes.Count || y.Length < changes.Count)
        {
            throw new ArgumentException("The destinations are too short.", nameof(x));
        }

        CopySegments<TX>(_x, changes, x);
        CopySegments<TY>(_y, changes, y);
        return IsIntact(changes);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Copies the ring in chronological order and allocates. Streaming series read the
    /// <see cref="GetChangesSince">changes</see> instead.
    /// </remarks>
    public DataSnapshot<TX, TY> GetSnapshot()
    {
        var spinner = default(SpinWait);
        while (true)
        {
            var changes = GetChangesSince(0);
            var x = new TX[changes.Count];
            var y = new TY[changes.Count];
            if (TryCopy(changes, x, y))
            {
                var scanner = new MetadataScanner<TX, TY>();
                scanner.Add(x, y);
                return new DataSnapshot<TX, TY>(x, y, changes.Version, scanner.ToMetadata(changes.Count), changes.Version);
            }

            spinner.SpinOnce();
        }
    }

    private static void CopyIntoRing<T>(ReadOnlySpan<T> source, T[] ring, int start)
    {
        var firstLength = Math.Min(source.Length, ring.Length - start);
        source[..firstLength].CopyTo(ring.AsSpan(start));
        source[firstLength..].CopyTo(ring);
    }

    private static void CopySegments<T>(T[] ring, in FifoChanges changes, Span<T> destination)
    {
        ring.AsSpan(changes.First.Start, changes.First.Length).CopyTo(destination);
        ring.AsSpan(changes.Second.Start, changes.Second.Length).CopyTo(destination[changes.First.Length..]);
    }

    private void Write(ReadOnlySpan<TX> x, ReadOnlySpan<TY> y)
    {
        var total = _published + x.Length;

        // Points beyond the capacity would be overwritten by the same append.
        if (x.Length > Capacity)
        {
            x = x[^Capacity..];
            y = y[^Capacity..];
        }

        // Announced first: a reader must learn about the overwrite before it can happen.
        Volatile.Write(ref _announced, total);
        Interlocked.MemoryBarrier();

        var start = (int)((total - x.Length) % Capacity);
        CopyIntoRing(x, _x, start);
        CopyIntoRing(y, _y, start);

        Volatile.Write(ref _published, total);
    }

    private DataMetadata ScanMetadata(out long version)
    {
        var metadata = default(DataMetadata);
        version = 0;
        for (var attempt = 0; attempt < MetadataAttempts; attempt++)
        {
            var changes = GetChangesSince(0);
            var scanner = new MetadataScanner<TX, TY>();
            ScanSegment(ref scanner, changes.First);
            ScanSegment(ref scanner, changes.Second);

            metadata = scanner.ToMetadata(changes.Count);
            version = changes.Version;
            if (IsIntact(changes))
            {
                break;
            }
        }

        return metadata;
    }

    private void ScanSegment(ref MetadataScanner<TX, TY> scanner, RingSegment segment) =>
        scanner.Add(_x.AsSpan(segment.Start, segment.Length), _y.AsSpan(segment.Start, segment.Length));
}
