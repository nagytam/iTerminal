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
        "x.kt", "x.ts", "x.cs", "x.java", "x.c", "x.cpp", "x.go", "x.rs", "x.js", "x.ps1", "x.py", "x.sh", "x.yml", "x.toml",
        "x.ini", "x.env", "x.properties", "x.sql", "x.html", "x.json", ".gitignore", "x.bat", "x.lua", "x.r", "x.pl",
        "x.php", "x.vb", "x.fs", "x.rb", "Dockerfile", "x.log", "x.cob", "x.f90", "x.asm",
    ];

    /// <summary>The colour test helper: every span of <paramref name="line"/>, as (text, kind).</summary>
    private static List<(string Text, SyntaxKind Kind)> Spans(string file, string line)
    {
        SyntaxLanguage language = SyntaxHighlighter.ForFile(file)!;
        List<SyntaxSpan> spans = [];
        SyntaxHighlighter.Tokenize(language, line, false, spans);
        return spans.Select(s => (line.Substring(s.Start, s.Length), s.Kind)).ToList();
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("x.kt", "const x = \"s\"; // c", "const", "\"s\"", "// c", "C-like")]
    [DataRow("x.ts", "const x = \"s\"; // c", "const", "\"s\"", "// c", "TypeScript")]
    [DataRow("x.cs", "return \"s\"; // c", "return", "\"s\"", "// c", "C#")]
    [DataRow("x.java", "return \"s\"; // c", "return", "\"s\"", "// c", "Java")]
    [DataRow("x.c", "return \"s\"; /* c */", "return", "\"s\"", "/* c */", "C")]
    [DataRow("x.cpp", "return \"s\"; // c", "return", "\"s\"", "// c", "C++")]
    [DataRow("x.go", "func f() { return \"s\" } // c", "func", "\"s\"", "// c", "Go")]
    [DataRow("x.rs", "fn f() { let x = \"s\"; } // c", "fn", "\"s\"", "// c", "Rust")]
    [DataRow("x.js", "const a = 'x'; // c", "const", "'x'", "// c", "JavaScript")]
    [DataRow("x.ps1", "if ($a) { Write-Host 'x' } # c", "Write-Host", "'x'", "# c", "PowerShell")]
    [DataRow("x.py", "def f(): return 'x'  # c", "def", "'x'", "# c", "Python")]
    [DataRow("x.rb", "unless ready; puts 'x'; end # c", "unless", "'x'", "# c", "Ruby")]
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
    [DataRow("x.cmd", "setlocal enabledelayedexpansion", "setlocal", null, null, "Batch")]
    [DataRow("x.lua", "local x = \"s\" -- c", "local", "\"s\"", "-- c", "Lua")]
    [DataRow("x.r", "x <- TRUE # c", "TRUE", null, "# c", "R")]
    [DataRow("x.pl", "my $x = \"s\"; # c", "my", "\"s\"", "# c", "Perl")]
    [DataRow("x.php", "<?php echo \"s\"; // c", "echo", "\"s\"", "// c", "PHP")]
    [DataRow("x.cob", "       MOVE \"s\" TO WS-NAME. *> c", "MOVE", "\"s\"", "*> c", "COBOL")]
    [DataRow("x.f90", "x = 'it''s' ! c", null, "'it''s'", "! c", "FORTRAN")]
    [DataRow("x.asm", "mov rax, 1 ; c", "mov", null, "; c", "Assembly")]
    [DataRow("x.s", "    ldr x0, [sp] // c", "ldr", null, "// c", "Assembly")]
    [DataRow("x.vb", "Dim x As String = \"s\" ' c", "Dim", "\"s\"", "' c", "Visual Basic")]
    [DataRow("x.fs", "let x = \"s\" // c", "let", "\"s\"", "// c", "F#")]
    [DataRow("x.log", "2026-03-01 INFO \"x\" failed", "INFO", "\"x\"", null, "Log")]
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

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("x.php", "$class = $this;", "class")]
    [DataRow("x.php", "$echo = 1;", "echo")]
    [DataRow("x.pl", "my @keys = %values;", "keys")]
    [DataRow("x.pl", "my @keys = %values;", "values")]
    [DataRow("x.ps1", "$process = 1", "process")]
    [DataRow("x.sh", "echo $then", "then")]
    [DataRow("x.rb", "@class = $end", "class")]
    [DataRow("x.rb", "@class = $end", "end")]
    public void AVariableNamedLikeAKeyword_IsNotColouredAsOne(string file, string line, string name)
    {
        CollectionAssert.DoesNotContain(Spans(file, line), (name, SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("x.ts")]
    [DataRow("x.tsx")]
    public void TypeScript_ColoursItsOwnKeywordsInsteadOfCOnlyWords(string file)
    {
        var spans = Spans(file, "type T = unknown; value satisfies T; interface Producer<out T> {} typedef struct");

        CollectionAssert.Contains(spans, ("type", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("unknown", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("satisfies", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("out", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("typedef", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("struct", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TypeScript_BlockCommentContinuesAcrossLines()
    {
        SyntaxLanguage language = SyntaxHighlighter.ForFile("x.ts")!;
        List<SyntaxSpan> spans = [];

        Assert.IsTrue(SyntaxHighlighter.Tokenize(language, "/* start", false, spans));
        spans.Clear();
        Assert.IsFalse(SyntaxHighlighter.Tokenize(language, "end */ type", true, spans));
        CollectionAssert.Contains(spans, new SyntaxSpan(0, 6, SyntaxKind.Comment));
        CollectionAssert.Contains(spans, new SyntaxSpan(7, 4, SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Ruby_UsesCaseSensitiveRubyKeywordsRatherThanPythonKeywords()
    {
        var spans = Spans("x.rb", "unless ready; elsif done; end; elif; None; UNLESS");

        CollectionAssert.Contains(spans, ("unless", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("elsif", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("end", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("elif", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("None", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("UNLESS", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Cobol_IsCaseInsensitive_AndHyphenatedNamesAreOneWord()
    {
        var spans = Spans("x.cob", "perform until ws-eof end-perform ws-eof-flag");

        CollectionAssert.Contains(spans, ("perform", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("until", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("end-perform", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("ws", SyntaxKind.Keyword));
        CollectionAssert.DoesNotContain(spans, ("eof", SyntaxKind.Keyword));
        CollectionAssert.Contains(Spans("x.cob", "MOVE 1 TO x"), ("MOVE", SyntaxKind.Keyword));
        CollectionAssert.Contains(Spans("x.cob", "move 1 to x"), ("move", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Cobol_DoubledQuoteStaysInsideALiteral_AndAStarGreaterThanStartsAComment()
    {
        var spans = Spans("x.cob", "DISPLAY 'it''s' *> say it");

        CollectionAssert.Contains(spans, ("'it''s'", SyntaxKind.String));
        CollectionAssert.Contains(spans, ("*> say it", SyntaxKind.Comment));
        CollectionAssert.DoesNotContain(Spans("x.cob", "COMPUTE x = y * 2"), ("*", SyntaxKind.Comment));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Fortran_IsCaseInsensitive_AndColoursItsNumbersAndLogicalConstants()
    {
        var spans = Spans("x.f90", "IF (done .EQV. .TRUE.) THEN ! go");

        CollectionAssert.Contains(spans, ("IF", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("THEN", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("TRUE", SyntaxKind.Keyword));
        CollectionAssert.Contains(spans, ("! go", SyntaxKind.Comment));
        CollectionAssert.Contains(Spans("x.f90", "x = 1.0d0 + 2.5_dp"), ("1.0d0", SyntaxKind.Number));
        CollectionAssert.Contains(Spans("x.f90", "x = 1.0d0 + 2.5_dp"), ("2.5_dp", SyntaxKind.Number));
        CollectionAssert.DoesNotContain(Spans("x.f90", "x = 1"), ("x", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("x.cob", "COBOL")]
    [DataRow("x.CBL", "COBOL")]
    [DataRow("x.cpy", "COBOL")]
    [DataRow("x.f", "FORTRAN")]
    [DataRow("x.F90", "FORTRAN")]
    [DataRow("x.for", "FORTRAN")]
    [DataRow("x.f77", "FORTRAN")]
    [DataRow("x.asm", "Assembly")]
    [DataRow("x.S", "Assembly")]
    [DataRow("x.nasm", "Assembly")]
    public void CobolAndFortran_HaveTheirOwnExtensions(string file, string expected)
    {
        Assert.AreEqual(expected, SyntaxHighlighter.ForFile(file)!.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("example.cob", "COBOL", "IDENTIFICATION", "PERFORM")]
    [DataRow("example.f90", "FORTRAN", "program", "implicit")]
    [DataRow("example.py", "Python", "import", "def")]
    [DataRow("example.php", "PHP", "class", "function")]
    [DataRow("example.asm", "Assembly", "section", "syscall")]
    public void SampleFiles_ShowTheirKeywordsAndStrings(string sample, string language, string keyword, string otherKeyword)
    {
        string path = iConsole.Test.Common.SampleFiles.Path(sample);
        SyntaxLanguage? found = SyntaxHighlighter.ForFile(path);
        Assert.AreEqual(language, found!.Name);

        var kinds = new List<(string Text, SyntaxKind Kind)>();
        bool inComment = false;
        foreach (string line in File.ReadAllLines(path))
        {
            List<SyntaxSpan> spans = [];
            inComment = SyntaxHighlighter.Tokenize(found, line, inComment, spans);
            kinds.AddRange(spans.Select(s => (line.Substring(s.Start, s.Length), s.Kind)));
        }

        CollectionAssert.Contains(kinds, (keyword, SyntaxKind.Keyword), $"{sample} should colour {keyword}");
        CollectionAssert.Contains(kinds, (otherKeyword, SyntaxKind.Keyword));
        Assert.IsTrue(kinds.Any(k => k.Kind == SyntaxKind.String), $"{sample} should have a string");
        Assert.IsTrue(kinds.Any(k => k.Kind == SyntaxKind.Comment), $"{sample} should have a comment");
        Assert.IsTrue(kinds.Any(k => k.Kind == SyntaxKind.Number), $"{sample} should have a number");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Php_IsCaseInsensitive_LikeThePhpLanguage()
    {
        CollectionAssert.Contains(Spans("x.php", "ECHO \"s\";"), ("ECHO", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Perl_ColoursAPlainHashKeyWordLikeAKeyword_OnlyAfterASigilItIsAName()
    {
        // "keys" is a Perl builtin; after @ it names a variable, so it is not a keyword.
        CollectionAssert.DoesNotContain(Spans("x.pl", "@keys"), ("keys", SyntaxKind.Keyword));
        CollectionAssert.Contains(Spans("x.pl", "keys %h"), ("keys", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Sql_KeepsADoubledQuoteInsideTheString_AndColoursBacktickNames()
    {
        CollectionAssert.Contains(Spans("x.sql", "select 'it''s' from t"), ("'it''s'", SyntaxKind.String));
        CollectionAssert.Contains(Spans("x.sql", "select `a b` from t"), ("`a b`", SyntaxKind.String));
        CollectionAssert.Contains(Spans("x.psql", "WITH r AS (SELECT 1) SELECT * FROM r"), ("WITH", SyntaxKind.Keyword));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void R_ColoursNumbersAndSuffixedNumbers()
    {
        CollectionAssert.Contains(Spans("x.r", "x <- 10L + 2.5"), ("10L", SyntaxKind.Number));
        CollectionAssert.Contains(Spans("x.r", "x <- 2.5"), ("2.5", SyntaxKind.Number));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("x.ps1")]
    [DataRow("x.py")]
    [DataRow("x.go")]
    [DataRow("x.java")]
    [DataRow("x.rb")]
    [DataRow("x.cpp")]
    public void DecimalNumbers_IncludeSignedExponentsButNotFollowingOperators(string file)
    {
        List<string> numbers = Spans(file, "1e-3+4 1.5E+6-7 1e3 12 0x1e-3 1e-foo 1e3e-4")
            .Where(s => s.Kind == SyntaxKind.Number)
            .Select(s => s.Text)
            .ToList();

        CollectionAssert.AreEqual(
            new[] { "1e-3", "4", "1.5E+6", "7", "1e3", "12", "0x1e", "3", "1e", "1e3e", "4" },
            numbers);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Cpp_DecimalDigitSeparatorsDoNotSplitSignedExponents()
    {
        CollectionAssert.Contains(Spans("x.cpp", "1'000e-3"), ("1'000e-3", SyntaxKind.Number));
        CollectionAssert.DoesNotContain(Spans("x.cpp", "0x1'e-3"), ("0x1'e-3", SyntaxKind.Number));
    }
}
