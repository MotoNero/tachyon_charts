using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Numerics;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// The value scale of a raster: an axis beside the plot with a color bar. It belongs to the axes of a
/// pane, after the Y axis, and does not follow zooming the pane. Over the axis itself, zooming and
/// panning change the color range.
/// </summary>
public sealed class ColorAxis : AxisBase
{
    private const double DefaultBarWidth = 14;
    private const double RelativeFloor = 1e-6;

    private static readonly LogScaling _logarithm = new(10);

    private Colormap _colormap = Colormaps.Viridis;
    private bool _isLogarithmic;
    private Rgba8 _missingColor;
    private Rgba8? _belowColor;
    private Rgba8? _aboveColor;
    private AxisBase _ticks;
    private NumericLabelFormat? _labelFormat;

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorAxis"/> class. It follows the data by default.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="position">The side, left or right.</param>
    /// <exception cref="ArgumentException">The position is at the top or bottom.</exception>
    public ColorAxis(string id, AxisPosition position = AxisPosition.Right)
        : base(id, position)
    {
        if (IsHorizontal)
        {
            throw new ArgumentException("A color axis stands beside the plot.", nameof(position));
        }

        _ticks = new NumericAxis(id, position);
        IsAutoRange = true;
    }

    /// <summary>Gets or sets the format of linear labels.</summary>
    public NumericLabelFormat? LabelFormat
    {
        get => _labelFormat;
        set
        {
            _labelFormat = value;
            if (_ticks is NumericAxis numeric && value is not null)
            {
                numeric.LabelFormat = value;
            }

            OnChanged();
        }
    }

    /// <summary>Gets or sets the colors.</summary>
    public Colormap Colormap
    {
        get => _colormap;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_colormap != value)
            {
                _colormap = value;
                OnChanged();
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether colors follow the logarithm of the values.</summary>
    public bool IsLogarithmic
    {
        get => _isLogarithmic;
        set
        {
            if (_isLogarithmic != value)
            {
                _isLogarithmic = value;
                _ticks = value ? new LogAxis(Id, Position) : new NumericAxis(Id, Position) { LabelFormat = _labelFormat ?? new NumericLabelFormat() };

                // The setter of the range ends following the data, which this change is not meant to do.
                var follows = IsAutoRange;
                VisibleRange = NormalizeRange(VisibleRange);
                IsAutoRange = follows;
                OnChanged();
            }
        }
    }

    /// <summary>Gets or sets the color of missing values (NaN). The default is transparent.</summary>
    public Rgba8 MissingColor
    {
        get => _missingColor;
        set
        {
            if (_missingColor != value)
            {
                _missingColor = value;
                OnChanged();
            }
        }
    }

    /// <summary>Gets or sets the color of values below the range, or <see langword="null"/> for the first color of the map.</summary>
    public Rgba8? BelowColor
    {
        get => _belowColor;
        set
        {
            if (_belowColor != value)
            {
                _belowColor = value;
                OnChanged();
            }
        }
    }

    /// <summary>Gets or sets the color of values above the range, or <see langword="null"/> for the last color of the map.</summary>
    public Rgba8? AboveColor
    {
        get => _aboveColor;
        set
        {
            if (_aboveColor != value)
            {
                _aboveColor = value;
                OnChanged();
            }
        }
    }

    /// <inheritdoc />
    public override AxisScaling? Scaling => _isLogarithmic ? _logarithm : null;

    /// <inheritdoc />
    public override double BandThickness => DefaultBarWidth;

    /// <summary>Gets or sets the width the layout keeps for the labels.</summary>
    internal double StableLabelWidth { get; set; }

    /// <summary>Gets or sets the length of the axis that width belongs to.</summary>
    internal double StableLabelLength { get; set; }

    /// <summary>
    /// Gets how a renderer maps values to colors.
    /// </summary>
    /// <returns>The style, without a section of the texture.</returns>
    public RasterStyle CreateRasterStyle() => new((float)VisibleRange.Min, (float)VisibleRange.Max)
    {
        IsLogarithmic = _isLogarithmic,
        MissingColor = _missingColor,
        BelowColor = _belowColor,
        AboveColor = _aboveColor,
    };

    /// <inheritdoc />
    public override TickSet GenerateTicks(double length, ITextMeasurer measurer)
    {
        _ticks.VisibleRange = VisibleRange;
        return _ticks.GenerateTicks(length, measurer);
    }

    /// <inheritdoc />
    protected override DoubleRange NormalizeRange(DoubleRange range)
    {
        if (!_isLogarithmic || range.Min > 0)
        {
            return range;
        }

        var max = range.Max > 0 ? range.Max : 1;
        return new DoubleRange(max * RelativeFloor, max);
    }
}
