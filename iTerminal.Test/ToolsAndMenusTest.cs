namespace iTerminal.Test;

/// <summary>The menu bar and the small tools, used the way a person uses them.</summary>
[TestClass]
public class ToolsAndMenusTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCalculatorAddsTypedNumbers()
    {
        await _app.PressAsync("Ctrl+A");
        await _app.WaitForTextAsync(" Calculator ");

        await _app.TypeAsync("12+30");
        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(s => s.Lines.Any(line => line.Contains(" 42║", StringComparison.Ordinal)),
            "the display to show 42, right-aligned against the frame");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlEOpensTheUnitConverterNotTheEditMenu()
    {
        Assert.IsTrue(await _app.PressAsync("Ctrl+E"));

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.UnitConverter.UnitConverterDialog,
            "Ctrl+E — the key on the converter's own button and in the Tools menu — to open the converter");
    }

    /// <summary>On Linux the Virtual machine manager is listed directly under Calculator in the Tools menu; elsewhere it is not listed at all.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheVirtualMachineManagerIsListedRightAfterCalculator()
    {
        await _app.PressAsync("Ctrl+F");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar, "the menu");
        await _app.PressSequenceAsync("Right", "Right", "Right");
        await _app.WaitForTextAsync("Calculator");

        if (OperatingSystem.IsLinux())
        {
            await _app.WaitUntilAsync(
                s => s.Lines.Zip(s.Lines.Skip(1), (row, next) => (row, next))
                    .Any(pair => pair.row.Contains("Calculator", StringComparison.Ordinal)
                        && pair.next.Contains("Virtual machine manager", StringComparison.Ordinal)),
                "Virtual machine manager on the row directly below Calculator");
        }
        else
        {
            await _app.WaitUntilAsync(
                s => !s.Lines.Any(l => l.Contains("Virtual machine manager", StringComparison.Ordinal)),
                "no Virtual machine manager entry off Linux");
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheToolsMenuStaysOnAShortScreenAndScrollsToItsLastEntry()
    {
        await using TerminalApp small = await TerminalApp.StartAsync(width: 80, height: 24);
        await small.PressAsync("Ctrl+F");
        await small.WaitUntilAsync(() => small.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar, "the menu");
        await small.PressSequenceAsync("Right", "Right", "Right");
        await small.WaitForTextAsync("Calculator");

        // Its entries are more than the screen has rows: the bottom border and a "more below" mark must show.
        await small.WaitUntilAsync(
            s => s.Lines[s.Height - 1].Contains("╚", StringComparison.Ordinal) && s.Lines.Any(l => l.Contains('▼')),
            "the drop-down to end on its bottom border inside the screen, marked as continuing");

        // Up from the first entry wraps to the last one, whatever the menu's length.
        await small.PressAsync("Up");

        await small.WaitForTextAsync("Remote connections");
        await small.WaitUntilAsync(s => s.Lines.Any(l => l.Contains('▲')), "a mark that entries are hidden above");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ARemoteConnectionsDialogWrittenFor84ColumnsKeepsItsRightBorderOn80()
    {
        await using TerminalApp small = await TerminalApp.StartAsync(width: 80, height: 24);

        await small.PressAsync("Alt+F2");

        await small.WaitUntilAsync(
            s => s.Lines.Any(l => l.Contains("Remote connections", StringComparison.Ordinal) && l.TrimEnd().EndsWith('╗')),
            "the dialog's top border to end in a corner inside the screen");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheViewerStatusBarKeepsOnlyWhatFitsOn80Columns()
    {
        await using TerminalApp small = await TerminalApp.StartAsync(width: 80, height: 24);
        await small.SelectEntryAsync("readme.txt");

        await small.PressAsync("F3");

        await small.WaitForTextAsync("Hello from the left panel.");
        await small.WaitUntilAsync(
            s => s.Lines.Any(l => l.Contains("[F3-FindNext][F7-Search]", StringComparison.Ordinal) && l.Contains("X: 0/", StringComparison.Ordinal)),
            "the buttons that fit to be stacked from the left, with nothing drawn over them");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task APanelOn60ColumnsDropsDateAndTimeSoNamesStayReadable()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.WaitForTextAsync("readme.txt");
        Assert.IsFalse(narrow.Screen.Lines.Any(l => l.Contains("2026-", StringComparison.Ordinal) && l.Contains("readme", StringComparison.Ordinal)),
            narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlFOpensTheFileMenu()
    {
        await _app.PressAsync("Ctrl+F");

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar, "the menu to take the keyboard");
        await _app.WaitForTextAsync("Create Folder");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task RightFromTheFileMenuReachesTheEditMenu()
    {
        await _app.PressAsync("Ctrl+F");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar, "the File menu");

        await _app.PressAsync("Right");

        await _app.WaitForTextAsync("Shift+F6", TimeSpan.FromSeconds(5));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EscClosesTheMenuAndLeavesNothingBehind()
    {
        await _app.PressAsync("Ctrl+F");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar, "the File menu");

        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheFileViewerShowsTheFile()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("F3");

        await _app.WaitForTextAsync("Hello from the left panel.");
        await _app.WaitForTextAsync("Second line.");
        await _app.PressAsync("Esc");
        await _app.WaitForTextGoneAsync("Hello from the left panel.");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F4OpensAMarkdownFileInTheEditor()
    {
        await _app.SelectEntryAsync("notes.md");

        Assert.IsTrue(await _app.PressAsync("F4"), "F4 on a .md file used to do nothing but beep");

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.TextFileEditorDialog, "the editor to open");
        await _app.WaitForTextAsync("# Notes");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlShiftOTogglesTheFullScreenCommandLog()
    {
        await _app.PressAsync("Ctrl+Shift+O");

        await _app.WaitUntilAsync(s => !s.Contains("readme.txt"), "the panels to make way for the terminal");

        await _app.PressAsync("Ctrl+Shift+O");

        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s), "the panels to come back");
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlOHandsTheTerminalToAShellAndTakesItBack()
    {
        var shell = _app.Driver.GetRequiredService<SandboxServices.Shell>();
        var ownership = _app.Driver.GetRequiredService<iConsole.Services.ConsoleOwnership>();

        await _app.PressAsync("Ctrl+O");
        await _app.WaitUntilAsync(() => ownership.IsLent, "the terminal to be lent to the shell");
        Assert.AreEqual(_app.LeftRoot, shell.LastLaunch!.WorkingDirectory?.TrimEnd(Path.DirectorySeparatorChar), "the shell starts in the focused panel's folder");

        // The shell has the screen: the app's repaints — the clock ticks every second — must not
        // draw over it, and its keys are the shell's.
        await _app.WaitForTextAsync(SandboxServices.Shell.Prompt);
        await Task.Delay(1500);
        StringAssert.Contains(_app.Screen.Lines[0], SandboxServices.Shell.Prompt, "the renderer drew over the shell");

        shell.Exit();
        await _app.WaitUntilAsync(() => !ownership.IsLent, "the terminal to come back");

        // Everything is repainted — not diffed against a frame the shell has overwritten.
        await _app.WaitForTextGoneAsync(SandboxServices.Shell.Prompt);
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s) && s.Lines[0].StartsWith("File Edit"), "the panels and the menu to be back");

        // And the keyboard is the app's again.
        await _app.PressAsync("Down");
        await _app.WaitUntilAsync(() => _app.FocusedEntryName is not null and not "..", "the cursor to move");
    }
}
