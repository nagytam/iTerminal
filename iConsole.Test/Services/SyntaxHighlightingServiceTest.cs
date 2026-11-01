using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Markdown;
using iConsole.Services;
using iConsole.Test.Core;

namespace iConsole.Test.Services;

[TestClass]
public class SyntaxHighlightingServiceTest
{
    private readonly TestThemeService _theme = new();

    [TestMethod]
    public void FindByFile_FallsBackToTheBuiltInLanguages()
    {
        var service = new SyntaxHighlightingService();

        Assert.AreEqual("C#", service.FindByFile("/src/Program.cs")?.Name);
        Assert.AreEqual("Build file", service.FindByFile("Dockerfile")?.Name);
        Assert.IsNull(service.FindByFile("notes.txt"));
        Assert.IsNull(service.FindByFile(null));
    }

    [TestMethod]
    [DataRow("csharp", "C#")]
    [DataRow("C#", "C#")]
    [DataRow("c++", "C++")]
    [DataRow("py", "Python")]
    [DataRow("Python", "Python")]
    [DataRow("PowerShell", "PowerShell")]
    [DataRow("pwsh", "PowerShell")]
    [DataRow("bash", "Shell")]
    [DataRow("yaml", "YAML")]
    [DataRow("toml", "TOML")]
    [DataRow("ini", "INI")]
    [DataRow("env", "Dotenv")]
    [DataRow(".json", "JSON")]
    [DataRow("SQL", "SQL")]
    [DataRow("fsharp", "F#")]
    [DataRow("dockerfile", "Build file")]
    [DataRow("Lua", "Lua")]
    public void FindByName_UnderstandsFenceNames(string name, string expected)
    {
        Assert.AreEqual(expected, new SyntaxHighlightingService().FindByName(name)?.Name);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("text")]
    [DataRow("no-such-language")]
    public void FindByName_ReturnsNullForUnknownNames(string name)
    {
        Assert.IsNull(new SyntaxHighlightingService().FindByName(name));
    }

    [TestMethod]
    public void Register_AddsALanguageByExtensionFileNameAndAlias()
    {
        var service = new SyntaxHighlightingService();
        var zig = new SyntaxLanguage("Zig", ["const", "fn", "pub"], ["//"]);

        service.Register(zig, extensions: ["zig", ".zon"], fileNames: ["build.zig.zon"], aliases: ["ziglang"]);

        Assert.AreSame(zig, service.FindByFile("main.zig"));
        Assert.AreSame(zig, service.FindByFile("x.ZON"));
        Assert.AreSame(zig, service.FindByFile(@"C:\proj\build.zig.zon"));
        Assert.AreSame(zig, service.FindByName("ziglang"));
        Assert.AreSame(zig, service.FindByName("zig"));
        CollectionAssert.Contains(service.Languages.ToList(), zig);
    }

    [TestMethod]
    public void Register_LetsAnApplicationOverrideABuiltInExtension()
    {
        var service = new SyntaxHighlightingService();
        var mine = new SyntaxLanguage("Mine", ["x"], ["#"]);

        service.Register(mine, extensions: [".cs"]);

        Assert.AreSame(mine, service.FindByFile("a.cs"));
        Assert.AreEqual("C#", new SyntaxHighlightingService().FindByFile("a.cs")?.Name);
    }

    [TestMethod]
    public void Tokenize_MatchesTheStaticHighlighter()
    {
        var service = new SyntaxHighlightingService();
        SyntaxLanguage language = service.FindByFile("a.cs")!;
        List<SyntaxSpan> viaService = [];
        List<SyntaxSpan> viaStatic = [];

        service.Tokenize(language, "return \"x\" + 1; // done", false, viaService);
        SyntaxHighlighter.Tokenize(language, "return \"x\" + 1; // done", false, viaStatic);

        CollectionAssert.AreEqual(viaStatic, viaService);
        Assert.IsTrue(viaService.Any(s => s.Kind == SyntaxKind.Keyword));
    }

    [TestMethod]
    public void Palette_MapsEachKind_AndPlainTextKeepsTheGivenColour()
    {
        SyntaxPalette palette = SyntaxPalette.Default;

        Assert.AreEqual(Color.Cyan, palette.ColorOf(SyntaxKind.Keyword, Color.White));
        Assert.AreEqual(Color.Green, palette.ColorOf(SyntaxKind.String, Color.White));
        Assert.AreEqual(Color.Gray, palette.ColorOf(SyntaxKind.Comment, Color.White));
        Assert.AreEqual(Color.Magenta, palette.ColorOf(SyntaxKind.Number, Color.White));
        Assert.AreEqual(Color.Yellow, palette.ColorOf(SyntaxKind.Plain, Color.Yellow));
    }

    [TestMethod]
    public void TextViewer_FollowsThePaletteOfItsService()
    {
        var service = new SyntaxHighlightingService
        {
            Palette = new SyntaxPalette(Color.Red, Color.Green, Color.Gray, Color.Magenta),
        };
        Window parent = new(_theme) { ViewSize = new Size(30, 3) };
        var viewer = new TextViewerControl(false, _theme)
        {
            Parent = parent,
            Position = new Position(0, 0),
            ViewSize = new Size(30, 3),
            Highlighting = service,
            Syntax = service.FindByFile("a.cs"),
        };
        viewer.TextModel.Lines.Add("x");
        viewer.TextModel.Lines.Add("return 1");
        viewer.OnInitializeLayout();
        var screen = new ScreenBuffer(30, 3);

        viewer.Draw(screen);

        Assert.AreEqual(Color.Red, screen.ForegroundAt(0, 1));
    }

    [TestMethod]
    public void MarkdownParser_KeepsTheFenceLanguage()
    {
        IReadOnlyList<MarkdownBlock> blocks = MarkdownParser.Parse("```csharp title=x\nvar a = 1;\n```\n~~~\nplain\n~~~");

        Assert.AreEqual("csharp", blocks[0].Language);
        Assert.AreEqual(string.Empty, blocks[1].Language);
    }

    private MarkdownViewerControl Viewer(string markdown, ISyntaxHighlightingService? service = null)
    {
        var viewer = new MarkdownViewerControl(_theme) { Position = new Position(0, 0), ViewSize = new Size(30, 8) };
        if (service is not null)
        {
            viewer.Highlighting = service;
        }

        viewer.Markdown = markdown;
        return viewer;
    }

    [TestMethod]
    public void MarkdownViewer_ColoursACodeFenceByItsLanguage()
    {
        MarkdownViewerControl viewer = Viewer("```csharp\nreturn \"s\" + 1;\n```");
        var screen = new ScreenBuffer(30, 8);

        viewer.Draw(screen);
        viewer.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(0, 0));
        Assert.AreEqual(Color.Green, screen.ForegroundAt(7, 0));
        Assert.AreEqual(Color.Magenta, screen.ForegroundAt(13, 0));
        Assert.AreEqual(' ', screen.CharAt(29, 0));
    }

    [TestMethod]
    public void MarkdownViewer_LeavesAFenceWithoutALanguageInTheCodeColour()
    {
        var scheme = new ColorScheme();
        MarkdownViewerControl viewer = Viewer("```\nreturn 1\n```");
        var screen = new ScreenBuffer(30, 8);

        viewer.Draw(screen);

        Assert.AreEqual(_theme.CurrentColorScheme.DisabledSelectedForeground, screen.ForegroundAt(0, 0));
        Assert.IsNotNull(scheme);
    }

    [TestMethod]
    public void MarkdownViewer_FollowsABlockCommentAcrossCodeLines()
    {
        MarkdownViewerControl viewer = Viewer("```c\n/* start\nreturn\nend */ return\n```");
        var screen = new ScreenBuffer(30, 8);

        viewer.Draw(screen);

        Assert.AreEqual(Color.Gray, screen.ForegroundAt(0, 1), "'return' inside the comment stays a comment");
        Assert.AreEqual(Color.Gray, screen.ForegroundAt(0, 2));
        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(7, 2), "'return' after the comment ends is a keyword");
    }

    [TestMethod]
    public void MarkdownViewer_UsesALanguageRegisteredWithTheService()
    {
        var service = new SyntaxHighlightingService();
        service.Register(new SyntaxLanguage("Zig", ["pub"], ["//"]), aliases: ["zig"]);
        MarkdownViewerControl viewer = Viewer("```zig\npub x\n```", service);
        var screen = new ScreenBuffer(30, 8);

        viewer.Draw(screen);

        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(0, 0));
    }
}
