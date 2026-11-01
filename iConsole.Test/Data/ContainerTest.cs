#pragma warning disable MSTEST0032 // These tests pin a constant's value on purpose.
using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ContainerTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Container_FILE_HasCorrectValue()
    {
        Assert.AreEqual("file://", Container.FILE);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_ZIP_ARCHIVE_HasCorrectValue()
    {
        Assert.AreEqual("zip://", Container.ZIP_ARCHIVE);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_SSH_HasCorrectValue()
    {
        Assert.AreEqual("ssh://", Container.SSH);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_MEMORY_DRIVE_HasCorrectValue()
    {
        Assert.AreEqual("memdrive://", Container.MEMORY_DRIVE);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_MUSIC_PLAYLIST_HasCorrectValue()
    {
        Assert.AreEqual("musicplaylist://", Container.MUSIC_PLAYLIST);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_MUSIC_CATALOG_HasCorrectValue()
    {
        Assert.AreEqual("musiccatalog://", Container.MUSIC_CATALOG);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Container_FILE_IsConstant()
    {
        var value1 = Container.FILE;
        var value2 = Container.FILE;
        Assert.AreSame(value1, value2);
    }
}
