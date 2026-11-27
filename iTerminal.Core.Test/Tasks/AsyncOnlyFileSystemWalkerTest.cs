using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>
/// A drive whose synchronous existence and delete members throw. A walker that still calls one of them fails the test,
/// so the test proves the walker went through the asynchronous member.
/// </summary>
file sealed class AsyncOnlyFileSystem : IFileSystem
{
    public List<string> AsyncCalls { get; } = [];

    public bool IsReadOnly => false;
    public bool ExistsFile(Url url) => throw new InvalidOperationException("The synchronous ExistsFile was used.");
    public bool ExistsFolder(Url url) => throw new InvalidOperationException("The synchronous ExistsFolder was used.");
    public bool DeleteFile(Url url, bool moveToRecycleBin) => throw new InvalidOperationException("The synchronous DeleteFile was used.");
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw new InvalidOperationException("The synchronous DeleteFolder was used.");
    public string GetAvailableDiskSpace() => string.Empty;

    public Task<bool> ExistsFileAsync(Url url, CancellationToken cancellationToken)
    {
        AsyncCalls.Add("ExistsFileAsync");
        return Task.FromResult(false);
    }

    public Task<bool> ExistsFolderAsync(Url url, CancellationToken cancellationToken)
    {
        AsyncCalls.Add("ExistsFolderAsync");
        return Task.FromResult(false);
    }

    public Task<bool> DeleteFileAsync(Url url, bool moveToRecycleBin, CancellationToken cancellationToken)
    {
        AsyncCalls.Add("DeleteFileAsync");
        return Task.FromResult(true);
    }

    public Task<bool> DeleteFolderAsync(Url url, bool recursive, bool moveToRecycleBin, CancellationToken cancellationToken)
    {
        AsyncCalls.Add("DeleteFolderAsync");
        return Task.FromResult(true);
    }

    public Task<IMemoryFile?> ReadFileAsync(Url url) => throw new NotSupportedException();
    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw new NotSupportedException();
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
    public Task CreateFolderAsync(Url url) => throw new NotSupportedException();
    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
}

[TestClass]
public class AsyncOnlyFileSystemWalkerTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteTask_ForAFile_GoesThroughTheAsynchronousMember()
    {
        var drive = new AsyncOnlyFileSystem();
        var task = new ContentModelDeleteTask(drive, new Url("memdrive://folder/file.txt"), recursive: false, moveToRecycleBin: false, discoveryTask: null, new FakeErrorPromptService());

        await task.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "DeleteFileAsync" }, drive.AsyncCalls);
        Assert.IsNull(task.Error, "the synchronous member would have thrown, and the task would have reported it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DeleteTask_ForAFolder_GoesThroughTheAsynchronousMember()
    {
        var drive = new AsyncOnlyFileSystem();
        var task = new ContentModelDeleteTask(drive, new Url("memdrive://folder/sub/"), recursive: false, moveToRecycleBin: false, discoveryTask: null, new FakeErrorPromptService());

        await task.ExecuteAsync();

        CollectionAssert.AreEqual(new[] { "DeleteFolderAsync" }, drive.AsyncCalls);
        Assert.IsNull(task.Error);
    }
}
