using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.AI;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.AI;

/// <summary>The archive, compare and synchronise tools, run against real temporary folders.</summary>
[TestClass]
public class AiPanelToolsArchivesAndFoldersTest
{
    private string _root = null!;
    private string _left = null!;
    private string _right = null!;
    private Mock<IContentModel> _leftModel = null!;
    private Mock<IContentModel> _rightModel = null!;
    private Mock<IAiActionApprovalService> _approval = null!;
    private AiPanelTools _tools = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "ai-panel-tools-" + Guid.NewGuid().ToString("N")[..8]);
        _left = Path.Combine(_root, "left") + Path.DirectorySeparatorChar;
        _right = Path.Combine(_root, "right") + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);

        _leftModel = Panel(Content.Left, _left);
        _rightModel = Panel(Content.Right, _right);
        _approval = new Mock<IAiActionApprovalService>();

        var service = new Mock<IContentModelService>();
        service.Setup(s => s.ContentModels).Returns([_leftModel.Object, _rightModel.Object]);
        service.Setup(s => s.GetFocusedModel()).Returns(_leftModel.Object);

        var runner = new FakeProcessRunner();
        _tools = new AiPanelTools(
            service.Object,
            new ArchiveCompressionService(runner, new SevenZipArchiveWriter(runner, () => null)),
            new ArchiveExtractionService(runner, () => null),
            new FileSystemResolver(new FileSystemService(), null!, null!, null!, service.Object),
            _approval.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static Mock<IContentModel> Panel(string name, string folder)
    {
        var model = new Mock<IContentModel>();
        model.Setup(m => m.Name).Returns(name);
        model.Setup(m => m.Url).Returns(new Url(folder));
        model.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns((Url u) => File.Exists(u.FullName));
        model.Setup(m => m.ExistsFolder(It.IsAny<Url>())).Returns((Url u) => Directory.Exists(u.FullName));
        model.Setup(m => m.FilteredEntries).Returns(() =>
            [.. Directory.GetFileSystemEntries(folder).Select(path =>
            {
                var entry = new Mock<IEntry>();
                string display = Path.GetFileName(path);
                entry.Setup(e => e.DisplayName).Returns(display);
                entry.Setup(e => e.Name).Returns(display);
                entry.Setup(e => e.Url).Returns(new Url(Directory.Exists(path) ? path + Path.DirectorySeparatorChar : path));
                return entry.Object;
            })]);
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CompressThenExtract_RoundTripsAFileThroughTheOtherPanel()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "hello");
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string compressed = await _tools.CompressEntries("pack", "left", "a.txt", confirmed: true);

        Assert.IsTrue(File.Exists(Path.Combine(_right, "pack.zip")), compressed);

        // Unpack the archive from the right panel back into the left one.
        string extracted = await _tools.ExtractArchives("right", "pack.zip", intoOtherPanel: true, confirmed: true);

        Assert.IsTrue(File.Exists(Path.Combine(_left, "pack", "a.txt")), extracted);
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(_left, "pack", "a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Compress_WithoutConfirmationOnlyPreviews()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "hello");

        string preview = await _tools.CompressEntries("pack", "left", "a.txt");

        StringAssert.Contains(preview, "Preview, nothing changed");
        StringAssert.Contains(preview, "pack.zip");
        Assert.IsFalse(File.Exists(Path.Combine(_right, "pack.zip")));
        _approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Compress_ConfirmedButDeclined_DoesNotCreateArchive()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "hello");
        AiActionApprovalRequest? request = null;
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(false);

        string result = await _tools.CompressEntries("pack", "left", "a.txt", confirmed: true);

        StringAssert.Contains(result, "declined or cancelled");
        Assert.IsFalse(File.Exists(Path.Combine(_right, "pack.zip")));
        StringAssert.Contains(request!.Actions.Single(), "a.txt");
        StringAssert.Contains(request.Actions.Single(), "pack.zip");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Compress_ArchiveAppearsWhileApprovalIsOpen_DoesNotReplaceIt()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "hello");
        string archivePath = Path.Combine(_right, "pack.zip");
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((_, _) => File.WriteAllText(archivePath, "created elsewhere"))
            .ReturnsAsync(true);

        string result = await _tools.CompressEntries("pack", "left", "a.txt", confirmed: true);

        StringAssert.Contains(result, "appeared while approval was pending");
        Assert.AreEqual("created elsewhere", File.ReadAllText(archivePath));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Extract_WithoutConfirmationOnlyPreviews()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "hello");
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _tools.CompressEntries("pack", "left", "a.txt", confirmed: true);
        _approval.Reset();

        string preview = await _tools.ExtractArchives("right", "pack.zip");

        StringAssert.Contains(preview, "Preview, nothing changed");
        StringAssert.Contains(preview, "pack.zip");
        Assert.IsFalse(Directory.Exists(Path.Combine(_left, "pack")));
        _approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Extract_ConfirmedButDeclined_DoesNotWriteFiles()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "hello");
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        await _tools.CompressEntries("pack", "left", "a.txt", confirmed: true);
        _approval.Reset();
        AiActionApprovalRequest? request = null;
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(false);

        string result = await _tools.ExtractArchives("right", "pack.zip", confirmed: true);

        StringAssert.Contains(result, "declined or cancelled");
        Assert.IsFalse(Directory.Exists(Path.Combine(_left, "pack")));
        StringAssert.Contains(request!.Actions.Single(), "pack.zip");
        StringAssert.Contains(request.Actions.Single(), Path.Combine(_left, "pack"));
        StringAssert.Contains(request.Actions.Single(), "overwrite existing files: no");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Compress_RefusesToTouchAnExistingArchive_AndABadFormat()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "x");
        File.WriteAllText(Path.Combine(_right, "pack.zip"), "not really a zip");

        StringAssert.Contains(await _tools.CompressEntries("pack", "left", "a.txt"), "already exists");
        StringAssert.StartsWith(await _tools.CompressEntries("pack", "left", "a.txt", format: "rar"), "Error:");
        Assert.AreEqual("not really a zip", File.ReadAllText(Path.Combine(_right, "pack.zip")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Extract_IgnoresNonArchives()
    {
        File.WriteAllText(Path.Combine(_left, "a.txt"), "x");

        string text = await _tools.ExtractArchives("left", "a.txt");

        StringAssert.Contains(text, "not an archive");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CompareFolders_ListsWhatIsOnlyOnOneSide()
    {
        File.WriteAllText(Path.Combine(_left, "only-left.txt"), "1");
        File.WriteAllText(Path.Combine(_right, "only-right.txt"), "2");

        string text = await _tools.CompareFolders();

        StringAssert.Contains(text, "only on the left: only-left.txt");
        StringAssert.Contains(text, "only on the right: only-right.txt");
        StringAssert.StartsWith(await _tools.CompareFolders("bogus"), "Error:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Synchronize_WithoutConfirmation_OnlyPreviews()
    {
        File.WriteAllText(Path.Combine(_left, "new.txt"), "1");

        string preview = await _tools.SynchronizeFolders("left_to_right");

        StringAssert.Contains(preview, "confirmed=true");
        Assert.IsFalse(File.Exists(Path.Combine(_right, "new.txt")));
        _approval.Verify(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Synchronize_ConfirmedButDeclined_LeavesFilesUnchanged()
    {
        File.WriteAllText(Path.Combine(_left, "new.txt"), "1");
        AiActionApprovalRequest? request = null;
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AiActionApprovalRequest, CancellationToken>((value, _) => request = value)
            .ReturnsAsync(false);

        string result = await _tools.SynchronizeFolders("left_to_right", confirmed: true);

        StringAssert.Contains(result, "declined or cancelled");
        Assert.IsFalse(File.Exists(Path.Combine(_right, "new.txt")));
        Assert.IsTrue(request!.Actions.Any(action => action.Contains("new.txt", StringComparison.Ordinal)));
        StringAssert.Contains(request.Summary, _left);
        StringAssert.Contains(request.Summary, _right);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Synchronize_Confirmed_CopiesAcross()
    {
        File.WriteAllText(Path.Combine(_left, "new.txt"), "1");
        _approval.Setup(s => s.ConfirmAsync(It.IsAny<AiActionApprovalRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        string text = await _tools.SynchronizeFolders("left_to_right", confirmed: true);

        StringAssert.Contains(text, "copied 1");
        Assert.IsTrue(File.Exists(Path.Combine(_right, "new.txt")));
        StringAssert.StartsWith(await _tools.SynchronizeFolders("sideways"), "Error:");
    }
}
