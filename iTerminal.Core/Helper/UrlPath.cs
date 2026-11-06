using iConsole.Data;

namespace iTerminal.Core.Helper;

/// <summary>
/// Naming a file or folder inside a folder URL, for every drive kind.
///
/// <para>The obvious way — trim the trailing separators, append one and the name — breaks at a
/// drive's root: <c>memdrive://</c> trimmed is <c>memdrive:</c>, and "a folder inside the memory
/// drive" became <c>memdrive:/one/</c>, which no longer parses as the memory drive at all (it was
/// read as a local path relative to the working folder).</para>
/// </summary>
internal static class UrlPath
{
    /// <summary><paramref name="url"/> without trailing separators — but never cut into its <c>scheme://</c>.</summary>
    public static string TrimFolder(string? url)
    {
        url ??= string.Empty;
        var trimmed = url.TrimEnd('/', '\\');
        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        return scheme >= 0 && trimmed.Length < scheme + 3 ? url[..(scheme + 3)] : trimmed;
    }

    /// <summary>True when <paramref name="inner"/> is <paramref name="outer"/> itself or lies inside it (case-insensitive, either separator).</summary>
    public static bool SameOrInside(Url outer, Url inner)
    {
        static string Norm(string? url) => TrimFolder(url).Replace('\\', '/');
        string o = Norm(outer.FullUrl);
        string i = Norm(inner.FullUrl);
        return i.Equals(o, StringComparison.OrdinalIgnoreCase) || i.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The separator a URL is written with: backslashes only on a local Windows path ("file://C:\x" still has the scheme's slashes).</summary>
    public static char SeparatorOf(string? url) => url is not null && url.Contains('\\') ? '\\' : '/';

    /// <summary>
    /// <paramref name="relative"/> (a name, or a path written with either separator) inside
    /// <paramref name="folderUrl"/>, ending in a separator when it is a folder.
    /// </summary>
    public static string Child(string? folderUrl, string? relative, bool isFolder)
    {
        folderUrl ??= string.Empty;
        relative ??= string.Empty;
        var folder = TrimFolder(folderUrl);
        var separator = SeparatorOf(folder);
        var path = relative.Replace(separator == '\\' ? '/' : '\\', separator).Trim(separator);
        var joined = folder.EndsWith("://", StringComparison.Ordinal) ? folder + path : folder + separator + path;
        return isFolder ? joined + separator : joined;
    }
}
