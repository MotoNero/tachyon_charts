using System.Globalization;

namespace Tachyon.Charts.Core.Axes;

/// <summary>
/// How a numeric axis writes its labels. All ticks of an axis share one precision and one SI prefix,
/// so that they read as one scale.
/// </summary>
public sealed record NumericLabelFormat
{
    private const int ScientificExponent = 15;
    private const int SmallestPrefixExponent = -24;

    private static readonly string[] _siPrefixes =
        ["y", "z", "a", "f", "p", "n", "µ", "m", string.Empty, "k", "M", "G", "T", "P", "E", "Z", "Y"];

    /// <summary>
    /// Gets the culture of the numbers, or <see langword="null"/> for the language of the charts, <see cref="Instruments.ChartText.Language"/>.
    /// </summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// Gets a value indicating whether large and small numbers use SI prefixes such as k and µ.
    /// </summary>
    public bool UseSiPrefixes { get; init; }

    /// <summary>
    /// Gets the unit behind the number, such as V.
    /// </summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// Writes the labels of ticks.
    /// </summary>
    /// <param name="values">The tick values.</param>
    /// <param name="step">The distance of the ticks, positive.</param>
    /// <param name="labels">Receives the labels, as long as <paramref name="values"/>.</param>
    internal void FormatTicks(ReadOnlySpan<double> values, double step, Span<string> labels)
    {
        var largest = 0.0;
        foreach (var value in values)
        {
            largest = Math.Max(largest, Math.Abs(value));
        }

        var culture = Culture ?? Instruments.ChartText.Culture;
        var magnitude = largest > 0 ? (int)Math.Floor(Math.Log10(largest)) : 0;
        var stepExponent = (int)Math.Floor(Math.Log10(step) + 1e-9);

        if (UseSiPrefixes && TryFindPrefix(magnitude, out var prefixExponent, out var prefix))
        {
            var scale = Math.Pow(10, prefixExponent);
            var decimals = Math.Max(0, prefixExponent - stepExponent);
            var format = "F" + decimals.ToString(CultureInfo.InvariantCulture);
            for (var i = 0; i < values.Length; i++)
            {
                labels[i] = Decorate(WithoutNegativeZero(values[i] / scale, decimals).ToString(format, culture), prefix);
            }

            return;
        }

        if (magnitude >= ScientificExponent || stepExponent <= -ScientificExponent)
        {
            FormatScientific(values, Math.Max(0, magnitude - stepExponent), culture, labels);
            return;
        }

        var fixedDecimals = Math.Max(0, -stepExponent);
        var fixedFormat = "F" + fixedDecimals.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < values.Length; i++)
        {
            labels[i] = Decorate(WithoutNegativeZero(values[i], fixedDecimals).ToString(fixedFormat, culture), string.Empty);
        }
    }

    private void FormatScientific(ReadOnlySpan<double> values, int digits, CultureInfo culture, Span<string> labels)
    {
        var format = digits == 0 ? "0E+0" : "0." + new string('0', digits) + "E+0";
        for (var i = 0; i < values.Length; i++)
        {
            labels[i] = Decorate(values[i] == 0 ? "0" : values[i].ToString(format, culture), string.Empty);
        }
    }

    private static bool TryFindPrefix(int magnitude, out int exponent, out string prefix)
    {
        exponent = (int)Math.Floor(magnitude / 3.0) * 3;
        var index = (exponent - SmallestPrefixExponent) / 3;
        if (index < 0 || index >= _siPrefixes.Length)
        {
            prefix = string.Empty;
            return false;
        }

        prefix = _siPrefixes[index];
        return true;
    }

    // Rounding must not print "-0.0".
    private static double WithoutNegativeZero(double value, int decimals) =>
        Math.Abs(value) < 0.5 * Math.Pow(10, -decimals) ? 0 : value;

    private string Decorate(string number, string prefix)
    {
        if (prefix.Length == 0 && Unit.Length == 0)
        {
            return number;
        }

        return Unit.Length == 0 ? number + prefix : $"{number} {prefix}{Unit}";
    }
}
