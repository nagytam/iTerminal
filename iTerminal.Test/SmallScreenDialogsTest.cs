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
    [DataRow("Shift+F2", "Delete what the target", false)]
    [DataRow("Ctrl+Shift+E", "Writes name.enc beside", true)]
    [DataRow("Ctrl+W", "Set up an account", false)]
    public async Task ALineTooLongForTheDialogOn60Columns_EndsOnAnEllipsis(string chord, string start, bool needsAFile)
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);
        if (needsAFile)
        {
            await narrow.SelectEntryAsync("readme.txt");
        }

        await narrow.PressAsync(chord);

        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(start) && l.Contains('…')), $"the line starting \"{start}\" to end on an ellipsis");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheFolderSelectorsTabsOn18Rows_StayInsideTheDialogsFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync("Ctrl+F1");

        await narrow.WaitForTextAsync("Folder Selector");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("6-Path")), "the last tab name");
        string[] lines = narrow.Screen.Lines.ToArray();
        int lastTab = Array.FindIndex(lines, l => l.Contains("6-Path"));
        int footer = Array.FindIndex(lines, l => l.Contains("Esc cancel"));
        Assert.IsTrue(footer > lastTab, "the dialog's bottom border must come after the last tab name\n" + narrow.Screen.Dump());
        Assert.IsFalse(lines.Skip(footer + 1).Any(l => l.Contains("╠") || l.Contains("-Path")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Ctrl+Shift+F4", "The source language")]
    [DataRow("Alt+F3", "Nothing indexed yet")]
    [DataRow("Ctrl+Shift+F5", "1 EUR =")]
    public async Task AStatusOrRateLineTooLongFor50Columns_EndsOnAnEllipsis(string chord, string start)
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync(chord);

        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(start) && l.Contains('…')), $"the line starting \"{start}\" to end on an ellipsis");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("About", "A keyboard-driven")]
    [DataRow("Label", "Blank tags remove")]
    public async Task ASentenceTooLongForTheDialogOn50Columns_EndsOnAnEllipsis(string command, string start)
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync("Shift+F1");
        await narrow.WaitForTextAsync("Run:");
        await narrow.TypeAsync(command);
        await narrow.PressAsync("Enter");

        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(start) && l.Contains('…')), $"the line starting \"{start}\" to end on an ellipsis");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheFolderSelectorsButtonsOn50Columns_EndOnAnEllipsisWhenTheirCaptionIsCut()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync("Ctrl+F1");

        await narrow.WaitForTextAsync("Folder Selector");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("…]")), "a button caption that ends on an ellipsis instead of stopping mid-word");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheUnitConvertersCategoriesOn50Columns_DoNotTouchTheFirstUnitColumn()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync("Ctrl+E");

        await narrow.WaitForTextAsync("Unit Converter");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Temperat")), "the Temperature category");
        Assert.IsFalse(narrow.Screen.Lines.Any(l => l.Contains("TemperaturInch") || l.Contains("TemperatureInch")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheStripMetadataNotesOn50Columns_AreWrappedAndNotCutMidWord()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync("Shift+F1");
        await narrow.WaitForTextAsync("Run:");
        await narrow.TypeAsync("Strip");
        await narrow.PressAsync("Enter");

        await narrow.WaitForTextAsync("Strip metadata");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("are removed.")) && s.Lines.Any(l => l.Contains("recovered.")), "the ends of the two sentences");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheKeyBarOn40Columns_KeepsWholeKeysAndNotJustTheLetterF()
    {
        await using TerminalApp tiny = await TerminalApp.StartAsync(width: 40, height: 15);

        // The bar used to be "[F][F][F][F][F][F6][F][F8]…": every key but two cut to its first letter.
        string bar = tiny.Screen.Lines[tiny.Screen.Height - 1];
        Assert.IsTrue(bar.Contains("[F1-"), tiny.Screen.Dump());
        Assert.IsFalse(bar.Contains("[F]"), tiny.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheButtonsOfThePropertiesOn15Rows_DoNotDrawOverEachOther()
    {
        await using TerminalApp tiny = await TerminalApp.StartAsync(width: 40, height: 15);
        await tiny.SelectEntryAsync("readme.txt");

        await tiny.PressAsync("Ctrl+F12");

        await tiny.WaitForTextAsync("Properties of readme.txt");
        await tiny.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("[     Apply     ]") && l.Contains("Cancel")), "Apply and Cancel whole, on a row of their own");
        Assert.IsFalse(tiny.Screen.Lines.Any(l => l.Contains("])]")), tiny.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheChessRulesOn20Rows_EndOnAnEllipsisInsteadOfASentenceCutInTheAir()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Shift+F1");
        await narrow.WaitForTextAsync("Run:");
        await narrow.TypeAsync("Chess");
        await narrow.PressAsync("Enter");

        await narrow.WaitForTextAsync("against the computer");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Select a piece") && l.Contains('…')), "the rules, cut at a clear ellipsis");
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
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("readme.txt") && l.Contains("%")), "readme.txt in full beside its share (its size, so the percentage, differs between Windows and Linux)");
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

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheDateBoxesOfTheProperties_StartInOneColumn()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 40);
        await app.SelectEntryAsync("readme.txt");

        await app.PressAsync("Ctrl+F12");

        await app.WaitForTextAsync("Properties of readme.txt");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Modified: [")) && s.Lines.Any(l => l.Contains("Accessed: [")), "the date boxes");
        int[] columns = [.. app.Screen.Lines.Where(l => l.Contains("Modified: [") || l.Contains("Accessed: [") || l.Contains("Created: [")).Select(l => l.IndexOf('['))];
        Assert.IsTrue(columns.Length >= 2 && columns.Distinct().Count() == 1, app.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheGlobalSettingsPageOn45Columns_KeepsItsTextAndCheckBoxInsideTheFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);

        await narrow.PressAsync("Shift+F8");

        await narrow.WaitForTextAsync("Draw pict");
        string[] lines = narrow.Screen.Lines.ToArray();
        int box = Array.FindIndex(lines, l => l.Contains("Draw pict"));
        int bottom = Array.FindLastIndex(lines, l => l.Contains('╚'));
        Assert.IsTrue(box >= 0 && box < bottom, narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCalendarOn16Rows_DrawsNothingOnItsBottomBorder()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);

        await narrow.PressAsync("Ctrl+D");

        await narrow.WaitForTextAsync("Calendar");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Esc close")), "the hint that closes the dialog");
        Assert.IsTrue(narrow.Screen.Lines[narrow.Screen.Height - 1].TrimStart().StartsWith("╚═") && narrow.Screen.Lines[narrow.Screen.Height - 1].Count(c => c == '═') >= 40, narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheRemoteConnectionsHelpOn60Columns_EndsOnAnEllipsisNotMidWord()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Alt+F2");

        await narrow.WaitForTextAsync("Remote connections");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Nothing saved yet")), "the empty-list help");
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains("Nothing saved yet") && l.Contains("…")), narrow.Screen.Dump());
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains(" saved  ·") && l.Contains("…")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheBenchmarkOn22Rows_ShowsItsNetworkGroup()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 70, height: 22);

        await narrow.PressAsync("Ctrl+Shift+B");

        await narrow.WaitForTextAsync("Performance benchmark");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Network")), "the Network group");
        Assert.IsTrue(narrow.Screen.Lines.Count(l => l.Contains("waiting")) >= 8, narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheDiskManagerOn70Columns_WrapsALongFactInsteadOfCuttingIt()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 70, height: 22);

        await narrow.PressAsync("Alt+F9");

        await narrow.WaitForTextAsync("Disk manager");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("saved to its database")), "the end of the memory drive's description");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheEnvironmentVariablesAndTheEncryptionHaveTheirOwnKeys_WithAFileUnderTheCursor()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SelectEntryAsync("readme.txt");

        await app.PressAsync("Ctrl+Shift+H");
        await app.WaitUntilAsync(() => app.Driver.FocusedModal is Core.Dialogs.EnvVars.EnvironmentVariablesDialog, "the environment editor to open");
        await app.PressAsync("Esc");
        await app.WaitForNoDialogAsync();

        await app.PressAsync("Ctrl+Shift+E");
        await app.WaitUntilAsync(() => app.Driver.FocusedModal is Core.Dialogs.FileEncryptionDialog, "the encryption dialog to open");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheEnvironmentVariablesOn18Rows_KeepTheBottomBorderWhole()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync("Ctrl+Shift+H");

        await narrow.WaitForTextAsync("Environment Variables");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("N new")), "the footer");
        string last = narrow.Screen.Lines[narrow.Screen.Height - 1];
        Assert.IsTrue(last.Trim().StartsWith('╚') && !last.Trim().Contains(' '), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheTaskHeadingOn50Columns_KeepsTheWordSubjectWhole()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync("Ctrl+Y");

        await narrow.WaitForTextAsync("Tasks");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Priority")), "the heading");
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains("Priority") && l.Contains("Subject")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Ctrl+Shift+F1", "No torrents yet")]
    [DataRow("Ctrl+Shift+F2", "No workspaces yet")]
    public async Task TheEmptyListHelpOn50Columns_EndsOnAnEllipsis(string chord, string text)
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync(chord);

        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(text)), "the empty-list help");
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains(text) && l.Contains('…')), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ThePropertiesButtonsOn45Columns_StayInsideTheFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync("Ctrl+F12");

        await narrow.WaitForTextAsync("Properties of readme.txt");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Apply")), "the buttons");
        foreach (string line in narrow.Screen.Lines.Where(l => l.Contains("Permissions") || l.Contains("Remove mark") || l.Contains("Apply")))
        {
            Assert.IsTrue(line.TrimEnd().EndsWith('║'), narrow.Screen.Dump());
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCompressHintOn45Columns_EndsOnAnEllipsis()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 20);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync("Alt+F5");

        await narrow.WaitForTextAsync("Compress readme.txt");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Level: 0 stores")), "the level hint");
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains("Level: 0 stores") && l.Contains('…')), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheHelpOn45Columns_WrapsEachDescriptionWithinTheFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);

        await narrow.PressAsync("F1");

        await narrow.WaitForTextAsync("Available commands");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("available in the current")), "the first description wrapped at the frame, not at the old indent");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheContactsHeadingOn50Columns_ShowsEveryColumnTitle()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 50, height: 18);

        await narrow.PressAsync("Ctrl+Shift+K");

        await narrow.WaitForTextAsync("Contacts");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Email")), "the heading");
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains("Phone") && l.Contains("Company")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheMailSetupOn16Rows_ScrollsItsFieldsAndKeepsTheBorderWhole()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);

        await narrow.PressAsync("Ctrl+W");

        await narrow.WaitForTextAsync("Account name");
        for (int i = 0; i < 14; i++)
        {
            await narrow.PressAsync("Tab");
            await Task.Delay(100);
        }

        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("LDAP bind DN")), "the last field scrolled into view");
        string last = narrow.Screen.Lines[narrow.Screen.Height - 1];
        Assert.IsTrue(last.Trim().StartsWith('╚') && !last.Trim().Contains(' '), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheSynchroniseOptionsOn60Columns_ContinueOnASecondLineInsteadOfBeingCut()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);

        await narrow.PressAsync("Shift+F1");
        await narrow.TypeAsync("Synchronise");
        await narrow.PressAsync("Enter");

        await narrow.WaitForTextAsync("Synchronise folders");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Delete extras")), "the delete-extras option");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCalculatorOn16Rows_KeepsItsKeysInsideTheFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);

        await narrow.PressAsync("Ctrl+A");

        await narrow.WaitForTextAsync("Calculator");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("[  =")) || s.Lines.Any(l => l.Contains("=  ]")), "the equals key");
        Assert.IsFalse(narrow.Screen.Lines.Any(l => l.Contains("]═[") || l.Contains("]║[")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("F3", "F7", "Search text")]
    [DataRow("F4", "Ctrl+R", "Replace text")]
    public async Task TheSearchAndReplaceDialogsOn45Columns_KeepTheirOptionsAndButtonsInsideTheFrame(string open, string chord, string title)
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 45, height: 16);
        await narrow.SelectEntryAsync("readme.txt");
        await narrow.PressAsync(open);
        await narrow.WaitForTextAsync("Hello from the left panel");

        await narrow.PressAsync(chord);

        await narrow.WaitForTextAsync(title);
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Regular expression")), "the options");
        foreach (string line in narrow.Screen.Lines.Where(l => l.Contains("Regular expression") || l.Contains("Match case") || l.Contains("Cancel")))
        {
            Assert.IsTrue(line.TrimEnd().EndsWith('║') && !line.Contains("a[") && !line.Contains("l[") , narrow.Screen.Dump());
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCurrencyBoxesOn40Columns_EndInsideTheFrame()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 40, height: 15);

        await narrow.PressAsync("Ctrl+Shift+F5");

        await narrow.WaitForTextAsync("Currency Converter");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Amount:")), "the boxes");
        string row = narrow.Screen.Lines.First(l => l.Contains("Amount:"));
        Assert.IsTrue(row[row.IndexOf("To:", StringComparison.Ordinal)..].Contains('║'), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheDiskUsageOn40Columns_ShowsEachRowsPercentage()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 40, height: 15);

        await narrow.PressAsync("Ctrl+Shift+F12");

        await narrow.WaitForTextAsync("Disk usage");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("readme")), "the rows");
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains("readme") && l.Contains('%')), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ThePasswordsOn15Rows_KeepTheBottomBorderWhole()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 40, height: 15);

        await narrow.PressAsync("Ctrl+X");

        await narrow.WaitForTextAsync("Passwords");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Enter edit")), "the footer");
        string last = narrow.Screen.Lines[narrow.Screen.Height - 1];
        Assert.IsTrue(last.Trim().StartsWith('╚') && !last.Trim().Contains(' '), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheStreamsHintOn60Columns_EndsOnAWholeHint()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 20);
        await narrow.SelectEntryAsync("readme.txt");

        await narrow.PressAsync("Shift+F1");
        await narrow.TypeAsync("Streams and Attributes");
        await narrow.PressAsync("Enter");

        await narrow.WaitForTextAsync("Streams and attributes");
        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("choose")), "the hint line");
        string hint = narrow.Screen.Lines.First(l => l.Contains("choose"));
        Assert.IsTrue(hint.Contains("Esc close") || !hint.Contains("Esc"), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheScientificCalculatorOn16Rows_ShowsItsLastKeyRow()
    {
        await using TerminalApp narrow = await TerminalApp.StartAsync(width: 60, height: 16);

        await narrow.PressAsync("Ctrl+A");
        await narrow.WaitForTextAsync("Basic");
        await narrow.PressAsync("Ctrl+Right");

        await narrow.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("[ sin")), "the scientific keys");
        await Task.Delay(1000);
        Assert.IsTrue(narrow.Screen.Lines.Any(l => l.Contains("+/-") && l.Contains("[  =")), narrow.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Ctrl+Shift+K", "Esc schließen")]
    [DataRow("F7", "Esc:schließen")]
    [DataRow("Alt+F3", "Esc:schließen")]
    [DataRow("Ctrl+D", "Esc schließen")]
    [DataRow("Ctrl+W", "Esc zurück")]
    [DataRow("Ctrl+X", "Esc schließen")]
    [DataRow("Alt+F2", "Esc schließen")]
    [DataRow("Ctrl+Shift+F2", "Esc schließen")]
    public async Task TheFootersOfContactsFindAndDocumentsInGerman_AreGerman(string chord, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync(chord);

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the footer in German");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheContactsListInGerman_HasGermanHeadingsAndMessages()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Ctrl+Shift+K");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Telefon") && l.Contains("Firma")), "the German column headings");
        Assert.IsTrue(app.Screen.Lines.Any(l => l.Contains("Keine Kontakte")), app.Screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheWorkspacesEmptyHelpInGerman_IsGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Ctrl+Shift+F2");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Noch keine Arbeitsbereiche")), "the empty help in German");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCalendarInGerman_SaysTodayAndNoMeetingsInGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Ctrl+D");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Keine Termine")) && s.Lines.Any(l => l.Contains("Woche") && l.Contains("heute")), "the German day line and meetings header");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheRemoteConnectionsListInGerman_HasGermanHeadings()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 120, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Alt+F2");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("gespeichert")) && s.Lines.Any(l => l.Contains("Ziel")), "the German summary and column heading");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Ctrl+W", "Kontoname")]
    [DataRow("Ctrl+Shift+F4", "Ausgangssprache")]
    public async Task TheMailFormAndTheTranslatorNoteInGerman_AreGerman(string chord, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync(chord);

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCalendarInGerman_HasGermanDayNames()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Ctrl+D");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Mo Di Mi Do Fr Sa So")), "the German day names over the month");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheAiModelInformationInGerman_HasGermanSections()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Ctrl+F2");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Konfiguration")) && s.Lines.Any(l => l.Contains("Verfügbar")), "the German sections");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Alt+F6", "Enter:anwenden")]
    [DataRow("Ctrl+Shift+F7", "signieren")]
    [DataRow("Ctrl+Shift+F8", "Enter:löschen")]
    [DataRow("Ctrl+F1", "Enter öffnen")]
    [DataRow("Alt+F3", "Noch nichts indiziert")]
    [DataRow("Alt+F11", "Keine iOS-Geräte")]
    [DataRow("Ctrl+Shift+F6", "Keine doppelten Dateien")]
    public async Task TheFootersAndMessagesOfSevenMoreDialogsInGerman_AreGerman(string chord, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync(chord);

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Snake", "Pfeile:bewegen")]
    [DataRow("Tetris", "Enter:pausieren")]
    [DataRow("Sort by", "Enter:anwenden")]
    [DataRow("Open with", "Einfg:hinzufügen")]
    [DataRow("Strip Metadata", "Kamera, GPS")]
    public async Task TheGamesSortOpenWithAndStripDialogsInGerman_AreGerman(string command, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync("Shift+F1");
        await app.TypeAsync(command);
        await app.PressAsync("Enter");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Tic-Tac-Toe", "X ist am Zug.")]
    [DataRow("Chess", "Weiß ist am Zug.")]
    [DataRow("Go", "Schwarz ist am Zug.")]
    [DataRow("Nine Men", "Weiß setzt einen Stein.")]
    [DataRow("Snake", "Zum Starten eine Pfeiltaste")]
    [DataRow("Tetris", "Staple die fallenden Teile")]
    public async Task TheGamesStatusLinesAndRulesInGerman_AreGerman(string game, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync("Shift+F1");
        await app.TypeAsync(game);
        await app.PressAsync("Enter");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German status line or rules");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Alt+F9", null, "Speicherlaufwerk")]
    [DataRow("Ctrl+Shift+F10", null, "Arbeitsgruppe gilt")]
    [DataRow(null, "Map", "Pfeile:schwenken")]
    [DataRow(null, "Web Browser", "F1:Hilfe")]
    [DataRow(null, "Compare Folders", "Enter:vergleichen")]
    [DataRow(null, "Duplicate", "Leertaste/Klick:markieren")]
    [DataRow(null, "Disk usage", "g:zum Panel")]
    [DataRow("Ctrl+F8", null, "Papierkorb")]
    [DataRow("Ctrl+F9", null, "docker: Container")]
    [DataRow("Ctrl+F6", null, "git: Repositories")]
    [DataRow(null, "Stocks", "Kurs")]
    [DataRow(null, "Passwords", "gespeichert")]
    [DataRow("Ctrl+Shift+F1", null, "Upload")]
    public async Task TheDiskManagerSystemSettingsMapAndWebBrowserInGerman_AreGerman(string? chord, string? command, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 160, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        if (chord is not null)
        {
            await app.PressAsync(chord);
        }
        else
        {
            await app.PressAsync("Shift+F1");
            await app.TypeAsync(command!);
            await app.PressAsync("Enter");
        }

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheCommandPaletteInGerman_ListsGermanNamesAndStillFindsEnglishOnes()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 120, height: 36);
        await app.SwitchToGermanAsync();

        await app.PressAsync("Shift+F1");
        await app.TypeAsync("Ordner vergl");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Ordner vergleichen")), "the German name found by its German words");
        await app.PressAsync("Esc");
        await app.WaitForNoDialogAsync();

        await app.PressAsync("Shift+F1");
        await app.TypeAsync("compare fold");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Ordner vergleichen")), "the German name found by its English words");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheHelpInGerman_NamesTheCommandsInGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 120, height: 36);
        await app.SwitchToGermanAsync();

        await app.PressAsync("F1");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("F7") && l.Contains("Dateien suchen")), "F7 named Dateien suchen");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Voice input", "wählen")]
    [DataRow("Checksums", "F2:.sha256 speichern")]
    [DataRow("About", "tastaturgesteuerter")]
    [DataRow("Filter by Label", "Ordner bleiben aufgelistet")]
    [DataRow("Open with", "Systemstandard")]
    [DataRow("Passwords", "Netzwerk- und Mail-Passwörter")]
    [DataRow("Documents", "· Bilder:")]
    public async Task ThePaletteFooterAndSixMoreDialogsInGerman_AreGerman(string command, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync("Shift+F1");
        await app.TypeAsync(command);
        await app.PressAsync(command == "Voice input" ? "Esc" : "Enter");
        if (command == "Voice input")
        {
            await app.WaitForNoDialogAsync();
            await app.PressAsync("Shift+F1");
            await app.TypeAsync("zzzz");
        }

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task APropertiesDialogOpenedAfterALiveSwitchToGerman_IsGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 100, height: 30);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync("Ctrl+F12");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("[") && l.Contains("Übernehmen")), "the Apply button in German");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("F5", "Überschreiben")]
    [DataRow("F6", "Überschreiben")]
    [DataRow("Shift+F7", "Ordner erstellen")]
    [DataRow("F8", "Datei löschen")]
    public async Task TheFileOperationDialogsOpenedAfterALiveSwitchToGerman_AreGerman(string key, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 120, height: 36);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync(key);

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the dialog in German");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Ctrl+F1", "0-Favoriten")]
    [DataRow("F12", "(aktuell)")]
    [DataRow("Shift+F3", "öffnen mit")]
    [DataRow("F3", "F7-Suchen")]
    [DataRow("F3", "Umbruch")]
    [DataRow("F4", "F2-Speichern]")]
    [DataRow("F4", "Beschreibbar")]
    public async Task TheFolderSelectorTabsScreenManagerOpenWithViewerAndEditorInGerman_AreGerman(string key, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync(key);

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    [DataRow("Alt+F11", "Verbundene Geräte")]
    [DataRow("Ctrl+N", "Netzwerkbrowser")]
    [DataRow("Ctrl+E", "Einheitenumrechner —")]
    [DataRow("Ctrl+E", "Länge")]
    [DataRow("Ctrl+E", "Zentimeter")]
    [DataRow("Ctrl+F4", "iTerminal-Editor")]
    [DataRow("Ctrl+Shift+F10", "Der Name, unter dem")]
    [DataRow("Ctrl+Shift+F10", "Umgebung")]
    public async Task SixMoreHeadingsAndNotesInGerman_AreGerman(string chord, string expected)
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync(chord);

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains(expected)), "the German text");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheFileContextMenuInGerman_IsGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();
        var screen = await app.WaitForTextAsync("readme.txt");
        var position = screen.Find("readme.txt")!;

        await app.Driver.RightClickAsync((int)position.X, (int)position.Y);

        screen = await app.WaitForTextAsync("Eigenschaften");
        foreach (string item in new[] { "Kopieren", "Löschen", "Prüfsummen" })
        {
            Assert.IsTrue(screen.Contains(item), item + " missing:" + screen.Dump());
        }
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheInfoStripAndTheAiBarInGerman_AreGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Datei: readme.txt") && l.Contains("Dateigröße:")), "the information strip in German");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("nicht verfügbar")), "the AI bar in German");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheFolderMarkAndTheFileCountInGerman_AreGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SwitchToGermanAsync();

        await app.SelectEntryAsync("alpha");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("alpha") && l.Contains("Ordner")), "the folder mark in German");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Dateien:")), "the file count in German");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ThePanelFooterAndTheStripAfterALiveSwitchToGerman_AreGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 140, height: 40);
        await app.SelectEntryAsync("readme.txt");

        await app.SwitchToGermanAsync();

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("5 Einträge")), "the entry count in German");
        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Hoch zu:") || l.Contains("Datei: readme.txt")), "the strip in German");
        await app.WaitUntilAsync(s => !s.Lines.Any(l => l.Contains("Up to:") || l.Contains("File size:")), "no English left in the strip");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheHelpTitleInGerman_IsGerman()
    {
        await using TerminalApp app = await TerminalApp.StartAsync(width: 80, height: 24);
        await app.SelectEntryAsync("readme.txt");
        await app.SwitchToGermanAsync();

        await app.PressAsync("F1");

        await app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("═ Hilfe ═")), "the Help title in German");
    }
}