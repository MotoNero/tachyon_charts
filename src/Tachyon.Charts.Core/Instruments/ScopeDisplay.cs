using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Input;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Instruments;

/// <summary>What an instrument measures on a waveform.</summary>
/// <param name="Minimum">The lowest value.</param>
/// <param name="Maximum">The highest value.</param>
/// <param name="Mean">The mean.</param>
/// <param name="Rms">The root mean square.</param>
/// <param name="Frequency">The frequency in hertz, NaN without two rising edges.</param>
public readonly record struct ScopeMeasurements(double Minimum, double Maximum, double Mean, double Rms, double Frequency)
{
    /// <summary>Gets the difference between highest and lowest value.</summary>
    public double PeakToPeak => Maximum - Minimum;

    /// <summary>Measures a waveform.</summary>
    /// <param name="time">The times in seconds, ascending.</param>
    /// <param name="values">The values.</param>
    /// <returns>The results, NaN without finite values.</returns>
    public static ScopeMeasurements Measure(ReadOnlySpan<double> time, ReadOnlySpan<float> values)
    {
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        var sum = 0.0;
        var squares = 0.0;
        var count = 0;
        foreach (var value in values)
        {
            if (float.IsFinite(value))
            {
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                sum += value;
                squares += (double)value * value;
                count++;
            }
        }

        if (count == 0)
        {
            return new ScopeMeasurements(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        return new ScopeMeasurements(minimum, maximum, sum / count, Math.Sqrt(squares / count), MeasureFrequency(time, values, minimum, maximum));
    }

    // Rising edges through the middle. The bands around it keep noise from counting as an edge.
    private static double MeasureFrequency(ReadOnlySpan<double> time, ReadOnlySpan<float> values, double minimum, double maximum)
    {
        const double Hysteresis = 0.1;
        var high = minimum + ((maximum - minimum) * (0.5 + Hysteresis));
        var low = minimum + ((maximum - minimum) * (0.5 - Hysteresis));
        var isArmed = false;
        var first = double.NaN;
        var last = double.NaN;
        var edges = 0;
        for (var i = 0; i < values.Length && i < time.Length; i++)
        {
            if (values[i] < low)
            {
                isArmed = true;
            }
            else if (isArmed && values[i] > high)
            {
                isArmed = false;
                first = edges == 0 ? time[i] : first;
                last = time[i];
                edges++;
            }
        }

        return edges >= 2 && last > first ? (edges - 1) / (last - first) : double.NaN;
    }
}

/// <summary>Writes values the way instruments do, such as 500 mV or 1.25 kHz.</summary>
public static class ScopeFormat
{
    private static readonly string[] _prefixes = ["p", "n", "µ", "m", string.Empty, "k", "M", "G"];

    /// <summary>Writes a value with a prefix and three significant digits.</summary>
    /// <param name="value">The value.</param>
    /// <param name="unit">The unit.</param>
    /// <returns>The text.</returns>
    public static string Value(double value, string unit) => Value(value, unit, ChartText.Culture);

    /// <summary>Writes a value with a prefix and three significant digits.</summary>
    /// <param name="value">The value.</param>
    /// <param name="unit">The unit.</param>
    /// <param name="culture">The format of numbers.</param>
    /// <returns>The text.</returns>
    public static string Value(double value, string unit, CultureInfo culture)
    {
        if (!double.IsFinite(value))
        {
            return "---";
        }

        if (value == 0)
        {
            return "0 " + unit;
        }

        // Rounded first: 999.9 reads 1 k, not 1000.
        var rounded = double.Parse(value.ToString("G3", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        var power = Math.Clamp((int)Math.Floor(Math.Log10(Math.Abs(rounded)) / 3), -4, 3);
        var scaled = rounded / Math.Pow(1000, power);
        return string.Create(culture, $"{scaled:0.##} {_prefixes[power + 4]}{unit}");
    }

    /// <summary>Finds the next value of the 1-2-5 sequence of an instrument.</summary>
    /// <param name="value">The value.</param>
    /// <param name="steps">The steps, positive for larger.</param>
    /// <returns>The value.</returns>
    public static double Step(double value, int steps)
    {
        var exponent = Math.Floor(Math.Log10(value));
        var mantissa = value / Math.Pow(10, exponent);
        var position = mantissa switch
        {
            >= 4.99 => 2,
            >= 1.99 => 1,
            _ => 0,
        };
        var index = (int)(exponent * 3) + position + steps;
        var decade = (int)Math.Floor(index / 3.0);
        var rest = index - (decade * 3);
        var digit = rest switch
        {
            0 => 1,
            1 => 2,
            _ => 5,
        };
        return digit * Math.Pow(10, decade);
    }
}

/// <summary>One input of an oscilloscope. Waveforms can come from any thread.</summary>
public sealed class ScopeChannel
{
    private readonly ScopeDisplay _display;
    private double _voltsPerDivision = 1;
    private double _position;
    private ScopeMeasurements _measurements = new(double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);
    private readonly Lock _gate = new();
    private long _acquisitions;
    private Rgba8 _color;
    private Colors.Colormap? _fade;

    internal ScopeChannel(ScopeDisplay display, int number, Rgba8 color)
    {
        _display = display;
        Number = number;
        _color = color;
    }

    /// <summary>Gets the number shown on the display, starting at 1.</summary>
    public int Number { get; }

    /// <summary>Gets the color. It follows the theme of the chart.</summary>
    public Rgba8 Color
    {
        get => _color;
        internal set
        {
            _color = value;
            _fade = null;
        }
    }

    /// <summary>Gets the waveform. Time is in seconds, zero is the trigger.</summary>
    public DataSource<double, float> Data { get; } = new();

    /// <summary>Gets or sets the scale.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double VoltsPerDivision
    {
        get => _voltsPerDivision;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _voltsPerDivision = value;
            _display.TouchScale();
        }
    }

    /// <summary>Gets or sets where zero volts lie, in divisions above the middle.</summary>
    public double Position
    {
        get => _position;
        set
        {
            _position = IsUnbounded ? value : Math.Clamp(value, -ScopeDisplay.VerticalDivisions / 2.0, ScopeDisplay.VerticalDivisions / 2.0);
            _display.TouchScale();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the position may lie beyond the screen, as for the level scale of a
    /// spectrum, whose reference level can be any number of divisions away from zero.
    /// </summary>
    internal bool IsUnbounded { get; set; }

    /// <summary>Gets the measurements of the last waveform.</summary>
    public ScopeMeasurements Measurements
    {
        get
        {
            using (_gate.EnterScope())
            {
                return _measurements;
            }
        }
    }

    /// <summary>Gets the number of waveforms so far.</summary>
    public long AcquisitionCount => Interlocked.Read(ref _acquisitions);

    /// <summary>Gets the fading colors of this channel.</summary>
    internal Colors.Colormap FadeColormap => _fade ??= ScopeColormaps.Fade(Color);

    /// <summary>Gets or sets where the scale of this channel stands on the screen.</summary>
    internal double ReadoutLeft { get; set; }

    /// <summary>Gets or sets where the scale of this channel ends on the screen.</summary>
    internal double ReadoutRight { get; set; }

    /// <summary>Gets or sets the top of the line of the scale of this channel on the screen.</summary>
    internal double ReadoutTop { get; set; }

    /// <summary>Gets or sets the bottom of the line of the scale of this channel on the screen.</summary>
    internal double ReadoutBottom { get; set; }

    /// <summary>Gets the name of the vertical axis of this channel.</summary>
    internal string AxisId => "ch" + Number.ToString(CultureInfo.InvariantCulture);

    /// <summary>Replaces the waveform with a new acquisition and measures it.</summary>
    /// <param name="time">The times in seconds, zero is the trigger.</param>
    /// <param name="volts">The values.</param>
    public void SetWaveform(ReadOnlySpan<double> time, ReadOnlySpan<float> volts)
    {
        var measurements = ScopeMeasurements.Measure(time, volts);
        Data.Clear();
        Data.Append(time, volts);
        using (_gate.EnterScope())
        {
            _measurements = measurements;
        }

        Interlocked.Increment(ref _acquisitions);
        _display.Touch();
    }

    /// <summary>Calculates the range of the axis of this channel.</summary>
    /// <returns>The range in volts.</returns>
    internal DoubleRange GetRange()
    {
        var half = ScopeDisplay.VerticalDivisions / 2.0;
        return new DoubleRange((-half - _position) * _voltsPerDivision, (half - _position) * _voltsPerDivision);
    }
}

/// <summary>The state of an oscilloscope display: time base, channels and trigger.</summary>
public sealed class ScopeDisplay
{
    /// <summary>The number of divisions from left to right.</summary>
    public const int HorizontalDivisions = 10;

    /// <summary>The number of divisions from top to bottom.</summary>
    public const int VerticalDivisions = 8;

    private static readonly Rgba8[] _colors =
    [
        new(245, 214, 60, 255),
        new(70, 215, 120, 255),
        new(90, 170, 255, 255),
        new(240, 110, 170, 255),
    ];

    private readonly List<ScopeChannel> _channels = [];
    private double _timePerDivision = 1e-3;
    private double _timeOffset;
    private double _triggerLevel;
    private long _version;
    private long _scaleVersion;
    private ScopePersistence _persistence;
    private double _persistenceSeconds = 0.3;

    /// <summary>Gets the channels.</summary>
    public IReadOnlyList<ScopeChannel> Channels => _channels;

    /// <summary>Gets or sets the time base.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double TimePerDivision
    {
        get => _timePerDivision;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _timePerDivision = value;
            TouchScale();
        }
    }

    /// <summary>Gets or sets the time in the middle of the screen, relative to the trigger.</summary>
    public double TimeOffset
    {
        get => _timeOffset;
        set
        {
            _timeOffset = value;
            TouchScale();
        }
    }

    /// <summary>Gets or sets the trigger level in volts on the first channel.</summary>
    public double TriggerLevel
    {
        get => _triggerLevel;
        set
        {
            _triggerLevel = value;
            Touch();
        }
    }

    /// <summary>Gets or sets the trigger state on the screen.</summary>
    public ChartTextKey? TriggerStatus { get; set; }

    /// <summary>Gets a version that grows with each change.</summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>Adds a channel. Four have their own colors, more repeat them.</summary>
    /// <returns>The channel.</returns>
    public ScopeChannel AddChannel()
    {
        var channel = new ScopeChannel(this, _channels.Count + 1, _colors[_channels.Count % _colors.Length]);
        _channels.Add(channel);
        return channel;
    }

    /// <summary>Calculates the visible time.</summary>
    /// <returns>The range in seconds.</returns>
    public DoubleRange GetTimeRange()
    {
        var half = HorizontalDivisions / 2.0 * _timePerDivision;
        return new DoubleRange(_timeOffset - half, _timeOffset + half);
    }

    /// <summary>Gets or sets how old waveforms stay on the display.</summary>
    public ScopePersistence Persistence
    {
        get => _persistence;
        set
        {
            _persistence = value;
            TouchScale();
        }
    }

    /// <summary>Gets or sets how long waveforms fade in <see cref="ScopePersistence.Variable"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public double PersistenceSeconds
    {
        get => _persistenceSeconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _persistenceSeconds = value;
            Touch();
        }
    }

    /// <summary>Gets a version that grows when the history no longer fits the display.</summary>
    public long ScaleVersion => Volatile.Read(ref _scaleVersion);

    /// <summary>Forgets the history on the display.</summary>
    public void ClearPersistence() => TouchScale();

    internal void Touch() => Interlocked.Increment(ref _version);

    internal void TouchScale()
    {
        Interlocked.Increment(ref _scaleVersion);
        Touch();
    }
}

/// <summary>The screen of an oscilloscope: graticule, markers and readouts.</summary>
/// <param name="display">The state.</param>
public sealed class ScopeOverlay(ScopeDisplay display) : ChartOverlay
{
    private const float LabelSize = 12;
    private const float MarkerSize = 7;
    private const int SubDivisions = 5;
    private const float RowPadding = 5;

    /// <inheritdoc />
    protected internal override long ExternalVersion => display.Version + ChartText.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        DrawGraticule(canvas, pane);
        DrawReadouts(canvas, pane);
        DrawMarkers(canvas, pane);
    }

    private static void DrawGraticule(OverlayCanvas canvas, Layout.DipRect pane)
    {
        var line = canvas.TextColor with { A = 34 };
        var mark = canvas.TextColor with { A = 110 };
        var stepX = pane.Width / ScopeDisplay.HorizontalDivisions;
        var stepY = pane.Height / ScopeDisplay.VerticalDivisions;
        for (var i = 0; i <= ScopeDisplay.HorizontalDivisions; i++)
        {
            var x = (float)Math.Round(pane.X + (i * stepX));
            canvas.DrawLine(new Vector2(x, (float)pane.Y), new Vector2(x, (float)pane.Bottom), 1, line);
        }

        for (var i = 0; i <= ScopeDisplay.VerticalDivisions; i++)
        {
            var y = (float)Math.Round(pane.Y + (i * stepY));
            canvas.DrawLine(new Vector2((float)pane.X, y), new Vector2((float)pane.Right, y), 1, line);
        }

        // The ticks on the center lines, which make reading between the divisions possible.
        var centerX = (float)Math.Round(pane.X + (pane.Width / 2));
        var centerY = (float)Math.Round(pane.Y + (pane.Height / 2));
        for (var i = 0; i <= ScopeDisplay.HorizontalDivisions * SubDivisions; i++)
        {
            var x = (float)Math.Round(pane.X + (i * stepX / SubDivisions));
            var length = i % SubDivisions == 0 ? 5 : 3;
            canvas.DrawLine(new Vector2(x, centerY - length), new Vector2(x, centerY + length), 1, mark);
        }

        for (var i = 0; i <= ScopeDisplay.VerticalDivisions * SubDivisions; i++)
        {
            var y = (float)Math.Round(pane.Y + (i * stepY / SubDivisions));
            var length = i % SubDivisions == 0 ? 5 : 3;
            canvas.DrawLine(new Vector2(centerX - length, y), new Vector2(centerX + length, y), 1, mark);
        }
    }

    private void DrawMarkers(OverlayCanvas canvas, Layout.DipRect pane)
    {
        var stepY = pane.Height / ScopeDisplay.VerticalDivisions;
        foreach (var channel in display.Channels)
        {
            var y = (float)Math.Round(pane.Y + (pane.Height / 2) - (channel.Position * stepY));
            var tip = new Vector2((float)pane.X + (MarkerSize * 2), y);
            canvas.FillTriangle(tip, new Vector2((float)pane.X, y - MarkerSize), new Vector2((float)pane.X, y + MarkerSize), channel.Color);
            var number = channel.Number.ToString(CultureInfo.InvariantCulture);
            canvas.DrawText(number, Math.Round(pane.X + (MarkerSize * 2) + 2), Math.Round(y + (LabelSize * 0.36)), LabelSize, channel.Color);
        }

        var time = display.GetTimeRange();
        var triggerX = pane.X + (pane.Width * (0 - time.Min) / (time.Max - time.Min));
        if (triggerX >= pane.X && triggerX <= pane.Right)
        {
            var x = (float)Math.Round(triggerX);
            var tip = new Vector2(x, (float)pane.Y + (MarkerSize * 1.5f));
            var orange = new Rgba8(255, 160, 50, 255);
            canvas.FillTriangle(tip, new Vector2(x - MarkerSize, (float)pane.Y), new Vector2(x + MarkerSize, (float)pane.Y), orange);
        }

        if (display.Channels.Count > 0)
        {
            var range = display.Channels[0].GetRange();
            var share = (display.TriggerLevel - range.Min) / (range.Max - range.Min);
            if (share is >= 0 and <= 1)
            {
                var y = (float)Math.Round(pane.Bottom - (share * pane.Height));
                var tip = new Vector2((float)pane.Right - (MarkerSize * 1.5f), y);
                var orange = new Rgba8(255, 160, 50, 255);
                canvas.FillTriangle(tip, new Vector2((float)pane.Right, y - MarkerSize), new Vector2((float)pane.Right, y + MarkerSize), orange);
            }
        }
    }

    private void DrawReadouts(OverlayCanvas canvas, Layout.DipRect pane)
    {
        var height = canvas.GetLineHeight(LabelSize) + RowPadding;
        var timeText = (display.TriggerStatus is { } status ? ChartText.Get(status) + "   " : string.Empty) + ScopeFormat.Value(display.TimePerDivision, "s") + "/div   T " + ScopeFormat.Value(display.TriggerLevel, "V");
        var timeLeft = pane.Right - canvas.MeasureWidth(timeText, LabelSize) - 28;
        canvas.DrawText(timeText, Math.Round(timeLeft), Math.Round(pane.Y + height - RowPadding), LabelSize, canvas.TextColor);

        // The scales of the channels run from the left; one that would reach the time goes to the next line.
        var (x, row) = (pane.X + 36, 0);
        foreach (var channel in display.Channels)
        {
            var text = channel.Number.ToString(CultureInfo.InvariantCulture) + ": " + ScopeFormat.Value(channel.VoltsPerDivision, "V") + "/div";
            var width = canvas.MeasureWidth(text, LabelSize);
            if (x + width > timeLeft - 18 && x > pane.X + 36)
            {
                (x, row) = (pane.X + 36, row + 1);
            }

            canvas.DrawText(text, Math.Round(x), Math.Round(pane.Y + (height * (row + 1)) - RowPadding), LabelSize, channel.Color);
            (channel.ReadoutLeft, channel.ReadoutRight) = (x, x + width);
            (channel.ReadoutTop, channel.ReadoutBottom) = (pane.Y + (height * row), pane.Y + (height * (row + 1)));
            x += width + 18;
        }

        var rows = display.Channels.Count;
        var top = pane.Bottom - (rows * height) - RowPadding;
        canvas.FillRectangle(new Layout.DipRect(pane.X, top, pane.Width, pane.Bottom - top), canvas.BackgroundColor with { A = 200 });
        var baseline = top + height;
        foreach (var channel in display.Channels)
        {
            var values = channel.Measurements;
            var text = channel.Number.ToString(CultureInfo.InvariantCulture)
                + ":  Vpp " + ScopeFormat.Value(values.PeakToPeak, "V")
                + "    Vrms " + ScopeFormat.Value(values.Rms, "V")
                + "    " + ChartText.Get(ChartTextKey.Mean) + " " + ScopeFormat.Value(values.Mean, "V")
                + "    Freq " + ScopeFormat.Value(values.Frequency, "Hz");
            canvas.DrawText(text, Math.Round(pane.X + 8), Math.Round(baseline - RowPadding), LabelSize, channel.Color);
            baseline += height;
        }
    }
}

/// <summary>
/// Operates an oscilloscope: dragging at the left edge moves a channel, at the right edge the trigger,
/// elsewhere the time. The wheel changes the scale of a channel at the left edge, over its scale readout
/// or anywhere with Ctrl, always the nearest channel. Otherwise it changes the time base.
/// </summary>
/// <param name="display">The state.</param>
public sealed class ScopeModifier(ScopeDisplay display) : ChartModifier(ZoomPanModifier.DefaultPriority)
{
    private const float EdgeWidth = 60;
    private const double GrabReach = 14;
    private const double ReadoutHeight = 26;

    private ScopeChannel? _channel;
    private bool _isTrigger;
    private bool _isTime;
    private long _pointerId;
    private Vector2 _start;
    private double _startValue;

    /// <inheritdoc />
    protected internal override ModifierResult OnPointer(in PointerInputEvent input)
    {
        if (Interaction?.Chart.CurrentLayout is not { } layout)
        {
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        if (input.Kind == PointerEventKind.Pressed && input.ChangedButton == PointerButton.Primary)
        {
            return Begin(input, pane);
        }

        if (input.PointerId != _pointerId || (_channel is null && !_isTrigger && !_isTime))
        {
            return ModifierResult.Ignored;
        }

        if (input.Kind == PointerEventKind.Moved)
        {
            Continue(input, pane);
            return ModifierResult.Handled;
        }

        if (input.Kind == PointerEventKind.Released)
        {
            _channel = null;
            _isTrigger = false;
            _isTime = false;
            return ModifierResult.Release;
        }

        return ModifierResult.Ignored;
    }

    /// <inheritdoc />
    protected internal override ModifierResult OnWheel(in WheelInputEvent input)
    {
        if (Interaction?.Chart.CurrentLayout is not { } layout || input.Delta.Y == 0)
        {
            return ModifierResult.Ignored;
        }

        var pane = layout.Panes[0];
        var steps = input.Delta.Y > 0 ? -1 : 1;
        if (FindChannelForScale(input, pane) is { } channel)
        {
            channel.VoltsPerDivision = ScopeFormat.Step(channel.VoltsPerDivision, steps);
        }
        else
        {
            display.TimePerDivision = ScopeFormat.Step(display.TimePerDivision, steps);
        }

        return ModifierResult.Handled;
    }

    /// <inheritdoc />
    protected internal override void OnCaptureLost(InputCapture capture)
    {
        _channel = null;
        _isTrigger = false;
        _isTime = false;
    }

    private ScopeChannel? FindChannelForScale(in WheelInputEvent input, Layout.DipRect pane)
    {
        foreach (var channel in display.Channels)
        {
            if (input.Position.X >= channel.ReadoutLeft && input.Position.X <= channel.ReadoutRight && input.Position.Y >= channel.ReadoutTop && input.Position.Y <= channel.ReadoutBottom)
            {
                return channel;
            }
        }

        var isOverScale = (input.Modifiers & InputModifiers.Control) != 0 || input.Position.X <= pane.X + EdgeWidth;
        return isOverScale ? FindChannel(input.Position.Y, pane, double.PositiveInfinity) : null;
    }

    private ModifierResult Begin(in PointerInputEvent input, Layout.DipRect pane)
    {
        _pointerId = input.PointerId;
        _start = input.Position;
        if (input.Position.X <= pane.X + EdgeWidth && FindChannel(input.Position.Y, pane, GrabReach) is { } channel)
        {
            _channel = channel;
            _startValue = channel.Position;
        }
        else if (input.Position.X >= pane.Right - EdgeWidth && display.Channels.Count > 0)
        {
            _isTrigger = true;
            _startValue = display.TriggerLevel;
        }
        else
        {
            _isTime = true;
            _startValue = display.TimeOffset;
        }

        return ModifierResult.Capture;
    }

    private void Continue(in PointerInputEvent input, Layout.DipRect pane)
    {
        var divisions = (_start.Y - input.Position.Y) / (pane.Height / ScopeDisplay.VerticalDivisions);
        if (_channel is { } channel)
        {
            channel.Position = _startValue + divisions;
        }
        else if (_isTrigger)
        {
            display.TriggerLevel = _startValue + (divisions * display.Channels[0].VoltsPerDivision);
        }
        else
        {
            var seconds = (_start.X - input.Position.X) / (pane.Width / ScopeDisplay.HorizontalDivisions) * display.TimePerDivision;
            display.TimeOffset = _startValue + seconds;
        }
    }

    private ScopeChannel? FindChannel(float y, Layout.DipRect pane, double reach)
    {
        var stepY = pane.Height / ScopeDisplay.VerticalDivisions;
        var best = default(ScopeChannel);
        var distance = reach;
        foreach (var channel in display.Channels)
        {
            var current = Math.Abs(pane.Y + (pane.Height / 2) - (channel.Position * stepY) - y);
            if (current <= distance)
            {
                distance = current;
                best = channel;
            }
        }

        return best;
    }
}

/// <summary>Keeps the hidden axes of an oscilloscope in step with its state.</summary>
/// <param name="chart">The chart.</param>
/// <param name="display">The state.</param>
internal sealed class ScopeSync(ChartSurface chart, ScopeDisplay display) : ChartOverlay
{
    private long _applied = -1;
    private Themes.Theme? _theme;

    /// <summary>Gets a value indicating whether lines stay visible with color grading.</summary>
    public bool KeepsLines { get; init; }

    /// <summary>Gets the distance in the palette between two channels.</summary>
    public int PaletteStep { get; init; } = 1;

    /// <inheritdoc />
    protected internal override long ExternalVersion => display.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var version = display.Version;
        if (version == _applied && ReferenceEquals(chart.Theme, _theme))
        {
            return;
        }

        _applied = version;
        ApplyPalette();
        chart.HorizontalAxes[0].VisibleRange = display.GetTimeRange();
        foreach (var line in chart.Series.OfType<Series.LineSeries<double, float>>())
        {
            if (!KeepsLines)
            {
                line.IsVisible = display.Persistence != ScopePersistence.ColorGrade;
            }
        }

        var axes = chart.Panes[0].Axes;
        for (var i = 0; i < display.Channels.Count && i < axes.Count; i++)
        {
            axes[i].VisibleRange = display.Channels[i].GetRange();
        }
    }

    private void ApplyPalette()
    {
        _theme = chart.Theme;
        if (_theme?.Palette is not { Length: > 0 } palette)
        {
            return;
        }

        var lines = chart.Series.OfType<Series.LineSeries<double, float>>().ToArray();
        for (var i = 0; i < display.Channels.Count; i++)
        {
            var color = palette[(i * PaletteStep) % palette.Length].Value;
            display.Channels[i].Color = color;
            if (i < lines.Length)
            {
                lines[i].Color = color;
            }
        }
    }
}

/// <summary>Describes an oscilloscope, for WPF, Avalonia and charts without a UI framework.</summary>
public static class ScopeChart
{
    /// <summary>Describes a chart that shows an oscilloscope.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="display">The state.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Oscilloscope(this ChartBuilder builder, ScopeDisplay display)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(display);
        var time = display.GetTimeRange();
        builder.XAxis(a => a.Numeric("time").Range(time.Min, time.Max).Hidden());
        foreach (var channel in display.Channels)
        {
            var range = channel.GetRange();
            builder.YAxis(a => a.Numeric(channel.AxisId).Range(range.Min, range.Max).Hidden());
        }

        foreach (var channel in display.Channels)
        {
            builder.AddSeries((x, y) => new ScopePersistenceSeries(display, channel, x, y), yAxis: channel.AxisId);
        }

        foreach (var channel in display.Channels)
        {
            builder.AddLine(channel.Data, s => s.Name("CH" + channel.Number.ToString(CultureInfo.InvariantCulture)).YAxis(channel.AxisId).Stroke(channel.Color).Thickness(1.5));
        }

        return builder
            .AddOverlay(chart => new ScopeSync(chart, display))
            .AddOverlay(_ => new ScopeOverlay(display))
            .Modifiers(m => m.Custom(() => new ScopeModifier(display)));
    }
}
