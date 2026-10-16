#pragma warning disable MSTEST0032 // These tests pin a constant's value on purpose.
using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ContainerTest
{
    [TestMethod]
    public void Container_FILE_HasCorrectValue()
    {
        Assert.AreEqual("file://", Container.FILE);
    }

    [TestMethod]
    public void Container_ZIP_ARCHIVE_HasCorrectValue()
    {
        Assert.AreEqual("zip://", Container.ZIP_ARCHIVE);
    }

    [TestMethod]
    public void Container_SSH_HasCorrectValue()
    {
        Assert.AreEqual("ssh://", Container.SSH);
    }

    [TestMethod]
    public void Container_MEMORY_DRIVE_HasCorrectValue()
    {
        Assert.AreEqual("memdrive://", Container.MEMORY_DRIVE);
    }

    [TestMethod]
    public void Container_MUSIC_PLAYLIST_HasCorrectValue()
    {
        Assert.AreEqual("musicplaylist://", Container.MUSIC_PLAYLIST);
    }

    [TestMethod]
    public void Container_MUSIC_CATALOG_HasCorrectValue()
    {
        Assert.AreEqual("musiccatalog://", Container.MUSIC_CATALOG);
    }

    [TestMethod]
    public void Container_FILE_IsConstant()
    {
        var value1 = Container.FILE;
        var value2 = Container.FILE;
        Assert.AreSame(value1, value2);
    }
}
