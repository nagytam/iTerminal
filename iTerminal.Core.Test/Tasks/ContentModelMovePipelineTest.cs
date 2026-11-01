using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iConsole.Services;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// Wraps a real <see cref="IFileSystem"/> but delays every <see cref="StreamToAsync"/> call and
/// tracks in-flight concurrency, so tests can deterministically prove
/// <see cref="ContentModelMoveFolderTask"/>'s copy-worker pool overlaps multiple file moves
/// instead of running them one at a time — without relying on a wall-clock threshold.
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
public class ContentModelMovePipelineTest
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
    public async Task ContentModelMoveFileTask_CrossInstanceLocalMove_MovesContentAndDeletesSource()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, new string('X', 300_000));

        var task = new ContentModelMoveFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(true));
        await task.ExecuteAsync();

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual(new string('X', 300_000), File.ReadAllText(targetPath));
        Assert.AreEqual(100, task.ProgressPercentage);
    }

    [TestMethod]
    public async Task ContentModelMoveFileTask_OverwriteFalse_ExistingTarget_LeavesBothFilesUnchanged()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");

        var task = new ContentModelMoveFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(false));
        await task.ExecuteAsync();

        Assert.IsTrue(File.Exists(sourcePath));
        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        StringAssert.Contains(task.Description, "already exists");
    }

    [TestMethod]
    public async Task ContentModelMoveFileTask_Cancelled_DeletesPartialTargetAndKeepsSource()
    {
        var sourcePath = Path.Combine(_tempDir, "source.bin");
        var targetPath = Path.Combine(_tempDir, "target.bin");
        File.WriteAllBytes(sourcePath, new byte[1024]);

        var task = new ContentModelMoveFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(true));
        task.CancellationTokenSource.Cancel();
        await task.ExecuteAsync();

        Assert.IsFalse(File.Exists(targetPath));
        Assert.IsTrue(File.Exists(sourcePath));
        StringAssert.Contains(task.Description, "cancelled");
    }

    [TestMethod]
    public async Task ContentModelMoveFileTask_MissingSource_ReportsFailure()
    {
        var sourcePath = Path.Combine(_tempDir, "does-not-exist.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");

        var task = new ContentModelMoveFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), ConflictResolvers.Of(true));
        await task.ExecuteAsync();

        StringAssert.Contains(task.Description, "failed");
    }

    [TestMethod]
    public async Task ContentModelMoveFolderTask_MovesFilesConcurrentlyAndDeletesSource()
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
        var task = new ContentModelMoveFolderTask(source, target, sourceUrl, targetUrl, overwrite: true, discoveryTask, new FakeErrorPromptService());

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        Assert.IsFalse(Directory.Exists(srcDir), "Source folder should have been removed after the move");
        for (var i = 0; i < fileCount; i++)
        {
            Assert.IsTrue(File.Exists(Path.Combine(dstDir, $"file{i}.txt")), $"file{i}.txt should have been moved");
            Assert.AreEqual($"content {i}", File.ReadAllText(Path.Combine(dstDir, $"file{i}.txt")));
        }

        Assert.IsTrue(
            source.MaxObservedConcurrency > 1,
            $"Expected multiple copy workers to overlap in flight, but max observed concurrency was {source.MaxObservedConcurrency}");
    }

    // ---- File conflicts on a move: a skipped source stays; a rename moves to the new name. ----

    [TestMethod]
    public async Task ContentModelMoveFileTask_AskAndSkip_KeepsSourceAndTarget()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");
        var resolver = new FileConflictResolver(FileConflictMode.Ask, new FakeConflictPromptService(FileConflictChoice.Skip));

        var task = new ContentModelMoveFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), resolver);
        await task.ExecuteAsync();

        Assert.IsTrue(File.Exists(sourcePath));
        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        Assert.IsNull(task.Error);
    }

    [TestMethod]
    public async Task ContentModelMoveFileTask_AskAndRename_MovesToTheNewName()
    {
        var sourcePath = Path.Combine(_tempDir, "source.txt");
        var targetPath = Path.Combine(_tempDir, "target.txt");
        File.WriteAllText(sourcePath, "new content");
        File.WriteAllText(targetPath, "original content");
        var resolver = new FileConflictResolver(FileConflictMode.Ask, new FakeConflictPromptService(FileConflictChoice.Rename));

        var task = new ContentModelMoveFileTask(new FileSystemService(), new FileSystemService(), new Url(sourcePath), new Url(targetPath), resolver);
        await task.ExecuteAsync();

        Assert.IsFalse(File.Exists(sourcePath));
        Assert.AreEqual("original content", File.ReadAllText(targetPath));
        Assert.AreEqual("new content", File.ReadAllText(Path.Combine(_tempDir, "target (2).txt")));
        Assert.IsNull(task.Error);
    }
}
