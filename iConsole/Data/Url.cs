using iConsole.Helper;
using Microsoft.Extensions.Caching.Memory;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace iConsole.Data;

[DebuggerDisplay("{_url}")]
public partial class Url
{
    private static readonly TimeSpan DirectoryExistsCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly MemoryCache DirectoryExistsCache = new(new MemoryCacheOptions());
    private static readonly TimeSpan FileInfoCacheDuration = TimeSpan.FromSeconds(5);
    private static readonly MemoryCache FileInfoCache = new(new MemoryCacheOptions());

    private sealed record CachedFileInfo(
        string Name,
        string Extension,
        string FullName,
        DateTime LastWriteTime,
        bool Exists,
        long Length,
        string? LinkTarget = null,
        bool IsHidden = false);

    [GeneratedRegex("ssh://([a-zA-Z0-9]*)@([a-zA-Z0-9.]*):([\\\\/a-zA-Z0-9.\\-_@]*)")]
    private static partial Regex RegexSshUrl();

    // ftp://user@host/path  (with credentials)
    [GeneratedRegex("ftp://([^@/:]*)@([a-zA-Z0-9.\\-]*(?::[0-9]+)?)(\\/.*)")]
    private static partial Regex RegexFtpUrlWithUser();

    // ftp://host/path  (anonymous / no credentials)
    [GeneratedRegex("ftp://([a-zA-Z0-9.\\-]*(?::[0-9]+)?)(\\/.*)")]
    private static partial Regex RegexFtpUrl();

    // smb://user@host/share/path  (with credentials)
    [GeneratedRegex("smb://([a-zA-Z0-9]*)@([a-zA-Z0-9.\\-]*)(\\/[\\\\/a-zA-Z0-9.\\-_@]*)")]
    private static partial Regex RegexSmbUrlWithUser();

    // smb://host/share/path  (anonymous / no credentials)
    [GeneratedRegex("smb://([a-zA-Z0-9.\\-]*)(\\/[\\\\/a-zA-Z0-9.\\-_@]*)")]
    private static partial Regex RegexSmbUrl();

    private readonly string _url;
    private string _urlScheme = null!;
    private string _drive = null!;
    private string? _path;
    private readonly string _date = null!;
    private readonly string _time = null!;
    private readonly string _fullName = null!;
    private readonly string _directoryName = null!;
    private readonly string _host = null!;
    private readonly string _username = null!;
    private readonly string? _container;
    private readonly string? _containerFilePath;
    private readonly Url? _innerUrl;
    public string FullUrl => _url;
    public string UrlScheme => _urlScheme;
    public string Drive => _drive;
    public string Host => _host;
    public string Username => _username;
    public string Path => _path ?? string.Empty;
    public PathType PathType { get; private set; }
    public ContainerType ContainerType { get; private set; } = ContainerType.FileSystem;
    private readonly char _directorySeparatorChar = System.IO.Path.DirectorySeparatorChar;
    public char DirectorySeparatorChar => _directorySeparatorChar;
    public static char DirectorySeparatorSlashChar => '/';
    public const string Folder = "Folder";

    public string _displayName = string.Empty;
    public string DisplayName => _displayName;
    private readonly string _name = string.Empty;
    public string Name => _name;
    private readonly string _extension = string.Empty;
    public string Extension => _extension;

    private readonly string _fileSize = string.Empty;
    public string FileSize => _fileSize;

    /// <summary>
    /// Where this entry points when it is a symbolic link or a junction on the local disk — as
    /// written in the link, so it may be relative and may lead nowhere — otherwise null.
    /// </summary>
    public string? LinkTarget { get; }

    /// <summary>Whether this is a link rather than the file or folder itself (see <see cref="LinkTarget"/>).</summary>
    public bool IsLink => LinkTarget is not null;

    /// <summary>
    /// Whether the local disk marks this entry hidden: the Hidden attribute on Windows, a name that
    /// starts with a dot elsewhere (which is how .NET reports that attribute there). False on every
    /// other drive, which has no such thing to say.
    /// </summary>
    public bool IsHidden { get; }
    public string Date => _date;
    public string Time => _time;
    public string FullName => _fullName;
    public string Directory => _directoryName;
    private readonly long _length = 0;
    public long Length => _length;
    public long PathLength => _fullName.Length;
    public string ParentPath
    {
        get
        {
            if (ContainerType == ContainerType.FileSystem)
            {
                var fullName = _fullName[..^1];
                var target = fullName[..fullName.LastIndexOf(DirectorySeparatorChar)];
                return target;
            }
            else if (ContainerType == ContainerType.SshFileSystem
                     || ContainerType == ContainerType.FtpFileSystem
                     || ContainerType == ContainerType.SmbFileSystem
                     || IsHttpPathDrive
                     || ContainerType == ContainerType.IPhoneFileSystem)
            {
                var fullName = FullName;
                if (fullName.EndsWith('/'))
                {
                    fullName = fullName[..^1];
                }
                var target = FullName[..fullName.LastIndexOf('/')] + "/";
                return target;
            }
            else if (IsSlashSeparatedVirtualDrive)
            {
                var fullName = Path.EndsWith('/') ? Path[..^1] : Path;
                var lastSlash = fullName.LastIndexOf('/');
                return lastSlash >= 0 ? fullName[..(lastSlash + 1)] : string.Empty;
            }
            return string.Empty;
        }
    }

    public Url ParentUrl
    {
        get
        {
            if (ContainerType == ContainerType.FileSystem)
            {
                var fullUrl = FullUrl;
                if (fullUrl.EndsWith(DirectorySeparatorChar))
                {
                    fullUrl = fullUrl[..^1];
                }
                var target = fullUrl[..fullUrl.LastIndexOf(DirectorySeparatorChar)];
                return target;
            }
            else if (ContainerType == ContainerType.SshFileSystem
                     || ContainerType == ContainerType.FtpFileSystem
                     || ContainerType == ContainerType.SmbFileSystem
                     || IsHttpPathDrive
                     || ContainerType == ContainerType.IPhoneFileSystem)
            {
                var fullName = FullUrl;
                if (fullName.EndsWith('/'))
                {
                    fullName = fullName[..^1];
                }
                var target = fullName[..fullName.LastIndexOf('/')] + "/";
                return new Url(target);
            }
            else if (IsSlashSeparatedVirtualDrive)
            {
                return new Url(UrlScheme + ParentPath);
            }
            return this;
        }
    }

    public Url RootUrl
    {
        get
        {
            if (ContainerType == ContainerType.SshFileSystem)
            {
                var target = UrlScheme + Username + "@" + Host + ":/";
                return target;
            }
            else if (ContainerType == ContainerType.FtpFileSystem)
            {
                var userPart = string.IsNullOrEmpty(Username) ? string.Empty : Username + "@";
                var target = Data.Container.FTP + userPart + Host + "/";
                return new Url(target);
            }
            else if (IsHttpPathDrive)
            {
                var userPart = string.IsNullOrEmpty(Username) ? string.Empty : Username + "@";
                return new Url(UrlScheme + userPart + Host + "/");
            }
            else if (ContainerType == ContainerType.SmbFileSystem)
            {
                var userPart = string.IsNullOrEmpty(Username) ? string.Empty : Username + "@";
                var target = Data.Container.SMB + userPart + Host + "/" + Share + "/";
                return new Url(target);
            }
            else if (ContainerType == ContainerType.IPhoneFileSystem)
            {
                var target = Data.Container.IPHONE + Host + "/";
                return new Url(target);
            }
            else if (ContainerType == ContainerType.Docker && Host.Length > 0)
            {
                // The container's own root, not the list of containers: Ctrl+\ inside a container
                // means "/" in that container, the way it means the drive root on a local disk.
                return new Url(Data.Container.DOCKER + Host + "/");
            }
            else if (ContainerType == ContainerType.Archive && Host.Length > 0)
            {
                // The archive's own top level, not the (empty) drive root: archive://<archive>/.
                return new Url(Data.Container.ARCHIVE + Host + "/");
            }
            else if (ContainerType == ContainerType.Mtp)
            {
                // The storage's own root (mtp://<device>/<storage>/), or the device's list of storages.
                var segments = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length >= 2
                    ? new Url(Data.Container.MTP + segments[0] + "/" + segments[1] + "/")
                    : new Url(Data.Container.MTP + (segments.Length == 1 ? segments[0] + "/" : string.Empty));
            }
            else if (ContainerType == ContainerType.Git)
            {
                // The revision's tree root: git://<repository>/<revision>/.
                var segments = Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                return segments.Length >= 2
                    ? new Url(Data.Container.GIT + segments[0] + "/" + segments[1] + "/")
                    : new Url(Data.Container.GIT + (segments.Length == 1 ? segments[0] + "/" : string.Empty));
            }
            else if (ContainerType == ContainerType.FileSystem)
            {
                var target = UrlScheme;
                if (OperatingSystem.IsWindows())
                {
                    target += Drive;
                }
                target += System.IO.Path.DirectorySeparatorChar;
                return new Url(target);
            }
            return this;
        }
    }

    public string Container
    {
        get
        {
            return _container ?? string.Empty;
        }
    }

    /// <summary>SMB share name — the first segment of <see cref="Path"/>. Empty for non-SMB URLs.</summary>
    public string Share
    {
        get
        {
            if (ContainerType != ContainerType.SmbFileSystem)
            {
                return string.Empty;
            }
            var trimmed = Path.TrimStart('/');
            var slashIndex = trimmed.IndexOf('/');
            return slashIndex >= 0 ? trimmed[..slashIndex] : trimmed;
        }
    }

    /// <summary>SMB path relative to the share root (i.e. <see cref="Path"/> with the leading
    /// share-name segment removed). Empty for non-SMB URLs or the share root itself.</summary>
    public string SharePath
    {
        get
        {
            if (ContainerType != ContainerType.SmbFileSystem)
            {
                return string.Empty;
            }
            var trimmed = Path.TrimStart('/');
            var slashIndex = trimmed.IndexOf('/');
            return slashIndex >= 0 ? trimmed[(slashIndex + 1)..] : string.Empty;
        }
    }

    /// <summary>
    /// For archive URLs, the file-system path to the outermost archive file.
    /// </summary>
    public string ContainerFilePath => _containerFilePath ?? string.Empty;

    /// <summary>
    /// For recursive archive URLs (e.g. outer.zip@/inner.zip@/file.txt),
    /// the URL that describes the path inside the outermost archive.
    /// Null for non-archive or single-level archive URLs.
    /// </summary>
    public Url? InnerUrl => _innerUrl;

    public Url(string? url, string? displayName = null)
    {
        _url = AppendUrlPrefix(url);
        SetUrlScheme();
        SetDrive();
        SetPath();
        UpdatePathType();
        if (!string.IsNullOrEmpty(_path))
        {
            if (_url.StartsWith(Data.Container.MEMORY_DRIVE))
            {
                _path = _url[Data.Container.MEMORY_DRIVE.Length..];
                _fullName = _path;
                var trimmed = _path.EndsWith('/') ? _path[..^1] : _path;
                var lastSlash = trimmed.LastIndexOf('/');
                _name = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
                _directoryName = _name;
                _displayName = _name;
                ContainerType = ContainerType.MemoryDrive;
                _container = Data.Container.MEMORY_DRIVE;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.MUSIC_PLAYLIST)
                     || _url.StartsWith(Data.Container.MUSIC_CATALOG))
            {
                // musicplaylist://<section>/ and musiccatalog://<artist>/<album>/<track> — flat,
                // slash-separated virtual paths with no host or credentials, so they parse the same
                // way the memory drive does. Segments are percent-escaped by the content models
                // because artist and album names routinely contain slashes.
                var isPlaylist = _url.StartsWith(Data.Container.MUSIC_PLAYLIST);
                _container = isPlaylist ? Data.Container.MUSIC_PLAYLIST : Data.Container.MUSIC_CATALOG;
                ContainerType = isPlaylist ? ContainerType.MusicPlaylist : ContainerType.MusicCatalog;

                // Note this replaces the "/" that SetPath substitutes for an empty path: the drive
                // root is "" here, the same shape the memory drive uses.
                _path = _url[_container.Length..];
                _fullName = _path;
                var trimmed = _path.EndsWith('/') ? _path[..^1] : _path;
                var lastSlash = trimmed.LastIndexOf('/');
                _name = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
                _directoryName = _name;
                _displayName = _name;
                // Decided from the URL alone: the generic path-type probe would hit the disk for
                // something that never exists there. The bare scheme is the drive root, hence a folder.
                PathType = _path.Length == 0 || _path.EndsWith('/') ? PathType.Folder : PathType.File;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (TryGetVirtualDrive(_url, out var virtualScheme, out var virtualType))
            {
                // docker://, git://, search:// and preview:// — the same flat, slash-separated shape
                // as the music drives, decided from the URL alone. Probing the disk for these is not
                // only wasted work: a docker path such as /etc/ names a real folder on Linux, and the
                // generic branch would read that folder's metadata as if it were the container's.
                _container = virtualScheme;
                ContainerType = virtualType;
                _path = _url[virtualScheme.Length..];
                _fullName = _path;
                var trimmed = _path.EndsWith('/') ? _path[..^1] : _path;
                var lastSlash = trimmed.LastIndexOf('/');
                _name = lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
                _directoryName = _name;
                _displayName = _name;
                PathType = _path.Length == 0 || _path.EndsWith('/') ? PathType.Folder : PathType.File;
                var dot = _name.LastIndexOf('.');
                _extension = PathType == PathType.File && dot > 0 && dot < _name.Length - 1 ? _name[(dot + 1)..] : string.Empty;
                // The first segment — the container on docker://, the repository on git:// — is
                // what the rest of the app calls a host.
                var firstSlash = _path.IndexOf('/');
                _host = firstSlash >= 0 ? _path[..firstSlash] : _path;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.SSH))
            {
                var regex = RegexSshUrl();
                var match = regex.Match(_url);
                if (match.Success)
                {
                    _host = match.Groups[2].Value;
                    _username = match.Groups[1].Value;
                    var fullPath = match.Groups[3].Value; // may contain @

                    var atIndex = fullPath.IndexOf('@');
                    if (atIndex >= 0)
                    {
                        // Embedded archive inside an SSH path
                        // e.g. /home/user/archive.zip@/file.txt
                        _containerFilePath = fullPath[..atIndex];
                        var innerPath = fullPath[(atIndex + 1)..];

                        var innerAtIndex = innerPath.IndexOf('@');
                        if (innerAtIndex >= 0)
                        {
                            var nestedRaw = innerPath.TrimStart('/');
                            _innerUrl = new Url(nestedRaw);
                            var innerArchiveName = nestedRaw[..nestedRaw.IndexOf('@')];
                            var innerFileInfo = GetFileInfoCached(innerArchiveName);
                            _name = innerFileInfo.Name;
                            _extension = (innerFileInfo.Extension.Length > 1) ? innerFileInfo.Extension[1..] : string.Empty;
                        }
                        else
                        {
                            var entryName = innerPath.TrimStart('/');
                            if (string.IsNullOrEmpty(entryName))
                            {
                                var archiveFileInfo = GetFileInfoCached(_containerFilePath!);
                                _name = archiveFileInfo.Name;
                                _extension = (archiveFileInfo.Extension.Length > 1) ? archiveFileInfo.Extension[1..] : string.Empty;
                            }
                            else
                            {
                                var entryFileInfo = GetFileInfoCached(entryName);
                                _name = entryFileInfo.Name;
                                _extension = (entryFileInfo.Extension.Length > 1) ? entryFileInfo.Extension[1..] : string.Empty;
                            }
                        }

                        _path = fullPath;
                        _fullName = innerPath;
                        _directoryName = innerPath.Split(DirectorySeparatorSlashChar).Last();
                        _displayName = _name;
                        PathType = innerPath.EndsWith('/') ? PathType.Folder : PathType.File;
                        ContainerType = ContainerType.SshFileSystemArchive;
                        _container = Data.Container.SSH;
                    }
                    else
                    {
                        _path = fullPath;
                        _directoryName = fullPath;
                        _fullName = fullPath;
                        _displayName = fullPath.Replace("/", string.Empty);
                        PathType = (_url.EndsWith('/')) ? PathType.Folder : PathType.File;
                        ContainerType = ContainerType.SshFileSystem;
                        _container = Data.Container.SSH;
                    }
                }
                else
                {
                    ContainerType = ContainerType.SshFileSystem;
                    _container = Data.Container.SSH;
                }
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.FTP))
            {
                // Try with-user regex first, then anonymous
                var matchWithUser = RegexFtpUrlWithUser().Match(_url);
                string ftpHost, ftpUser, ftpPath;
                if (matchWithUser.Success)
                {
                    ftpUser = matchWithUser.Groups[1].Value;
                    ftpHost = matchWithUser.Groups[2].Value;
                    ftpPath = matchWithUser.Groups[3].Value;
                }
                else
                {
                    var matchAnon = RegexFtpUrl().Match(_url);
                    ftpUser = string.Empty;
                    ftpHost = matchAnon.Success ? matchAnon.Groups[1].Value : string.Empty;
                    ftpPath = matchAnon.Success ? matchAnon.Groups[2].Value : _path ?? "/";
                }

                _host = ftpHost;
                _username = ftpUser;

                var atIndex = ftpPath.IndexOf('@');
                if (atIndex >= 0)
                {
                    // Embedded archive over FTP: /remote/archive.zip@/entry.txt
                    _containerFilePath = ftpPath[..atIndex];
                    var innerPath = ftpPath[(atIndex + 1)..];

                    var innerAtIndex = innerPath.IndexOf('@');
                    if (innerAtIndex >= 0)
                    {
                        var nestedRaw = innerPath.TrimStart('/');
                        _innerUrl = new Url(nestedRaw);
                        var innerArchiveName = nestedRaw[..nestedRaw.IndexOf('@')];
                        var innerFileInfo = GetFileInfoCached(innerArchiveName);
                        _name = innerFileInfo.Name;
                        _extension = (innerFileInfo.Extension.Length > 1) ? innerFileInfo.Extension[1..] : string.Empty;
                    }
                    else
                    {
                        var entryName = innerPath.TrimStart('/');
                        if (string.IsNullOrEmpty(entryName))
                        {
                            var archiveFileInfo = GetFileInfoCached(_containerFilePath!);
                            _name = archiveFileInfo.Name;
                            _extension = (archiveFileInfo.Extension.Length > 1) ? archiveFileInfo.Extension[1..] : string.Empty;
                        }
                        else
                        {
                            var entryFileInfo = GetFileInfoCached(entryName);
                            _name = entryFileInfo.Name;
                            _extension = (entryFileInfo.Extension.Length > 1) ? entryFileInfo.Extension[1..] : string.Empty;
                        }
                    }

                    _path = ftpPath;
                    _fullName = innerPath;
                    _directoryName = innerPath.Split(DirectorySeparatorSlashChar).Last();
                    _displayName = _name;
                    PathType = innerPath.EndsWith('/') ? PathType.Folder : PathType.File;
                    ContainerType = ContainerType.FtpFileSystemArchive;
                    _container = Data.Container.FTP;
                }
                else
                {
                    _path = ftpPath;
                    _fullName = ftpPath;
                    _directoryName = ftpPath;
                    _displayName = ftpPath.TrimEnd('/').Split('/').Last();
                    _name = _displayName;
                    PathType = ftpPath.EndsWith('/') ? PathType.Folder : PathType.File;
                    ContainerType = ContainerType.FtpFileSystem;
                    _container = Data.Container.FTP;
                }
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.SMB))
            {
                // Try with-user regex first, then anonymous. The first path segment is the
                // share name (e.g. smb://user@host/Share/sub/dir/) — no embedded-archive support.
                var matchWithUser = RegexSmbUrlWithUser().Match(_url);
                string smbHost, smbUser, smbPath;
                if (matchWithUser.Success)
                {
                    smbUser = matchWithUser.Groups[1].Value;
                    smbHost = matchWithUser.Groups[2].Value;
                    smbPath = matchWithUser.Groups[3].Value;
                }
                else
                {
                    var matchAnon = RegexSmbUrl().Match(_url);
                    smbUser = string.Empty;
                    smbHost = matchAnon.Success ? matchAnon.Groups[1].Value : string.Empty;
                    smbPath = matchAnon.Success ? matchAnon.Groups[2].Value : _path ?? "/";
                }

                _host = smbHost;
                _username = smbUser;
                _path = smbPath;
                _fullName = smbPath;
                _directoryName = smbPath;
                _displayName = smbPath.TrimEnd('/').Split('/').Last();
                _name = _displayName;
                PathType = smbPath.EndsWith('/') ? PathType.Folder : PathType.File;
                ContainerType = ContainerType.SmbFileSystem;
                _container = Data.Container.SMB;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.WEBDAV) || _url.StartsWith(Data.Container.WEBDAV_SECURE))
            {
                // dav://[user@]host[:port]/path (HTTP) and davs://… (HTTPS). The path is the raw,
                // unescaped one — the service escapes each segment when it builds a request — and the
                // password is never part of a URL.
                var scheme = _url.StartsWith(Data.Container.WEBDAV_SECURE) ? Data.Container.WEBDAV_SECURE : Data.Container.WEBDAV;
                var rest = _url[scheme.Length..];
                var slashIndex = rest.IndexOf('/');
                var authority = slashIndex < 0 ? rest : rest[..slashIndex];
                var davPath = slashIndex < 0 ? "/" : rest[slashIndex..];
                var atIndex = authority.LastIndexOf('@');
                _username = atIndex >= 0 ? authority[..atIndex] : string.Empty;
                _host = atIndex >= 0 ? authority[(atIndex + 1)..] : authority;
                _path = davPath;
                _fullName = davPath;
                _directoryName = davPath;
                _displayName = davPath.TrimEnd('/').Split('/').Last();
                _name = _displayName;
                PathType = davPath.EndsWith('/') ? PathType.Folder : PathType.File;
                var davDot = _name.LastIndexOf('.');
                _extension = PathType == PathType.File && davDot > 0 && davDot < _name.Length - 1 ? _name[(davDot + 1)..] : string.Empty;
                ContainerType = ContainerType.WebDav;
                _container = scheme;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.S3)
                     || _url.StartsWith(Data.Container.ONEDRIVE)
                     || _url.StartsWith(Data.Container.GOOGLE_DRIVE))
            {
                // s3://[accessKeyId@]bucket/key, onedrive://[account]/path and gdrive://[account]/path.
                // The authority is the bucket or the account label (empty lists the buckets / uses the
                // default account); the path is raw, as for WebDAV; no secret is ever part of a URL.
                var (cloudScheme, cloudType) = _url.StartsWith(Data.Container.S3)
                    ? (Data.Container.S3, ContainerType.S3)
                    : _url.StartsWith(Data.Container.ONEDRIVE)
                        ? (Data.Container.ONEDRIVE, ContainerType.OneDrive)
                        : (Data.Container.GOOGLE_DRIVE, ContainerType.GoogleDrive);
                var cloudRest = _url[cloudScheme.Length..];
                var cloudSlash = cloudRest.IndexOf('/');
                var cloudAuthority = cloudSlash < 0 ? cloudRest : cloudRest[..cloudSlash];
                var cloudPath = cloudSlash < 0 ? "/" : cloudRest[cloudSlash..];
                var cloudAt = cloudAuthority.LastIndexOf('@');
                _username = cloudAt >= 0 ? cloudAuthority[..cloudAt] : string.Empty;
                _host = cloudAt >= 0 ? cloudAuthority[(cloudAt + 1)..] : cloudAuthority;
                _path = cloudPath;
                _fullName = cloudPath;
                _directoryName = cloudPath;
                _displayName = cloudPath.TrimEnd('/').Split('/').Last();
                _name = _displayName;
                PathType = cloudPath.EndsWith('/') ? PathType.Folder : PathType.File;
                var cloudDot = _name.LastIndexOf('.');
                _extension = PathType == PathType.File && cloudDot > 0 && cloudDot < _name.Length - 1 ? _name[(cloudDot + 1)..] : string.Empty;
                ContainerType = cloudType;
                _container = cloudScheme;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (_url.StartsWith(Data.Container.IPHONE))
            {
                // iphone://<udid>/<afc-path> — <udid> is the device identifier (treated as the
                // host), and the remainder is an absolute path inside the device's AFC (media)
                // file system. There is no user/share concept, unlike SSH/SMB.
                var rest = _url[Data.Container.IPHONE.Length..];
                var slashIndex = rest.IndexOf('/');
                if (slashIndex < 0)
                {
                    _host = rest;
                    _path = "/";
                }
                else
                {
                    _host = rest[..slashIndex];
                    _path = rest[slashIndex..];
                }
                _username = string.Empty;
                _fullName = _path;
                _directoryName = _path;
                _displayName = _path.TrimEnd('/').Split('/').Last();
                _name = _displayName;
                PathType = _path.EndsWith('/') ? PathType.Folder : PathType.File;
                ContainerType = ContainerType.IPhoneFileSystem;
                _container = Data.Container.IPHONE;
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
            else if (!_path.Contains('@'))
            {
                var fileInfo = GetFileInfoCached(_path);
                _date = fileInfo.LastWriteTime.ToString("yyyy-MM-dd");
                _time = fileInfo.LastWriteTime.ToString("HH:mm");
                _name = fileInfo.Name;
                _extension = (fileInfo.Extension.Length > 1) ? fileInfo.Extension[1..] : string.Empty;
                _fullName = fileInfo.FullName;
                _directoryName = _fullName.Split(DirectorySeparatorChar).Last();
                _container = null;
                ContainerType = ContainerType.FileSystem;
                _container = Data.Container.FILE;
                _length = fileInfo.Exists
                            ? fileInfo.Length
                            : 0;
                _fileSize = (PathType == PathType.File && fileInfo.Exists)
                            ? fileInfo.Length.ToFileSize()
                            : Folder;
                _exists = fileInfo.Exists;
                LinkTarget = fileInfo.LinkTarget;
                // A folder URL keeps its trailing separator, and on Unix a name's leading dot is what makes it
                // hidden, which for "dir/" is the empty name. Take the name without the separator.
                IsHidden = fileInfo.IsHidden || (!OperatingSystem.IsWindows()
                    && System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(_path)).StartsWith('.'));
            }
            else
            {
                // Split at the FIRST '@' to get the archive file path and the inner path
                var atIndex = _path.IndexOf('@');
                _containerFilePath = _path[..atIndex];
                var innerPath = _path[(atIndex + 1)..];

                // Determine name/extension from the innermost entry in the inner path
                // The inner path may itself contain '@' for further recursion (e.g. /inner.zip@/file.txt)
                var innerAtIndex = innerPath.IndexOf('@');
                if (innerAtIndex >= 0)
                {
                    // There are more levels: build a nested Url from the inner path.
                    // The inner path starts with '/' (separator inside ZIP), so we strip that
                    // leading slash to get a bare path that AppendUrlPrefix can process.
                    var nestedRaw = innerPath.TrimStart('/');
                    _innerUrl = new Url(nestedRaw);

                    // Name/extension come from the outermost archive file of the nested URL,
                    // which is the segment up to the first '@' in innerPath.
                    var innerArchiveName = innerPath.TrimStart('/')[ ..innerPath.TrimStart('/').IndexOf('@')];
                    var innerFileInfo = GetFileInfoCached(innerArchiveName);
                    _name = innerFileInfo.Name;
                    _extension = (innerFileInfo.Extension.Length > 1) ? innerFileInfo.Extension[1..] : string.Empty;
                }
                else
                {
                    // Single level: innerPath is something like '/Lena.png' or '/'
                    var entryName = innerPath.TrimStart('/');
                    if (string.IsNullOrEmpty(entryName))
                    {
                        // Root of archive — use archive file name
                        var archiveFileInfo = GetFileInfoCached(_containerFilePath!);
                        _name = archiveFileInfo.Name;
                        _extension = (archiveFileInfo.Extension.Length > 1) ? archiveFileInfo.Extension[1..] : string.Empty;
                    }
                    else
                    {
                        // FileInfo.Name returns "" for a path with a trailing separator
                        // (folder entries inside an archive), so trim it before lookup.
                        var trimmedEntryName = entryName.EndsWith('/') ? entryName[..^1] : entryName;
                        var entryFileInfo = GetFileInfoCached(trimmedEntryName);
                        _name = entryFileInfo.Name;
                        _extension = (entryFileInfo.Extension.Length > 1) ? entryFileInfo.Extension[1..] : string.Empty;
                    }
                }

                _fullName = innerPath;
                _directoryName = _fullName.Split(DirectorySeparatorSlashChar).Last();
                ContainerType = GetArchiveContainerType(_containerFilePath);
                _container = GetArchiveContainerString(_containerFilePath);
                _directorySeparatorChar = DirectorySeparatorSlashChar;
            }
        }
        _displayName = displayName ?? Name;
        }

        /// <summary>
        /// Fast constructor for local filesystem entries obtained from
        /// <see cref="DirectoryInfo.EnumerateFileSystemInfos()"/>.
        /// All metadata fields come from the already-fetched <paramref name="info"/> object so
        /// no extra <c>FileInfo</c> allocation or <c>Directory.Exists</c> syscall is needed.
        /// </summary>
        public Url(FileSystemInfo info)
        {
            var isDir = info is DirectoryInfo;
            var path  = isDir ? info.FullName + System.IO.Path.DirectorySeparatorChar : info.FullName;

            _url          = Data.Container.FILE + path;
            _urlScheme    = Data.Container.FILE;
            _path         = path;
            _fullName     = info.FullName;
            _directoryName= info.FullName.Split(System.IO.Path.DirectorySeparatorChar).Last();
            _name         = info.Name;
            _extension    = (info.Extension.Length > 1) ? info.Extension[1..] : string.Empty;
            _displayName  = info.Name;
            _date         = info.LastWriteTime.ToString("yyyy-MM-dd");
            _time         = info.LastWriteTime.ToString("HH:mm");
            _host         = string.Empty;
            _username     = string.Empty;
            _container    = Data.Container.FILE;
            ContainerType = ContainerType.FileSystem;
            PathType      = isDir ? PathType.Folder : PathType.File;
            _exists       = true;  // entry was returned by enumeration so it exists
            LinkTarget    = iConsole.Helper.FileLinkHelper.GetLinkTarget(info);
            IsHidden      = (info.Attributes & FileAttributes.Hidden) != 0;

            if (!isDir && info is FileInfo fi)
            {
                _length   = fi.Length;
                _fileSize = fi.Length.ToFileSize();
            }
            else
            {
                _length   = 0;
                _fileSize = Url.Folder;
            }

            if (OperatingSystem.IsWindows())
            {
                var indexOfDriveEnd = path.IndexOf(":\\") + 1;
                _drive = indexOfDriveEnd > 0 ? path[..indexOfDriveEnd] : string.Empty;
            }
            else
            {
                _drive = string.Empty;
            }
        }

        private void SetPath()
    {
        var indexOf = _url.IndexOf("://") + 3;
        _path = _url[indexOf..];
        if (string.IsNullOrEmpty(_path))
        {
            _path = "/";
        }
    }

    private void SetUrlScheme()
    {
        var indexOf = _url.IndexOf("://") + 3;
        _urlScheme = _url[0..indexOf];
    }

    private void SetDrive()
    {
        if (OperatingSystem.IsWindows())
        {
            var indexOfSchemeEnd = _url.IndexOf("://") + 3;
            var indexOfDriveEnd = _url.IndexOf(":\\", indexOfSchemeEnd) + 1;
            if (indexOfDriveEnd > 0)
            {
                _drive = _url[indexOfSchemeEnd..indexOfDriveEnd];
            }
        }
        else
        {
            _drive = string.Empty;
        }
    }

    private static string AppendUrlPrefix(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            url = System.IO.Directory.GetCurrentDirectory();
        }
        if (   !url.StartsWith(Data.Container.FILE)
            && !url.StartsWith(Data.Container.MEMORY_DRIVE)
            && !url.StartsWith(Data.Container.MUSIC_PLAYLIST)
            && !url.StartsWith(Data.Container.MUSIC_CATALOG)
            && !TryGetVirtualDrive(url, out _, out _)
            && !url.StartsWith(Data.Container.SSH)
            && !url.StartsWith(Data.Container.FTP)
            && !url.StartsWith(Data.Container.SMB)
            && !url.StartsWith(Data.Container.WEBDAV)
            && !url.StartsWith(Data.Container.WEBDAV_SECURE)
            && !url.StartsWith(Data.Container.S3)
            && !url.StartsWith(Data.Container.ONEDRIVE)
            && !url.StartsWith(Data.Container.GOOGLE_DRIVE)
            && !url.StartsWith(Data.Container.IPHONE)
            && !url.StartsWith(Data.Container.ZIP_ARCHIVE)
            && !url.StartsWith(Data.Container.GZ_ARCHIVE)
            && !url.StartsWith(Data.Container.TAR_GZ_ARCHIVE))
        {
            url = Data.Container.FILE + url;
        }
        return url;
    }

    /// <summary>
    /// The drives that live entirely behind a scheme of their own — no host syntax, no credentials,
    /// nothing on the local disk — and therefore all parse the same way.
    /// </summary>
    private static bool TryGetVirtualDrive(string url, out string scheme, out ContainerType containerType)
    {
        (string Scheme, ContainerType Type)[] drives =
        [
            (Data.Container.DOCKER, ContainerType.Docker),
            (Data.Container.GIT, ContainerType.Git),
            (Data.Container.SEARCH_RESULTS, ContainerType.SearchResults),
            (Data.Container.PREVIEW, ContainerType.Preview),
            (Data.Container.ARCHIVE, ContainerType.Archive),
            (Data.Container.RECYCLE, ContainerType.Recycle),
            (Data.Container.MTP, ContainerType.Mtp),
        ];
        foreach (var (candidate, type) in drives)
        {
            if (url.StartsWith(candidate, StringComparison.Ordinal))
            {
                scheme = candidate;
                containerType = type;
                return true;
            }
        }

        scheme = string.Empty;
        containerType = ContainerType.FileSystem;
        return false;
    }

    /// <summary>Whether this URL belongs to one of the slash-separated virtual drives.</summary>
    private bool IsSlashSeparatedVirtualDrive =>
        ContainerType is ContainerType.MemoryDrive
            or ContainerType.MusicPlaylist
            or ContainerType.MusicCatalog
            or ContainerType.Docker
            or ContainerType.Git
            or ContainerType.SearchResults
            or ContainerType.Preview
            or ContainerType.Archive
            or ContainerType.Recycle
            or ContainerType.Mtp;

    /// <summary>The HTTP-backed drives whose URL is <c>scheme://[user@]authority/raw/path</c>.</summary>
    private bool IsHttpPathDrive =>
        ContainerType is ContainerType.WebDav or ContainerType.S3 or ContainerType.OneDrive or ContainerType.GoogleDrive;

    private void UpdatePathType()
    {
        if (TryGetVirtualDrive(_url, out _, out _))
        {
            // Settled by the virtual-drive branch from the trailing slash; the probes below would
            // ask the local disk about a path that is not on it.
            return;
        }

        if (string.IsNullOrEmpty(_path))
        {
            PathType = PathType.Empty;
        }
        else if ((ContainerType == ContainerType.FileSystemArchive
                    || ContainerType == ContainerType.FileSystemGzArchive
                    || ContainerType == ContainerType.FileSystemTarGzArchive
                    || ContainerType == ContainerType.SshFileSystemArchive
                    || ContainerType == ContainerType.FtpFileSystemArchive)
                && (_path.EndsWith(DirectorySeparatorSlashChar)))
        {
            PathType = PathType.Folder;
        }
        else if ((ContainerType == ContainerType.SshFileSystem
                    || ContainerType == ContainerType.FtpFileSystem
                    || ContainerType == ContainerType.SmbFileSystem
                    || IsHttpPathDrive
                    || ContainerType == ContainerType.IPhoneFileSystem)
                && (_path.EndsWith(DirectorySeparatorSlashChar)))
        {
            PathType = PathType.Folder;
        }
        else if (System.IO.Path.GetDirectoryName(_path) == _path)
        {
            PathType = PathType.Folder;
        }
        else if (_path.EndsWith(DirectorySeparatorChar) || _path.EndsWith('\\'))
        {
            PathType = PathType.Folder;
        }
        else if (_path.EndsWith('/'))
        {
            PathType = PathType.Folder;
        }
        else if (DirectoryExistsCached(_path))
        {
            PathType = PathType.Folder;
        }
        else
        {
            PathType = PathType.File;
        }
    }

    private static bool DirectoryExistsCached(string path)
    {
        if (DirectoryExistsCache.TryGetValue(path, out bool exists))
        {
            return exists;
        }

        exists = System.IO.Directory.Exists(path);
        DirectoryExistsCache.Set(path, exists, DirectoryExistsCacheDuration);
        return exists;
    }

    private static CachedFileInfo GetFileInfoCached(string path)
    {
        if (FileInfoCache.TryGetValue(path, out CachedFileInfo? cachedFileInfo)
            && cachedFileInfo is not null)
        {
            return cachedFileInfo;
        }

        FileInfo fileInfo = new(path);
        bool exists = fileInfo.Exists;
        cachedFileInfo = new CachedFileInfo(
            fileInfo.Name,
            fileInfo.Extension,
            fileInfo.FullName,
            fileInfo.LastWriteTime,
            exists,
            exists ? fileInfo.Length : 0,
            // The attributes were read with the rest, so an ordinary entry costs nothing more:
            // only a reparse point is opened to read where it leads.
            GetLinkTargetQuietly(fileInfo, path),
            (int)fileInfo.Attributes != -1 && (fileInfo.Attributes & FileAttributes.Hidden) != 0);

        FileInfoCache.Set(path, cachedFileInfo, FileInfoCacheDuration);
        return cachedFileInfo;
    }

    private static string? GetLinkTargetQuietly(FileInfo fileInfo, string path)
    {
        try
        {
            return (int)fileInfo.Attributes != -1 && (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0
                ? iConsole.Helper.FileLinkHelper.GetLinkTarget(path)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static ContainerType GetArchiveContainerType(string archiveFilePath)
    {
        var lower = archiveFilePath.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
            return ContainerType.FileSystemTarGzArchive;
        if (lower.EndsWith(".gz"))
            return ContainerType.FileSystemGzArchive;
        return ContainerType.FileSystemArchive;
    }

    private static string GetArchiveContainerString(string archiveFilePath)
    {
        var lower = archiveFilePath.ToLowerInvariant();
        if (lower.EndsWith(".tar.gz") || lower.EndsWith(".tgz"))
            return Data.Container.TAR_GZ_ARCHIVE;
        if (lower.EndsWith(".gz"))
            return Data.Container.GZ_ARCHIVE;
        return Data.Container.ZIP_ARCHIVE;
    }

    public static implicit operator Url(string url)
    {
        var u = new Url(url);
        return u;
    }

    public static implicit operator string(Url url)
    {
        return url?._url ?? string.Empty;
    }

    public Url Combine(string path)
    {
        if (PathType == PathType.Folder)
        {
            var url = _url 
                + (_url.EndsWith(DirectorySeparatorChar) ? string.Empty : DirectorySeparatorChar) 
                + path;
            return new Url(url);
        }
        return this;
    }

    public string Shrink(int length)
    {
        var shrinkedPath = _path.Shrink(length) ?? string.Empty;
        return shrinkedPath;
    }

    private readonly bool _exists = false;
    public bool Exists => _exists;

    public static uint MaxLength { get; private set; } = 127;

    public bool IsValid(PathExistMode pathExistMode)
    {
        var isValid = !string.IsNullOrEmpty(FullName);
        isValid = isValid && PathHelper.IsValidPath(FullName);
        switch (pathExistMode)
        {
            case PathExistMode.NewFolder:
                {
                    isValid = isValid && !System.IO.Directory.Exists(FullName);
                    break;
                }
            case PathExistMode.NewFile:
                {
                    // There was no case for NewFile, so "Create file" accepted the name of a file
                    // that already exists — and creating it truncates what is there.
                    isValid = isValid
                        && !File.Exists(FullName)
                        && !System.IO.Directory.Exists(FullName);
                    break;
                }
            case PathExistMode.FolderOrNewFile:
                {
                    isValid = isValid &&
                        (System.IO.Directory.Exists(FullName) || !File.Exists(FullName));
                    break;
                }
        }
        return isValid;
    }
}
