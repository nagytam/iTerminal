using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Services;

/// <summary>
/// The one answer to "which file system serves this URL" — what copy, move, delete, compress,
/// extract and compare all ask.
/// </summary>
[TestClass]
public class FileSystemResolverTest
{
    private readonly IFileSystemService _local = Mock.Of<IFileSystemService>();
    private readonly IMemoryDriveService _memory = Mock.Of<IMemoryDriveService>();
    private readonly ISshFileSystemService _ssh = Mock.Of<ISshFileSystemService>();
    private readonly IArchiveFileSystemService _zip = Mock.Of<IArchiveFileSystemService>();

    private FileSystemResolver Create(params IContentModel[] panels)
    {
        var service = new Mock<IContentModelService>();
        var models = new iConsole.Data.ConcurrentList<IContentModel>();
        foreach (var panel in panels)
        {
            models.Add(panel);
        }

        service.SetupGet(s => s.ContentModels).Returns(models);
        return new FileSystemResolver(_local, _memory, _ssh, _zip, service.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheSharedServicesServeTheirSchemes()
    {
        var resolver = Create();

        Assert.AreSame(_local, resolver.Resolve(new Url(Path.GetTempPath())));
        Assert.AreSame(_memory, resolver.Resolve(new Url("memdrive://x/")));
        Assert.AreSame(_ssh, resolver.Resolve(new Url("ssh://me@host:/home/")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ADriveReachableOnlyThroughItsPanelIsServedByWhatThePanelHandsOver()
    {
        var dockerFileSystem = Mock.Of<IFileSystem>();
        var panel = new Mock<IContentModel>();
        panel.As<IFileSystemProvider>().SetupGet(p => p.ContainerType).Returns(ContainerType.Docker);
        panel.As<IFileSystemProvider>().SetupGet(p => p.FileSystem).Returns(dockerFileSystem);

        Assert.AreSame(dockerFileSystem, Create(panel.Object).Resolve(new Url("docker://web/etc/")),
            "found among the panels, whichever one shows the drive");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("ftp://me@files.example.com/pub/a.txt")]
    [DataRow("smb://me@nas/share/My Documents/a.txt")]
    [DataRow("dav://me@cloud.example.com/dir/a.txt")]
    [DataRow("iphone://0000-1111/DCIM/a.jpg")]
    public void ARemoteAccountWithNoSharedServiceIsServedByThePanelShowingIt(string url)
    {
        var panel = new Mock<IContentModel>();
        panel.SetupGet(p => p.Url).Returns(new Url(url).ParentUrl);

        Assert.AreSame(panel.Object, Create(panel.Object).Resolve(new Url(url)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void APanelOnAnotherServerIsNeverUsed()
    {
        var panel = new Mock<IContentModel>();
        panel.SetupGet(p => p.Url).Returns(new Url("ftp://me@other.example.com/pub/"));

        Assert.ThrowsExactly<NotSupportedException>(() => Create(panel.Object).Resolve(new Url("ftp://me@files.example.com/pub/a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NothingToServeSaysWhatWasBeingDone()
    {
        var exception = Assert.ThrowsExactly<NotSupportedException>(() => Create().Resolve(new Url("docker://web/"), operation: "Copy"));

        Assert.AreEqual("Copy is not supported on Docker drives.", exception.Message);
    }
}
