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

        var service = new Mock<IContentModelService>();
        service.Setup(s => s.ContentModels).Returns([_leftModel.Object, _rightModel.Object]);
        service.Setup(s => s.GetFocusedModel()).Returns(_leftModel.Object);

        var runner = new FakeProcessRunner();
        _tools = new AiPanelTools(
            service.Object,
            new ArchiveCompressionService(runner, new SevenZipArchiveWriter(runner, () => null)),
            new ArchiveExtractionService(runner, () => null),
            new FileSystemResolver(new FileSystemService(), null!, null!, null!, service.Object));
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

        string compressed = await _tools.CompressEntries("pack", "left", "a.txt");

        Assert.IsTrue(File.Exists(Path.Combine(_right, "pack.zip")), compressed);

        // Unpack the archive from the right panel back into the left one.
        string extracted = await _tools.ExtractArchives("right", "pack.zip", intoOtherPanel: true);

        Assert.IsTrue(File.Exists(Path.Combine(_left, "pack", "a.txt")), extracted);
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(_left, "pack", "a.txt")));
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
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Synchronize_Confirmed_CopiesAcross()
    {
        File.WriteAllText(Path.Combine(_left, "new.txt"), "1");

        string text = await _tools.SynchronizeFolders("left_to_right", confirmed: true);

        StringAssert.Contains(text, "copied 1");
        Assert.IsTrue(File.Exists(Path.Combine(_right, "new.txt")));
        StringAssert.StartsWith(await _tools.SynchronizeFolders("sideways"), "Error:");
    }
}
