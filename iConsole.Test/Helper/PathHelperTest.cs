using iConsole.Helper;
using System.Runtime.InteropServices;

namespace iConsole.Test.Helper;

[TestClass]
public class PathHelperTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidSimplePath()
    {
        var result = PathHelper.IsValidPath("test");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidRelativePath()
    {
        var separator = Path.DirectorySeparatorChar;
        var result = PathHelper.IsValidPath($"folder{separator}test");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidAbsolutePath()
    {
        // Test absolute path that works on the current platform
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var result = PathHelper.IsValidPath(@"C:\Users\Test");
            Assert.IsTrue(result);
        }
        else
        {
            var result = PathHelper.IsValidPath("/home/user/test");
            Assert.IsTrue(result);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperInvalidNullPath()
    {
        var result = PathHelper.IsValidPath(null!);
        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperInvalidEmptyPath()
    {
        var result = PathHelper.IsValidPath("");
        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperInvalidWhitespacePath()
    {
        var result = PathHelper.IsValidPath("   ");
        Assert.IsFalse(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidPathWithDots()
    {
        var separator = Path.DirectorySeparatorChar;
        var result = PathHelper.IsValidPath($"..{separator}test");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidPathWithDashes()
    {
        var result = PathHelper.IsValidPath("test-folder");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidPathWithUnderscore()
    {
        var result = PathHelper.IsValidPath("test_folder");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidPathWithNumbers()
    {
        var result = PathHelper.IsValidPath("test123");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperValidFileExtension()
    {
        var result = PathHelper.IsValidPath("test.txt");
        Assert.IsTrue(result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PathHelperPathWithoutRootIssue()
    {
        var separator = Path.DirectorySeparatorChar;
        var result = PathHelper.IsValidPath($"folder1{separator}folder2");
        Assert.IsTrue(result);
    }
}
