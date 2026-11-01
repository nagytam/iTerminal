using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class ContainerTypeTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ContainerType_FileSystem_HasValue()
    {
        var type = ContainerType.FileSystem;
        Assert.AreEqual(0, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContainerType_FileSystemArchive_HasValue()
    {
        var type = ContainerType.FileSystemArchive;
        Assert.IsTrue((int)type >= 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContainerType_SshFileSystem_HasValue()
    {
        var type = ContainerType.SshFileSystem;
        Assert.IsTrue((int)type >= 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContainerType_MemoryDrive_HasValue()
    {
        var type = ContainerType.MemoryDrive;
        Assert.IsTrue((int)type >= 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ContainerType_AllValuesAreUnique()
    {
        var types = new[]
        {
            ContainerType.FileSystem,
            ContainerType.FileSystemArchive,
            ContainerType.SshFileSystem,
            ContainerType.MemoryDrive
        };

        var distinctTypes = types.Distinct().ToArray();
        Assert.AreEqual(types.Length, distinctTypes.Length);
    }
}
