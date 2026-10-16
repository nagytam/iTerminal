namespace iTerminal.Test;

/// <summary>Moving around the file panels with the keyboard, through the real key routing.</summary>
[TestClass]
public class PanelNavigationTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [TestMethod]
    public async Task DownAndUpMoveTheCursor()
    {
        await _app.PressAsync("Home");
        await _app.PressAsync("Down");
        Assert.AreEqual("alpha", _app.FocusedEntryName);

        await _app.PressAsync("Down");
        Assert.AreEqual("beta", _app.FocusedEntryName);

        await _app.PressAsync("Up");
        Assert.AreEqual("alpha", _app.FocusedEntryName);
    }

    [TestMethod]
    public async Task HomeAndEndJumpToTheEnds()
    {
        await _app.PressAsync("End");
        Assert.AreEqual("readme.txt", _app.FocusedEntryName);

        await _app.PressAsync("Home");
        Assert.AreEqual("..", _app.FocusedModel!.GetFocusedEntry()!.DisplayName.Trim());
    }

    [TestMethod]
    public async Task TheCursorRowIsDrawnDifferently()
    {
        await _app.SelectEntryAsync("notes.md");

        // The cursor has moved in the model; the repaint is a separate job. A snapshot taken once the
        // screen merely stops changing can still be the frame from before the move on a loaded machine
        // (the old cursor row highlighted, notes.md not), so wait for the frame that draws it.
        var screen = await _app.Driver.WaitUntilAsync(
            s => s.BackgroundAt(5, s.RowOf("notes.md")) != s.BackgroundAt(5, s.RowOf("readme.txt")),
            "the cursor row to have a background of its own");
        var cursorRow = screen.RowOf("notes.md");
        var otherRow = screen.RowOf("readme.txt");
        Assert.AreNotEqual(screen.BackgroundAt(5, otherRow), screen.BackgroundAt(5, cursorRow),
            "the cursor row has a background of its own" + Environment.NewLine + screen.Dump());
    }

    [TestMethod]
    public async Task EnterOpensAFolder()
    {
        await _app.SelectEntryAsync("alpha");

        await _app.PressAsync("Enter");

        await _app.WaitForFocusedFolderAsync(Path.Combine(_app.LeftRoot, "alpha"));
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("inside_alpha.txt", s), "the folder's file to be listed");
        StringAssert.Contains(_app.Screen.Line(1), Path.Combine("left", "alpha"));
    }

    [TestMethod]
    public async Task EnterOnTheParentEntryGoesBackAndKeepsTheCursorOnTheFolderLeft()
    {
        await _app.SelectEntryAsync("alpha");
        await _app.PressAsync("Enter");
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("inside_alpha.txt", s), "alpha to open");

        await _app.PressAsync("Home");
        await _app.PressAsync("Enter");

        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);
        await _app.WaitUntilAsync(() => _app.FocusedEntryName == "alpha", "the cursor to be back on the folder that was left");
    }

    [TestMethod]
    public async Task CtrlPageUpGoesToTheParentFolder()
    {
        await _app.SelectEntryAsync("beta");
        await _app.PressAsync("Enter");
        await _app.WaitForFocusedFolderAsync(Path.Combine(_app.LeftRoot, "beta"));

        await _app.PressAsync("Ctrl+PgUp");

        await _app.WaitForFocusedFolderAsync(_app.LeftRoot);
        await _app.WaitUntilAsync(() => _app.FocusedEntryName == "beta", "the cursor to land on the folder that was left");
    }

    [TestMethod]
    public async Task TabGivesTheKeyboardToTheOtherPanel()
    {
        await _app.PressAsync("Tab");

        Assert.AreEqual(Core.Services.ContentModelService.Right, _app.ContentModels.FocusedPanel);
        await _app.SelectEntryAsync("right_only.txt");
        Assert.AreEqual("right_only.txt", _app.FocusedEntryName, "the arrows now move the right panel's cursor");
    }

    /// <summary>
    /// Tab goes round the panels and the bar below them, and comes back to where it started — it
    /// never strands the keyboard somewhere Tab cannot leave.
    /// </summary>
    [TestMethod]
    public async Task TabComesBackRoundToTheLeftPanel()
    {
        for (var presses = 1; presses <= 6; presses++)
        {
            await _app.PressAsync("Tab");
            if (presses > 1 && _app.ContentModels.FocusedPanel == Core.Services.ContentModelService.Left)
            {
                return;
            }
        }

        Assert.Fail("Six presses of Tab never came back to the left panel." + Environment.NewLine + _app.Screen.Dump());
    }

    [TestMethod]
    public async Task ShiftTabGoesBackToThePanelBefore()
    {
        await _app.PressAsync("Tab");
        Assert.AreEqual(Core.Services.ContentModelService.Right, _app.ContentModels.FocusedPanel);

        await _app.PressAsync("Shift+Tab");

        Assert.AreEqual(Core.Services.ContentModelService.Left, _app.ContentModels.FocusedPanel);
    }

    [TestMethod]
    public async Task AltLetterStartsTypeToSearch()
    {
        await _app.PressAsync("Alt+r");

        var screen = await _app.WaitForTextAsync("Filter: r");
        Assert.AreEqual("readme.txt", _app.FocusedEntryName, "the cursor jumps to the first match");
        Assert.IsTrue(_app.LeftPanelLists("readme.txt", screen));
    }

    [TestMethod]
    public async Task TypingMoreNarrowsTheSearchAndEscClearsIt()
    {
        await _app.PressAsync("Alt+n");
        await _app.TypeAsync("ot");
        await _app.WaitForTextAsync("Filter: not");
        Assert.AreEqual("notes.md", _app.FocusedEntryName);

        await _app.PressAsync("Esc");

        await _app.WaitForTextGoneAsync("Filter:");
        await _app.WaitUntilAsync(s => s.Contains(" 5 entries "), "the full listing back");
    }

    [TestMethod]
    public async Task BackspaceShortensTheSearch()
    {
        await _app.PressAsync("Alt+r");
        await _app.TypeAsync("x");
        await _app.WaitForTextAsync("Filter: rx");

        await _app.PressAsync("Backspace");

        await _app.WaitForTextAsync("Filter: r ");
    }

    [TestMethod]
    public async Task ANewFileOnDiskAppearsWithoutARefresh()
    {
        File.WriteAllText(Path.Combine(_app.LeftRoot, "dropped_in.txt"), "new");

        await _app.WaitUntilAsync(s => _app.LeftPanelLists("dropped_in.txt", s), "the folder watcher to pick the file up");
    }

    [TestMethod]
    public async Task CtrlRRefreshesThePanel()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+R"));

        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s), "the listing to come back");
    }
}
