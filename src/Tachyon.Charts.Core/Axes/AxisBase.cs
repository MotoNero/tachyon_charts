using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// The side of the plot area an axis sits on.
/// </summary>
public enum AxisPosition
{
    /// <summary>
    /// Left of the plot area, vertical.
    /// </summary>
    Left,

    /// <summary>
    /// Right of the plot area, vertical.
    /// </summary>
    Right,

    /// <summary>
    /// Above the plot area, horizontal.
    /// </summary>
    Top,

    /// <summary>
    /// Below the plot area, horizontal.
    /// </summary>
    Bottom,
}

/// <summary>
/// Measures labels. The text system of the renderer implements it.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>
    /// Gets the height of a line in device-independent units.
    /// </summary>
    double LineHeight { get; }

    /// <summary>
    /// Measures the width of a text in device-independent units.
    /// </summary>
    double MeasureWidth(string text);
}

/// <summary>
/// A labeled tick.
/// </summary>
/// <param name="Value">The position in data units.</param>
/// <param name="Label">The text.</param>
public readonly record struct AxisTick(double Value, string Label);

/// <summary>
/// The ticks of an axis for a visible range.
/// </summary>
/// <param name="Major">The labeled ticks in ascending order.</param>
/// <param name="Minor">The positions of the unlabeled ticks between them, in ascending order.</param>
/// <param name="Step">The distance of the major ticks, 0 if there is at most one.</param>
public sealed record TickSet(IReadOnlyList<AxisTick> Major, IReadOnlyList<double> Minor, double Step)
{
    /// <summary>
    /// Gets a set without ticks.
    /// </summary>
    public static TickSet Empty { get; } = new([], [], 0);
}

/// <summary>
/// What all axes share: placement, visible range and its limits.
/// </summary>
/// <remarks>
/// An axis belongs to the thread that owns the chart. The render thread works on copies of its state.
/// </remarks>
public abstract class AxisBase
{
    private const double MinimumRelativeLength = 1e-13;

    private const double SingleValueMargin = 0.1;

    private DoubleRange _visibleRange = new(0, 1);
    private DoubleRange? _limits;
    private bool _isInverted;
    private bool _isAutoRange = true;
    private string? _title;

    /// <summary>
    /// Initializes a new instance of the <see cref="AxisBase"/> class.
    /// </summary>
    /// <param name="id">The identifier that series refer to.</param>
    /// <param name="position">The side of the plot area.</param>
    /// <exception cref="ArgumentException">The identifier is empty.</exception>
    protected AxisBase(string id, AxisPosition position)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        Id = id;
        Position = position;
    }

    /// <summary>
    /// Gets the identifier that series refer to.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the side of the plot area.
    /// </summary>
    public AxisPosition Position { get; }

    /// <summary>
    /// Gets or sets the title beside the labels, or <see langword="null"/> for none.
    /// </summary>
    public string? Title
    {
        get => _title;
        set
        {
            if (_title != value)
            {
                _title = value;
                Version++;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the axis runs horizontally.
    /// </summary>
    public bool IsHorizontal => Position is AxisPosition.Top or AxisPosition.Bottom;

    /// <summary>
    /// Gets a number that grows with every change, to detect outdated layouts.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Gets or sets the visible range. Setting it ends the automatic range and keeps it inside the
    /// <see cref="Limits"/>: a range that reaches out moves back, a longer one is cut.
    /// </summary>
    /// <exception cref="ArgumentException">The range is empty.</exception>
    public DoubleRange VisibleRange
    {
        get => _visibleRange;
        set
        {
            IsAutoRange = false;
            ApplyRange(value);
        }
    }

    /// <summary>
    /// Gets or sets the bounds of zooming and panning, or <see langword="null"/> for none.
    /// </summary>
    /// <exception cref="ArgumentException">The limits are empty.</exception>
    public DoubleRange? Limits
    {
        get => _limits;
        set
        {
            if (value is { IsEmpty: true })
            {
                throw new ArgumentException("The limits must not be empty.", nameof(value));
            }

            _limits = value;
            Version++;
            ApplyRange(_visibleRange);
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the range follows the data through <see cref="FitTo"/>.
    /// </summary>
    public bool IsAutoRange
    {
        get => _isAutoRange;
        set
        {
            if (_isAutoRange != value)
            {
                _isAutoRange = value;
                Version++;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the axis runs against its usual direction.
    /// </summary>
    public bool IsInverted
    {
        get => _isInverted;
        set
        {
            if (_isInverted != value)
            {
                _isInverted = value;
                Version++;
            }
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the axis takes no room and draws nothing, for example
    /// in polar charts, which only use its range.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Gets or sets the rotation of the labels in degrees. Only axes with free text support it.
    /// </summary>
    /// <exception cref="NotSupportedException">This axis has no rotation.</exception>
    public virtual double LabelRotation
    {
        get => 0;
        set => throw new NotSupportedException("This axis has no rotated labels.");
    }

    /// <summary>
    /// Finds the height the labels of a horizontal axis take up.
    /// </summary>
    /// <param name="measurer">The measurement of text.</param>
    /// <returns>The height.</returns>
    public virtual double MeasureLabelHeight(ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        return measurer.LineHeight;
    }

    /// <summary>
    /// Gets the width of a band between the plot and the ticks, for example of a color bar.
    /// </summary>
    public virtual double BandThickness => 0;

    /// <summary>
    /// Gets the nonlinear scale of the axis, or <see langword="null"/> for a linear one.
    /// </summary>
    public virtual AxisScaling? Scaling => null;

    /// <summary>
    /// Applies the data range while <see cref="IsAutoRange"/> is set. Empty data leaves the range alone.
    /// </summary>
    /// <param name="data">The range of the data.</param>
    /// <param name="padding">The share of the length to add on each side, not negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">The padding is negative or not finite.</exception>
    public void FitTo(DoubleRange data, double padding = 0.05)
    {
        if (!double.IsFinite(padding) || padding < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(padding), padding, "The padding must not be negative.");
        }

        if (IsAutoRange && !data.IsEmpty)
        {
            ApplyRange(PadScaled(data, padding));
        }
    }

    /// <summary>
    /// Creates the mapping to pixels. Vertical axes grow upwards, against the pixel direction.
    /// </summary>
    /// <param name="start">The first pixel of the axis, the left or top end.</param>
    /// <param name="length">The length in pixels, not negative.</param>
    /// <returns>The transform.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
    public virtual LinearAxisTransform CreateTransform(double start, double length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        var end = start + length;
        return IsHorizontal != IsInverted
            ? new LinearAxisTransform(_visibleRange, start, end, Scaling)
            : new LinearAxisTransform(_visibleRange, end, start, Scaling);
    }

    /// <summary>
    /// Scales the visible range around a value, which keeps its place on the axis. Limits still apply.
    /// A range too small for <see cref="double"/> or too large to be finite stays as it is.
    /// </summary>
    /// <param name="anchor">The value that stays in place.</param>
    /// <param name="factor">The new length relative to the old one.</param>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not positive and finite.</exception>
    public void ZoomAround(double anchor, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "The factor must be positive and finite.");
        }

        var scaling = Scaling;
        var center = scaling?.Forward(anchor) ?? anchor;
        var min = scaling?.Forward(_visibleRange.Min) ?? _visibleRange.Min;
        var max = scaling?.Forward(_visibleRange.Max) ?? _visibleRange.Max;
        var first = center - ((center - min) * factor);
        var second = center + ((max - center) * factor);
        ShowRange(scaling?.Inverse(first) ?? first, scaling?.Inverse(second) ?? second);
    }

    /// <summary>
    /// Moves the visible range without changing its length. Limits still apply.
    /// </summary>
    /// <param name="delta">The distance in axis units.</param>
    public void PanBy(double delta) => ShowRange(_visibleRange.Min + delta, _visibleRange.Max + delta);

    /// <summary>
    /// Shows a range given by its two ends in any order, unless it cannot be told apart in
    /// <see cref="double"/> or is not finite.
    /// </summary>
    /// <param name="first">One end.</param>
    /// <param name="second">The other end.</param>
    public void ShowRange(double first, double second)
    {
        var range = DoubleRange.FromUnordered(first, second);
        var magnitude = Math.Max(Math.Abs(range.Min), Math.Abs(range.Max));
        if (double.IsFinite(range.Min) && double.IsFinite(range.Max) && double.IsFinite(range.Length) && range.Length > magnitude * MinimumRelativeLength)
        {
            VisibleRange = range;
        }
    }

    /// <summary>
    /// Formats a value for a cursor, with more precision than a tick label needs.
    /// </summary>
    /// <param name="value">The value in units of the axis.</param>
    /// <returns>The text.</returns>
    public virtual string FormatCursorValue(double value) => value.ToString("G6", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Brings a range into the domain of the axis.
    /// </summary>
    /// <param name="range">The range, not empty.</param>
    /// <returns>The valid range.</returns>
    protected virtual DoubleRange NormalizeRange(DoubleRange range) => range;

    /// <summary>
    /// Marks the axis as changed, for state of derived axes.
    /// </summary>
    protected void OnChanged() => Version++;

    /// <summary>
    /// Finds the ticks for the visible range.
    /// </summary>
    /// <param name="length">The length of the axis in device-independent units.</param>
    /// <param name="measurer">The measurement of labels.</param>
    /// <returns>The ticks, without overlapping labels.</returns>
    public abstract TickSet GenerateTicks(double length, ITextMeasurer measurer);

    private static DoubleRange Pad(DoubleRange data, double padding)
    {
        // A single value has no length to scale, it gets a margin of its own size.
        var ownSize = data.Min != 0 ? Math.Abs(data.Min) * SingleValueMargin : 1;
        var margin = data.Length > 0 ? data.Length * padding : ownSize;

        var min = data.Min - margin;
        var max = data.Max + margin;
        return double.IsFinite(min) && double.IsFinite(max) ? new DoubleRange(min, max) : data;
    }

    // On a nonlinear axis, padding is the same distance on the screen at both ends.
    private DoubleRange PadScaled(DoubleRange data, double padding)
    {
        if (Scaling is not { } scaling)
        {
            return Pad(data, padding);
        }

        var valid = NormalizeRange(data);
        var padded = Pad(new DoubleRange(scaling.Forward(valid.Min), scaling.Forward(valid.Max)), padding);
        return new DoubleRange(scaling.Inverse(padded.Min), scaling.Inverse(padded.Max));
    }

    private void ApplyRange(DoubleRange range)
    {
        if (range.IsEmpty)
        {
            throw new ArgumentException("The visible range must not be empty.", nameof(range));
        }

        range = NormalizeRange(range);
        if (_limits is { } limits)
        {
            range = Constrain(range, limits);
        }

        if (range != _visibleRange)
        {
            _visibleRange = range;
            Version++;
        }
    }

    private static DoubleRange Constrain(DoubleRange range, DoubleRange limits)
    {
        if (range.Length >= limits.Length)
        {
            return limits;
        }

        if (range.Min < limits.Min)
        {
            return new DoubleRange(limits.Min, limits.Min + range.Length);
        }

        return range.Max > limits.Max
            ? new DoubleRange(limits.Max - range.Length, limits.Max)
            : range;
    }
}
