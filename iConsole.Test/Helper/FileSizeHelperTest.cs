using iConsole.Helper;

namespace iConsole.Test.Helper;

[TestClass]
public class FileSizeHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithZero()
    {
        var fileSize = FileSizeHelper.ToFileSize(0);
        Assert.AreEqual("0  B", fileSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithOne()
    {
        var fileSize = FileSizeHelper.ToFileSize(1);
        Assert.AreEqual("1  B", fileSize);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithBytes()
    {
        var fileSize = FileSizeHelper.ToFileSize(512);
        Assert.IsTrue(fileSize.Contains("B"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithKilobytes()
    {
        var fileSize = FileSizeHelper.ToFileSize(1024);
        Assert.IsTrue(fileSize.Contains("KB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithMegabytes()
    {
        var fileSize = FileSizeHelper.ToFileSize(1024 * 1024);
        Assert.IsTrue(fileSize.Contains("MB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithGigabytes()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("GB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeWithTerabytes()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("TB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeFromString()
    {
        var fileSize = FileSizeHelper.ToFileSize("1024");
        Assert.IsTrue(fileSize.Contains("KB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeLarge()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("PB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeVeryLarge()
    {
        var fileSize = FileSizeHelper.ToFileSize((long)1024 * 1024 * 1024 * 1024 * 1024 * 1024);
        Assert.IsTrue(fileSize.Contains("EB"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FileSizeFormatted()
    {
        var fileSize = FileSizeHelper.ToFileSize(2048);
        Assert.IsNotNull(fileSize);
        Assert.IsFalse(string.IsNullOrEmpty(fileSize));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StringOverload_ReadsAByteCount_AndShowsOtherTextAsItIs()
    {
        Assert.AreEqual(1024L.ToFileSize(), "1024".ToFileSize());
        Assert.AreEqual("not a size", "not a size".ToFileSize());
        Assert.AreEqual("1.5 MB", "1.5 MB".ToFileSize());
    }
}
