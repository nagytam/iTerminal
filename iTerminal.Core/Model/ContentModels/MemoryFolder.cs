using iConsole.Data;
using iConsole.Interfaces;
using System.Collections.Concurrent;

namespace iTerminal.Core.Model.ContentModels;

public class MemoryFolder(Url url) 
    : MemoryBase(url)
{
    public string FolderName { get; set; } = url.DisplayName;
    public ConcurrentDictionary<string, MemoryFile> Files { get; set; } = [];
    public ConcurrentDictionary<string, MemoryFolder> Folders { get; set; } = [];

    public Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        var memoryFile = new MemoryFile(url.Name, inputStream) { Url = url };
        Files.AddOrUpdate(url.Name, memoryFile, (key, existingFile) => { return memoryFile; });
        return Task.CompletedTask;
    }

    public Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        if (Files.TryGetValue(url.Name, out var memoryFile))
        {
            return Task.FromResult<IMemoryFile?>(memoryFile);
        }
        return Task.FromResult<IMemoryFile?>(null);
    }

    public bool DeleteFile(Url url)
    {
        return Files.TryRemove(url.Name, out var _);
    }

    public bool DeleteFolder(Url url, bool recursive)
    {
        if (!Folders.TryGetValue(url.Name, out var folder))
        {
            return false;
        }
        if (!recursive && (folder.Files.Count > 0 || folder.Folders.Count > 0))
        {
            return false;
        }
        return Folders.TryRemove(url.Name, out var _);
    }

    /// <summary>
    /// Resolves the nested folder addressed by <paramref name="path"/> (a '/'-delimited path
    /// relative to this folder), walking <see cref="Folders"/> one segment at a time.
    /// </summary>
    public MemoryFolder? Navigate(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return this;
        }
        var current = this;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.Folders.TryGetValue(segment, out var next))
            {
                return null;
            }
            current = next;
        }
        return current;
    }

    /// <summary>
    /// Moves (or renames, when <paramref name="targetParent"/> is <see langword="this"/>) the
    /// file or folder addressed by <paramref name="sourceUrl"/> from this folder into
    /// <paramref name="targetParent"/> under <paramref name="targetUrl"/>'s leaf name.
    /// </summary>
    public void MoveEntryTo(Url sourceUrl, Url targetUrl, MemoryFolder targetParent, bool overwrite)
    {
        if (sourceUrl.PathType == PathType.Folder)
        {
            if (!Folders.TryRemove(sourceUrl.Name, out var folder))
            {
                throw new DirectoryNotFoundException($"Memory folder not found: {sourceUrl.FullUrl}");
            }
            if (!overwrite && targetParent.Folders.ContainsKey(targetUrl.Name))
            {
                Folders.TryAdd(sourceUrl.Name, folder);
                throw new IOException($"Target folder already exists: {targetUrl.FullUrl}");
            }
            folder.Rebase(AsFolderUrl(targetUrl));
            targetParent.Folders[targetUrl.Name] = folder;
        }
        else
        {
            if (!Files.TryRemove(sourceUrl.Name, out var file))
            {
                throw new FileNotFoundException($"Memory file not found: {sourceUrl.FullUrl}");
            }
            if (!overwrite && targetParent.Files.ContainsKey(targetUrl.Name))
            {
                Files.TryAdd(sourceUrl.Name, file);
                throw new IOException($"Target file already exists: {targetUrl.FullUrl}");
            }
            file.Url = targetUrl;
            file.FileName = targetUrl.Name;
            targetParent.Files[targetUrl.Name] = file;
        }
    }

    /// <summary>
    /// Copies the file or folder addressed by <paramref name="sourceUrl"/> from this folder into
    /// <paramref name="targetParent"/> under <paramref name="targetUrl"/>'s leaf name. Folders are
    /// deep-cloned (contents included).
    /// </summary>
    public void Copy(Url sourceUrl, Url targetUrl, MemoryFolder targetParent, bool overwrite)
    {
        if (sourceUrl.PathType == PathType.Folder)
        {
            if (!Folders.TryGetValue(sourceUrl.Name, out var folder))
            {
                throw new DirectoryNotFoundException($"Memory folder not found: {sourceUrl.FullUrl}");
            }
            if (!overwrite && targetParent.Folders.ContainsKey(targetUrl.Name))
            {
                throw new IOException($"Target folder already exists: {targetUrl.FullUrl}");
            }
            targetParent.Folders[targetUrl.Name] = folder.DeepClone(AsFolderUrl(targetUrl));
        }
        else
        {
            if (!Files.TryGetValue(sourceUrl.Name, out var file))
            {
                throw new FileNotFoundException($"Memory file not found: {sourceUrl.FullUrl}");
            }
            if (!overwrite && targetParent.Files.ContainsKey(targetUrl.Name))
            {
                throw new IOException($"Target file already exists: {targetUrl.FullUrl}");
            }
            targetParent.Files[targetUrl.Name] = new MemoryFile(targetUrl.Name, file.Content) { Url = targetUrl };
        }
    }

    /// <summary>
    /// Re-points this folder (and, recursively, everything nested inside it) at
    /// <paramref name="newUrl"/> after it has been moved to a new location in the tree.
    /// </summary>
    private void Rebase(Url newUrl)
    {
        Url = newUrl;
        FolderName = newUrl.Name;
        var basePath = newUrl.Path.EndsWith('/') ? newUrl.Path[..^1] : newUrl.Path;
        foreach (var kvp in Folders)
        {
            kvp.Value.Rebase(new Url(Container.MEMORY_DRIVE + CombinePath(basePath, kvp.Key) + "/"));
        }
        foreach (var kvp in Files)
        {
            var childUrl = new Url(Container.MEMORY_DRIVE + CombinePath(basePath, kvp.Key));
            kvp.Value.Url = childUrl;
            kvp.Value.FileName = childUrl.Name;
        }
    }

    /// <summary>
    /// Recursively clones this folder's entire subtree, re-based under <paramref name="newUrl"/>.
    /// </summary>
    private MemoryFolder DeepClone(Url newUrl)
    {
        var clone = new MemoryFolder(newUrl);
        var basePath = newUrl.Path.EndsWith('/') ? newUrl.Path[..^1] : newUrl.Path;
        foreach (var kvp in Folders)
        {
            var childUrl = new Url(Container.MEMORY_DRIVE + CombinePath(basePath, kvp.Key) + "/");
            clone.Folders[kvp.Key] = kvp.Value.DeepClone(childUrl);
        }
        foreach (var kvp in Files)
        {
            var childUrl = new Url(Container.MEMORY_DRIVE + CombinePath(basePath, kvp.Key));
            clone.Files[kvp.Key] = new MemoryFile(kvp.Key, kvp.Value.Content) { Url = childUrl };
        }
        return clone;
    }

    /// <summary>
    /// A folder's URL always ends with '/': without it the URL parses as a file, and the folder is
    /// then listed, entered and copied as one. A target typed in a dialog usually has no slash.
    /// </summary>
    private static Url AsFolderUrl(Url url)
        => url.FullUrl.EndsWith('/') ? url : new Url(url.FullUrl + "/");

    private static string CombinePath(string basePath, string name)
        => string.IsNullOrEmpty(basePath) ? name : basePath + "/" + name;
}
