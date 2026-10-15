namespace iTerminal.Test;

/// <summary>What a user sees when the app has just started: a first run on a fresh data folder.</summary>
[TestClass]
public class StartupTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [TestMethod]
    public void TheMenusAreOnTheTopRow()
    {
        StringAssert.StartsWith(_app.Screen.Line(0), "File Edit Navigate Tools Help");
    }

    [TestMethod]
    public void TheKeyBarIsOnTheBottomRow()
    {
        var bottom = _app.Screen.Line(_app.Screen.Height - 1);

        StringAssert.StartsWith(bottom, "[F1-Help][F2-Terminal][F3-View][F4-Edit][F5-Copy][F6-Rename][F7-Find][F8-Delete]");
        StringAssert.Contains(bottom, "[F10-Quit]");
    }

    [TestMethod]
    public void EachPanelListsItsFolder()
    {
        var screen = _app.Screen;

        foreach (var name in new[] { "..", "alpha", "beta", "notes.md", "readme.txt" })
        {
            Assert.IsTrue(_app.LeftPanelLists(name, screen), $"left panel lists {name}{Environment.NewLine}{screen.Dump()}");
        }
        foreach (var name in new[] { "..", "target", "right_only.txt" })
        {
            Assert.IsTrue(_app.RightPanelLists(name, screen), $"right panel lists {name}{Environment.NewLine}{screen.Dump()}");
        }
        Assert.IsFalse(_app.LeftPanelLists("right_only.txt", screen));
    }

    [TestMethod]
    public void FoldersAreListedBeforeFiles()
    {
        var rows = _app.LeftPanel().Select(row => row.Length > 1 ? row[1..].Split(' ')[0] : string.Empty).ToList();

        Assert.IsTrue(rows.IndexOf("beta") < rows.IndexOf("notes.md"), string.Join(Environment.NewLine, rows));
    }

    [TestMethod]
    public void EachPanelIsTitledWithItsFolder()
    {
        var top = _app.Screen.Line(1);

        StringAssert.Contains(top, Path.GetFileName(_app.LeftRoot));
        StringAssert.Contains(top, Path.GetFileName(_app.RightRoot));
        Assert.IsTrue(top.IndexOf("left", StringComparison.Ordinal) < top.IndexOf("right", StringComparison.Ordinal),
            "left panel on the left");
    }

    /// <summary>The count used to be joined to its word with a border character: "══5═entries══".</summary>
    [TestMethod]
    public async Task EachPanelFooterCountsItsEntries()
    {
        var screen = await _app.WaitUntilAsync(s => s.Contains(" 5 entries ") && s.Contains(" 3 entries "), "both footers to count their entries");

        Assert.IsFalse(screen.Contains("═entries"), screen.Dump());
    }

    [TestMethod]
    public void TheLeftPanelHasTheKeyboard()
    {
        Assert.AreEqual(Core.Services.ContentModelService.Left, _app.ContentModels.FocusedPanel);
        Assert.AreEqual("..", _app.FocusedModel!.FilteredEntries[0].DisplayName.Trim());
    }

    [TestMethod]
    public async Task TheSplashScreenIsGoneAndNoWindowIsLeftBehind()
    {
        Assert.IsFalse(_app.Screen.Contains("Welcome to iTerminal"));
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public void AFirstRunCreatesItsDatabaseInTheConfiguredDataFolder()
    {
        Assert.IsTrue(File.Exists(Path.Combine(_app.DataDirectory, "iTerminal.sqlite")),
            "the database belongs in the folder the configuration names, not in %APPDATA%");
    }

    [TestMethod]
    public void NoKeyIsNeededToStart()
    {
        Assert.AreEqual(0, _app.Driver.Console.BeepCount, "nothing beeped while starting");
    }
}
