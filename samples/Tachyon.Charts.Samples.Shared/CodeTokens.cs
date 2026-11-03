using Tachyon.Charts.Rendering;

namespace Tachyon.Charts.Samples.Shared;

/// <summary>What a piece of C# code is, for its color.</summary>
public enum CodeKind
{
    /// <summary>Names, operators and spaces.</summary>
    Plain,

    /// <summary>A keyword of the language.</summary>
    Keyword,

    /// <summary>A name that starts with a capital letter, mostly a type or a method.</summary>
    Name,

    /// <summary>A string or a character.</summary>
    Text,

    /// <summary>A comment.</summary>
    Comment,

    /// <summary>A number.</summary>
    Number,
}

/// <summary>A piece of code.</summary>
/// <param name="Start">The first character.</param>
/// <param name="Length">The number of characters.</param>
/// <param name="Kind">What it is.</param>
public readonly record struct CodeToken(int Start, int Length, CodeKind Kind);

/// <summary>Splits C# code into pieces for colors: enough for reading, not a compiler.</summary>
public static class CodeTokens
{
    private static readonly HashSet<string> _keywords =
    [
        "abstract", "as", "base", "bool", "break", "case", "catch", "char", "class", "const", "continue", "default",
        "delegate", "do", "double", "else", "enum", "event", "false", "finally", "fixed", "float", "for", "foreach",
        "get", "if", "in", "init", "int", "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "out", "override", "params", "private", "protected", "public", "readonly", "record", "ref", "return",
        "sealed", "set", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unsafe", "using", "var", "virtual", "void", "volatile", "when", "while", "with", "yield", "async",
        "await", "byte", "short", "ushort", "sizeof", "nameof", "partial", "required", "global", "not", "and", "or",
    ];

    /// <summary>Gets the colors on a dark background, as the showroom has.</summary>
    public static IReadOnlyDictionary<CodeKind, Rgba8> DarkColors { get; } = new Dictionary<CodeKind, Rgba8>
    {
        [CodeKind.Plain] = new(220, 226, 235, 255),
        [CodeKind.Keyword] = new(110, 170, 255, 255),
        [CodeKind.Name] = new(110, 215, 200, 255),
        [CodeKind.Text] = new(235, 170, 120, 255),
        [CodeKind.Comment] = new(120, 160, 110, 255),
        [CodeKind.Number] = new(190, 215, 150, 255),
    };

    /// <summary>Splits code into pieces that cover it without gaps.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The pieces in order.</returns>
    public static IReadOnlyList<CodeToken> Split(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var tokens = new List<CodeToken>();
        var plainStart = 0;
        var i = 0;
        while (i < code.Length)
        {
            var (length, kind) = Read(code, i);
            if (kind == CodeKind.Plain)
            {
                i += Math.Max(length, 1);
                continue;
            }

            if (i > plainStart)
            {
                tokens.Add(new CodeToken(plainStart, i - plainStart, CodeKind.Plain));
            }

            tokens.Add(new CodeToken(i, length, kind));
            i += length;
            plainStart = i;
        }

        if (code.Length > plainStart)
        {
            tokens.Add(new CodeToken(plainStart, code.Length - plainStart, CodeKind.Plain));
        }

        return tokens;
    }

    // One piece at a place: its length and kind, or a length of one for plain text.
    private static (int Length, CodeKind Kind) Read(string code, int i)
    {
        var c = code[i];
        var next = i + 1 < code.Length ? code[i + 1] : '\0';
        if (c == '/' && next == '/')
        {
            return (Until(code, i, "\n", include: false) - i, CodeKind.Comment);
        }

        if (c == '/' && next == '*')
        {
            return (Until(code, i + 2, "*/", include: true) - i, CodeKind.Comment);
        }

        if (c == '"' || (c is '@' or '$' && next == '"') || (c == '$' && next == '@'))
        {
            return (ReadString(code, i) - i, CodeKind.Text);
        }

        if (c == '\'')
        {
            var end = i + 1;
            while (end < code.Length && code[end] != '\'' && code[end] != '\n')
            {
                end += code[end] == '\\' ? 2 : 1;
            }

            return (Math.Min(end + 1, code.Length) - i, CodeKind.Text);
        }

        var isWordStart = i == 0 || !(char.IsLetterOrDigit(code[i - 1]) || code[i - 1] == '_');
        if (char.IsDigit(c) && isWordStart)
        {
            var end = i;
            while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] is '.' or '_') && !(code[end] == '.' && (end + 1 >= code.Length || !char.IsDigit(code[end + 1]))))
            {
                end++;
            }

            return (end - i, CodeKind.Number);
        }

        if ((char.IsLetter(c) || c == '_') && isWordStart)
        {
            var end = i;
            while (end < code.Length && (char.IsLetterOrDigit(code[end]) || code[end] == '_'))
            {
                end++;
            }

            var word = code[i..end];
            var kind = _keywords.Contains(word) ? CodeKind.Keyword : char.IsUpper(c) ? CodeKind.Name : CodeKind.Plain;
            return kind == CodeKind.Plain ? (end - i, CodeKind.Plain) : (end - i, kind);
        }

        return (1, CodeKind.Plain);
    }

    // A string with its prefix; verbatim strings double their quotes, the others escape them.
    private static int ReadString(string code, int i)
    {
        var isVerbatim = false;
        while (code[i] is '@' or '$')
        {
            isVerbatim |= code[i] == '@';
            i++;
        }

        i++;
        while (i < code.Length)
        {
            if (!isVerbatim && code[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (code[i] == '"')
            {
                if (isVerbatim && i + 1 < code.Length && code[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }

                return i + 1;
            }

            if (!isVerbatim && code[i] == '\n')
            {
                return i;
            }

            i++;
        }

        return code.Length;
    }

    private static int Until(string code, int from, string end, bool include)
    {
        var index = code.IndexOf(end, from, StringComparison.Ordinal);
        return index < 0 ? code.Length : include ? index + end.Length : index;
    }
}
