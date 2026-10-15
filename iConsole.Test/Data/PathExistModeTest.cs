using iConsole.Data;

namespace iConsole.Test.Data;

[TestClass]
public class PathExistModeTest
{
    [TestMethod]
    public void PathExistMode_Default_HasValue()
    {
        var mode = PathExistMode.Default;
        Assert.AreEqual(0, (int)mode);
    }

    [TestMethod]
    public void PathExistMode_NewFolder_HasValue()
    {
        var mode = PathExistMode.NewFolder;
        Assert.IsTrue((int)mode > 0);
    }

    [TestMethod]
    public void PathExistMode_NewFile_HasValue()
    {
        var mode = PathExistMode.NewFile;
        Assert.IsTrue((int)mode > 0);
    }

    [TestMethod]
    public void PathExistMode_FolderOrNewFile_HasValue()
    {
        var mode = PathExistMode.FolderOrNewFile;
        Assert.IsTrue((int)mode > 0);
    }

    [TestMethod]
    public void PathExistMode_AllValuesAreUnique()
    {
        var modes = new[]
        {
            PathExistMode.Default,
            PathExistMode.NewFolder,
            PathExistMode.NewFile,
            PathExistMode.FolderOrNewFile
        };

        var distinctModes = modes.Distinct().ToArray();
        Assert.AreEqual(modes.Length, distinctModes.Length);
    }
}
