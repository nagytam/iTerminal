using System.Text;
using iConsole.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;


namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// Regression guards for the file-conflict work. They pin today's behaviour, or a known bug left for a
/// follow-up. They are run on the unchanged branch first, and must pass there and after the change.
/// </summary>
[TestClass]
public class ConflictRegressionGuardTest
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "conflict-guard-" + Guid.NewGuid().ToString("N"));
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
    public async Task R1_CrossDriveFolderCopy_WithoutOverwrite_OverwritesExistingFileSilently()
    {
        // Pins a KNOWN BUG: the cross-drive folder pipeline ignores the overwrite flag and writes with
        // FileMode.Create. The folder follow-up fixes it; changing this assertion is that fix.
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "a.txt"), "new content");

        var source = new FileSystemService();
        var target = new MemoryDriveService();
        await target.CreateFolderAsync(new Url("memdrive://copy/"));
        await target.WriteFileAsync(new Url("memdrive://copy/a.txt"), new MemoryStream(Encoding.UTF8.GetBytes("old content")));

        var sourceUrl = new Url(srcDir);
        var targetUrl = new Url("memdrive://copy/");
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
        var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: false, discoveryTask, new FakeErrorPromptService());

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        var file = await target.ReadFileAsync(new Url("memdrive://copy/a.txt"));
        Assert.AreEqual("new content", Encoding.UTF8.GetString(file!.Content!));
        Assert.IsNull(task.Error);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task R2_SameDriveFolderCopy_WithoutOverwrite_FailsOnFirstExistingFile()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var dstDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(dstDir);
        File.WriteAllText(Path.Combine(srcDir, "a.txt"), "new");
        File.WriteAllText(Path.Combine(dstDir, "a.txt"), "old");

        var fileSystem = new FileSystemService();
        var task = new ContentModelCopyFolderTask(fileSystem, fileSystem, new Url(srcDir), new Url(dstDir), overwrite: false, discoveryTask: null, new FakeErrorPromptService());
        await task.ExecuteAsync();

        Assert.IsNotNull(task.Error);
        Assert.AreEqual("old", File.ReadAllText(Path.Combine(dstDir, "a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task R3_FileCopy_WithoutOverwrite_ReportsAlreadyExistsAndLeavesTarget()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");

        var fileSystem = new FileSystemService();
        var task = new ContentModelCopyFileTask(fileSystem, fileSystem, new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(overwrite: false));
        await task.ExecuteAsync();

        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        StringAssert.Contains(task.Error ?? string.Empty, "already exists");
    }
}
