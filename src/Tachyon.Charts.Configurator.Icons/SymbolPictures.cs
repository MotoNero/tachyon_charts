using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Tachyon.Charts.Icons;

namespace Tachyon.Charts.Configurator.Icons;

/// <summary>
/// The pictures of the symbols for the list of choices: the outline of each symbol as path data, read when the list
/// first shows it, as there are thousands.
/// </summary>
internal sealed class SymbolPictures : IReadOnlyDictionary<string, string>
{
    private readonly ConcurrentDictionary<string, string?> _read = new(StringComparer.Ordinal);

    private SymbolPictures()
    {
    }

    /// <summary>Gets the pictures.</summary>
    public static SymbolPictures Instance { get; } = new();

    /// <inheritdoc />
    public int Count => MaterialSymbols.Names.Count;

    /// <inheritdoc />
    public IEnumerable<string> Keys => MaterialSymbols.Names;

    /// <inheritdoc />
    public IEnumerable<string> Values => MaterialSymbols.Names.Select(name => this[name]);

    /// <inheritdoc />
    public string this[string key] => TryGetValue(key, out var path) ? path : throw new KeyNotFoundException(key);

    /// <inheritdoc />
    public bool ContainsKey(string key) => TryGetValue(key, out _);

    /// <inheritdoc />
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
    {
        value = _read.GetOrAdd(key, MaterialSymbols.PathData);
        return value is not null;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => MaterialSymbols.Names.Select(name => new KeyValuePair<string, string>(name, this[name])).GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
