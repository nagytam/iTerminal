namespace iTerminal.Core.Video;

/// <summary>
/// The pictures in the folder of a picture, in the order the image viewer steps through them: by file name, without case.
/// The listing is made on a worker thread by the viewer, since a large folder is slow to enumerate.
/// </summary>
public static class PictureFolder
{
    /// <summary>The full paths of the picture files beside <paramref name="path"/>, including that file. Empty when the folder cannot be read.</summary>
    public static IReadOnlyList<string> PicturesBeside(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            string? folder = Path.GetDirectoryName(full);
            if (folder is null)
            {
                return [];
            }

            return Directory.EnumerateFiles(folder)
                .Where(ImageFileTypes.IsImageFile)
                .Select(Path.GetFullPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>The index of <paramref name="path"/> in <paramref name="pictures"/>, or -1 when it is not there.</summary>
    public static int IndexOf(IReadOnlyList<string> pictures, string path)
    {
        string full = Path.GetFullPath(path);
        for (int i = 0; i < pictures.Count; i++)
        {
            if (string.Equals(pictures[i], full, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
