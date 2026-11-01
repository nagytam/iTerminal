using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Services;
using iConsole.Services;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// Wraps a real <see cref="IFileSystem"/> but delays every <see cref="StreamToAsync"/> call and
/// tracks how many calls are in flight at once, so tests can deterministically prove the
/// copy-worker pool in <see cref="ContentModelCopyFolderTask"/> genuinely runs multiple file
/// copies concurrently instead of one at a time — without relying on a wall-clock threshold, which
/// is unreliable when other tests are competing for thread-pool threads in the same run.
/// </summary>
file sealed class DelayingFileSystem(IFileSystem inner, TimeSpan delay) : IFileSystem
{
    private int _inFlight;
    public int MaxObservedConcurrency { get; private set; }
    private readonly Lock _lock = new();

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
    public Task CreateFolderAsync(Url url) => inner.CreateFolderAsync(url);

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _inFlight);
        lock (_lock)
        {
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
        }
        try
        {
            await Task.Delay(delay, cancellationToken);
            await inner.StreamToAsync(url, destination, progress, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) =>
        inner.StreamFromAsync(url, source, progress, cancellationToken);
}

[TestClass]
public class ContentModelCopyPipelineTest
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
    public async Task StreamCopyHelper_CopyWithProgressAsync_ReportsMonotonicProgressAndCopiesAllBytes()
    {
        var data = new byte[500_000];
        new Random(42).NextBytes(data);
        using var source = new MemoryStream(data);
        using var destination = new MemoryStream();
        var reports = new List<long>();
        var progress = new SynchronousProgress<long>(reports.Add);

        await StreamCopyHelper.CopyWithProgressAsync(source, destination, progress, CancellationToken.None, bufferSize: 64 * 1024);

        Assert.IsTrue(reports.Count > 1, $"Expected multiple progress reports but got {reports.Count}");
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.IsTrue(reports[i] >= reports[i - 1], "Progress reports must be monotonically non-decreasing");
        }
        Assert.AreEqual(data.Length, reports[^1]);
        CollectionAssert.AreEqual(data, destination.ToArray());
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_OverwriteFalse_ExistingTarget_LeavesTargetUnchanged()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");

        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(false));
        await task.ExecuteAsync();

        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        StringAssert.Contains(task.Description, "already exists");
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_Cancelled_DeletesPartialTargetAndReportsCancelled()
    {
        var sourcePath = Path.Combine(_tempDir, "source.bin");
        var targetPath = Path.Combine(_tempDir, "target.bin");
        File.WriteAllBytes(sourcePath, new byte[1024]);

        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(true));
        task.CancellationTokenSource.Cancel();
        await task.ExecuteAsync();

        Assert.IsFalse(File.Exists(targetPath));
        StringAssert.Contains(task.Description, "cancelled");
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_MissingSource_ReportsFailure()
    {
        var sourcePath = Path.Combine(_tempDir, "does-not-exist.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");

        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(true));
        await task.ExecuteAsync();

        StringAssert.Contains(task.Description, "failed");
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_CrossInstanceLocalCopy_CopiesContentWithRealProgress()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, new string('X', 300_000));

        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(true));
        await task.ExecuteAsync();

        Assert.AreEqual(File.ReadAllText(sourcePath), File.ReadAllText(targetPath));
        Assert.AreEqual(100, task.ProgressPercentage);
    }

    [TestMethod]
    public async Task ContentModelCopyFolderTask_ToMemoryDrive_CopiesSubfoldersAndReportsBytes()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        Directory.CreateDirectory(Path.Combine(srcDir, "a", "b"));
        Directory.CreateDirectory(Path.Combine(srcDir, "empty"));
        File.WriteAllBytes(Path.Combine(srcDir, "root.bin"), new byte[200_000]);
        File.WriteAllBytes(Path.Combine(srcDir, "a", "mid.bin"), new byte[300_000]);
        File.WriteAllBytes(Path.Combine(srcDir, "a", "b", "deep.bin"), new byte[100_000]);

        var source = new FileSystemService();
        var target = new MemoryDriveService();
        var sourceUrl = new Url(srcDir);
        var targetUrl = new Url("memdrive://copy/");
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
        var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        Assert.IsTrue(target.ExistsFile(new Url("memdrive://copy/a/b/deep.bin")));
        Assert.IsTrue(target.ExistsFolder(new Url("memdrive://copy/empty/")));
        Assert.AreEqual(3, task.ItemsProcessed);
        Assert.AreEqual(600_000, task.TotalBytes);
        Assert.AreEqual(600_000, task.BytesProcessed);
        Assert.AreEqual(100, task.ProgressPercentage);
    }

    [TestMethod]
    public async Task ContentModelCopyFolderTask_CopiesFilesConcurrently()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var dstDir = Path.Combine(_tempDir, "dst");
        Directory.CreateDirectory(srcDir);

        const int fileCount = 8;
        var perFileDelay = TimeSpan.FromMilliseconds(200);
        for (var i = 0; i < fileCount; i++)
        {
            File.WriteAllText(Path.Combine(srcDir, $"file{i}.txt"), $"content {i}");
        }

        var source = new DelayingFileSystem(new FileSystemService(), perFileDelay);
        var target = new FileSystemService();
        var sourceUrl = new Url(srcDir);
        var targetUrl = new Url(dstDir + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDiscoveryTask(source, target, sourceUrl, targetUrl);
        var task = new ContentModelCopyFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        for (var i = 0; i < fileCount; i++)
        {
            Assert.IsTrue(File.Exists(Path.Combine(dstDir, $"file{i}.txt")), $"file{i}.txt should have been copied");
            Assert.AreEqual($"content {i}", File.ReadAllText(Path.Combine(dstDir, $"file{i}.txt")));
        }

        Assert.IsTrue(
            source.MaxObservedConcurrency > 1,
            $"Expected multiple copy workers to overlap in flight, but max observed concurrency was {source.MaxObservedConcurrency}");
    }

    // ---- File conflicts (single file): the prompt decides; nothing is written without an answer. ----

    [TestMethod]
    public async Task ContentModelCopyFileTask_AskAndSkip_KeepsTargetAndReportsNoError()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");
        var prompts = new FakeConflictPromptService(FileConflictChoice.Skip);
        var resolver = new FileConflictResolver(FileConflictMode.Ask, prompts);

        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), resolver);
        await task.ExecuteAsync();

        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        Assert.IsNull(task.Error);
        Assert.AreEqual(1, prompts.PromptCount);
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_AskAndRename_WritesBesideTheExistingFile()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");
        var resolver = new FileConflictResolver(FileConflictMode.Ask, new FakeConflictPromptService(FileConflictChoice.Rename));

        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), resolver);
        await task.ExecuteAsync();

        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        Assert.AreEqual("new content", File.ReadAllText(Path.Combine(_tempDir, "target (2).txt")));
        Assert.IsNull(task.Error);
    }

    [TestMethod]
    public async Task ContentModelCopyFileTask_AskAndCancel_EndsCancelledAndTouchesNothing()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");
        var prompts = new FakeConflictPromptService(FileConflictChoice.Cancel);
        var resolver = new FileConflictResolver(FileConflictMode.Ask, prompts);
        var task = new ContentModelCopyFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), resolver);
        resolver.CancelRun = () => task.CancellationTokenSource.Cancel();

        await task.ExecuteAsync();

        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        Assert.IsNull(task.Error);
        StringAssert.Contains(task.Description, "cancelled");
        Assert.AreEqual(1, prompts.PromptCount);
    }
}
