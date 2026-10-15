using iConsole.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;
using System.IO.Compression;
using System.Text;

namespace iTerminal.Core.Tasks.Test;

[TestClass]
public class ContentModelCopyMoveTaskTest
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

    [TestMethod]
    public async Task ContentModelCopyFileTask_RealFileToMemDrive_CopiesContentAndKeepsSource()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "hello memdrive");
        var fileSystemService = new FileSystemService();
        var memoryDriveService = new MemoryDriveService();

        var task = new ContentModelCopyFileTask(
            fileSystemService,
            memoryDriveService,
            new Url(sourcePath),
            new Url("memdrive://source.txt"),
            overwrite: true);

        await task.ExecuteAsync();

        var copied = await memoryDriveService.ReadFileAsync(new Url("memdrive://source.txt"));
        Assert.IsNotNull(copied);
        Assert.AreEqual("hello memdrive", System.Text.Encoding.UTF8.GetString(copied.Content!));
        Assert.IsTrue(File.Exists(sourcePath));
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_MemDriveToRealFile_CopiesContent()
    {
        var memoryDriveService = new MemoryDriveService();
        await memoryDriveService.WriteFileAsync(new Url("memdrive://source.txt"), new MemoryStream(System.Text.Encoding.UTF8.GetBytes("from memdrive")));
        var fileSystemService = new FileSystemService();
        var targetPath = Path.Combine(_tempDir, "target.txt");

        var task = new ContentModelCopyFileTask(
            memoryDriveService,
            fileSystemService,
            new Url("memdrive://source.txt"),
            new Url(targetPath),
            overwrite: true);

        await task.ExecuteAsync();

        Assert.AreEqual("from memdrive", File.ReadAllText(targetPath));
    }

    [TestMethod]
    public async Task ContentModelMoveFileTask_RealFileToMemDrive_MovesAndDeletesSource()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "move me");
        var fileSystemService = new FileSystemService();
        var memoryDriveService = new MemoryDriveService();

        var task = new ContentModelMoveFileTask(
            fileSystemService,
            memoryDriveService,
            new Url(sourcePath),
            new Url("memdrive://moved.txt"),
            overwrite: true);

        await task.ExecuteAsync();

        var moved = await memoryDriveService.ReadFileAsync(new Url("memdrive://moved.txt"));
        Assert.IsNotNull(moved);
        Assert.AreEqual("move me", System.Text.Encoding.UTF8.GetString(moved.Content!));
        Assert.IsFalse(File.Exists(sourcePath));
    }

    [TestMethod]
    public async Task ContentModelCopyFolderTask_RealFolderToMemDrive_CopiesNestedContents()
    {
        var sourceDir = Path.Combine(_tempDir, "srcfolder");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(Path.Combine(sourceDir, "sub"));
        File.WriteAllText(Path.Combine(sourceDir, "top.txt"), "top");
        File.WriteAllText(Path.Combine(sourceDir, "sub", "nested.txt"), "nested");

        var fileSystemService = new FileSystemService();
        var memoryDriveService = new MemoryDriveService();

        var sourceUrl = new Url(sourceDir + Path.DirectorySeparatorChar);
        var targetUrl = new Url("memdrive://destfolder/");
        var discoveryTask = new FolderDiscoveryTask(fileSystemService, memoryDriveService, sourceUrl, targetUrl);
        var task = new ContentModelCopyFolderTask(
            fileSystemService,
            memoryDriveService,
            sourceUrl,
            targetUrl,
            overwrite: true,
            discoveryTask,
            new FakeErrorPromptService());

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        var top = await memoryDriveService.ReadFileAsync(new Url("memdrive://destfolder/top.txt"));
        var nested = await memoryDriveService.ReadFileAsync(new Url("memdrive://destfolder/sub/nested.txt"));
        Assert.IsNotNull(top);
        Assert.IsNotNull(nested);
        Assert.AreEqual("top", System.Text.Encoding.UTF8.GetString(top.Content!));
        Assert.AreEqual("nested", System.Text.Encoding.UTF8.GetString(nested.Content!));
    }

    private string CreateTempZip()
    {
        var zipPath = Path.Combine(_tempDir, "archive.zip");
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("inside.txt");
        using var writer = new StreamWriter(entry.Open());
        writer.Write("archived content");
        return zipPath;
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_ExtractFromZipToRealFile_CopiesContent()
    {
        var zipPath = CreateTempZip();
        var archiveFileSystemService = new ArchiveFileSystemService();
        var fileSystemService = new FileSystemService();
        var targetPath = Path.Combine(_tempDir, "extracted.txt");

        var task = new ContentModelCopyFileTask(
            archiveFileSystemService,
            fileSystemService,
            new Url(Container.ZIP_ARCHIVE + zipPath + "@/inside.txt"),
            new Url(targetPath),
            overwrite: true);

        await task.ExecuteAsync();

        Assert.AreEqual("archived content", File.ReadAllText(targetPath));
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_CopyIntoZip_ThrowsWithoutTouchingArchive()
    {
        var zipPath = CreateTempZip();
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        File.WriteAllText(sourcePath, "should not land in the zip");
        var archiveFileSystemService = new ArchiveFileSystemService();
        var fileSystemService = new FileSystemService();

        var task = new ContentModelCopyFileTask(
            fileSystemService,
            archiveFileSystemService,
            new Url(sourcePath),
            new Url(Container.ZIP_ARCHIVE + zipPath + "@/newentry.txt"),
            overwrite: true);

        await task.ExecuteAsync();

        Assert.IsTrue(task.Description.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(archiveFileSystemService.ExistsFile(new Url(Container.ZIP_ARCHIVE + zipPath + "@/newentry.txt")));
    }

    [TestMethod]
    public async Task ContentModelMoveFileTask_MoveFromZip_ThrowsWithoutCopyingOrDeleting()
    {
        var zipPath = CreateTempZip();
        var archiveFileSystemService = new ArchiveFileSystemService();
        var fileSystemService = new FileSystemService();
        var targetPath = Path.Combine(_tempDir, "moved.txt");

        var task = new ContentModelMoveFileTask(
            archiveFileSystemService,
            fileSystemService,
            new Url(Container.ZIP_ARCHIVE + zipPath + "@/inside.txt"),
            new Url(targetPath),
            overwrite: true);

        await task.ExecuteAsync();

        Assert.IsTrue(task.Description.Contains("read-only", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.Exists(targetPath));
        Assert.IsTrue(archiveFileSystemService.ExistsFile(new Url(Container.ZIP_ARCHIVE + zipPath + "@/inside.txt")));
    }
}
