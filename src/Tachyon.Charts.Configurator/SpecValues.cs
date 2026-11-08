using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Named values of parameters: numbers, yes/no and text, written to JSON as such. A missing value takes the default of
/// its <see cref="ParameterInfo"/>, so a saved configuration stays short and keeps working when defaults change.
/// </summary>
[JsonConverter(typeof(SpecValuesConverter))]
public sealed class SpecValues
{
    private readonly SortedDictionary<string, object> _values = new(StringComparer.Ordinal);

    /// <summary>Gets the number of values set.</summary>
    public int Count => _values.Count;

    /// <summary>Gets the names of the values set, in order.</summary>
    public IEnumerable<string> Names => _values.Keys;

    /// <summary>Sets a number.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value, finite.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is not finite.</exception>
    public void Set(string name, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only finite numbers are kept.");
        }

        _values[name] = value;
    }

    /// <summary>Sets a yes/no value.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void Set(string name, bool value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _values[name] = value;
    }

    /// <summary>Sets a text, such as a choice or a color.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void Set(string name, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(value);
        _values[name] = value;
    }

    /// <summary>Removes a value, so that its default applies again.</summary>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when a value was set.</returns>
    public bool Remove(string name) => _values.Remove(name);

    /// <summary>Gets a number, or a default.</summary>
    /// <param name="name">The name.</param>
    /// <param name="fallback">The value when none is set or it is no number.</param>
    /// <returns>The value.</returns>
    public double GetNumber(string name, double fallback) => _values.TryGetValue(name, out var value) && value is double number ? number : fallback;

    /// <summary>Gets a yes/no value, or a default.</summary>
    /// <param name="name">The name.</param>
    /// <param name="fallback">The value when none is set or it is no yes/no value.</param>
    /// <returns>The value.</returns>
    public bool GetFlag(string name, bool fallback) => _values.TryGetValue(name, out var value) && value is bool flag ? flag : fallback;

    /// <summary>Gets a text, or a default.</summary>
    /// <param name="name">The name.</param>
    /// <param name="fallback">The value when none is set or it is no text.</param>
    /// <returns>The value.</returns>
    public string GetText(string name, string fallback) => _values.TryGetValue(name, out var value) && value is string text ? text : fallback;

    /// <summary>Gets a value as it is kept, or <see langword="null"/>.</summary>
    /// <param name="name">The name.</param>
    /// <returns>A <see cref="double"/>, <see cref="bool"/> or <see cref="string"/>.</returns>
    public object? Get(string name) => _values.GetValueOrDefault(name);

    /// <summary>Gets a value indicating whether two sets hold the same values.</summary>
    /// <param name="other">The other set.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    public bool Matches(SpecValues other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return _values.Count == other._values.Count && _values.All(pair => other._values.TryGetValue(pair.Key, out var value) && Equals(pair.Value, value));
    }

    /// <inheritdoc />
    public override string ToString() => string.Join(", ", _values.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key}={pair.Value}")));

    private sealed class SpecValuesConverter : JsonConverter<SpecValues>
    {
        public override SpecValues Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Parameter values are an object of names and values.");
            }

            var values = new SpecValues();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var name = reader.GetString() ?? throw new JsonException("A parameter has no name.");
                reader.Read();
                switch (reader.TokenType)
                {
                    case JsonTokenType.Number:
                        values.Set(name, reader.GetDouble());
                        break;
                    case JsonTokenType.True:
                    case JsonTokenType.False:
                        values.Set(name, reader.GetBoolean());
                        break;
                    case JsonTokenType.String:
                        values.Set(name, reader.GetString()!);
                        break;
                    default:
                        throw new JsonException($"The parameter {name} is no number, yes/no value or text.");
                }
            }

            return values;
        }

        public override void Write(Utf8JsonWriter writer, SpecValues value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var (name, item) in value._values)
            {
                switch (item)
                {
                    case double number:
                        writer.WriteNumber(name, number);
                        break;
                    case bool flag:
                        writer.WriteBoolean(name, flag);
                        break;
                    default:
                        writer.WriteString(name, (string)item);
                        break;
                }
            }

            writer.WriteEndObject();
        }
    }
}
