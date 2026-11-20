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
    /// The local path a file:// URL names, as the OS writes it (<see cref="Uri.LocalPath"/>: percent-decoded, with the
    /// drive letter or UNC share in place). Anything else, or a URL that does not parse, is returned unchanged.
    /// </summary>
    private static string FilePathOf(string place)
    {
        if (!place.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return place;
        }
        try
        {
            return new Uri(place).LocalPath;
        }
        catch (UriFormatException)
        {
            return place;
        }
    }
}
