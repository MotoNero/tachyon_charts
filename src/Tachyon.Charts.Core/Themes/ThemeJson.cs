using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tachyon.Charts.Core.Themes;

/// <summary>
/// Source-generated JSON serialization for themes.
/// </summary>
public static class ThemeJson
{
    /// <summary>
    /// Serializes a theme.
    /// </summary>
    /// <param name="theme">The theme.</param>
    /// <returns>JSON text.</returns>
    public static string Serialize(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return JsonSerializer.Serialize(theme, ThemeJsonContext.Default.Theme);
    }

    /// <summary>
    /// Deserializes a theme.
    /// </summary>
    /// <param name="json">JSON text.</param>
    /// <returns>The theme.</returns>
    /// <exception cref="JsonException">The document is invalid or null, or names a property that themes do not have; the message gives the path.</exception>
    public static Theme Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, ThemeJsonContext.Default.Theme)
            ?? throw new JsonException("A theme document cannot be null.");
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(Theme))]
internal sealed partial class ThemeJsonContext : JsonSerializerContext;
