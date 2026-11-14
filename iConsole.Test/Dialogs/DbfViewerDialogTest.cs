using iConsole.Dialogs;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Common;
using Moq;

namespace iConsole.Test.Dialogs;

[TestClass]
public class DbfViewerDialogTest
{
    private static DbfViewerDialog CreateDialog(string path, byte[] content)
    {
        var document = new Url(path);
        Mock<IMemoryFile> file = new();
        file.SetupGet(f => f.Content).Returns(content);
        Mock<IContentModel> fileSystem = new();
        fileSystem.Setup(f => f.ReadFileAsync(document)).ReturnsAsync(file.Object);
        Mock<IContentModelService> models = new();
        models.Setup(m => m.GetFocusedModel()).Returns(fileSystem.Object);
        return new DbfViewerDialog(models.Object, new ThemeService()) { Url = document };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OpensTheExampleTableWithItsCountsAndDeletedRecords()
    {
        DbfViewerDialog dialog = CreateDialog("file:///data/example.dbf", SampleFiles.Bytes("example.dbf"));

        await dialog.OnInitializeAsync();

        Assert.IsNotNull(dialog.Document);
        Assert.AreEqual(3, dialog.Document.Rows.Count);
        StringAssert.Contains(dialog.Status, "3 record(s), 5 field(s)");
        StringAssert.Contains(dialog.Status, "1 deleted not shown");
        StringAssert.Contains(dialog.Status, "WINDOWS-1252");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFileThatIsNotATableSaysWhy()
    {
        DbfViewerDialog dialog = CreateDialog("file:///data/notes.dbf", new byte[100]);

        await dialog.OnInitializeAsync();

        Assert.IsNull(dialog.Document);
        StringAssert.StartsWith(dialog.Status, "Not a dBase table:");
    }
}
