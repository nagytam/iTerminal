using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Services;
using Org.BouncyCastle.Utilities.Zlib;
using System;

namespace iTerminal.Core.Model.ContentModels;

public class ContentModelMemoryDrive(
    string name,
    ISettingsService settingsService,
    IMemoryDriveService memoryDriveService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService
    )
    : ContentModelBase(name, frequentExecutionGuardService), IContentModel
{
    public override bool IsReadOnly => false;

    public override void ReadUrl()
    {
        var key = Name;
        var url = _settingsService.Get(key) ?? Directory.GetCurrentDirectory();
        SetUrl(url);
    }

    public override string GetHeader(int width)
    {
        var url = Url.FullUrl;
        url = url.Replace("///", "//");
        var available = memoryDriveService.GetAvailableDiskSpace();
        url += $" [Free: {available}]";
        var header = url.Shrink(width) ?? string.Empty;
        return header;
    }

    public override string GetFooter() => EntryCountFooter();

    private FolderHistoryService _folderHistoryService { get; set; } = folderHistoryService;
    private ISettingsService _settingsService { get; set; } = settingsService;

    private bool _listensForMirror;

    public override void SetUrl(Url url)
    {
        if (!_listensForMirror)
        {
            _listensForMirror = true;
            // Files mirrored from an earlier run are loaded in the background, possibly after the
            // first listing: list again once they are in.
            memoryDriveService.MirrorLoaded += (_, _) =>
            {
                if (Url is not null)
                {
                    Refresh();
                }
            };
        }

        _settingsService.Set(Name, url);
        _folderHistoryService.Add(url);
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

        var currentFolder = memoryDriveService.GetFolder(Url) ?? memoryDriveService.Root;

        // Built aside and swapped in whole: this can run on the job's thread as well as the UI's.
        var entries = new List<IEntry>();
        if (!string.IsNullOrEmpty(Url.Path?.Trim('/')))
        {
            entries.Add(new MemoryDriveEntry("..", Url.ParentUrl));
        }

        foreach (var folder in currentFolder.Folders)
        {
            entries.Add(new MemoryDriveEntry(folder.Value));
        }
        foreach (var file in currentFolder.Files)
        {
            entries.Add(new MemoryDriveEntry(file.Value));
        }

        Entries.ReplaceAll(entries);
    }

    public override async Task CreateFolderAsync(Url url)
    {
        await memoryDriveService.CreateFolderAsync(url);
    }

    public override bool ExistsFile(Url url)
    {
        return memoryDriveService.ExistsFile(url);
    }

    public override bool ExistsFolder(Url url)
    {
        return memoryDriveService.ExistsFolder(url);
    }

    public override void Delete(Url url, bool recursive, bool moveToRecycleBin)
    {
        if (url.PathType == PathType.Folder)
        {
            memoryDriveService.DeleteFolder(url, recursive, moveToRecycleBin);
        }
        else if (url.PathType == PathType.File)
        {
            memoryDriveService.DeleteFile(url, moveToRecycleBin);
        }
        Refresh();
    }

    public override async Task WriteFileAsync(Url url, MemoryStream inputStream)
    {
        await memoryDriveService.WriteFileAsync(url, inputStream);
    }

    public override async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        return await memoryDriveService.ReadFileAsync(url);
    }

    public override async Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        await memoryDriveService.MoveAsync(sourceUrl, targetUrl, overwrite);
    }

    public override async Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite)
    {
        await memoryDriveService.CopyAsync(sourceUrl, targetUrl, overwrite);
    }

}
