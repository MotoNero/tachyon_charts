namespace Tachyon.Charts.Core.Instruments;

/// <summary>Which edge fires the trigger.</summary>
public enum TriggerSlope
{
    /// <summary>The signal rises through the level.</summary>
    Rising,

    /// <summary>The signal falls through the level.</summary>
    Falling,

    /// <summary>Either edge.</summary>
    Either,
}

/// <summary>What happens without a trigger.</summary>
public enum TriggerMode
{
    /// <summary>The display shows the signal anyway, untriggered.</summary>
    Auto,

    /// <summary>The display keeps the last triggered waveform.</summary>
    Normal,

    /// <summary>The display freezes after one trigger until it is armed again.</summary>
    Single,
}

/// <summary>What a trigger did with a block of samples.</summary>
public enum TriggerState
{
    /// <summary>An edge fired, the display is new and stable.</summary>
    Triggered,

    /// <summary>No edge, the display is new but free-running.</summary>
    Auto,

    /// <summary>No edge, the display stays as it was.</summary>
    Waiting,

    /// <summary>A single acquisition is done.</summary>
    Stopped,
}

/// <summary>
/// The trigger of an oscilloscope in software: it compares a continuous signal with level and slope,
/// and places the waveforms so that the edge lies at time zero, with samples from before it.
/// </summary>
/// <param name="display">The display with level, time base and channels.</param>
public sealed class ScopeTrigger(ScopeDisplay display)
{
    private const double HysteresisDivisions = 0.1;

    private bool _isArmed = true;
    private double[] _time = [];

    /// <summary>Gets or sets the edge.</summary>
    public TriggerSlope Slope { get; set; }

    /// <summary>Gets or sets what happens without a trigger.</summary>
    public TriggerMode Mode { get; set; }

    /// <summary>Gets or sets the channel that fires the trigger, from zero.</summary>
    public int Source { get; set; }

    /// <summary>Gets or sets the time after an edge in which the next edge is ignored, in seconds.</summary>
    public double HoldoffSeconds { get; set; }

    /// <summary>Gets the result of the last block.</summary>
    public TriggerState State { get; private set; } = TriggerState.Waiting;

    /// <summary>Allows a single acquisition again.</summary>
    public void Arm()
    {
        _isArmed = true;
        State = TriggerState.Waiting;
        display.Touch();
    }

    /// <summary>
    /// Finds the first edge. The signal must first leave the level by the hysteresis, so that noise
    /// around the level fires nothing.
    /// </summary>
    /// <param name="samples">The signal.</param>
    /// <param name="level">The level.</param>
    /// <param name="hysteresis">The noise band below or above the level.</param>
    /// <param name="slope">The edge.</param>
    /// <param name="start">The first sample to look at.</param>
    /// <returns>The place of the crossing with its fraction, or a negative value without an edge.</returns>
    public static double FindEdge(ReadOnlySpan<float> samples, double level, double hysteresis, TriggerSlope slope, int start = 0)
    {
        var isBelow = false;
        var isAbove = false;
        for (var i = Math.Max(start, 1); i < samples.Length; i++)
        {
            double previous = samples[i - 1];
            double current = samples[i];
            if (slope != TriggerSlope.Falling && isBelow && previous < level && current >= level)
            {
                return i - 1 + ((level - previous) / (current - previous));
            }

            if (slope != TriggerSlope.Rising && isAbove && previous > level && current <= level)
            {
                return i - 1 + ((previous - level) / (previous - current));
            }

            isBelow |= current < level - hysteresis;
            isAbove |= current > level + hysteresis;
        }

        return -1;
    }

    /// <summary>Takes a block of equally spaced samples and updates the channels.</summary>
    /// <param name="sampleInterval">The time between samples in seconds.</param>
    /// <param name="channels">The samples of each channel, all of the same length.</param>
    /// <returns>What happened.</returns>
    public TriggerState Process(double sampleInterval, IReadOnlyList<float[]> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleInterval);
        if (Mode == TriggerMode.Single && !_isArmed)
        {
            return Report(TriggerState.Stopped);
        }

        var range = display.GetTimeRange();
        var before = (int)Math.Ceiling(-Math.Min(range.Min, 0) / sampleInterval);
        var source = channels[Source];
        var channel = display.Channels[Source];
        var hysteresis = channel.VoltsPerDivision * HysteresisDivisions;
        var edge = FindEdge(source, display.TriggerLevel, hysteresis, Slope, before);
        if (edge >= 0 && HoldoffSeconds > 0)
        {
            var next = FindEdge(source, display.TriggerLevel, hysteresis, Slope, (int)edge + (int)(HoldoffSeconds / sampleInterval));
            edge = next >= 0 ? next : edge;
        }

        if (edge < 0)
        {
            if (Mode != TriggerMode.Auto)
            {
                return Report(TriggerState.Waiting);
            }

            Show(sampleInterval, channels, source.Length / 2.0);
            return Report(TriggerState.Auto);
        }

        Show(sampleInterval, channels, edge);
        _isArmed = Mode != TriggerMode.Single;
        return Report(TriggerState.Triggered);
    }

    private TriggerState Report(TriggerState state)
    {
        State = state;
        display.TriggerStatus = state switch
        {
            TriggerState.Triggered => ChartTextKey.Triggered,
            TriggerState.Auto => ChartTextKey.Auto,
            TriggerState.Waiting => ChartTextKey.Waiting,
            _ => ChartTextKey.Stopped,
        };
        display.Touch();
        return state;
    }

    private void Show(double sampleInterval, IReadOnlyList<float[]> channels, double origin)
    {
        var length = channels[0].Length;
        if (_time.Length != length)
        {
            _time = new double[length];
        }

        // The fraction keeps the waveform still: the edge lies between two samples, not on one.
        for (var i = 0; i < length; i++)
        {
            _time[i] = (i - origin) * sampleInterval;
        }

        for (var c = 0; c < channels.Count && c < display.Channels.Count; c++)
        {
            display.Channels[c].SetWaveform(_time, channels[c]);
        }
    }
}
