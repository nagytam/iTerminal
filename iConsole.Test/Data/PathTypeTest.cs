using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class PathTypeTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void PathType_Empty_HasValue()
    {
        var type = PathType.Empty;
        Assert.AreEqual(0, (int)type);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathType_Folder_HasValue()
    {
        var type = PathType.Folder;
        Assert.IsTrue((int)type > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathType_File_HasValue()
    {
        var type = PathType.File;
        Assert.IsTrue((int)type > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathType_Link_HasValue()
    {
        var type = PathType.Link;
        Assert.IsTrue((int)type > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathType_AllValuesAreUnique()
    {
        var types = new[]
        {
            PathType.Empty,
            PathType.Folder,
            PathType.File,
            PathType.Link
        };

        var distinctTypes = types.Distinct().ToArray();
        Assert.AreEqual(types.Length, distinctTypes.Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathType_CanConvertToString()
    {
        var type = PathType.File;
        var str = type.ToString();
        Assert.AreEqual("File", str);
    }
}
