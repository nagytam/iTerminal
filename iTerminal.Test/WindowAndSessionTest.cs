namespace iTerminal.Test;

/// <summary>The terminal window around the app, quitting, and what a restart brings back.</summary>
[TestClass]
public class WindowAndSessionTest
{
    [TestMethod]
    public async Task ResizingTheTerminalReLaysOutBothPanels()
    {
        await using var app = await TerminalApp.StartAsync();

        await app.Driver.ResizeAsync(100, 30);

        var screen = await app.WaitUntilAsync(
            s => s.Line(29).StartsWith("[F1-", StringComparison.Ordinal) && s.Line(29).Contains("[F10-") && s.CharAt(50, 1) == '╔',
            "the key bar on the new last row and the right panel starting at the new middle");
        Assert.IsTrue(app.LeftPanelLists("readme.txt", screen));
        Assert.IsTrue(app.RightPanelLists("right_only.txt", screen));
        await app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task ASmallTerminalStillShowsThePanelsAndTheKeyBar()
    {
        await using var app = await TerminalApp.StartAsync(width: 80, height: 24);

        // The labels are shortened to fit; the keys themselves must all still be there.
        var screen = await app.WaitUntilAsync(
            s => s.Line(23).StartsWith("[F1-", StringComparison.Ordinal) && s.Line(23).Contains("[F8-") && s.Line(23).Contains("[F10-"),
            "the key bar on row 23 with every key");
        Assert.IsTrue(app.LeftPanelLists("readme.txt", screen), screen.Dump());
    }

    /// <summary>
    /// Below about 100 columns the key bar used to re-lay itself out on every frame, so the renderer
    /// never went idle — and lists that fill on an idle pass never filled: the disk manager said
    /// "No drives found" on a machine with three.
    /// </summary>
    [TestMethod]
    public async Task ASmallTerminalStillFillsDialogLists()
    {
        await using var app = await TerminalApp.StartAsync(width: 80, height: 25);

        await app.PressAsync("Alt+F9");

        await app.WaitForTextAsync("memdrive://");
    }

    [TestMethod]
    public async Task F10Quits()
    {
        await using var app = await TerminalApp.StartAsync();

        await app.PressAsync("F10");

        await app.Driver.WaitForExitAsync();
    }

    [TestMethod]
    public async Task CtrlCQuits()
    {
        await using var app = await TerminalApp.StartAsync();

        await app.PressAsync("Ctrl+C");

        await app.Driver.WaitForExitAsync();
    }

    [TestMethod]
    public async Task ARestartReopensTheFolderThePanelWasIn()
    {
        var app = await TerminalApp.StartAsync();
        try
        {
            await app.SelectEntryAsync("alpha");
            await app.PressAsync("Enter");
            await app.WaitForFocusedFolderAsync(Path.Combine(app.LeftRoot, "alpha"));

            app = await app.RestartAsync();

            await app.WaitUntilAsync(s => app.LeftPanelLists("inside_alpha.txt", s), "the left panel to come back in alpha");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task ARestartKeepsTheChosenTheme()
    {
        var app = await TerminalApp.StartAsync();
        try
        {
            var themeService = (iConsole.Services.ThemeService)app.Driver.GetRequiredService<iConsole.Services.IThemeService>();
            await app.PressAsync("Alt+F6");
            await app.WaitUntilAsync(() => app.Driver.FocusedModal is Core.Dialogs.ThemeManagerDialog, "the theme manager");
            await app.PressAsync("Down");
            await app.PressAsync("Enter");
            await app.WaitUntilAsync(() => themeService.Theme != iConsole.Data.Theme.Default, "a new theme to be applied");
            var chosen = themeService.Theme;
            await app.WaitForTextAsync("theme applied");
            await app.PressAsync("Esc");
            await app.WaitForNoDialogAsync();

            app = await app.RestartAsync();

            Assert.AreEqual(chosen, ((iConsole.Services.ThemeService)app.Driver.GetRequiredService<iConsole.Services.IThemeService>()).Theme,
                "the theme picked last time is the one the app starts in");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
