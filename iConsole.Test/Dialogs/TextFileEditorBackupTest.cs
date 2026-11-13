using System.Text;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Test.Dialogs;

/// <summary>The editor's auto-save backups (written while the text is unsaved, restored when newer than the file, removed on save) and Ctrl+F8 encoding conversion.</summary>
[TestClass]
public class TextFileEditorBackupTest
{
    private string _folder = string.Empty;
    private string _file = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iTerminal-editor-backup-" + Guid.NewGuid().ToString("N"));
        _file = Path.GetTempFileName();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }

        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }

    private static Url FileUrl(string path) => new("file://" + path.Replace('\\', '/'));

    private TextFileEditorDialog NewDialog(Url? url, EditorBackupStore? store)
    {
        var read = new Mock<IFileToTextConverterService>();
        read.Setup(f => f.ReadFileAsync(It.IsAny<Url>(), It.IsAny<IFileSystem>(), It.IsAny<TextModel>())).Returns(Task.CompletedTask);
        var dialog = new TextFileEditorDialog(
            Mock.Of<IContentModelService>(),
            read.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService(),
            store);
        dialog.Url = url;
        return dialog;
    }

    private static void Type(TextFileEditorDialog dialog, params string[] lines)
    {
        foreach (var line in lines)
        {
            dialog.TextEditorControl.TextModel.Lines.Add(line);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UnsavedText_IsBackedUp_OnIdle()
    {
        EditorBackupStore store = new(_folder);
        var dialog = NewDialog(FileUrl(_file), store);
        Type(dialog, "draft");
        dialog.TextEditorControl.IsDirty = true;

        dialog.OnIdle();
        await dialog.PendingBackup;

        var backup = store.Read(FileUrl(_file).FullUrl);
        Assert.IsNotNull(backup);
        CollectionAssert.AreEqual(new[] { "draft" }, backup.Lines);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CleanText_IsNotBackedUp()
    {
        EditorBackupStore store = new(_folder);
        var dialog = NewDialog(FileUrl(_file), store);
        Type(dialog, "saved");

        dialog.OnIdle();
        await dialog.PendingBackup;

        Assert.IsNull(store.Read(FileUrl(_file).FullUrl));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotOnTheLocalDisk_IsNotBackedUp()
    {
        EditorBackupStore store = new(_folder);
        var dialog = NewDialog(new Url("memdrive://notes.txt"), store);
        Type(dialog, "draft");
        dialog.TextEditorControl.IsDirty = true;

        dialog.OnIdle();
        await dialog.PendingBackup;

        Assert.IsFalse(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NoStore_MeansNoBackups()
    {
        var dialog = NewDialog(FileUrl(_file), null);
        Type(dialog, "draft");
        dialog.TextEditorControl.IsDirty = true;

        dialog.OnIdle();
        await dialog.PendingBackup;

        Assert.IsFalse(Directory.Exists(_folder));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABackupNewerThanTheFile_IsRestoredAsUnsavedEdits()
    {
        File.WriteAllText(_file, "on disk");
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddHours(-1));
        EditorBackupStore store = new(_folder);
        store.Write(FileUrl(_file).FullUrl, new EditorBackup(DateTime.UtcNow, ["unsaved words"], "\r\n", "utf-8"));

        var dialog = NewDialog(FileUrl(_file), store);
        await dialog.OnInitializeAsync();

        CollectionAssert.AreEqual(new[] { "unsaved words" }, dialog.TextEditorControl.TextModel.Lines.AsSnapshot().ToArray());
        Assert.IsTrue(dialog.TextEditorControl.IsDirty, "the restored text is unsaved");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABackupOlderThanTheFile_IsDiscarded_AndNotRestored()
    {
        EditorBackupStore store = new(_folder);
        store.Write(FileUrl(_file).FullUrl, new EditorBackup(DateTime.UtcNow.AddHours(-2), ["stale"], "\r\n", "utf-8"));
        File.WriteAllText(_file, "newer on disk");
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow);

        var dialog = NewDialog(FileUrl(_file), store);
        await dialog.OnInitializeAsync();
        await dialog.PendingBackup;

        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
        Assert.IsFalse(dialog.TextEditorControl.TextModel.Lines.Contains("stale"));
        Assert.IsNull(store.Read(FileUrl(_file).FullUrl), "the stale backup is removed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABackupThatMatchesTheFile_IsRemoved_AndNotMarkedUnsaved()
    {
        File.WriteAllText(_file, "same");
        File.SetLastWriteTimeUtc(_file, DateTime.UtcNow.AddHours(-1));
        EditorBackupStore store = new(_folder);
        store.Write(FileUrl(_file).FullUrl, new EditorBackup(DateTime.UtcNow, [], "\r\n", "utf-8"));

        var dialog = NewDialog(FileUrl(_file), store);
        await dialog.OnInitializeAsync();
        await dialog.PendingBackup;

        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
        Assert.IsNull(store.Read(FileUrl(_file).FullUrl));
    }

    // ---- encoding conversion ----

    [Timeout(60_000)]
    [TestMethod]
    public void ConvertEncoding_ChangesWhatIsSavedAndKeepsTheText()
    {
        var dialog = NewDialog(null, null);
        var model = dialog.TextEditorControl.TextModel;
        model.Encoding = Encoding.UTF8;
        Type(dialog, "tûrõ");

        dialog.ConvertEncoding();

        Assert.AreNotEqual(Encoding.UTF8.WebName, model.Encoding.WebName);
        CollectionAssert.AreEqual(new[] { "tûrõ" }, model.Lines.AsSnapshot().ToArray());
        Assert.IsTrue(dialog.TextEditorControl.IsDirty, "saving now writes different bytes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlF8_ConvertsTheEncoding_AndCtrlShiftF8_GoesTheOtherWay()
    {
        var dialog = NewDialog(null, null);
        var model = dialog.TextEditorControl.TextModel;
        model.Encoding = Encoding.UTF8;
        var start = model.Encoding.WebName;

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F8, false, false, true));
        var forward = model.Encoding.WebName;
        Assert.AreNotEqual(start, forward);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F8, true, false, true));
        Assert.AreEqual(start, model.Encoding.WebName, "Ctrl+Shift+F8 goes back to where it was");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ConvertEncoding_OfAReadOnlyEditor_DoesNothing()
    {
        var dialog = NewDialog(null, null);
        dialog.TextEditorControl.IsReadOnly = true;
        var before = dialog.TextEditorControl.TextModel.Encoding.WebName;

        dialog.ConvertEncoding();

        Assert.AreEqual(before, dialog.TextEditorControl.TextModel.Encoding.WebName);
        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
    }
}
