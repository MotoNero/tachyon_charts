using System.Numerics;
using Tachyon.Charts.Core.Fluent;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Core.Overlays;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Instruments;

/// <summary>Where a trace broke through a frequency mask.</summary>
/// <param name="Frequency">The frequency of the highest violation in hertz.</param>
/// <param name="Level">The level there.</param>
/// <param name="Limit">The limit there.</param>
/// <param name="PointCount">The number of points above the limit.</param>
public readonly record struct MaskViolation(double Frequency, double Level, double Limit, int PointCount);

/// <summary>An upper limit over frequency. Between its points the limit runs straight.</summary>
public sealed class FrequencyMask
{
    private readonly double[] _frequencies;
    private readonly double[] _levels;

    /// <summary>Initializes a new instance of the <see cref="FrequencyMask"/> class.</summary>
    /// <param name="frequencies">The corners in hertz, ascending.</param>
    /// <param name="levels">The limit at each corner.</param>
    /// <exception cref="ArgumentException">The lengths differ, or there are fewer than two corners.</exception>
    public FrequencyMask(double[] frequencies, double[] levels)
    {
        ArgumentNullException.ThrowIfNull(frequencies);
        ArgumentNullException.ThrowIfNull(levels);
        if (frequencies.Length != levels.Length || frequencies.Length < 2)
        {
            throw new ArgumentException("A mask needs at least two corners with a level each.", nameof(levels));
        }

        _frequencies = frequencies;
        _levels = levels;
    }

    /// <summary>Gets the corners in hertz.</summary>
    public IReadOnlyList<double> Frequencies => _frequencies;

    /// <summary>Gets the limit at each corner.</summary>
    public IReadOnlyList<double> Levels => _levels;

    /// <summary>Creates the same mask higher or lower.</summary>
    /// <param name="level">The change of all limits.</param>
    /// <returns>The new mask.</returns>
    public FrequencyMask Shift(double level)
    {
        var levels = new double[_levels.Length];
        for (var i = 0; i < levels.Length; i++)
        {
            levels[i] = _levels[i] + level;
        }

        return new FrequencyMask((double[])_frequencies.Clone(), levels);
    }

    /// <summary>Finds the limit at a frequency.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <returns>The limit, or positive infinity outside of the mask.</returns>
    public double GetLimit(double frequency)
    {
        if (frequency < _frequencies[0] || frequency > _frequencies[^1])
        {
            return double.PositiveInfinity;
        }

        var index = Array.BinarySearch(_frequencies, frequency);
        if (index >= 0)
        {
            return _levels[index];
        }

        var upper = ~index;
        var share = (frequency - _frequencies[upper - 1]) / (_frequencies[upper] - _frequencies[upper - 1]);
        return _levels[upper - 1] + (share * (_levels[upper] - _levels[upper - 1]));
    }

    /// <summary>Checks a trace.</summary>
    /// <param name="frequencies">The frequencies in hertz.</param>
    /// <param name="levels">The levels.</param>
    /// <returns>The worst violation, or <see langword="null"/> if the trace stays below.</returns>
    public MaskViolation? Check(ReadOnlySpan<double> frequencies, ReadOnlySpan<float> levels)
    {
        var count = 0;
        var worst = double.NegativeInfinity;
        var result = default(MaskViolation);
        for (var i = 0; i < frequencies.Length && i < levels.Length; i++)
        {
            var limit = GetLimit(frequencies[i]);
            var excess = levels[i] - limit;
            if (excess > 0)
            {
                count++;
                if (excess > worst)
                {
                    worst = excess;
                    result = new MaskViolation(frequencies[i], levels[i], limit, 0);
                }
            }
        }

        return count == 0 ? null : result with { PointCount = count };
    }
}

/// <summary>The state of a spectrum analyzer: frequency span, level range, traces and mask.</summary>
public sealed class SpectrumDisplay
{
    private const int MaximumExtraMarkers = 5;

    private double[] _extraMarkers = [];
    private const int Divisions = 10;
    private const int HistoryColumns = 1024;
    private const int HistoryRows = 400;

    private readonly ScopeDisplay _scope = new();
    private readonly Lock _gate = new();
    private readonly float[] _historyRow = new float[HistoryColumns];
    private float[] _maxHold = [];
    private double[] _holdFrequencies = [];
    private double _start;
    private double _stop = 1;
    private double _referenceLevel;
    private double _perDivision = 10;
    private (double Frequency, double Level) _peak = (double.NaN, double.NaN);
    private MaskViolation? _lastViolation;
    private bool _hasHistorySpan;

    /// <summary>Initializes a new instance of the <see cref="SpectrumDisplay"/> class.</summary>
    public SpectrumDisplay()
    {
        Trace = _scope.AddChannel();
        Hold = _scope.AddChannel();

        // The reference level may be any number of divisions away from 0 dBm.
        Trace.IsUnbounded = Hold.IsUnbounded = true;
        Apply();
    }

    /// <summary>Gets the traces of the last time, newest first, for the spectrogram below the spectrum.</summary>
    public Data.SpectrogramData History { get; } = new(HistoryColumns, HistoryRows);

    /// <summary>Occurs when a trace breaks through the mask, on the thread that delivered the trace.</summary>
    public event Action<MaskViolation>? MaskViolated;

    /// <summary>Gets or sets the mask, or <see langword="null"/> for none.</summary>
    public FrequencyMask? Mask
    {
        get => field;
        set
        {
            field = value;
            _scope.Touch();
        }
    }

    /// <summary>Gets or sets a value indicating whether the highest values stay visible.</summary>
    public bool ShowsMaxHold
    {
        get => field;
        set
        {
            field = value;
            _scope.Touch();
        }
    } = true;

    /// <summary>Gets or sets a value indicating whether the color shows how often a level occurred.</summary>
    public bool ShowsDensity
    {
        get => _scope.Persistence == ScopePersistence.ColorGrade;
        set => _scope.Persistence = value ? ScopePersistence.ColorGrade : ScopePersistence.Off;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the axes show their numbers: the frequency below with its unit (Hz, kHz,
    /// MHz, GHz) and the level in dBm on the left. Without them, as on many instruments, the readings in the corners
    /// name reference level, division, start and stop.
    /// </summary>
    public bool ShowsAxes
    {
        get => field;
        set
        {
            field = value;
            _scope.Touch();
        }
    }

    /// <summary>Gets or sets the frequency of the marker, or <see langword="null"/> for the peak.</summary>
    public double? MarkerFrequency
    {
        get => field;
        set
        {
            field = value;
            _scope.Touch();
        }
    }

    /// <summary>Gets the frequencies of the further markers, at most five.</summary>
    public IReadOnlyList<double> ExtraMarkers => Volatile.Read(ref _extraMarkers);

    /// <summary>Gets the point the marker shows.</summary>
    public (double Frequency, double Level) MarkerPoint => MarkerFrequency is { } frequency ? ReadPoint(frequency) : Peak;

    /// <summary>Adds a further marker. Beyond five further markers nothing happens.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    public void AddMarker(double frequency)
    {
        using (_gate.EnterScope())
        {
            if (_extraMarkers.Length >= MaximumExtraMarkers)
            {
                return;
            }

            Volatile.Write(ref _extraMarkers, [.. _extraMarkers, frequency]);
        }

        _scope.Touch();
    }

    /// <summary>Removes the further marker next to a frequency.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <param name="tolerance">How far away the marker may be, in hertz.</param>
    /// <returns>Whether a marker was removed.</returns>
    public bool RemoveMarker(double frequency, double tolerance)
    {
        using (_gate.EnterScope())
        {
            var index = Array.FindIndex(_extraMarkers, marker => Math.Abs(marker - frequency) <= tolerance);
            if (index < 0)
            {
                return false;
            }

            Volatile.Write(ref _extraMarkers, [.. _extraMarkers[..index], .. _extraMarkers[(index + 1)..]]);
        }

        _scope.Touch();
        return true;
    }

    /// <summary>Reads the trace next to a frequency.</summary>
    /// <param name="frequency">The frequency in hertz.</param>
    /// <returns>The point, or NaN without a trace.</returns>
    public (double Frequency, double Level) ReadPoint(double frequency)
    {
        var snapshot = Trace.Data.GetSnapshot();
        var values = snapshot.X.Span;
        if (values.IsEmpty)
        {
            return (double.NaN, double.NaN);
        }

        var index = values.BinarySearch(frequency);
        index = index >= 0 ? index : Math.Min(~index, values.Length - 1);
        return (values[index], snapshot.Y.Span[index]);
    }

    /// <summary>Gets the highest point of the last trace.</summary>
    public (double Frequency, double Level) Peak
    {
        get
        {
            using (_gate.EnterScope())
            {
                return _peak;
            }
        }
    }

    /// <summary>Gets the violation of the last trace, or <see langword="null"/> if it passed.</summary>
    public MaskViolation? LastViolation
    {
        get
        {
            using (_gate.EnterScope())
            {
                return _lastViolation;
            }
        }
    }

    /// <summary>Gets the number of the current span, for updates of the display.</summary>
    internal long Version => _scope.Version;

    internal ScopeDisplay Scope => _scope;

    internal ScopeChannel Trace { get; }

    internal ScopeChannel Hold { get; }

    /// <summary>Gets the left edge in hertz.</summary>
    public double Start => _start;

    /// <summary>Gets the first frequency of the history.</summary>
    public double HistoryStart { get; private set; }

    /// <summary>Gets the last frequency of the history.</summary>
    public double HistoryStop { get; private set; } = 1;

    /// <summary>Gets the right edge in hertz.</summary>
    public double Stop => _stop;

    /// <summary>Gets the level at the top edge.</summary>
    public double ReferenceLevel => _referenceLevel;

    /// <summary>Gets the level per division, of <see cref="ScopeDisplay.VerticalDivisions"/> divisions.</summary>
    public double PerDivision => _perDivision;

    /// <summary>Sets what the display shows.</summary>
    /// <param name="start">The left edge in hertz.</param>
    /// <param name="stop">The right edge in hertz.</param>
    /// <param name="referenceLevel">The level at the top edge.</param>
    /// <param name="perDivision">The level per division.</param>
    /// <exception cref="ArgumentOutOfRangeException">The span or the scale is not positive.</exception>
    public void SetView(double start, double stop, double referenceLevel, double perDivision = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(stop, start);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perDivision);
        _start = start;
        _stop = stop;
        _referenceLevel = referenceLevel;
        _perDivision = perDivision;
        if (!_hasHistorySpan)
        {
            HistoryStart = start;
            HistoryStop = stop;
            _hasHistorySpan = true;
        }

        Apply();
    }

    /// <summary>Delivers a measured spectrum, from any thread.</summary>
    /// <param name="frequencies">The frequencies in hertz, ascending.</param>
    /// <param name="levels">The levels.</param>
    public void SetTrace(ReadOnlySpan<double> frequencies, ReadOnlySpan<float> levels)
    {
        var peak = FindPeak(frequencies, levels);
        using (_gate.EnterScope())
        {
            _peak = peak;
            if (_maxHold.Length != levels.Length)
            {
                _maxHold = levels.ToArray();
                _holdFrequencies = frequencies.ToArray();
            }

            for (var i = 0; i < levels.Length; i++)
            {
                _maxHold[i] = MathF.Max(_maxHold[i], levels[i]);
            }

            Hold.SetWaveform(_holdFrequencies, _maxHold);
        }

        var violation = Mask?.Check(frequencies, levels);
        using (_gate.EnterScope())
        {
            _lastViolation = violation;
        }

        AddHistory(frequencies, levels);
        Trace.SetWaveform(frequencies, levels);
        if (violation is { } found)
        {
            MaskViolated?.Invoke(found);
        }
    }

    // Each column keeps its highest level: a short carrier must not slip between two columns.
    private void AddHistory(ReadOnlySpan<double> frequencies, ReadOnlySpan<float> levels)
    {
        var width = (HistoryStop - HistoryStart) / HistoryColumns;
        Array.Fill(_historyRow, float.NegativeInfinity);
        for (var i = 0; i < levels.Length; i++)
        {
            var column = (int)((frequencies[i] - HistoryStart) / width);
            if (column >= 0 && column < HistoryColumns)
            {
                _historyRow[column] = MathF.Max(_historyRow[column], levels[i]);
            }
        }

        for (var i = 1; i < HistoryColumns; i++)
        {
            _historyRow[i] = float.IsNegativeInfinity(_historyRow[i]) ? _historyRow[i - 1] : _historyRow[i];
        }

        History.AppendRow(_historyRow);
    }

    /// <summary>Forgets the highest values.</summary>
    public void ClearMaxHold()
    {
        using (_gate.EnterScope())
        {
            _maxHold = [];
        }
    }

    /// <summary>Finds the highest point.</summary>
    /// <param name="frequencies">The frequencies.</param>
    /// <param name="levels">The levels.</param>
    /// <returns>The peak, NaN without values.</returns>
    public static (double Frequency, double Level) FindPeak(ReadOnlySpan<double> frequencies, ReadOnlySpan<float> levels)
    {
        var best = -1;
        for (var i = 0; i < levels.Length && i < frequencies.Length; i++)
        {
            if (float.IsFinite(levels[i]) && (best < 0 || levels[i] > levels[best]))
            {
                best = i;
            }
        }

        return best < 0 ? (double.NaN, double.NaN) : (frequencies[best], levels[best]);
    }

    private void Apply()
    {
        var division = (_stop - _start) / Divisions;
        _scope.TimePerDivision = division;
        _scope.TimeOffset = (_start + _stop) / 2;
        foreach (var channel in _scope.Channels)
        {
            channel.VoltsPerDivision = _perDivision;
            channel.Position = -((_referenceLevel / _perDivision) - (ScopeDisplay.VerticalDivisions / 2.0));
        }
    }
}

/// <summary>The screen of a spectrum analyzer: graticule, mask, peak marker and readouts.</summary>
/// <param name="display">The state.</param>
public sealed class SpectrumOverlay(SpectrumDisplay display) : ChartOverlay
{
    private const float LabelSize = 12;
    private const float MarkerSize = 7;

    /// <inheritdoc />
    protected internal override long ExternalVersion => display.Version + ChartText.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        var pane = canvas.Layout.Panes[0];
        var line = canvas.TextColor with { A = 34 };
        for (var i = 0; i <= ScopeDisplay.HorizontalDivisions; i++)
        {
            var x = (float)Math.Round(pane.X + (i * pane.Width / ScopeDisplay.HorizontalDivisions));
            canvas.DrawLine(new Vector2(x, (float)pane.Y), new Vector2(x, (float)pane.Bottom), 1, line);
        }

        for (var i = 0; i <= ScopeDisplay.VerticalDivisions; i++)
        {
            var y = (float)Math.Round(pane.Y + (i * pane.Height / ScopeDisplay.VerticalDivisions));
            canvas.DrawLine(new Vector2((float)pane.X, y), new Vector2((float)pane.Right, y), 1, line);
        }

        Vector2 ToPoint(double frequency, double level)
        {
            var bottom = display.ReferenceLevel - (display.PerDivision * ScopeDisplay.VerticalDivisions);
            return new Vector2(
                (float)(pane.X + (pane.Width * (frequency - display.Start) / (display.Stop - display.Start))),
                (float)(pane.Bottom - (pane.Height * (level - bottom) / (display.ReferenceLevel - bottom))));
        }

        if (display.Mask is { } mask)
        {
            var color = new Rgba8(240, 80, 70, 255);
            for (var i = 1; i < mask.Frequencies.Count; i++)
            {
                var from = Math.Max(mask.Frequencies[i - 1], display.Start);
                var to = Math.Min(mask.Frequencies[i], display.Stop);
                if (from < to)
                {
                    canvas.DrawLine(ToPoint(from, mask.GetLimit(from)), ToPoint(to, mask.GetLimit(to)), 1.5f, color);
                }
            }
        }

        var height = canvas.GetLineHeight(LabelSize) + 5;
        var peak = display.MarkerPoint;
        var extras = display.ExtraMarkers;
        for (var i = 0; i < extras.Count; i++)
        {
            var (frequency, level) = display.ReadPoint(extras[i]);
            if (double.IsFinite(frequency))
            {
                var point = ToPoint(frequency, level);
                var color = new Rgba8(255, 160, 50, 255);
                var name = (i + 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
                canvas.FillTriangle(point, point + new Vector2(-MarkerSize, -MarkerSize * 1.5f), point + new Vector2(MarkerSize, -MarkerSize * 1.5f), color);
                canvas.DrawText(name, Math.Round(point.X - 3), Math.Round(point.Y - (MarkerSize * 2)), LabelSize, color);
                var row = "Mkr" + name + " " + ScopeFormat.Value(frequency, "Hz") + "   " + level.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " dBm";
                canvas.DrawText(row, Math.Round(pane.Right - canvas.MeasureWidth(row, LabelSize) - 10), Math.Round(pane.Y + (height * (i + 2))), LabelSize, canvas.TextColor);
            }
        }

        if (double.IsFinite(peak.Frequency))
        {
            var point = ToPoint(peak.Frequency, peak.Level);
            var orange = new Rgba8(255, 160, 50, 255);
            canvas.FillTriangle(point, point + new Vector2(-MarkerSize, -MarkerSize * 1.5f), point + new Vector2(MarkerSize, -MarkerSize * 1.5f), orange);
            var text = "Mkr " + ScopeFormat.Value(peak.Frequency, "Hz") + "   " + peak.Level.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + " dBm";
            canvas.DrawText(text, Math.Round(pane.Right - canvas.MeasureWidth(text, LabelSize) - 10), Math.Round(pane.Y + height), LabelSize, canvas.TextColor);
        }

        if (display.LastViolation is { } violation)
        {
            var failed = ChartText.Get(ChartTextKey.MaskFail) + "  " + ScopeFormat.Value(violation.Frequency, "Hz");
            canvas.DrawText(failed, Math.Round(pane.Right - canvas.MeasureWidth(failed, LabelSize) - 10), Math.Round(pane.Y + (height * (extras.Count + 2))), LabelSize, new Rgba8(255, 90, 80, 255));
        }

        var reference = "Ref " + display.ReferenceLevel.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " dBm   " + display.PerDivision.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + " dB/div";
        canvas.DrawText(reference, Math.Round(pane.X + 10), Math.Round(pane.Y + height), LabelSize, canvas.TextColor);
        var span = "Start " + ScopeFormat.Value(display.Start, "Hz") + "   Stop " + ScopeFormat.Value(display.Stop, "Hz");
        canvas.DrawText(span, Math.Round(pane.X + 10), Math.Round(pane.Bottom - 8), LabelSize, canvas.TextColor);
    }
}

/// <summary>Keeps the trace of the highest values visible or hidden.</summary>
/// <param name="chart">The chart.</param>
/// <param name="display">The state.</param>
internal sealed class SpectrumSync(ChartSurface chart, SpectrumDisplay display) : ChartOverlay
{
    /// <inheritdoc />
    protected internal override long ExternalVersion => display.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        var lines = chart.Series.OfType<LineSeries<double, float>>().ToArray();
        if (lines.Length == 2)
        {
            lines[1].IsVisible = display.ShowsMaxHold;
        }

        ShowAxes();
    }

    // The frequency below and the level of the trace on the left, when the display wants them; the other channels'
    // axes stay hidden. The vertical axes come in the order of the channels.
    private void ShowAxes()
    {
        var shows = display.ShowsAxes;
        if (chart.HorizontalAxes[0] is Axes.NumericAxis frequency && frequency.IsHidden == shows)
        {
            frequency.IsHidden = !shows;
            frequency.Title = shows ? "Frequency" : null;
            frequency.LabelFormat = _hertz;
        }

        var channels = display.Scope.Channels;
        var axes = chart.Panes[0].Axes;
        for (var i = 0; i < channels.Count && i < axes.Count; i++)
        {
            var visible = shows && channels[i] == display.Trace;
            if (axes[i].IsHidden == visible)
            {
                axes[i].IsHidden = !visible;
                axes[i].Title = visible ? "Level in dBm" : null;
                if (visible && axes[i] is Axes.NumericAxis level)
                {
                    level.LabelFormat = new Axes.NumericLabelFormat();
                }
            }

            // The crosshair reads hidden axes too: frequency in hertz, level in dBm.
            ScopeSync.WithUnit(axes[i], _dbm);
        }

        ScopeSync.WithUnit(chart.HorizontalAxes[0], _hertz);
    }

    private static readonly Axes.NumericLabelFormat _dbm = new() { Unit = "dBm" };

    private static readonly Axes.NumericLabelFormat _hertz = new() { UseSiPrefixes = true, Unit = "Hz" };
}

/// <summary>Keeps a spectrogram exactly below its spectrum: same frequencies, no color bar.</summary>
/// <param name="chart">The chart.</param>
/// <param name="display">The state.</param>
internal sealed class SpectrumHistorySync(ChartSurface chart, SpectrumDisplay display) : ChartOverlay
{
    /// <inheritdoc />
    protected internal override long ExternalVersion => display.Version;

    /// <inheritdoc />
    protected internal override void Draw(OverlayCanvas canvas)
    {
        chart.HorizontalAxes[0].VisibleRange = new DoubleRange(display.Start, display.Stop);
        foreach (var axis in chart.Panes[0].Axes.OfType<Axes.ColorAxis>())
        {
            axis.IsHidden = true;
            axis.VisibleRange = new DoubleRange(display.ReferenceLevel - (display.PerDivision * ScopeDisplay.VerticalDivisions), display.ReferenceLevel);
        }
    }
}

/// <summary>Describes a spectrum analyzer, for WPF, Avalonia and charts without a UI framework.</summary>
public static class SpectrumChart
{
    /// <summary>
    /// Describes the spectrogram below a spectrum, as on a real-time analyzer: the frequencies line up,
    /// the newest trace is at the top. Call <see cref="SpectrumDisplay.SetView"/> first.
    /// </summary>
    /// <param name="builder">The description.</param>
    /// <param name="display">The state.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder SpectrumHistory(this ChartBuilder builder, SpectrumDisplay display)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(display);
        var data = display.History;
        return builder
            .XAxis(a => a.Numeric("frequency").Range(display.Start, display.Stop).Hidden())
            .YAxis(a => a.Numeric("time").Range(-data.Rows, 0).Hidden())
            .AddSpectrogram(data, s => s
                .Cells(display.HistoryStart, (display.HistoryStop - display.HistoryStart) / data.Columns, 1)
                .ColorAxis(a => a.Colormap(Colors.Colormaps.Inferno).Range(display.ReferenceLevel - 80, display.ReferenceLevel)))
            .AddOverlay(chart => new SpectrumHistorySync(chart, display));
    }

    /// <summary>Describes a chart that shows a spectrum.</summary>
    /// <param name="builder">The description.</param>
    /// <param name="display">The state.</param>
    /// <returns>The description.</returns>
    public static ChartBuilder Spectrum(this ChartBuilder builder, SpectrumDisplay display)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(display);
        var scope = display.Scope;
        var time = scope.GetTimeRange();
        builder.XAxis(a => a.Numeric("frequency").Range(time.Min, time.Max).Hidden());
        foreach (var channel in scope.Channels)
        {
            var range = channel.GetRange();
            builder.YAxis(a => a.Numeric(channel.AxisId).Range(range.Min, range.Max).Hidden());
        }

        builder.AddSeries((x, y) => new ScopePersistenceSeries(scope, display.Trace, x, y), yAxis: display.Trace.AxisId);
        builder.AddLine(display.Trace.Data, s => s.Name("Trace").YAxis(display.Trace.AxisId).Stroke(new Rgba8(245, 214, 60, 255)).Thickness(1.2));
        builder.AddLine(display.Hold.Data, s => s.Name("Max Hold").YAxis(display.Hold.AxisId).Stroke(new Rgba8(90, 170, 255, 255)).Thickness(1));
        return builder
            .AddOverlay(chart => new ScopeSync(chart, scope) { KeepsLines = true, PaletteStep = 2 })
            .AddOverlay(chart => new SpectrumSync(chart, display))
            .AddOverlay(_ => new SpectrumOverlay(display))
            .Modifiers(m => m.Custom(() => new SpectrumModifier(display)));
    }
}
