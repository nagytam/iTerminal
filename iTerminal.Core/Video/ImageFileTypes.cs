namespace iTerminal.Core.Video;

public static class ImageFileTypes
{
    /// <summary>Pictures ffmpeg decodes as a single frame. F3 opens these in the image viewer.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff",
        };

    public static bool IsImageFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}
