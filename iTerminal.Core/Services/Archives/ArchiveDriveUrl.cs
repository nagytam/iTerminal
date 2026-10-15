using iConsole.Data;

namespace iTerminal.Core.Services.Archives;

/// <summary>
/// Builds and reads <c>archive://&lt;archive&gt;/&lt;path&gt;</c> URLs.
///
/// <para>The archive segment is the archive's local path, percent-escaped — it carries a colon and
/// backslashes (<c>C:\dl\photos.zip</c>) that would otherwise split it into levels, the reason the
/// git drive escapes its repository. The path inside is left as it is, so the name a copy takes from
/// <see cref="Url.Name"/> is the entry's real name.</para>
/// </summary>
public static class ArchiveDriveUrl
{
    /// <summary>The archive's top level, or a folder or file inside it.</summary>
    public static Url Build(string archivePath, string? innerPath = null, bool isFolder = true)
    {
        var url = Container.ARCHIVE + Uri.EscapeDataString(archivePath) + "/";
        var path = (innerPath ?? string.Empty).Replace('\\', '/').Trim('/');
        if (path.Length > 0)
        {
            url += path + (isFolder ? "/" : string.Empty);
        }

        return new Url(url);
    }

    /// <summary>
    /// Splits an archive drive URL: the archive's local path (empty at the bare drive root) and the
    /// path inside it, '/'-separated, without leading or trailing slash.
    /// </summary>
    public static (string ArchivePath, string InnerPath) Parse(Url url)
    {
        var segments = url.Path.Split('/');
        if (segments.Length == 0 || segments[0].Length == 0)
        {
            return (string.Empty, string.Empty);
        }

        return (Uri.UnescapeDataString(segments[0]), string.Join('/', segments.Skip(1)).Trim('/'));
    }
}
