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

    /// <summary>The background read of the folder being shown; a test waits for it, the panel does not.</summary>
    public Task ListingTask { get; private set; } = Task.CompletedTask;

    private CancellationTokenSource? _listingCancellation;

    public override void Refresh(bool forceRefresh = false)
    {
        if (forceRefresh)
        {
            Entries.Clear();
        }
        if (string.IsNullOrEmpty(ArchivePath))
        {
            return;
        }

        // The ".." row costs nothing, so it is shown at once; reading the archive takes as long as the archive is big,
        // and the key handler that asked for the folder must not wait for it.
        var location = ArchivePath;
        Split(location, out var archivePath, out var inside);
        Entries.ReplaceAll([ParentRow(archivePath, inside)]);

        _listingCancellation?.Cancel();
        var cancellation = _listingCancellation = new CancellationTokenSource();
        ListingTask = Task.Run(() =>
        {
            var entries = ReadListing(archivePath, inside);
            // Only the folder still on screen may take the listing: the user may have gone elsewhere while it was read.
            if (cancellation.IsCancellationRequested || location != ArchivePath)
            {
                return;
            }

            ReplaceEntriesAndRestoreFocus(entries, isComplete: true);
        });
    }

    /// <summary>Splits the panel's location into the archive's file and the folder inside it ("" at the top, "sub/" below).</summary>
    private static void Split(string location, out string archivePath, out string inside)
    {
        var path = location.Replace(Container.ZIP_ARCHIVE, string.Empty);
        var marker = path.IndexOf("@/", StringComparison.Ordinal);
        archivePath = path[..marker];
        inside = path[(marker + 2)..];
    }

    /// <summary>
    /// The ".." row and what is directly in the folder: a file as its own row, and a sub-folder once, however many entries lie
    /// under it. An archive that cannot be opened (deleted while shown) gives just the ".." row, rather than an exception.
    /// </summary>
    private static List<IEntry> ReadListing(string archivePath, string inside)
    {
        var entries = new List<IEntry> { ParentRow(archivePath, inside) };
        try
        {
            using var zipToOpen = new FileStream(archivePath, FileMode.Open, FileAccess.Read);
            using var archive = new ZipArchive(zipToOpen, ZipArchiveMode.Read);
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
                    entries.Add(new ArchiveEntry(zipArchiveEntry));
                }
                else if (folders.Add(rest[..slash]))
                {
                    var folder = rest[..slash];
                    entries.Add(new ArchiveEntry(folder, Container.ZIP_ARCHIVE + archivePath + "@/" + inside + folder + "/"));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Debug.WriteLine($"{nameof(ContentModelFileSystemArchive)}.{nameof(ReadListing)} could not read {archivePath}: {exception}");
        }

        return entries;
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
