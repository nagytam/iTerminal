namespace iConsole.Helper;

/// <summary>What a stretch of source text is, for colouring.</summary>
public enum SyntaxKind
{
    Plain,
    Keyword,
    String,
    Comment,
    Number,
}

/// <summary>A coloured stretch of one line: <c>Start</c> and <c>Length</c> in characters.</summary>
public readonly record struct SyntaxSpan(int Start, int Length, SyntaxKind Kind);

/// <summary>
/// The lexical rules of one language, just enough to colour it: keywords, string quotes, a line comment
/// and a block comment. It is a colouring aid, not a parser — an unterminated string ends with its line.
/// </summary>
public sealed class SyntaxLanguage
{
    public SyntaxLanguage(
        string name,
        IEnumerable<string> keywords,
        string[] lineComments,
        (string Start, string End)? blockComment = null,
        string stringQuotes = "\"'",
        bool ignoreCase = false)
    {
        Name = name;
        Keywords = new HashSet<string>(keywords, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        LineComments = lineComments;
        BlockComment = blockComment;
        StringQuotes = stringQuotes;
    }

    public string Name { get; }
    public HashSet<string> Keywords { get; }
    public string[] LineComments { get; }
    public (string Start, string End)? BlockComment { get; }
    public string StringQuotes { get; }
}

/// <summary>
/// Picks a <see cref="SyntaxLanguage"/> from a file name and splits lines into coloured spans. A block comment
/// runs over several lines, so a line is tokenized from the state the line before it ended in
/// (<see cref="Tokenize"/> returns the state the next line starts in).
/// </summary>
public static class SyntaxHighlighter
{
    private static readonly string[] CLikeKeywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof", "uint",
        "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while", "record",
        "init", "required", "yield", "when", "where", "get", "set", "partial", "function", "let", "export", "import",
        "from", "extends", "implements", "package", "final", "throws", "super", "instanceof", "typedef", "union",
        "template", "typename", "auto", "register", "signed", "unsigned", "inline", "nullptr", "fn", "mut", "impl",
        "trait", "pub", "match", "loop", "mod", "use", "crate", "self", "type", "func", "defer", "go", "chan", "range",
        "map", "interface", "select", "val", "fun", "object", "companion", "data", "open", "override", "lateinit",
    ];

    private static readonly string[] PythonKeywords =
    [
        "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except",
        "False", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not",
        "or", "pass", "raise", "return", "True", "try", "while", "with", "yield", "self", "print",
    ];

    private static readonly string[] ShellKeywords =
    [
        "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done", "case", "esac", "in", "function",
        "return", "exit", "export", "local", "readonly", "echo", "cd", "set", "unset", "source", "alias", "trap",
        "param", "foreach", "switch", "begin", "process", "end", "try", "catch", "finally", "throw", "break",
        "continue", "filter", "workflow", "Write-Host", "Write-Output", "Get-ChildItem", "Set-Location",
    ];

    private static readonly string[] SqlKeywords =
    [
        "select", "from", "where", "insert", "into", "values", "update", "set", "delete", "create", "table", "alter",
        "drop", "index", "view", "join", "inner", "left", "right", "outer", "on", "group", "by", "order", "having",
        "union", "all", "distinct", "as", "and", "or", "not", "null", "is", "in", "like", "between", "exists", "case",
        "when", "then", "else", "end", "limit", "offset", "primary", "key", "foreign", "references", "default",
        "constraint", "unique", "begin", "commit", "rollback", "transaction", "int", "varchar", "text", "date",
    ];

    private static readonly string[] MarkupKeywords = [];

    private static readonly string[] DataKeywords = ["true", "false", "null", "yes", "no", "on", "off"];

    private static readonly SyntaxLanguage CLike = new(
        "C-like", CLikeKeywords, ["//"], ("/*", "*/"), "\"'`");

    private static readonly SyntaxLanguage Python = new(
        "Python", PythonKeywords, ["#"], null, "\"'");

    private static readonly SyntaxLanguage Shell = new(
        "Shell", ShellKeywords, ["#"], null, "\"'");

    private static readonly SyntaxLanguage Sql = new(
        "SQL", SqlKeywords, ["--"], ("/*", "*/"), "\"'", ignoreCase: true);

    private static readonly SyntaxLanguage Markup = new(
        "Markup", MarkupKeywords, [], ("<!--", "-->"), "\"'");

    private static readonly SyntaxLanguage Json = new(
        "JSON", DataKeywords, [], null, "\"");

    private static readonly SyntaxLanguage Config = new(
        "Config", DataKeywords, ["#", ";"], null, "\"'", ignoreCase: true);

    private static readonly SyntaxLanguage Batch = new(
        "Batch", ["if", "else", "for", "in", "do", "goto", "call", "echo", "set", "exit", "rem", "setlocal", "endlocal", "not", "exist"],
        ["rem ", "::"], null, "\"", ignoreCase: true);

    private static readonly Dictionary<string, SyntaxLanguage> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = CLike, [".csx"] = CLike, [".java"] = CLike, [".js"] = CLike, [".jsx"] = CLike, [".mjs"] = CLike,
        [".ts"] = CLike, [".tsx"] = CLike, [".c"] = CLike, [".h"] = CLike, [".cpp"] = CLike, [".cc"] = CLike,
        [".hpp"] = CLike, [".go"] = CLike, [".rs"] = CLike, [".kt"] = CLike, [".swift"] = CLike, [".php"] = CLike,
        [".scala"] = CLike, [".dart"] = CLike, [".css"] = CLike, [".scss"] = CLike,
        [".py"] = Python, [".pyw"] = Python, [".rb"] = Python, [".pl"] = Python, [".r"] = Python,
        [".sh"] = Shell, [".bash"] = Shell, [".zsh"] = Shell, [".ps1"] = Shell, [".psm1"] = Shell,
        [".sql"] = Sql,
        [".xml"] = Markup, [".html"] = Markup, [".htm"] = Markup, [".xaml"] = Markup, [".csproj"] = Markup,
        [".props"] = Markup, [".targets"] = Markup, [".svg"] = Markup, [".slnx"] = Markup, [".wxs"] = Markup,
        [".json"] = Json, [".jsonc"] = Json,
        [".ini"] = Config, [".conf"] = Config, [".cfg"] = Config, [".toml"] = Config, [".yml"] = Config,
        [".yaml"] = Config, [".env"] = Config, [".editorconfig"] = Config, [".properties"] = Config,
        [".bat"] = Batch, [".cmd"] = Batch,
    };

    /// <summary>The language for a file name or path, or null when there is none to colour it with.</summary>
    public static SyntaxLanguage? ForFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        // A URL's query or fragment is not part of the file name.
        int cut = fileName.IndexOfAny(['?', '#']);
        string name = cut >= 0 ? fileName[..cut] : fileName;
        string extension = Path.GetExtension(name);
        return extension.Length > 0 && ByExtension.TryGetValue(extension, out SyntaxLanguage? language) ? language : null;
    }

    /// <summary>
    /// Splits <paramref name="line"/> into coloured spans, appended to <paramref name="spans"/> (plain text is left out).
    /// <paramref name="inBlockComment"/> says whether the line starts inside a block comment; the result is whether the
    /// next line does.
    /// </summary>
    public static bool Tokenize(SyntaxLanguage language, string line, bool inBlockComment, List<SyntaxSpan> spans)
    {
        int i = 0;
        int length = line.Length;
        while (i < length)
        {
            if (inBlockComment)
            {
                string end = language.BlockComment!.Value.End;
                int close = line.IndexOf(end, i, StringComparison.Ordinal);
                int stop = close < 0 ? length : close + end.Length;
                spans.Add(new SyntaxSpan(i, stop - i, SyntaxKind.Comment));
                i = stop;
                inBlockComment = close < 0;
                continue;
            }

            char c = line[i];
            if (language.BlockComment is { } block && string.CompareOrdinal(line, i, block.Start, 0, block.Start.Length) == 0)
            {
                inBlockComment = true;
                int close = line.IndexOf(block.End, i + block.Start.Length, StringComparison.Ordinal);
                int stop = close < 0 ? length : close + block.End.Length;
                spans.Add(new SyntaxSpan(i, stop - i, SyntaxKind.Comment));
                i = stop;
                inBlockComment = close < 0;
                continue;
            }

            if (StartsLineComment(language, line, i))
            {
                spans.Add(new SyntaxSpan(i, length - i, SyntaxKind.Comment));
                return false;
            }

            if (language.StringQuotes.Contains(c))
            {
                int stop = i + 1;
                while (stop < length && line[stop] != c)
                {
                    stop += line[stop] == '\\' ? 2 : 1;
                }

                stop = Math.Min(length, stop + 1);
                spans.Add(new SyntaxSpan(i, stop - i, SyntaxKind.String));
                i = stop;
                continue;
            }

            if (char.IsDigit(c))
            {
                int stop = i + 1;
                while (stop < length && (char.IsLetterOrDigit(line[stop]) || line[stop] == '.' || line[stop] == '_'))
                {
                    stop++;
                }

                spans.Add(new SyntaxSpan(i, stop - i, SyntaxKind.Number));
                i = stop;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int stop = i + 1;
                while (stop < length && (char.IsLetterOrDigit(line[stop]) || line[stop] == '_' || line[stop] == '-' && language.Name == "Shell"))
                {
                    stop++;
                }

                if (language.Keywords.Contains(line.Substring(i, stop - i)))
                {
                    spans.Add(new SyntaxSpan(i, stop - i, SyntaxKind.Keyword));
                }

                i = stop;
                continue;
            }

            i++;
        }

        return inBlockComment;
    }

    private static bool StartsLineComment(SyntaxLanguage language, string line, int index)
    {
        foreach (string marker in language.LineComments)
        {
            if (string.Compare(line, index, marker, 0, marker.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                // "rem" must start the line's first word; the symbol markers may sit anywhere.
                if (char.IsLetter(marker[0]))
                {
                    return line.AsSpan(0, index).Trim().IsEmpty;
                }

                return true;
            }
        }

        return false;
    }
}
