using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiPanelToolsMoreOperationsTest
{
    private sealed class Fixture
    {
        public Mock<IContentModel> Left { get; } = new();
        public Mock<IContentModelService> Service { get; } = new();
        public Mock<IAiActionApprovalService> Approval { get; } = new();
        public Mock<ISettingsService> Settings { get; } = new();
        public Mock<ISortableContentModel> LeftSort { get; }
        public RenameUndoService Undo { get; } = new();
        public List<IEntry> Entries { get; } = [];
        public string Folder { get; }
        public AiPanelTools Tools { get; }

        public Fixture(IChecksumService? checksum = null)
        {
            Folder = Path.Combine(Path.GetTempPath(), "ai-more-" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(Folder);
            Left.Setup(m => m.Name).Returns(Content.Left);
            Left.Setup(m => m.Url).Returns(new Url(Folder));
            Left.Setup(m => m.FilteredEntries).Returns(Entries);
            LeftSort = Left.As<ISortableContentModel>();
            Service.Setup(s => s.ContentModels).Returns([Left.Object]);
            Service.Setup(s => s.GetFocusedModel()).Returns(Left.Object);
            Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Tools = new AiPanelTools(
                Service.Object,
                approvalService: Approval.Object,
                settingsService: Settings.Object,
                checksumService: checksum,
                renameUndoService: Undo);
        }

        public IEntry AddEntry(string name)
        {
            var entry = new Mock<IEntry>();
            entry.Setup(e => e.DisplayName).Returns(name);
            entry.Setup(e => e.Name).Returns(name);
            entry.Setup(e => e.Url).Returns(new Url(Path.Combine(Folder, name)));
            Entries.Add(entry.Object);
            return entry.Object;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_IncludesTheMorePanelTools()
    {
        var names = new AiPanelTools(Mock.Of<IContentModelService>()).Create().Select(t => t.Name).ToList();

        Assert.AreEqual(names.Count, names.Distinct().Count());
        foreach (string expected in new[] { "go_to_path", "go_to_root_folder", "set_hidden_files_visible", "create_file", "calculate_folder_size", "get_entry_properties", "compute_checksum", "find_files", "multi_rename", "undo_last_rename" })
        {
            CollectionAssert.Contains(names, expected);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoToPath_NavigatesThePanelAndRejectsAnEmptyPath()
    {
        var f = new Fixture();

        string result = await f.Tools.GoToPath(f.Folder, "left");

        StringAssert.Contains(result, "now at");
        f.Service.Verify(s => s.SetUrlAsync(Content.Left, It.IsAny<Url>()), Times.Once);
        StringAssert.StartsWith(await f.Tools.GoToPath("  ", "left"), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GoToRootFolder_ReportsAFailedNavigationInsteadOfThrowing()
    {
        var f = new Fixture();
        f.Service.Setup(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>())).ThrowsAsync(new IOException("the share is gone"));

        string result = await f.Tools.GoToRootFolder("left");

        StringAssert.StartsWith(result, "Error:");
        StringAssert.Contains(result, "the share is gone");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MultiRename_ACaseOnlyChangeIsNotACollisionWithItself()
    {
        var f = new Fixture();
        f.AddEntry("img_1.jpg");
        // On a case-insensitive file system the new name finds the entry being renamed.
        f.Left.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(true);

        string preview = await f.Tools.MultiRename("img_1", "IMG_1", "left", "img_1.jpg");

        StringAssert.Contains(preview, "IMG_1.jpg");
        Assert.IsFalse(preview.Contains("already exists"), preview);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MultiRename_AFailedMoveIsReportedAndLeftOutOfTheUndoBatch()
    {
        var f = new Fixture();
        f.AddEntry("IMG_1.jpg");
        f.AddEntry("IMG_2.jpg");
        f.Left.Setup(m => m.MoveAsync(It.Is<Url>(u => u.FullUrl.EndsWith("IMG_2.jpg")), It.IsAny<Url>(), false))
            .ThrowsAsync(new IOException("file is locked"));

        string done = await f.Tools.MultiRename("IMG_", "holiday_", "left", "IMG_1.jpg;IMG_2.jpg", confirmed: true);

        StringAssert.Contains(done, "Renamed 1");
        StringAssert.Contains(done, "file is locked");
        IReadOnlyList<RenameUndoService.Rename> batch = f.Undo.TakeAll();
        Assert.AreEqual(1, batch.Count, "only the rename that happened can be undone");
        StringAssert.EndsWith(batch[0].Source.FullUrl, "IMG_1.jpg");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetHiddenFilesVisible_SavesTheSettingAndAppliesItToThePanels()
    {
        var f = new Fixture();

        f.Tools.SetHiddenFilesVisible(false);

        f.Settings.Verify(s => s.Set(PanelViewOptions.ShowHiddenKey, "False"), Times.Once);
        f.LeftSort.Verify(s => s.SetViewOptions(It.Is<PanelViewOptions>(o => !o.ShowHidden)), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateFile_PreviewsFirstThenWritesAfterApproval()
    {
        var f = new Fixture();

        string preview = await f.Tools.CreateFile("notes.txt", "left");
        f.Left.Verify(m => m.WriteFileAsync(It.IsAny<Url>(), It.IsAny<MemoryStream>()), Times.Never);
        StringAssert.StartsWith(preview, "Preview, nothing changed");

        string done = await f.Tools.CreateFile("notes.txt", "left", confirmed: true);
        StringAssert.StartsWith(done, "Created");
        f.Left.Verify(m => m.WriteFileAsync(It.Is<Url>(u => u.FullUrl.EndsWith("notes.txt")), It.IsAny<MemoryStream>()), Times.Once);
        StringAssert.StartsWith(await f.Tools.CreateFile("a/b.txt", "left"), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MultiRename_PreviewsThenRenamesAndRemembersTheBatchForUndo()
    {
        var f = new Fixture();
        f.AddEntry("IMG_1.jpg");
        f.AddEntry("IMG_2.jpg");
        f.AddEntry("other.txt");

        string preview = await f.Tools.MultiRename("IMG_", "holiday_", "left", "IMG_1.jpg;IMG_2.jpg");
        StringAssert.Contains(preview, "holiday_1.jpg");
        f.Left.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);

        string done = await f.Tools.MultiRename("IMG_", "holiday_", "left", "IMG_1.jpg;IMG_2.jpg", confirmed: true);

        StringAssert.Contains(done, "Renamed 2");
        f.Left.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), false), Times.Exactly(2));
        Assert.IsNotNull(f.Undo.Peek());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MultiRename_RefusesWhenAnotherEntryWouldGetTheSameName()
    {
        var f = new Fixture();
        f.AddEntry("a-1.txt");
        f.AddEntry("b-1.txt");

        string result = await f.Tools.MultiRename("a-", "", "left", "a-1.txt;b-1.txt", confirmed: false);
        StringAssert.Contains(result, "1.txt");

        string collision = await f.Tools.MultiRename("a", "b", "left", "a-1.txt;b-1.txt");
        StringAssert.Contains(collision, "Preview");
        StringAssert.Contains(await f.Tools.MultiRename("zzz", "y", "left", "a-1.txt"), "no name contains");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UndoLastRename_RestoresWhenApproved()
    {
        var f = new Fixture();
        Mock<IContentModel> model = f.Left;
        Url source = new(Path.Combine(f.Folder, "old.txt"));
        Url target = new(Path.Combine(f.Folder, "new.txt"));
        f.Undo.Remember(model.Object, source, target);
        model.Setup(m => m.ExistsFile(It.Is<Url>(u => u.FullUrl == target.FullUrl))).Returns(true);

        Assert.IsTrue((await f.Tools.UndoLastRename()).StartsWith("Preview"));
        string done = await f.Tools.UndoLastRename(confirmed: true);

        StringAssert.Contains(done, "Restored 1");
        model.Verify(m => m.MoveAsync(It.Is<Url>(u => u.FullUrl == target.FullUrl), It.Is<Url>(u => u.FullUrl == source.FullUrl), false), Times.Once);
        StringAssert.Contains(await f.Tools.UndoLastRename(), "no rename");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FindFiles_ReturnsMatchingPathsAndNeverFileContent()
    {
        var f = new Fixture();
        try
        {
            Directory.CreateDirectory(Path.Combine(f.Folder, "sub"));
            File.WriteAllText(Path.Combine(f.Folder, "sub", "hit.log"), "needle inside");
            File.WriteAllText(Path.Combine(f.Folder, "miss.txt"), "nothing");

            string byName = await f.Tools.FindFiles("*.log", "left");
            StringAssert.Contains(byName, "hit.log");
            Assert.IsFalse(byName.Contains("miss.txt"));

            string byText = await f.Tools.FindFiles("*", "left", containingText: "needle");
            StringAssert.Contains(byText, "hit.log");
            Assert.IsFalse(byText.Contains("needle inside"));

            StringAssert.Contains(await f.Tools.FindFiles("*.nothing", "left"), "Nothing found");
        }
        finally
        {
            Directory.Delete(f.Folder, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComputeChecksum_ReturnsOnlyTheHashOfTheChosenAlgorithm()
    {
        var checksum = new Mock<IChecksumService>();
        var f = new Fixture(checksum.Object);

        // Without a file system resolver the tool says so instead of throwing.
        StringAssert.StartsWith(await f.Tools.ComputeChecksum("x.txt", "left"), "Error:");
        StringAssert.StartsWith(await f.Tools.ComputeChecksum("x.txt", "left", "crc"), "Error:");
    }
}
