using iConsole.Data;
namespace iConsole.Interfaces;

public interface IFileSystem
{
    bool IsReadOnly { get; }
    Task WriteFileAsync(Url url, MemoryStream inputStream);
    Task<IMemoryFile?> ReadFileAsync(Url url);
    bool DeleteFile(Url url, bool moveToRecycleBin);
    bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin);
    string GetAvailableDiskSpace();
    Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite);
    Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite);
    bool ExistsFile(Url url);
    bool ExistsFolder(Url url);

    /// <summary>
    /// Whether a file is at <paramref name="url"/>. Asks the drive without blocking the caller; a cancelled token is
    /// rethrown rather than read as "not there". The default answers through <see cref="ExistsFile"/>.
    /// </summary>
    Task<bool> ExistsFileAsync(Url url, CancellationToken cancellationToken) => Task.FromResult(ExistsFile(url));

    /// <summary>
    /// Whether a folder is at <paramref name="url"/>. Asks the drive without blocking the caller; a cancelled token is
    /// rethrown rather than read as "not there". The default answers through <see cref="ExistsFolder"/>.
    /// </summary>
    Task<bool> ExistsFolderAsync(Url url, CancellationToken cancellationToken) => Task.FromResult(ExistsFolder(url));

    /// <summary>
    /// Deletes the file at <paramref name="url"/> without blocking the caller. The result is false when there was
    /// nothing to delete. The default deletes through <see cref="DeleteFile"/>.
    /// </summary>
    Task<bool> DeleteFileAsync(Url url, bool moveToRecycleBin, CancellationToken cancellationToken) => Task.FromResult(DeleteFile(url, moveToRecycleBin));

    /// <summary>
    /// Deletes the folder at <paramref name="url"/> without blocking the caller. The result is false when there was
    /// nothing to delete. The default deletes through <see cref="DeleteFolder"/>.
    /// </summary>
    Task<bool> DeleteFolderAsync(Url url, bool recursive, bool moveToRecycleBin, CancellationToken cancellationToken) => Task.FromResult(DeleteFolder(url, recursive, moveToRecycleBin));

    Task CreateFolderAsync(Url url);

    /// <summary>
    /// Streams the content of the entry at <paramref name="url"/> into <paramref name="destination"/>,
    /// reporting cumulative bytes copied via <paramref name="progress"/> when supplied.
    /// </summary>
    Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken);

    /// <summary>
    /// Streams <paramref name="source"/> into the entry at <paramref name="url"/>, reporting
    /// cumulative bytes copied via <paramref name="progress"/> when supplied.
    /// </summary>
    Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken);
}
