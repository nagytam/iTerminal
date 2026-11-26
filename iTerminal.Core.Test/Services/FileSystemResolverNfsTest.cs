using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Nfs;
using Moq;
using iTerminal.Core.Test.Services.Nfs;

namespace iTerminal.Core.Test.Services;

/// <summary>
/// An nfs:// URL reaches the NFS drive only through an NFS panel. It must never reach a local panel's file system, which
/// would read <c>nfs://host/...</c> as a relative path.
/// </summary>
[TestClass]
public class FileSystemResolverNfsTest
{
    private static NfsDriveService Service() => new(new FakeNfsMountFactory());

    private static FileSystemResolver Create(params IContentModel[] panels)
    {
        var service = new Mock<IContentModelService>();
        var models = new iConsole.Data.ConcurrentList<IContentModel>();
        foreach (var panel in panels)
        {
            models.Add(panel);
        }

        service.SetupGet(s => s.ContentModels).Returns(models);
        return new FileSystemResolver(Mock.Of<IFileSystemService>(), Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(), Mock.Of<IArchiveFileSystemService>(), service.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAnNfsPanelAnNfsUrlIsRefusedRatherThanGivenToALocalPanel()
    {
        var localPanel = Mock.Of<IContentModel>();
        var resolver = Create(localPanel);

        Assert.ThrowsExactly<NotSupportedException>(() =>
            resolver.Resolve(new Url("nfs://nas/%2Fsrv%2Fdata/a.txt"), localPanel, "Copy"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnOpenNfsPanelServesItsOwnFileSystem()
    {
        var service = Service();
        var panel = new ContentModelNfsDrive("right", null, service, null, Mock.Of<iConsole.Services.IFrequentExecutionGuardService>());
        var resolver = Create(panel);

        Assert.AreSame(service, resolver.Resolve(new Url("nfs://nas/%2Fsrv%2Fdata/a.txt"), null, "Copy"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheModelOfAnNfsPanelServesItsOwnFileSystemWhenItIsTheOne()
    {
        var service = Service();
        var panel = new ContentModelNfsDrive("left", null, service, null, Mock.Of<iConsole.Services.IFrequentExecutionGuardService>());
        var resolver = Create();

        Assert.AreSame(service, resolver.Resolve(new Url("nfs://nas/%2Fsrv%2Fdata/a.txt"), panel, "Copy"));
    }
}
