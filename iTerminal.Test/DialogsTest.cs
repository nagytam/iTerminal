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

    [Timeout(120_000)]
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
    [DataRow("Ctrl+S", "WebBrowserDialog")]
    [DataRow("Ctrl+X", "PasswordManagerDialog")]
    [DataRow("Ctrl+G", "GitClientDialog")]
    [DataRow("F2", "TerminalDialog")]
    [DataRow("Alt+F11", "IPhoneBrowserDialog")]
    [DataRow("Ctrl+F11", "SystemInfoDialog")]
    [DataRow("Ctrl+Shift+F10", "SystemSettingsDialog")]
    [DataRow("Alt+F7", "StockBrowserDialog")]
    [DataRow("Ctrl+Shift+F4", "TranslatorDialog")]
    [DataRow("Ctrl+Shift+F5", "CurrencyConverterDialog")]
    [DataRow("Ctrl+Shift+K", "ContactManagerDialog")]
    [DataRow("Ctrl+Shift+H", "EnvironmentVariablesDialog")]
    [DataRow("Ctrl+Shift+W", "EncyclopediaDialog")]
    [DataRow("Ctrl+Shift+B", "PerformanceBenchmarkDialog")]
    [DataRow("Alt+F2", "RemoteConnectionManagerDialog")]
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
    [DataRow("Shift+Alt+F3", "HashManifestDialog")]
    public async Task AGlobalDialogOpensOnTopAndClosesCleanly(string chord, string dialogTypeName)
    {
        await OpenAndCloseAsync(chord, dialogTypeName);
    }

    /// <summary>
    /// Shift+Alt+F6 in the document manager, in the real app: the footer names the other mode, and the sandbox has no embedding
    /// model (AI:EmbeddingModelId is empty), so meaning search says it is off rather than searching.
    /// </summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task ShiftAltF6SwitchesTheDocumentManagerToMeaningSearchAndSaysWhenItIsOff()
    {
        Assert.IsTrue(await _app.PressAsync("Alt+F3"), "Alt+F3 is handled");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == "DocumentManagerDialog", "the document manager on top");

        // The toggle is confirmed by the message below, which only appears while meaning mode is on.
        Assert.IsTrue(await _app.PressAsync("Shift+Alt+F6"), "Shift+Alt+F6 is handled by the dialog");

        foreach (char letter in "money")
        {
            await _app.PressAsync(letter.ToString());
        }

        await _app.WaitForTextAsync("Meaning search is off");

        await _app.PressAsync("Esc");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task SwissJassOpensFromTheCommandPaletteAndPlaysAHotSeatTurn()
    {
        await _app.PressAsync("Shift+F1");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.CommandPaletteDialog>();
        await _app.TypeAsync("Swiss Jass");
        await _app.PressAsync("Enter");
        await _app.Driver.WaitForModalAsync<Core.Dialogs.Games.JassDialog>();

        await _app.WaitForTextAsync("Pass the device to Player 1");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("choose trump");
        await _app.TypeAsync("c");
        await _app.WaitForTextAsync("Trump: clubs");

        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Player 1's hand");
        await _app.PressAsync("1");
        await _app.WaitForTextAsync("Pass the device to Player 2");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>The Virtual Machine Manager opens from its chord on Linux (libvirt) and Windows (Hyper-V) and does not open anywhere else.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheVirtualMachineManagerOpensWhereAHypervisorBackendExists()
    {
        if (Core.Commands.VirtualMachineManagerDialogCommand.IsSupported)
        {
            await OpenAndCloseAsync("Shift+Alt+F9", "VirtualMachineManagerDialog");
            return;
        }

        await _app.PressAsync("Shift+Alt+F9");
        await _app.WaitForNoDialogAsync();
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }

    /// <summary>The Web Browser through the whole app: opens on its home page, takes an address, follows a link and closes.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheWebBrowserOpensAnAddressAndFollowsALink()
    {
        await _app.PressAsync("Ctrl+S");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal?.GetType().Name == "WebBrowserDialog", "the Web Browser to open");
        await _app.WaitForTextAsync("You asked for about:home");

        await _app.PressAsync("Ctrl+L");
        await _app.TypeAsync("example.test");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("You asked for https://example.test");

        await _app.PressAsync("Tab");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("You asked for https://example.test/next");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>The translator through the whole app: type a text, translate it, read the result and the language it was found to be in.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheTranslatorTranslatesATypedText()
    {
        await _app.PressAsync("Ctrl+Shift+F4");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Translator.TranslatorDialog, "the translator to open");

        // From, To and Engine come first; the text field is the fourth stop.
        await _app.PressAsync("Tab");
        await _app.PressAsync("Tab");
        await _app.PressAsync("Tab");
        await _app.TypeAsync("Guten Tag");
        await _app.PressAsync("F5");

        await _app.WaitForTextAsync("[en] Guten Tag");
        await _app.WaitForTextAsync("German (detected) → English");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>The environment variables editor through the whole app: add a user variable, see it, move to the session scope, delete.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheEnvironmentVariablesEditorAddsShowsAndDeletesAVariable()
    {
        await _app.PressAsync("Ctrl+Shift+H");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.EnvVars.EnvironmentVariablesDialog, "the environment editor to open");
        await _app.WaitForTextAsync("SANDBOX_USER");

        await _app.TypeAsync("n");
        await _app.TypeAsync("E2E_VARIABLE");
        await _app.PressAsync("Enter");
        await _app.TypeAsync("hello world");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("hello world");
        await _app.WaitForTextAsync("E2E_VARIABLE");

        await _app.TypeAsync("d");
        await _app.WaitForTextAsync("Delete \"E2E_VARIABLE\"? (y/n)");
        await _app.TypeAsync("y");
        await _app.WaitForTextGoneAsync("hello world");

        // The session scope is the second stop of S on the sandbox (user, machine, session).
        await _app.TypeAsync("s");
        await _app.TypeAsync("s");
        await _app.WaitForTextAsync("SANDBOX_SESSION");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task UserGroupManagerCreatesRenamesAndDeletesSandboxAccounts()
    {
        await _app.Driver.ClickAsync("Tools");
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is Core.Controls.MenuBar.TerminalMenuBar,
            "the Tools menu to take the keyboard");
        for (int attempt = 0; attempt < 100
            && !_app.Screen.Lines.Any(line => line.Contains("User and group manager", StringComparison.Ordinal));
            attempt++)
        {
            iConsole.Test.Core.Automation.ConsoleSnapshot previous = _app.Screen;
            await _app.PressAsync("Down");
            await _app.Driver.WaitUntilAsync(
                screen => !screen.HasSameContent(previous),
                "the Tools menu to redraw after moving selection",
                TimeSpan.FromSeconds(2));
        }
        await _app.WaitForTextAsync("User and group manager");
        await _app.Driver.ClickAsync("User and group manager");
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is Core.Dialogs.UsersAndGroups.UserGroupManagerDialog,
            "the user and group manager to open from Tools");
        await _app.WaitForTextAsync("sandbox-user");
        SandboxServices.SystemSettings.UsersAndGroups sandbox = _app.Driver.GetRequiredService<SandboxServices.SystemSettings.UsersAndGroups>();

        await _app.PressAsync("Tab");
        await _app.PressAsync("n");
        await _app.WaitForTextAsync("Group name");
        await _app.TypeAsync("e2e-group");
        await _app.WaitForTextAsync("e2e-group");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("e2e-group was created.");

        await _app.PressAsync("Home");
        await _app.PressAsync("e");
        await _app.WaitForTextAsync("New name for e2e-group");
        await _app.TypeAsync("e2e-renamed-group");
        await _app.WaitForTextAsync("e2e-renamed-group");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("e2e-group was renamed to e2e-renamed-group.");

        await _app.PressAsync("Tab");
        await _app.PressAsync("n");
        await _app.WaitForTextAsync("User name");
        await _app.TypeAsync("e2e-user");
        await _app.WaitForTextAsync("e2e-user");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Full name");
        await _app.TypeAsync("End-to-end User");
        await _app.WaitForTextAsync("End-to-end User");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Password");
        const string password = "E2E-Secret-123";
        await _app.TypeAsync(password);
        await _app.WaitForTextAsync("●");
        var passwordScreen = await _app.Driver.WaitForIdleAsync();
        Assert.IsFalse(passwordScreen.ToString()!.Contains(password, StringComparison.Ordinal));
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("e2e-user was created.");
        await _app.WaitForTextAsync("e2e-user");

        await _app.PressAsync("a");
        await _app.WaitForTextAsync("Group name");
        await _app.TypeAsync("e2e-renamed-group");
        await _app.WaitForTextAsync("e2e-renamed-group");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Membership changed.");

        await _app.PressAsync("d");
        await _app.WaitForTextAsync("Delete user \"e2e-user\"?");
        await _app.PressAsync("y");
        await _app.WaitUntilAsync(() => !sandbox.HasUser("e2e-user"), "the sandbox user to be deleted");
        await _app.WaitForTextAsync("e2e-user was deleted.");

        await _app.PressAsync("Tab");
        await _app.PressAsync("d");
        await _app.WaitForTextAsync("Delete group \"e2e-renamed-group\"?");
        await _app.PressAsync("y");
        await _app.WaitUntilAsync(() => !sandbox.HasGroup("e2e-renamed-group"), "the sandbox group to be deleted");
        await _app.WaitForTextAsync("e2e-renamed-group was deleted.");

        CollectionAssert.IsSubsetOf(new[] { "create-user", "create-group", "rename-group", "add-member", "delete-user", "delete-group" }, sandbox.Calls.ToArray());
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>The encyclopedia through the whole app: type a term, search, read the introduction, read the whole article, open it in the browser.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheEncyclopediaSearchesAndReadsAnArticle()
    {
        await _app.PressAsync("Ctrl+Shift+W");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Encyclopedia.EncyclopediaDialog, "the encyclopedia to open");

        await _app.TypeAsync("tulip");
        await _app.PressAsync("Enter");

        // The results arrive, and the first one's introduction is read without another key.
        await _app.WaitForTextAsync("tulip (sandbox)");
        await _app.WaitForTextAsync("The sandbox introduction to tulip (sandbox).");

        // Enter on the results reads the whole article.
        await _app.PressAsync("Tab");
        await _app.PressAsync("Tab");
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("The whole sandbox article on tulip (sandbox).");

        await _app.PressAsync("F2");
        await _app.WaitUntilAsync(() => _app.Driver.GetRequiredService<SandboxServices.Launcher>().Launches.Any(l => l.FilePath == "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString("tulip_(sandbox)")), "the article to be opened in the browser");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>The performance benchmark through the whole app: Enter runs it, the scores land, and the probes are the sandbox's fixed figures.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task ThePerformanceBenchmarkRunsAndShowsItsScores()
    {
        await _app.PressAsync("Ctrl+Shift+B");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.Benchmark.PerformanceBenchmarkDialog, "the benchmark to open");

        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Finished. Enter runs it again.");
        await _app.WaitForTextAsync("Overall score");
        await _app.WaitForTextAsync("Single thread");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    /// <summary>The currency converter through the whole app: the rates download on opening, and the amount is converted with them.</summary>
    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCurrencyConverterConvertsWithTheDownloadedRates()
    {
        await _app.PressAsync("Ctrl+Shift+F5");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.CurrencyConverter.CurrencyConverterDialog, "the currency converter to open");

        await _app.WaitForTextAsync("1.00 EUR  =  2.00 USD");
        await _app.WaitForTextAsync("Rates of 2026-10-16 downloaded.");

        await _app.PressAsync("F6");
        await _app.WaitForTextAsync("1.00 USD  =  0.50 EUR");

        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
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
    [Timeout(120_000)]
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

    [Timeout(120_000)]
    [TestMethod]
    public async Task HelpListsTheKeysWithReadableNames()
    {
        await _app.PressAsync("F1");

        var screen = await _app.WaitForTextAsync("Available commands");
        Assert.IsFalse(screen.Contains("Oem"), "keys are named as printed, not by their virtual-key constant" + Environment.NewLine + screen.Dump());
    }

    [Timeout(120_000)]
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
