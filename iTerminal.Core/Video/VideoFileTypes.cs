namespace iTerminal.Core.Video;

public static class VideoFileTypes
{
    /// <summary>
    /// Container extensions F3 opens in the video viewer. <c>.ts</c> is left out on purpose: in a
    /// source tree it is far more often TypeScript than an MPEG transport stream.
    /// </summary>
    public static readonly IReadOnlySet<string> Extensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".webm", ".wmv", ".flv",
            ".mpg", ".mpeg", ".ogv", ".3gp", ".mts", ".m2ts",
        };

    public static bool IsVideoFile(string path) =>
        !string.IsNullOrEmpty(path) && Extensions.Contains(Path.GetExtension(path));
}
