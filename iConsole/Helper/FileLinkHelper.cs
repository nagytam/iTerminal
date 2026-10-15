namespace iConsole.Helper;

/// <summary>
/// Tells a link — a symbolic link or, on Windows, a junction — from the file or folder it points
/// to. Everything that walks a tree needs to: a folder that is a link is one entry of the folder it
/// is in, and what is inside it belongs to somewhere else.
///
/// <para>A hard link is not a link in this sense and cannot be told apart at all: it is a second
/// name for the same file, and neither name is "the" one.</para>
/// </summary>
public static class FileLinkHelper
{
    /// <summary>
    /// Where <paramref name="info"/> points when it is a link, as written in the link (it may be
    /// relative, and it may lead nowhere); null when it is an ordinary file or folder.
    ///
    /// <para>The attributes are asked first. They came with the directory enumeration, so an
    /// ordinary entry costs nothing; only a reparse point is opened to read its target — and one
    /// that is not a link (a OneDrive placeholder, say) has no target and reads as ordinary.</para>
    /// </summary>
    public static string? GetLinkTarget(FileSystemInfo? info)
    {
        try
        {
            if (info is null
                || (int)info.Attributes == -1
                || (info.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return null;
            }

            return info.LinkTarget;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>As <see cref="GetLinkTarget(FileSystemInfo?)"/>, for a path; a trailing separator is ignored.</summary>
    public static string? GetLinkTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var trimmed = Path.TrimEndingDirectorySeparator(path);

            // A FileInfo reads the attributes of whatever is at the path, a folder included, and
            // of the link itself rather than of what it points to.
            return GetLinkTarget(new FileInfo(trimmed));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static bool IsLink(string? path) => GetLinkTarget(path) is not null;

    /// <summary>
    /// Whether the folder link at <paramref name="path"/> leads back to a folder it is inside —
    /// itself or any parent. Walking into one never ends.
    /// </summary>
    public static bool LeadsToAnAncestor(string path)
    {
        try
        {
            var link = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            var target = new DirectoryInfo(link).ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                return false;
            }

            var resolved = Path.TrimEndingDirectorySeparator(target.FullName);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return link.Equals(resolved, comparison)
                || link.StartsWith(resolved + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // A link that cannot be resolved (too many levels — itself a loop) is not walked into.
            return true;
        }
    }
}
