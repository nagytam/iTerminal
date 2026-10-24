using iConsole.Helper;
using System.Globalization;

namespace iTerminal.Core.Services.Properties;

/// <summary>What the disk says about one file or folder.</summary>
/// <param name="Size">A file's length; null for a folder, whose size has to be added up (<see cref="IFilePropertiesService.MeasureAsync"/>).</param>
/// <param name="UnixMode">The permission bits, where the system has them; null on Windows.</param>
/// <param name="LinkTarget">Where it points when it is a symbolic link or a junction.</param>
public sealed record FileProperties(
    string Path,
    string Name,
    bool IsFolder,
    long? Size,
    DateTime Created,
    DateTime Modified,
    DateTime Accessed,
    FileAttributes Attributes,
    UnixFileMode? UnixMode,
    string? LinkTarget);

/// <summary>What a folder holds, everything under it counted.</summary>
public sealed record FolderMeasure(long Files, long Folders, long Bytes, long Unreadable)
{
    public static FolderMeasure Empty { get; } = new(0, 0, 0, 0);
}

/// <summary>
/// Reads and changes what the disk records about a local file or folder. Async by contract where
/// it can take time: measuring a folder walks everything under it.
/// </summary>
public interface IFilePropertiesService
{
    /// <summary>Throws <see cref="IOException"/> when there is nothing at <paramref name="path"/>.</summary>
    FileProperties Read(string path);

    /// <summary>
    /// Adds up everything under the folder at <paramref name="path"/>, reporting as it goes. It
    /// does not go through links: what is behind one is another folder's, and would be counted
    /// twice — or for ever, through a link to a parent.
    /// </summary>
    Task<FolderMeasure> MeasureAsync(string path, IProgress<FolderMeasure>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Sets the attributes (Windows) or the permission bits (elsewhere); a null leaves that one alone.</summary>
    void Apply(string path, FileAttributes? attributes, UnixFileMode? unixMode);

    /// <summary>
    /// Sets the dates; a null leaves that one alone. The modified and accessed dates can be set everywhere; the created date
    /// only on Windows, where it throws <see cref="NotSupportedException"/>. A symbolic link is refused: its dates are those
    /// of what it points to, and setting them would change that file.
    /// </summary>
    void SetTimes(string path, DateTime? created, DateTime? modified, DateTime? accessed);
}

public class FilePropertiesService : IFilePropertiesService
{
    /// <summary>The attributes a user can meaningfully change; the rest describe what the entry is.</summary>
    public const FileAttributes ChangeableAttributes =
        FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(200);

    public FileProperties Read(string path)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        // A FileInfo reads whatever is at the path, a folder included — and the link itself rather
        // than what it leads to, which is the entry the panel listed.
        var info = new FileInfo(trimmed);
        if ((int)info.Attributes == -1)
        {
            throw new IOException($"{trimmed} does not exist.");
        }

        var isFolder = (info.Attributes & FileAttributes.Directory) != 0;
        UnixFileMode? unixMode = null;
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                unixMode = info.UnixFileMode;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new FileProperties(
            trimmed,
            info.Name,
            isFolder,
            isFolder ? null : info.Length,
            info.CreationTime,
            info.LastWriteTime,
            info.LastAccessTime,
            info.Attributes,
            unixMode,
            FileLinkHelper.GetLinkTarget(info));
    }

    public Task<FolderMeasure> MeasureAsync(string path, IProgress<FolderMeasure>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            long files = 0, folders = 0, bytes = 0, unreadable = 0;
            var lastReport = Environment.TickCount64;
            var pending = new Stack<string>();
            pending.Push(path);
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folder = pending.Pop();
                try
                {
                    foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (info is DirectoryInfo)
                        {
                            folders++;
                            if (FileLinkHelper.GetLinkTarget(info) is null)
                            {
                                pending.Push(info.FullName);
                            }
                        }
                        else if (info is FileInfo file)
                        {
                            files++;
                            bytes += file.Length;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // One folder that cannot be read costs that folder, not the total — which then
                    // says it is a lower bound.
                    unreadable++;
                }

                if (progress is not null && Environment.TickCount64 - lastReport >= ProgressInterval.TotalMilliseconds)
                {
                    lastReport = Environment.TickCount64;
                    progress.Report(new FolderMeasure(files, folders, bytes, unreadable));
                }
            }

            return new FolderMeasure(files, folders, bytes, unreadable);
        }, cancellationToken);

    public void Apply(string path, FileAttributes? attributes, UnixFileMode? unixMode)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (attributes is { } wanted)
        {
            // Only the four a user sets: the others (Directory, ReparsePoint, Compressed…) are
            // facts about the entry, and writing them back as they were read can fail or lie.
            var current = File.GetAttributes(trimmed);
            var changed = (current & ~ChangeableAttributes) | (wanted & ChangeableAttributes);
            if (changed != current)
            {
                File.SetAttributes(trimmed, changed);
            }
        }

        if (unixMode is { } mode && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(trimmed, mode);
        }
    }

    public void SetTimes(string path, DateTime? created, DateTime? modified, DateTime? accessed)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        if (created is null && modified is null && accessed is null)
        {
            return;
        }

        if (FileLinkHelper.GetLinkTarget(new FileInfo(trimmed)) is not null)
        {
            throw new IOException("A link's dates are those of what it points to; change the target instead.");
        }

        if (created is not null && !OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("The creation date can only be set on Windows.");
        }

        // Directory and File have the same setters, and each only accepts its own kind of entry.
        if (Directory.Exists(trimmed))
        {
            if (created is { } folderCreated)
            {
                Directory.SetCreationTime(trimmed, folderCreated);
            }

            if (modified is { } folderModified)
            {
                Directory.SetLastWriteTime(trimmed, folderModified);
            }

            if (accessed is { } folderAccessed)
            {
                Directory.SetLastAccessTime(trimmed, folderAccessed);
            }

            return;
        }

        if (created is { } fileCreated)
        {
            File.SetCreationTime(trimmed, fileCreated);
        }

        if (modified is { } fileModified)
        {
            File.SetLastWriteTime(trimmed, fileModified);
        }

        if (accessed is { } fileAccessed)
        {
            File.SetLastAccessTime(trimmed, fileAccessed);
        }
    }

    #region Permission bits as text
    /// <summary>The mode as <c>chmod</c> takes it: three or four octal digits (<c>755</c>, <c>4755</c>).</summary>
    public static string ToOctal(UnixFileMode mode) => Convert.ToString((int)mode, 8).PadLeft(3, '0');

    /// <summary>Parses what <see cref="ToOctal"/> writes; null for anything that is not three or four octal digits.</summary>
    public static UnixFileMode? ParseOctal(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length is < 3 or > 4 || trimmed.Any(c => c is < '0' or > '7'))
        {
            return null;
        }

        return (UnixFileMode)Convert.ToInt32(trimmed, 8);
    }

    /// <summary>The mode as <c>ls -l</c> shows it: <c>rwxr-xr-x</c>.</summary>
    public static string ToSymbolic(UnixFileMode mode)
    {
        Span<char> text = stackalloc char[9];
        (UnixFileMode Bit, char Letter)[] bits =
        [
            (UnixFileMode.UserRead, 'r'), (UnixFileMode.UserWrite, 'w'), (UnixFileMode.UserExecute, 'x'),
            (UnixFileMode.GroupRead, 'r'), (UnixFileMode.GroupWrite, 'w'), (UnixFileMode.GroupExecute, 'x'),
            (UnixFileMode.OtherRead, 'r'), (UnixFileMode.OtherWrite, 'w'), (UnixFileMode.OtherExecute, 'x'),
        ];
        for (var i = 0; i < bits.Length; i++)
        {
            text[i] = (mode & bits[i].Bit) != 0 ? bits[i].Letter : '-';
        }

        return new string(text);
    }
    #endregion

    /// <summary>A size for reading and for checking: "1.4 MB (1,468,006 bytes)".</summary>
    public static string FormatSize(long bytes) =>
        // ToFileSize pads its unit for the panel's size column ("40  B"); here it is prose.
        $"{bytes.ToFileSize().Trim().Replace("  ", " ")} ({bytes.ToString("N0", CultureInfo.InvariantCulture)} byte{(bytes == 1 ? string.Empty : "s")})";

    /// <summary>"1 file", "2 files".</summary>
    public static string Count(long count, string noun) =>
        $"{count.ToString("N0", CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";
}
