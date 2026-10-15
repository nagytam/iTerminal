using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;

namespace iTerminal.Core.Services.Recycle;

/// <summary>A row of the <c>recycle://</c> drive: a deleted file or folder, dated by when it was deleted.</summary>
public record RecycledEntry : VirtualDriveEntry
{
    public RecycledEntry(Url url, RecycledItem item)
        : base(url, null, item.IsFolder ? Url.Folder : item.Size.ToFileSize(), item.Deleted == DateTime.MinValue ? null : item.Deleted)
    {
        Item = item;
        Size = item.Size;
        Detail = "Deleted from " + (Path.GetDirectoryName(item.OriginalPath) ?? item.OriginalPath);
    }

    public RecycledItem Item { get; }
}

/// <summary>What a restore did: how many came back, and what was said about the ones that did not.</summary>
public sealed record RestoreOutcome(int Restored, IReadOnlyList<string> Failures);

/// <summary>
/// The recycle bin as the <c>recycle://</c> drive sees it. Async by contract — every call reads or
/// moves files — except the <see cref="IFileSystem"/> members, which the copy and delete tasks call
/// from their own worker threads.
/// </summary>
public interface IRecycleBinService : IFileSystem
{
    /// <summary>Empty, or why there is no bin to show on this system.</summary>
    string Unavailable { get; }

    /// <summary>What is in the bin, most recently deleted first.</summary>
    Task<IReadOnlyList<IEntry>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Puts the rows at <paramref name="urls"/> back where they were deleted from.</summary>
    Task<RestoreOutcome> RestoreAsync(IReadOnlyList<Url> urls, CancellationToken cancellationToken = default);
}

/// <summary>
/// <para><b>A row's URL is its name</b> — <c>recycle://report.pdf</c> — so that copying one out
/// names the copy correctly and the panel is recognised as the folder a deleted row was in. Two
/// deleted files can share a name, so the second is listed as <c>report (2).pdf</c>: numbered in
/// the order they were deleted, which keeps a row's URL the same from one listing to the next.</para>
///
/// <para><b>A deleted folder is one row, and a file to every operation</b> (its URL has no trailing
/// slash). Restoring or deleting it for good takes the whole folder, which is the only thing either
/// can mean here; it cannot be entered or copied out — restore it first.</para>
/// </summary>
public class RecycleBinService(IRecycleBinStore store) : IRecycleBinService
{
    private readonly Lock _lock = new();
    private Dictionary<string, RecycledItem> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The bin this operating system keeps.</summary>
    public static IRecycleBinStore CreateStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsRecycleBinStore();
        }

        return OperatingSystem.IsLinux()
            ? new FreedesktopTrashStore()
            : new UnavailableRecycleBinStore("The Trash on this system keeps no record of where a file came from, so it cannot be listed or restored here.");
    }

    public string Unavailable => store.Unavailable;

    public static Url UrlOf(string listedName) => new(Container.RECYCLE + listedName);

    public Task<IReadOnlyList<IEntry>> ListAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<IEntry>>(() =>
        {
            if (store.Unavailable.Length > 0)
            {
                throw new NotSupportedException(store.Unavailable);
            }

            var named = Name(store.List());
            lock (_lock)
            {
                _byName = named.ToDictionary(pair => pair.ListedName, pair => pair.Item, StringComparer.OrdinalIgnoreCase);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return [.. named
                .OrderByDescending(pair => pair.Item.Deleted)
                .ThenBy(pair => pair.ListedName, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new RecycledEntry(UrlOf(pair.ListedName), pair.Item))];
        }, cancellationToken);

    /// <summary>
    /// Gives every item the name it is listed under: its own, or "name (2).ext" for a second deleted
    /// file of that name. Numbered oldest first, by the store's key when two were deleted in the
    /// same instant, so the numbering does not move between listings.
    /// </summary>
    internal static List<(string ListedName, RecycledItem Item)> Name(IEnumerable<RecycledItem> items)
    {
        var named = new List<(string, RecycledItem)>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items.OrderBy(i => i.Deleted).ThenBy(i => i.Key, StringComparer.Ordinal))
        {
            var name = item.Name;
            for (var number = 2; !taken.Add(name); number++)
            {
                name = $"{Path.GetFileNameWithoutExtension(item.Name)} ({number}){Path.GetExtension(item.Name)}";
            }

            named.Add((name, item));
        }

        return named;
    }

    private RecycledItem? Find(Url url)
    {
        var name = url.Path.Trim('/');
        lock (_lock)
        {
            return _byName.GetValueOrDefault(name);
        }
    }

    private RecycledItem Require(Url url) =>
        Find(url) ?? throw new FileNotFoundException($"{url.Name} is no longer in the recycle bin.");

    public Task<RestoreOutcome> RestoreAsync(IReadOnlyList<Url> urls, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var restored = 0;
            var failures = new List<string>();
            foreach (var url in urls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var item = Require(url);
                    store.Restore(item);
                    Forget(url);
                    restored++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    failures.Add(exception.Message);
                }
            }

            return new RestoreOutcome(restored, failures);
        }, cancellationToken);

    private void Forget(Url url)
    {
        lock (_lock)
        {
            _byName.Remove(url.Path.Trim('/'));
        }
    }

    #region IFileSystem: read a deleted file, or delete it for good
    public bool IsReadOnly => false;

    public bool ExistsFile(Url url) => Find(url) is not null;

    public bool ExistsFolder(Url url) => url.Path.Trim('/').Length == 0;

    /// <summary>F8 on this drive: there is no bin to move to from here, so the delete is for good.</summary>
    public bool DeleteFile(Url url, bool moveToRecycleBin)
    {
        if (Find(url) is not { } item)
        {
            return false;
        }

        store.Purge(item);
        Forget(url);
        return true;
    }

    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => DeleteFile(url, moveToRecycleBin);

    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var item = Require(url);
        if (item.IsFolder)
        {
            throw new NotSupportedException($"{item.Name} is a folder: restore it, then copy it.");
        }

        await using var source = new FileStream(item.ContentPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
            progress?.Report(total);
        }
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        var item = Require(url);
        if (item.IsFolder)
        {
            throw new NotSupportedException($"{item.Name} is a folder: restore it to look inside.");
        }

        return new MemoryFile(url, await File.ReadAllBytesAsync(item.ContentPath));
    }

    private static NotSupportedException NothingIsPutHere() =>
        new("Nothing is copied into the recycle bin: delete a file (F8) to put it there.");

    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw NothingIsPutHere();

    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw NothingIsPutHere();

    public Task CreateFolderAsync(Url url) => throw NothingIsPutHere();

    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw NothingIsPutHere();

    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw NothingIsPutHere();

    public string GetAvailableDiskSpace() => string.Empty;
    #endregion
}
