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
