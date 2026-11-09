using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Preview;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.AI;

[TestClass]
public class AiPanelToolsTest
{
    private sealed class Fixture
    {
        public Mock<IContentModel> Left { get; } = new();
        public Mock<IContentModel> Right { get; } = new();
        public Mock<IMarkableContentModel> LeftMarks { get; }
        public Mock<ISortableContentModel> LeftSort { get; }
        public Mock<IContentModelService> Service { get; } = new();
        public Mock<IAiActionApprovalService> Approval { get; } = new();
        public Mock<IFileSystemService> FileSystem { get; } = new();
        public Mock<IFilePreviewService> FilePreview { get; } = new();
        public AiPanelTools Tools { get; }
        public List<IEntry> LeftEntries { get; } = [];

        public Fixture()
        {
            Left.Setup(m => m.Name).Returns(Content.Left);
            Right.Setup(m => m.Name).Returns(Content.Right);
            Left.Setup(m => m.Url).Returns(new Url(Path.GetTempPath()));
            Right.Setup(m => m.Url).Returns(new Url(Path.Combine(Path.GetTempPath(), "right") + Path.DirectorySeparatorChar));
            Left.Setup(m => m.FilteredEntries).Returns(LeftEntries);
            LeftMarks = Left.As<IMarkableContentModel>();
            LeftMarks.Setup(m => m.CanMark).Returns(true);
            LeftSort = Left.As<ISortableContentModel>();
            LeftSort.Setup(m => m.CanSort).Returns(true);
            LeftSort.Setup(m => m.Sort).Returns(PanelSort.AsListed);

            Service.Setup(s => s.ContentModels).Returns([Left.Object, Right.Object]);
            Service.Setup(s => s.GetFocusedModel()).Returns(Left.Object);
            FileSystemResolver resolver = new(
                FileSystem.Object,
                Mock.Of<IMemoryDriveService>(),
                Mock.Of<ISshFileSystemService>(),
                Mock.Of<IArchiveFileSystemService>(),
                Service.Object);
            Tools = new AiPanelTools(
                Service.Object,
                fileSystemResolver: resolver,
                approvalService: Approval.Object,
                filePreviewService: FilePreview.Object);
        }

        public IEntry AddEntry(string name, bool folder = false)
        {
            var entry = new Mock<IEntry>();
            entry.Setup(e => e.DisplayName).Returns(name);
            entry.Setup(e => e.Name).Returns(name);
            entry.Setup(e => e.FileSize).Returns("1 KB");
            string path = Path.Combine(Path.GetTempPath(), name) + (folder ? Path.DirectorySeparatorChar : string.Empty);
            entry.Setup(e => e.Url).Returns(new Url(path));
            LeftEntries.Add(entry.Object);
            return entry.Object;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Create_ToolNames_AreUniqueAndCoverThePanelFunctions()
    {
        var names = new AiPanelTools(Mock.Of<IContentModelService>()).Create().Select(t => t.Name).ToList();

        Assert.AreEqual(names.Count, names.Distinct().Count());
        foreach (string expected in new[] { "get_panel_state", "list_panel_entries", "read_panel_file", "mark_entries", "set_panel_sort", "set_panel_filter", "copy_to_other_panel", "move_to_other_panel", "delete_entries", "create_folder", "rename_entry" })
        {
            CollectionAssert.Contains(names, expected);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPanelFile_ApprovesAndDisclosesBeforeReadingCursorFile()
    {
        var f = new Fixture();
        IEntry entry = f.AddEntry("readme.md");
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(entry);
        var calls = new List<string>();
        AiActionApprovalRequest? request = null;
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) =>
            {
                calls.Add("approval");
                request = value;
            })
            .ReturnsAsync(true);
        f.FilePreview.Setup(s => s.BuildAsync(It.IsAny<IEntry>(), It.IsAny<IFileSystem>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<IEntry, IFileSystem, int, CancellationToken>((_, fileSystem, width, _) =>
            {
                calls.Add("preview");
                Assert.AreSame(f.FileSystem.Object, fileSystem);
                Assert.AreEqual(512, width);
            })
            .ReturnsAsync(new FilePreview("readme.md", "text", ["Project overview"]));

        string result = await f.Tools.ReadPanelFile();

        CollectionAssert.AreEqual(new[] { "approval", "preview" }, calls);
        StringAssert.Contains(request!.Summary, entry.Url.FullUrl);
        StringAssert.Contains(request.Summary, "configured AI provider");
        StringAssert.Contains(result, "Project overview");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPanelFile_UsesExactVisibleNameAndRejectsPathsFoldersAndAmbiguousNames()
    {
        var f = new Fixture();
        IEntry cursor = f.AddEntry("cursor.txt");
        IEntry target = f.AddEntry("target.txt");
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(cursor);
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        f.FilePreview.Setup(s => s.BuildAsync(It.IsAny<IEntry>(), It.IsAny<IFileSystem>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FilePreview("target.txt", "text", ["target content"]));

        string namedResult = await f.Tools.ReadPanelFile("target.txt", "left");

        StringAssert.Contains(namedResult, "target content");
        f.FilePreview.Verify(s => s.BuildAsync(
            It.Is<IEntry>(entry => entry.Url.FullUrl == target.Url.FullUrl),
            It.IsAny<IFileSystem>(),
            It.IsAny<int>(),
            It.IsAny<CancellationToken>()), Times.Once);

        StringAssert.StartsWith(await f.Tools.ReadPanelFile("../secret.txt", "left"), "Error:");
        IEntry folder = f.AddEntry("folder", folder: true);
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(folder);
        StringAssert.StartsWith(await f.Tools.ReadPanelFile(panel: "left"), "Error:");

        f.LeftEntries.Clear();
        f.AddEntry("NOTES.txt");
        f.AddEntry("Notes.TXT");
        StringAssert.Contains(await f.Tools.ReadPanelFile("notes.txt", "left"), "ambiguous");
        f.Approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        f.FilePreview.Verify(s => s.BuildAsync(It.IsAny<IEntry>(), It.IsAny<IFileSystem>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPanelFile_DeclinedApprovalDoesNotRead()
    {
        var f = new Fixture();
        IEntry entry = f.AddEntry("private.txt");
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(entry);
        AiActionApprovalRequest? request = null;
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(false);

        string result = await f.Tools.ReadPanelFile();

        StringAssert.Contains(result, "Not read");
        StringAssert.Contains(request!.Summary, entry.Url.FullUrl);
        StringAssert.Contains(request.Summary, UiText.Get("Ai.Approval.ReadFileSummary", "AI requests reading the preview of {0}.").Split("{0}")[0].Trim());
        f.FilePreview.Verify(s => s.BuildAsync(It.IsAny<IEntry>(), It.IsAny<IFileSystem>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadPanelFile_ApprovalBindsImmutableTargetAcrossEntryAndPanelChanges()
    {
        var f = new Fixture();
        IEntry entry = f.AddEntry("approved.txt");
        Mock<IEntry> mutableEntry = Mock.Get(entry);
        Url approvedUrl = entry.Url;
        Url changedUrl = new(Path.Combine(Path.GetTempPath(), "changed.txt"));
        mutableEntry.SetupGet(e => e.Url).Returns(() => approvedUrl);
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(entry);
        IContentModel activePanel = f.Left.Object;
        f.Service.Setup(s => s.GetFocusedModel()).Returns(() => activePanel);

        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((_, _) =>
            {
                approvedUrl = changedUrl;
                activePanel = f.Right.Object;
                f.Left.Setup(m => m.GetFocusedEntry()).Returns(f.AddEntry("other.txt"));
            })
            .ReturnsAsync(true);

        IEntry? previewEntry = null;
        IFileSystem? previewFileSystem = null;
        f.FilePreview.Setup(s => s.BuildAsync(It.IsAny<IEntry>(), It.IsAny<IFileSystem>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<IEntry, IFileSystem, int, CancellationToken>((value, fileSystem, _, _) =>
            {
                previewEntry = value;
                previewFileSystem = fileSystem;
            })
            .ReturnsAsync(new FilePreview("approved.txt", "text", ["captured content"]));

        await f.Tools.ReadPanelFile();

        Assert.IsNotNull(previewEntry);
        Assert.AreNotSame(entry, previewEntry);
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "approved.txt"), previewEntry.Url.FullName);
        Assert.AreSame(f.FileSystem.Object, previewFileSystem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ResolvePanel_LeftRightFocusedOther()
    {
        var f = new Fixture();

        Assert.AreSame(f.Left.Object, f.Tools.ResolvePanel("left"));
        Assert.AreSame(f.Right.Object, f.Tools.ResolvePanel("RIGHT"));
        Assert.AreSame(f.Left.Object, f.Tools.ResolvePanel(null));
        Assert.AreSame(f.Right.Object, f.Tools.ResolvePanel("other"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListPanelEntries_ShowsCursorMarksAndFolders()
    {
        var f = new Fixture();
        IEntry a = f.AddEntry("a.txt");
        IEntry dir = f.AddEntry("docs", folder: true);
        f.Left.Setup(m => m.GetFocusedEntry()).Returns(dir);
        f.LeftMarks.Setup(m => m.IsMarked(a)).Returns(true);

        string text = f.Tools.ListPanelEntries("left");

        StringAssert.Contains(text, " * a.txt");
        StringAssert.Contains(text, ">  docs/");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ListPanelEntries_CapsAtMax()
    {
        var f = new Fixture();
        for (int i = 0; i < 5; i++)
        {
            f.AddEntry($"f{i}.txt");
        }

        string text = f.Tools.ListPanelEntries("left", max: 2);

        StringAssert.Contains(text, "3 more not shown");
        Assert.IsFalse(text.Contains("f4.txt"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UnknownPanel_ReportsAnErrorInsteadOfThrowing()
    {
        var f = new Fixture();
        f.Service.Setup(s => s.GetFocusedModel()).Returns((IContentModel?)null);
        f.Service.Setup(s => s.GetLastContentModel()).Returns((IContentModel?)null);

        StringAssert.StartsWith(f.Tools.GetPanelState(), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MarkEntries_PassesPatternsToTheModel()
    {
        var f = new Fixture();
        f.LeftMarks.Setup(m => m.SetMarks("*.cs", true, false)).Returns(3);
        f.LeftMarks.Setup(m => m.MarkedCount).Returns(3);

        string text = f.Tools.MarkEntries("*.cs", "left");

        StringAssert.Contains(text, "Marked 3");
        f.LeftMarks.Verify(m => m.SetMarks("*.cs", true, false), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SetPanelSort_ParsesModeAndDirection_AndRejectsGarbage()
    {
        var f = new Fixture();

        f.Tools.SetPanelSort("size", "left", descending: true);
        f.LeftSort.Verify(m => m.SetSort(new PanelSort(PanelSortMode.Size, true)), Times.Once);

        f.Tools.SetPanelSort("as_listed", "left", descending: true);
        f.LeftSort.Verify(m => m.SetSort(new PanelSort(PanelSortMode.AsListed, false)), Times.Once);

        StringAssert.StartsWith(f.Tools.SetPanelSort("colour", "left"), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void MoveCursorToEntry_FindsByPrefix_AndReportsAmbiguity()
    {
        var f = new Fixture();
        f.AddEntry("alpha.txt");
        f.AddEntry("alpine.txt");
        f.AddEntry("beta.txt");
        var cursor = new Position(0, 0);
        f.Left.Setup(m => m.CursorPosition).Returns(cursor);

        StringAssert.Contains(f.Tools.MoveCursorToEntry("al", "left"), "ambiguous");
        f.Tools.MoveCursorToEntry("bet", "left");

        Assert.AreEqual(2, cursor.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteEntries_WithoutConfirmation_DeletesNothing()
    {
        var f = new Fixture();
        f.AddEntry("keep.txt");

        string text = await f.Tools.DeleteEntries("left", "keep.txt", confirmed: false);

        StringAssert.Contains(text, "Not deleted");
        f.Left.Verify(m => m.Delete(It.IsAny<Url>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
        f.Approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteEntries_ConfirmedButDeclined_DeletesNothing()
    {
        var f = new Fixture();
        f.AddEntry("keep.txt");
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        string text = await f.Tools.DeleteEntries("left", "keep.txt", confirmed: true);

        StringAssert.Contains(text, "declined or cancelled");
        f.Left.Verify(m => m.Delete(It.IsAny<Url>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteEntries_Approved_MovesNamedEntryToTheRecycleBin()
    {
        var f = new Fixture();
        f.AddEntry("gone.txt");
        AiActionApprovalRequest? request = null;
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(true);

        string text = await f.Tools.DeleteEntries("left", "gone.txt", confirmed: true);

        StringAssert.Contains(text, "Deleted");
        f.Left.Verify(m => m.Delete(It.IsAny<Url>(), false, true), Times.Once);
        Assert.AreEqual(new Url(Path.Combine(Path.GetTempPath(), "gone.txt")).FullUrl, request!.Actions.Single());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CopyToOtherPanel_RefusesToOverwrite()
    {
        var f = new Fixture();
        f.AddEntry("same.txt");
        f.Right.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns(true);

        string text = await f.Tools.CopyToOtherPanel("left", "same.txt");

        StringAssert.Contains(text, "already exists");
        f.Left.Verify(m => m.CopyAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CopyToOtherPanel_WithoutConfirmationOnlyPreviews()
    {
        var f = new Fixture();
        f.AddEntry("source.txt");

        string preview = await f.Tools.CopyToOtherPanel("left", "source.txt");

        StringAssert.Contains(preview, "Preview, nothing changed");
        StringAssert.Contains(preview, "source.txt");
        f.Left.Verify(m => m.CopyAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
        f.Approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CopyToOtherPanel_Approved_CopiesOnlyTheShownTarget()
    {
        var f = new Fixture();
        IEntry source = f.AddEntry("source.txt");
        f.Left.Setup(m => m.CopyAsync(It.IsAny<Url>(), It.IsAny<Url>(), false)).Returns(Task.CompletedTask);
        AiActionApprovalRequest? request = null;
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(true);

        string result = await f.Tools.CopyToOtherPanel("left", "source.txt", confirmed: true);

        StringAssert.Contains(result, "Copied");
        f.Left.Verify(m => m.CopyAsync(source.Url!, It.Is<Url>(url => url.FullUrl.Contains("right", StringComparison.Ordinal)), false), Times.Once);
        StringAssert.Contains(request!.Actions.Single(), source.Url!.FullUrl);
        StringAssert.Contains(request.Actions.Single(), "right");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MoveToOtherPanel_ConfirmedButDeclined_LeavesSourceUntouched()
    {
        var f = new Fixture();
        f.AddEntry("source.txt");
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        string result = await f.Tools.MoveToOtherPanel("left", "source.txt", confirmed: true);

        StringAssert.Contains(result, "declined or cancelled");
        f.Left.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MoveToOtherPanel_Approved_MovesNamedEntry()
    {
        var f = new Fixture();
        IEntry source = f.AddEntry("source.txt");
        f.Left.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), false)).Returns(Task.CompletedTask);
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string result = await f.Tools.MoveToOtherPanel("left", "source.txt", confirmed: true);

        StringAssert.Contains(result, "Moved");
        f.Left.Verify(m => m.MoveAsync(source.Url!, It.Is<Url>(url => url.FullUrl.Contains("right", StringComparison.Ordinal)), false), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateFolder_WithoutConfirmationOnlyPreviews()
    {
        var f = new Fixture();

        string preview = await f.Tools.CreateFolder("new-folder", "left");

        StringAssert.Contains(preview, "Preview, nothing changed");
        StringAssert.Contains(preview, "new-folder");
        f.Left.Verify(m => m.CreateFolderAsync(It.IsAny<Url>()), Times.Never);
        f.Approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateFolder_ConfirmedButDeclined_CreatesNothing()
    {
        var f = new Fixture();
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        string result = await f.Tools.CreateFolder("new-folder", "left", confirmed: true);

        StringAssert.Contains(result, "declined or cancelled");
        f.Left.Verify(m => m.CreateFolderAsync(It.IsAny<Url>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateFolder_Approved_CreatesTheShownFolder()
    {
        var f = new Fixture();
        f.Left.Setup(m => m.CreateFolderAsync(It.IsAny<Url>())).Returns(Task.CompletedTask);
        AiActionApprovalRequest? request = null;
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(true);

        string result = await f.Tools.CreateFolder("new-folder", "left", confirmed: true);

        StringAssert.Contains(result, "Created");
        StringAssert.Contains(request!.Actions.Single(), "new-folder");
        f.Left.Verify(m => m.CreateFolderAsync(It.Is<Url>(url => url.FullUrl.Contains("new-folder", StringComparison.Ordinal))), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RenameEntry_WithoutConfirmationOnlyPreviews()
    {
        var f = new Fixture();
        f.AddEntry("old.txt");

        string preview = await f.Tools.RenameEntry("new.txt", "old.txt", "left");

        StringAssert.Contains(preview, "Preview, nothing changed");
        StringAssert.Contains(preview, "old.txt");
        StringAssert.Contains(preview, "new.txt");
        f.Left.Verify(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), It.IsAny<bool>()), Times.Never);
        f.Approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RenameEntry_Approved_RenamesTheShownEntry()
    {
        var f = new Fixture();
        IEntry source = f.AddEntry("old.txt");
        f.Left.Setup(m => m.MoveAsync(It.IsAny<Url>(), It.IsAny<Url>(), false)).Returns(Task.CompletedTask);
        AiActionApprovalRequest? request = null;
        f.Approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(true);

        string result = await f.Tools.RenameEntry("new.txt", "old.txt", "left", confirmed: true);

        StringAssert.Contains(result, "Renamed");
        f.Left.Verify(m => m.MoveAsync(source.Url!, It.Is<Url>(url => url.FullUrl.Contains("new.txt", StringComparison.Ordinal)), false), Times.Once);
        StringAssert.Contains(request!.Actions.Single(), "new.txt");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CreateFolder_RejectsPathSeparators()
    {
        var f = new Fixture();

        StringAssert.StartsWith(await f.Tools.CreateFolder("../x", "left"), "Error:");
        f.Left.Verify(m => m.CreateFolderAsync(It.IsAny<Url>()), Times.Never);
    }
}
