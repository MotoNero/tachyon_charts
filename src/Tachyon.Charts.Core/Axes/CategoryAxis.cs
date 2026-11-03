namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// An axis with named places at 0, 1, 2 and so on. Without room for all names it shows every n-th one.
/// </summary>
public sealed class CategoryAxis : AxisBase
{
    private const double LabelGap = 8;
    private const double MinimumAngle = 1;

    private IReadOnlyList<string> _categories = [];
    private double _labelRotation;

    /// <summary>Initializes a new instance of the <see cref="CategoryAxis"/> class.</summary>
    /// <param name="id">The identifier that series refer to.</param>
    /// <param name="position">The side of the plot area.</param>
    /// <param name="categories">The names of the places.</param>
    public CategoryAxis(string id, AxisPosition position, IReadOnlyList<string> categories)
        : base(id, position)
    {
        Categories = categories;
    }

    /// <summary>Gets or sets the names of the places. Setting them shows all places.</summary>
    public IReadOnlyList<string> Categories
    {
        get => _categories;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _categories = value;
            if (value.Count > 0)
            {
                VisibleRange = new Numerics.DoubleRange(-0.5, value.Count - 0.5);
            }

            OnChanged();
        }
    }

    /// <summary>
    /// Gets or sets the rotation of the labels in degrees, up to 90 in both directions. Negative angles
    /// lift the end of the text, and rotated labels end at their tick.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The angle is outside of this range.</exception>
    public override double LabelRotation
    {
        get => _labelRotation;
        set
        {
            if (!(Math.Abs(value) <= 90))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The rotation must be at most 90 degrees.");
            }

            _labelRotation = value;
            OnChanged();
        }
    }

    /// <inheritdoc />
    public override double MeasureLabelHeight(ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        var angle = Math.Abs(_labelRotation) * Math.PI / 180;
        return (Longest(measurer) * Math.Sin(angle)) + (measurer.LineHeight * Math.Cos(angle));
    }

    /// <inheritdoc />
    public override TickSet GenerateTicks(double length, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        var range = VisibleRange;
        if (_categories.Count == 0 || length <= 0)
        {
            return TickSet.Empty;
        }

        var first = Math.Max((int)Math.Ceiling(range.Min), 0);
        var last = Math.Min((int)Math.Floor(range.Max), _categories.Count - 1);
        var perPlace = length / (range.Max - range.Min);
        var step = Math.Max((int)Math.Ceiling(NeededSpace(measurer) / perPlace), 1);
        var ticks = new List<AxisTick>();

        // Multiples of the step, so that a label keeps its place while the view moves.
        for (var i = ((first + step - 1) / step) * step; i <= last; i += step)
        {
            ticks.Add(new AxisTick(i, _categories[i]));
        }

        return new TickSet(ticks, [], step);
    }

    private double Longest(ITextMeasurer measurer) =>
        _categories.Count == 0 ? 0 : _categories.Max(category => measurer.MeasureWidth(category));

    private double NeededSpace(ITextMeasurer measurer)
    {
        if (!IsHorizontal)
        {
            return measurer.LineHeight * 1.2;
        }

        var angle = Math.Abs(_labelRotation);
        return angle < MinimumAngle
            ? Longest(measurer) + LabelGap
            : Math.Min(measurer.LineHeight * 1.2 / Math.Sin(angle * Math.PI / 180), Longest(measurer) + LabelGap);
    }
}
