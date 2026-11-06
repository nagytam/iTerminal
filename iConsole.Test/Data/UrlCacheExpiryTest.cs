using iConsole.Data;

namespace iConsole.Test.Data;

/// <summary>
/// The folder and file caches of <see cref="Url"/> forget what they know after their lifetime. The lifetime is process-wide,
/// so these run one at a time, and they shorten it rather than sleep through the application's five seconds.
/// </summary>
[TestClass]
[DoNotParallelize]
public class UrlCacheExpiryTest
{
    private static readonly TimeSpan TestLifetime = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan PastTheLifetime = TimeSpan.FromMilliseconds(400);

    private TimeSpan _originalLifetime;

    [TestInitialize]
    public void Initialize()
    {
        _originalLifetime = Url.CacheLifetime;
        Url.CacheLifetime = TestLifetime;
    }

    [TestCleanup]
    public void Cleanup()
    {
        Url.CacheLifetime = _originalLifetime;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSystemPath_DirectoryExistsCache_ExpiresAfterItsLifetime()
    {
        string tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"UrlCache_{Guid.NewGuid():N}");
        string cachedPath = System.IO.Path.Combine(tempRoot, "expiring-folder");

        try
        {
            _ = new Url(cachedPath);

            System.IO.Directory.CreateDirectory(cachedPath);
            Thread.Sleep(PastTheLifetime);

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

    [Timeout(60_000)]
    [TestMethod]
    public void FileSystemPath_FileInfoCache_ExpiresAfterItsLifetime()
    {
        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"UrlFileInfoCache_{Guid.NewGuid():N}.txt");

        try
        {
            System.IO.File.WriteAllText(tempFile, "a");
            _ = new Url(tempFile);

            System.IO.File.WriteAllText(tempFile, "abcdef");
            Thread.Sleep(PastTheLifetime);

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
}
