using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using Serilog;
using System.Diagnostics;

namespace iTerminal.Core.Model.ContentModels;

/// <summary>
/// Shared behavior for remote drive content models (SSH, SMB): persists the panel URL, keeps
/// folder history, caches directory listings via <see cref="IMemoryCacheService"/>, diffs new
/// listings into <see cref="ContentModelBase.Entries"/> in place, and delegates all file-system
/// operations to the injected <see cref="IRemoteFileSystemService"/>. Subclasses only implement
/// the protocol-specific <see cref="Connect"/> (credential lookup + service connect call).
/// </summary>
public abstract class ContentModelRemoteDrive(
    string name,
    ISettingsService settingsService,
    IRemoteFileSystemService remoteFileSystemService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IMemoryCacheService memoryCacheService,
    ILogger logger)
    : ContentModelBase(name, frequentExecutionGuardService), IContentModel, IFolderListingFileSystem
{
    private readonly Lock _lock = new();

    public override bool IsReadOnly => false;

    /// <summary>Looks up credentials and connects <paramref name="url"/> on the underlying service.</summary>
    protected abstract void Connect(Url url);

    /// <summary>Formats the log message when <see cref="Connect"/> throws.</summary>
    protected virtual string FormatConnectionError(Url url, Exception exception) =>
        $"Connection error: {url.Host}: {exception.Message}";

    public override void ReadUrl()
    {
        var key = Name;
        var path = settingsService.Get(key) ?? Directory.GetCurrentDirectory();
        SetUrl(path);
    }

    public override string GetHeader(int width)
    {
        var path = Url.FullUrl;
        path = path.Replace("///", "//");
        if (remoteFileSystemService.IsConnected)
        {
            var available = remoteFileSystemService.GetAvailableDiskSpace();
            path += string.IsNullOrEmpty(available) ? string.Empty : $" [Free: {available}]";
        }
        else
        {
            path += " (Offline)";
        }
        return path.Shrink(width) ?? string.Empty;
    }

    public override string GetFooter() => EntryCountFooter();

    public override void SetUrl(Url url)
    {
        // A panel always shows a folder, but "ftp://host/pub" typed without the slash parses as a
        // file — and then nothing built on the panel's URL (a new file or folder, a copy target)
        // could be put inside it.
        if (url.PathType != PathType.Folder && !url.FullUrl.EndsWith('/'))
        {
            url = new Url(url.FullUrl + "/");
        }

        settingsService.Set(Name, url);
        folderHistoryService.Add(url);
        try
        {
            Connect(url);
        }
        catch (Exception ex)
        {
            logger.Error(FormatConnectionError(url, ex));
        }
        Entries.Clear();
        Url = url;
        ResetNavigation();

        Refresh();
    }

    public override void Refresh(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            Entries.Clear();
        }
        if (string.IsNullOrEmpty(Url))
        {
            return;
        }

        var key = GetType().Name + Url.FullUrl;
        lock (_lock)
        {
            // forceRefresh means the caller knows the folder changed under us — a copy or a delete
            // that has just landed on this drive. Handing back the cached listing is then the one
            // answer that is certainly wrong, so re-list and replace what is cached. Clearing
            // Entries above without this only made the panel redraw the same stale rows.
            IEnumerable<IEntry>? entries = forceRefresh
                ? null
                : memoryCacheService.Get(key) as IEnumerable<IEntry>;
            if (entries == null)
            {
                entries = remoteFileSystemService.List(Url);
                memoryCacheService.Set(key, entries);
            }
            UpdateEntries(entries);
        }
    }

    private void UpdateEntries(IEnumerable<IEntry> entries)
    {
        int i = 0;
        foreach (var entry in entries)
        {
            if (i < Entries.Count && Entries[i].Url.FullUrl != entry.Url.FullUrl)
            {
                Entries.ReplaceAt(i, entry);
            }
            else if (i >= Entries.Count)
            {
                Entries.Add(entry);
            }
            i++;
        }
        while (Entries.Count > i)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }

    public override bool ExistsFile(Url url)
    {
        return remoteFileSystemService.ExistsFile(url);
    }

    public override bool ExistsFolder(Url url)
    {
        return remoteFileSystemService.ExistsFolder(url);
    }

    public override async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        return await remoteFileSystemService.ReadFileAsync(url);
    }

    public override async Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        await remoteFileSystemService.WriteFileAsync(url, inputStream);
    }

    public override async Task CreateFolderAsync(Url url)
    {
        await remoteFileSystemService.CreateFolderAsync(url);
    }

    public override void Delete(Url url, bool recursive, bool moveToRecycleBin)
    {
        Debug.Assert(!recursive || (url.PathType == PathType.Folder));
        switch (url.PathType)
        {
            case PathType.File:
                {
                    remoteFileSystemService.DeleteFile(url, moveToRecycleBin);
                    break;
                }
            case PathType.Folder:
                {
                    remoteFileSystemService.DeleteFolder(url, recursive, moveToRecycleBin);
                    break;
                }
            default:
                {
                    throw new NotImplementedException();
                }
        }
    }

    public override async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        await remoteFileSystemService.MoveAsync(sourceUrl, targetUrl, overwrite);
    }

    public override async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        await remoteFileSystemService.CopyAsync(sourceUrl, targetUrl, overwrite);
    }

    /// <summary>
    /// The folder's children, which is what a folder copy to another drive and a recursive delete walk
    /// with — so every remote drive supports both, not only the ones that said so. A drive whose service
    /// lists children more cheaply overrides this.
    /// </summary>
    public virtual IEnumerable<Url> ListChildren(Url folderUrl) =>
        remoteFileSystemService.List(folderUrl)
            .Where(entry => entry.DisplayName != ".." && entry.Url.DisplayName != "..")
            .Select(entry => entry.Url);
}
