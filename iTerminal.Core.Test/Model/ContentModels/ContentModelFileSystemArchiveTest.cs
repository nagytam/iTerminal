using System.IO.Compression;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Model.ContentModels.Test;

/// <summary>Browsing inside a zip: each folder of the archive lists only its own entries.</summary>
[TestClass]
public class ContentModelFileSystemArchiveTest
{
    private string _zip = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _zip = Path.Combine(Path.GetTempPath(), "ArchiveModelTest_" + Guid.NewGuid().ToString("N")[..8] + ".zip");
        using var archive = ZipFile.Open(_zip, ZipArchiveMode.Create);
        archive.CreateEntry("top.txt");
        archive.CreateEntry("sub/nested.txt");
        archive.CreateEntry("sub/deep/x.txt");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            File.Delete(_zip);
        }
        catch
        {
        }
    }

    private static ContentModelFileSystemArchive NewModel()
    {
        var guard = new Mock<IFrequentExecutionGuardService>();
        guard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        var settings = new Mock<ISettingsService>();
        return new ContentModelFileSystemArchive(
            "Test",
            new Moq.Mock<iConsole.Tasks.TaskManager>().Object,
            settings.Object,
            new FolderHistoryService(settings.Object),
            guard.Object,
            Mock.Of<IArchiveFileSystemService>());
    }

    [TestMethod]
    public void TheRootOfTheArchive_ListsItsOwnEntries_AndTheDotDotRow()
    {
        var model = NewModel();

        model.SetUrl(new Url(Container.ZIP_ARCHIVE + _zip + "@/"));

        var names = model.Entries.Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "top.txt");
        CollectionAssert.Contains(names, "..");
    }

    [TestMethod]
    public void AFolderInsideTheArchive_ListsOnlyItsOwnEntries()
    {
        var model = NewModel();

        model.SetUrl(new Url(Container.ZIP_ARCHIVE + _zip + "@/sub/"));

        var names = model.Entries.Select(e => e.Name).ToList();
        CollectionAssert.Contains(names, "nested.txt", "the folder's own file is listed");
        CollectionAssert.DoesNotContain(names, "top.txt", "a file at the root of the archive is not inside sub/");
    }

    [TestMethod]
    public void TheParentRow_IsNamedDotDot_AndIsRecognisedAsTheParent()
    {
        var model = NewModel();
        model.SetUrl(new Url(Container.ZIP_ARCHIVE + _zip + "@/"));

        var parent = model.Entries.First();

        Assert.AreEqual("..", parent.DisplayName);
        Assert.IsTrue(iTerminal.Core.Commands.FocusedEntry.IsParentEntry(parent));
    }

    [TestMethod]
    public void InsideASubFolder_TheParentRowGoesUpOneFolderInTheArchive()
    {
        var model = NewModel();
        model.SetUrl(new Url(Container.ZIP_ARCHIVE + _zip + "@/sub/"));

        var parent = model.Entries.First(e => e.DisplayName == "..");

        StringAssert.EndsWith(parent.Url.FullUrl, "@/");
    }

    [TestMethod]
    public void ASubFolderOfASubFolder_ShowsAsOneFolderRow()
    {
        var model = NewModel();

        model.SetUrl(new Url(Container.ZIP_ARCHIVE + _zip + "@/sub/"));

        var names = model.Entries.Select(e => e.DisplayName).ToList();
        Assert.AreEqual(1, names.Count(n => n == "deep"), "one row for the folder, however many entries lie under it");
    }

    [TestMethod]
    public void AnArchiveThatIsGone_ShowsOnlyTheWayOut_AndDoesNotThrow()
    {
        var model = NewModel();
        var gone = Path.Combine(Path.GetTempPath(), "gone-" + Guid.NewGuid().ToString("N") + ".zip");

        model.SetUrl(new Url(Container.ZIP_ARCHIVE + gone + "@/"));

        Assert.AreEqual(1, model.Entries.Count);
        Assert.AreEqual("..", model.Entries.First().DisplayName);
    }
}
