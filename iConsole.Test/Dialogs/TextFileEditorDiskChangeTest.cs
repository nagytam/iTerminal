using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Dialogs.Test;

/// <summary>The editor follows its file when another program changes it on disk: a clean editor reloads, an edited one keeps its edits.</summary>
[TestClass]
public class TextFileEditorDiskChangeTest
{
    private string _path = string.Empty;
    private Mock<IContentModel> _model = null!;
    private Mock<IFileToTextConverterService> _converter = null!;
    private Mock<IContentModelService> _contentModelService = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), "itermdisk-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(_path, "one\n");

        // The file system the editor reads and saves through: the real bytes go to the temp file.
        _model = new Mock<IContentModel>();
        _model.Setup(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()))
            .Callback<Url, MemoryStream>((url, stream) => File.WriteAllBytes(url.Path, stream.ToArray()))
            .Returns(Task.CompletedTask);

        _converter = new Mock<IFileToTextConverterService>();
        _converter.Setup(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()))
            .Returns((Url? url, IFileSystem _, ITextModel text) =>
            {
                foreach (string line in File.ReadAllLines(url!.Path))
                {
                    text.Lines.Add(line);
                }

                return Task.CompletedTask;
            });

        _contentModelService = new Mock<IContentModelService>();
        _contentModelService.Setup(c => c.GetFocusedModel()).Returns(_model.Object);
        _contentModelService.Setup(c => c.GetLastContentModel()).Returns(_model.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private async Task<TextFileEditorDialog> OpenAsync()
    {
        TextFileEditorDialog dialog = new(
            _contentModelService.Object,
            _converter.Object,
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            new ThemeService())
        {
            Url = new Url(Container.FILE + _path),
        };
        await dialog.OnInitializeAsync();
        return dialog;
    }

    private static string Text(TextFileEditorDialog dialog) => string.Join("|", dialog.TextEditorControl.TextModel.Lines);

    [Timeout(60_000)]
    [TestMethod]
    public async Task CleanEditor_ReloadsWhenTheFileChanges()
    {
        TextFileEditorDialog dialog = await OpenAsync();
        Assert.AreEqual("one", Text(dialog));

        File.WriteAllText(_path, "two\nthree\n");
        await dialog.CheckDiskAsync();
        dialog.ApplyDiskChange();

        Assert.AreEqual("two|three", Text(dialog));
        Assert.IsFalse(dialog.TextEditorControl.IsDirty);
        Assert.IsFalse(dialog.Title.Contains("changed on disk"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EditedEditor_KeepsItsEditsAndSaysTheFileChanged()
    {
        TextFileEditorDialog dialog = await OpenAsync();
        dialog.TextEditorControl.IsDirty = true;

        File.WriteAllText(_path, "two\n");
        await dialog.CheckDiskAsync();
        dialog.ApplyDiskChange();

        Assert.AreEqual("one", Text(dialog));
        Assert.IsTrue(dialog.Title.Contains("changed on disk"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Save_ClearsTheChangedMarkAndIsNotReloadedAsAChange()
    {
        TextFileEditorDialog dialog = await OpenAsync();
        dialog.TextEditorControl.IsDirty = true;
        File.WriteAllText(_path, "two\n");
        await dialog.CheckDiskAsync();
        dialog.ApplyDiskChange();

        Assert.IsTrue(await dialog.SaveAsync());
        // The editor's own text, not the disk's "two": the save wrote what the editor shows.
        Assert.AreEqual("one", File.ReadAllText(_path));
        Assert.IsFalse(dialog.Title.Contains("changed on disk"));

        // The editor's own write is not a change: the next check reads nothing.
        await dialog.CheckDiskAsync();
        _converter.Verify(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Exactly(2));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Check_ReadsNothingWhenTheFileIsUnchanged()
    {
        TextFileEditorDialog dialog = await OpenAsync();

        await dialog.CheckDiskAsync();
        await dialog.CheckDiskAsync();

        _converter.Verify(c => c.ReadFileAsync(It.IsAny<Url?>(), It.IsAny<IFileSystem>(), It.IsAny<ITextModel>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RemoteFiles_AreNotWatched()
    {
        Assert.IsNull(TextFileEditorDialog.StampOf(new Url("ssh://host/home/x/notes.txt")));
        Assert.IsNull(TextFileEditorDialog.StampOf(null));
        Assert.IsNotNull(TextFileEditorDialog.StampOf(new Url(Container.FILE + _path)));
    }
}
