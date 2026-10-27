using iConsole.Display;
using iConsole.Helper;

namespace iConsole.Services;

/// <summary>The colour each kind of source text is drawn in.</summary>
public sealed record SyntaxPalette(Color Keyword, Color String, Color Comment, Color Number)
{
    /// <summary>The colours the viewer and editor have always used.</summary>
    public static SyntaxPalette Default { get; } = new(Color.Cyan, Color.Green, Color.Gray, Color.Magenta);

    /// <summary>The colour of <paramref name="kind"/>, or <paramref name="plain"/> for plain text.</summary>
    public Color ColorOf(SyntaxKind kind, Color plain)
    {
        return kind switch
        {
            SyntaxKind.Keyword => Keyword,
            SyntaxKind.String => String,
            SyntaxKind.Comment => Comment,
            SyntaxKind.Number => Number,
            _ => plain,
        };
    }
}

/// <summary>
/// The one place that knows how to colour source text, shared by the text viewer, the editor and the Markdown viewer:
/// which language a file or a code fence name means, how a line is split into spans, and what colour each kind gets.
/// An application adds a language with <see cref="Register"/> or recolours with <see cref="Palette"/> once and every
/// control follows.
/// </summary>
public interface ISyntaxHighlightingService
{
    /// <summary>The colours used for each kind of text.</summary>
    SyntaxPalette Palette { get; set; }

    /// <summary>Every language known, built-in and registered.</summary>
    IReadOnlyList<SyntaxLanguage> Languages { get; }

    /// <summary>The language for a file name, path or URL; registered file names and extensions win over the built-in ones. Null when there is none.</summary>
    SyntaxLanguage? FindByFile(string? fileName);

    /// <summary>
    /// The language for a name such as a Markdown fence's info string (<c>csharp</c>, <c>py</c>, <c>PowerShell</c>, <c>.json</c>):
    /// a registered alias, a language name, a built-in alias or a file extension, ignoring case. Null for none.
    /// </summary>
    SyntaxLanguage? FindByName(string? name);

    /// <summary>
    /// Adds a language. <paramref name="extensions"/> are written with the dot (<c>.zig</c>), <paramref name="fileNames"/> are whole
    /// names (<c>Justfile</c>) and <paramref name="aliases"/> are names for <see cref="FindByName"/>. A later registration of the
    /// same extension, file name or alias replaces the earlier one.
    /// </summary>
    void Register(SyntaxLanguage language, IEnumerable<string>? extensions = null, IEnumerable<string>? fileNames = null, IEnumerable<string>? aliases = null);

    /// <summary>Splits <paramref name="line"/> into spans; see <see cref="SyntaxHighlighter.Tokenize"/>. Returns whether the next line starts in a block comment.</summary>
    bool Tokenize(SyntaxLanguage language, string line, bool inBlockComment, List<SyntaxSpan> spans);
}

public sealed class SyntaxHighlightingService : ISyntaxHighlightingService
{
    /// <summary>Built-in names that are not a language's own name or a file extension.</summary>
    private static readonly Dictionary<string, string> BuiltInAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["csharp"] = ".cs", ["c#"] = ".cs", ["c++"] = ".cpp", ["javascript"] = ".js", ["typescript"] = ".ts", ["rust"] = ".rs",
        ["kotlin"] = ".kt", ["python"] = ".py", ["python3"] = ".py", ["ruby"] = ".rb", ["perl"] = ".pl", ["bash"] = ".sh",
        ["shell"] = ".sh", ["sh"] = ".sh", ["powershell"] = ".ps1", ["pwsh"] = ".ps1", ["yaml"] = ".yml", ["docker"] = "Dockerfile",
        ["make"] = "Makefile", ["fsharp"] = ".fs", ["f#"] = ".fs", ["vbnet"] = ".vb", ["vb.net"] = ".vb", ["batch"] = ".bat",
        ["markup"] = ".xml", ["config"] = ".ini", ["json5"] = ".jsonc", ["golang"] = ".go", ["objc"] = ".m", ["objective-c"] = ".m",
    };

    private readonly Lock _gate = new();
    private readonly List<SyntaxLanguage> _registered = [];
    private readonly Dictionary<string, SyntaxLanguage> _extensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SyntaxLanguage> _fileNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SyntaxLanguage> _aliases = new(StringComparer.OrdinalIgnoreCase);
    private SyntaxPalette _palette = SyntaxPalette.Default;

    /// <summary>The instance controls use when none is given to them.</summary>
    public static ISyntaxHighlightingService Default { get; } = new SyntaxHighlightingService();

    public SyntaxPalette Palette
    {
        get { lock (_gate) { return _palette; } }
        set { lock (_gate) { _palette = value ?? SyntaxPalette.Default; } }
    }

    public IReadOnlyList<SyntaxLanguage> Languages
    {
        get
        {
            lock (_gate)
            {
                return [.. SyntaxHighlighter.BuiltInLanguages, .. _registered];
            }
        }
    }

    public SyntaxLanguage? FindByFile(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return null;
        }

        lock (_gate)
        {
            if (_registered.Count > 0)
            {
                int cut = fileName.IndexOfAny(['?', '#']);
                string name = cut >= 0 ? fileName[..cut] : fileName;
                string baseName = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
                if (_fileNames.TryGetValue(baseName, out SyntaxLanguage? named))
                {
                    return named;
                }

                string extension = Path.GetExtension(name);
                if (extension.Length > 0 && _extensions.TryGetValue(extension, out SyntaxLanguage? byExtension))
                {
                    return byExtension;
                }
            }
        }

        return SyntaxHighlighter.ForFile(fileName);
    }

    public SyntaxLanguage? FindByName(string? name)
    {
        string text = (name ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            if (_aliases.TryGetValue(text, out SyntaxLanguage? alias))
            {
                return alias;
            }

            SyntaxLanguage? ownName = _registered.LastOrDefault(l => string.Equals(l.Name, text, StringComparison.OrdinalIgnoreCase));
            if (ownName is not null)
            {
                return ownName;
            }
        }

        SyntaxLanguage? builtIn = SyntaxHighlighter.BuiltInLanguages
            .FirstOrDefault(l => string.Equals(l.Name, text, StringComparison.OrdinalIgnoreCase));
        if (builtIn is not null)
        {
            return builtIn;
        }

        if (BuiltInAliases.TryGetValue(text, out string? target))
        {
            return FindByFile(target);
        }

        return FindByFile(text.StartsWith('.') ? "x" + text : "x." + text);
    }

    public void Register(SyntaxLanguage language, IEnumerable<string>? extensions = null, IEnumerable<string>? fileNames = null, IEnumerable<string>? aliases = null)
    {
        ArgumentNullException.ThrowIfNull(language);
        lock (_gate)
        {
            _registered.Remove(language);
            _registered.Add(language);
            foreach (string extension in extensions ?? [])
            {
                _extensions[extension.StartsWith('.') ? extension : "." + extension] = language;
            }

            foreach (string name in fileNames ?? [])
            {
                _fileNames[name] = language;
            }

            foreach (string alias in aliases ?? [])
            {
                _aliases[alias] = language;
            }
        }
    }

    public bool Tokenize(SyntaxLanguage language, string line, bool inBlockComment, List<SyntaxSpan> spans)
    {
        return SyntaxHighlighter.Tokenize(language, line, inBlockComment, spans);
    }
}
