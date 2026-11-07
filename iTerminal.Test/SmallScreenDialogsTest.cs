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
}
