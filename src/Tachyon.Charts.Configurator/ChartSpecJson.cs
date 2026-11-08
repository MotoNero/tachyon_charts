using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Saves and reads <see cref="ChartSpec"/> as JSON. Every file carries its version; older versions are brought up to
/// date step by step when read, newer ones are refused. Medical data never get into the file: their paths are dropped
/// on writing and on reading.
/// </summary>
public static class ChartSpecJson
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Gets the steps from one version to the next: the step at index n turns a file of version n + 1 into one of
    /// version n + 2. Version 1 is the first, so there are none yet; tests add their own through <see cref="Read(string, IReadOnlyList{Action{JsonObject}})"/>.
    /// </summary>
    internal static IReadOnlyList<Action<JsonObject>> Upgrades { get; } = [];

    /// <summary>Writes a description.</summary>
    /// <param name="spec">The description.</param>
    /// <returns>The JSON text.</returns>
    public static string Write(ChartSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.Version = ChartSpec.CurrentVersion;
        return JsonSerializer.Serialize(spec, _options);
    }

    /// <summary>Reads a description, bringing an older version up to date.</summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The description.</returns>
    /// <exception cref="FormatException">The text is no description, or it comes from a newer version.</exception>
    public static ChartSpec Read(string json) => Read(json, Upgrades);

    internal static ChartSpec Read(string json, IReadOnlyList<Action<JsonObject>> upgrades)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonObject root;
        try
        {
            root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("A chart description is a JSON object.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The chart description is no valid JSON.", exception);
        }

        var version = root["version"]?.GetValue<int>() ?? 1;
        var current = upgrades.Count + 1;
        if (version < 1 || version > current)
        {
            throw new FormatException($"The chart description has version {version}; this configurator reads versions 1 to {current}.");
        }

        for (var step = version - 1; step < upgrades.Count; step++)
        {
            upgrades[step](root);
        }

        root["version"] = ChartSpec.CurrentVersion;
        ChartSpec spec;
        try
        {
            spec = root.Deserialize<ChartSpec>(_options) ?? throw new FormatException("The chart description is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("The chart description does not fit the format: " + exception.Message, exception);
        }

        // A path of medical data in a file written by hand or by another program is dropped, not kept.
        foreach (var series in spec.Series.Where(series => series.Data.Kind == DataKind.MedicalFile))
        {
            series.Data.Path = null;
        }

        return spec;
    }
}
