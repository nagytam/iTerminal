using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class UrlWebDavTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void DavUrl_ParsesUserHostPortAndPath()
    {
        var url = new Url("dav://alice@files.example.org:8080/shared/my docs/");

        Assert.AreEqual(ContainerType.WebDav, url.ContainerType);
        Assert.AreEqual(Container.WEBDAV, url.UrlScheme);
        Assert.AreEqual(Container.WEBDAV, url.Container);
        Assert.AreEqual("alice", url.Username);
        Assert.AreEqual("files.example.org:8080", url.Host);
        Assert.AreEqual("/shared/my docs/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("my docs", url.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DavsUrl_IsTheSecureScheme_AndAnonymousWithoutAUser()
    {
        var url = new Url("davs://cloud.example.org/remote.php/webdav/report.final.pdf");

        Assert.AreEqual(ContainerType.WebDav, url.ContainerType);
        Assert.AreEqual(Container.WEBDAV_SECURE, url.UrlScheme);
        Assert.AreEqual(string.Empty, url.Username);
        Assert.AreEqual("cloud.example.org", url.Host);
        Assert.AreEqual("/remote.php/webdav/report.final.pdf", url.Path);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("report.final.pdf", url.Name);
        Assert.AreEqual("pdf", url.Extension);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void BareHost_IsTheRootFolder()
    {
        var url = new Url("dav://example.org");

        Assert.AreEqual("/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("example.org", url.Host);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParentAndRoot_KeepSchemeAndUser()
    {
        var url = new Url("davs://bob@example.org:8443/a/b/c.txt");

        Assert.AreEqual("davs://bob@example.org:8443/a/b/", url.ParentUrl.FullUrl);
        Assert.AreEqual("davs://bob@example.org:8443/", url.RootUrl.FullUrl);
        Assert.AreEqual("davs://bob@example.org:8443/a/", new Url("davs://bob@example.org:8443/a/b/").ParentUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void DavScheme_IsNotMistakenForALocalPath()
    {
        Assert.AreNotEqual(ContainerType.FileSystem, new Url("dav://host/x/").ContainerType);
        Assert.AreNotEqual(ContainerType.FileSystem, new Url("davs://host/x/").ContainerType);
    }
}
