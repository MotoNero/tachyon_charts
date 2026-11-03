using System.Numerics;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Time series keep their X values as <see cref="DateTime.Ticks"/> in a <see cref="long"/>.
/// </summary>
public static class DateTimeDataExtensions
{
    /// <summary>
    /// Appends points with timestamps.
    /// </summary>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="source">The data source.</param>
    /// <param name="x">The timestamps. Their <see cref="DateTime.Kind"/> is not stored.</param>
    /// <param name="y">The Y values, as many as <paramref name="x"/>.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public static void Append<TY>(this DataSource<long, TY> source, ReadOnlySpan<DateTime> x, ReadOnlySpan<TY> y)
        where TY : unmanaged, INumber<TY>
    {
        ArgumentNullException.ThrowIfNull(source);
        if (x.Length != y.Length)
        {
            throw new ArgumentException("X and Y must have the same length.", nameof(y));
        }

        using var writer = source.BeginWrite(x.Length);
        for (var i = 0; i < x.Length; i++)
        {
            writer.X[i] = x[i].Ticks;
        }

        y.CopyTo(writer.Y);
    }

    /// <summary>
    /// Appends one point with a timestamp.
    /// </summary>
    /// <typeparam name="TY">The Y type.</typeparam>
    /// <param name="source">The data source.</param>
    /// <param name="x">The timestamp. Its <see cref="DateTime.Kind"/> is not stored.</param>
    /// <param name="y">The Y value.</param>
    public static void Append<TY>(this DataSource<long, TY> source, DateTime x, TY y)
        where TY : unmanaged, INumber<TY>
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Append(x.Ticks, y);
    }
}
