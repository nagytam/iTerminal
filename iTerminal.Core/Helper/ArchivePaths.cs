namespace iTerminal.Core.Helper;

/// <summary>
/// The "stays inside the folder" check for the entry names of an archive. Every extractor uses it, so an entry
/// that escapes is refused the same way whichever library reads the archive.
/// </summary>
internal static class ArchivePaths
{
    /// <summary>
    /// Where <paramref name="entryName"/> lands under <paramref name="destination"/>, or null when it would land
    /// outside it. Backslashes count as separators, leading slashes are dropped (the entry stays inside), and a
    /// name with a colon (a drive letter or a stream name) is refused. Case is ignored on Windows only.
    /// </summary>
    internal static string? Inside(string destination, string entryName)
    {
        string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string relative = entryName.Replace('\\', '/').TrimStart('/');
        if (relative.Length == 0 || Path.IsPathRooted(relative) || relative.Contains(':'))
        {
            return null;
        }

        string full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : null;
    }
}
