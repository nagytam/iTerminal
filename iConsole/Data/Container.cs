namespace iConsole.Data;

public static class Container
{
    public const string FILE = "file://";
    public const string ZIP_ARCHIVE = "zip://";
    public const string GZ_ARCHIVE = "gz://";
    public const string TAR_GZ_ARCHIVE = "targz://";
    public const string SSH = "ssh://";
    public const string FTP = "ftp://";
    public const string SMB = "smb://";

    /// <summary>WebDAV over HTTP: <c>dav://[user@]host[:port]/path</c>.</summary>
    public const string WEBDAV = "dav://";

    /// <summary>WebDAV over HTTPS: <c>davs://[user@]host[:port]/path</c>.</summary>
    public const string WEBDAV_SECURE = "davs://";
    public const string IPHONE = "iphone://";
    public const string MEMORY_DRIVE = "memdrive://";

    /// <summary>The playing queue, browsed as a panel drive: tracks plus the control folders.</summary>
    public const string MUSIC_PLAYLIST = "musicplaylist://";

    /// <summary>The indexed music library, browsed as a panel drive: artist / album / track.</summary>
    public const string MUSIC_CATALOG = "musiccatalog://";

    /// <summary>Docker containers as a drive: <c>docker://&lt;container&gt;/&lt;path&gt;</c>.</summary>
    public const string DOCKER = "docker://";

    /// <summary>
    /// A git repository's history as a drive: <c>git://&lt;repository&gt;/&lt;revision&gt;/&lt;path&gt;</c>,
    /// the repository and revision segments percent-escaped.
    /// </summary>
    public const string GIT = "git://";

    /// <summary>The results of the last F7 search, listed as a drive.</summary>
    public const string SEARCH_RESULTS = "search://";

    /// <summary>A panel showing a preview of the file under the other panel's cursor.</summary>
    public const string PREVIEW = "preview://";

    /// <summary>
    /// An archive browsed as a drive: <c>archive://&lt;archive&gt;/&lt;path&gt;</c>, the archive's
    /// local path percent-escaped. ZIP, TAR, TAR.GZ, TAR.BZ2, and 7z/RAR/ISO/CAB through 7-Zip.
    /// </summary>
    public const string ARCHIVE = "archive://";

    /// <summary>
    /// Deleted files as a drive: <c>recycle://&lt;name&gt;</c>, one flat list. The name is the
    /// deleted file's own, numbered when two share it.
    /// </summary>
    public const string RECYCLE = "recycle://";

    /// <summary>
    /// Phones and cameras that speak MTP: <c>mtp://&lt;device&gt;/&lt;storage&gt;/&lt;path&gt;</c> — the
    /// root lists the connected devices, a device lists its storages.
    /// </summary>
    public const string MTP = "mtp://";

}
