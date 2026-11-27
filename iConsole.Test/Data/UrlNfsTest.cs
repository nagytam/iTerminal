using iConsole.Data;

namespace iConsole.Test.Data;

/// <summary>
/// nfs://host/&lt;escaped export&gt;/&lt;path&gt; parses from the URL alone: the host is the first segment, the export stays one
/// escaped segment through every parent and root, and the parents walk back to the export, the host and the drive root.
/// </summary>
[TestClass]
public class UrlNfsTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void TheHostAndTheEscapedExportStayOneSegmentEachUnderTheScheme()
    {
        var url = new Url("nfs://nas/%2Fsrv%2Fdata/sub/a.txt");

        Assert.AreEqual(ContainerType.Nfs, url.ContainerType);
        Assert.AreEqual("nas", url.Host);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("a.txt", url.Name);
        Assert.AreEqual("txt", url.Extension);
        Assert.AreEqual("nas/%2Fsrv%2Fdata/sub/a.txt", url.Path, "the export is not unescaped by Url");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentsWalkBackThroughTheExportToTheHostAndTheDriveRoot()
    {
        var file = new Url("nfs://nas/%2Fsrv%2Fdata/sub/a.txt");

        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/sub/", file.ParentUrl.FullUrl);
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/", file.ParentUrl.ParentUrl.FullUrl);
        Assert.AreEqual("nfs://nas/", file.ParentUrl.ParentUrl.ParentUrl.FullUrl);
        Assert.AreEqual("nfs://", file.ParentUrl.ParentUrl.ParentUrl.ParentUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheRootOfAnExportIsTheExportAndTheRootOfAHostIsTheHost()
    {
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/", new Url("nfs://nas/%2Fsrv%2Fdata/sub/a.txt").RootUrl.FullUrl);
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/", new Url("nfs://nas/%2Fsrv%2Fdata/").RootUrl.FullUrl, "an export is its own root");
        Assert.AreEqual("nfs://nas/", new Url("nfs://nas/").RootUrl.FullUrl);
        Assert.AreEqual("nfs://", new Url("nfs://").RootUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheBareSchemeIsTheDriveRootAndAFolder()
    {
        var root = new Url("nfs://");

        Assert.AreEqual(ContainerType.Nfs, root.ContainerType);
        Assert.AreEqual(PathType.Folder, root.PathType);
        Assert.AreEqual(string.Empty, root.Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnExportFolderEndsInASlashAndIsAFolder()
    {
        var export = new Url("nfs://nas/%2Fsrv%2Fdata/");

        Assert.AreEqual(PathType.Folder, export.PathType);
        Assert.AreEqual("nfs://nas/%2Fsrv%2Fdata/", export.FullUrl);
    }
}
