using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using System.IO.Compression;

namespace iTerminal.Core.Services;

/// <summary>
/// Read-only <see cref="IFileSystem"/> backing for embedded zip archives (<c>zip://...@/entry</c>
/// URLs). Only reading entries out of the archive is supported; every write member throws a
/// clear <see cref="NotSupportedException"/> instead of silently doing nothing.
/// </summary>
public class ArchiveFileSystemService : IArchiveFileSystemService
{
    public bool IsReadOnly => true;

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var zipToOpen = new FileStream(url.ContainerFilePath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
        var entry = archive.GetEntry(url.FullName.TrimStart('/'));
        if (entry is null)
        {
            return Task.FromResult<IMemoryFile?>(null);
        }
        using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream();
        entryStream.CopyTo(memoryStream);
        return Task.FromResult<IMemoryFile?>(new MemoryFile(entry.Name, memoryStream.ToArray()));
    }

    public bool ExistsFile(Url url)
    {
        using var zipToOpen = new FileStream(url.ContainerFilePath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
        return archive.GetEntry(url.FullName.TrimStart('/')) is not null;
    }

    public bool ExistsFolder(Url url)
    {
        using var zipToOpen = new FileStream(url.ContainerFilePath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
        var prefix = url.FullName.TrimStart('/');
        return archive.Entries.Any(e => e.FullName.StartsWith(prefix, StringComparison.Ordinal));
    }

    public IEnumerable<Url> ListChildren(Url folderUrl)
    {
        using var zipToOpen = new FileStream(folderUrl.ContainerFilePath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
        var prefix = folderUrl.FullName.TrimStart('/');

        var children = new List<Url>();
        var seenFolders = new HashSet<string>();
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var remainder = entry.FullName[prefix.Length..];
            if (string.IsNullOrEmpty(remainder))
            {
                continue;
            }
            var slashIndex = remainder.IndexOf('/');
            if (slashIndex < 0)
            {
                children.Add(new Url(Container.ZIP_ARCHIVE + folderUrl.ContainerFilePath + "@/" + prefix + remainder));
            }
            else
            {
                var folderName = remainder[..(slashIndex + 1)];
                if (seenFolders.Add(folderName))
                {
                    children.Add(new Url(Container.ZIP_ARCHIVE + folderUrl.ContainerFilePath + "@/" + prefix + folderName));
                }
            }
        }
        return children;
    }

    public string GetAvailableDiskSpace() => string.Empty;

    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw ReadOnlyException();
    public bool DeleteFile(Url url, bool moveToRecycleBin) => throw ReadOnlyException();
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw ReadOnlyException();
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnlyException();
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnlyException();
    public Task CreateFolderAsync(Url url) => throw ReadOnlyException();

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var zipToOpen = new FileStream(url.ContainerFilePath, FileMode.Open, FileAccess.Read);
        using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
        var entry = archive.GetEntry(url.FullName.TrimStart('/'))
            ?? throw new FileNotFoundException($"Archive entry not found: {url.FullUrl}");
        using var entryStream = entry.Open();
        await StreamCopyHelper.CopyWithProgressAsync(entryStream, destination, progress, cancellationToken);
    }

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw ReadOnlyException();

    private static NotSupportedException ReadOnlyException() =>
        new("Archives are read-only; only copying files out of an archive is supported.");
}
