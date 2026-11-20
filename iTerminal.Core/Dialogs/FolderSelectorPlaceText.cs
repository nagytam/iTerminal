namespace iTerminal.Core.Dialogs;

/// <summary>
/// The text of a folder-selector row that names a folder by its path (the Places and Recent pages). It leads with
/// the folder's own name, so Documents and Downloads are told apart without reading the whole path, then the path.
/// A root, and an app drive's URL (memdrive://, music://), has no folder name and is shown as it is. A row's item
/// stays the path, so selecting it does not change.
/// </summary>
internal static class FolderSelectorPlaceText
{
    public static string Label(string place)
    {
        // The Recent page holds file:// URLs; the label is the local path they name.
        var path = FilePathOf(place);
        if (path.Contains("://", StringComparison.Ordinal) || System.IO.Path.GetPathRoot(path) == path)
        {
            return path;
        }
        var name = System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : $"{name}  {path}";
    }

    /// <summary>
    /// The local path a file:// URL names (percent-decoded, and without the slash that sits before a Windows drive
    /// letter in file:///C:/...). Anything else is returned unchanged.
    /// </summary>
    private static string FilePathOf(string place)
    {
        const string prefix = "file://";
        if (!place.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return place;
        }
        var path = Uri.UnescapeDataString(place[prefix.Length..]);
        return System.Text.RegularExpressions.Regex.IsMatch(path, "^/[A-Za-z]:") ? path[1..] : path;
    }
}
