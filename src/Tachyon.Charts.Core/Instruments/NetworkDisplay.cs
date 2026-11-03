using System.Globalization;
using System.Numerics;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Instruments;

/// <summary>How a network analyzer shows a trace.</summary>
public enum NetworkFormat
{
    /// <summary>The magnitude in decibels over frequency.</summary>
    LogMagnitude,

    /// <summary>The phase in degrees over frequency.</summary>
    Phase,

    /// <summary>The impedance in a Smith chart. Limits do not apply here.</summary>
    Smith,
}

/// <summary>Which side of a limit line passes.</summary>
public enum LimitKind
{
    /// <summary>Values must stay below the line.</summary>
    Maximum,

    /// <summary>Values must stay above the line.</summary>
    Minimum,
}

/// <summary>One straight piece of a limit.</summary>
/// <param name="Kind">The side that passes.</param>
/// <param name="StartFrequency">The left end in hertz.</param>
/// <param name="StartLevel">The level at the left end.</param>
/// <param name="StopFrequency">The right end in hertz.</param>
/// <param name="StopLevel">The level at the right end.</param>
public readonly record struct LimitLine(LimitKind Kind, double StartFrequency, double StartLevel, double StopFrequency, double StopLevel)
{
    /// <summary>Checks a value.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <param name="level">The value.</param>
    /// <returns>Whether the line does not object.</returns>
    public bool Passes(double frequency, double level)
    {
        if (frequency < StartFrequency || frequency > StopFrequency)
        {
            return true;
        }

        var share = StopFrequency > StartFrequency ? (frequency - StartFrequency) / (StopFrequency - StartFrequency) : 0;
        var limit = StartLevel + (share * (StopLevel - StartLevel));
        return Kind == LimitKind.Maximum ? level <= limit : level >= limit;
    }
}

/// <summary>One measurement of a network analyzer, such as S11, as complex values over frequency.</summary>
public sealed class NetworkTrace
{
    private readonly NetworkDisplay _display;
    private readonly List<LimitLine> _limits = [];
    private double[] _frequencies = [];
    private Complex[] _values = [];

    internal NetworkTrace(NetworkDisplay display, string name, Rgba8 color)
    {
        _display = display;
        Name = name;
        Color = color;
    }

    /// <summary>Gets the name.</summary>
    public string Name { get; }

    /// <summary>Gets the color. It follows the theme of the chart.</summary>
    public Rgba8 Color { get; internal set; }

    /// <summary>Gets the limits of this trace.</summary>
    public IReadOnlyList<LimitLine> Limits => _limits;

    /// <summary>Gets the values in the format of the display.</summary>
    public DataSource<double, float> Data { get; } = new();

    /// <summary>Gets a value indicating whether the trace keeps its limits. Without limits or in a Smith chart it passes.</summary>
    public bool Passes
    {
        get
        {
            if (!_display.AreLimitsActive)
            {
                return true;
            }

            for (var i = 0; i < _frequencies.Length; i++)
            {
                var level = _display.ToLevel(_values[i]);
                foreach (var limit in _limits)
                {
                    if (!limit.Passes(_frequencies[i], level))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }

    /// <summary>Adds a limit.</summary>
    /// <param name="limit">The limit.</param>
    public void AddLimit(LimitLine limit)
    {
        _limits.Add(limit);
        _display.Touch();
    }

    /// <summary>Delivers a sweep.</summary>
    /// <param name="frequencies">The frequencies in hertz, ascending.</param>
    /// <param name="values">The complex values.</param>
    /// <exception cref="ArgumentException">The lengths differ.</exception>
    public void SetSweep(double[] frequencies, Complex[] values)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(values);
        if (frequencies.Length != values.Length)
        {
            throw new ArgumentException("Each frequency needs a value.", nameof(values));
        }

        _frequencies = frequencies;
        _values = values;
        var levels = new float[values.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = (float)_display.ToLevel(values[i]);
        }

        Data.Clear();
        Data.Append(frequencies, levels);
        _display.Touch();
    }

    /// <summary>Reads the trace at a frequency, at the nearest measured point.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <returns>The complex value, or <see langword="null"/> without data.</returns>
    public Complex? Read(double frequency)
    {
        if (_frequencies.Length == 0)
        {
            return null;
        }

        var index = Array.BinarySearch(_frequencies, frequency);
        if (index < 0)
        {
            var upper = ~index;
            if (upper == 0)
            {
                index = 0;
            }
            else if (upper >= _frequencies.Length)
            {
                index = _frequencies.Length - 1;
            }
            else
            {
                index = frequency - _frequencies[upper - 1] < _frequencies[upper] - frequency ? upper - 1 : upper;
            }
        }

        return _values[index];
    }
}

/// <summary>The state of a network analyzer: traces, shared markers and the format.</summary>
/// <param name="format">How the traces are shown.</param>
public sealed class NetworkDisplay(NetworkFormat format = NetworkFormat.LogMagnitude)
{
    private static readonly Rgba8[] _colors =
    [
        new(245, 214, 60, 255),
        new(90, 170, 255, 255),
        new(240, 110, 170, 255),
        new(70, 215, 120, 255),
    ];

    private readonly List<NetworkTrace> _traces = [];
    private double[] _markers = [];
    private long _version;

    /// <summary>Gets the format.</summary>
    public NetworkFormat Format { get; } = format;

    /// <summary>Gets a value indicating whether limits count. As on an instrument, they do not in a Smith chart.</summary>
    public bool AreLimitsActive => Format != NetworkFormat.Smith;

    /// <summary>Gets the traces.</summary>
    public IReadOnlyList<NetworkTrace> Traces => _traces;

    /// <summary>Gets the marker frequencies in hertz. Every marker applies to all traces.</summary>
    public IReadOnlyList<double> Markers => _markers;

    /// <summary>Gets a version that grows with each change.</summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>Adds a trace.</summary>
    /// <param name="name">The name, such as S11.</param>
    /// <returns>The trace.</returns>
    public NetworkTrace AddTrace(string name)
    {
        var trace = new NetworkTrace(this, name, _colors[_traces.Count % _colors.Length]);
        _traces.Add(trace);
        return trace;
    }

    /// <summary>Adds a marker.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    public void AddMarker(double frequency)
    {
        Volatile.Write(ref _markers, [.. _markers, frequency]);
        Touch();
    }

    /// <summary>Moves a marker.</summary>
    /// <param name="index">The marker.</param>
    /// <param name="frequency">The frequency in hertz.</param>
    public void MoveMarker(int index, double frequency)
    {
        double[] markers = [.. _markers];
        markers[index] = frequency;
        Volatile.Write(ref _markers, markers);
        Touch();
    }

    /// <summary>Removes a marker.</summary>
    /// <param name="index">The marker.</param>
    public void RemoveMarker(int index)
    {
        Volatile.Write(ref _markers, [.. _markers[..index], .. _markers[(index + 1)..]]);
        Touch();
    }

    /// <summary>Calculates what the format shows of a value.</summary>
    /// <param name="value">The complex value.</param>
    /// <returns>Decibels or degrees.</returns>
    public double ToLevel(Complex value) => Format == NetworkFormat.Phase
        ? value.Phase * 180 / Math.PI
        : 20 * Math.Log10(value.Magnitude);

    internal void Touch() => Interlocked.Increment(ref _version);
}

/// <summary>Limits, markers, the marker table and the pass or fail results.</summary>
/// <param name="display">The state.</param>
public sealed class NetworkOverlay(NetworkDisplay display) : ChartOverlay
{
    private const float LabelSize = 12;
    private const float MarkerSize = 5;

    /// <inheritdoc />
    protected internal override long ExternalVersion => display.Version + ChartText.Version;

    /// <summary>Gets the chart, whose theme gives the colors of the traces.</summary>
    public Pipeline.ChartSurface? Chart { get; init; }

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ApplyPalette();
        var layout = canvas.Layout;
        var pane = layout.Panes[0];
        var x = layout.Axes.First(axis => axis.Axis.IsHorizontal).Axis;
        var y = layout.Axes.First(axis => !axis.Axis.IsHorizontal).Axis;
        Vector2 ToPoint(double frequency, double level) => new(
            (float)(pane.X + (pane.Width * (frequency - x.VisibleRange.Min) / (x.VisibleRange.Max - x.VisibleRange.Min))),
            (float)(pane.Bottom - (pane.Height * (level - y.VisibleRange.Min) / (y.VisibleRange.Max - y.VisibleRange.Min))));

        foreach (var trace in display.Traces)
        {
            foreach (var limit in display.AreLimitsActive ? trace.Limits : [])
            {
                canvas.DrawLine(ToPoint(limit.StartFrequency, limit.StartLevel), ToPoint(limit.StopFrequency, limit.StopLevel), 1.5f, trace.Color with { A = 170 });
            }
        }

        var number = 0;
        foreach (var marker in display.Markers)
        {
            number++;
            foreach (var trace in display.Traces)
            {
                if (trace.Read(marker) is { } value)
                {
                    var point = ToPoint(marker, display.ToLevel(value));
                    canvas.FillTriangle(point, point + new Vector2(-MarkerSize, -MarkerSize * 2), point + new Vector2(MarkerSize, -MarkerSize * 2), trace.Color);
                    canvas.DrawText(number.ToString(CultureInfo.InvariantCulture), Math.Round(point.X - 3), Math.Round(point.Y - (MarkerSize * 2) - 2), LabelSize, trace.Color);
                }
            }
        }

        DrawTable(canvas, pane);
        DrawResults(canvas, pane);
    }

    private void ApplyPalette()
    {
        if (Chart?.Theme?.Palette is not { Length: > 0 } palette)
        {
            return;
        }

        var lines = Chart.Series.OfType<Series.LineSeries<double, float>>().ToArray();
        for (var i = 0; i < display.Traces.Count; i++)
        {
            var color = palette[i * 2 % palette.Length].Value;
            display.Traces[i].Color = color;
            if (i < lines.Length)
            {
                lines[i].Color = color;
            }
        }
    }

    private void DrawTable(OverlayCanvas canvas, Layout.DipRect pane)
    {
        var height = canvas.GetLineHeight(LabelSize) + 4;
        var rows = display.Markers.Count * display.Traces.Count;
        if (rows == 0)
        {
            return;
        }

        var width = 250.0;
        var top = pane.Bottom - (rows * height) - 10;
        var left = pane.Right - width - 8;
        canvas.FillRectangle(new Layout.DipRect(left, top, width, (rows * height) + 6), canvas.BackgroundColor with { A = 215 });
        var baseline = top + height;
        var unit = display.Format == NetworkFormat.Phase ? "°" : " dB";
        var number = 0;
        foreach (var marker in display.Markers)
        {
            number++;
            foreach (var trace in display.Traces)
            {
                var value = trace.Read(marker) is { } complex ? display.ToLevel(complex).ToString("F2", CultureInfo.InvariantCulture) + unit : "---";
                var text = number.ToString(CultureInfo.InvariantCulture) + "  " + trace.Name + "  " + ScopeFormat.Value(marker, "Hz") + "  " + value;
                canvas.DrawText(text, Math.Round(left + 8), Math.Round(baseline), LabelSize, trace.Color);
                baseline += height;
            }
        }
    }

    private void DrawResults(OverlayCanvas canvas, Layout.DipRect pane)
    {
        var baseline = pane.Y + canvas.GetLineHeight(LabelSize) + 4;
        var x = pane.X + 10;
        foreach (var trace in display.Traces)
        {
            var hasLimits = trace.Limits.Count > 0 && display.AreLimitsActive;
            var verdict = ChartText.Get(trace.Passes ? ChartTextKey.Pass : ChartTextKey.Fail);
            var text = trace.Name + (hasLimits ? "  " + verdict : string.Empty);
            var color = hasLimits && !trace.Passes ? new Rgba8(255, 90, 80, 255) : trace.Color;
            canvas.DrawText(text, Math.Round(x), Math.Round(baseline), LabelSize, color);
            x += canvas.MeasureWidth(text, LabelSize) + 18;
        }
    }
}

/// <summary>Describes a network analyzer, for WPF, Avalonia and charts without a UI framework.</summary>
public static class NetworkChart
{
    /// <summary>Describes a chart that shows the traces over frequency.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="display">The state.</param>
    /// <param name="start">The left edge in hertz.</param>
    /// <param name="stop">The right edge in hertz.</param>
    /// <param name="minimum">The lowest level.</param>
    /// <param name="maximum">The highest level.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder NetworkAnalyzer(this ChartBuilder builder, NetworkDisplay display, double start, double stop, double minimum, double maximum)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(display);
        builder
            .XAxis(a => a.Numeric("frequency").Range(start, stop))
            .YAxis(a => a.Numeric("level").Title(display.Format == NetworkFormat.Phase ? "°" : "dB").Range(minimum, maximum));
        foreach (var trace in display.Traces)
        {
            builder.AddLine(trace.Data, s => s.Name(trace.Name).Stroke(trace.Color).Thickness(1.5));
        }

        return builder.AddOverlay(chart =>
        {
            if (chart.Series.FirstOrDefault()?.XAxis is Axes.NumericAxis axis)
            {
                axis.LabelFormat = new Axes.NumericLabelFormat { UseSiPrefixes = true, Unit = "Hz", Culture = CultureInfo.InvariantCulture };
            }

            return new NetworkOverlay(display) { Chart = chart };
        });
    }
}
