using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services;
using System.Diagnostics;
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
        var location = ArchivePath.Replace(Container.ZIP_ARCHIVE, string.Empty);
        var marker = location.IndexOf("@/", StringComparison.Ordinal);
        var archivePath = location[..marker];
        // The folder inside the archive being shown: "" at the top, "sub/" or "sub/deeper/" below it.
        var inside = location[(marker + 2)..];
        Entries.Clear();
        Entries.Add(ParentRow(archivePath, inside));

        // The archive can be gone or unreadable (deleted while the panel shows it): then the panel shows only the way out,
        // rather than the exception from the refresh.
        ZipArchive archive;
        FileStream zipToOpen;
        try
        {
            zipToOpen = new FileStream(archivePath, FileMode.Open, FileAccess.Read);
            archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"{nameof(ContentModelFileSystemArchive)}.{nameof(Refresh)} could not open {archivePath}: {exception}");
            return;
        }

        using (zipToOpen)
        using (archive)
        {
            ListFolder(archive, archivePath, inside);
        }
    }

    private void ListFolder(ZipArchive archive, string archivePath, string inside)
    {

        // Only what is directly in this folder: a file as its own row, and a sub-folder once, however many entries lie under it.
        var folders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zipArchiveEntry in archive.Entries)
        {
            if (!zipArchiveEntry.FullName.StartsWith(inside, StringComparison.Ordinal) || zipArchiveEntry.FullName.Length == inside.Length)
            {
                continue;
            }

            var rest = zipArchiveEntry.FullName[inside.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                Entries.Add(new ArchiveEntry(zipArchiveEntry));
            }
            else if (folders.Add(rest[..slash]))
            {
                var folder = rest[..slash];
                Entries.Add(new ArchiveEntry(folder, Container.ZIP_ARCHIVE + archivePath + "@/" + inside + folder + "/"));
            }
        }
    }

    /// <summary>The ".." row: out of the archive from its top folder, and up one folder inside it from any other.</summary>
    private static ArchiveEntry ParentRow(string archivePath, string inside)
    {
        if (inside.Length == 0)
        {
            return new ArchiveEntry("..", Path.GetDirectoryName(archivePath)!);
        }

        var trimmed = inside.TrimEnd('/');
        var cut = trimmed.LastIndexOf('/');
        var parentInside = cut < 0 ? string.Empty : trimmed[..(cut + 1)];
        return new ArchiveEntry("..", Container.ZIP_ARCHIVE + archivePath + "@/" + parentInside);
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
