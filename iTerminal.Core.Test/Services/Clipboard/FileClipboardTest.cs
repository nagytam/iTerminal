#pragma warning disable CA1416 // The drop-block code under test is pure byte layout; it makes no Win32 call.
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Clipboard;
using Moq;

namespace iTerminal.Core.Test.Services.Clipboard;

[TestClass]
public class FileClipboardTest
{
    private string _root = null!;

    [TestInitialize]
    public void CreateRoot()
    {
        _root = Path.Combine(Path.GetTempPath(), "iTerminal-clip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void DeleteRoot()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static FileClipboardOperations Operations(IFileClipboardService? clipboard = null)
    {
        var files = new Mock<IFileSystemService>();
        files.Setup(f => f.ExistsFile(It.IsAny<Url>())).Returns<Url>(u => File.Exists(u.Path));
        files.Setup(f => f.ExistsFolder(It.IsAny<Url>())).Returns<Url>(u => Directory.Exists(u.Path));
        var models = Mock.Of<IContentModelService>();
        var resolver = new FileSystemResolver(files.Object, null!, null!, null!, models);
        return new FileClipboardOperations(
            clipboard ?? new InProcessFileClipboardService(),
            models,
            resolver,
            new TaskManager(),
            new ProgressWindowFactory(Mock.Of<IThemeService>()),
            Mock.Of<IErrorPromptService>(),
            Mock.Of<IConflictPromptService>(),
            Mock.Of<INotificationService>(),
            Serilog.Core.Logger.None);
    }

    private string Touch(string folder, string name)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    private static Url Folder(string path) => new(path.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);

    [TestMethod]
    public void DropFilesRoundTrips()
    {
        var paths = new[] { @"C:\a b\one.txt", @"D:\Árvíz\két.txt" };

        var block = WindowsFileClipboardService.BuildDropFiles(paths);
        var back = WindowsFileClipboardService.ParseDropFiles(block);

        CollectionAssert.AreEqual(paths, back);
        Assert.AreEqual(20, BitConverter.ToInt32(block, 0), "the names start right after the header");
        Assert.AreEqual(1, BitConverter.ToInt32(block, 16), "the names are wide");
    }

    [TestMethod]
    public void ADropBlockWithNoNamesIsEmpty()
    {
        Assert.AreEqual(0, WindowsFileClipboardService.ParseDropFiles(new byte[10]).Count);
        Assert.AreEqual(0, WindowsFileClipboardService.ParseDropFiles(WindowsFileClipboardService.BuildDropFiles([])).Count);
    }

    [TestMethod]
    public void TheLinuxFormatCarriesCopyOrCutAndFileUris()
    {
        var path = Path.Combine(_root, "a b.txt");
        var payload = LinuxFileClipboardService.BuildPayload([path], isCut: true);

        StringAssert.StartsWith(payload, "cut\nfile://");
        StringAssert.Contains(payload, "a%20b.txt");
        var parsed = LinuxFileClipboardService.TryParse(payload);
        Assert.IsNotNull(parsed);
        Assert.IsTrue(parsed.IsCut);
        CollectionAssert.AreEqual(new[] { path }, parsed.Paths.ToArray());
        Assert.IsNull(LinuxFileClipboardService.TryParse("some text"));
    }

    [TestMethod]
    public async Task TheInProcessClipboardHoldsWhatWasSet()
    {
        var clipboard = new InProcessFileClipboardService();
        Assert.IsNull(await clipboard.GetFilesAsync());

        await clipboard.SetFilesAsync(["a", "b"], isCut: true);

        var content = await clipboard.GetFilesAsync();
        Assert.IsNotNull(content);
        Assert.IsTrue(content.IsCut);
        CollectionAssert.AreEqual(new[] { "a", "b" }, content.Paths.ToArray());
    }

    private sealed class ThrowingClipboard : IFileClipboardService
    {
        public Task SetFilesAsync(IReadOnlyList<string> paths, bool isCut, CancellationToken cancellationToken = default) =>
            throw new IOException("clipboard busy");

        public Task<FileClipboardContent?> GetFilesAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("clipboard busy");
    }

    [TestMethod]
    public async Task AFailingSystemClipboardFallsBackToTheProcessOne()
    {
        var clipboard = new FallbackFileClipboardService(new ThrowingClipboard(), new InProcessFileClipboardService());

        await clipboard.SetFilesAsync(["a"], isCut: false);

        var content = await clipboard.GetFilesAsync();
        Assert.IsNotNull(content);
        CollectionAssert.AreEqual(new[] { "a" }, content.Paths.ToArray());
    }

    [TestMethod]
    public void PastingACopyBesideItsOriginalNamesItLikeExplorer()
    {
        var file = Touch("src", "report.txt");
        var folder = Path.GetDirectoryName(file)!;

        var plan = Operations().Plan(new FileClipboardContent([file], IsCut: false), Folder(folder));

        Assert.AreEqual(1, plan.Targets.Count);
        Assert.AreEqual("report - Copy.txt", Path.GetFileName(plan.Targets[0].FullUrl));
        Assert.AreEqual(0, plan.Skipped.Count);
    }

    [TestMethod]
    public void TheCopyNumberCountsUpWhileTheNameIsTaken()
    {
        var file = Touch("src", "report.txt");
        Touch("src", "report - Copy.txt");
        var folder = Path.GetDirectoryName(file)!;

        var plan = Operations().Plan(new FileClipboardContent([file], IsCut: false), Folder(folder));

        Assert.AreEqual("report - Copy (2).txt", Path.GetFileName(plan.Targets[0].FullUrl));
    }

    [TestMethod]
    public void PastingIntoAnotherFolderKeepsTheName()
    {
        var file = Touch("src", "report.txt");
        Directory.CreateDirectory(Path.Combine(_root, "dst"));

        var plan = Operations().Plan(new FileClipboardContent([file], IsCut: false), Folder(Path.Combine(_root, "dst")));

        Assert.AreEqual("report.txt", Path.GetFileName(plan.Targets[0].FullUrl));
        Assert.AreEqual(file, plan.Sources[0].Path);
    }

    [TestMethod]
    public void CuttingIntoTheSameFolderDoesNothing()
    {
        var file = Touch("src", "report.txt");

        var plan = Operations().Plan(new FileClipboardContent([file], IsCut: true), Folder(Path.GetDirectoryName(file)!));

        Assert.AreEqual(0, plan.Sources.Count);
        Assert.AreEqual(1, plan.Skipped.Count);
    }

    [TestMethod]
    public void AFolderCannotBePastedIntoItself()
    {
        var file = Touch("src/inner", "a.txt");
        var folder = Path.Combine(_root, "src");

        var plan = Operations().Plan(new FileClipboardContent([folder], IsCut: false), Folder(Path.GetDirectoryName(file)!));

        Assert.AreEqual(0, plan.Sources.Count);
        StringAssert.Contains(plan.Skipped[0], "into itself");
    }

    [TestMethod]
    public void FilesThatAreGoneAreSkippedAndSaidSo()
    {
        var gone = Path.Combine(_root, "src", "gone.txt");

        var plan = Operations().Plan(new FileClipboardContent([gone], IsCut: false), Folder(_root));

        Assert.AreEqual(0, plan.Sources.Count);
        StringAssert.Contains(plan.Skipped[0], "no longer exists");
    }

    [TestMethod]
    public void CopyNamesKeepTheExtensionAndNumberFolders()
    {
        Assert.AreEqual("a - Copy.txt", FileClipboardOperations.CopyName("a.txt", isFolder: false, 1));
        Assert.AreEqual("a - Copy (2).txt", FileClipboardOperations.CopyName("a.txt", isFolder: false, 2));
        Assert.AreEqual("photos - Copy (3)", FileClipboardOperations.CopyName("photos", isFolder: true, 3));
    }
}
