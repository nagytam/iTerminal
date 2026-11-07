namespace iTerminal.Test;

/// <summary>Dialogs on an 80 x 24 terminal: everything inside its frame, nothing cut mid-word that fits.</summary>
[TestClass]
public class SmallScreenDialogsTest
{
    [Timeout(120_000)]
    [TestMethod]
    public async Task PropertiesOn24Rows_KeepsEveryFieldInsideItsFrame()
    {
        await using TerminalApp small = await TerminalApp.StartAsync(width: 80, height: 24);
        await small.SelectEntryAsync("readme.txt");

        await small.PressAsync("Ctrl+F12");

        await small.WaitForTextAsync("Properties of readme.txt");
        // Windows lists three editable dates and Linux two, so the last one both have, Accessed, stands for them all.
        await small.WaitUntilAsync(
            s => s.Lines[s.Height - 1].Contains('╚') && s.Lines[s.Height - 1].Contains('╝')
                && s.Lines.Any(l => l.Contains("Accessed:")) && s.Lines.Any(l => l.Contains("[     Apply     ]")),
            "the dates and the Apply button above the dialog's bottom border");
        // The bottom border carries nothing but the frame: no text box was drawn over it.
        string last = small.Screen.Lines[small.Screen.Height - 1];
        Assert.IsFalse(last.Contains("Accessed:") || last.Contains("Modified:") || last.Contains("Created:"), small.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheTranslatorsSourceBoxOn80Columns_ShowsDetectLanguageInFull()
    {
        await using TerminalApp small = await TerminalApp.StartAsync(width: 80, height: 24);

        await small.PressAsync("Ctrl+Shift+F4");

        await small.WaitForTextAsync("Translator");
        await small.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("[Detect language")), "the whole of \"Detect language\" in the From box");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheChecksumsCompareBoxOn60x20_IsNotHiddenUnderTheBottomBorder()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync("Shift+F12");

        await narrow.WaitForTextAsync("Checksums");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Compare:")), "the compare box inside the frame");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Esc:close")), "the footer hints on the bottom border");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheTranslatorsEngineBoxOn60Columns_KeepsRoomForItsNames()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+Shift+F4");

        await narrow.WaitForTextAsync("Translator");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Engine:") && l.Contains("[Automa")), "more of the engine name than \"Aut\"");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheContactsFormOn20Rows_KeepsAllFiveFieldsAndTheFooterInsideItsFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+Shift+K");

        await narrow.WaitForTextAsync("Contacts");
        await narrow.WaitUntilAsync(
            s => new[] { "Name:", "Email:", "Phone:", "Company:", "Notes:" }.All(field => s.Lines.Any(l => l.Contains(field)))
                && s.Lines.Any(l => l.Contains("Esc close") || l.Contains("Esc:close")),
            "all five fields and the footer on rows of their own");
        string last = narrow.Screen.Lines[narrow.Screen.Height - 1];
        Assert.IsTrue(last.TrimStart().StartsWith('╚') && !last.Contains("Notes:"), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheUnitConvertersColumnsOn60Columns_DoNotRunIntoEachOther()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+E");

        await narrow.WaitForTextAsync("Unit Converter");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Nautical")), "the longest unit name");
        Assert.IsFalse(narrow.Screen.Lines.Any(l => l.Contains("(nmiNautical")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheTorrentManagersFooterOn60Columns_StillSaysHowToLeave()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+Shift+F1");

        await narrow.WaitForTextAsync("BitTorrent");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Esc close")), "the hint that closes the dialog, whole");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Ctrl+D", "Calendar", "N: add a meeting")]
    [DataRow("Ctrl+Y", "Tasks", "N: new task")]
    public async Task TheCalendarAndTheTaskListOn20Rows_KeepTheirPromptAndHintsInsideTheFrame(string chord, string title, string prompt)
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync(chord);

        await narrow.WaitForTextAsync(title);
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(prompt)) && s.Lines.Any(l => l.Contains("Esc close")), "the prompt and the hint that closes the dialog");
        string last = narrow.Screen.Lines[narrow.Screen.Height - 1];
        Assert.IsTrue(last.TrimStart().StartsWith('╚') && !last.Contains(prompt), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheStocksOn60Columns_ShowTheCompanyNameInsteadOfThreeLetters()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Alt+F7");

        await narrow.WaitForTextAsync("Stocks");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("AAPL") && l.Contains("AAPL Inc") && l.Contains("100.00")), "the whole company name in the table row, beside the symbol and the price");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheBluetoothAddressesOn60Columns_AreShownInFull()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+Shift+J");

        await narrow.WaitForTextAsync("Bluetooth devices");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("00:11:22:33:44:55")), "a whole 17-character address");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheGitDialogOutsideARepository_ExplainsWhatToDoAcrossItsWholeWidth()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+G");

        await narrow.WaitForTextAsync("No repository");
        // In the list's own 30-column strip the sentence broke after "a"; across the dialog the first words stay together.
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Open a folder inside a repository")), "the explanation, wrapped to the dialog and not to a narrow strip");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheDiskUsageOn60Columns_ShowsWholeFileNames()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+Shift+F12");

        await narrow.WaitForTextAsync("Disk usage");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("readme.txt") && l.Contains("68%")), "readme.txt in full beside its share");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheSystemSettingsNoteOn60Columns_IsWrappedAndNotCutOff()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Ctrl+Shift+F10");

        await narrow.WaitForTextAsync("System Settings");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("restart.")), "the end of the note about a restart");
    }
}
