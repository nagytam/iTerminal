using iConsole.Data;

namespace iConsole.Test.Data;

/// <summary>
/// docker://, git://, search:// and preview:// parse from the URL alone: no file:// prefix, no disk
/// probe, folder-ness from the trailing slash, and parents that stay on the drive.
/// </summary>
[TestClass]
public class UrlVirtualDriveTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("docker://", ContainerType.Docker)]
    [DataRow("git://", ContainerType.Git)]
    [DataRow("search://", ContainerType.SearchResults)]
    [DataRow("preview://", ContainerType.Preview)]
    public void TheDriveRootIsAFolderOnItsOwnDrive(string url, ContainerType containerType)
    {
        var parsed = new Url(url);

        Assert.AreEqual(url, parsed.FullUrl, "no file:// prefix is added");
        Assert.AreEqual(containerType, parsed.ContainerType);
        Assert.AreEqual(PathType.Folder, parsed.PathType);
        Assert.AreEqual(string.Empty, parsed.Path);
        Assert.AreEqual(url, parsed.UrlScheme);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADockerFileHasItsNameExtensionAndContainer()
    {
        var url = new Url("docker://web/etc/nginx/nginx.conf");

        Assert.AreEqual(ContainerType.Docker, url.ContainerType);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("nginx.conf", url.Name);
        Assert.AreEqual("conf", url.Extension);
        Assert.AreEqual("web", url.Host, "the container is the host");
        Assert.AreEqual('/', url.DirectorySeparatorChar);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADockerPathThatAlsoExistsLocallyIsStillTheContainers()
    {
        // /etc/ exists on Linux and C:\Windows\ on Windows; neither may leak local metadata in.
        var url = new Url("docker://web/etc/");

        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual("etc", url.Name);
        Assert.IsFalse(url.Exists);
        Assert.IsTrue(string.IsNullOrEmpty(url.Date), "no local file date leaks in");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParentsWalkUpTheDriveAndEndAtItsRoot()
    {
        var file = new Url("docker://web/etc/nginx/nginx.conf");

        Assert.AreEqual("docker://web/etc/nginx/", file.ParentUrl.FullUrl);
        Assert.AreEqual("docker://web/", new Url("docker://web/etc/").ParentUrl.FullUrl);
        Assert.AreEqual("docker://", new Url("docker://web/").ParentUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRootOfADockerPathIsTheContainersRoot()
    {
        Assert.AreEqual("docker://web/", new Url("docker://web/etc/nginx/").RootUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRootOfAGitPathIsTheRevisionsTree()
    {
        var url = new Url("git://C%3A%5Csrc%5Capp/feature%2Flogin/src/lib/");

        Assert.AreEqual(ContainerType.Git, url.ContainerType);
        Assert.AreEqual("lib", url.Name);
        Assert.AreEqual("git://C%3A%5Csrc%5Capp/feature%2Flogin/", url.RootUrl.FullUrl);
        Assert.AreEqual("git://C%3A%5Csrc%5Capp/feature%2Flogin/src/", url.ParentUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CombiningANameKeepsTheDrive()
    {
        var combined = new Url("docker://web/tmp/").Combine("notes.txt");

        Assert.AreEqual("docker://web/tmp/notes.txt", combined.FullUrl);
        Assert.AreEqual(ContainerType.Docker, combined.ContainerType);
        Assert.AreEqual(PathType.File, combined.PathType);
    }
}
