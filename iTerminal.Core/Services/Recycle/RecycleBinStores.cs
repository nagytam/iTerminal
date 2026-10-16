using System.Globalization;
using System.Text;

namespace iTerminal.Core.Services.Recycle;

/// <summary>Something that was deleted and can still be had back.</summary>
/// <param name="Key">What the store that listed it knows it by.</param>
/// <param name="Name">The name it had.</param>
/// <param name="OriginalPath">Where it was, and where restoring puts it.</param>
/// <param name="Size">Its size as recorded when it was deleted; a folder's is everything in it.</param>
/// <param name="ContentPath">Where the deleted file or folder itself is kept.</param>
public sealed record RecycledItem(
    string Key,
    string Name,
    string OriginalPath,
    DateTime Deleted,
    long Size,
    bool IsFolder,
    string ContentPath);

/// <summary>
/// One operating system's way of keeping deleted files. Everything here is synchronous disk work;
/// what keeps it off the key handler is that only <see cref="RecycleBinService"/> calls it, from
/// background tasks.
/// </summary>
public interface IRecycleBinStore
{
    /// <summary>Empty when the bin can be read; otherwise why this system has none this app can read.</summary>
    string Unavailable { get; }

    IReadOnlyList<RecycledItem> List();

    /// <summary>Puts the item back where it was. Throws <see cref="IOException"/>, in words, when something is already there.</summary>
    void Restore(RecycledItem item);

    /// <summary>Deletes the item for good.</summary>
    void Purge(RecycledItem item);
}

/// <summary>Shared by the stores: both keep the content as an ordinary file or folder and a small record beside it.</summary>
internal static class RecycleBinFiles
{
    /// <summary>
    /// Moves <paramref name="contentPath"/> back to <paramref name="originalPath"/>, never over
    /// something that is there now: what took the deleted file's place is the newer of the two, and
    /// restoring is not a decision to lose it.
    /// </summary>
    public static void MoveBack(string contentPath, string originalPath, bool isFolder)
    {
        if (File.Exists(originalPath) || Directory.Exists(originalPath) || iConsole.Helper.FileLinkHelper.IsLink(originalPath))
        {
            // Short: it is read in a panel's footer. The name, not the path — the path is on the row.
            throw new IOException($"{Path.GetFileName(originalPath)} already exists there");
        }

        var folder = Path.GetDirectoryName(originalPath);
        if (!string.IsNullOrEmpty(folder))
        {
            // The folder it was in may have been deleted too.
            Directory.CreateDirectory(folder);
        }

        if (isFolder)
        {
            Directory.Move(contentPath, originalPath);
        }
        else
        {
            File.Move(contentPath, originalPath);
        }
    }

    public static void Delete(string contentPath)
    {
        // The attributes of the path itself: a deleted link to a folder is removed as the link.
        var info = new FileInfo(contentPath);
        if ((int)info.Attributes == -1)
        {
            return;
        }

        if ((info.Attributes & FileAttributes.Directory) != 0)
        {
            Directory.Delete(contentPath, recursive: (info.Attributes & FileAttributes.ReparsePoint) == 0);
        }
        else
        {
            // A read-only file in the bin is still the user's to delete.
            if ((info.Attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(contentPath, info.Attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(contentPath);
        }
    }

    public static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// The Windows Recycle Bin, read from the files it is made of rather than through the shell.
///
/// <para>Every volume keeps <c>\$Recycle.Bin\&lt;the user's SID&gt;</c>. A deleted file becomes a pair
/// there: <c>$R&lt;random&gt;.ext</c>, the file itself, and <c>$I&lt;random&gt;.ext</c>, a small
/// record of its original path, its size and when it was deleted. Restoring is moving the
/// <c>$R</c> file back — always within one volume, so it is a rename — and removing the record.</para>
///
/// <para>The shell offers the same through COM, where restoring is "invoke the verb called
/// Restore": a name that is translated with the system's language. The file format has been stable
/// since Vista, and reading it makes the store a thing a test can build in a temporary folder.</para>
/// </summary>
public sealed class WindowsRecycleBinStore(Func<IEnumerable<string>> binFolders) : IRecycleBinStore
{
    private const string InfoPrefix = "$I";
    private const string ContentPrefix = "$R";

    /// <summary>The current user's bin on every fixed and removable volume that is ready.</summary>
    public WindowsRecycleBinStore() : this(CurrentUserBinFolders)
    {
    }

    public string Unavailable => string.Empty;

    private static IEnumerable<string> CurrentUserBinFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (string.IsNullOrEmpty(sid))
        {
            yield break;
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            bool usable;
            try
            {
                usable = drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                usable = false;
            }

            if (usable)
            {
                yield return Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
            }
        }
    }

    public IReadOnlyList<RecycledItem> List()
    {
        var items = new List<RecycledItem>();
        foreach (var folder in binFolders())
        {
            IEnumerable<string> records;
            try
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                records = Directory.EnumerateFiles(folder, InfoPrefix + "*").ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A volume whose bin cannot be read costs that volume, not the listing.
                continue;
            }

            foreach (var record in records)
            {
                if (ReadRecord(record) is { } item)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    private static RecycledItem? ReadRecord(string recordPath)
    {
        try
        {
            var name = Path.GetFileName(recordPath);
            var contentPath = Path.Combine(Path.GetDirectoryName(recordPath)!, ContentPrefix + name[InfoPrefix.Length..]);
            var isFolder = Directory.Exists(contentPath);
            if (!isFolder && !File.Exists(contentPath))
            {
                // A record whose file is gone: the shell does not list it either.
                return null;
            }

            if (ParseRecord(File.ReadAllBytes(recordPath)) is not { } parsed)
            {
                return null;
            }

            return new RecycledItem(recordPath, Path.GetFileName(parsed.OriginalPath), parsed.OriginalPath, parsed.Deleted, parsed.Size, isFolder, contentPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a <c>$I</c> record: a version, the size, the deletion time as a FILETIME, then the
    /// original path in UTF-16 — a fixed 260-character field in version 1 (Vista to 8.1), a
    /// character count and that many characters in version 2 (Windows 10 on).
    /// </summary>
    internal static (string OriginalPath, long Size, DateTime Deleted)? ParseRecord(byte[] bytes)
    {
        const int HeaderLength = 24;
        if (bytes.Length <= HeaderLength)
        {
            return null;
        }

        var version = BitConverter.ToInt64(bytes, 0);
        var size = BitConverter.ToInt64(bytes, 8);
        var fileTime = BitConverter.ToInt64(bytes, 16);
        string path;
        if (version == 2)
        {
            if (bytes.Length < HeaderLength + 4)
            {
                return null;
            }

            var characters = BitConverter.ToInt32(bytes, HeaderLength);
            var available = (bytes.Length - HeaderLength - 4) / 2;
            path = Encoding.Unicode.GetString(bytes, HeaderLength + 4, Math.Clamp(characters, 0, available) * 2);
        }
        else if (version == 1)
        {
            path = Encoding.Unicode.GetString(bytes, HeaderLength, (bytes.Length - HeaderLength) / 2 * 2);
        }
        else
        {
            return null;
        }

        var end = path.IndexOf('\0');
        path = end >= 0 ? path[..end] : path;
        if (path.Length == 0)
        {
            return null;
        }

        DateTime deleted;
        try
        {
            deleted = DateTime.FromFileTime(fileTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            deleted = DateTime.MinValue;
        }

        return (path, Math.Max(0, size), deleted);
    }

    /// <summary>Writes a version 2 record — what Windows 10 and later write. Here for the tests, which build a bin of their own.</summary>
    public static byte[] BuildRecord(string originalPath, long size, DateTime deleted)
    {
        var path = Encoding.Unicode.GetBytes(originalPath + '\0');
        var bytes = new byte[28 + path.Length];
        BitConverter.GetBytes(2L).CopyTo(bytes, 0);
        BitConverter.GetBytes(size).CopyTo(bytes, 8);
        BitConverter.GetBytes(deleted.ToFileTime()).CopyTo(bytes, 16);
        BitConverter.GetBytes(originalPath.Length + 1).CopyTo(bytes, 24);
        path.CopyTo(bytes, 28);
        return bytes;
    }

    public void Restore(RecycledItem item)
    {
        RecycleBinFiles.MoveBack(item.ContentPath, item.OriginalPath, item.IsFolder);
        RecycleBinFiles.DeleteQuietly(item.Key);
    }

    public void Purge(RecycledItem item)
    {
        RecycleBinFiles.Delete(item.ContentPath);
        RecycleBinFiles.DeleteQuietly(item.Key);
    }
}

/// <summary>
/// The freedesktop.org trash most Linux desktops use: <c>~/.local/share/Trash/files/&lt;name&gt;</c>
/// is the deleted file and <c>info/&lt;name&gt;.trashinfo</c> a record of its original path and when
/// it was deleted. Reads the home trash and the per-volume <c>.Trash-&lt;uid&gt;</c> folders of the
/// other mounted volumes, where a record holds its path relative to the volume's root; the files
/// themselves are put there by <see cref="FreedesktopTrash"/>.
/// </summary>
public sealed class FreedesktopTrashStore : IRecycleBinStore
{
    private const string InfoExtension = ".trashinfo";

    private readonly Func<IReadOnlyList<(string Folder, string? TopDirectory)>> _locations;

    /// <summary>Reads one trash folder and nothing else.</summary>
    public FreedesktopTrashStore(string trashFolder)
        : this(() => [(trashFolder, null)])
    {
    }

    /// <summary>The current user's trashes on this system.</summary>
    public FreedesktopTrashStore()
        : this(new FreedesktopTrash())
    {
    }

    public FreedesktopTrashStore(FreedesktopTrash trash)
        : this(trash.Locations)
    {
    }

    private FreedesktopTrashStore(Func<IReadOnlyList<(string Folder, string? TopDirectory)>> locations)
    {
        _locations = locations;
    }

    public string Unavailable => string.Empty;

    public IReadOnlyList<RecycledItem> List()
    {
        var items = new List<RecycledItem>();
        foreach (var (folder, topDirectory) in _locations())
        {
            ListFolder(folder, topDirectory, items);
        }

        return items;
    }

    private static void ListFolder(string trashFolder, string? topDirectory, List<RecycledItem> items)
    {
        var infoFolder = Path.Combine(trashFolder, "info");
        var filesFolder = Path.Combine(trashFolder, "files");
        IEnumerable<string> records;
        try
        {
            if (!Directory.Exists(infoFolder))
            {
                return;
            }

            records = Directory.EnumerateFiles(infoFolder, "*" + InfoExtension).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A volume whose trash cannot be read costs that volume, not the listing.
            return;
        }

        foreach (var record in records)
        {
            try
            {
                var contentPath = Path.Combine(filesFolder, Path.GetFileName(record)[..^InfoExtension.Length]);
                var info = new FileInfo(contentPath);
                if ((int)info.Attributes == -1 || ParseRecord(File.ReadAllText(record)) is not { } parsed)
                {
                    continue;
                }

                var originalPath = parsed.OriginalPath;
                if (!Path.IsPathRooted(originalPath) && topDirectory is not null)
                {
                    originalPath = Path.Combine(topDirectory, originalPath.Replace('/', Path.DirectorySeparatorChar));
                }

                var isFolder = (info.Attributes & FileAttributes.Directory) != 0;
                items.Add(new RecycledItem(
                    record,
                    Path.GetFileName(Path.TrimEndingDirectorySeparator(originalPath)),
                    originalPath,
                    parsed.Deleted,
                    isFolder ? 0 : info.Length,
                    isFolder,
                    contentPath));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
            }
        }
    }

    /// <summary>Reads a <c>.trashinfo</c> record: <c>Path=</c>, percent-encoded, and <c>DeletionDate=</c>, local time.</summary>
    internal static (string OriginalPath, DateTime Deleted)? ParseRecord(string text)
    {
        string? path = null;
        var deleted = DateTime.MinValue;
        foreach (var line in text.Split('\n').Select(l => l.Trim()))
        {
            if (line.StartsWith("Path=", StringComparison.Ordinal))
            {
                path = Uri.UnescapeDataString(line["Path=".Length..]);
            }
            else if (line.StartsWith("DeletionDate=", StringComparison.Ordinal))
            {
                DateTime.TryParse(line["DeletionDate=".Length..], CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out deleted);
            }
        }

        return string.IsNullOrEmpty(path) ? null : (path, deleted);
    }

    public void Restore(RecycledItem item)
    {
        RecycleBinFiles.MoveBack(item.ContentPath, item.OriginalPath, item.IsFolder);
        RecycleBinFiles.DeleteQuietly(item.Key);
    }

    public void Purge(RecycledItem item)
    {
        RecycleBinFiles.Delete(item.ContentPath);
        RecycleBinFiles.DeleteQuietly(item.Key);
    }
}

/// <summary>A system whose deleted files this app cannot read (macOS keeps no record of where a file in the Trash came from).</summary>
public sealed class UnavailableRecycleBinStore(string reason) : IRecycleBinStore
{
    public string Unavailable => reason;

    public IReadOnlyList<RecycledItem> List() => [];

    public void Restore(RecycledItem item) => throw new NotSupportedException(reason);

    public void Purge(RecycledItem item) => throw new NotSupportedException(reason);
}
