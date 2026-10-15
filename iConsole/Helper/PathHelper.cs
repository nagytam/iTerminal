using System.Runtime.InteropServices;

namespace iConsole.Helper;

public static class PathHelper
{
    private const string WindowsLongPathMark = "\\\\?";
    public static bool IsValidPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // You can remove these checks for the OS, leaving the check only for length, or add new ones with length
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            && path.Length > 260
            && !path.Contains(WindowsLongPathMark, StringComparison.CurrentCulture))
            return false;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && path.Length > 4096)
            return false;

        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return false;

        var clearedPath = path;
        if (clearedPath.StartsWith(WindowsLongPathMark))
            clearedPath = clearedPath.Replace(WindowsLongPathMark, string.Empty);

        if (Path.IsPathRooted(clearedPath))
        {
            var root = Path.GetPathRoot(clearedPath);
            if (clearedPath.IndexOf(root) > 0)
            {
                return false;
            }

            // Relative path
            if (root.Length == 1 && root[0] == Path.DirectorySeparatorChar)
                clearedPath = clearedPath[1..];
            // Absolute path
            else
                clearedPath = clearedPath.Replace(root, string.Empty);
        }

        var segments = clearedPath.Split(Path.DirectorySeparatorChar);
        if (segments.Any(string.IsNullOrWhiteSpace))
            return false;

        return segments.All(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) == -1);
    }
}
