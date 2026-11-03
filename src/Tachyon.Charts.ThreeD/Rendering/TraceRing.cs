using System.Numerics;
using Tachyon.Charts.Rendering;
using Tachyon.Charts.ThreeD.Scene;

namespace Tachyon.Charts.ThreeD.Rendering;

/// <summary>Receives points for the GPU.</summary>
internal interface ITraceTarget
{
    /// <summary>Writes points.</summary>
    /// <param name="place">The place in the buffer.</param>
    /// <param name="points">The points.</param>
    void Write(int place, ReadOnlySpan<LineVertex> points);
}

/// <summary>
/// Lays the ring of a line out for the GPU, where one line piece reads four neighbouring places, so that the corners
/// can meet. The ring has one slot more than points: the slot after the newest point always holds a gap, which
/// separates newest from oldest without a second draw call. The place before the first slot and the three places
/// after the last one repeat the other end, so the pieces at the seam read the same as all others.
/// </summary>
internal static class TraceRing
{
    private const int Lead = 1;
    private const int Tail = 3;
    private const int ChunkSize = 1 << 20;
    private const int KeptChunk = 1 << 16;

    [ThreadStatic]
    private static LineVertex[]? _kept;

    /// <summary>Gets the place that marks a gap. The shader asks for it with a comparison, as NaN is unreliable there.</summary>
    public static Vector3 Gap { get; } = new(1e30f, 0, 0);

    /// <summary>Gets the place of the first slot in the buffer.</summary>
    public static uint FirstSlot => Lead;

    /// <summary>Gets the number of slots.</summary>
    /// <param name="capacity">The number of points.</param>
    /// <returns>The slots.</returns>
    public static int Slots(int capacity) => capacity + 1;

    /// <summary>Gets the size of the buffer.</summary>
    /// <param name="capacity">The number of points.</param>
    /// <returns>The number of places.</returns>
    public static int Places(int capacity) => Lead + Slots(capacity) + Tail;

    /// <summary>Gets how many line pieces to draw.</summary>
    /// <param name="capacity">The number of points.</param>
    /// <param name="total">How many points were ever added.</param>
    /// <returns>The number of pieces.</returns>
    public static int Pieces(int capacity, long total) => (int)Math.Min(total, Slots(capacity));

    /// <summary>Writes what is new.</summary>
    /// <param name="points">The points.</param>
    /// <param name="uploaded">How many points the GPU has seen. It moves on.</param>
    /// <param name="map">The way into the box.</param>
    /// <param name="color">The color of points without one.</param>
    /// <param name="target">The buffer.</param>
    /// <param name="size">The code of the size of points without one.</param>
    public static void Upload(Points3D points, ref long uploaded, in BoxMap map, Rgba8 color, ITraceTarget target, byte size = 255)
    {
        var total = points.Total;
        var slots = Slots(points.Capacity);
        var row = Math.Max(uploaded, total - points.Capacity);
        if (row >= total)
        {
            return;
        }

        // A live cloud comes through here in every frame, so small chunks stay with the thread. The shared pool would
        // not do: the collector empties it under memory pressure.
        var chunkSize = (int)Math.Min(total - row, ChunkSize);
        var chunk = chunkSize <= KeptChunk ? (_kept ??= new LineVertex[KeptChunk]) : new LineVertex[chunkSize];
        while (row < total)
        {
            var slot = (int)(row % slots);
            var count = (int)Math.Min(Math.Min(total - row, slots - slot), chunkSize);
            points.CopyTo(row, chunk, count, map, color, size);
            Write(target, slot, chunk.AsSpan(0, count), slots);
            row += count;
        }

        chunk[0] = new LineVertex(Gap, default);
        Write(target, (int)(total % slots), chunk.AsSpan(0, 1), slots);
        uploaded = total;
    }

    /// <summary>Finds the newest points in the ring, in at most two runs of slots.</summary>
    /// <param name="capacity">The number of points.</param>
    /// <param name="total">How many points were ever added.</param>
    /// <param name="budget">The most points.</param>
    /// <returns>Both runs. The second one is empty unless the newest points cross the end of the ring.</returns>
    public static (CloudDraw First, CloudDraw Second) NewestRuns(int capacity, long total, int budget)
    {
        var slots = Slots(capacity);
        var count = (int)Math.Min(Math.Min(total, capacity), budget);
        var head = (int)(total % slots);
        if (head >= count)
        {
            return (new CloudDraw(head - count, count), default);
        }

        return (new CloudDraw(0, head), new CloudDraw(slots - (count - head), count - head));
    }

    /// <summary>Marks the place in front of the first slot as a gap, for a ring that is not yet full.</summary>
    /// <param name="target">The buffer.</param>
    public static void Prepare(ITraceTarget target)
    {
        Span<LineVertex> gap = [new LineVertex(Gap, default)];
        target.Write(0, gap);
    }

    private static void Write(ITraceTarget target, int slot, ReadOnlySpan<LineVertex> points, int slots)
    {
        target.Write(slot + Lead, points);
        if (slot < Tail)
        {
            target.Write(slots + Lead + slot, points[..Math.Min(Tail - slot, points.Length)]);
        }

        if (slot + points.Length == slots)
        {
            target.Write(0, points[^1..]);
        }
    }
}
