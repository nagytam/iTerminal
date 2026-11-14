namespace iTerminal.Core.Video;

public static class ImageFileTypes
{
    /// <summary>Camera raw files. Each carries a JPEG preview the viewer shows; the sensor data is never decoded.</summary>
    public static readonly IReadOnlySet<string> RawExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".3fr", ".arw", ".cr2", ".cr3", ".crw", ".dcr", ".dng", ".erf", ".iiq", ".kdc", ".mos", ".mrw", ".nef", ".nrw",
            ".orf", ".pef", ".raf", ".raw", ".rw2", ".rwl", ".sr2", ".srf", ".srw", ".x3f",
        };

    /// <summary>Adobe Photoshop documents. The viewer shows the flattened composite each one stores.</summary>
    public static readonly IReadOnlySet<string> PhotoshopExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".psd", ".psb" };

    /// <summary>JPEG 2000 pictures and codestreams, decoded by ffmpeg.</summary>
    public static readonly IReadOnlySet<string> Jpeg2000Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jp2", ".j2k", ".j2c", ".jpf", ".jpx" };

    /// <summary>Pictures the image viewer draws: the bitmaps ffmpeg decodes as a single frame, SVG drawings and icons, which are rasterized in-process, Photoshop documents and raw files, which are read in-process too.</summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".svg", ".ico",
        }
        .Concat(PhotoshopExtensions)
        .Concat(Jpeg2000Extensions)
        .Concat(RawExtensions)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsImageFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));

    public static bool IsRawFile(string path) =>
        !string.IsNullOrEmpty(path) && RawExtensions.Contains(Path.GetExtension(path));

    public static bool IsPhotoshopFile(string path) =>
        !string.IsNullOrEmpty(path) && PhotoshopExtensions.Contains(Path.GetExtension(path));
}
