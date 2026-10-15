using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Commands;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.Archives;

namespace iTerminal.Core.Tasks;

/// <summary>
/// Shift+F5: unpacks one or more archives, from any drive, into a folder on any drive.
///
/// <para>The extractors work on the local disk — a ZIP is read by seeking, and 7-Zip takes a path —
/// so an archive that lives elsewhere is fetched to a temporary file first, and a target that
/// lives elsewhere is unpacked into a temporary folder and uploaded file by file. The archives are
/// done one after another; one that fails (a wrong password, a damaged file) is listed and the
/// rest still go.</para>
/// </summary>
public sealed class ExtractTask(
    IArchiveExtractionService extractionService,
    IReadOnlyList<Url> archives,
    Func<Url, IFileSystem> resolveFileSystem,
    Url targetFolder,
    ExtractionOptions options,
    bool eachIntoOwnFolder) : TransferTask
{
    /// <summary>Totals over every archive.</summary>
    public ExtractionResult Result { get; private set; } = new(0, 0);

    public IReadOnlyList<string> Failures { get; private set; } = [];

    public override async Task ExecuteAsync()
    {
        var failures = new List<string>();
        int extracted = 0, skipped = 0, unsafePaths = 0;
        ReportItems(0, archives.Count);

        for (var index = 0; index < archives.Count && !CancellationToken.IsCancellationRequested; index++)
        {
            var archive = archives[index];
            var name = FocusedEntry.NameOf(archive);
            try
            {
                var result = await ExtractOneAsync(archive, name, index, CancellationToken);
                extracted += result.Extracted;
                skipped += result.Skipped;
                unsafePaths += result.Unsafe;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                failures.Add($"{name}: {exception.Message}");
            }

            ReportItems(index + 1, archives.Count);
        }

        Result = new ExtractionResult(extracted, skipped, unsafePaths);
        Failures = failures;
        if (CancellationToken.IsCancellationRequested)
        {
            Description = $"Extract cancelled{Environment.NewLine}{extracted} file(s) were already written";
            return;
        }

        ProgressPercentage = 100;
        Description = Describe(Result, archives.Count, failures);
        if (failures.Count > 0)
        {
            Error = failures.Count == 1 ? failures[0] : $"{failures.Count} of {archives.Count} archive(s) failed.";
        }
    }

    internal static string Describe(ExtractionResult result, int archiveCount, IReadOnlyList<string> failures)
    {
        var lines = new List<string>
        {
            $"Extracted {result.Extracted} file(s) from {archiveCount - failures.Count} of {archiveCount} archive(s)",
        };
        if (result.Skipped > 0)
        {
            lines.Add($"{result.Skipped} file(s) already there were left alone");
        }
        if (result.Unsafe > 0)
        {
            lines.Add($"{result.Unsafe} entr{(result.Unsafe == 1 ? "y" : "ies")} refused: the path led outside the target folder");
        }
        lines.AddRange(failures);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The folder an archive goes into: the target, or a folder named after the archive inside it.</summary>
    internal static Url DestinationFor(Url targetFolder, string folderName, bool eachIntoOwnFolder)
    {
        if (!eachIntoOwnFolder)
        {
            return targetFolder;
        }

        return new Url(UrlPath.Child(targetFolder.FullUrl, folderName, isFolder: true));
    }

    private async Task<ExtractionResult> ExtractOneAsync(Url archive, string name, int index, CancellationToken cancellationToken)
    {
        var destination = DestinationFor(targetFolder, extractionService.FolderNameFor(name), eachIntoOwnFolder);
        string? workFolder = null;
        try
        {
            Description = $"Extract {index + 1} of {archives.Count}{Environment.NewLine}{name.Shrink(60)}";

            string archivePath;
            if (archive.ContainerType == ContainerType.FileSystem)
            {
                archivePath = archive.FullName;
            }
            else
            {
                workFolder = Path.Combine(Path.GetTempPath(), "iTerminal-unpack-" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(workFolder);
                archivePath = Path.Combine(workFolder, name);
                Description = $"Extract {index + 1} of {archives.Count}{Environment.NewLine}{name.Shrink(60)}{Environment.NewLine}fetching the archive…";
                await using var download = new FileStream(archivePath, FileMode.Create, FileAccess.Write);
                await resolveFileSystem(archive).StreamToAsync(archive, download, null, cancellationToken);
            }

            var progress = new SynchronousProgress<ArchiveProgress>(p =>
            {
                ReportBytes(p.BytesDone, p.BytesTotal);
                if (p.BytesTotal > 0)
                {
                    // This archive's share of the whole run.
                    var share = 100.0 * p.BytesDone / p.BytesTotal;
                    OnProgressChanged((long)Math.Clamp((index * 100 + share) / archives.Count, 0, 100));
                }

                Description = $"Extract {index + 1} of {archives.Count}: {name.Shrink(50)}{Environment.NewLine}{p.CurrentEntry.Shrink(60)}";
            });

            if (destination.ContainerType == ContainerType.FileSystem)
            {
                return await extractionService.ExtractAsync(archivePath, destination.FullName, options, progress, cancellationToken);
            }

            // A target elsewhere: unpack locally, then upload the tree.
            workFolder ??= Path.Combine(Path.GetTempPath(), "iTerminal-unpack-" + Guid.NewGuid().ToString("N")[..8]);
            var unpacked = Path.Combine(workFolder, "out");
            var result = await extractionService.ExtractAsync(archivePath, unpacked, options with { Overwrite = true }, progress, cancellationToken);
            var uploaded = await UploadAsync(unpacked, destination, cancellationToken);
            return result with { Extracted = uploaded.Uploaded, Skipped = result.Skipped + uploaded.Skipped };
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

    /// <summary>Copies a local folder tree into <paramref name="destination"/> on its drive.</summary>
    private async Task<(int Uploaded, int Skipped)> UploadAsync(string localFolder, Url destination, CancellationToken cancellationToken)
    {
        var target = resolveFileSystem(destination);
        int uploaded = 0, skipped = 0;
        var root = destination.FullUrl;
        if (!UrlPath.TrimFolder(root).EndsWith("://", StringComparison.Ordinal))
        {
            // A drive's root needs no creating (and cannot be created).
            await target.CreateFolderAsync(destination);
        }

        foreach (var folder in Directory.EnumerateDirectories(localFolder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await target.CreateFolderAsync(new Url(UrlPath.Child(root, Path.GetRelativePath(localFolder, folder), isFolder: true)));
        }

        foreach (var file in Directory.EnumerateFiles(localFolder, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = new Url(UrlPath.Child(root, Path.GetRelativePath(localFolder, file), isFolder: false));
            if (!options.Overwrite && target.ExistsFile(url))
            {
                skipped++;
                continue;
            }

            await using var source = new FileStream(file, FileMode.Open, FileAccess.Read);
            await target.StreamFromAsync(url, source, null, cancellationToken);
            uploaded++;
        }

        return (uploaded, skipped);
    }
}
