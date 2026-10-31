namespace iTerminal.Core.Video;

public static class ImageFileTypes
{
    /// <summary>Pictures the image viewer draws: the bitmaps ffmpeg decodes as a single frame, SVG drawings and icons, which are rasterized in-process.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".svg", ".ico",
        };

    public static bool IsImageFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}
