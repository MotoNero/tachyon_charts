using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Reflection;

namespace Tachyon.Charts.Core.Data;

/// <summary>
/// Reads a collection of objects into a data source, for MVVM with small data. It follows
/// <see cref="INotifyCollectionChanged"/>: new items at the end are cheap, every other change reads the
/// whole collection again. Large or fast data belongs into a <see cref="DataSource{TX, TY}"/>.
/// </summary>
public sealed class ItemsDataSource : IDisposable
{
    /// <summary>The number of items up to which this way is meant for.</summary>
    public const int RecommendedLimit = 100_000;

    private readonly IEnumerable _items;
    private readonly PropertyInfo?[][] _paths = new PropertyInfo?[2][];
    private readonly string[][] _names;
    private readonly INotifyCollectionChanged? _notifier;
    private Type? _itemType;

    /// <summary>Initializes a new instance of the <see cref="ItemsDataSource"/> class.</summary>
    /// <param name="items">The items.</param>
    /// <param name="xPath">The property with the X value, with dots for nested ones.</param>
    /// <param name="yPath">The property with the Y value.</param>
    /// <exception cref="ArgumentException">A path is empty.</exception>
    public ItemsDataSource(IEnumerable items, string xPath, string yPath)
    {
        _items = items ?? throw new ArgumentNullException(nameof(items));
        ArgumentException.ThrowIfNullOrWhiteSpace(xPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(yPath);
        _names = [xPath.Split('.'), yPath.Split('.')];
        Reload();
        _notifier = items as INotifyCollectionChanged;
        if (_notifier is not null)
        {
            _notifier.CollectionChanged += OnCollectionChanged;
        }
    }

    /// <summary>Gets the data. Items without a number count as NaN.</summary>
    public DataSource<double, double> Source { get; } = new();

    /// <summary>Gets the number of items.</summary>
    public int Count { get; private set; }

    /// <summary>Gets a note for the diagnostics overlay if the collection is too large for this way.</summary>
    public string? Warning => Count > RecommendedLimit ? string.Create(CultureInfo.InvariantCulture, $"ItemsSource: {Count:N0} items, use a DataSource") : null;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_notifier is not null)
        {
            _notifier.CollectionChanged -= OnCollectionChanged;
        }
    }

    private static double ToNumber(object? value) => value switch
    {
        null => double.NaN,
        DateTime time => time.Ticks,
        DateTimeOffset time => time.UtcTicks,
        IConvertible number => number.ToDouble(CultureInfo.InvariantCulture),
        _ => double.NaN,
    };

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is { } added && e.NewStartingIndex == Count)
        {
            foreach (var item in added)
            {
                Source.Append(Read(item, 0), Read(item, 1));
                Count++;
            }

            return;
        }

        Reload();
    }

    private void Reload()
    {
        var x = new List<double>();
        var y = new List<double>();
        foreach (var item in _items)
        {
            x.Add(Read(item, 0));
            y.Add(Read(item, 1));
        }

        Source.Clear();
        Source.Append(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(x), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(y));
        Count = x.Count;
    }

    // The properties stay known as long as the items are of one type.
    private double Read(object? item, int axis)
    {
        if (item is null)
        {
            return double.NaN;
        }

        if (item.GetType() != _itemType)
        {
            _itemType = item.GetType();
            _paths[0] = Resolve(_itemType, _names[0]);
            _paths[1] = Resolve(_itemType, _names[1]);
        }

        object? value = item;
        foreach (var property in _paths[axis])
        {
            if (value is null || property is null)
            {
                return double.NaN;
            }

            value = property.GetValue(value);
        }

        return ToNumber(value);
    }

    private static PropertyInfo?[] Resolve(Type type, string[] names)
    {
        var path = new PropertyInfo?[names.Length];
        var current = type;
        for (var i = 0; i < names.Length && current is not null; i++)
        {
            path[i] = current.GetProperty(names[i]);
            current = path[i]?.PropertyType;
        }

        return path;
    }
}
