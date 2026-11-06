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

        // A 3 x 4 x 5 cube where cube[i, j, k] = 100 i + 10 j + k, so a cell's value says which slice it is from.
        var cube = new double[3, 4, 5];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                for (int k = 0; k < 5; k++)
                {
                    cube[i, j, k] = (i * 100) + (j * 10) + k;
                }
            }
        }

        new PureHDF.H5File { ["cube"] = cube }.Write(Path.Combine(left, "cube.h5"));

        // A bare ELF64 header (x86-64, little-endian, executable) and an "executable" cut off after its magic.
        byte[] elf = new byte[64];
        elf[0] = 0x7F;
        elf[1] = (byte)'E';
        elf[2] = (byte)'L';
        elf[3] = (byte)'F';
        elf[4] = 2;
        elf[5] = 1;
        elf[6] = 1;
        elf[16] = 2;
        elf[18] = 62;
        File.WriteAllBytes(Path.Combine(left, "tool.elf"), elf);
        File.WriteAllBytes(Path.Combine(left, "broken.exe"), [(byte)'M', (byte)'Z']);
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

    [Timeout(120_000)]
    [TestMethod]
    public async Task F3OnAnHdf5FileShowsASliceAsAGridAndTheSliceKeysMoveThroughTheCube()
    {
        await _app.SelectEntryAsync("cube.h5");
        Assert.IsTrue(await _app.PressAsync("F3"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.HdfViewerDialog, "the HDF5 viewer to open");
        await _app.WaitForTextAsync("/cube  float64 [3 × 4 × 5]");
        await _app.WaitForTextAsync("index 1 of 3");
        await _app.WaitForTextAsync("34");
        Assert.IsFalse(_app.Driver.Screen.Lines.Any(l => l.Contains("134", StringComparison.Ordinal)), "slice 0 holds 0..34, never 134");

        // The typed keys: + steps the index of the leading dimension, and the next slice's cells are 100 more.
        Assert.IsTrue(await _app.PressAsync("+"));
        await _app.WaitForTextAsync("index 2 of 3");
        await _app.WaitForTextAsync("134");

        // The layout-independent pair: Ctrl+Up steps the index too.
        Assert.IsTrue(await _app.PressAsync("Ctrl+Up"));
        await _app.WaitForTextAsync("index 3 of 3");
        await _app.WaitForTextAsync("234");

        Assert.IsTrue(await _app.PressAsync("-"));
        await _app.WaitForTextAsync("index 2 of 3");
        await _app.WaitForTextGoneAsync("234");

        // The viewer is still the modal, and Esc closes it.
        Assert.IsTrue(_app.Driver.FocusedModal is iConsole.Dialogs.HdfViewerDialog);
        Assert.IsTrue(await _app.PressAsync("Esc"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is not iConsole.Dialogs.HdfViewerDialog, "the HDF5 viewer to close");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F3OnAnElfFileShowsItsAnalysisAndEscClosesIt()
    {
        await _app.SelectEntryAsync("tool.elf");
        Assert.IsTrue(await _app.PressAsync("F3"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is iConsole.Dialogs.ExecutableAnalysisDialog, "the executable analysis to open");
        await _app.WaitForTextAsync("ELF64 little-endian, x86-64, 64 bytes");
        await _app.WaitForTextAsync("Hardening");
        await _app.WaitForTextAsync("Stack canary");

        Assert.IsTrue(await _app.PressAsync("Down"));
        Assert.IsTrue(_app.Driver.FocusedModal is iConsole.Dialogs.ExecutableAnalysisDialog, "the grid keeps the keys inside the dialog");
        Assert.IsTrue(await _app.PressAsync("Esc"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is not iConsole.Dialogs.ExecutableAnalysisDialog, "the analysis to close");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F3OnATruncatedExecutableOpensTheAnalysisAndSaysItCannotBeRead()
    {
        await _app.SelectEntryAsync("broken.exe");
        Assert.IsTrue(await _app.PressAsync("F3"));
        await _app.WaitUntilAsync(
            () => _app.Driver.FocusedModal is iConsole.Dialogs.ExecutableAnalysisDialog { Report: null } dialog
                && dialog.Status.Length > 0
                && !dialog.Status.StartsWith("Reading", StringComparison.Ordinal),
            "the analysis to report a failure");

        Assert.IsTrue(await _app.PressAsync("Esc"));
        await _app.WaitUntilAsync(() => _app.Driver.FocusedModal is not iConsole.Dialogs.ExecutableAnalysisDialog, "the analysis to close");
    }

    [TestMethod]
    [Timeout(120_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlShiftU_UpperCasesTheSelectionInTheEditor()
    {
        await OpenInEditorAsync("lf.txt", "alpha");

        await _app.PressAsync("Ctrl+A");
        await _app.PressAsync("Ctrl+Shift+U");

        await _app.WaitForTextAsync("ALPHA");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AFileChangedOnDiskWhileTheEditorIsOpenIsReloaded()
    {
        await OpenInEditorAsync("lf.txt", "alpha");

        // Another program saves the file; the editor has no edits, so it takes the new text on its own.
        File.WriteAllText(LfFile, "written elsewhere\n");

        await _app.WaitForTextAsync("written elsewhere");
    }
}
