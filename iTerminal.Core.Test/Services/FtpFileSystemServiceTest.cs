using FluentFTP;
using FluentFTP.Exceptions;
using iConsole.Data;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class FtpFileSystemServiceTest
{
    private readonly Mock<IFtpClient> _client = new();
    private string? _host;
    private int _port;
    private string? _user;
    private string? _password;
    private readonly List<bool> _secureRequests = [];

    private FtpFileSystemService CreateService()
    {
        _client.SetupGet(c => c.IsConnected).Returns(true);
        return new FtpFileSystemService((host, port, user, password, secure) =>
        {
            _host = host;
            _port = port;
            _user = user;
            _password = password;
            _secureRequests.Add(secure);
            return _client.Object;
        });
    }

    [TestMethod]
    public void Connect_AsksForAnEncryptedClientFirst()
    {
        _client.SetupGet(c => c.IsConnected).Returns(false);
        using var service = new FtpFileSystemService((_, _, _, _, secure) =>
        {
            _secureRequests.Add(secure);
            return _client.Object;
        });

        service.Connect("ftp.example.org", "alice", "pw");

        CollectionAssert.AreEqual(new[] { true }, _secureRequests);
    }

    [TestMethod]
    public void Connect_WithAPasswordRefusesPlainFtpUnlessAllowed()
    {
        _client.SetupGet(c => c.IsConnected).Returns(false);
        _client.Setup(c => c.Connect()).Throws(new FtpSecurityNotAvailableException("no TLS"));
        using var service = new FtpFileSystemService((_, _, _, _, secure) =>
        {
            _secureRequests.Add(secure);
            return _client.Object;
        });

        var exception = Assert.ThrowsExactly<IOException>(() => service.Connect("ftp.example.org", "alice", "pw"));

        StringAssert.Contains(exception.Message, FtpFileSystemService.AllowPlainSetting);
        CollectionAssert.AreEqual(new[] { true }, _secureRequests);
    }

    [TestMethod]
    public void Connect_FallsBackToPlainWhenAllowedOrAnonymous()
    {
        _client.SetupGet(c => c.IsConnected).Returns(false);
        _client.SetupSequence(c => c.Connect())
            .Throws(new FtpSecurityNotAvailableException("no TLS"))
            .Pass()
            .Throws(new FtpSecurityNotAvailableException("no TLS"))
            .Pass();
        Func<string, int, string, string, bool, IFtpClient> factory = (_, _, _, _, secure) =>
        {
            _secureRequests.Add(secure);
            return _client.Object;
        };

        using (var allowed = new FtpFileSystemService(factory, () => true))
        {
            allowed.Connect("ftp.example.org", "alice", "pw");
        }

        using (var anonymous = new FtpFileSystemService(factory))
        {
            anonymous.Connect("ftp.example.org", null, null);
        }

        CollectionAssert.AreEqual(new[] { true, false, true, false }, _secureRequests);
    }

    [TestMethod]
    public void SplitHost_ReadsAnOptionalPort()
    {
        Assert.AreEqual(("example.org", 21), FtpFileSystemService.SplitHost("example.org"));
        Assert.AreEqual(("example.org", 2121), FtpFileSystemService.SplitHost("example.org:2121"));
        Assert.AreEqual(("example.org:abc", 21), FtpFileSystemService.SplitHost("example.org:abc"));
    }

    [TestMethod]
    public void Url_ParsesUserHostPortAndPathWithSpaces()
    {
        var url = new Url("ftp://alice@files.example.org:2121/pub/my docs/");

        Assert.AreEqual(ContainerType.FtpFileSystem, url.ContainerType);
        Assert.AreEqual("alice", url.Username);
        Assert.AreEqual("files.example.org:2121", url.Host);
        Assert.AreEqual("/pub/my docs/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);

        var anonymous = new Url("ftp://ftp.example.org/pub/readme.txt");
        Assert.AreEqual(string.Empty, anonymous.Username);
        Assert.AreEqual("ftp.example.org", anonymous.Host);
        Assert.AreEqual("/pub/readme.txt", anonymous.Path);
    }

    [TestMethod]
    public void Connect_WithoutAUserLogsInAnonymously()
    {
        using var service = CreateService();

        service.Connect("ftp.example.org:2121", null, null);

        Assert.AreEqual("ftp.example.org", _host);
        Assert.AreEqual(2121, _port);
        Assert.AreEqual(FtpFileSystemService.AnonymousUser, _user);
        Assert.AreEqual(FtpFileSystemService.AnonymousPassword, _password);
        _client.Verify(c => c.Connect(), Times.AtMostOnce);
    }

    [TestMethod]
    public void List_PutsFoldersFirstAndAddsTheParentRow()
    {
        using var service = CreateService();
        service.Connect("ftp.example.org", "alice", "secret");
        _client.Setup(c => c.GetListing("/pub/")).Returns(
        [
            new FtpListItem { Name = "b.txt", Type = FtpObjectType.File, Size = 2048, Modified = new DateTime(2026, 1, 2, 3, 4, 0) },
            new FtpListItem { Name = "a_dir", Type = FtpObjectType.Directory, Modified = new DateTime(2026, 1, 1) },
            new FtpListItem { Name = ".", Type = FtpObjectType.Directory },
        ]);

        var rows = service.List(new Url("ftp://alice@ftp.example.org/pub/")).ToList();

        Assert.AreEqual(3, rows.Count);
        Assert.AreEqual("..", rows[0].DisplayName);
        Assert.AreEqual("a_dir", rows[1].DisplayName);
        Assert.AreEqual(PathType.Folder, rows[1].Url.PathType);
        Assert.AreEqual("b.txt", rows[2].DisplayName);
        Assert.AreEqual("2026-01-02", rows[2].Date);
        Assert.AreEqual("03:04", rows[2].Time);
    }

    [TestMethod]
    public void ListChildren_LeavesOutTheParentRow()
    {
        using var service = CreateService();
        service.Connect("ftp.example.org", "alice", "secret");
        _client.Setup(c => c.GetListing("/pub/")).Returns(
            [new FtpListItem { Name = "x.bin", Type = FtpObjectType.File, Size = 1 }]);

        var children = service.ListChildren(new Url("ftp://alice@ftp.example.org/pub/")).ToList();

        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("x.bin", children[0].DisplayName);
    }

    [TestMethod]
    public async Task ReadFileAsync_ReturnsTheDownloadedBytes()
    {
        using var service = CreateService();
        service.Connect("ftp.example.org", "alice", "secret");
        _client.Setup(c => c.DownloadBytes(out It.Ref<byte[]?>.IsAny, "/pub/x.bin", It.IsAny<long>(), It.IsAny<Action<FtpProgress>>(), It.IsAny<long>()))
            .Callback(new DownloadBytesCallback((out byte[]? bytes, string _, long _, Action<FtpProgress>? _, long _) => bytes = [1, 2, 3]))
            .Returns(true);

        var file = await service.ReadFileAsync(new Url("ftp://alice@ftp.example.org/pub/x.bin"));

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, file!.Content);
    }

    private delegate void DownloadBytesCallback(out byte[]? bytes, string remotePath, long restartPosition, Action<FtpProgress>? progress, long length);

    [TestMethod]
    public void ListChildren_WhenTheListingFails_Throws_SoAWalkDoesNotMistakeItForAnEmptyFolder()
    {
        using var service = CreateService();
        service.Connect("ftp.example.org", "alice", "secret");
        _client.Setup(c => c.GetListing("/pub/")).Throws(new IOException("550 Permission denied"));

        Assert.Throws<IOException>(() => service.ListChildren(new Url("ftp://alice@ftp.example.org/pub/")).ToList());
    }

    [TestMethod]
    public void List_WhenTheListingFails_IsAnEmptyPanel()
    {
        using var service = CreateService();
        service.Connect("ftp.example.org", "alice", "secret");
        _client.Setup(c => c.GetListing("/pub/")).Throws(new IOException("550 Permission denied"));

        var entries = service.List(new Url("ftp://alice@ftp.example.org/pub/")).ToList();

        Assert.AreEqual(0, entries.Count);
    }
}
