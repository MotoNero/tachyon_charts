using System.Globalization;
using System.Reflection;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Describes a series type once: which parameter sets which property. From that come both the step of the preview
/// and the C# of the generated code, so the two cannot drift apart.
/// </summary>
internal sealed class SeriesRecipe
{
    private readonly (ParameterInfo Info, PropertyInfo Property)[] _bound;

    /// <summary>Initializes a recipe.</summary>
    /// <param name="series">The type of the series.</param>
    /// <param name="parameters">The parameters of the type.</param>
    /// <param name="properties">Which parameter sets which property of the series; parameters not listed are read by the constructor.</param>
    public SeriesRecipe(Type series, IReadOnlyList<ParameterInfo> parameters, params (string Parameter, string Property)[] properties)
    {
        Parameters = parameters;
        _bound = [.. properties.Select(pair => (
            parameters.First(parameter => parameter.Name == pair.Parameter),
            series.GetProperty(pair.Property) ?? throw new ArgumentException($"{series.Name} has no property {pair.Property}.", nameof(properties))))];
        Usings = [.. _bound.Select(bound => bound.Property.PropertyType).Where(type => type.IsEnum).Select(type => type.Namespace!).Append(series.Namespace!).Distinct()];
    }

    /// <summary>Gets the parameters.</summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; }

    /// <summary>Gets the namespaces of the series and of the enums its properties take.</summary>
    public IReadOnlyList<string> Usings { get; }

    /// <summary>Makes a type whose series is added with <c>AddSeries</c>.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="name">The name shown.</param>
    /// <param name="shape">The form of the data.</param>
    /// <param name="sample">The sample data by default.</param>
    /// <param name="create">Creates the series from the parameters and the data.</param>
    /// <param name="constructor">Writes the same as C#, from the parameters and the name of the data.</param>
    /// <param name="usings">Further namespaces the constructor needs.</param>
    /// <returns>The type.</returns>
    public ChartTypeInfo Type(string id, string name, DataShape shape, string sample, Func<SpecValues, object, AxisBase, AxisBase, ChartSeries> create, Func<SpecValues, string, string> constructor, params string[] usings) => new(
        id,
        name,
        ChartCategory.TwoD,
        shape,
        sample,
        Parameters,
        (chart, series, data, axes) => chart.AddSeries(
            (x, y) =>
            {
                var created = create(series.Parameters, data, x, y);
                Set(created, series);
                return created;
            },
            axes.X,
            axes.Y),
        (series, data, axes) => $".AddSeries({Factory(constructor(series.Parameters, data), series)}, {CodeText.String(axes.X)}, {CodeText.String(axes.Y)})")
    {
        Usings = [.. Usings, .. usings],
    };

    /// <summary>Sets the name and the properties of a series.</summary>
    /// <param name="created">The series.</param>
    /// <param name="series">Its description.</param>
    public void Set(ChartSeries created, SeriesSpec series)
    {
        created.Name = series.Name;
        foreach (var (info, property) in _bound)
        {
            if (Value(info, property.PropertyType, series.Parameters) is { } value)
            {
                property.SetValue(created, value);
            }
        }
    }

    /// <summary>Writes the same as <see cref="Set"/>: one <c>Property = value</c> per property, the name first.</summary>
    /// <param name="series">The description.</param>
    /// <returns>The assignments.</returns>
    public IEnumerable<string> Assignments(SeriesSpec series)
    {
        yield return "Name = " + CodeText.String(series.Name);
        foreach (var (info, property) in _bound)
        {
            if (Code(info, property.PropertyType, series.Parameters) is { } code)
            {
                yield return property.Name + " = " + code;
            }
        }
    }

    // A constructor takes an object initializer; a factory method gets its properties set one by one.
    private string Factory(string create, SeriesSpec series) => create.StartsWith("new ", StringComparison.Ordinal)
        ? $"(x, y) => {create} {{ {string.Join(", ", Assignments(series))} }}"
        : $"(x, y) => {{ var s = {create}; {string.Concat(Assignments(series).Select(assignment => "s." + assignment + "; "))}return s; }}";

    // An empty color keeps the color of the theme, so the property is left alone.
    private static object? Value(ParameterInfo info, Type type, SpecValues values)
    {
        if (type == typeof(Rgba8) || type == typeof(SeriesFill))
        {
            return !SpecColor.TryParse(values.GetText(info.Name, (string)info.Default), out var color) ? null
                : type == typeof(Rgba8) ? color : new SolidFill(color);
        }

        return type switch
        {
            _ when type == typeof(double) => values.GetNumber(info.Name, (double)info.Default),
            _ when type == typeof(int) => (int)values.GetNumber(info.Name, (double)info.Default),
            _ when type == typeof(bool) => values.GetFlag(info.Name, (bool)info.Default),
            { IsEnum: true } => Enum.Parse(type, values.GetText(info.Name, (string)info.Default)),
            _ => throw new NotSupportedException($"Properties of type {type.Name} are not written yet."),
        };
    }

    private static string? Code(ParameterInfo info, Type type, SpecValues values) => Value(info, type, values) switch
    {
        null => null,
        Rgba8 color => CodeText.Color(color),
        SolidFill fill => $"new SolidFill({CodeText.Color(fill.Color)})",
        double number => CodeText.Number(number),
        int number => number.ToString(CultureInfo.InvariantCulture),
        bool flag => CodeText.Flag(flag),
        Enum choice => $"{type.Name}.{choice}",
        var other => throw new NotSupportedException($"Values of type {other.GetType().Name} are not written yet."),
    };
}
