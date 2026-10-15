using iConsole.Data;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>F5/Shift+F6/F8 over marked rows: the single-entry tasks run in order under one task.</summary>
[TestClass]
public class BatchOperationTaskTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), "BatchOperationTaskTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "folder"));
        Directory.CreateDirectory(Path.Combine(_root, "dst"));
        File.WriteAllText(Path.Combine(_root, "src", "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "src", "b.txt"), "b");
        File.WriteAllText(Path.Combine(_root, "src", "folder", "inside.txt"), "inside");
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

    private string Src(string name) => Path.Combine(_root, "src", name);
    private string Dst(string name) => Path.Combine(_root, "dst", name);

    private static BatchStep CopyFile(FileSystemService fs, string source, string target, bool overwrite = false) =>
        new(Path.GetFileName(source), new ContentModelCopyFileTask(fs, fs, new Url(source), new Url(target), overwrite));

    [TestMethod]
    public async Task EveryStepRunsInOrder()
    {
        var fs = new FileSystemService();
        var batch = new BatchOperationTask("Copy", [CopyFile(fs, Src("a.txt"), Dst("a.txt")), CopyFile(fs, Src("b.txt"), Dst("b.txt"))]);

        await batch.RunAsync();

        Assert.IsNull(batch.Error, batch.Description);
        Assert.AreEqual(2, batch.Succeeded);
        Assert.AreEqual("a", File.ReadAllText(Dst("a.txt")));
        Assert.AreEqual("b", File.ReadAllText(Dst("b.txt")));
        Assert.AreEqual(100, batch.ProgressPercentage);
    }

    [TestMethod]
    public async Task AFailedStepDoesNotStopTheRestAndIsListed()
    {
        // a.txt is already at the target and overwrite is off: that one fails, b.txt still goes.
        File.WriteAllText(Dst("a.txt"), "old");
        var fs = new FileSystemService();
        var batch = new BatchOperationTask("Copy", [CopyFile(fs, Src("a.txt"), Dst("a.txt")), CopyFile(fs, Src("b.txt"), Dst("b.txt"))]);

        await batch.RunAsync();

        Assert.AreEqual("1 of 2 item(s) failed.", batch.Error);
        Assert.AreEqual(1, batch.Succeeded);
        Assert.AreEqual("old", File.ReadAllText(Dst("a.txt")));
        Assert.AreEqual("b", File.ReadAllText(Dst("b.txt")));
        StringAssert.Contains(batch.Description, "a.txt:");
        Assert.AreEqual(iConsole.Data.TaskStatus.Completed, batch.Status, "a failure is reported through Error, as every task here does");
    }

    [TestMethod]
    public async Task AFolderStepRunsWithItsDiscoveryAcrossDrives()
    {
        var fs = new FileSystemService();
        var memory = new MemoryDriveService();
        var discovery = new FolderDiscoveryTask(fs, memory, new Url(Src("folder") + Path.DirectorySeparatorChar), new Url("memdrive://folder/"));
        var copy = new ContentModelCopyFolderTask(fs, memory, new Url(Src("folder") + Path.DirectorySeparatorChar), new Url("memdrive://folder/"), true, discovery, new FakeErrorPromptService());
        var batch = new BatchOperationTask("Copy", [new BatchStep("folder", copy, discovery)]);

        await batch.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsNull(batch.Error, batch.Description);
        var copied = await memory.ReadFileAsync(new Url("memdrive://folder/inside.txt"));
        Assert.AreEqual("inside", System.Text.Encoding.UTF8.GetString(copied!.Content!));
    }

    [TestMethod]
    public async Task ThePreparationRunsFirst()
    {
        var fs = new FileSystemService();
        var newFolder = Path.Combine(_root, "new");
        var batch = new BatchOperationTask(
            "Copy",
            [CopyFile(fs, Src("a.txt"), Path.Combine(newFolder, "a.txt"))],
            () => fs.CreateFolderAsync(new Url(newFolder + Path.DirectorySeparatorChar)));

        await batch.RunAsync();

        Assert.IsNull(batch.Error, batch.Description);
        Assert.IsTrue(File.Exists(Path.Combine(newFolder, "a.txt")));
    }

    [TestMethod]
    public async Task CancellingStopsTheStepsNotYetStarted()
    {
        var fs = new FileSystemService();
        var batch = new BatchOperationTask("Copy", [CopyFile(fs, Src("a.txt"), Dst("a.txt")), CopyFile(fs, Src("b.txt"), Dst("b.txt"))]);
        batch.CancellationTokenSource.Cancel();

        await batch.RunAsync();

        Assert.AreEqual(iConsole.Data.TaskStatus.Canceled, batch.Status);
        Assert.IsFalse(File.Exists(Dst("a.txt")));
        Assert.IsFalse(File.Exists(Dst("b.txt")));
    }
}
