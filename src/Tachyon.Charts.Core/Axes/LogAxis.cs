using System.Globalization;
using Tachyon.Charts.Core.Numerics;

namespace Tachyon.Charts.Core.Axes;

/// <summary>What a logarithmic axis does with values at or below zero.</summary>
public enum NonPositiveValues
{
    /// <summary>They are left out: a line has a gap there.</summary>
    Hide,

    /// <summary>They stick to the smallest value of the axis scale.</summary>
    Clamp,

    /// <summary>The axis is linear around zero and mirrored below it, so that they keep their place.</summary>
    SymLog,
}

/// <summary>
/// An axis with a logarithmic scale, with major ticks at powers of the base.
/// </summary>
/// <param name="id">The identifier that series refer to.</param>
/// <param name="position">The side of the plot area.</param>
public sealed class LogAxis(string id, AxisPosition position) : AxisBase(id, position)
{
    private const double MinimumTickSpacing = 40;
    private const double MinimumMinorSpacing = 4;
    private const double DefaultFloor = 1e-3;
    private const double RelativeFloor = 1e-6;
    private const int MaximumTicks = 512;

    // The logarithm of an exact power misses the integer by a rounding error: log(1000) / log(10) < 3.
    private const double PowerTolerance = 1e-9;

    private double _base = 10;
    private double _floor = DefaultFloor;
    private double _linearThreshold = 1;
    private NonPositiveValues _nonPositive;
    private AxisScaling? _scaling;

    /// <summary>Gets or sets the base, greater than one, for example 2, <see cref="Math.E"/> or 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than one and finite.</exception>
    public double Base
    {
        get => _base;
        set
        {
            if (!double.IsFinite(value) || value <= 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The base must be greater than one.");
            }

            _base = value;
            Reset();
        }
    }

    /// <summary>Gets or sets what happens to values at or below zero.</summary>
    public NonPositiveValues NonPositive
    {
        get => _nonPositive;
        set
        {
            _nonPositive = value;
            Reset();
        }
    }

    /// <summary>Gets or sets the smallest value of <see cref="NonPositiveValues.Clamp"/>, positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double Floor
    {
        get => _floor;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The floor must be positive.");
            }

            _floor = value;
            Reset();
        }
    }

    /// <summary>Gets or sets the value up to which <see cref="NonPositiveValues.SymLog"/> stays linear, positive.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive and finite.</exception>
    public double LinearThreshold
    {
        get => _linearThreshold;
        set
        {
            if (!double.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The threshold must be positive.");
            }

            _linearThreshold = value;
            Reset();
        }
    }

    /// <inheritdoc />
    public override AxisScaling Scaling => _scaling ??= _nonPositive switch
    {
        NonPositiveValues.SymLog => new SymLogScaling(_base, _linearThreshold),
        NonPositiveValues.Clamp => new LogScaling(_base, _floor),
        _ => new LogScaling(_base),
    };

    /// <inheritdoc />
    public override TickSet GenerateTicks(double length, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        if (!double.IsFinite(length) || length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "The length must not be negative or infinite.");
        }

        var range = VisibleRange;
        var scaling = Scaling;
        var low = scaling.Forward(range.Min);
        var high = scaling.Forward(range.Max);
        if (length == 0 || !(high > low))
        {
            return TickSet.Empty;
        }

        var decades = high - low;
        var pixelsPerDecade = length / decades;
        var spacing = Math.Max(MinimumTickSpacing, measurer.LineHeight * 1.5);
        var step = Math.Max(1, (int)Math.Ceiling(spacing / pixelsPerDecade));

        var values = CreateValues(range, step, measurer.LineHeight * 1.2 / pixelsPerDecade);

        // Text wider than the distance needs a coarser step on a horizontal axis.
        var labels = values.Select(Format).ToArray();
        while (IsHorizontal && labels.Length > 1 && labels.Max(label => measurer.MeasureWidth(label)) + 8 > step * pixelsPerDecade)
        {
            step++;
            values = CreateValues(range, step, 0);
            labels = [.. values.Select(Format)];
        }

        var major = new AxisTick[values.Length];
        for (var i = 0; i < major.Length; i++)
        {
            major[i] = new AxisTick(values[i], labels[i]);
        }

        return new TickSet(major, CreateMinorValues(range, step, pixelsPerDecade), step);
    }

    /// <inheritdoc />
    protected override DoubleRange NormalizeRange(DoubleRange range)
    {
        if (_nonPositive == NonPositiveValues.SymLog)
        {
            return range;
        }

        var limit = _nonPositive == NonPositiveValues.Clamp ? _floor : double.Epsilon;
        var max = Math.Max(range.Max, limit);
        var min = range.Min > 0 ? Math.Max(range.Min, limit) : Math.Max(max * RelativeFloor, limit);
        return new DoubleRange(Math.Min(min, max), max);
    }

    private static string Format(double value)
    {
        var magnitude = Math.Abs(value);
        return magnitude is >= 1e5 or (> 0 and < 1e-3)
            ? value.ToString("0.###E+0", CultureInfo.InvariantCulture)
            : value.ToString("G4", CultureInfo.InvariantCulture);
    }

    private void Reset()
    {
        _scaling = null;
        OnChanged();
    }

    private double[] CreateValues(DoubleRange range, int step, double zeroClearance)
    {
        if (_nonPositive == NonPositiveValues.SymLog)
        {
            return CreateSymLogValues(range, step, zeroClearance);
        }

        var scaling = Scaling;
        var first = (int)Math.Ceiling((scaling.Forward(range.Min) / step) - PowerTolerance);
        var last = (int)Math.Floor((scaling.Forward(range.Max) / step) + PowerTolerance);
        var values = new List<double>();
        for (var i = first; i <= last && values.Count < MaximumTicks; i++)
        {
            var power = Math.Pow(_base, i * step);
            if (range.Contains(power))
            {
                values.Add(power);
            }
        }

        return [.. values];
    }

    // The ticks next to zero are left out when their labels would touch the one of zero.
    private double[] CreateSymLogValues(DoubleRange range, int step, double zeroClearance)
    {
        var scaling = Scaling;
        var factor = Math.Pow(_base, step);
        var values = new List<double>();
        if (range.Contains(0))
        {
            values.Add(0);
        }

        for (var power = _linearThreshold; power <= Math.Max(Math.Abs(range.Min), Math.Abs(range.Max)) && values.Count < MaximumTicks; power *= factor)
        {
            if (scaling.Forward(power) < zeroClearance)
            {
                continue;
            }

            if (range.Contains(power))
            {
                values.Add(power);
            }

            if (range.Contains(-power))
            {
                values.Insert(0, -power);
            }
        }

        return [.. values];
    }

    // Multiples of each power, for integer bases and only while single decades are drawn.
    private double[] CreateMinorValues(DoubleRange range, int step, double pixelsPerDecade)
    {
        var steps = (int)_base;
        if (step != 1 || steps < 3 || steps != _base || _nonPositive == NonPositiveValues.SymLog)
        {
            return [];
        }

        var firstGap = (Math.Log(2) / Math.Log(_base)) * pixelsPerDecade;
        var lastGap = (Math.Log(_base / (_base - 1)) / Math.Log(_base)) * pixelsPerDecade;
        if (Math.Min(firstGap, lastGap) < MinimumMinorSpacing)
        {
            return [];
        }

        var scaling = Scaling;
        var minor = new List<double>();
        var last = (int)Math.Ceiling(scaling.Forward(range.Max) - PowerTolerance);
        for (var i = (int)Math.Floor(scaling.Forward(range.Min) + PowerTolerance); i <= last && minor.Count < MaximumTicks; i++)
        {
            var power = Math.Pow(_base, i);
            for (var factor = 2; factor < steps; factor++)
            {
                var value = power * factor;
                if (range.Contains(value))
                {
                    minor.Add(value);
                }
            }
        }

        return [.. minor];
    }
}
