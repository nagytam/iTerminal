using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Nfs;
using iTerminal.Core.Test.Services.Nfs;
using Moq;

namespace iTerminal.Core.Test.Model.ContentModels;

/// <summary>
/// The panel's rights follow the level it shows: the drive root and a host's exports take no new entries and no marks, an
/// export takes both inside it.
/// </summary>
[TestClass]
public class ContentModelNfsDriveTest
{
    private static ContentModelNfsDrive Panel(out FakeNfsMountFactory factory)
    {
        factory = new FakeNfsMountFactory();
        factory.Exports.Add("/srv/data");
        return new ContentModelNfsDrive("left", null, new NfsDriveService(factory), null, Mock.Of<iConsole.Services.IFrequentExecutionGuardService>());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDriveRootAndAHostAreReadOnlyAndUnmarkable()
    {
        var panel = Panel(out _);

        panel.SetUrl(new Url("nfs://"));
        Assert.IsTrue(panel.IsReadOnly);
        Assert.IsFalse(panel.CanMark);

        panel.SetUrl(new Url("nfs://nas/"));
        Assert.IsTrue(panel.IsReadOnly, "a host holds exports, not files");
        Assert.IsFalse(panel.CanMark, "an export is a mount and cannot be marked");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AnExportIsWritableAndItsRowsCanBeMarked()
    {
        var panel = Panel(out _);

        panel.SetUrl(new Url("nfs://nas/%2Fsrv%2Fdata/"));

        Assert.IsFalse(panel.IsReadOnly);
        Assert.IsTrue(panel.CanMark);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheDriveIsTheNfsContainerType()
    {
        var panel = Panel(out _);

        Assert.AreEqual(ContainerType.Nfs, panel.ContainerType);
    }
}
