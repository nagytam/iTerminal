using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services.Checksums;

namespace iTerminal.Core.Test.Helper;

/// <summary>
/// Wraps a real <see cref="IFileSystem"/> and damages what it writes: after a file whose name contains
/// <see cref="CorruptNamesContaining"/> has been streamed in, one byte on disk is flipped. Also counts
/// the drive's native copy and move calls, so a test can tell whether a transfer was streamed.
/// </summary>
internal sealed class CorruptingFileSystem(IFileSystem inner, string corruptNamesContaining = "") : IFileSystem
{
    public string CorruptNamesContaining { get; set; } = corruptNamesContaining;
    public int NativeMoves { get; private set; }
    public int NativeCopies { get; private set; }

    public bool IsReadOnly => inner.IsReadOnly;
    public bool ExistsFile(Url url) => inner.ExistsFile(url);
    public bool ExistsFolder(Url url) => inner.ExistsFolder(url);
    public Task<IMemoryFile?> ReadFileAsync(Url url) => inner.ReadFileAsync(url);
    public Task WriteFileAsync(Url url, MemoryStream inputStream) => inner.WriteFileAsync(url, inputStream);
    public bool DeleteFile(Url url, bool moveToRecycleBin) => inner.DeleteFile(url, moveToRecycleBin);
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => inner.DeleteFolder(url, recursive, moveToRecycleBin);
    public string GetAvailableDiskSpace() => inner.GetAvailableDiskSpace();
    public Task CreateFolderAsync(Url url) => inner.CreateFolderAsync(url);
    public Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken) =>
        inner.StreamToAsync(url, destination, progress, cancellationToken);

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        NativeMoves++;
        return inner.MoveAsync(sourceUrl, targetUrl, overwrite);
    }

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        NativeCopies++;
        return inner.CopyAsync(sourceUrl, targetUrl, overwrite);
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        await inner.StreamFromAsync(url, source, progress, cancellationToken);
        if (CorruptNamesContaining.Length > 0
            && Path.GetFileName(url.FullName).Contains(CorruptNamesContaining, StringComparison.OrdinalIgnoreCase)
            && new FileInfo(url.FullName).Length > 0)
        {
            byte[] bytes = await File.ReadAllBytesAsync(url.FullName, cancellationToken);
            bytes[0] ^= 0xFF;
            await File.WriteAllBytesAsync(url.FullName, bytes, cancellationToken);
        }
    }
}

/// <summary>An <see cref="IChecksumService"/> that counts its calls and can run a hook before hashing.</summary>
internal sealed class CountingChecksumService(IChecksumService? inner = null) : IChecksumService
{
    private readonly IChecksumService _inner = inner ?? new ChecksumService();
    public int Calls { get; private set; }
    public Action<int>? BeforeCompute { get; set; }

    public Task<ChecksumResult> ComputeAsync(Url url, IFileSystem fileSystem, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
    {
        Calls++;
        BeforeCompute?.Invoke(Calls);
        return _inner.ComputeAsync(url, fileSystem, progress, cancellationToken);
    }
}
