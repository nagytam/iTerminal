using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class FileSizeHelperTest
{
    [TestMethod]
    public void FileSizeWithZero()
    {
        var fileSize = FileSizeHelper.ToFileSize(0);
        Assert.AreEqual("0  B", fileSize);
    }

    [TestMethod]
    public void FileSizeWithOne()
    {
        var fileSize = FileSizeHelper.ToFileSize(1);
        Assert.AreEqual("1  B", fileSize);
    }

    [TestMethod]
    public void FileSizeWithBytes()
    {
        var fileSize = FileSizeHelper.ToFileSize(512);
        Assert.IsTrue(fileSize.Contains("B"));
    }

    [TestMethod]
    public void FileSizeWithKilobytes()
    {
        var fileSize = FileSizeHelper.ToFileSize(1024);
        Assert.IsTrue(fileSize.Contains("KB"));
    }

    [TestMethod]
    public void FileSizeWithMegabytes()
    {
        var fileSize = FileSizeHelper.ToFileSize(1024 * 1024);
        Assert.IsTrue(fileSize.Contains("MB"));
    }

    [TestMethod]
    public void FileSizeWithGigabytes()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("GB"));
    }

    [TestMethod]
    public void FileSizeWithTerabytes()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("TB"));
    }

    [TestMethod]
    public void FileSizeFromString()
    {
        var fileSize = FileSizeHelper.ToFileSize("1024");
        Assert.IsTrue(fileSize.Contains("KB"));
    }

    [TestMethod]
    public void FileSizeLarge()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("PB"));
    }

    [TestMethod]
    public void FileSizeVeryLarge()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024 * 1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("EB"));
    }

    [TestMethod]
    public void FileSizeFormatted()
    {
        var fileSize = FileSizeHelper.ToFileSize(2048);
        Assert.IsNotNull(fileSize);
        Assert.IsFalse(string.IsNullOrEmpty(fileSize));
    }

    [TestMethod]
    public void StringOverload_ReadsAByteCount_AndShowsOtherTextAsItIs()
    {
        Assert.AreEqual(1024L.ToFileSize(), "1024".ToFileSize());
        Assert.AreEqual("not a size", "not a size".ToFileSize());
        Assert.AreEqual("1.5 MB", "1.5 MB".ToFileSize());
    }
}
