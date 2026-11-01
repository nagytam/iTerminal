namespace iTerminal.Test;

/// <summary>
/// The editor, the viewer, the permissions dialog and the remote connection form, driven through the whole app
/// on a virtual terminal — a unit test of one control does not prove that a key reaches it through the dialogs.
/// </summary>
[TestClass]
public class EditorViewerAndPermissionsTest
{
    private TerminalApp _app = null!;
    private string LfFile => Path.Combine(_app.LeftRoot, "lf.txt");

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync(seed: (left, _) =>
    {
        // Written with LF on purpose: on Windows an editor that saves with the system's break rewrites every line.
        File.WriteAllBytes(Path.Combine(left, "lf.txt"), System.Text.Encoding.UTF8.GetBytes("alpha\nbeta\n"));
        File.WriteAllBytes(Path.Combine(left, "hu.csv"), System.Text.Encoding.Latin1.GetBytes("t\u00fbr\u00f5,n\u00e9v\nalma,3\n"));
        File.WriteAllText(Path.Combine(left, "long.txt"), string.Join("\n", Enumerable.Range(0, 300).Select(i => "row " + i.ToString("000"))));
        using var database = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(left, "shop.sqlite"),
            Pooling = false,
        }.ConnectionString);
        database.Open();
        using var create = database.CreateCommand();
        create.CommandText = "CREATE TABLE people (id INTEGER, name TEXT); INSERT INTO people VALUES (1, 'Ada');";
        create.ExecuteNonQuery();
    });

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private async Task OpenInEditorAsync(string name, string expectedText)
    {
        await _app.SelectEntryAsync(name);
        Assert.IsTrue(await _app.PressAsync("F4"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.TextFileEditorDialog, "the editor to open");
        await _app.WaitForTextAsync(expectedText);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F8AndShiftF8InTheCsvViewerSwitchTheCodePageAndNeverReachDeleteOrSettings()
    {
        await _app.SelectEntryAsync("hu.csv");
        Assert.IsTrue(await _app.PressAsync("F3"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.CsvViewerDialog, "the CSV viewer to open");

        Assert.IsTrue(await _app.PressAsync("Shift+F8"));
        Assert.IsTrue(_app.Driver.FocusedModal is iConsole.Dialogs.CsvViewerDialog, "Shift+F8 is the settings chord outside this dialog");

        Assert.IsTrue(await _app.PressAsync("F8"));
        Assert.IsTrue(_app.Driver.FocusedModal is iConsole.Dialogs.CsvViewerDialog, "F8 is Delete in the panels");
        Assert.IsTrue(File.Exists(Path.Combine(_app.LeftRoot, "hu.csv")));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F3OnASqliteFileOpensTheDatabaseViewerAndF4ShowsItsText()
    {
        await _app.SelectEntryAsync("shop.sqlite");
        Assert.IsTrue(await _app.PressAsync("F3"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.SqliteViewerDialog, "the SQLite viewer to open");
        await _app.WaitForTextAsync("people");

        Assert.IsTrue(await _app.PressAsync("F4"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.SqliteViewerDialog { IsTextMode: true }, "the text output");
    }

    [TestMethod]
    public async Task TabIndents_CtrlSSaves_AndAnLfFileStaysLf()
    {
        await OpenInEditorAsync("lf.txt", "alpha");

        await _app.PressAsync("Tab");
        await _app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("    alpha", StringComparison.Ordinal)), "Tab to indent the first line");
        await _app.PressAsync("Ctrl+S");

        await _app.WaitUntilAsync(() => File.Exists(LfFile) && File.ReadAllText(LfFile).StartsWith("    alpha", StringComparison.Ordinal),
            "the file to be written");
        Assert.AreEqual("    alpha\nbeta\n", File.ReadAllText(LfFile), "saved with the break the file had, not the system's");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EscOnAChangedFileAsksAndDiscardLeavesTheFileAlone()
    {
        await OpenInEditorAsync("lf.txt", "alpha");
        await _app.PressAsync("Tab");
        await _app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("    alpha", StringComparison.Ordinal)), "the indent");

        await _app.PressAsync("Esc");

        await _app.WaitForTextAsync("has unsaved changes");
        Assert.IsTrue(_app.Driver.FocusedModal is iConsole.Dialogs.UnsavedChangesDialog, "the question has the keyboard");
        await _app.PressAsync("d");
        await _app.WaitForNoDialogAsync();
        Assert.AreEqual("alpha\nbeta\n", File.ReadAllText(LfFile));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EscOnAnUnchangedFileClosesWithoutAsking()
    {
        await OpenInEditorAsync("lf.txt", "alpha");

        await _app.PressAsync("Esc");

        await _app.WaitForNoDialogAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EnterInTheEditorKeepsTheIndentOfTheLine()
    {
        await OpenInEditorAsync("lf.txt", "alpha");
        await _app.PressAsync("Tab");
        await _app.PressAsync("End");

        await _app.PressAsync("Enter");
        await _app.TypeAsync("x");
        await _app.PressAsync("Ctrl+S");

        await _app.WaitUntilAsync(() => File.ReadAllText(LfFile).Contains("x", StringComparison.Ordinal), "the file to be written");
        Assert.AreEqual("    alpha\n    x\nbeta\n", File.ReadAllText(LfFile));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F5InTheEditorGoesToALine()
    {
        await OpenInEditorAsync("long.txt", "row 000");

        await _app.PressAsync("F5");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.GoToLineDialog, "the go-to-line question");
        await _app.TypeAsync("250");
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("row 249");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheViewerScrollsToTheEndAndBack()
    {
        await _app.SelectEntryAsync("long.txt");
        await _app.PressAsync("F3");
        await _app.WaitForTextAsync("row 000");

        await _app.PressAsync("Down");
        await _app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("Y: 1/300", StringComparison.Ordinal)), "Down to move the cursor");
        await _app.PressAsync("PageDown");
        await _app.WaitUntilAsync(s => !s.Contains("row 000"), "PageDown to move on");
        await _app.PressAsync("End");
        await _app.WaitForTextAsync("row 299");
        await _app.PressAsync("PageUp");
        await _app.WaitUntilAsync(s => !s.Contains("row 299"), "PageUp to move back");
        await _app.PressAsync("Home");
        await _app.WaitForTextAsync("row 000");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlPInPropertiesOpensThePermissionsAndAnEntryCanBeAdded()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Ctrl+F12");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.PropertiesDialog, "the properties");

        await _app.PressAsync("Ctrl+P");

        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.AccessControlDialog, "the permissions");
        await _app.WaitForTextAsync("SANDBOX\\Owner");
        await _app.WaitForTextAsync("Inheriting entries from the parent folder");

        await _app.PressAsync("a");
        await _app.TypeAsync("Guests");
        await _app.PressAsync("Enter");

        await _app.WaitForTextAsync("Guests");
        await _app.WaitForTextAsync("Guests: Read.");
        await _app.PressAsync("Esc");
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is Core.Dialogs.PropertiesDialog, "back in the properties");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AnFtpConnectionCanBeSavedFromTheRemoteConnectionsForm()
    {
        await _app.PressAsync("Alt+F2");
        await _app.WaitForTextAsync("Remote connections");
        await _app.PressAsync("n");
        await _app.TypeAsync("files");
        await _app.PressAsync("Tab");
        await _app.PressAsync("Space");
        await _app.PressAsync("Space");
        await _app.WaitForTextAsync("FTP (FTPS when the server offers it)");
        await _app.PressAsync("Tab");
        await _app.TypeAsync("ftp.example.com");

        await _app.PressAsync("Enter");

        await _app.WaitUntilAsync(s => s.Lines.Any(l => l.Contains("FTP", StringComparison.Ordinal) && l.Contains("files", StringComparison.Ordinal)),
            "the saved FTP entry in the list");
        await _app.WaitForTextAsync("opens ftp://ftp.example.com/");
    }
}
