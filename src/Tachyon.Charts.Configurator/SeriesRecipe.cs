using System.Globalization;
using System.Reflection;
using Tachyon.Charts.Core.Axes;
using Tachyon.Charts.Core.Colors;
using Tachyon.Charts.Core.Pipeline;
using Tachyon.Charts.Core.Series;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>
/// Describes a series type once: which parameter sets which property. From that come both the step of the preview
/// and the C# of the generated code, so the two cannot drift apart. A property may be one of an object the series
/// holds, written as <c>Outer.Inner</c>, such as <c>Material.Opacity</c>; that object is then created anew.
/// </summary>
internal sealed class SeriesRecipe
{
    private readonly (ParameterInfo Info, PropertyInfo Outer, PropertyInfo? Inner)[] _bound;
    private readonly PropertyInfo? _name;

    /// <summary>Initializes a recipe.</summary>
    /// <param name="series">The type of the series.</param>
    /// <param name="parameters">The parameters of the type.</param>
    /// <param name="properties">Which parameter sets which property of the series; parameters not listed are read by the constructor.</param>
    public SeriesRecipe(Type series, IReadOnlyList<ParameterInfo> parameters, params (string Parameter, string Property)[] properties)
    {
        // Every type that can label its values offers the switch, unless it has labels of its own, as a pie has.
        if (series.GetProperty("ShowsValues") is { CanWrite: true } && parameters.All(parameter => parameter.Name is not "values" and not "labels"))
        {
            parameters = [.. parameters, CoreModule.ValuesParameter];
            properties = [.. properties, ("values", "ShowsValues")];
        }

        Parameters = parameters;
        _name = series.GetProperty("Name") is { CanWrite: true, PropertyType: var type } name && type == typeof(string) ? name : null;
        _bound = [.. properties.Select(pair => Bind(series, parameters, pair.Parameter, pair.Property))];
        Usings = [.. _bound.Select(bound => (bound.Inner ?? bound.Outer).PropertyType).Where(type => type.IsEnum).Select(type => type.Namespace!)
            .Concat(_bound.Where(bound => bound.Inner is not null).Select(bound => bound.Outer.PropertyType.Namespace!))
            .Concat(_bound.Any(bound => (bound.Inner ?? bound.Outer).PropertyType == typeof(Colormap)) ? ["Tachyon.Charts.Core.Colors"] : [])
            .Append(series.Namespace!).Distinct()];
    }

    /// <summary>Gets the parameters.</summary>
    public IReadOnlyList<ParameterInfo> Parameters { get; }

    /// <summary>Gets the namespaces of the series and of the types its properties take.</summary>
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

    /// <summary>Sets the name, if the object has one, and the properties of an object.</summary>
    /// <param name="created">The object.</param>
    /// <param name="series">Its description.</param>
    public void Set(object created, SeriesSpec series)
    {
        _name?.SetValue(created, series.Name);
        foreach (var group in _bound.GroupBy(bound => bound.Outer))
        {
            if (group.All(bound => bound.Inner is null))
            {
                foreach (var (info, property, _) in group)
                {
                    if (Value(info, property.PropertyType, series.Parameters) is { } value)
                    {
                        property.SetValue(created, Resolve(value));
                    }
                }

                continue;
            }

            var inner = Activator.CreateInstance(group.Key.PropertyType)!;
            foreach (var (info, _, property) in group)
            {
                if (Value(info, property!.PropertyType, series.Parameters) is { } value)
                {
                    property.SetValue(inner, Resolve(value));
                }
            }

            group.Key.SetValue(created, inner);
        }
    }

    /// <summary>Writes the same as <see cref="Set"/>: one <c>Property = value</c> per property, the name first.</summary>
    /// <param name="series">The description.</param>
    /// <returns>The assignments.</returns>
    public IEnumerable<string> Assignments(SeriesSpec series)
    {
        if (_name is not null)
        {
            yield return "Name = " + CodeText.String(series.Name);
        }

        foreach (var group in _bound.GroupBy(bound => bound.Outer))
        {
            if (group.All(bound => bound.Inner is null))
            {
                foreach (var (info, property, _) in group)
                {
                    if (Code(info, property.PropertyType, series.Parameters) is { } code)
                    {
                        yield return property.Name + " = " + code;
                    }
                }

                continue;
            }

            var inner = group.Select(bound => Code(bound.Info, bound.Inner!.PropertyType, series.Parameters) is { } code ? bound.Inner.Name + " = " + code : null).OfType<string>();
            yield return $"{group.Key.Name} = new {group.Key.PropertyType.Name} {{ {string.Join(", ", inner)} }}";
        }
    }

    /// <summary>Writes the object with its properties: an initializer after a constructor, single assignments after a method.</summary>
    /// <param name="create">The constructor or method call.</param>
    /// <param name="series">The description.</param>
    /// <returns>An expression of the object.</returns>
    public string Expression(string create, SeriesSpec series) => create.StartsWith("new ", StringComparison.Ordinal)
        ? $"{create} {{ {string.Join(", ", Assignments(series))} }}"
        : throw new ArgumentException("Only constructors take an initializer.", nameof(create));

    private static (ParameterInfo, PropertyInfo, PropertyInfo?) Bind(Type series, IReadOnlyList<ParameterInfo> parameters, string parameter, string path)
    {
        var info = parameters.First(candidate => candidate.Name == parameter);
        var names = path.Split('.');
        var outer = series.GetProperty(names[0]) ?? throw new ArgumentException($"{series.Name} has no property {names[0]}.", nameof(path));
        var inner = names.Length > 1 ? outer.PropertyType.GetProperty(names[1]) ?? throw new ArgumentException($"{outer.PropertyType.Name} has no property {names[1]}.", nameof(path)) : null;
        return (info, outer, inner);
    }

    // A constructor takes an object initializer; a factory method gets its properties set one by one.
    private string Factory(string create, SeriesSpec series) => create.StartsWith("new ", StringComparison.Ordinal)
        ? $"(x, y) => {Expression(create, series)}"
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
            _ when type == typeof(Colormap) => new NamedColormap(values.GetText(info.Name, (string)info.Default)),
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
        NamedColormap colormap => "Colormaps." + colormap.Name,
        Enum choice => $"{type.Name}.{choice}",
        var other => throw new NotSupportedException($"Values of type {other.GetType().Name} are not written yet."),
    };

    private static object Resolve(object value) => value is NamedColormap colormap ? CoreModule.Colormap(colormap.Name) : value;

    // A colormap chosen by name: written as its name, set as the colormap itself.
    private sealed record NamedColormap(string Name);
}
