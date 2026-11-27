using iConsole.Data;
using iConsole.Interfaces;

namespace iConsole.Test.Interfaces;

/// <summary>
/// A drive written before the asynchronous members existed: it implements only the synchronous ones. The defaults must
/// answer through them, so an implementer outside this repository keeps working unchanged.
/// </summary>
file sealed class SynchronousOnlyFileSystem : IFileSystem
{
    public List<string> Calls { get; } = [];

    public bool IsReadOnly => false;
    public bool ExistsFile(Url url) { Calls.Add("ExistsFile"); return true; }
    public bool ExistsFolder(Url url) { Calls.Add("ExistsFolder"); return false; }
    public bool DeleteFile(Url url, bool moveToRecycleBin) { Calls.Add($"DeleteFile:{moveToRecycleBin}"); return true; }
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) { Calls.Add($"DeleteFolder:{recursive}:{moveToRecycleBin}"); return false; }
    public string GetAvailableDiskSpace() => string.Empty;
    public Task<IMemoryFile?> ReadFileAsync(Url url) => throw new NotSupportedException();
    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw new NotSupportedException();
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
    public Task CreateFolderAsync(Url url) => throw new NotSupportedException();
    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();
}

[TestClass]
public class IFileSystemAsyncDefaultsTest
{
    [Timeout(60_000)]
    [TestMethod]
    public async Task AsyncMembers_AnswerThroughTheSynchronousOnesWhenNotOverridden()
    {
        var fake = new SynchronousOnlyFileSystem();
        IFileSystem drive = fake; // the defaults exist on the interface only
        var url = new Url("file:///tmp/x.txt");

        Assert.IsTrue(await drive.ExistsFileAsync(url, CancellationToken.None));
        Assert.IsFalse(await drive.ExistsFolderAsync(url, CancellationToken.None));
        Assert.IsTrue(await drive.DeleteFileAsync(url, moveToRecycleBin: true, CancellationToken.None));
        Assert.IsFalse(await drive.DeleteFolderAsync(url, recursive: true, moveToRecycleBin: false, CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { "ExistsFile", "ExistsFolder", "DeleteFile:True", "DeleteFolder:True:False" },
            fake.Calls);
    }
}
