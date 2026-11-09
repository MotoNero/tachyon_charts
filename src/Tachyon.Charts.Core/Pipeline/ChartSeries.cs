using System.Numerics;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Data;
using Tachyon.Charts.Core.HitTesting;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Pipeline;

/// <summary>
/// Where a series draws in a frame.
/// </summary>
/// <param name="Bounds">The pane of the Y axis in physical pixels.</param>
/// <param name="X">Converts X values to physical pixels.</param>
/// <param name="Y">Converts Y values to physical pixels.</param>
/// <param name="DpiScale">Physical pixels per device-independent unit.</param>
/// <param name="GlowRadius">The reach of the glow of the theme in device-independent units, zero for none.</param>
/// <param name="GlowIntensity">The strength of the glow of the theme, from 0 to 1.</param>
public readonly record struct SeriesFrame(PixelRect Bounds, LinearAxisTransform X, LinearAxisTransform Y, double DpiScale, double GlowRadius = 0, double GlowIntensity = 0)
{
    /// <summary>
    /// Gets the sum of the quarter turn of a chart whose X axis runs down, or <see langword="null"/> for an upright one.
    /// The series then draws upright in <see cref="Bounds"/>, and the chart turns the picture; see <see cref="ICommandList.SetQuarterTurn"/>.
    /// </summary>
    public float? QuarterTurn { get; init; }
}

/// <summary>
/// The visual representation of data in a chart.
/// </summary>
/// <remarks>All members run on the render thread, unless the chart is rendered directly.</remarks>
/// <param name="xAxis">The horizontal axis.</param>
/// <param name="yAxis">The vertical axis, which also selects the pane.</param>
public abstract class ChartSeries(AxisBase xAxis, AxisBase yAxis)
{
    private bool _isVisible = true;
    private bool _isHighlighted;

    /// <summary>
    /// Gets the horizontal axis.
    /// </summary>
    public AxisBase XAxis { get; } = xAxis ?? throw new ArgumentNullException(nameof(xAxis));

    /// <summary>
    /// Gets the vertical axis.
    /// </summary>
    public AxisBase YAxis { get; } = yAxis ?? throw new ArgumentNullException(nameof(yAxis));

    /// <summary>
    /// Gets or sets the name shown in rollovers and legends.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Gets a value indicating whether the series turns with a chart whose X axis runs down. A pie does not.</summary>
    internal virtual bool TurnsWithChart => true;

    /// <summary>
    /// Gets or sets a note for the diagnostics overlay, for example about data that is too large for
    /// the way it arrives.
    /// </summary>
    public string? DiagnosticWarning { get; set; }

    /// <summary>Adds lines of its own to the diagnostics overlay, a few times per second while it is shown.</summary>
    /// <param name="text">Receives the lines.</param>
    protected internal virtual void WriteDiagnostics(DiagnosticsText text)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the series is drawn and found by hit tests.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible != value)
            {
                _isVisible = value;
                StateVersion++;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the series stands out from the others.
    /// </summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                StateVersion++;
            }
        }
    }

    internal long StateVersion { get; private set; }

    /// <summary>
    /// Gets the color that stands for this series in labels.
    /// </summary>
    public virtual Rgba8 AccentColor => new(150, 160, 175, 255);

    /// <summary>
    /// Gets the value scale, for series that color their values.
    /// </summary>
    public virtual ColorAxis? ColorAxis => null;

    /// <summary>
    /// Finds the range of the colored values, for a color axis that follows the data.
    /// </summary>
    /// <param name="range">Receives the range.</param>
    /// <returns>Whether there is a range.</returns>
    protected internal virtual bool TryGetValueRange(out DoubleRange range)
    {
        range = DoubleRange.Empty;
        return false;
    }

    /// <summary>
    /// Gets a version that grows with every change that needs a new frame. Read once per frame interval.
    /// </summary>
    protected internal abstract long Version { get; }

    /// <summary>
    /// Gets the number of points drawn in the last frame, a diagnostic counter.
    /// </summary>
    protected internal virtual int DrawnPointCount => 0;

    /// <summary>
    /// Gets the number of points in the data, a diagnostic counter.
    /// </summary>
    protected internal virtual long SourcePointCount => DrawnPointCount;

    /// <summary>
    /// Finds the point of this series nearest to a position. Runs on the thread of the caller, usually
    /// the UI thread, so implementations only touch thread-safe data.
    /// </summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="pixel">The position in physical pixels.</param>
    /// <param name="maximumDistance">The largest distance in physical pixels.</param>
    /// <param name="mode">What counts as nearest.</param>
    /// <param name="index">Receives the index in the data.</param>
    /// <param name="x">Receives the X value in units of the axis.</param>
    /// <param name="y">Receives the Y value.</param>
    /// <returns>Whether a point is close enough. The default is that a series has no points to find.</returns>
    protected internal virtual bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y)
    {
        index = -1;
        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Collects the points inside an area. Runs on the thread of the caller.
    /// </summary>
    /// <param name="frame">The place of the series.</param>
    /// <param name="area">The area in physical pixels.</param>
    /// <param name="indices">Receives the numbers of the points.</param>
    protected internal virtual void CollectPoints(in SeriesFrame frame, in SelectionArea area, List<long> indices)
    {
    }

    /// <summary>
    /// Reads a point by its number. Runs on the thread of the caller.
    /// </summary>
    /// <param name="index">The number of the point.</param>
    /// <param name="x">Receives the X value in units of the axis.</param>
    /// <param name="y">Receives the Y value.</param>
    /// <returns>Whether the point exists.</returns>
    protected internal virtual bool TryReadPoint(long index, out double x, out double y)
    {
        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Reads the colored value of a point. Runs on the thread of the caller.
    /// </summary>
    /// <param name="index">The number of the point.</param>
    /// <param name="value">Receives the value.</param>
    /// <returns>Whether the point has a value.</returns>
    protected internal virtual bool TryReadValue(long index, out double value)
    {
        value = double.NaN;
        return false;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the newest value shows as a colored mark at the vertical axis, live.
    /// Marks of several series move apart so that they never overlap.
    /// </summary>
    public bool ShowsLastValue { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the values show as labels at their points: above a point or bar, in the
    /// middle of a cell or a stacked part. Labels that would cover others are left out, so dense data show only some.
    /// </summary>
    public bool ShowsValues { get; set; }

    /// <summary>
    /// Hands over the labels of the values within the visible ranges, in the units of the axes, for
    /// <see cref="ShowsValues"/>. Runs on the render thread. By default the points that <see cref="TryReadPoint"/> reads,
    /// above each point; series with bars, cells or groups write their own.
    /// </summary>
    /// <param name="x">The visible range of the X axis.</param>
    /// <param name="y">The visible range of the Y axis.</param>
    /// <param name="labels">Receives the labels.</param>
    protected internal virtual void CollectValueLabels(DoubleRange x, DoubleRange y, List<ValueLabel> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var count = SourcePointCount;
        for (long i = 0; i < count && labels.Count < Overlays.ValueLabelMarks.Limit; i++)
        {
            if (TryReadPoint(i, out var px, out var py) && x.Contains(px) && y.Contains(py))
            {
                labels.Add(new ValueLabel(px, py, py, ValueLabelPlace.Above));
            }
        }
    }

    /// <summary>Reads the newest value for the mark at the axis. Runs on the render thread.</summary>
    /// <param name="value">Receives the value.</param>
    /// <returns>Whether there is one. The default is that a series has none.</returns>
    protected internal virtual bool TryGetLatestValue(out double value)
    {
        value = double.NaN;
        return false;
    }

    /// <summary>
    /// Finds the data range for axes that follow the data.
    /// </summary>
    /// <param name="x">Receives the X range in units of the X axis.</param>
    /// <param name="y">Receives the Y range.</param>
    /// <returns>Whether there is a range. The default is that there is none.</returns>
    protected internal virtual bool TryGetDataRange(out DoubleRange x, out DoubleRange y)
    {
        x = DoubleRange.Empty;
        y = DoubleRange.Empty;
        return false;
    }

    /// <summary>
    /// Fixes the data of the coming frame. Runs for all series before anything is drawn.
    /// </summary>
    protected internal virtual void TakeSnapshot()
    {
    }

    /// <summary>
    /// Prepares the frame, for example by uploading data. No frame is open yet.
    /// </summary>
    /// <param name="device">The device.</param>
    /// <param name="frame">The place of the series.</param>
    protected internal virtual void Prepare(IRenderDevice device, in SeriesFrame frame)
    {
    }

    /// <summary>
    /// Takes the colors of a new theme. Series with colors set by the application leave this alone,
    /// special chart types without their own color choice follow the theme.
    /// </summary>
    /// <param name="palette">The colors of the theme, at least one.</param>
    protected internal virtual void OnPalette(ReadOnlySpan<Rgba8> palette)
    {
    }

    /// <summary>
    /// Frees what belongs to the device, because the chart moves to another one, for example for an
    /// export. The next <see cref="Prepare"/> creates it again. Series of their own that keep device
    /// objects have to override this.
    /// </summary>
    protected internal virtual void ReleaseResources()
    {
    }

    /// <summary>
    /// Draws the series in the series pass.
    /// </summary>
    /// <param name="commands">The open frame.</param>
    /// <param name="frame">The place of the series.</param>
    protected internal abstract void Draw(ICommandList commands, in SeriesFrame frame);
}

/// <summary>
/// A series that shows a data source.
/// </summary>
/// <typeparam name="TX">The X type.</typeparam>
/// <typeparam name="TY">The Y type.</typeparam>
/// <param name="source">The data.</param>
/// <param name="xAxis">The horizontal axis.</param>
/// <param name="yAxis">The vertical axis.</param>
public abstract class DataSeries<TX, TY>(IDataSource<TX, TY> source, AxisBase xAxis, AxisBase yAxis) : ChartSeries(xAxis, yAxis)
    where TX : unmanaged, INumber<TX>
    where TY : unmanaged, INumber<TY>
{
    private readonly SeriesHitTester<TX, TY> _hitTester = new(source);
    private long _appearanceVersion;
    private bool _hasSnapshot;

    private TX XOrigin => XAxis is DateTimeAxis time ? TX.CreateTruncating(time.OriginTicks) : TX.Zero;

    /// <summary>
    /// Gets the data.
    /// </summary>
    public IDataSource<TX, TY> Source { get; } = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>
    /// Gets the data of the current frame, which does not change while the frame is drawn.
    /// </summary>
    protected DataSnapshot<TX, TY> Snapshot { get; private set; }

    /// <inheritdoc />
    protected internal override long Version => Source.Version + Volatile.Read(ref _appearanceVersion);

    /// <inheritdoc />
    protected internal override long SourcePointCount => Snapshot.X.Length;

    /// <inheritdoc />
    protected internal override bool TryGetLatestValue(out double value)
    {
        var found = Source.TryGetLatest(out var y);
        value = found ? double.CreateTruncating(y) : double.NaN;
        return found && double.IsFinite(value);
    }

    /// <inheritdoc />
    protected internal override void TakeSnapshot()
    {
        if (!_hasSnapshot || Snapshot.Version != Source.Version)
        {
            Snapshot = Source.GetSnapshot();
            _hasSnapshot = true;
        }
    }

    /// <inheritdoc />
    protected internal override bool TryFindPoint(in SeriesFrame frame, Vector2 pixel, double maximumDistance, HitTestMode mode, out long index, out double x, out double y) =>
        _hitTester.TryFind(frame, XOrigin, pixel, maximumDistance, mode, out index, out x, out y);

    /// <inheritdoc />
    protected internal override void CollectPoints(in SeriesFrame frame, in SelectionArea area, List<long> indices) =>
        _hitTester.CollectPoints(frame, XOrigin, area, indices);

    /// <inheritdoc />
    protected internal override bool TryReadPoint(long index, out double x, out double y) =>
        _hitTester.TryReadPoint(XOrigin, index, out x, out y);

    /// <summary>
    /// Requests a new frame after a change of the appearance.
    /// </summary>
    protected void OnAppearanceChanged() => Interlocked.Increment(ref _appearanceVersion);
}

/// <summary>Where a value label sits at its point.</summary>
public enum ValueLabelPlace
{
    /// <summary>Above the point, as on a bar or a marker.</summary>
    Above,

    /// <summary>Centered on the point, as in a cell or a stacked part.</summary>
    Center,

    /// <summary>To the right of the point, as at a horizontal bar or a median.</summary>
    Right,
}

/// <summary>A value label of a series, in the units of its axes.</summary>
/// <param name="X">The place on the X axis.</param>
/// <param name="Y">The place on the Y axis.</param>
/// <param name="Value">The value written.</param>
/// <param name="Place">Where the label sits at the place.</param>
public readonly record struct ValueLabel(double X, double Y, double Value, ValueLabelPlace Place);
