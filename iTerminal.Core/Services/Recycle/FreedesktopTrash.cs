using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace iTerminal.Core.Services.Recycle;

/// <summary>Puts local files in the Trash, so a delete can be undone from <c>recycle://</c> or any file manager.</summary>
public interface ITrashService
{
    /// <summary>
    /// Moves <paramref name="path"/> (a file, or a folder with everything in it) to the Trash. False
    /// when it is already inside one, which the caller then deletes for good — there is nowhere
    /// further to move it. Throws <see cref="IOException"/>, in words, when no Trash can be made for
    /// the volume the path is on.
    /// </summary>
    bool TryMoveToTrash(string path);
}

/// <summary>
/// The freedesktop.org Trash specification: <c>files/&lt;name&gt;</c> holds the deleted file and
/// <c>info/&lt;name&gt;.trashinfo</c> a record of where it was and when it was deleted, the form
/// every Linux file manager reads and <see cref="FreedesktopTrashStore"/> lists.
///
/// <para>A file on the same volume as the home Trash (<c>$XDG_DATA_HOME/Trash</c>) goes there, with
/// its absolute path in the record. A file on another volume goes to <c>&lt;mount point&gt;/.Trash-&lt;uid&gt;</c>
/// with a path relative to the mount point: moving it to the home Trash would be a copy across
/// volumes — a slow, interruptible delete that can fail half-done. The mount table is read from
/// <c>/proc/self/mountinfo</c>.</para>
///
/// <para>The name is reserved by creating its <c>.trashinfo</c> exclusively before the file moves,
/// which is how two programs trashing "a.txt" at once do not collide. A failed move removes the
/// record again. <c>directorysizes</c> is not maintained: it is a cache readers rebuild.</para>
/// </summary>
public sealed class FreedesktopTrash : ITrashService
{
    private const string InfoExtension = ".trashinfo";

    private readonly string _homeTrash;
    private readonly Func<IReadOnlyList<string>> _mountPoints;
    private readonly uint _uid;

    /// <summary>The current user's Trash on this system.</summary>
    public FreedesktopTrash()
        : this(DefaultHomeTrash(), ReadMountPoints, CurrentUid())
    {
    }

    /// <param name="homeTrash">The home Trash folder (the one holding <c>files</c> and <c>info</c>).</param>
    /// <param name="mountPoints">The mounted volumes' roots; replaced in tests.</param>
    /// <param name="uid">The user id the per-volume folders are named for.</param>
    public FreedesktopTrash(string homeTrash, Func<IReadOnlyList<string>> mountPoints, uint uid)
    {
        _homeTrash = homeTrash;
        _mountPoints = mountPoints;
        _uid = uid;
    }

    public static string DefaultHomeTrash()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(dataHome, "Trash");
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();

    private static uint CurrentUid()
    {
        try
        {
            return OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() ? GetEffectiveUserId() : 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return 0;
        }
    }

    /// <summary>The mount points in <c>/proc/self/mountinfo</c>; empty where there is no such file.</summary>
    public static IReadOnlyList<string> ReadMountPoints()
    {
        try
        {
            return ParseMountInfo(File.ReadAllLines("/proc/self/mountinfo"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Field 5 of each line is the mount point, with a blank, tab, newline or backslash written as a
    /// backslash and three octal digits (<c>\040</c>).
    /// </summary>
    internal static IReadOnlyList<string> ParseMountInfo(IEnumerable<string> lines)
    {
        var mounts = new List<string>();
        foreach (var line in lines)
        {
            var fields = line.Split(' ');
            if (fields.Length > 4)
            {
                mounts.Add(UnescapeOctal(fields[4]));
            }
        }

        return mounts;
    }

    private static string UnescapeOctal(string text)
    {
        if (!text.Contains('\\'))
        {
            return text;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && IsOctal(text, i + 1))
            {
                builder.Append((char)Convert.ToInt32(text.Substring(i + 1, 3), 8));
                i += 3;
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return builder.ToString();
    }

    private static bool IsOctal(string text, int start) =>
        start + 3 <= text.Length && text.AsSpan(start, 3).IndexOfAnyExceptInRange('0', '7') < 0;

    /// <summary>The longest mount point that contains <paramref name="path"/>, which is its volume's root.</summary>
    internal static string TopDirectoryOf(string path, IReadOnlyList<string> mountPoints)
    {
        var separator = Path.DirectorySeparatorChar;
        var best = separator.ToString();
        foreach (var mount in mountPoints)
        {
            var root = mount.Length > 1 ? mount.TrimEnd(separator) : mount;
            var contains = root == best
                || path == root
                || path.StartsWith(root + separator, StringComparison.Ordinal);
            if (contains && root.Length > best.Length)
            {
                best = root;
            }
        }

        return best;
    }

    /// <summary>
    /// Every Trash that exists: the home one, and a <c>.Trash-&lt;uid&gt;</c> (or <c>.Trash/&lt;uid&gt;</c>)
    /// at the root of each mounted volume. The second field is the volume root whose relative paths
    /// the records hold, or null for the home Trash where they are absolute.
    /// </summary>
    public IReadOnlyList<(string Folder, string? TopDirectory)> Locations()
    {
        var locations = new List<(string, string?)> { (_homeTrash, null) };
        var homeTop = TopDirectoryOf(_homeTrash, _mountPoints());
        foreach (var mount in _mountPoints().Distinct(StringComparer.Ordinal))
        {
            if (mount == homeTop)
            {
                continue;
            }

            foreach (var candidate in new[]
            {
                Path.Combine(mount, ".Trash-" + _uid.ToString(CultureInfo.InvariantCulture)),
                Path.Combine(mount, ".Trash", _uid.ToString(CultureInfo.InvariantCulture)),
            })
            {
                try
                {
                    if (Directory.Exists(Path.Combine(candidate, "info")))
                    {
                        locations.Add((candidate, mount));
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return locations;
    }

    public bool TryMoveToTrash(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var mounts = _mountPoints();
        var top = TopDirectoryOf(full, mounts);
        var homeTop = TopDirectoryOf(_homeTrash, mounts);

        string trashFolder;
        string recordedPath;
        if (top == homeTop)
        {
            trashFolder = _homeTrash;
            recordedPath = full.Replace(Path.DirectorySeparatorChar, '/');
        }
        else
        {
            trashFolder = Path.Combine(top, ".Trash-" + _uid.ToString(CultureInfo.InvariantCulture));
            recordedPath = Path.GetRelativePath(top, full).Replace(Path.DirectorySeparatorChar, '/');
        }

        if (IsInside(full, trashFolder) || IsInside(full, _homeTrash))
        {
            return false;
        }

        string files = Path.Combine(trashFolder, "files");
        string info = Path.Combine(trashFolder, "info");
        try
        {
            CreatePrivateDirectory(trashFolder);
            CreatePrivateDirectory(files);
            CreatePrivateDirectory(info);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Cannot make a Trash on the volume {top}: {exception.Message}. Untick \"Move to Trash\" to delete for good.", exception);
        }

        var isFolder = Directory.Exists(full) && !File.Exists(full);
        var baseName = Path.GetFileName(full);
        var record = BuildRecord(recordedPath, DateTime.Now);

        string name = baseName;
        string recordPath;
        for (var attempt = 2; ; attempt++)
        {
            recordPath = Path.Combine(info, name + InfoExtension);
            if (!File.Exists(Path.Combine(files, name)) && !Directory.Exists(Path.Combine(files, name)) && TryReserve(recordPath, record))
            {
                break;
            }

            name = baseName + "." + attempt.ToString(CultureInfo.InvariantCulture);
        }

        var target = Path.Combine(files, name);
        try
        {
            if (isFolder)
            {
                Directory.Move(full, target);
            }
            else
            {
                File.Move(full, target);
            }
        }
        catch
        {
            try
            {
                File.Delete(recordPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }

            throw;
        }

        return true;
    }

    private static bool IsInside(string path, string folder) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static void CreatePrivateDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
        }
        else
        {
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static bool TryReserve(string recordPath, string content)
    {
        try
        {
            using var stream = new FileStream(recordPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (IOException) when (File.Exists(recordPath))
        {
            return false;
        }
    }

    /// <summary>The text of a <c>.trashinfo</c> file: the path with each segment percent-encoded, and the local time.</summary>
    internal static string BuildRecord(string path, DateTime deleted)
    {
        var encoded = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return "[Trash Info]\n"
            + "Path=" + encoded + "\n"
            + "DeletionDate=" + deleted.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture) + "\n";
    }
}
