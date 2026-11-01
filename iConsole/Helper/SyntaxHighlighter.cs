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
/// and a block comment, plus the few spellings that differ between languages (the string escape character,
/// C# verbatim strings, hyphenated cmdlet names, <c>$variables</c> and <c>#</c> preprocessor lines). It is a
/// colouring aid, not a parser — an unterminated string ends with its line.
/// </summary>
public sealed class SyntaxLanguage
{
    public SyntaxLanguage(
        string name,
        IEnumerable<string> keywords,
        string[] lineComments,
        (string Start, string End)? blockComment = null,
        string stringQuotes = "\"'",
        bool ignoreCase = false,
        char stringEscape = '\\',
        bool verbatimStrings = false,
        bool hyphenatedWords = false,
        string variableSigils = "",
        bool preprocessorDirectives = false,
        bool doubledQuotes = false,
        bool tripleQuotes = false,
        char keyDelimiter = '\0',
        bool sectionHeaders = false)
    {
        Name = name;
        Keywords = new HashSet<string>(keywords, ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        LineComments = lineComments;
        BlockComment = blockComment;
        StringQuotes = stringQuotes;
        StringEscape = stringEscape;
        VerbatimStrings = verbatimStrings;
        HyphenatedWords = hyphenatedWords;
        VariableSigils = variableSigils;
        PreprocessorDirectives = preprocessorDirectives;
        DoubledQuotes = doubledQuotes;
        TripleQuotes = tripleQuotes;
        KeyDelimiter = keyDelimiter;
        SectionHeaders = sectionHeaders;
    }

    public string Name { get; }
    public HashSet<string> Keywords { get; }
    public string[] LineComments { get; }
    public (string Start, string End)? BlockComment { get; }
    public string StringQuotes { get; }

    /// <summary>The character that escapes the next one inside a string; <c>'\0'</c> for none (PowerShell uses the backtick).</summary>
    public char StringEscape { get; }

    /// <summary>C# <c>@"…"</c> strings: no backslash escapes, and a doubled quote is a quote.</summary>
    public bool VerbatimStrings { get; }

    /// <summary>Whether a word may contain hyphens, so <c>Write-Host</c> and <c>ForEach-Object</c> are one word.</summary>
    public bool HyphenatedWords { get; }

    /// <summary>The sigils that start a variable (<c>$</c> in PowerShell, <c>$@%</c> in Perl); a keyword right after one is a name, not a keyword. Empty for none.</summary>
    public string VariableSigils { get; }

    /// <summary>Whether a <c>#</c> at the start of a line opens a preprocessor directive (<c>#include</c>, <c>#region</c>), coloured as a keyword.</summary>
    public bool PreprocessorDirectives { get; }

    /// <summary>VB <c>"say ""hi"""</c>: a doubled quote inside a string is one quote, and there are no backslash escapes.</summary>
    public bool DoubledQuotes { get; }

    /// <summary>Python triple-quoted strings on one line; a string that runs past the line ends with it.</summary>
    public bool TripleQuotes { get; }

    /// <summary>The character that ends a key on a line of a key-value format (<c>:</c> in YAML, <c>=</c> in INI); the key is coloured as a keyword. <c>'\0'</c> for none.</summary>
    public char KeyDelimiter { get; }

    /// <summary>Whether a line that starts with <c>[</c> is a section header (<c>[core]</c>), coloured as a keyword.</summary>
    public bool SectionHeaders { get; }
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
        "break", "continue", "shift", "declare", "typeset", "eval", "exec", "printf", "read", "test", "getopts", "select",
        "time", "let", "wait", "umask", "pushd", "popd", "hash", "type", "builtin", "command", "coproc",
    ];

    /// <summary>C# keywords and the contextual ones that read as keywords (<c>get</c>, <c>nameof</c>, LINQ's <c>from</c>); not <c>value</c> or <c>by</c>, which are often names.</summary>
    private static readonly string[] CSharpKeywords =
    [
        "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class",
        "const", "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit",
        "extern", "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out",
        "override", "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "var", "virtual", "void", "volatile", "while",
        "nint", "nuint", "dynamic", "record", "init", "required", "file", "scoped", "global", "nameof", "with", "and",
        "or", "not", "when", "where", "yield", "partial", "get", "set", "from", "select", "orderby", "ascending", "descending",
    ];

    /// <summary>Java keywords, the literals and the contextual <c>var</c>, <c>record</c>, <c>yield</c>, <c>sealed</c> and <c>permits</c>.</summary>
    private static readonly string[] JavaKeywords =
    [
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default",
        "do", "double", "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if", "implements", "import",
        "instanceof", "int", "interface", "long", "native", "new", "package", "private", "protected", "public", "return",
        "short", "static", "strictfp", "super", "switch", "synchronized", "this", "throw", "throws", "transient", "try",
        "void", "volatile", "while", "true", "false", "null", "var", "record", "yield", "sealed", "permits",
    ];

    /// <summary>C++ keywords, the alternative spellings (<c>and</c>, <c>not_eq</c>) and the contextual <c>final</c>, <c>override</c>, <c>import</c>, <c>module</c>.</summary>
    private static readonly string[] CppKeywords =
    [
        "alignas", "alignof", "and", "and_eq", "asm", "auto", "bitand", "bitor", "bool", "break", "case", "catch", "char",
        "char8_t", "char16_t", "char32_t", "class", "compl", "concept", "const", "consteval", "constexpr", "constinit",
        "const_cast", "continue", "co_await", "co_return", "co_yield", "decltype", "default", "delete", "do", "double",
        "dynamic_cast", "else", "enum", "explicit", "export", "extern", "false", "float", "for", "friend", "goto", "if",
        "inline", "int", "long", "mutable", "namespace", "new", "noexcept", "not", "not_eq", "nullptr", "operator", "or",
        "or_eq", "private", "protected", "public", "register", "reinterpret_cast", "requires", "return", "short", "signed",
        "sizeof", "static", "static_assert", "static_cast", "struct", "switch", "template", "this", "thread_local", "throw",
        "true", "try", "typedef", "typeid", "typename", "union", "unsigned", "using", "virtual", "void", "volatile",
        "wchar_t", "while", "xor", "xor_eq", "override", "final", "import", "module",
    ];

    /// <summary>
    /// PowerShell keywords, the operators written after a hyphen (<c>-eq</c> is read as <c>eq</c>), and the common cmdlets.
    /// PowerShell is case-insensitive, so the list is matched without regard to case.
    /// </summary>
    private static readonly string[] PowerShellKeywords =
    [
        "begin", "break", "catch", "class", "continue", "data", "define", "do", "dynamicparam", "else", "elseif", "end",
        "enum", "exit", "filter", "finally", "for", "foreach", "from", "function", "hidden", "if", "in", "param", "process",
        "return", "static", "switch", "throw", "trap", "try", "until", "using", "var", "while", "workflow", "parallel",
        "sequence", "configuration", "inlinescript", "true", "false", "null",
        "and", "or", "not", "xor", "eq", "ne", "gt", "ge", "lt", "le", "is", "isnot", "as", "notin",
        "Write-Host", "Write-Output", "Write-Error", "Write-Warning", "Write-Verbose", "Read-Host", "Get-ChildItem",
        "Get-Content", "Set-Content", "Add-Content", "Get-Item", "Get-Process", "Get-Service", "Get-Command", "Get-Help",
        "Get-Member", "Get-Location", "Set-Location", "Set-Variable", "Get-Variable", "New-Item", "Remove-Item", "Copy-Item",
        "Move-Item", "Rename-Item", "Test-Path", "Start-Process", "Stop-Process", "Start-Sleep", "Invoke-Expression",
        "Invoke-Command", "Invoke-WebRequest", "Invoke-RestMethod", "Import-Module", "Export-ModuleMember",
        "Select-Object", "Where-Object", "ForEach-Object", "Sort-Object", "Group-Object", "Measure-Object",
        "Format-Table", "Format-List", "Out-File", "Out-Null", "Out-String", "ConvertTo-Json", "ConvertFrom-Json",
        "Set-StrictMode", "Set-ExecutionPolicy", "Get-ExecutionPolicy", "Start-Job", "Receive-Job", "Wait-Job",
    ];

    /// <summary>C keywords (C99 and C11), without the C++-only words.</summary>
    private static readonly string[] CKeywords =
    [
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float",
        "for", "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed", "sizeof", "static",
        "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "_Alignas", "_Alignof", "_Atomic",
        "_Bool", "_Complex", "_Generic", "_Imaginary", "_Noreturn", "_Static_assert", "_Thread_local",
    ];

    /// <summary>Go keywords, the predeclared constants and the common predeclared types.</summary>
    private static readonly string[] GoKeywords =
    [
        "break", "case", "chan", "const", "continue", "default", "defer", "else", "fallthrough", "for", "func", "go", "goto",
        "if", "import", "interface", "map", "package", "range", "return", "select", "struct", "switch", "type", "var",
        "true", "false", "nil", "iota", "any", "error", "string", "bool", "int", "int64", "uint8", "byte", "rune", "float64",
    ];

    /// <summary>Rust keywords, the reserved words and the primitive types a reader expects to be coloured.</summary>
    private static readonly string[] RustKeywords =
    [
        "as", "async", "await", "break", "const", "continue", "crate", "dyn", "else", "enum", "extern", "false", "fn", "for",
        "if", "impl", "in", "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static",
        "struct", "super", "trait", "true", "type", "unsafe", "use", "where", "while", "abstract", "become", "box", "do",
        "final", "macro", "override", "priv", "typeof", "unsized", "virtual", "yield", "try",
        "bool", "char", "str", "i8", "i16", "i32", "i64", "i128", "isize", "u8", "u16", "u32", "u64", "u128", "usize", "f32", "f64",
    ];

    /// <summary>JavaScript keywords, the literals and the contextual <c>let</c>, <c>static</c>, <c>async</c>, <c>await</c>, <c>yield</c>, <c>of</c>.</summary>
    private static readonly string[] JavaScriptKeywords =
    [
        "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do", "else",
        "export", "extends", "false", "finally", "for", "function", "if", "import", "in", "instanceof", "let", "new", "null",
        "return", "static", "super", "switch", "this", "throw", "true", "try", "typeof", "undefined", "var", "void", "while",
        "with", "yield", "async", "of",
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

    private static readonly SyntaxLanguage CSharp = new(
        "C#", CSharpKeywords, ["//"], ("/*", "*/"), "\"'", verbatimStrings: true, preprocessorDirectives: true);

    private static readonly SyntaxLanguage Java = new(
        "Java", JavaKeywords, ["//"], ("/*", "*/"), "\"'");

    private static readonly SyntaxLanguage Cpp = new(
        "C++", CppKeywords, ["//"], ("/*", "*/"), "\"'", preprocessorDirectives: true);

    private static readonly SyntaxLanguage PowerShell = new(
        "PowerShell", PowerShellKeywords, ["#"], ("<#", "#>"), "\"'", ignoreCase: true,
        stringEscape: '`', hyphenatedWords: true, variableSigils: "$");

    private static readonly SyntaxLanguage C = new(
        "C", CKeywords, ["//"], ("/*", "*/"), "\"'", preprocessorDirectives: true);

    private static readonly SyntaxLanguage Go = new(
        "Go", GoKeywords, ["//"], ("/*", "*/"), "\"'`");

    // Rust's single quote is also a lifetime ('a), so only double-quoted strings are coloured.
    private static readonly SyntaxLanguage Rust = new(
        "Rust", RustKeywords, ["//"], ("/*", "*/"), "\"");

    private static readonly SyntaxLanguage JavaScript = new(
        "JavaScript", JavaScriptKeywords, ["//"], ("/*", "*/"), "\"'`");

    private static readonly SyntaxLanguage Python = new(
        "Python", PythonKeywords, ["#"], null, "\"'", tripleQuotes: true);

    private static readonly SyntaxLanguage Shell = new(
        "Shell", ShellKeywords, ["#"], null, "\"'", hyphenatedWords: true, variableSigils: "$");

    private static readonly SyntaxLanguage Yaml = new(
        "YAML", DataKeywords, ["#"], null, "\"'", ignoreCase: true, doubledQuotes: true, keyDelimiter: ':');

    private static readonly SyntaxLanguage Toml = new(
        "TOML", DataKeywords, ["#"], null, "\"'", ignoreCase: true, keyDelimiter: '=', sectionHeaders: true, tripleQuotes: true);

    private static readonly SyntaxLanguage Ini = new(
        "INI", [], [";", "#"], null, "\"'", keyDelimiter: '=', sectionHeaders: true);

    private static readonly SyntaxLanguage DotEnv = new(
        "Dotenv", [], ["#"], null, "\"'", keyDelimiter: '=');

    private static readonly SyntaxLanguage Properties = new(
        "Properties", [], ["#"], null, "\"'", keyDelimiter: '=');

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

    private static readonly SyntaxLanguage Lua = new(
        "Lua",
        ["and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in", "local", "nil", "not", "or",
         "repeat", "return", "then", "true", "until", "while"],
        ["--"], ("--[[", "]]"), "\"'");

    private static readonly SyntaxLanguage R = new(
        "R",
        ["if", "else", "repeat", "while", "function", "for", "in", "next", "break", "TRUE", "FALSE", "NULL", "NA",
         "NA_integer_", "NA_real_", "NA_character_", "Inf", "NaN"],
        ["#"], null, "\"'");

    private static readonly SyntaxLanguage Perl = new(
        "Perl",
        ["abs", "and", "bless", "chomp", "close", "cmp", "defined", "delete", "die", "do", "each", "else", "elsif", "eq",
         "eval", "exists", "exit", "for", "foreach", "ge", "grep", "gt", "if", "index", "join", "keys", "last", "le", "length",
         "local", "lc", "uc", "map", "my", "ne", "next", "no", "not", "open", "or", "our", "package", "pop", "print", "printf",
         "push", "redo", "ref", "require", "return", "reverse", "scalar", "shift", "sort", "splice", "split", "sprintf", "state",
         "sub", "substr", "undef", "unless", "until", "unshift", "use", "values", "wantarray", "warn", "while", "xor", "qw", "qq",
         "BEGIN", "END", "__END__", "__DATA__", "__PACKAGE__"],
        ["#"], null, "\"'", variableSigils: "$@%");

    private static readonly SyntaxLanguage Php = new(
        "PHP",
        ["abstract", "and", "array", "as", "break", "callable", "case", "catch", "class", "clone", "const", "continue",
         "declare", "default", "die", "do", "echo", "else", "elseif", "empty", "enddeclare", "endfor", "endforeach", "endif",
         "endswitch", "endwhile", "enum", "eval", "exit", "extends", "final", "finally", "fn", "for", "foreach", "function",
         "global", "goto", "if", "implements", "include", "include_once", "instanceof", "insteadof", "interface", "isset",
         "list", "match", "namespace", "new", "or", "print", "private", "protected", "public", "readonly", "require",
         "require_once", "return", "static", "switch", "throw", "trait", "try", "unset", "use", "var", "while", "xor", "yield",
         "true", "false", "null", "self", "parent"],
        ["//", "#"], ("/*", "*/"), "\"'", ignoreCase: true, variableSigils: "$");

    private static readonly SyntaxLanguage VisualBasic = new(
        "Visual Basic",
        ["AddHandler", "As", "Boolean", "ByRef", "ByVal", "Case", "Catch", "Class", "Const", "Dim", "Do", "Double", "Each", "Else",
         "ElseIf", "End", "Enum", "Exit", "False", "Finally", "For", "Function", "If", "Imports", "In", "Integer", "Interface",
         "Is", "Loop", "Me", "Module", "Namespace", "New", "Next", "Nothing", "Not", "Of", "Option", "Or", "OrElse", "And",
         "AndAlso", "Private", "Property", "Protected", "Public", "Return", "Select", "Shared", "Step", "String", "Structure",
         "Sub", "Then", "Throw", "To", "True", "Try", "Using", "Wend", "While", "With"],
        ["'", "rem "], null, "\"", ignoreCase: true, stringEscape: '\0', doubledQuotes: true);

    private static readonly SyntaxLanguage FSharp = new(
        "F#",
        ["abstract", "and", "as", "async", "begin", "do", "done", "downto", "elif", "else", "end", "exception", "false", "finally",
         "for", "fun", "function", "if", "in", "inherit", "inline", "interface", "let", "match", "member", "module", "mutable",
         "namespace", "new", "not", "null", "of", "open", "or", "override", "rec", "return", "then", "to", "true", "try", "type",
         "use", "val", "when", "while", "with", "yield", "task"],
        ["//"], ("(*", "*)"), "\"");

    private static readonly SyntaxLanguage BuildFile = new(
        "Build file",
        ["include", "ifeq", "ifneq", "ifdef", "ifndef", "else", "endif", "define", "endef", "export", "override", "vpath",
         "FROM", "RUN", "CMD", "LABEL", "EXPOSE", "ENV", "ADD", "COPY", "ENTRYPOINT", "VOLUME", "USER", "WORKDIR", "ARG",
         "ONBUILD", "STOPSIGNAL", "HEALTHCHECK", "SHELL", "AS"],
        ["#"], null, "\"'");

    /// <summary>Files that have a language by name, because they have no extension, or one that says nothing.</summary>
    private static readonly Dictionary<string, SyntaxLanguage> ByFileName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Dockerfile"] = BuildFile, ["Containerfile"] = BuildFile, ["Makefile"] = BuildFile, ["GNUmakefile"] = BuildFile,
        [".bashrc"] = Shell, [".zshrc"] = Shell, [".profile"] = Shell, [".bash_profile"] = Shell,
        [".gitignore"] = Config, [".gitattributes"] = Config, [".dockerignore"] = Config, [".gitconfig"] = Ini, [".npmrc"] = Ini,
        ["Directory.Build.props"] = Markup, ["Directory.Build.targets"] = Markup,
    };

    private static readonly Dictionary<string, SyntaxLanguage> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".lua"] = Lua, [".vb"] = VisualBasic, [".fs"] = FSharp, [".fsx"] = FSharp, [".fsi"] = FSharp,
        [".mk"] = BuildFile, [".dockerfile"] = BuildFile,
        [".gradle"] = CLike, [".groovy"] = CLike, [".kts"] = CLike, [".proto"] = CLike, [".m"] = CLike, [".mm"] = CLike,
        [".cs"] = CSharp, [".csx"] = CSharp,
        [".c"] = C, [".h"] = Cpp, [".cpp"] = Cpp, [".cc"] = Cpp, [".cxx"] = Cpp, [".hpp"] = Cpp, [".hh"] = Cpp, [".hxx"] = Cpp,
        [".ps1"] = PowerShell, [".psm1"] = PowerShell, [".psd1"] = PowerShell,
        [".vue"] = Markup, [".svelte"] = Markup, [".xhtml"] = Markup, [".resx"] = Markup, [".nuspec"] = Markup,
        [".tf"] = Config, [".tfvars"] = Config, [".gitignore"] = Config,
        [".java"] = Java, [".js"] = JavaScript, [".jsx"] = JavaScript, [".mjs"] = JavaScript,
        [".ts"] = CLike, [".tsx"] = CLike, [".go"] = Go, [".rs"] = Rust, [".kt"] = CLike, [".swift"] = CLike, [".php"] = Php,
        [".scala"] = CLike, [".dart"] = CLike, [".css"] = CLike, [".scss"] = CLike,
        [".py"] = Python, [".pyw"] = Python, [".rb"] = Python, [".pl"] = Perl, [".pm"] = Perl, [".r"] = R,
        [".sh"] = Shell, [".bash"] = Shell, [".zsh"] = Shell,
        [".sql"] = Sql,
        [".xml"] = Markup, [".html"] = Markup, [".htm"] = Markup, [".xaml"] = Markup, [".csproj"] = Markup,
        [".props"] = Markup, [".targets"] = Markup, [".svg"] = Markup, [".slnx"] = Markup, [".wxs"] = Markup,
        [".json"] = Json, [".jsonc"] = Json,
        [".ini"] = Ini, [".conf"] = Ini, [".cfg"] = Ini, [".editorconfig"] = Ini, [".toml"] = Toml, [".yml"] = Yaml,
        [".yaml"] = Yaml, [".env"] = DotEnv, [".properties"] = Properties,
        [".bat"] = Batch, [".cmd"] = Batch,
    };

    /// <summary>Every language built in, for <see cref="iConsole.Services.ISyntaxHighlightingService"/> to list and look up by name.</summary>
    internal static IReadOnlyList<SyntaxLanguage> BuiltInLanguages { get; } =
        [CLike, CSharp, Java, C, Cpp, Go, Rust, JavaScript, PowerShell, Python, Shell, Yaml, Toml, Ini, DotEnv, Properties, Sql, Markup, Json, Config, Batch, Lua, R, Perl, Php, VisualBasic, FSharp, BuildFile];

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
        // The name alone can say it (Dockerfile, Makefile, .bashrc); a URL's last segment is the name too.
        string baseName = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];
        if (ByFileName.TryGetValue(baseName, out SyntaxLanguage? named))
        {
            return named;
        }

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
        if (!inBlockComment && TryLeadingKey(language, line, spans, out int afterKey))
        {
            i = afterKey;
        }
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

            if (c == '#' && language.PreprocessorDirectives && line.AsSpan(0, i).Trim().IsEmpty)
            {
                // "#include", "#region": the directive word is a keyword, the rest of the line is ordinary code.
                int stop = i + 1;
                while (stop < length && char.IsLetter(line[stop]))
                {
                    stop++;
                }

                spans.Add(new SyntaxSpan(i, stop - i, SyntaxKind.Keyword));
                i = stop;
                continue;
            }

            if (StartsLineComment(language, line, i))
            {
                spans.Add(new SyntaxSpan(i, length - i, SyntaxKind.Comment));
                return false;
            }

            if (language.StringQuotes.Contains(c) && language.TripleQuotes && i + 2 < length && line[i + 1] == c && line[i + 2] == c)
            {
                // Python """…""" or '''…''' on one line.
                int close = line.IndexOf(new string(c, 3), i + 3, StringComparison.Ordinal);
                int tripleStop = close < 0 ? length : close + 3;
                spans.Add(new SyntaxSpan(i, tripleStop - i, SyntaxKind.String));
                i = tripleStop;
                continue;
            }

            if (language.StringQuotes.Contains(c))
            {
                // C# @"…": backslashes are plain and a doubled quote stands for one quote; VB's "" is the same doubling.
                bool verbatim = language.VerbatimStrings && i > 0 && line[i - 1] == '@';
                int start = verbatim ? i - 1 : i;
                int stop = i + 1;
                while (stop < length)
                {
                    if (line[stop] == c)
                    {
                        if ((verbatim || language.DoubledQuotes) && stop + 1 < length && line[stop + 1] == c)
                        {
                            stop += 2;
                            continue;
                        }

                        break;
                    }

                    bool escaped = !verbatim && language.StringEscape != '\0' && line[stop] == language.StringEscape;
                    stop += escaped ? 2 : 1;
                }

                stop = Math.Min(length, stop + 1);
                spans.Add(new SyntaxSpan(start, stop - start, SyntaxKind.String));
                i = stop;
                continue;
            }

            if (char.IsDigit(c))
            {
                int stop = i + 1;
                while (stop < length && (char.IsLetterOrDigit(line[stop]) || line[stop] == '.' || line[stop] == '_'
                    // C++14 digit separators: 1'000'000.
                    || line[stop] == '\'' && stop + 1 < length && char.IsLetterOrDigit(line[stop + 1])))
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
                while (stop < length && (char.IsLetterOrDigit(line[stop]) || line[stop] == '_' || line[stop] == '-' && language.HyphenatedWords))
                {
                    stop++;
                }

                // A name after a sigil ($process, $data) is a variable, not the keyword it spells.
                bool variable = i > 0 && language.VariableSigils.Contains(line[i - 1]);
                if (!variable && language.Keywords.Contains(line.Substring(i, stop - i)))
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

    /// <summary>
    /// The start of a key-value or section line: a <c>[section]</c> header, or the key before the delimiter (after the
    /// indentation and a YAML <c>- </c>). The key is coloured as a keyword; <paramref name="end"/> is where ordinary
    /// tokenizing resumes.
    /// </summary>
    private static bool TryLeadingKey(SyntaxLanguage language, string line, List<SyntaxSpan> spans, out int end)
    {
        end = 0;
        int start = 0;
        while (start < line.Length && char.IsWhiteSpace(line[start]))
        {
            start++;
        }

        if (start >= line.Length || StartsLineComment(language, line, start))
        {
            return false;
        }

        if (language.SectionHeaders && line[start] == '[')
        {
            int close = line.IndexOf(']', start);
            end = close < 0 ? line.Length : close + 1;
            spans.Add(new SyntaxSpan(start, end - start, SyntaxKind.Keyword));
            return true;
        }

        if (language.KeyDelimiter == '\0')
        {
            return false;
        }

        // A YAML list item: "- key: value".
        if (language.KeyDelimiter == ':' && line[start] == '-' && start + 1 < line.Length && line[start + 1] == ' ')
        {
            start += 2;
            while (start < line.Length && line[start] == ' ')
            {
                start++;
            }

            if (start >= line.Length)
            {
                return false;
            }
        }

        int keyEnd;
        if (line[start] == '"' || line[start] == '\'')
        {
            // A quoted key; the quotes are part of it.
            int close = line.IndexOf(line[start], start + 1);
            if (close < 0)
            {
                return false;
            }

            int delimiter = close + 1;
            while (delimiter < line.Length && line[delimiter] == ' ')
            {
                delimiter++;
            }

            if (delimiter >= line.Length || line[delimiter] != language.KeyDelimiter)
            {
                return false;
            }

            keyEnd = close + 1;
        }
        else
        {
            int delimiter = start;
            while (true)
            {
                delimiter = line.IndexOf(language.KeyDelimiter, delimiter);
                if (delimiter < 0)
                {
                    return false;
                }

                // A YAML ':' ends a key only before a space or the end of the line, so "http://x" is not a key.
                if (language.KeyDelimiter == '=' || delimiter + 1 >= line.Length || char.IsWhiteSpace(line[delimiter + 1]))
                {
                    break;
                }

                delimiter++;
            }

            keyEnd = delimiter;
            while (keyEnd > start && char.IsWhiteSpace(line[keyEnd - 1]))
            {
                keyEnd--;
            }

            if (keyEnd <= start)
            {
                return false;
            }
        }

        spans.Add(new SyntaxSpan(start, keyEnd - start, SyntaxKind.Keyword));
        end = keyEnd;
        return true;
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
