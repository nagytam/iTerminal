using iConsole.Data;
using iConsole.Interfaces;

namespace iConsole.Test.Services;

/// <summary>
/// Minimal IFileSystem implementation that reads files from the real local file system.
/// Used by fixture-based tests that need to load actual sample files.
/// </summary>
internal sealed class LocalFileSystemAdapter : IFileSystem
{
    public bool IsReadOnly => true;

    public bool ExistsFile(Url url) => System.IO.File.Exists(url.Path);
    public bool ExistsFolder(Url url) => System.IO.Directory.Exists(url.Path);

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        if (!System.IO.File.Exists(url.Path))
            return Task.FromResult<IMemoryFile?>(null);

        var bytes = System.IO.File.ReadAllBytes(url.Path);
        return Task.FromResult<IMemoryFile?>(new ByteMemoryFile(bytes));
    }

    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw new NotSupportedException();
    public bool DeleteFile(Url url, bool moveToRecycleBin) => throw new NotSupportedException();
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw new NotSupportedException();
    public string GetAvailableDiskSpace() => string.Empty;
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw new NotSupportedException();
    public Task CreateFolderAsync(Url url) => throw new NotSupportedException();

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var sourceStream = System.IO.File.OpenRead(url.Path);
        await sourceStream.CopyToAsync(destination, cancellationToken);
        progress?.Report(sourceStream.Length);
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw new NotSupportedException();

    private sealed class ByteMemoryFile(byte[] content) : IMemoryFile
    {
        public byte[]? Content => content;
    }
}
