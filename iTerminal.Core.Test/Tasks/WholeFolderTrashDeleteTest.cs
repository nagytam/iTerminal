using iConsole.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Recycle;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// A recursive delete of a local folder with the bin ticked goes to the Trash as one folder
/// (<see cref="ContentModelDeleteTask"/> without a discovery task, <see cref="FileSystemService.DeleteFolder"/>),
/// not file by file. These tests pin that the whole tree is one trash move, and that a folder link is
/// removed without the Trash being asked.
/// </summary>
[TestClass]
public class WholeFolderTrashDeleteTest
{
    private string _tempDir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WholeFolderTrash_TakesTheTreeAsOneTrashMove()
    {
        // FileSystemService asks the shell's Recycle Bin on Windows, not the fake, so this runs on Linux only.
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The fake Trash is the Linux one; the Windows bin is the shell's.");
        }

        var folderPath = Path.Combine(_tempDir, "tree");
        Directory.CreateDirectory(Path.Combine(folderPath, "sub", "deeper"));
        File.WriteAllText(Path.Combine(folderPath, "top.txt"), "top");
        File.WriteAllText(Path.Combine(folderPath, "sub", "nested.txt"), "nested");
        File.WriteAllText(Path.Combine(folderPath, "sub", "deeper", "deep.txt"), "deep");

        var trash = new FakeTrashService(Path.Combine(_tempDir, "trash"));
        var target = new FileSystemService(trash);
        var folderUrl = new Url(folderPath + Path.DirectorySeparatorChar);
        var task = new ContentModelDeleteTask(target, folderUrl, recursive: true, moveToRecycleBin: true, discoveryTask: null, new FakeErrorPromptService());

        await task.ExecuteAsync();

        Assert.IsNull(task.Error, "a whole-folder trash must not report an error");
        CollectionAssert.AreEqual(new[] { folderPath }, trash.Trashed, "one trash move, for the folder itself");
        Assert.IsFalse(Directory.Exists(folderPath));
        Assert.AreEqual(1, Directory.GetFileSystemEntries(Path.Combine(_tempDir, "trash")).Length, "one entry in the Trash");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RecursiveFolderWithoutTheBin_AndNoDiscovery_IsRefusedAndLeftAlone()
    {
        var folderPath = Path.Combine(_tempDir, "tree");
        Directory.CreateDirectory(folderPath);
        var keptFile = Path.Combine(folderPath, "keep.txt");
        File.WriteAllText(keptFile, "keep");

        var target = new FileSystemService(new ForbiddenTrashService());
        var folderUrl = new Url(folderPath + Path.DirectorySeparatorChar);
        var task = new ContentModelDeleteTask(target, folderUrl, recursive: true, moveToRecycleBin: false, discoveryTask: null, new FakeErrorPromptService());

        await task.ExecuteAsync();

        Assert.IsNotNull(task.Error, "a permanent recursive delete with no walk to run must be refused");
        Assert.IsTrue(File.Exists(keptFile), "nothing is removed by a refused delete");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WholeFolderTrash_FolderAlreadyInTheTrashIsDeletedForGood()
    {
        // The Linux Trash refuses a folder that is already inside one, so DeleteFolder removes it instead.
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The freedesktop Trash is the Linux one.");
        }

        var homeTrash = Path.Combine(_tempDir, "trash");
        var folderPath = Path.Combine(homeTrash, "files", "tree");
        Directory.CreateDirectory(Path.Combine(folderPath, "sub"));
        File.WriteAllText(Path.Combine(folderPath, "sub", "nested.txt"), "nested");

        var trash = new FreedesktopTrash(homeTrash, () => [], 0);
        var target = new FileSystemService(trash);
        var folderUrl = new Url(folderPath + Path.DirectorySeparatorChar);
        var task = new ContentModelDeleteTask(target, folderUrl, recursive: true, moveToRecycleBin: true, discoveryTask: null, new FakeErrorPromptService());

        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.IsFalse(Directory.Exists(folderPath), "the folder is gone");
        var info = Path.Combine(homeTrash, "info");
        Assert.IsTrue(!Directory.Exists(info) || Directory.GetFileSystemEntries(info).Length == 0, "no record is made for a delete for good");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WholeFolderTrash_FolderLinkIsRemovedAndNeverTrashed()
    {
        var targetPath = Path.Combine(_tempDir, "real");
        Directory.CreateDirectory(targetPath);
        var keptFile = Path.Combine(targetPath, "keep.txt");
        File.WriteAllText(keptFile, "keep");
        var linkPath = Path.Combine(_tempDir, "link");
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Inconclusive("This account may not create symbolic links (Windows without Developer Mode).");
        }

        var target = new FileSystemService(new ForbiddenTrashService());
        var linkUrl = new Url(linkPath + Path.DirectorySeparatorChar);
        var task = new ContentModelDeleteTask(target, linkUrl, recursive: true, moveToRecycleBin: true, discoveryTask: null, new FakeErrorPromptService());

        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.IsFalse(Directory.Exists(linkPath) || File.Exists(linkPath), "the link is gone");
        Assert.IsTrue(File.Exists(keptFile), "what the link pointed to survives");
    }
}
