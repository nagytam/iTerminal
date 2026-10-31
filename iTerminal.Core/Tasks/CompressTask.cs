using iConsole.Localization;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Archives;
using System.Globalization;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Alt+F5: packs files and folders from any drive into an archive on any drive.
///
/// <para>Three stages, all on this task's thread: walk the sources into a flat list of entries
/// (folders through <see cref="ContentModelFolderTaskHelper.ListChildren"/>, so a commit, a
/// container or an SSH folder packs like a local one); have
/// <see cref="IArchiveCompressionService"/> write the archive; and, when the archive lives
/// anywhere but the local disk, bring the existing one down first and put the result back after —
/// the writers need a local, seekable file.</para>
/// </summary>
public sealed class CompressTask(
    IArchiveCompressionService compressionService,
    IReadOnlyList<Url> sources,
    Func<Url, IFileSystem> resolveFileSystem,
    Url archiveUrl,
    CompressionOptions options) : TransferTask
{
    public CompressionResult? Result { get; private set; }

    public override async Task ExecuteAsync()
    {
        var archiveName = FocusedEntry.NameOf(archiveUrl);
        string? workFolder = null;
        try
        {
            Description = string.Join(Environment.NewLine, UiText.Get("Task.Compress", "Compress"), archiveName, UiText.Get("Task.ReadingFiles", "reading the files…"));
            var items = CollectItems(sources, resolveFileSystem, archiveUrl, CancellationToken);
            var files = items.Count(i => !i.IsFolder);
            ReportItems(0, files);
            ReportBytes(0, items.Where(i => !i.IsFolder).Sum(i => Math.Max(0, i.Size)));

            var target = resolveFileSystem(archiveUrl);
            var isLocal = archiveUrl.ContainerType == ContainerType.FileSystem;
            string workPath;
            if (isLocal)
            {
                workPath = archiveUrl.FullName;
            }
            else
            {
                workFolder = Path.Combine(Path.GetTempPath(), "iTerminal-pack-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(workFolder);
                workPath = Path.Combine(workFolder, archiveName);
                if (target.ExistsFile(archiveUrl))
                {
                    Description = string.Join(Environment.NewLine, UiText.Get("Task.Compress", "Compress"), archiveName, UiText.Get("Task.FetchingArchiveToAdd", "fetching the archive to add to…"));
                    await using var download = new FileStream(workPath, FileMode.Create, FileAccess.Write);
                    await target.StreamToAsync(archiveUrl, download, null, CancellationToken);
                }
            }

            var progress = new SynchronousProgress<ArchiveProgress>(p =>
            {
                ReportItems(p.ItemsDone, p.ItemsTotal);
                ReportBytes(p.BytesDone, p.BytesTotal);
                if (p.BytesTotal > 0)
                {
                    OnProgressChanged(Math.Clamp(100L * p.BytesDone / p.BytesTotal, 0, 100));
                }

                Description = string.Join(Environment.NewLine, string.Format(UiText.Get("Task.CompressingInto", "Compressing into {0}"), archiveName.Shrink(50)), p.CurrentEntry.Shrink(60));
            });

            Result = await compressionService.CompressAsync(workPath, items, options, progress, CancellationToken);

            if (!isLocal)
            {
                Description = string.Join(Environment.NewLine, UiText.Get("Task.Compress", "Compress"), archiveName, UiText.Get("Task.Uploading", "uploading…"));
                await using var upload = new FileStream(workPath, FileMode.Open, FileAccess.Read);
                await target.StreamFromAsync(archiveUrl, upload, null, CancellationToken);
            }

            ProgressPercentage = 100;
            Description = Describe(archiveName, Result);
        }
        catch (OperationCanceledException)
        {
            Description = string.Join(Environment.NewLine, UiText.Get("Task.Compress", "Compress"), archiveName, UiText.Get("Task.CompressCancelled", "cancelled — the archive was left as it was"));
        }
        catch (Exception exception)
        {
            Description = string.Format(UiText.Get("Task.CompressFailed", "Compress failed: {0}"), exception.Message);
            Error = exception.Message;
        }
        finally
        {
            if (workFolder is not null)
            {
                try
                {
                    Directory.Delete(workFolder, recursive: true);
                }
                catch
                {
                }
            }
        }
    }

    internal static string Describe(string archiveName, CompressionResult result)
    {
        var text = $"Compressed {result.Added} file(s) into {archiveName}";
        if (result.Kept > 0)
        {
            text += $"{Environment.NewLine}{result.Kept} entr{(result.Kept == 1 ? "y" : "ies")} kept from the archive";
        }
        if (result.Skipped > 0)
        {
            text += $"{Environment.NewLine}{result.Skipped} file(s) already in it were left alone";
        }
        return text;
    }

    /// <summary>
    /// Every file and folder under <paramref name="sources"/>, named as it will be in the archive:
    /// each source under its own name, its contents below it. The archive itself is left out — packing
    /// a folder into an archive inside that folder must not try to put the archive in itself.
    /// </summary>
    internal static List<ArchiveItem> CollectItems(
        IReadOnlyList<Url> sources,
        Func<Url, IFileSystem> resolveFileSystem,
        Url archiveUrl,
        CancellationToken cancellationToken)
    {
        var items = new List<ArchiveItem>();
        var archiveKey = Key(archiveUrl);
        foreach (var source in sources)
        {
            var fileSystem = resolveFileSystem(source);
            Collect(source, FocusedEntry.NameOf(source), fileSystem, items, archiveKey, cancellationToken);
        }

        return items;
    }

    private static void Collect(Url url, string entryName, IFileSystem fileSystem, List<ArchiveItem> items, string archiveKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(Key(url), archiveKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var isLocal = url.ContainerType == ContainerType.FileSystem;
        var localPath = isLocal ? url.FullName.TrimEnd('/', '\\') : null;
        if (url.PathType == PathType.Folder)
        {
            items.Add(new ArchiveItem(entryName + "/", true, 0, Modified(url, localPath, folder: true), localPath));
            foreach (var child in ContentModelFolderTaskHelper.ListChildren(fileSystem, url))
            {
                // A link back to a folder it is inside would be archived for ever; the copy, size and delete walks refuse it too.
                if (ContentModelFolderTaskHelper.IsLinkBackToAncestor(child))
                {
                    continue;
                }

                Collect(child, entryName + "/" + FocusedEntry.NameOf(child), fileSystem, items, archiveKey, cancellationToken);
            }

            return;
        }

        var size = isLocal && File.Exists(localPath) ? new FileInfo(localPath!).Length : url.Length;
        items.Add(new ArchiveItem(
            entryName,
            false,
            size,
            Modified(url, localPath, folder: false),
            localPath,
            isLocal ? null : (destination, ct) => fileSystem.StreamToAsync(url, destination, null, ct)));
    }

    private static DateTime Modified(Url url, string? localPath, bool folder)
    {
        try
        {
            if (localPath is not null)
            {
                return folder ? Directory.GetLastWriteTime(localPath) : File.GetLastWriteTime(localPath);
            }
        }
        catch
        {
        }

        return DateTime.TryParse($"{url.Date} {url.Time}", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : DateTime.Now;
    }

    private static string Key(Url url) => (url.FullUrl ?? string.Empty).TrimEnd('/', '\\');
}
