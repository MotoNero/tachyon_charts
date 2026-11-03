namespace Tachyon.Charts.Core.Numerics;

/// <summary>
/// A nonlinear scale of an axis: values are mapped to a space in which the axis is linear.
/// </summary>
public abstract class AxisScaling
{
    /// <summary>Maps a value into the linear space.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The mapped value, NaN if the value has no place on the axis.</returns>
    public abstract double Forward(double value);

    /// <summary>Maps a position of the linear space back to a value.</summary>
    /// <param name="value">The position.</param>
    /// <returns>The value.</returns>
    public abstract double Inverse(double value);
}

/// <summary>
/// A logarithm. Values at or below zero have no logarithm: they vanish, or stick to a floor if there is one.
/// </summary>
/// <param name="logBase">The base, greater than one.</param>
/// <param name="floor">The value that replaces everything below it, or zero to let such values vanish.</param>
public sealed class LogScaling(double logBase, double floor = 0) : AxisScaling
{
    private readonly double _logOfBase = Math.Log(logBase);

    /// <inheritdoc />
    public override double Forward(double value)
    {
        if (floor > 0 && !(value >= floor))
        {
            value = double.IsNaN(value) ? value : floor;
        }

        return value > 0 ? Math.Log(value) / _logOfBase : double.NaN;
    }

    /// <inheritdoc />
    public override double Inverse(double value) => Math.Exp(value * _logOfBase);
}

/// <summary>
/// A logarithm for values of both signs, linear around zero, so that zero and negative values keep a place.
/// </summary>
/// <param name="logBase">The base, greater than one.</param>
/// <param name="linearThreshold">The value up to which the scale stays linear, positive.</param>
public sealed class SymLogScaling(double logBase, double linearThreshold) : AxisScaling
{
    private readonly double _logOfBase = Math.Log(logBase);

    /// <inheritdoc />
    public override double Forward(double value) =>
        Math.Sign(value) * Math.Log(1 + (Math.Abs(value) / linearThreshold)) / _logOfBase;

    /// <inheritdoc />
    public override double Inverse(double value) =>
        Math.Sign(value) * linearThreshold * (Math.Exp(Math.Abs(value) * _logOfBase) - 1);
}
