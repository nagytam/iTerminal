using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Services;

public class MemoryDriveService : IMemoryDriveService
{
    public bool IsReadOnly => false;

    public MemoryFolder Root { get; private set; } = new MemoryFolder(new Url(Container.MEMORY_DRIVE));

    public string GetAvailableDiskSpace()
    {
        var availableDiskSpace = (SystemHelper.GetAvailableMemoryInMBs() * 1024 * 1024).ToFileSize();
        return availableDiskSpace;
    }

    public MemoryFolder? GetFolder(Url url)
    {
        var path = (url.Path ?? string.Empty).Trim('/');
        return string.IsNullOrEmpty(path) ? Root : Root.Navigate(path);
    }

    /// <summary>
    /// Resolves the folder that directly contains the entry addressed by <paramref name="url"/>
    /// (i.e. everything before the last '/' segment).
    /// </summary>
    private MemoryFolder? ResolveParentFolder(Url url)
    {
        var path = (url.Path ?? string.Empty).TrimEnd('/');
        var lastSlash = path.LastIndexOf('/');
        var parentPath = lastSlash >= 0 ? path[..lastSlash] : string.Empty;
        return Root.Navigate(parentPath);
    }

    public async Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        var folder = ResolveParentFolder(url) ?? throw new DirectoryNotFoundException($"Memory folder not found: {url.FullUrl}");
        await folder.WriteFileAsync(url, inputStream);
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        var folder = ResolveParentFolder(url);
        return folder is null ? null : await folder.ReadFileAsync(url);
    }

    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        var folder = ResolveParentFolder(url);
        return folder?.DeleteFile(url) ?? false;
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin)
    {
        var folder = ResolveParentFolder(url);
        return folder?.DeleteFolder(url, recursive) ?? false;
    }

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var sourceParent = ResolveParentFolder(sourceUrl) ?? throw new DirectoryNotFoundException($"Memory folder not found: {sourceUrl.FullUrl}");
        var targetParent = ResolveParentFolder(targetUrl) ?? throw new DirectoryNotFoundException($"Memory folder not found: {targetUrl.FullUrl}");
        sourceParent.MoveEntryTo(sourceUrl, targetUrl, targetParent, overwrite);
        return Task.CompletedTask;
    }

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        var sourceParent = ResolveParentFolder(sourceUrl) ?? throw new DirectoryNotFoundException($"Memory folder not found: {sourceUrl.FullUrl}");
        var targetParent = ResolveParentFolder(targetUrl) ?? throw new DirectoryNotFoundException($"Memory folder not found: {targetUrl.FullUrl}");
        sourceParent.Copy(sourceUrl, targetUrl, targetParent, overwrite);
        return Task.CompletedTask;
    }

    public bool ExistsFile(Url url)
    {
        var folder = ResolveParentFolder(url);
        return folder?.Files.ContainsKey(url.Name) ?? false;
    }

    public bool ExistsFolder(Url url)
    {
        var folder = ResolveParentFolder(url);
        return folder?.Folders.ContainsKey(url.Name) ?? false;
    }

    public Task CreateFolderAsync(Url url)
    {
        var folder = ResolveParentFolder(url) ?? throw new DirectoryNotFoundException($"Memory folder not found: {url.FullUrl}");
        folder.Folders.AddOrUpdate(url.Name, new MemoryFolder(url), (key, oldValue) => oldValue);
        return Task.CompletedTask;
    }

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var file = await ReadFileAsync(url) ?? throw new FileNotFoundException($"Memory file not found: {url.FullUrl}");
        using var sourceStream = new MemoryStream(file.Content ?? []);
        await StreamCopyHelper.CopyWithProgressAsync(sourceStream, destination, progress, cancellationToken);
    }

    public async Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await StreamCopyHelper.CopyWithProgressAsync(source, buffer, progress, cancellationToken);
        buffer.Position = 0;
        await WriteFileAsync(url, buffer);
    }
}
