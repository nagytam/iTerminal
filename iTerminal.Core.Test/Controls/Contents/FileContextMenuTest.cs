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
