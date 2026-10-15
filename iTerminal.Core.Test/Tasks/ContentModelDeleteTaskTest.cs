using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// Wraps a real <see cref="IFileSystem"/> but delays every <see cref="DeleteFile"/> call and
/// tracks in-flight concurrency, so tests can deterministically prove
/// <see cref="FolderDeletePipeline"/>'s delete-worker pool overlaps multiple file deletions
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
    public string GetAvailableDiskSpace() => inner.GetAvailableDiskSpace();
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => inner.MoveAsync(sourceUrl, targetUrl, overwrite);
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => inner.CopyAsync(sourceUrl, targetUrl, overwrite);
    public Task CreateFolderAsync(Url url) => inner.CreateFolderAsync(url);
    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) =>
        inner.StreamToAsync(url, destination, progress, cancellationToken);
    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) =>
        inner.StreamFromAsync(url, source, progress, cancellationToken);
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => inner.DeleteFolder(url, recursive, moveToRecycleBin);

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var current = Interlocked.Increment(ref _inFlight);
        lock (_lock)
        {
            MaxObservedConcurrency = Math.Max(MaxObservedConcurrency, current);
        }
        try
        {
            Thread.Sleep(delay);
            return inner.DeleteFile(url, moveToRecycleBin);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

[TestClass]
public class ContentModelDeleteTaskTest
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
    public async Task ContentModelDeleteTask_DeletesFile()
    {
        var filePath = Path.Combine(_tempDir, "file.txt");
        File.WriteAllText(filePath, "content");

        var task = new ContentModelDeleteTask(new FileSystemService(), new Url(filePath), recursive: false, moveToRecycleBin: false, discoveryTask: null, new FakeErrorPromptService());
        await task.ExecuteAsync();

        Assert.IsFalse(File.Exists(filePath));
        StringAssert.Contains(task.Description, "finished");
    }

    [TestMethod]
    public async Task ContentModelDeleteTask_NonRecursiveEmptyFolder_DeletesFolder()
    {
        var folderPath = Path.Combine(_tempDir, "emptyfolder");
        Directory.CreateDirectory(folderPath);

        var task = new ContentModelDeleteTask(new FileSystemService(), new Url(folderPath + Path.DirectorySeparatorChar), recursive: false, moveToRecycleBin: false, discoveryTask: null, new FakeErrorPromptService());
        await task.ExecuteAsync();

        Assert.IsFalse(Directory.Exists(folderPath));
    }

    [TestMethod]
    public async Task ContentModelDeleteTask_NonRecursiveNonEmptyFolder_ReportsFailureAndLeavesFolder()
    {
        var folderPath = Path.Combine(_tempDir, "nonempty");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        var task = new ContentModelDeleteTask(new FileSystemService(), new Url(folderPath + Path.DirectorySeparatorChar), recursive: false, moveToRecycleBin: false, discoveryTask: null, new FakeErrorPromptService());
        await task.ExecuteAsync();

        Assert.IsTrue(Directory.Exists(folderPath));
        StringAssert.Contains(task.Description, "failed");
    }

    [TestMethod]
    public async Task ContentModelDeleteTask_RecursiveFolder_DeletesNestedTree()
    {
        var folderPath = Path.Combine(_tempDir, "tree");
        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, "sub"));
        File.WriteAllText(Path.Combine(folderPath, "top.txt"), "top");
        File.WriteAllText(Path.Combine(folderPath, "sub", "nested.txt"), "nested");

        var target = new FileSystemService();
        var folderUrl = new Url(folderPath + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDeleteDiscoveryTask(target, folderUrl);
        var task = new ContentModelDeleteTask(target, folderUrl, recursive: true, moveToRecycleBin: false, discoveryTask, new FakeErrorPromptService());
        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        Assert.IsFalse(Directory.Exists(folderPath));
        StringAssert.Contains(task.Description, "finished");
    }

    [TestMethod]
    public async Task ContentModelDeleteTask_Cancelled_LeavesTreeUntouched()
    {
        var folderPath = Path.Combine(_tempDir, "tree");
        Directory.CreateDirectory(folderPath);
        File.WriteAllText(Path.Combine(folderPath, "file.txt"), "content");

        var target = new FileSystemService();
        var folderUrl = new Url(folderPath + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDeleteDiscoveryTask(target, folderUrl);
        var task = new ContentModelDeleteTask(target, folderUrl, recursive: true, moveToRecycleBin: false, discoveryTask, new FakeErrorPromptService());
        task.CancellationTokenSource.Cancel();
        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        Assert.IsTrue(Directory.Exists(folderPath));
        Assert.IsTrue(File.Exists(Path.Combine(folderPath, "file.txt")));
        StringAssert.Contains(task.Description, "cancelled");
    }

    [TestMethod]
    public async Task ContentModelDeleteTask_DeletesFilesConcurrently()
    {
        var folderPath = Path.Combine(_tempDir, "tree");
        Directory.CreateDirectory(folderPath);

        const int fileCount = 8;
        var perFileDelay = TimeSpan.FromMilliseconds(200);
        for (var i = 0; i < fileCount; i++)
        {
            File.WriteAllText(Path.Combine(folderPath, $"file{i}.txt"), $"content {i}");
        }

        var target = new DelayingFileSystem(new FileSystemService(), perFileDelay);
        var folderUrl = new Url(folderPath + Path.DirectorySeparatorChar);
        var discoveryTask = new FolderDeleteDiscoveryTask(target, folderUrl);
        var task = new ContentModelDeleteTask(target, folderUrl, recursive: true, moveToRecycleBin: false, discoveryTask, new FakeErrorPromptService());

        _ = discoveryTask.ExecuteAsync();
        await task.ExecuteAsync();

        Assert.IsFalse(Directory.Exists(folderPath));
        Assert.IsTrue(
            target.MaxObservedConcurrency > 1,
            $"Expected multiple delete workers to overlap in flight, but max observed concurrency was {target.MaxObservedConcurrency}");
    }
}
