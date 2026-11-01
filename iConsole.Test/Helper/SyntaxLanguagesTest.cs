using iConsole.Helper;
using iConsole.Services;

namespace iConsole.Test.Helper;

/// <summary>Each built-in language colours its own keywords, strings, comments and numbers, and nothing else.</summary>
[TestClass]
public class SyntaxLanguagesTest
{
    /// <summary>One file per built-in language, so the coverage test can tell whether a new language has no sample.</summary>
    private static readonly string[] SampleFiles =
    [
        "x.ts", "x.cs", "x.java", "x.c", "x.cpp", "x.go", "x.rs", "x.js", "x.ps1", "x.py", "x.sh", "x.yml", "x.toml",
        "x.ini", "x.env", "x.properties", "x.sql", "x.html", "x.json", ".gitignore", "x.bat", "x.lua", "x.r", "x.pl",
        "x.php", "x.vb", "x.fs", "Dockerfile",
    ];

    /// <summary>The colour test helper: every span of <paramref name="line"/>, as (text, kind).</summary>
    private static List<(string Text, SyntaxKind Kind)> Spans(string file, string line)
    {
        SyntaxLanguage language = SyntaxHighlighter.ForFile(file)!;
        List<SyntaxSpan> spans = [];
        SyntaxHighlighter.Tokenize(language, line, false, spans);
        return spans.Select(s => (line.Substring(s.Start, s.Length), s.Kind)).ToList();
    }

    [TestMethod]
    public void EveryBuiltInLanguage_HasASampleFile()
    {
        var service = new SyntaxHighlightingService();
        HashSet<string> covered = SampleFiles.Select(f => service.FindByFile(f)!.Name).ToHashSet();

        CollectionAssert.AreEquivalent(
            service.Languages.Select(l => l.Name).ToList(),
            covered.ToList(),
            "a new language needs a sample file in SampleFiles and a row in ColoursItsOwnRules");
    }

    [TestMethod]
    [DataRow("x.ts", "const x = \"s\"; // c", "const", "\"s\"", "// c", "C-like")]
    [DataRow("x.cs", "return \"s\"; // c", "return", "\"s\"", "// c", "C#")]
    [DataRow("x.java", "return \"s\"; // c", "return", "\"s\"", "// c", "Java")]
    [DataRow("x.c", "return \"s\"; /* c */", "return", "\"s\"", "/* c */", "C")]
    [DataRow("x.cpp", "return \"s\"; // c", "return", "\"s\"", "// c", "C++")]
    [DataRow("x.go", "func f() { return \"s\" } // c", "func", "\"s\"", "// c", "Go")]
    [DataRow("x.rs", "fn f() { let x = \"s\"; } // c", "fn", "\"s\"", "// c", "Rust")]
    [DataRow("x.js", "const a = 'x'; // c", "const", "'x'", "// c", "JavaScript")]
    [DataRow("x.ps1", "if ($a) { Write-Host 'x' } # c", "Write-Host", "'x'", "# c", "PowerShell")]
    [DataRow("x.py", "def f(): return 'x'  # c", "def", "'x'", "# c", "Python")]
    [DataRow("x.sh", "if [ -f x ]; then echo \"x\"; fi # c", "then", "\"x\"", "# c", "Shell")]
    [DataRow("x.yml", "name: \"x\" # c", "name", "\"x\"", "# c", "YAML")]
    [DataRow("x.toml", "name = \"x\" # c", "name", "\"x\"", "# c", "TOML")]
    [DataRow("x.ini", "key = \"x\" ; c", "key", "\"x\"", "; c", "INI")]
    [DataRow("x.env", "KEY=value", "KEY", null, null, "Dotenv")]
    [DataRow("x.properties", "key=value", "key", null, null, "Properties")]
    [DataRow("x.sql", "SELECT \"a\" FROM t -- c", "SELECT", "\"a\"", "-- c", "SQL")]
    [DataRow("x.html", "<a href=\"x\"><!-- c --></a>", null, "\"x\"", "<!-- c -->", "Markup")]
    [DataRow("x.json", "{\"a\": true}", "true", "\"a\"", null, "JSON")]
    [DataRow(".gitignore", "# c", null, null, "# c", "Config")]
    [DataRow("x.bat", "echo \"x\"", "echo", "\"x\"", null, "Batch")]
    [DataRow("x.bat", "rem c", null, null, "rem c", "Batch")]
    [DataRow("x.lua", "local x = \"s\" -- c", "local", "\"s\"", "-- c", "Lua")]
    [DataRow("x.r", "x <- TRUE # c", "TRUE", null, "# c", "R")]
    [DataRow("x.pl", "my $x = \"s\"; # c", "my", "\"s\"", "# c", "Perl")]
    [DataRow("x.php", "<?php echo \"s\"; // c", "echo", "\"s\"", "// c", "PHP")]
    [DataRow("x.vb", "Dim x As String = \"s\" ' c", "Dim", "\"s\"", "' c", "Visual Basic")]
    [DataRow("x.fs", "let x = \"s\" // c", "let", "\"s\"", "// c", "F#")]
    [DataRow("Dockerfile", "RUN echo \"x\" # c", "RUN", "\"x\"", "# c", "Build file")]
    public void ColoursItsOwnRules(string file, string line, string? keyword, string? text, string? comment, string language)
    {
        var spans = Spans(file, line);

        Assert.AreEqual(language, SyntaxHighlighter.ForFile(file)!.Name);
        if (keyword is not null)
        {
            CollectionAssert.Contains(spans, (keyword, SyntaxKind.Keyword));
        }

        if (text is not null)
        {
            CollectionAssert.Contains(spans, (text, SyntaxKind.String));
        }

        if (comment is not null)
        {
            CollectionAssert.Contains(spans, (comment, SyntaxKind.Comment));
        }
    }

    [TestMethod]
    [DataRow("x.php", "$class = $this;", "class")]
    [DataRow("x.php", "$echo = 1;", "echo")]
    [DataRow("x.pl", "my @keys = %values;", "keys")]
    [DataRow("x.pl", "my @keys = %values;", "values")]
    [DataRow("x.ps1", "$process = 1", "process")]
    [DataRow("x.sh", "echo $then", "then")]
    public void AVariableNamedLikeAKeyword_IsNotColouredAsOne(string file, string line, string name)
    {
        CollectionAssert.DoesNotContain(Spans(file, line), (name, SyntaxKind.Keyword));
    }

    [TestMethod]
    public void Php_IsCaseInsensitive_LikeThePhpLanguage()
    {
        CollectionAssert.Contains(Spans("x.php", "ECHO \"s\";"), ("ECHO", SyntaxKind.Keyword));
    }

    [TestMethod]
    public void Perl_ColoursAPlainHashKeyWordLikeAKeyword_OnlyAfterASigilItIsAName()
    {
        // "keys" is a Perl builtin; after @ it names a variable, so it is not a keyword.
        CollectionAssert.DoesNotContain(Spans("x.pl", "@keys"), ("keys", SyntaxKind.Keyword));
        CollectionAssert.Contains(Spans("x.pl", "keys %h"), ("keys", SyntaxKind.Keyword));
    }

    [TestMethod]
    public void Sql_KeepsADoubledQuoteInsideTheString_AndColoursBacktickNames()
    {
        CollectionAssert.Contains(Spans("x.sql", "select 'it''s' from t"), ("'it''s'", SyntaxKind.String));
        CollectionAssert.Contains(Spans("x.sql", "select `a b` from t"), ("`a b`", SyntaxKind.String));
        CollectionAssert.Contains(Spans("x.psql", "WITH r AS (SELECT 1) SELECT * FROM r"), ("WITH", SyntaxKind.Keyword));
    }

    [TestMethod]
    public void R_ColoursNumbersAndSuffixedNumbers()
    {
        CollectionAssert.Contains(Spans("x.r", "x <- 10L + 2.5"), ("10L", SyntaxKind.Number));
        CollectionAssert.Contains(Spans("x.r", "x <- 2.5"), ("2.5", SyntaxKind.Number));
    }
}
