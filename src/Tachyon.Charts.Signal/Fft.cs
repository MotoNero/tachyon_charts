using System.Buffers;
using System.Numerics;

namespace Tachyon.Charts.Signal;

/// <summary>The fast Fourier transform.</summary>
public static class Fft
{
    /// <summary>Transforms in place. The length must be a power of two.</summary>
    /// <param name="data">The values.</param>
    /// <param name="inverse">Whether to transform back, with scaling.</param>
    /// <exception cref="ArgumentException">The length is not a power of two.</exception>
    public static void Transform(Span<Complex> data, bool inverse = false)
    {
        if (!BitOperations.IsPow2(data.Length))
        {
            throw new ArgumentException("The length must be a power of two.", nameof(data));
        }

        Reorder(data);
        var sign = inverse ? 1.0 : -1.0;
        for (var size = 2; size <= data.Length; size *= 2)
        {
            var half = size / 2;
            var step = Complex.FromPolarCoordinates(1, sign * 2 * Math.PI / size);
            for (var start = 0; start < data.Length; start += size)
            {
                var twiddle = Complex.One;
                for (var i = 0; i < half; i++)
                {
                    var even = data[start + i];
                    var odd = data[start + i + half] * twiddle;
                    data[start + i] = even + odd;
                    data[start + i + half] = even - odd;
                    twiddle *= step;
                }
            }
        }

        if (inverse)
        {
            for (var i = 0; i < data.Length; i++)
            {
                data[i] /= data.Length;
            }
        }
    }

    /// <summary>Calculates a spectrum of real samples, in decibels relative to an amplitude of one.</summary>
    /// <param name="samples">The samples. The length must be a power of two.</param>
    /// <param name="decibels">Receives half the length plus one values, from 0 hertz to half the sample rate.</param>
    /// <param name="window">The window.</param>
    /// <exception cref="ArgumentException">A length does not fit.</exception>
    public static void Spectrum(ReadOnlySpan<float> samples, Span<float> decibels, WindowKind window = WindowKind.Hann)
    {
        if (decibels.Length != (samples.Length / 2) + 1)
        {
            throw new ArgumentException("The result needs half the samples plus one values.", nameof(decibels));
        }

        var length = samples.Length;
        var weights = ArrayPool<float>.Shared.Rent(length);
        var data = ArrayPool<Complex>.Shared.Rent(length);
        try
        {
            var weight = weights.AsSpan(0, length);
            Windows.Fill(weight, window);
            var gain = Windows.CoherentGain(weight);
            var values = data.AsSpan(0, length);
            for (var i = 0; i < length; i++)
            {
                values[i] = samples[i] * weight[i];
            }

            Transform(values);
            var scale = 2.0 / (length * gain);
            for (var i = 0; i < decibels.Length; i++)
            {
                var magnitude = values[i].Magnitude * (i == 0 || i == length / 2 ? scale / 2 : scale);
                decibels[i] = (float)(20 * Math.Log10(Math.Max(magnitude, 1e-12)));
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(weights);
            ArrayPool<Complex>.Shared.Return(data);
        }
    }

    private static void Reorder(Span<Complex> data)
    {
        var target = 0;
        for (var i = 1; i < data.Length; i++)
        {
            var bit = data.Length >> 1;
            for (; (target & bit) != 0; bit >>= 1)
            {
                target ^= bit;
            }

            target ^= bit;
            if (i < target)
            {
                (data[i], data[target]) = (data[target], data[i]);
            }
        }
    }
}

/// <summary>The window functions.</summary>
public enum WindowKind
{
    /// <summary>No window.</summary>
    Rectangular,

    /// <summary>The usual choice.</summary>
    Hann,

    /// <summary>A narrow peak.</summary>
    Hamming,

    /// <summary>Low side lobes.</summary>
    Blackman,

    /// <summary>Exact amplitudes, wide peaks.</summary>
    FlatTop,
}

/// <summary>Calculates window functions.</summary>
public static class Windows
{
    /// <summary>Fills in a window.</summary>
    /// <param name="window">Receives the window.</param>
    /// <param name="kind">The kind.</param>
    public static void Fill(Span<float> window, WindowKind kind)
    {
        for (var i = 0; i < window.Length; i++)
        {
            var phase = 2 * Math.PI * i / window.Length;
            window[i] = (float)(kind switch
            {
                WindowKind.Hann => 0.5 - (0.5 * Math.Cos(phase)),
                WindowKind.Hamming => 0.54 - (0.46 * Math.Cos(phase)),
                WindowKind.Blackman => 0.42 - (0.5 * Math.Cos(phase)) + (0.08 * Math.Cos(2 * phase)),
                WindowKind.FlatTop => 0.21557895 - (0.41663158 * Math.Cos(phase)) + (0.277263158 * Math.Cos(2 * phase)) - (0.083578947 * Math.Cos(3 * phase)) + (0.006947368 * Math.Cos(4 * phase)),
                _ => 1,
            });
        }
    }

    /// <summary>Calculates by how much a window lowers amplitudes.</summary>
    /// <param name="window">The window.</param>
    /// <returns>The mean of the window.</returns>
    public static double CoherentGain(ReadOnlySpan<float> window)
    {
        var sum = 0.0;
        foreach (var value in window)
        {
            sum += value;
        }

        return window.IsEmpty ? 1 : sum / window.Length;
    }
}
