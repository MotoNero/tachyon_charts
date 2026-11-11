using System.Globalization;
using System.Text;
using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Configurator;

/// <summary>Writes values as C# literals, the same on every machine.</summary>
public static class CodeText
{
    /// <summary>Writes a text as a C# string literal.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The literal with quotes.</returns>
    public static string String(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var code = new StringBuilder("\"", text.Length + 2);
        foreach (var character in text)
        {
            code.Append(character switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) => string.Create(CultureInfo.InvariantCulture, $"\\u{(int)character:X4}"),
                _ => character.ToString(),
            });
        }

        return code.Append('"').ToString();
    }

    /// <summary>Writes a number so that it reads back to the same value.</summary>
    /// <param name="value">The number, finite.</param>
    /// <returns>The literal.</returns>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Writes a yes/no value.</summary>
    /// <param name="value">The value.</param>
    /// <returns><c>true</c> or <c>false</c>.</returns>
    public static string Flag(bool value) => value ? "true" : "false";

    /// <summary>Writes a color as a new <see cref="Rgba8"/>.</summary>
    /// <param name="color">The color.</param>
    /// <returns>The expression.</returns>
    public static string Color(Rgba8 color) => string.Create(CultureInfo.InvariantCulture, $"new Rgba8(0x{color.R:X2}, 0x{color.G:X2}, 0x{color.B:X2}, 0x{color.A:X2})");

    /// <summary>Turns a name into a C# identifier: letters and digits, the first letter as asked.</summary>
    /// <param name="name">The name.</param>
    /// <param name="upper">Whether the identifier starts with a capital, as for properties and methods.</param>
    /// <param name="fallback">The identifier when the name has no letters.</param>
    /// <returns>The identifier.</returns>
    public static string Identifier(string name, bool upper, string fallback)
    {
        var words = (name ?? string.Empty).Split(static character => !char.IsLetterOrDigit(character));
        var code = new StringBuilder();
        foreach (var word in words.Where(word => word.Length > 0))
        {
            code.Append(char.ToUpperInvariant(word[0])).Append(word.AsSpan(1));
        }

        if (code.Length == 0 || !char.IsLetter(code[0]))
        {
            code.Insert(0, fallback);
        }

        code[0] = upper ? char.ToUpperInvariant(code[0]) : char.ToLowerInvariant(code[0]);

        // A word of the language, such as "fixed" from a series called Fixed, gets the fallback behind it.
        var identifier = code.ToString();
        return _keywords.Contains(identifier) ? identifier + char.ToUpperInvariant(fallback[0]) + fallback[1..] : identifier;
    }

    private static readonly HashSet<string> _keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const", "continue",
        "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof", "stackalloc", "static", "string",
        "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
        "using", "virtual", "void", "volatile", "while",
    };

    private static string[] Split(this string text, Func<char, bool> separator)
    {
        var parts = new List<string>();
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || separator(text[i]))
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        return [.. parts];
    }
}
