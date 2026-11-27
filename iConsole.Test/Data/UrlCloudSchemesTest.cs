using iConsole.Data;

namespace iConsole.Test.Data;

/// <summary>The three schemes of the Dropbox, Azure Blob and Bluetooth OBEX drives parse without touching the disk.</summary>
[TestClass]
public class UrlCloudSchemesTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Dropbox_ReadsTheAccountAndTheRawPath()
    {
        var url = new Url("dropbox://work/Photos/2024/trip.jpg");

        Assert.AreEqual(ContainerType.Dropbox, url.ContainerType);
        Assert.AreEqual("work", url.Host);
        Assert.AreEqual("/Photos/2024/trip.jpg", url.Path);
        Assert.AreEqual(PathType.File, url.PathType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dropbox_TheBareSchemeIsTheDefaultAccountsRoot()
    {
        var url = new Url("dropbox:///");

        Assert.AreEqual(ContainerType.Dropbox, url.ContainerType);
        Assert.AreEqual(string.Empty, url.Host);
        Assert.AreEqual(PathType.Folder, url.PathType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AzureBlob_ReadsTheStorageAccountAsTheHost_AndTheContainerAsTheFirstSegment()
    {
        var url = new Url("azblob://acc/photos/2024/");

        Assert.AreEqual(ContainerType.AzureBlob, url.ContainerType);
        Assert.AreEqual("acc", url.Host);
        Assert.AreEqual("/photos/2024/", url.Path);
        Assert.AreEqual(PathType.Folder, url.PathType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Obex_ReadsTheDeviceAddressAndItsPath_AsAVirtualDrive()
    {
        var url = new Url("obex://AA:BB:CC:DD:EE:FF/DCIM/Camera/a.jpg");

        Assert.AreEqual(ContainerType.Obex, url.ContainerType);
        Assert.AreEqual("AA:BB:CC:DD:EE:FF/DCIM/Camera/a.jpg", url.Path);
        Assert.AreEqual(PathType.File, url.PathType);
        Assert.AreEqual("AA:BB:CC:DD:EE:FF/DCIM/Camera/", url.ParentUrl.Path);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentOfADeviceFolder_IsTheDevice()
    {
        var url = new Url("obex://AA:BB:CC:DD:EE:FF/DCIM/");

        Assert.AreEqual("obex://AA:BB:CC:DD:EE:FF/", url.ParentUrl.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheParentOfADeviceRoot_IsTheDeviceList()
    {
        var url = new Url("obex://AA:BB:CC:DD:EE:FF/");

        Assert.AreEqual("obex://", url.ParentUrl.FullUrl);
    }
}
