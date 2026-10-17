using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services;
using System.IO.Compression;

namespace iTerminal.Core.Model.ContentModels;

public class ContentModelFileSystemArchive(
    string name,
    TaskManager taskManager,
    ISettingsService settingsService,
    FolderHistoryService folderHistoryService,
    IFrequentExecutionGuardService frequentExecutionGuardService,
    IArchiveFileSystemService archiveFileSystemService
    )
    : ContentModelBase(name, frequentExecutionGuardService), IContentModel
{
    public override bool IsReadOnly => true;
    public override void ReadUrl()
    {
        var key = Name;
        var path = _settingsService.Get(key) ?? Directory.GetCurrentDirectory();
        SetUrl(path);
    }

    public override string GetHeader(int width)
    {
        var header = ArchivePath?.Shrink(width) ?? string.Empty;
        return header;
    }

    public override string GetFooter() => EntryCountFooter();

    public string ArchivePath { get; private set; } = string.Empty; // TODO
    public override Url Url { get; protected set; } = new(Directory.GetCurrentDirectory());

    private TaskManager _taskManager { get; set; } = taskManager;
    private FolderHistoryService _folderHistoryService { get; set; } = folderHistoryService;
    private ISettingsService _settingsService { get; set; } = settingsService;

    public override void SetUrl(Url url)
    {
        _settingsService.Set(Name, url.FullUrl);
        _folderHistoryService.Add(url);
        Entries.Clear();
        ArchivePath = url.Path;
        ResetNavigation();

        Refresh();
    }

    public override void Refresh(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            Entries.Clear();
        }
        //if (!_taskManager.HasTask<FilePanelRefreshTask>((t) => true))
        //{
        //    _taskManager.Add(new FilePanelRefreshTask(this));
        //}
        if (string.IsNullOrEmpty(ArchivePath))
        {
            return;
        }
        var archivePath = ArchivePath.Replace(Container.ZIP_ARCHIVE, string.Empty);
        archivePath = archivePath[..archivePath.IndexOf("@/", StringComparison.Ordinal)];
        using FileStream zipToOpen = new(archivePath, FileMode.Open, FileAccess.Read);
        using ZipArchive archive = new(zipToOpen, ZipArchiveMode.Read);

        Entries.Clear();
        var path = Path.GetDirectoryName(archivePath);
        Entries.Add(new ArchiveEntry("..", path!));
        foreach (var zipArchiveEntry in archive.Entries)
        {
            Entries.Add(new ArchiveEntry(zipArchiveEntry));
        }
    }

    public override async Task<IMemoryFile?> ReadFileAsync(Url url) => await archiveFileSystemService.ReadFileAsync(url);
    public override bool ExistsFile(Url url) => archiveFileSystemService.ExistsFile(url);
    public override bool ExistsFolder(Url url) => archiveFileSystemService.ExistsFolder(url);

    public override void Delete(Url url, bool recursive, bool moveToRecycleBin) => throw ReadOnlyException();
    public override Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnlyException();
    public override Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnlyException();
    public override Task CreateFolderAsync(Url url) => throw ReadOnlyException();
    public override Task WriteFileAsync(Url url, MemoryStream inputStream) => throw ReadOnlyException();

    private static NotSupportedException ReadOnlyException() =>
        new("Archives are read-only; only copying files out of an archive is supported.");
}
