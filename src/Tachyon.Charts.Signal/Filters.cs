namespace Tachyon.Charts.Signal;

/// <summary>A filter with a finite response. It keeps its state between blocks.</summary>
public sealed class FirFilter
{
    private readonly float[] _coefficients;
    private readonly float[] _history;
    private int _position;

    /// <summary>Initializes a new instance of the <see cref="FirFilter"/> class.</summary>
    /// <param name="coefficients">The coefficients.</param>
    /// <exception cref="ArgumentException">There are no coefficients.</exception>
    public FirFilter(float[] coefficients)
    {
        ArgumentNullException.ThrowIfNull(coefficients);
        if (coefficients.Length == 0)
        {
            throw new ArgumentException("A filter needs coefficients.", nameof(coefficients));
        }

        _coefficients = coefficients;
        _history = new float[coefficients.Length];
    }

    /// <summary>Creates a low pass with a Hamming window.</summary>
    /// <param name="cutoff">The cutoff frequency in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="taps">The number of coefficients, odd. More of them make the edge steeper.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cutoff is not between 0 and half the sample rate, or the count is even.</exception>
    public static FirFilter LowPass(double cutoff, double sampleRate, int taps = 101) => new(CreateLowPass(cutoff, sampleRate, taps));

    /// <summary>Creates a high pass by inverting a low pass.</summary>
    /// <param name="cutoff">The cutoff frequency in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="taps">The number of coefficients, odd.</param>
    /// <returns>The filter.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cutoff is not between 0 and half the sample rate, or the count is even.</exception>
    public static FirFilter HighPass(double cutoff, double sampleRate, int taps = 101)
    {
        var coefficients = CreateLowPass(cutoff, sampleRate, taps);
        for (var i = 0; i < coefficients.Length; i++)
        {
            coefficients[i] = -coefficients[i];
        }

        coefficients[taps / 2] += 1;
        return new FirFilter(coefficients);
    }

    /// <summary>Filters a block.</summary>
    /// <param name="input">The samples.</param>
    /// <param name="output">Receives the result. It can be the input.</param>
    /// <exception cref="ArgumentException">The output is too short.</exception>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output is too short.", nameof(output));
        }

        for (var i = 0; i < input.Length; i++)
        {
            _history[_position] = input[i];
            var sum = 0f;
            var index = _position;
            foreach (var coefficient in _coefficients)
            {
                sum += coefficient * _history[index];
                index = index == 0 ? _history.Length - 1 : index - 1;
            }

            output[i] = sum;
            _position = _position + 1 == _history.Length ? 0 : _position + 1;
        }
    }

    private static float[] CreateLowPass(double cutoff, double sampleRate, int taps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cutoff);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(cutoff, sampleRate / 2);
        if (taps < 3 || taps % 2 == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(taps), taps, "The number of coefficients must be odd and at least 3.");
        }

        var result = new float[taps];
        var relative = cutoff / sampleRate;
        var middle = taps / 2;
        var sum = 0.0;
        for (var i = 0; i < taps; i++)
        {
            var n = i - middle;
            var sinc = n == 0 ? 2 * relative : Math.Sin(2 * Math.PI * relative * n) / (Math.PI * n);
            var value = sinc * (0.54 - (0.46 * Math.Cos(2 * Math.PI * i / (taps - 1))));
            result[i] = (float)value;
            sum += value;
        }

        for (var i = 0; i < taps; i++)
        {
            result[i] = (float)(result[i] / sum);
        }

        return result;
    }
}

/// <summary>A second-order filter with little computing effort. It keeps its state between blocks.</summary>
public sealed class BiquadFilter
{
    private const double DefaultQuality = 0.7071067811865476;

    private readonly double _b0;
    private readonly double _b1;
    private readonly double _b2;
    private readonly double _a1;
    private readonly double _a2;
    private double _state1;
    private double _state2;

    private BiquadFilter(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    /// <summary>Creates a low pass.</summary>
    /// <param name="cutoff">The cutoff frequency in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="quality">The quality.</param>
    /// <returns>The filter.</returns>
    public static BiquadFilter LowPass(double cutoff, double sampleRate, double quality = DefaultQuality)
    {
        var (cosine, alpha) = Prepare(cutoff, sampleRate, quality);
        return new BiquadFilter((1 - cosine) / 2, 1 - cosine, (1 - cosine) / 2, 1 + alpha, -2 * cosine, 1 - alpha);
    }

    /// <summary>Creates a high pass.</summary>
    /// <param name="cutoff">The cutoff frequency in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="quality">The quality.</param>
    /// <returns>The filter.</returns>
    public static BiquadFilter HighPass(double cutoff, double sampleRate, double quality = DefaultQuality)
    {
        var (cosine, alpha) = Prepare(cutoff, sampleRate, quality);
        return new BiquadFilter((1 + cosine) / 2, -(1 + cosine), (1 + cosine) / 2, 1 + alpha, -2 * cosine, 1 - alpha);
    }

    /// <summary>Creates a notch, for example against mains hum.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="quality">The quality, higher values are narrower.</param>
    /// <returns>The filter.</returns>
    public static BiquadFilter Notch(double frequency, double sampleRate, double quality = 10)
    {
        var (cosine, alpha) = Prepare(frequency, sampleRate, quality);
        return new BiquadFilter(1, -2 * cosine, 1, 1 + alpha, -2 * cosine, 1 - alpha);
    }

    /// <summary>Filters a block.</summary>
    /// <param name="input">The samples.</param>
    /// <param name="output">Receives the result. It can be the input.</param>
    /// <exception cref="ArgumentException">The output is too short.</exception>
    public void Process(ReadOnlySpan<float> input, Span<float> output)
    {
        if (output.Length < input.Length)
        {
            throw new ArgumentException("The output is too short.", nameof(output));
        }

        for (var i = 0; i < input.Length; i++)
        {
            double value = input[i];
            var result = (_b0 * value) + _state1;
            _state1 = (_b1 * value) - (_a1 * result) + _state2;
            _state2 = (_b2 * value) - (_a2 * result);
            output[i] = (float)result;
        }
    }

    private static (double Cosine, double Alpha) Prepare(double frequency, double sampleRate, double quality)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frequency, sampleRate / 2);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quality);
        var omega = 2 * Math.PI * frequency / sampleRate;
        return (Math.Cos(omega), Math.Sin(omega) / (2 * quality));
    }
}

/// <summary>The shapes of test signals.</summary>
public enum SignalShape
{
    /// <summary>A sine.</summary>
    Sine,

    /// <summary>A square wave.</summary>
    Square,

    /// <summary>A triangle wave.</summary>
    Triangle,

    /// <summary>A sawtooth.</summary>
    Sawtooth,
}

/// <summary>Creates test signals.</summary>
public static class SignalGenerator
{
    /// <summary>Fills in a periodic signal.</summary>
    /// <param name="samples">Receives the signal.</param>
    /// <param name="shape">The shape.</param>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="amplitude">The peak value.</param>
    /// <param name="phase">The phase in radians.</param>
    public static void Fill(Span<float> samples, SignalShape shape, double frequency, double sampleRate, double amplitude = 1, double phase = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        for (var i = 0; i < samples.Length; i++)
        {
            var angle = (2 * Math.PI * frequency * i / sampleRate) + phase;
            var cycle = (angle / (2 * Math.PI)) - Math.Floor(angle / (2 * Math.PI));
            samples[i] = (float)(amplitude * shape switch
            {
                SignalShape.Square => cycle < 0.5 ? 1 : -1,
                SignalShape.Triangle => cycle < 0.5 ? (4 * cycle) - 1 : 3 - (4 * cycle),
                SignalShape.Sawtooth => (2 * cycle) - 1,
                _ => Math.Sin(angle),
            });
        }
    }

    /// <summary>Fills in a sine whose frequency rises linearly.</summary>
    /// <param name="samples">Receives the signal.</param>
    /// <param name="startFrequency">The frequency at the start in hertz.</param>
    /// <param name="stopFrequency">The frequency at the end in hertz.</param>
    /// <param name="sampleRate">The sample rate in hertz.</param>
    /// <param name="amplitude">The peak value.</param>
    public static void FillChirp(Span<float> samples, double startFrequency, double stopFrequency, double sampleRate, double amplitude = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        var phase = 0.0;
        for (var i = 0; i < samples.Length; i++)
        {
            var frequency = startFrequency + ((stopFrequency - startFrequency) * i / samples.Length);
            samples[i] = (float)(amplitude * Math.Sin(phase));
            phase += 2 * Math.PI * frequency / sampleRate;
        }
    }

    /// <summary>Adds Gaussian noise.</summary>
    /// <param name="samples">The signal.</param>
    /// <param name="deviation">The standard deviation.</param>
    /// <param name="random">The source of randomness.</param>
    public static void AddNoise(Span<float> samples, double deviation, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        for (var i = 0; i < samples.Length; i++)
        {
            var radius = Math.Sqrt(-2 * Math.Log(1 - random.NextDouble()));
            samples[i] += (float)(deviation * radius * Math.Cos(2 * Math.PI * random.NextDouble()));
        }
    }
}
