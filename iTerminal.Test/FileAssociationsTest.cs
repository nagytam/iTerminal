using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.FileAssociations;

namespace iTerminal.Test;

/// <summary>
/// File associations, end to end: Shift+F3 and Ctrl+F4 choose a program for the file under the
/// cursor, and from then on Enter and F4 use it. The programs are never started — the sandbox's
/// <see cref="SandboxServices.Launcher"/> records what would have been.
/// </summary>
[TestClass]
public class FileAssociationsTest
{
    private TerminalApp _app = null!;

    [TestInitialize]
    public async Task StartAsync() => _app = await TerminalApp.StartAsync();

    [TestCleanup]
    public async Task StopAsync() => await _app.DisposeAsync();

    private SandboxServices.Launcher Launcher => _app.Driver.GetRequiredService<SandboxServices.Launcher>();

    private IFileAssociationService Associations => _app.Driver.GetRequiredService<IFileAssociationService>();

    private string ReadmePath => Path.Combine(_app.LeftRoot, "readme.txt");

    private Task WaitForLaunchesAsync(int count) =>
        _app.WaitUntilAsync(() => Launcher.Launches.Count == count, $"{count} launch(es), not {Launcher.Launches.Count}");

    /// <summary>Opens the form with Ins, types a command and saves it.</summary>
    private async Task AddProgramAsync(string command)
    {
        await _app.PressAsync("Insert");
        await _app.Driver.WaitForModalAsync<FileAssociationDialog>();
        await _app.TypeAsync(command);
        await _app.PressAsync("Enter");
        await _app.Driver.WaitForModalAsync<OpenWithDialog>();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ShiftF3_WithNothingChosen_OffersTheSystemDefault()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Shift+F3");
        var dialog = await _app.Driver.WaitForModalAsync<OpenWithDialog>();
        var screen = await _app.WaitForTextAsync("Open readme.txt with");

        Assert.IsTrue(screen.Contains("System default"), screen.Dump());
        Assert.AreEqual(1, dialog.Choices.Count);

        await _app.PressAsync("Enter");

        await _app.WaitForNoDialogAsync();
        await WaitForLaunchesAsync(1);
        Assert.IsNull(Launcher.Launches[0].Association, "the system default");
        Assert.AreEqual(ReadmePath, Launcher.Launches[0].FilePath);
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task Enter_OnAFileWithNothingChosen_OpensItWithTheSystemDefaultOutsideTheApp()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Enter");

        await WaitForLaunchesAsync(1);
        Assert.IsNull(Launcher.Launches[0].Association, "the system default, not a program run in the command log");
        Assert.AreEqual(ReadmePath, Launcher.Launches[0].FilePath);
        await _app.WaitForNoStrayFramesAsync();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AProgramAddedWithIns_BecomesWhatEnterUses_ForFilesLikeIt()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Shift+F3");
        await _app.Driver.WaitForModalAsync<OpenWithDialog>();

        await AddProgramAsync("reader --wide %f");

        // Back on the list: the new program, named after its command, is the default.
        var screen = await _app.WaitForTextAsync("reader");
        await _app.WaitUntilAsync(s => s.Contains("default") && s.Contains("System default"), "the list to show the new program as the default");
        var saved = Associations.GetDefault("readme.txt", FileAssociationKind.Open);
        Assert.IsNotNull(saved, screen.Dump());
        Assert.AreEqual("reader", saved.Name);
        Assert.AreEqual("*.txt", saved.Patterns);
        Assert.IsFalse(saved.InTerminal);

        await _app.PressAsync("Enter");
        await _app.WaitForNoDialogAsync();
        await WaitForLaunchesAsync(1);
        Assert.AreEqual(saved, Launcher.Launches[0].Association);

        // From now on Enter on a .txt file is that program — and on another kind of file it is not.
        await _app.PressAsync("Enter");
        await WaitForLaunchesAsync(2);
        Assert.AreEqual(saved, Launcher.Launches[1].Association);
        Assert.AreEqual(ReadmePath, Launcher.Launches[1].FilePath);

        await _app.SelectEntryAsync("alpha");
        await _app.PressAsync("Enter");
        await _app.WaitForFocusedFolderAsync(Path.Combine(_app.LeftRoot, "alpha"));
        Assert.AreEqual(2, Launcher.Launches.Count, "Enter on a folder still opens the folder");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheChoiceSurvivesARestart()
    {
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Shift+F3");
        await _app.Driver.WaitForModalAsync<OpenWithDialog>();
        await AddProgramAsync("reader");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        _app = await _app.RestartAsync();

        Assert.AreEqual("reader", Associations.GetDefault("readme.txt", FileAssociationKind.Open)?.Name);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task CtrlF4_ChoosesTheEditor_AndF4UsesIt()
    {
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Ctrl+F4");
        var dialog = await _app.Driver.WaitForModalAsync<OpenWithDialog>();
        var screen = await _app.WaitForTextAsync("Edit readme.txt with");
        Assert.IsTrue(screen.Contains("iTerminal editor"), screen.Dump());
        Assert.IsFalse(screen.Contains("System default"), "editing is not what the system default is for");

        await AddProgramAsync("vim");
        await _app.WaitUntilAsync(() => dialog.Choices.Count == 2, "the editor to be listed");
        await _app.PressAsync("Esc");
        await _app.WaitForNoDialogAsync();

        await _app.PressAsync("F4");

        await WaitForLaunchesAsync(1);
        Assert.AreEqual("vim", Launcher.Launches[0].Association?.Name);
        Assert.AreEqual(FileAssociationKind.Edit, Launcher.Launches[0].Association?.Kind);
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal, "the built-in editor did not open as well");

        // Enter is a different question, and nothing was chosen for it.
        Assert.IsNull(Associations.GetDefault("readme.txt", FileAssociationKind.Open));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task TheBuiltInEditorIsStillOneRowAway()
    {
        Associations.Save(new FileAssociation("1", "vim", "*.txt", "vim", FileAssociationKind.Edit, InTerminal: true));
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Ctrl+F4");
        await _app.Driver.WaitForModalAsync<OpenWithDialog>();
        await _app.WaitForTextAsync("iTerminal editor");

        await _app.PressAsync("End");
        await _app.PressAsync("Enter");

        await _app.Driver.WaitForModalAsync<iConsole.Dialogs.TextFileEditorDialog>();
        await _app.WaitForTextAsync("Hello from the left panel.");
        Assert.AreEqual(0, Launcher.Launches.Count);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task SpaceMakesAProgramTheDefault_AndDelRemovesOne_AfterAsking()
    {
        Associations.Save(new FileAssociation("1", "First", "*.txt", "first", FileAssociationKind.Open, false));
        Associations.Save(new FileAssociation("2", "Second", "*.txt", "second", FileAssociationKind.Open, false));
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Shift+F3");
        var dialog = await _app.Driver.WaitForModalAsync<OpenWithDialog>();
        await _app.WaitForTextAsync("Second");
        Assert.AreEqual("Second", Associations.GetDefault("readme.txt", FileAssociationKind.Open)?.Name);

        await _app.PressAsync("Down");
        await _app.PressAsync("Space");
        Assert.AreEqual("First", Associations.GetDefault("readme.txt", FileAssociationKind.Open)?.Name);
        Assert.AreEqual("First", dialog.Selected?.Title, "the cursor follows the program it moved");

        // Del asks; any key but y leaves the program where it is.
        await _app.PressAsync("Delete");
        await _app.WaitForTextAsync("Remove First from the list? (y/n)");
        await _app.PressAsync("n");
        Assert.AreEqual(2, Associations.GetAll().Count);

        await _app.PressAsync("Delete");
        await _app.PressAsync("y");
        await _app.WaitForTextGoneAsync("First");
        CollectionAssert.AreEqual(new[] { "Second" }, Associations.GetAll().Select(a => a.Name).ToArray());
        Assert.IsInstanceOfType<OpenWithDialog>(_app.Driver.FocusedModal, "removing a program does not close the list");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task F4OnTheListChangesAProgram_AndAnEmptyCommandIsRefused()
    {
        Associations.Save(new FileAssociation("1", "Reader", "*.txt", "reader", FileAssociationKind.Open, false));
        await _app.SelectEntryAsync("readme.txt");
        await _app.PressAsync("Shift+F3");
        await _app.Driver.WaitForModalAsync<OpenWithDialog>();

        await _app.PressAsync("F4");
        var form = await _app.Driver.WaitForModalAsync<FileAssociationDialog>();
        await _app.WaitForTextAsync("Change the program to open with");
        Assert.AreEqual("reader", form.Command);

        // The cursor is at the end of the command: six backspaces empty it.
        for (var i = 0; i < 6; i++)
        {
            await _app.PressAsync("Backspace");
        }
        await _app.PressAsync("Enter");
        await _app.WaitForTextAsync("Type the command that opens the file");
        Assert.IsInstanceOfType<FileAssociationDialog>(_app.Driver.FocusedModal);

        await _app.TypeAsync("viewer");
        await _app.PressAsync("Enter");
        await _app.Driver.WaitForModalAsync<OpenWithDialog>();

        var changed = Associations.GetAll().Single();
        Assert.AreEqual("viewer", changed.Command);
        Assert.AreEqual("1", changed.Id);
        Assert.AreEqual("Reader", changed.Name, "the name the user gave it is kept");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AProgramThatCannotStart_IsReportedInAToast()
    {
        Associations.Save(new FileAssociation("1", "Reader", "*.txt", "reader", FileAssociationKind.Open, false));
        Launcher.NextFailure = "reader was not found on the PATH.";
        await _app.SelectEntryAsync("readme.txt");

        await _app.PressAsync("Enter");

        var screen = await _app.WaitForTextAsync("Could not open readme.txt");
        Assert.IsTrue(screen.Contains("reader was not found on the PATH."), screen.Dump());
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task OpenWithIsNotOfferedOnAFolder()
    {
        await _app.SelectEntryAsync("alpha");

        Assert.IsFalse(await _app.PressAsync("Shift+F3"), "a folder is not opened with a program");
        Assert.IsInstanceOfType<Core.Screens.TerminalScreen>(_app.Driver.FocusedModal);
    }
}
