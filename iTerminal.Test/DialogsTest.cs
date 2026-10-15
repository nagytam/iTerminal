using iConsole.Controls.PopUpWindows;

namespace iTerminal.Test;

/// <summary>
/// Every dialog the app offers, opened by its own key from the file panels: it must come up on top
/// with its title in its frame, take the keyboard, and close with Esc leaving nothing behind — no
/// stray frame, no part of it still drawn over the panels.
/// </summary>
[TestClass]
public class DialogsTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task OpenAndCloseAsync(string chord, string dialogTypeName)
    {
        Assert.IsTrue(await _app.PressAsync(chord), $"{chord} is handled");

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == dialogTypeName,
            $"{chord} to open {dialogTypeName} on top");
        var dialog = (PopUpWindow)_app.Driver.FocusedModal!;
        // As the frame draws it — " Help " alone also matches the menu bar — and anchored on the
        // left only, since some dialogs follow their title with a count ("Task Manager (0)").
        var title = "═ " + dialog.Title + " ";
        await _app.WaitForTextAsync(title);

        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
        await _app.WaitForTextGoneAsync(title);
        await _app.WaitUntilAsync(s => _app.LeftPanelLists("readme.txt", s) && _app.RightPanelLists("right_only.txt", s),
            "both panels to be drawn again");
        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    [DataRow("F1", "HelpDialog")]
    [DataRow("F9", "TaskManagerDialog")]
    [DataRow("F12", "ScreenManagerDialog")]
    [DataRow("Ctrl+F1", "FolderSelectorDialog")]
    [DataRow("Ctrl+A", "CalculatorDialog")]
    [DataRow("Ctrl+E", "UnitConverterDialog")]
    [DataRow("Ctrl+D", "CalendarDialog")]
    [DataRow("Ctrl+Y", "ToDoListDialog")]
    [DataRow("Ctrl+W", "MailDialog")]
    [DataRow("Ctrl+X", "PasswordManagerDialog")]
    [DataRow("Ctrl+G", "GitClientDialog")]
    [DataRow("F2", "TerminalDialog")]
    [DataRow("Alt+F11", "IPhoneBrowserDialog")]
    [DataRow("Ctrl+F11", "SystemInfoDialog")]
    [DataRow("Alt+F9", "DiskManagerDialog")]
    [DataRow("Alt+F12", "ProcessManagerDialog")]
    [DataRow("Alt+F3", "DocumentManagerDialog")]
    [DataRow("Alt+F6", "ThemeManagerDialog")]
    [DataRow("Shift+F8", "SettingsDialog")]
    [DataRow("Ctrl+F5", "NetworkManagerDialog")]
    [DataRow("Ctrl+Alt+Shift+F1", "AboutDialog")]
    [DataRow("F7", "FindFilesDialog")]
    [DataRow("Shift+F7", "CreateFolderDialog")]
    [DataRow("Shift+F4", "CreateFileDialog")]
    public async Task AGlobalDialogOpensOnTopAndClosesCleanly(string chord, string dialogTypeName)
    {
        await OpenAndCloseAsync(chord, dialogTypeName);
    }

    [TestMethod]
    [DataRow("F3", "FileViewerDialog")]
    [DataRow("F4", "TextFileEditorDialog")]
    [DataRow("F5", "CopyDialog")]
    [DataRow("F6", "RenameDialog")]
    [DataRow("Shift+F6", "MoveDialog")]
    [DataRow("F8", "DeleteDialog")]
    public async Task AFileDialogOpensOnTopAndClosesCleanly(string chord, string dialogTypeName)
    {
        await _app.SelectEntryAsync("readme.txt");

        await OpenAndCloseAsync(chord, dialogTypeName);

        Assert.AreEqual("readme.txt", _app.FocusedEntryName, "the cursor is where it was");
    }

    /// <summary>
    /// The ghost this suite found: closing a dialog while anything else still drew on its layer left
    /// the whole dialog on screen, and the next one opened over it.
    /// </summary>
    [TestMethod]
    public async Task DialogsOpenedOneAfterAnotherNeverShowTheOneBefore()
    {
        foreach (var (chord, type, title) in new[]
        {
            ("Ctrl+A", "CalculatorDialog", "═ Calculator ═"),
            ("Ctrl+G", "GitClientDialog", "═ Git ═"),
            ("F9", "TaskManagerDialog", null),
        })
        {
            await _app.PressAsync(chord);
            await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == type, $"{type} to open");
            var screen = await _app.Driver.WaitForIdleAsync();
            if (type != "CalculatorDialog")
            {
                Assert.IsFalse(screen.Contains("═ Calculator ═"), "the calculator closed earlier is not drawn" + Environment.NewLine + screen.Dump());
            }
            await _app.PressAsync("Esc");
            await _app.WaitForNoDialogAsync();
            if (title is not null)
            {
                await _app.WaitForTextGoneAsync(title);
            }
        }

        await _app.WaitForNoStrayFramesAsync();
    }

    [TestMethod]
    public async Task HelpListsTheKeysWithReadableNames()
    {
        await _app.PressAsync("F1");

        var screen = await _app.WaitForTextAsync("Available commands");
        Assert.IsFalse(screen.Contains("Oem"), "keys are named as printed, not by their virtual-key constant" + Environment.NewLine + screen.Dump());
    }

    [TestMethod]
    public async Task ADialogOverADialogClosesFirst()
    {
        await _app.PressAsync("Ctrl+F1");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == "FolderSelectorDialog", "the folder selector");

        await _app.PressAsync("F1");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == "HelpDialog", "help over the selector");

        await _app.PressAsync("Esc");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == "FolderSelectorDialog",
            "Esc to close the help and leave the selector");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
    }
}
