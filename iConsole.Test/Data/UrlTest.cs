using iConsole.Data;
using System.Threading;

namespace iConsole.Test.Data;

[TestClass]
public class UrlTest
{
    [TestMethod]
    public void FolderAbsoluteWithOneLevel()
    {
        if (!OperatingSystem.IsWindows()) return;
        var url = new Url(@"c:\windows");
        Assert.AreEqual("file://c:\\windows", url.FullUrl);
        Assert.AreEqual("file://", url.UrlScheme);
        Assert.AreEqual("c:", url.Drive);
        Assert.AreEqual("c:\\windows", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FILE, url.Container);
        Assert.AreEqual(0, url.Length);
    }

    [TestMethod]
    public void FolderAbsoluteWithMultipleLevels()
    {
        if (!OperatingSystem.IsWindows()) return;
        var url = new Url(@"c:\windows\system32");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FILE, url.Container);
    }

    [TestMethod]
    public void FolderRelative()
    {
        if (!OperatingSystem.IsWindows()) return;
        var url = new Url(@"\windows\system32\drivers");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FILE, url.Container);
    }

    [TestMethod]
    public void FileRelative()
    {
        var url = new Url(@"\windows\system32\drivers\etc\hosts");
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FILE, url.Container);
    }

    [TestMethod]
    public void Container()
    {
        var url = new Url(@"..\..\..\..\Test\FileTypes\iTerminal.zip");
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FILE, url.Container);
    }

    [TestMethod]
    public void FolderInContainer()
    {
        var url = new Url(@"..\..\..\..\Test\FileTypes\iTerminal.zip@/");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.Container);
    }

    [TestMethod]
    public void FileInContainer()
    {
        var url = new Url(@"..\Test\FileTypes\iTerminal.zip@/Lena.png");
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.Container);
    }

    [TestMethod]
    public void MemDrive()
    {
        var url = new Url(@"memdrive://");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.MemoryDrive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.MEMORY_DRIVE, url.Container);
    }

    [TestMethod]
    public void MemDriveFile()
    {
        var url = new Url(@"memdrive://file");
        Assert.AreEqual("file", url.Path);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual(ContainerType.MemoryDrive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.MEMORY_DRIVE, url.Container);
    }

    [TestMethod]
    public void MemDriveFolder()
    {
        var url = new Url(@"memdrive://folder/");
        Assert.AreEqual("folder/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.MemoryDrive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.MEMORY_DRIVE, url.Container);
    }

    [TestMethod]
    public void MusicPlaylistRoot()
    {
        var url = new Url(@"musicplaylist://");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.MusicPlaylist, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.MUSIC_PLAYLIST, url.Container);
    }

    [TestMethod]
    public void MusicPlaylistControlFolder()
    {
        var url = new Url(@"musicplaylist://devices/");
        Assert.AreEqual("devices/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("devices", url.Name);
        Assert.AreEqual(@"musicplaylist://", url.ParentUrl.FullUrl);
    }

    [TestMethod]
    public void MusicCatalogRoot()
    {
        var url = new Url(@"musiccatalog://");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.MusicCatalog, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.MUSIC_CATALOG, url.Container);
    }

    [TestMethod]
    public void MusicCatalogAlbumFolder()
    {
        var url = new Url(@"musiccatalog://The%20Beatles/Abbey%20Road/");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.MusicCatalog, url.ContainerType);
        Assert.AreEqual(@"musiccatalog://The%20Beatles/", url.ParentUrl.FullUrl);
    }

    /// <summary>
    /// A catalog track carries an escaped file path, which must not be mistaken for a folder — Enter
    /// on it queues the track rather than trying to descend into it.
    /// </summary>
    [TestMethod]
    public void MusicCatalogTrackIsAFile()
    {
        var url = new Url(@"musiccatalog://C%3A%5CMusic%5Ctrack.mp3");
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual(ContainerType.MusicCatalog, url.ContainerType);
    }

    [TestMethod]
    public void SshFileSystemFile()
    {
        var url = new Url(@"ssh://username@host:/Lena.png");
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual(ContainerType.SshFileSystem, url.ContainerType);
        Assert.AreEqual("host", url.Host);
        Assert.AreEqual("username", url.Username);
        Assert.AreEqual("/Lena.png", url.Path);
        Assert.AreEqual(iConsole.Data.Container.SSH, url.Container);
    }

    [TestMethod]
    public void SshFileSystemFolder()
    {
        var url = new Url(@"ssh://tamas@nagy.ovh:/home/tamas/");
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual(ContainerType.SshFileSystem, url.ContainerType);
        Assert.AreEqual("nagy.ovh", url.Host);
        Assert.AreEqual("tamas", url.Username);
        Assert.AreEqual("/home/tamas/", url.Path);
        Assert.AreEqual("/home/", url.ParentPath);
        Assert.AreEqual("ssh://tamas@nagy.ovh:/home/", url.ParentUrl.FullUrl);
        Assert.AreEqual(iConsole.Data.Container.SSH, url.Container);
    }

    [TestMethod]
    public void FileSystemFolder_ParentUrl_ReturnsActualParent()
    {
        if (!OperatingSystem.IsWindows()) return;
        // A folder URL has a trailing separator; ParentUrl must navigate one level up,
        // not just strip the trailing separator and return the same folder.
        var url = new Url(@"c:\windows\system32\");
        Assert.AreEqual(ContainerType.FileSystem, url.ContainerType);
        var parent = url.ParentUrl;
        Assert.AreEqual(ContainerType.FileSystem, parent.ContainerType);
        Assert.IsFalse(parent.FullUrl.Contains("system32"),
            $"ParentUrl should not contain 'system32', got '{parent.FullUrl}'");
        Assert.IsTrue(parent.FullUrl.Contains("windows"),
            $"ParentUrl should contain 'windows', got '{parent.FullUrl}'");
    }

    [TestMethod]
    public void FileSystemFolder_WithTrailingSlash_ParentUrl_ReturnsActualParent()
    {
        // Same as above but using a path that exists — verifying ContainerType is FileSystem
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "UrlTest_Parent");
        var childDir = System.IO.Path.Combine(tempDir, "child");
        System.IO.Directory.CreateDirectory(childDir);
        try
        {
            var url = new Url(childDir + System.IO.Path.DirectorySeparatorChar);
            var parent = url.ParentUrl;
            Assert.AreEqual(ContainerType.FileSystem, parent.ContainerType);
            Assert.IsTrue(parent.FullUrl.EndsWith("UrlTest_Parent") || parent.FullUrl.EndsWith("UrlTest_Parent\\"),
                $"Expected parent to be 'UrlTest_Parent', got '{parent.FullUrl}'");
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Recursive / multi-embedded archive tests ──────────────────────────────

    [TestMethod]
    public void SingleLevelArchive_ContainerFilePath()
    {
        // The archive file path (before '@') must be exposed via ContainerFilePath
        var url = new Url(@"..\Test\FileTypes\iTerminal.zip@/Lena.png");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.IsFalse(string.IsNullOrEmpty(url.ContainerFilePath),
            "ContainerFilePath should not be empty for a single-level archive URL");
        Assert.IsTrue(url.ContainerFilePath.EndsWith("iTerminal.zip"),
            $"Expected ContainerFilePath to end with 'iTerminal.zip', got '{url.ContainerFilePath}'");
        Assert.IsNull(url.InnerUrl, "InnerUrl should be null for a single-level archive URL");
    }

    [TestMethod]
    public void SingleLevelArchive_RootFolder_ContainerFilePath()
    {
        var url = new Url(@"..\Test\FileTypes\iTerminal.zip@/");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("iTerminal.zip"));
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void TwoLevelRecursiveArchive_FileInInnerZip()
    {
        // outer.zip@/inner.zip@/file.txt  — two levels of embedding
        var url = new Url(@"..\Test\FileTypes\outer.zip@/inner.zip@/file.txt");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);

        // ContainerFilePath should point to the outermost archive
        Assert.IsTrue(url.ContainerFilePath.EndsWith("outer.zip"),
            $"Expected ContainerFilePath to end with 'outer.zip', got '{url.ContainerFilePath}'");

        // InnerUrl should be non-null and represent the inner archive entry
        Assert.IsNotNull(url.InnerUrl, "InnerUrl should be non-null for a two-level recursive archive URL");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.InnerUrl.Container);
    }

    [TestMethod]
    public void TwoLevelRecursiveArchive_FolderInInnerZip()
    {
        // outer.zip@/inner.zip@/  — root folder of the inner ZIP
        // The full URL ends with '/' so PathType is Folder at every level
        var url = new Url(@"..\Test\FileTypes\outer.zip@/inner.zip@/");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType); // trailing '/' → Folder

        Assert.IsNotNull(url.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.Folder, url.InnerUrl.PathType);
    }

    [TestMethod]
    public void ThreeLevelRecursiveArchive()
    {
        // a.zip@/b.zip@/c.zip@/file.txt  — three levels of embedding
        var url = new Url(@"..\Test\FileTypes\a.zip@/b.zip@/c.zip@/file.txt");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("a.zip"),
            $"Expected ContainerFilePath to end with 'a.zip', got '{url.ContainerFilePath}'");

        Assert.IsNotNull(url.InnerUrl, "Level-2 InnerUrl must not be null");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl!.ContainerType);

        Assert.IsNotNull(url.InnerUrl.InnerUrl, "Level-3 InnerUrl must not be null");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.File, url.InnerUrl.InnerUrl.PathType);
        Assert.IsNull(url.InnerUrl.InnerUrl.InnerUrl, "No further nesting expected");
    }

    [TestMethod]
    public void TwoLevelRecursiveArchive_InnerUrl_ContainerFilePath()
    {
        // Verify InnerUrl.ContainerFilePath points to the second archive
        var url = new Url(@"..\Test\FileTypes\outer.zip@/inner.zip@/photo.png");
        Assert.IsNotNull(url.InnerUrl);
        Assert.IsTrue(url.InnerUrl!.ContainerFilePath.EndsWith("inner.zip"),
            $"Expected inner ContainerFilePath to end with 'inner.zip', got '{url.InnerUrl.ContainerFilePath}'");
    }

    [TestMethod]
    public void RecursiveArchive_FullUrl_RoundTrip()
    {
        // FullUrl should preserve the original URL string (with file:// prefix)
        const string raw = @"..\Test\FileTypes\outer.zip@/inner.zip@/file.txt";
        var url = new Url(raw);
        Assert.AreEqual("file://" + raw, url.FullUrl);
    }

    // ── GZ archive tests ──────────────────────────────────────────────────────

    [TestMethod]
    public void GzArchive_FileInside()
    {
        var url = new Url(@"..\Test\FileTypes\data.gz@/data.csv");
        Assert.AreEqual(ContainerType.FileSystemGzArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.GZ_ARCHIVE, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("data.gz"),
            $"Expected ContainerFilePath to end with 'data.gz', got '{url.ContainerFilePath}'");
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void GzArchive_RootFolder()
    {
        var url = new Url(@"..\Test\FileTypes\data.gz@/");
        Assert.AreEqual(ContainerType.FileSystemGzArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.GZ_ARCHIVE, url.Container);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.IsNull(url.InnerUrl);
    }

    // ── TAR.GZ archive tests ──────────────────────────────────────────────────

    [TestMethod]
    public void TarGzArchive_FileInside()
    {
        var url = new Url(@"..\Test\FileTypes\archive.tar.gz@/readme.txt");
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.TAR_GZ_ARCHIVE, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("archive.tar.gz"),
            $"Expected ContainerFilePath to end with 'archive.tar.gz', got '{url.ContainerFilePath}'");
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void TarGzArchive_FolderInside()
    {
        var url = new Url(@"..\Test\FileTypes\archive.tar.gz@/subdir/");
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void TgzArchive_FileInside()
    {
        var url = new Url(@"..\Test\FileTypes\bundle.tgz@/config.json");
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.TAR_GZ_ARCHIVE, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("bundle.tgz"),
            $"Expected ContainerFilePath to end with 'bundle.tgz', got '{url.ContainerFilePath}'");
    }

    [TestMethod]
    public void TgzArchive_FullUrl_RoundTrip()
    {
        const string raw = @"..\Test\FileTypes\bundle.tgz@/config.json";
        var url = new Url(raw);
        Assert.AreEqual("file://" + raw, url.FullUrl);
    }

    // ── Mixed archive combinations ────────────────────────────────────────────

    [TestMethod]
    public void Mixed_ZipContainingTarGz_FileInInnerArchive()
    {
        // outer.zip@/inner.tar.gz@/file.txt
        var url = new Url(@"..\Test\FileTypes\outer.zip@/inner.tar.gz@/file.txt");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.Container);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("outer.zip"));

        Assert.IsNotNull(url.InnerUrl, "InnerUrl must not be null");
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.TAR_GZ_ARCHIVE, url.InnerUrl.Container);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
        Assert.IsTrue(url.InnerUrl.ContainerFilePath.EndsWith("inner.tar.gz"),
            $"Expected inner ContainerFilePath to end with 'inner.tar.gz', got '{url.InnerUrl.ContainerFilePath}'");
    }

    [TestMethod]
    public void Mixed_TarGzContainingZip_FileInInnerArchive()
    {
        // archive.tar.gz@/inner.zip@/photo.png
        var url = new Url(@"..\Test\FileTypes\archive.tar.gz@/inner.zip@/photo.png");
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.TAR_GZ_ARCHIVE, url.Container);

        Assert.IsNotNull(url.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.ZIP_ARCHIVE, url.InnerUrl.Container);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
    }

    [TestMethod]
    public void Mixed_ZipContainingGz_FileInInnerArchive()
    {
        // outer.zip@/data.gz@/data.csv
        var url = new Url(@"..\Test\FileTypes\outer.zip@/data.gz@/data.csv");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);

        Assert.IsNotNull(url.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemGzArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.GZ_ARCHIVE, url.InnerUrl.Container);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
    }

    [TestMethod]
    public void Mixed_GzContainingTarGz_ThreeLevels()
    {
        // a.gz@/b.tar.gz@/c.zip@/file.txt
        var url = new Url(@"..\Test\FileTypes\a.gz@/b.tar.gz@/c.zip@/file.txt");
        Assert.AreEqual(ContainerType.FileSystemGzArchive, url.ContainerType);

        Assert.IsNotNull(url.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.InnerUrl!.ContainerType);

        Assert.IsNotNull(url.InnerUrl.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.File, url.InnerUrl.InnerUrl.PathType);
        Assert.IsNull(url.InnerUrl.InnerUrl.InnerUrl);
    }

    [TestMethod]
    public void Mixed_ZipContainingTarGz_FolderInInner()
    {
        // outer.zip@/inner.tar.gz@/  — root folder of the inner tar.gz
        var url = new Url(@"..\Test\FileTypes\outer.zip@/inner.tar.gz@/");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);

        Assert.IsNotNull(url.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.Folder, url.InnerUrl.PathType);
    }

    // ── FTP tests ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void FtpFileSystemFile()
    {
        var url = new Url(@"ftp://user@ftp.example.com/pub/data.csv");
        Assert.AreEqual(ContainerType.FtpFileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FTP, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("ftp.example.com", url.Host);
        Assert.AreEqual("user", url.Username);
        Assert.AreEqual("/pub/data.csv", url.Path);
    }

    [TestMethod]
    public void FtpFileSystemFolder()
    {
        var url = new Url(@"ftp://user@ftp.example.com/pub/");
        Assert.AreEqual(ContainerType.FtpFileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FTP, url.Container);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("ftp.example.com", url.Host);
        Assert.AreEqual("user", url.Username);
        Assert.AreEqual("/pub/", url.Path);
    }

    [TestMethod]
    public void FtpFileSystemAnonymousFile()
    {
        var url = new Url(@"ftp://ftp.example.com/pub/readme.txt");
        Assert.AreEqual(ContainerType.FtpFileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FTP, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("ftp.example.com", url.Host);
        Assert.AreEqual(string.Empty, url.Username);
        Assert.AreEqual("/pub/readme.txt", url.Path);
    }

    [TestMethod]
    public void FtpFileSystemAnonymousFolder()
    {
        var url = new Url(@"ftp://ftp.example.com/pub/");
        Assert.AreEqual(ContainerType.FtpFileSystem, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("ftp.example.com", url.Host);
    }

    [TestMethod]
    public void FtpFullUrl_RoundTrip()
    {
        var url = new Url(@"ftp://user@ftp.example.com/pub/data.csv");
        Assert.AreEqual("ftp://user@ftp.example.com/pub/data.csv", url.FullUrl);
    }

    [TestMethod]
    public void FtpRootUrl()
    {
        var url = new Url(@"ftp://user@ftp.example.com/pub/data.csv");
        Assert.AreEqual("ftp://user@ftp.example.com/", url.RootUrl.FullUrl);
    }

    // ── FTP embedded archive tests ────────────────────────────────────────────

    [TestMethod]
    public void FtpEmbeddedZip_FileInside()
    {
        var url = new Url(@"ftp://user@ftp.example.com/pub/archive.zip@/readme.txt");
        Assert.AreEqual(ContainerType.FtpFileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FTP, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("ftp.example.com", url.Host);
        Assert.AreEqual("user", url.Username);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("archive.zip"),
            $"Expected ContainerFilePath to end with 'archive.zip', got '{url.ContainerFilePath}'");
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void FtpEmbeddedZip_RootFolder()
    {
        var url = new Url(@"ftp://user@ftp.example.com/pub/archive.zip@/");
        Assert.AreEqual(ContainerType.FtpFileSystemArchive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void FtpEmbeddedTarGz_FileInside()
    {
        var url = new Url(@"ftp://user@ftp.example.com/dist/release.tar.gz@/bin/app");
        Assert.AreEqual(ContainerType.FtpFileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FTP, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("release.tar.gz"),
            $"Expected ContainerFilePath to end with 'release.tar.gz', got '{url.ContainerFilePath}'");
    }

    [TestMethod]
    public void FtpEmbeddedZip_TwoLevels()
    {
        // ftp archive containing a nested zip
        var url = new Url(@"ftp://user@ftp.example.com/pub/outer.zip@/inner.zip@/file.txt");
        Assert.AreEqual(ContainerType.FtpFileSystemArchive, url.ContainerType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("outer.zip"));

        Assert.IsNotNull(url.InnerUrl, "InnerUrl must not be null");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
    }

    [TestMethod]
    public void FtpAnonymousEmbeddedZip_FileInside()
    {
        var url = new Url(@"ftp://ftp.example.com/pub/archive.zip@/readme.txt");
        Assert.AreEqual(ContainerType.FtpFileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.FTP, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("ftp.example.com", url.Host);
        Assert.AreEqual(string.Empty, url.Username);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("archive.zip"));
    }

    // ── SSH embedded archive tests ────────────────────────────────────────────

    [TestMethod]
    public void SshEmbeddedZip_FileInside()
    {
        var url = new Url(@"ssh://tamas@nagy.ovh:/home/tamas/archive.zip@/readme.txt");
        Assert.AreEqual(ContainerType.SshFileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.SSH, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("nagy.ovh", url.Host);
        Assert.AreEqual("tamas", url.Username);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("archive.zip"),
            $"Expected ContainerFilePath to end with 'archive.zip', got '{url.ContainerFilePath}'");
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void SshEmbeddedZip_RootFolder()
    {
        var url = new Url(@"ssh://tamas@nagy.ovh:/home/tamas/archive.zip@/");
        Assert.AreEqual(ContainerType.SshFileSystemArchive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.IsNull(url.InnerUrl);
    }

    [TestMethod]
    public void SshEmbeddedTarGz_FileInside()
    {
        var url = new Url(@"ssh://tamas@nagy.ovh:/opt/release.tar.gz@/bin/app");
        Assert.AreEqual(ContainerType.SshFileSystemArchive, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.SSH, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("release.tar.gz"),
            $"Expected ContainerFilePath to end with 'release.tar.gz', got '{url.ContainerFilePath}'");
    }

    [TestMethod]
    public void SshEmbeddedZip_TwoLevels()
    {
        // SSH path containing a zip that itself contains another zip
        var url = new Url(@"ssh://tamas@nagy.ovh:/home/tamas/outer.zip@/inner.zip@/file.txt");
        Assert.AreEqual(ContainerType.SshFileSystemArchive, url.ContainerType);
        Assert.IsTrue(url.ContainerFilePath.EndsWith("outer.zip"));

        Assert.IsNotNull(url.InnerUrl, "InnerUrl must not be null");
        Assert.AreEqual(ContainerType.FileSystemArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
        Assert.IsTrue(url.InnerUrl.ContainerFilePath.EndsWith("inner.zip"));
    }

    [TestMethod]
    public void SshEmbeddedZip_ThreeLevels()
    {
        // SSH → zip → tar.gz → file
        var url = new Url(@"ssh://tamas@nagy.ovh:/data/outer.zip@/middle.tar.gz@/file.txt");
        Assert.AreEqual(ContainerType.SshFileSystemArchive, url.ContainerType);

        Assert.IsNotNull(url.InnerUrl);
        Assert.AreEqual(ContainerType.FileSystemTarGzArchive, url.InnerUrl!.ContainerType);
        Assert.AreEqual(PathType.File, url.InnerUrl.PathType);
    }

    [TestMethod]
    public void SshEmbeddedZip_FullUrl_RoundTrip()
    {
        const string raw = "ssh://tamas@nagy.ovh:/home/tamas/archive.zip@/readme.txt";
        var url = new Url(raw);
        Assert.AreEqual(raw, url.FullUrl);
    }

    [TestMethod]
    public void FileSystemPath_DirectoryExistsResult_IsCachedForFiveSeconds()
    {
        string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"UrlCache_{Guid.NewGuid():N}");
        string cachedPath = System.IO.Path.Combine(tempRoot, "cached-folder");

        try
        {
            Url initial = new Url(cachedPath);

            System.IO.Directory.CreateDirectory(cachedPath);

            Url cached = new Url(cachedPath);

            Assert.AreEqual(PathType.File, initial.PathType);
            Assert.AreEqual(PathType.File, cached.PathType);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempRoot))
            {
                System.IO.Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FileSystemPath_DirectoryExistsCache_ExpiresAfterFiveSeconds()
    {
        string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"UrlCache_{Guid.NewGuid():N}");
        string cachedPath = System.IO.Path.Combine(tempRoot, "expiring-folder");

        try
        {
            _ = new Url(cachedPath);

            System.IO.Directory.CreateDirectory(cachedPath);
            Thread.Sleep(TimeSpan.FromMilliseconds(5200));

            Url refreshed = new Url(cachedPath);

            Assert.AreEqual(PathType.Folder, refreshed.PathType);
        }
        finally
        {
            if (System.IO.Directory.Exists(tempRoot))
            {
                System.IO.Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public void FileSystemPath_FileInfoResult_IsCachedForFiveSeconds()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"UrlFileInfoCache_{Guid.NewGuid():N}.txt");

        try
        {
            System.IO.File.WriteAllText(tempFile, "a");
            Url initial = new Url(tempFile);

            System.IO.File.WriteAllText(tempFile, "abcdef");
            Url cached = new Url(tempFile);

            Assert.AreEqual(1, initial.Length);
            Assert.AreEqual(1, cached.Length);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    [TestMethod]
    public void FileSystemPath_FileInfoCache_ExpiresAfterFiveSeconds()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"UrlFileInfoCache_{Guid.NewGuid():N}.txt");

        try
        {
            System.IO.File.WriteAllText(tempFile, "a");
            _ = new Url(tempFile);

            System.IO.File.WriteAllText(tempFile, "abcdef");
            Thread.Sleep(TimeSpan.FromMilliseconds(5200));

            Url refreshed = new Url(tempFile);

            Assert.AreEqual(6, refreshed.Length);
        }
        finally
        {
            if (System.IO.File.Exists(tempFile))
            {
                System.IO.File.Delete(tempFile);
            }
        }
    }

    // ── SMB tests ────────────────────────────────────────────────────────────

    [TestMethod]
    public void SmbFileSystemFile()
    {
        var url = new Url(@"smb://user@fileserver/Share/pub/data.csv");
        Assert.AreEqual(ContainerType.SmbFileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.SMB, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("fileserver", url.Host);
        Assert.AreEqual("user", url.Username);
        Assert.AreEqual("/Share/pub/data.csv", url.Path);
        Assert.AreEqual("Share", url.Share);
        Assert.AreEqual("pub/data.csv", url.SharePath);
    }

    [TestMethod]
    public void SmbFileSystemFolder()
    {
        var url = new Url(@"smb://user@fileserver/Share/pub/");
        Assert.AreEqual(ContainerType.SmbFileSystem, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("fileserver", url.Host);
        Assert.AreEqual("user", url.Username);
        Assert.AreEqual("Share", url.Share);
        Assert.AreEqual("pub/", url.SharePath);
    }

    [TestMethod]
    public void SmbFileSystemAnonymousFile()
    {
        var url = new Url(@"smb://fileserver/Public/readme.txt");
        Assert.AreEqual(ContainerType.SmbFileSystem, url.ContainerType);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("fileserver", url.Host);
        Assert.AreEqual(string.Empty, url.Username);
        Assert.AreEqual("Public", url.Share);
        Assert.AreEqual("readme.txt", url.SharePath);
    }

    [TestMethod]
    public void SmbFileSystemShareRoot()
    {
        var url = new Url(@"smb://user@fileserver/Share/");
        Assert.AreEqual("Share", url.Share);
        Assert.AreEqual(string.Empty, url.SharePath);
    }

    [TestMethod]
    public void SmbFullUrl_RoundTrip()
    {
        var url = new Url(@"smb://user@fileserver/Share/pub/data.csv");
        Assert.AreEqual("smb://user@fileserver/Share/pub/data.csv", url.FullUrl);
    }

    [TestMethod]
    public void SmbRootUrl()
    {
        var url = new Url(@"smb://user@fileserver/Share/pub/data.csv");
        Assert.AreEqual("smb://user@fileserver/Share/", url.RootUrl.FullUrl);
    }

    [TestMethod]
    public void SmbParentUrl()
    {
        var url = new Url(@"smb://user@fileserver/Share/pub/data.csv");
        Assert.AreEqual("smb://user@fileserver/Share/pub/", url.ParentUrl.FullUrl);
    }

    [TestMethod]
    public void Share_NonSmbUrl_ReturnsEmpty()
    {
        var url = new Url(@"ssh://tamas@nagy.ovh:/home/tamas/");
        Assert.AreEqual(string.Empty, url.Share);
        Assert.AreEqual(string.Empty, url.SharePath);
    }

    // ── iPhone (AFC) tests ────────────────────────────────────────────────────

    [TestMethod]
    public void IPhoneFileSystemFile()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/DCIM/100APPLE/IMG_0001.JPG");
        Assert.AreEqual(ContainerType.IPhoneFileSystem, url.ContainerType);
        Assert.AreEqual(iConsole.Data.Container.IPHONE, url.Container);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("00008110-001A2B3C", url.Host);
        Assert.AreEqual(string.Empty, url.Username);
        Assert.AreEqual("/DCIM/100APPLE/IMG_0001.JPG", url.Path);
        Assert.AreEqual("IMG_0001.JPG", url.Name);
    }

    [TestMethod]
    public void IPhoneFileSystemFolder()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/DCIM/");
        Assert.AreEqual(ContainerType.IPhoneFileSystem, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("00008110-001A2B3C", url.Host);
        Assert.AreEqual("/DCIM/", url.Path);
    }

    [TestMethod]
    public void IPhoneFileSystemRoot()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/");
        Assert.AreEqual(ContainerType.IPhoneFileSystem, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("00008110-001A2B3C", url.Host);
        Assert.AreEqual("/", url.Path);
    }

    [TestMethod]
    public void IPhoneFullUrl_RoundTrip()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/DCIM/100APPLE/IMG_0001.JPG");
        Assert.AreEqual("iphone://00008110-001A2B3C/DCIM/100APPLE/IMG_0001.JPG", url.FullUrl);
    }

    [TestMethod]
    public void IPhoneRootUrl()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/DCIM/100APPLE/IMG_0001.JPG");
        Assert.AreEqual("iphone://00008110-001A2B3C/", url.RootUrl.FullUrl);
    }

    [TestMethod]
    public void IPhoneParentUrl()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/DCIM/100APPLE/IMG_0001.JPG");
        Assert.AreEqual("iphone://00008110-001A2B3C/DCIM/100APPLE/", url.ParentUrl.FullUrl);
    }

    [TestMethod]
    public void IPhoneParentUrl_OfTopLevelFolder_IsRoot()
    {
        var url = new Url(@"iphone://00008110-001A2B3C/DCIM/");
        Assert.AreEqual("iphone://00008110-001A2B3C/", url.ParentUrl.FullUrl);
    }

    [TestMethod]
    public void ANewFileMustNotExistYet()
    {
        // NewFile had no rule of its own, so an existing file passed as "new" and was truncated
        // when the create-file dialog wrote an empty file over it.
        var existing = Path.Combine(Path.GetTempPath(), "UrlTest_" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(existing, "x");
        try
        {
            Assert.IsFalse(new Url(existing).IsValid(PathExistMode.NewFile));
            Assert.IsTrue(new Url(existing + ".new").IsValid(PathExistMode.NewFile));
            Assert.IsFalse(new Url(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)).IsValid(PathExistMode.NewFile), "a folder is not a new file either");
        }
        finally
        {
            File.Delete(existing);
        }
    }
}
