using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Core.Themes;

/// <summary>
/// A theme color serialized as <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.
/// </summary>
/// <param name="Value">The render color.</param>
[JsonConverter(typeof(ThemeColorJsonConverter))]
public readonly record struct ThemeColor(Rgba8 Value)
{
    /// <summary>Converts a theme color to a render color.</summary>
    public static implicit operator Rgba8(ThemeColor color) => color.Value;

    /// <summary>Converts a render color to a theme color.</summary>
    public static implicit operator ThemeColor(Rgba8 color) => new(color);

    /// <summary>
    /// Parses a hexadecimal color.
    /// </summary>
    /// <param name="text">The color text.</param>
    /// <returns>The color.</returns>
    /// <exception cref="FormatException">The value is not #RRGGBB or #RRGGBBAA.</exception>
    public static ThemeColor Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is not (7 or 9) || text[0] != '#')
        {
            throw new FormatException("A theme color must be #RRGGBB or #RRGGBBAA.");
        }

        return new ThemeColor(new Rgba8(
            ParseByte(text.AsSpan(1, 2)),
            ParseByte(text.AsSpan(3, 2)),
            ParseByte(text.AsSpan(5, 2)),
            text.Length == 9 ? ParseByte(text.AsSpan(7, 2)) : byte.MaxValue));
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var value = Value;
        return value.A == byte.MaxValue
            ? string.Create(CultureInfo.InvariantCulture, $"#{value.R:X2}{value.G:X2}{value.B:X2}")
            : string.Create(CultureInfo.InvariantCulture, $"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}");
    }

    private static byte ParseByte(ReadOnlySpan<char> text) =>
        byte.Parse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}

/// <summary>
/// A two-color linear gradient serialized as <c>linear(start, end)</c>.
/// </summary>
/// <param name="Start">The top or left color.</param>
/// <param name="End">The bottom or right color.</param>
[JsonConverter(typeof(ThemeGradientJsonConverter))]
public readonly record struct ThemeGradient(ThemeColor Start, ThemeColor End)
{
    /// <summary>
    /// Parses a linear gradient or a single color.
    /// </summary>
    /// <param name="text">The gradient text.</param>
    /// <returns>The gradient.</returns>
    /// <exception cref="FormatException">The syntax is invalid.</exception>
    public static ThemeGradient Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.StartsWith('#'))
        {
            var color = ThemeColor.Parse(text);
            return new ThemeGradient(color, color);
        }

        const string prefix = "linear(";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || text[^1] != ')')
        {
            throw new FormatException("A plot area must be a color or linear(start, end).");
        }

        var content = text.AsSpan(prefix.Length, text.Length - prefix.Length - 1);
        var comma = content.IndexOf(',');
        if (comma < 0)
        {
            throw new FormatException("A linear gradient needs two colors.");
        }

        return new ThemeGradient(
            ThemeColor.Parse(content[..comma].Trim().ToString()),
            ThemeColor.Parse(content[(comma + 1)..].Trim().ToString()));
    }

    /// <inheritdoc />
    public override string ToString() => $"linear({Start}, {End})";
}

internal sealed class ThemeColorJsonConverter : JsonConverter<ThemeColor>
{
    public override ThemeColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ThemeColor.Parse(reader.GetString() ?? throw new JsonException("A color cannot be null."));

    public override void Write(Utf8JsonWriter writer, ThemeColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

internal sealed class ThemeGradientJsonConverter : JsonConverter<ThemeGradient>
{
    public override ThemeGradient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ThemeGradient.Parse(reader.GetString() ?? throw new JsonException("A gradient cannot be null."));

    public override void Write(Utf8JsonWriter writer, ThemeGradient value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
