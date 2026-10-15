using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using ICSharpCode.SharpZipLib.Zip;
using iConsole.Data;
using iConsole.Helper;
using iConsole.Interfaces;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Processes;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace iTerminal.Core.Services.Archives;

/// <summary>One file or folder in an archive's listing.</summary>
/// <param name="Path">'/'-separated, no leading or trailing slash.</param>
/// <param name="RawPath">The name as the archive (or 7-Zip) spells it, which is what reading it back needs.</param>
public sealed record ArchiveIndexEntry(string Path, string RawPath, bool IsFolder, long Size, DateTime Modified);

/// <summary>See <see cref="ArchiveDriveService"/>.</summary>
public interface IArchiveDriveService : IEntryListingFileSystem
{
    /// <summary>Whether a file of this name can be opened as an <c>archive://</c> drive here.</summary>
    bool CanBrowse(string fileName);

    /// <summary>The rows of a folder inside an archive: folders first, then files, each by name.</summary>
    new Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken);
}

/// <summary>
/// The <c>archive://</c> drive: an archive browsed like a folder, read-only.
///
/// <para>Three readers behind one listing. ZIP is read with SharpZipLib's <see cref="ZipFile"/>,
/// which seeks straight to an entry. The TAR family is one stream from front to back, so a listing
/// reads it through and a file is found by reading up to it. 7z, RAR, ISO and CAB go to the 7-Zip
/// command-line tool: <c>7z l -slt</c> to list, <c>7z x -so</c> to stream one entry to stdout.</para>
///
/// <para>An archive's listing is read once and kept — keyed by the file's size and time, so an
/// archive rewritten by Alt+F5 is read again — because every folder of it is drawn from that one
/// listing, and a TAR.GZ cannot be listed without decompressing all of it. Folders an archive does
/// not store as entries of their own (most ZIPs) are inferred from the paths below them.</para>
/// </summary>
public sealed class ArchiveDriveService(IExternalProcessRunner runner, Func<string?>? locateSevenZip = null) : IArchiveDriveService
{
    private static readonly string[] ZipEndings = [".zip"];
    private static readonly string[] TarEndings = [".tar.gz", ".tar.bz2", ".tgz", ".tbz2", ".tbz", ".tar"];
    private static readonly string[] SevenZipEndings = [".7z", ".rar", ".iso", ".cab"];

    private readonly Lazy<string?> _sevenZip = new(locateSevenZip ?? SevenZipArchiveWriter.FindExecutable);
    private readonly ConcurrentDictionary<string, (long Size, DateTime Time, IReadOnlyList<ArchiveIndexEntry> Index)> _indexes = new(StringComparer.OrdinalIgnoreCase);

    public bool IsReadOnly => true;

    private static string? EndingOf(string fileName, IEnumerable<string> endings) =>
        endings.FirstOrDefault(e => fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether the name is one this drive opens, 7-Zip's formats included whether or not it is
    /// installed — Enter on a .7z without it opens the drive, and its footer says what is missing,
    /// which tells the user more than a key that silently does nothing.
    /// </summary>
    public static bool LooksBrowsable(string fileName) =>
        EndingOf(fileName, ZipEndings) is not null
        || EndingOf(fileName, TarEndings) is not null
        || EndingOf(fileName, SevenZipEndings) is not null;

    public bool CanBrowse(string fileName) =>
        EndingOf(fileName, ZipEndings) is not null
        || EndingOf(fileName, TarEndings) is not null
        || (EndingOf(fileName, SevenZipEndings) is not null && _sevenZip.Value is not null);

    #region Listing
    public async Task<IReadOnlyList<IEntry>> ListAsync(Url url, CancellationToken cancellationToken)
    {
        var (archivePath, innerPath) = ArchiveDriveUrl.Parse(url);
        if (archivePath.Length == 0)
        {
            throw new InvalidOperationException("Open an archive with Enter to browse it.");
        }

        var index = await GetIndexAsync(archivePath, cancellationToken);
        return [.. Children(index, innerPath).Select(entry => ToRow(archivePath, entry))];
    }

    public IEnumerable<Url> ListChildren(Url folderUrl)
    {
        var (archivePath, innerPath) = ArchiveDriveUrl.Parse(folderUrl);
        var index = GetIndexAsync(archivePath, CancellationToken.None).GetAwaiter().GetResult();
        return Children(index, innerPath).Select(entry => ArchiveDriveUrl.Build(archivePath, entry.Path, entry.IsFolder));
    }

    private static IEntry ToRow(string archivePath, ArchiveIndexEntry entry) =>
        new VirtualDriveEntry(
            ArchiveDriveUrl.Build(archivePath, entry.Path, entry.IsFolder),
            entry.Path[(entry.Path.LastIndexOf('/') + 1)..],
            entry.IsFolder ? "Folder" : Math.Max(0, entry.Size).ToFileSize(),
            entry.Modified == default ? null : entry.Modified)
        {
            Size = entry.IsFolder ? 0 : entry.Size,
        };

    /// <summary>The direct children of <paramref name="folder"/> ('' for the top), folders first.</summary>
    internal static IEnumerable<ArchiveIndexEntry> Children(IReadOnlyList<ArchiveIndexEntry> index, string folder)
    {
        var prefix = folder.Length == 0 ? string.Empty : folder + "/";
        return index
            .Where(e => e.Path.Length > prefix.Length
                        && e.Path.StartsWith(prefix, StringComparison.Ordinal)
                        && e.Path.IndexOf('/', prefix.Length) < 0)
            .OrderBy(e => e.IsFolder ? 0 : 1)
            .ThenBy(e => e.Path, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The archive's listing, read once per version of the file.</summary>
    internal async Task<IReadOnlyList<ArchiveIndexEntry>> GetIndexAsync(string archivePath, CancellationToken cancellationToken)
    {
        var info = new FileInfo(archivePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"{Path.GetFileName(archivePath)} is gone.", archivePath);
        }

        if (_indexes.TryGetValue(archivePath, out var cached) && cached.Size == info.Length && cached.Time == info.LastWriteTimeUtc)
        {
            return cached.Index;
        }

        var entries = EndingOf(archivePath, ZipEndings) is not null ? await Task.Run(() => ReadZipIndex(archivePath), cancellationToken)
            : EndingOf(archivePath, TarEndings) is { } ending ? await Task.Run(() => ReadTarIndex(archivePath, ending, cancellationToken), cancellationToken)
            : await ReadSevenZipIndexAsync(archivePath, cancellationToken);
        var index = WithImpliedFolders(entries);
        _indexes[archivePath] = (info.Length, info.LastWriteTimeUtc, index);
        return index;
    }

    /// <summary>Adds the folders the archive only implies (<c>a/b/c.txt</c> implies <c>a</c> and <c>a/b</c>), once each.</summary>
    internal static IReadOnlyList<ArchiveIndexEntry> WithImpliedFolders(IEnumerable<ArchiveIndexEntry> entries)
    {
        var byPath = new Dictionary<string, ArchiveIndexEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var path = Normalize(entry.Path);
            if (path.Length == 0)
            {
                continue;
            }

            byPath[path] = entry with { Path = path };
            for (var cut = path.LastIndexOf('/'); cut > 0; cut = path.LastIndexOf('/', cut - 1))
            {
                var folder = path[..cut];
                byPath.TryAdd(folder, new ArchiveIndexEntry(folder, folder + "/", true, 0, default));
            }
        }

        return [.. byPath.Values];
    }

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/').Trim('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static List<ArchiveIndexEntry> ReadZipIndex(string archivePath)
    {
        using var zip = new ZipFile(archivePath);
        return [.. zip.Cast<ZipEntry>().Select(e => new ArchiveIndexEntry(e.Name, e.Name, e.IsDirectory, e.Size, e.DateTime))];
    }

    private static List<ArchiveIndexEntry> ReadTarIndex(string archivePath, string ending, CancellationToken cancellationToken)
    {
        var entries = new List<ArchiveIndexEntry>();
        using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
        using var tar = OpenTar(file, ending);
        while (tar.GetNextEntry() is { } entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory || entry.TarHeader.TypeFlag is TarHeader.LF_NORMAL or TarHeader.LF_OLDNORM)
            {
                entries.Add(new ArchiveIndexEntry(entry.Name, entry.Name, entry.IsDirectory, entry.Size, entry.ModTime.ToLocalTime()));
            }
        }

        return entries;
    }

    private static TarInputStream OpenTar(Stream file, string ending)
    {
        Stream decompressed = ending switch
        {
            ".tar.gz" or ".tgz" => new GZipInputStream(file) { IsStreamOwner = false },
            ".tar.bz2" or ".tbz2" or ".tbz" => new BZip2InputStream(file) { IsStreamOwner = false },
            _ => file,
        };
        return new TarInputStream(decompressed, Encoding.UTF8) { IsStreamOwner = decompressed != file };
    }

    /// <summary>
    /// <c>7z l -slt -ba</c>: one block of <c>Key = value</c> lines per entry. Stdin is given and closed
    /// at once — an archive with encrypted names makes 7-Zip ask for a password, and left with the
    /// app's own console it would wait there for good.
    /// </summary>
    private async Task<List<ArchiveIndexEntry>> ReadSevenZipIndexAsync(string archivePath, CancellationToken cancellationToken)
    {
        var executable = _sevenZip.Value ?? throw new InvalidOperationException($"{Path.GetExtension(archivePath)} archives need 7-Zip installed.");
        using var output = new MemoryStream();
        var result = await runner.RunToStreamAsync(executable, ["l", "-slt", "-ba", "--", archivePath], output, null, _ => Task.CompletedTask, cancellationToken);
        var text = Encoding.UTF8.GetString(output.ToArray());
        if (!result.Success)
        {
            throw new IOException(DescribeSevenZipFailure(result, text, Path.GetFileName(archivePath)));
        }

        return ParseSevenZipListing(text);
    }

    internal static List<ArchiveIndexEntry> ParseSevenZipListing(string text)
    {
        var entries = new List<ArchiveIndexEntry>();
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        void Flush()
        {
            if (fields.TryGetValue("Path", out var path) && path.Length > 0)
            {
                var isFolder = fields.TryGetValue("Folder", out var folder) ? folder == "+"
                    : fields.TryGetValue("Attributes", out var attributes) && attributes.StartsWith('D');
                _ = long.TryParse(fields.GetValueOrDefault("Size"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size);
                _ = DateTime.TryParse(fields.GetValueOrDefault("Modified"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var modified);
                entries.Add(new ArchiveIndexEntry(path, path, isFolder, size, modified));
            }

            fields.Clear();
        }

        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                Flush();
                continue;
            }

            var separator = trimmed.IndexOf(" = ", StringComparison.Ordinal);
            if (separator > 0)
            {
                var key = trimmed[..separator];

                // A new "Path" starts the next entry even when no blank line came between.
                if (key == "Path" && fields.ContainsKey("Path"))
                {
                    Flush();
                }

                fields[key] = trimmed[(separator + 3)..];
            }
        }

        Flush();
        return entries;
    }

    private static string DescribeSevenZipFailure(ProcessOutput result, string output, string archiveName)
    {
        var text = output + "\n" + result.StandardError;
        return text.Contains("password", StringComparison.OrdinalIgnoreCase)
            ? $"{archiveName} hides its file names behind a password: unpack it with Shift+F5."
            : SevenZipArchiveWriter.DescribeFailure(result, output);
    }
    #endregion

    #region Reading
    public async Task StreamToAsync(Url url, Stream destination, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var (archivePath, innerPath) = ArchiveDriveUrl.Parse(url);
        var index = await GetIndexAsync(archivePath, cancellationToken);
        var entry = index.FirstOrDefault(e => !e.IsFolder && e.Path == innerPath)
            ?? throw new FileNotFoundException($"{innerPath} is not in {Path.GetFileName(archivePath)}.");
        var target = new ProgressStream(destination, progress);

        if (EndingOf(archivePath, ZipEndings) is not null)
        {
            using var zip = new ZipFile(archivePath);
            var zipEntry = zip.GetEntry(entry.RawPath) ?? throw new FileNotFoundException(innerPath);
            if (zipEntry.IsCrypted)
            {
                throw new InvalidOperationException($"{zipEntry.Name} is encrypted: unpack it with Shift+F5 and the password.");
            }

            await using var input = zip.GetInputStream(zipEntry);
            await input.CopyToAsync(target, cancellationToken);
            return;
        }

        if (EndingOf(archivePath, TarEndings) is { } ending)
        {
            await Task.Run(() =>
            {
                using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
                using var tar = OpenTar(file, ending);
                while (tar.GetNextEntry() is { } tarEntry)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (tarEntry.Name == entry.RawPath)
                    {
                        tar.CopyEntryContents(target);
                        return;
                    }
                }

                throw new FileNotFoundException(innerPath);
            }, cancellationToken);
            return;
        }

        var executable = _sevenZip.Value ?? throw new InvalidOperationException("This archive needs 7-Zip installed.");
        var result = await runner.RunToStreamAsync(executable, ["x", "-so", "-bd", "--", archivePath, entry.RawPath], target, null, _ => Task.CompletedTask, cancellationToken);
        if (!result.Success)
        {
            throw new IOException(result.StandardError.Contains("password", StringComparison.OrdinalIgnoreCase)
                ? $"{Path.GetFileName(innerPath)} is encrypted: unpack it with Shift+F5 and the password."
                : SevenZipArchiveWriter.DescribeFailure(result, string.Empty));
        }
    }

    public async Task<IMemoryFile?> ReadFileAsync(Url url)
    {
        using var buffer = new MemoryStream();
        await StreamToAsync(url, buffer, null, CancellationToken.None);
        return new MemoryFile(url.Name, buffer.ToArray());
    }

    public bool ExistsFile(Url url) => Find(url) is { IsFolder: false };

    public bool ExistsFolder(Url url)
    {
        var (archivePath, innerPath) = ArchiveDriveUrl.Parse(url);
        return archivePath.Length > 0 && (innerPath.Length == 0 ? File.Exists(archivePath) : Find(url) is { IsFolder: true });
    }

    private ArchiveIndexEntry? Find(Url url)
    {
        var (archivePath, innerPath) = ArchiveDriveUrl.Parse(url);
        if (archivePath.Length == 0 || !File.Exists(archivePath))
        {
            return null;
        }

        try
        {
            return GetIndexAsync(archivePath, CancellationToken.None).GetAwaiter().GetResult().FirstOrDefault(e => e.Path == innerPath);
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Read-only
    public string GetAvailableDiskSpace() => string.Empty;

    public Task WriteFileAsync(Url url, MemoryStream inputStream) => throw ReadOnly();
    public bool DeleteFile(Url url, bool moveToRecycleBin) => throw ReadOnly();
    public bool DeleteFolder(Url url, bool recursive, bool moveToRecycleBin) => throw ReadOnly();
    public Task MoveAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnly();
    public Task CopyAsync(Url sourceUrl, Url targetUrl, bool overwrite) => throw ReadOnly();
    public Task CreateFolderAsync(Url url) => throw ReadOnly();
    public Task StreamFromAsync(Url url, Stream source, IProgress<long>? progress, CancellationToken cancellationToken) => throw ReadOnly();

    private static NotSupportedException ReadOnly() =>
        new("An archive is read-only here: copy files out with F5, add to it with Alt+F5.");
    #endregion
}
