using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// Wraps a real <see cref="IFileSystem"/> but makes reading one specific file always fail, or — with
/// <paramref name="failFirstRead"/> — whichever file is read first.
/// </summary>
file sealed class FailingFileSystem(IFileSystem inner, string failingFileName, TimeSpan? goodFileDelay = null, bool failFirstRead = false, TimeSpan? createFolderDelay = null) : IFileSystem
{
    private int _reads;

    public bool IsReadOnly => inner.IsReadOnly;
    public bool ExistsFile(Url url) => inner.ExistsFile(url);
    public bool ExistsFolder(Url url) => inner.ExistsFolder(url);
    public Task<IMemoryFile?> ReadFileAsync(Url url) => inner.ReadFileAsync(url);
    public Task WriteFileAsync(Url url, MemoryStream inputStream) => inner.WriteFileAsync(url, inputStream);
    public bool DeleteFile(Url url, bool moveToRecycleBin) => inner.DeleteFile(url, moveToRecycleBin);
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => inner.DeleteFolder(url, recursive, moveToRecycleBin);
    public string GetAvailableDiskSpace() => inner.GetAvailableDiskSpace();
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => inner.MoveAsync(sourceUrl, targetUrl, overwrite);
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => inner.CopyAsync(sourceUrl, targetUrl, overwrite);
    public async Task CreateFolderAsync(Url url)
    {
        if (createFolderDelay is { } delay)
        {
            await Task.Delay(delay);
        }
        await inner.CreateFolderAsync(url);
    }
    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) =>
        inner.StreamFromAsync(url, source, progress, cancellationToken);

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        // The directory lists files in whatever order the file system likes, so "the first read" is the
        // only failure whose timing the copy cannot be ahead of.
        bool fails = failFirstRead ? Interlocked.Increment(ref _reads) == 1 : url.Name == failingFileName;
        if (fails)
        {
            throw new IOException($"Simulated failure reading {url.Name}");
        }
        if (goodFileDelay is { } delay)
        {
            await Task.Delay(delay, cancellationToken);
        }
        await inner.StreamToAsync(url, destination, progress, cancellationToken);
    }
}

/// <summary>Minimal concrete <see cref="TransferTask"/> to exercise its statistics computation.</summary>
file sealed class TestTransferTask : TransferTask
{
    public override Task ExecuteAsync() => Task.CompletedTask;
    public void SetItems(long processed, long total) => ReportItems(processed, total);
    public void SetBytes(long processed, long total) => ReportBytes(processed, total);
}

[TestClass]
public class ErrorRecoveryAndTaskVisibilityTest
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
    public void TransferTask_ReportsComputedRates()
    {
        var task = new TestTransferTask();
        task.SetItems(4, 10);
        task.SetBytes(4000, 10000);

        Assert.AreEqual(4, task.ItemsProcessed);
        Assert.AreEqual(10, task.TotalItems);
        Assert.AreEqual(4000, task.BytesProcessed);
        Assert.AreEqual(10000, task.TotalBytes);
        // No time has elapsed (Stopwatch never started outside RunAsync), so rates are 0 rather
        // than a division-by-near-zero blowup.
        Assert.AreEqual(0, task.ItemsPerSecond);
        Assert.AreEqual(0, task.BytesPerSecond);
    }

    [TestMethod]
    public async Task ContentModelCopyFolderTask_ErrorPrompt_ContinueSkipsFailedFileAndProceedsWithRest()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var dstDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(srcDir);
        File.WriteAllText(Path.Combine(srcDir, "good1.txt"), "one");
        File.WriteAllText(Path.Combine(srcDir, "bad.txt"), "bad");
        File.WriteAllText(Path.Combine(srcDir, "good2.txt"), "two");

        var source = new FailingFileSystem(new FileSystemService(), "bad.txt");
        var target = new FileSystemService();
        var sourceUrl = new Url(srcDir);
        var targetUrl = new Url(dstDir + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
        var errorPrompt = new FakeErrorPromptService(shouldContinue: true);
        var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, errorPrompt);

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        Assert.IsTrue(File.Exists(Path.Combine(dstDir, "good1.txt")), "good1.txt should have been copied despite the other file failing");
        Assert.IsTrue(File.Exists(Path.Combine(dstDir, "good2.txt")), "good2.txt should have been copied despite the other file failing");
        Assert.IsFalse(File.Exists(Path.Combine(dstDir, "bad.txt")), "bad.txt should not exist at the destination");
        Assert.AreEqual(1, errorPrompt.PromptCount);
        StringAssert.Contains(task.Description, "1 error");
        StringAssert.Contains(task.Description, "bad.txt");
    }

    [TestMethod]
    public async Task ContentModelCopyFolderTask_ErrorPrompt_CancelStopsRemainingWork()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var dstDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(srcDir);

        // Far more files than copy workers. The first read fails, so the other workers are still in their
        // delay when the cancel lands, and the files not yet picked up never start.
        const int fileCount = 100;
        for (var i = 0; i < fileCount; i++)
        {
            File.WriteAllText(Path.Combine(srcDir, $"good{i}.txt"), $"content {i}");
        }

        var source = new FailingFileSystem(new FileSystemService(), failingFileName: string.Empty, goodFileDelay: TimeSpan.FromMilliseconds(50), failFirstRead: true);
        var target = new FileSystemService();
        var sourceUrl = new Url(srcDir);
        var targetUrl = new Url(dstDir + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
        var errorPrompt = new FakeErrorPromptService(shouldContinue: false);
        var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, errorPrompt);

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        StringAssert.Contains(task.Description, "cancelled");
        // A file is created in the target before its bytes arrive, so a cancelled copy leaves an empty
        // one behind: only the files that received their content count as copied.
        var copiedCount = Directory.Exists(dstDir)
            ? Directory.GetFiles(dstDir).Count(file => new FileInfo(file).Length > 0)
            : 0;
        Assert.IsTrue(copiedCount < fileCount, $"Expected the cancel choice to stop some remaining work, but {copiedCount}/{fileCount} files were copied");
    }

    [TestMethod]
    public async Task CopyFolder_DiscoveryAndProcessingTasks_AreBothIndependentlyVisibleInTaskManager()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var dstDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(srcDir);
        for (var i = 0; i < 5; i++)
        {
            File.WriteAllText(Path.Combine(srcDir, $"file{i}.txt"), $"content {i}");
        }

        var source = new FailingFileSystem(new FileSystemService(), failingFileName: "no-such-file", goodFileDelay: TimeSpan.FromMilliseconds(300));
        // Discovery creates the target folder first, so slowing that down keeps the discovery task
        // listed for as long as it takes to see it next to the processing task. Five tiny files are
        // discovered in microseconds on a fast machine, and the test then raced the task manager
        // removing the finished discovery task.
        var target = new FailingFileSystem(new FileSystemService(), failingFileName: "no-such-file", createFolderDelay: TimeSpan.FromMilliseconds(500));
        var sourceUrl = new Url(srcDir);
        var targetUrl = new Url(dstDir + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
        var processingTask = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());

        var taskManager = new TaskManager(workerCount: 4);
        try
        {
            await taskManager.Add(discoveryTask);
            await taskManager.Add(processingTask);

            // Poll briefly for both to show up as concurrently-running entries — they're two
            // separate IBackgroundTask instances added independently, not internal phases of one.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            bool sawBoth;
            do
            {
                var snapshot = taskManager.Tasks.AsSnapshot();
                sawBoth = snapshot.Contains(discoveryTask) && snapshot.Contains(processingTask);
                if (!sawBoth)
                {
                    await Task.Delay(20);
                }
            } while (!sawBoth && DateTime.UtcNow < deadline);

            Assert.IsTrue(sawBoth, "Expected both the discovery task and the processing task to be visible in TaskManager.Tasks at the same time");

            deadline = DateTime.UtcNow.AddSeconds(10);
            while (taskManager.Tasks.AsSnapshot().Contains(processingTask) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }
        }
        finally
        {
            taskManager.Dispose();
        }
    }
}
