using iConsole.Data;
using iTerminal.Core.Services;
using System.IO.Compression;
using System.Text;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class ArchiveFileSystemServiceTest
{
    private string _zipPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _zipPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        using var archive = ZipFile.Open(_zipPath, ZipArchiveMode.Create);
        var topEntry = archive.CreateEntry("top.txt");
        using (var writer = new StreamWriter(topEntry.Open()))
        {
            writer.Write("top content");
        }
        var nestedEntry = archive.CreateEntry("sub/nested.txt");
        using (var writer = new StreamWriter(nestedEntry.Open()))
        {
            writer.Write("nested content");
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_zipPath))
        {
            File.Delete(_zipPath);
        }
    }

    private Url EntryUrl(string innerPath) => new(Container.ZIP_ARCHIVE + _zipPath + "@/" + innerPath);

    [TestMethod]
    public void IsReadOnly_ReturnsTrue()
    {
        var service = new ArchiveFileSystemService();
        Assert.IsTrue(service.IsReadOnly);
    }

    [TestMethod]
    public async Task ReadFileAsync_TopLevelEntry_ReturnsContent()
    {
        var service = new ArchiveFileSystemService();
        var file = await service.ReadFileAsync(EntryUrl("top.txt"));
        Assert.IsNotNull(file);
        Assert.AreEqual("top content", Encoding.UTF8.GetString(file.Content!));
    }

    [TestMethod]
    public async Task ReadFileAsync_NestedEntry_ReturnsContent()
    {
        var service = new ArchiveFileSystemService();
        var file = await service.ReadFileAsync(EntryUrl("sub/nested.txt"));
        Assert.IsNotNull(file);
        Assert.AreEqual("nested content", Encoding.UTF8.GetString(file.Content!));
    }

    [TestMethod]
    public async Task ReadFileAsync_MissingEntry_ReturnsNull()
    {
        var service = new ArchiveFileSystemService();
        var file = await service.ReadFileAsync(EntryUrl("missing.txt"));
        Assert.IsNull(file);
    }

    [TestMethod]
    public void ExistsFile_ForExistingEntry_ReturnsTrue()
    {
        var service = new ArchiveFileSystemService();
        Assert.IsTrue(service.ExistsFile(EntryUrl("top.txt")));
    }

    [TestMethod]
    public void ExistsFile_ForMissingEntry_ReturnsFalse()
    {
        var service = new ArchiveFileSystemService();
        Assert.IsFalse(service.ExistsFile(EntryUrl("missing.txt")));
    }

    [TestMethod]
    public void ExistsFolder_ForFolderPrefix_ReturnsTrue()
    {
        var service = new ArchiveFileSystemService();
        Assert.IsTrue(service.ExistsFolder(EntryUrl("sub/")));
    }

    [TestMethod]
    public void ListChildren_AtRoot_ReturnsTopLevelFileAndFolder()
    {
        var service = new ArchiveFileSystemService();
        var children = service.ListChildren(EntryUrl(string.Empty)).ToList();

        Assert.AreEqual(2, children.Count);
        Assert.IsTrue(children.Any(c => c.Name == "top.txt" && c.PathType == PathType.File));
        Assert.IsTrue(children.Any(c => c.Name == "sub" && c.PathType == PathType.Folder));
    }

    [TestMethod]
    public void ListChildren_InSubfolder_ReturnsNestedFile()
    {
        var service = new ArchiveFileSystemService();
        var children = service.ListChildren(EntryUrl("sub/")).ToList();

        Assert.AreEqual(1, children.Count);
        Assert.AreEqual("nested.txt", children[0].Name);
        Assert.AreEqual(PathType.File, children[0].PathType);
    }

    private static void AssertThrowsNotSupported(Action action)
    {
        try
        {
            action();
            Assert.Fail("Expected NotSupportedException");
        }
        catch (NotSupportedException)
        {
        }
    }

    [TestMethod]
    public void WriteFileAsync_Throws()
    {
        var service = new ArchiveFileSystemService();
        AssertThrowsNotSupported(() => service.WriteFileAsync(EntryUrl("new.txt"), new MemoryStream()));
    }

    [TestMethod]
    public void DeleteFile_Throws()
    {
        var service = new ArchiveFileSystemService();
        AssertThrowsNotSupported(() => service.DeleteFile(EntryUrl("top.txt"), false));
    }

    [TestMethod]
    public void DeleteFolder_Throws()
    {
        var service = new ArchiveFileSystemService();
        AssertThrowsNotSupported(() => service.DeleteFolder(EntryUrl("sub/"), true, false));
    }

    [TestMethod]
    public void CreateFolderAsync_Throws()
    {
        var service = new ArchiveFileSystemService();
        AssertThrowsNotSupported(() => service.CreateFolderAsync(EntryUrl("new/")));
    }

    [TestMethod]
    public void MoveAsync_Throws()
    {
        var service = new ArchiveFileSystemService();
        AssertThrowsNotSupported(() => service.MoveAsync(EntryUrl("top.txt"), EntryUrl("renamed.txt"), false));
    }

    [TestMethod]
    public void CopyAsync_Throws()
    {
        var service = new ArchiveFileSystemService();
        AssertThrowsNotSupported(() => service.CopyAsync(EntryUrl("top.txt"), EntryUrl("copy.txt"), false));
    }
}
