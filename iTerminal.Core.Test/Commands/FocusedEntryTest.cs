using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Commands;
using Moq;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// What F5, Shift+F6, F6 and F8 may act on. The ".." row's URL is the parent folder, which exists,
/// so every one of them used to accept it — F8 on ".." offered to delete the folder the user was in,
/// recursively.
/// </summary>
[TestClass]
public class FocusedEntryTest
{
    internal static Mock<IContentModel> ModelFocusedOn(string displayName, string url, bool isFile = false, bool isFolder = false)
    {
        var entryUrl = new Url(url);
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.DisplayName).Returns(displayName);
        entry.Setup(e => e.Url).Returns(entryUrl);
        var model = new Mock<IContentModel>();
        model.Setup(m => m.GetFocusedEntry()).Returns(entry.Object);
        model.Setup(m => m.ExistsFile(entryUrl)).Returns(isFile);
        model.Setup(m => m.ExistsFolder(entryUrl)).Returns(isFolder);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentRowIsNeverActedOn()
    {
        var model = ModelFocusedOn("..", "file:///tmp/", isFolder: true);

        Assert.IsNull(FocusedEntry.ActionableUrl(model.Object));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentRowIsRecognisedWithPadding()
    {
        var entry = new Mock<IEntry>();
        entry.Setup(e => e.DisplayName).Returns(".. ");

        Assert.IsTrue(FocusedEntry.IsParentEntry(entry.Object));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFileIsActedOn()
    {
        var model = ModelFocusedOn("readme.txt", "file:///tmp/readme.txt", isFile: true);

        Assert.AreEqual("file:///tmp/readme.txt", FocusedEntry.ActionableUrl(model.Object)?.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AFolderIsActedOn()
    {
        var model = ModelFocusedOn("alpha", "file:///tmp/alpha/", isFolder: true);

        Assert.IsNotNull(FocusedEntry.ActionableUrl(model.Object));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnEntryThatIsGoneIsNotActedOn()
    {
        var model = ModelFocusedOn("deleted.txt", "file:///tmp/deleted.txt");

        Assert.IsNull(FocusedEntry.ActionableUrl(model.Object));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NoModelOrNoEntryMeansNothing()
    {
        Assert.IsNull(FocusedEntry.ActionableUrl(null));
        Assert.IsNull(FocusedEntry.ActionableUrl(Mock.Of<IContentModel>()));
        Assert.IsFalse(FocusedEntry.IsParentEntry(null));
    }
}
