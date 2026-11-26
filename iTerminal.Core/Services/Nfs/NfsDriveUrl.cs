using iConsole.Data;
#pragma warning disable S1075 // iTerminal's own URLs (not file-system paths) use '/' on every platform.

namespace iTerminal.Core.Services.Nfs;

/// <summary>Where an <c>nfs://</c> URL points: 0 the drive root, 1 a host's exports, 2 an export, 3 or more inside it.</summary>
public readonly record struct NfsLocation(int Depth, string Host, string Export, string Inner);

/// <summary>
/// The <c>nfs://host/&lt;export&gt;/&lt;path&gt;</c> URL: the host is the first segment, the export is the server path
/// percent-escaped as one segment (<c>%2Fsrv%2Fdata</c>), and the rest is the path inside the export. <see cref="Url"/>
/// keeps the escaped segment as written; this is the one place it is unescaped.
/// </summary>
public static class NfsDriveUrl
{
    public static NfsLocation Parse(Url url)
    {
        var segments = url.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return new NfsLocation(
            segments.Length,
            segments.Length >= 1 ? segments[0] : string.Empty,
            segments.Length >= 2 ? Uri.UnescapeDataString(segments[1]) : string.Empty,
            segments.Length >= 3 ? string.Join('/', segments.Skip(2)) : string.Empty);
    }

    /// <summary>The path an export-relative operation takes: <c>.</c> for the export's root.</summary>
    public static string RelativePath(string inner) => inner.Length == 0 ? "." : inner;

    public static string HostUrl(string host) => Container.NFS + host + "/";

    public static string ExportUrl(string host, string export) => Container.NFS + host + "/" + Uri.EscapeDataString(export) + "/";

    public static string ChildUrl(string host, string export, string inner, string name, bool isFolder) =>
        ExportUrl(host, export) + (inner.Length == 0 ? name : inner + "/" + name) + (isFolder ? "/" : string.Empty);

    /// <summary>The header text for a location: <c>host:/srv/data/sub</c>.</summary>
    public static string Describe(NfsLocation location) => location.Depth switch
    {
        0 => string.Empty,
        1 => location.Host,
        2 => location.Host + ":" + location.Export,
        _ => location.Host + ":" + location.Export.TrimEnd('/') + "/" + location.Inner,
    };
}
