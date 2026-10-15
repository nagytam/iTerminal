using iConsole.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class FileSystemServiceTest
{
    private string CreateTempFile(string content = "hello")
    {
        var f = Path.GetTempFileName();
        File.WriteAllText(f, content);
        return f;
    }

    private string CreateTempFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [TestMethod]
    public void IsReadOnly_ReturnsFalse()
    {
        var svc = new FileSystemService();
        Assert.IsFalse(svc.IsReadOnly);
    }

    [TestMethod]
    public async Task ReadFileAsync_ReturnsContent()
    {
        var svc = new FileSystemService();
        var f = CreateTempFile("test data");
        try
        {
            var result = await svc.ReadFileAsync(new Url(f));
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Length > 0);
        }
        finally { File.Delete(f); }
    }

    [TestMethod]
    public async Task WriteFileAsync_WritesContent()
    {
        var svc = new FileSystemService();
        var f = Path.GetTempFileName();
        try
        {
            var data = new byte[] { 1, 2, 3 };
            await svc.WriteFileAsync(new Url(f), new MemoryStream(data));
            var written = File.ReadAllBytes(f);
            CollectionAssert.AreEqual(data, written);
        }
        finally { File.Delete(f); }
    }

    [TestMethod]
    public void DeleteFile_ReturnsTrueWhenExists()
    {
        var svc = new FileSystemService();
        var f = CreateTempFile();
        var result = svc.DeleteFile(new Url(f), false);
        Assert.IsTrue(result);
        Assert.IsFalse(File.Exists(f));
    }

    [TestMethod]
    public void DeleteFile_ReturnsFalseWhenNotExists()
    {
        var svc = new FileSystemService();
        var result = svc.DeleteFile(new Url(@"c:\nonexistent_xyz123.txt"), false);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void DeleteFolder_ReturnsTrueWhenExists()
    {
        var svc = new FileSystemService();
        var dir = CreateTempFolder();
        var result = svc.DeleteFolder(new Url(dir), true, false);
        Assert.IsTrue(result);
        Assert.IsFalse(Directory.Exists(dir));
    }

    [TestMethod]
    public void DeleteFolder_ReturnsFalseWhenNotExists()
    {
        var svc = new FileSystemService();
        var result = svc.DeleteFolder(new Url(@"c:\nonexistent_folder_xyz123"), true, false);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void DeleteFolder_NonEmptyWithoutRecursive_ThrowsInsteadOfSwallowing()
    {
        var svc = new FileSystemService();
        var dir = CreateTempFolder();
        File.WriteAllText(Path.Combine(dir, "child.txt"), "x");
        try
        {
            try
            {
                svc.DeleteFolder(new Url(dir), false, false);
                Assert.Fail("Expected IOException");
            }
            catch (IOException)
            {
                // Expected: a real error must propagate, not be swallowed into 'false'.
            }
            Assert.IsTrue(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [TestMethod]
    public void DeleteFile_MoveToRecycleBin_RemovesFromOriginalLocation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Recycle bin behavior is Windows-specific.");
            return;
        }

        var svc = new FileSystemService();
        var f = CreateTempFile();
        var result = svc.DeleteFile(new Url(f), true);
        Assert.IsTrue(result);
        Assert.IsFalse(File.Exists(f));
    }

    [TestMethod]
    public void ExistsFile_ReturnsTrueWhenExists()
    {
        var svc = new FileSystemService();
        var f = CreateTempFile();
        try
        {
            Assert.IsTrue(svc.ExistsFile(new Url(f)));
        }
        finally { File.Delete(f); }
    }

    [TestMethod]
    public void ExistsFile_ReturnsFalseWhenNotExists()
    {
        var svc = new FileSystemService();
        Assert.IsFalse(svc.ExistsFile(new Url(@"c:\definitely_not_there_xyz.txt")));
    }

    [TestMethod]
    public void ExistsFolder_ReturnsTrueWhenExists()
    {
        var svc = new FileSystemService();
        Assert.IsTrue(svc.ExistsFolder(new Url(Path.GetTempPath())));
    }

    [TestMethod]
    public void ExistsFolder_ReturnsFalseWhenNotExists()
    {
        var svc = new FileSystemService();
        Assert.IsFalse(svc.ExistsFolder(new Url(@"c:\nonexistent_folder_xyz123")));
    }

    [TestMethod]
    public async Task CreateFolderAsync_CreatesDirectory()
    {
        var svc = new FileSystemService();
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await svc.CreateFolderAsync(new Url(dir));
            Assert.IsTrue(Directory.Exists(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir);
        }
    }

    [TestMethod]
    public async Task MoveAsync_File_MovesFile()
    {
        var svc = new FileSystemService();
        var src = CreateTempFile("move me");
        var dst = Path.GetTempFileName();
        File.Delete(dst);
        try
        {
            await svc.MoveAsync(new Url(src), new Url(dst), true);
            Assert.IsFalse(File.Exists(src));
            Assert.IsTrue(File.Exists(dst));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [TestMethod]
    public async Task CopyAsync_File_CopiesFile()
    {
        var svc = new FileSystemService();
        var src = CreateTempFile("copy me");
        var dst = Path.GetTempFileName();
        File.Delete(dst);
        try
        {
            await svc.CopyAsync(new Url(src), new Url(dst), true);
            Assert.IsTrue(File.Exists(src));
            Assert.IsTrue(File.Exists(dst));
            Assert.AreEqual(File.ReadAllText(src), File.ReadAllText(dst));
        }
        finally
        {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }

    [TestMethod]
    public async Task MoveAsync_Folder_MovesFolderAndContents()
    {
        var svc = new FileSystemService();
        var src = CreateTempFolder();
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await svc.MoveAsync(new Url(src), new Url(dst), false);
            Assert.IsFalse(Directory.Exists(src));
            Assert.IsTrue(Directory.Exists(dst));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(dst, "a.txt")));
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public async Task MoveAsync_Folder_TargetExistsWithoutOverwrite_Throws()
    {
        var svc = new FileSystemService();
        var src = CreateTempFolder();
        var dst = CreateTempFolder();
        try
        {
            await Assert.ThrowsExactlyAsync<IOException>(() => svc.MoveAsync(new Url(src), new Url(dst), false));
            Assert.IsTrue(Directory.Exists(src));
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public async Task CopyAsync_Folder_CopiesNestedContents()
    {
        var svc = new FileSystemService();
        var src = CreateTempFolder();
        var subDir = Path.Combine(src, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        File.WriteAllText(Path.Combine(subDir, "b.txt"), "b");
        var dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await svc.CopyAsync(new Url(src), new Url(dst), false);
            Assert.IsTrue(Directory.Exists(src));
            Assert.AreEqual("a", File.ReadAllText(Path.Combine(dst, "a.txt")));
            Assert.AreEqual("b", File.ReadAllText(Path.Combine(dst, "sub", "b.txt")));
        }
        finally
        {
            if (Directory.Exists(src)) Directory.Delete(src, true);
            if (Directory.Exists(dst)) Directory.Delete(dst, true);
        }
    }

    [TestMethod]
    public void GetAvailableDiskSpace_Throws()
    {
        var svc = new FileSystemService();
        try
        {
            svc.GetAvailableDiskSpace();
            Assert.Fail("Expected NotImplementedException");
        }
        catch (NotImplementedException) { }
    }
}
