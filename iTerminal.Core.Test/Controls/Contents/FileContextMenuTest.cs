using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Controls.Contents;
using Moq;

namespace iTerminal.Core.Test.Controls.Contents;

/// <summary>What the right-click menu offers — worked out from the commands the panel already has.</summary>
[TestClass]
public class FileContextMenuTest
{
    private static Mock<IContentModel> ModelWithFocusedEntry(string name)
    {
        var entry = new Mock<IEntry>();
        entry.SetupGet(e => e.DisplayName).Returns(name);
        entry.SetupGet(e => e.Url).Returns(new Url(Path.Combine(Path.GetTempPath(), name)));
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        return model;
    }

    /// <summary>A model whose row is <paramref name="url"/>, with the panel service handing that model out.</summary>
    private static iTerminal.Core.Commands.PrintDialogCommand PrintFor(string path)
    {
        var entry = new Mock<IEntry>();
        entry.SetupGet(e => e.DisplayName).Returns(Path.GetFileName(path));
        entry.SetupGet(e => e.Url).Returns(new Url(path));
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        model.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(!Directory.Exists(path));
        model.Setup(m => m.ExistsFolder(It.IsAny<Url>())).Returns(Directory.Exists(path));
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model.Object);
        return new iTerminal.Core.Commands.PrintDialogCommand(null!, service.Object);
    }

    [TestMethod]
    public void Print_IsOfferedOnALocalFile_ButNotOnAFolder()
    {
        var folder = Directory.CreateTempSubdirectory("iTerminal.Menu.");
        try
        {
            var file = Path.Combine(folder.FullName, "report.txt");
            File.WriteAllText(file, "x");
            var onFile = PrintFor(file);
            var onFolder = PrintFor(folder.FullName);

            var fileRows = FileContextMenu.Build(ModelWithFocusedEntry("report.txt").Object, [onFile], null, () => Task.CompletedTask);
            var folderRows = FileContextMenu.Build(ModelWithFocusedEntry("sub").Object, [onFolder], null, () => Task.CompletedTask);

            Assert.IsTrue(onFile.CanExecute() && onFolder.CanExecute(), "Print always runs: it is the queue manager too");
            Assert.IsTrue(fileRows.Any(r => r.Label.StartsWith("Print")));
            Assert.IsFalse(folderRows.Any(r => r.Label.StartsWith("Print")));
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [TestMethod]
    public void Print_IsNotOfferedOnAnotherDrivesRow()
    {
        var remote = PrintFor("memdrive://docs/report.txt");

        var rows = FileContextMenu.Build(ModelWithFocusedEntry("report.txt").Object, [remote], null, () => Task.CompletedTask);

        Assert.IsFalse(remote.HasPrintableFile);
        Assert.IsFalse(rows.Any(r => r.Label.StartsWith("Print")));
    }

    [TestMethod]
    public void NothingIsOfferedOnTheParentRow()
    {
        var model = ModelWithFocusedEntry("..");

        Assert.AreEqual(0, FileContextMenu.Build(model.Object, [], null, () => Task.CompletedTask).Count);
    }

    [TestMethod]
    public void NothingIsOfferedWithoutARow()
    {
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns((IEntry?)null);

        Assert.AreEqual(0, FileContextMenu.Build(model.Object, [], null, () => Task.CompletedTask).Count);
    }

    [TestMethod]
    public async Task OpenIsAlwaysFirstAndRunsWhatEnterDoes()
    {
        var model = ModelWithFocusedEntry("notes.txt");
        var opened = false;

        var items = FileContextMenu.Build(model.Object, [], null, () =>
        {
            opened = true;
            return Task.CompletedTask;
        });

        Assert.AreEqual(1, items.Count);
        StringAssert.StartsWith(items[0].Label, "Open");
        await items[0].Run();
        Assert.IsTrue(opened);
    }
}
