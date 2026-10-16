using iConsole.Data;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Services;

[TestClass]
public class MemoryDriveServiceTest
{
    [TestMethod]
    public async Task WriteFile()
    {
        var memoryDrive = new MemoryDriveService();
        var inputArray = new byte[65536];
        new Random().NextBytes(inputArray);
        var inputStream = new MemoryStream(inputArray);
        await memoryDrive.WriteFileAsync("memdrive://a.b", inputStream);
        var memFile = await memoryDrive.ReadFileAsync("memdrive://a.b");
        Assert.IsNotNull(memFile);
        CollectionAssert.AreEqual(inputArray, memFile.Content);
    }

    [TestMethod]
    public void GetAvailableDiskSpace()
    {
        var memoryDrive = new MemoryDriveService();
        var availableDiskSpace = memoryDrive.GetAvailableDiskSpace();
        Assert.IsTrue(availableDiskSpace.Contains(" GB"));
    }

    [TestMethod]
    public async Task NestedFolder_CreateWriteReadRoundTrip()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/b/"));

        var data = new byte[] { 1, 2, 3 };
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/b/file.txt"), new MemoryStream(data));

        var file = await memoryDrive.ReadFileAsync(new Url("memdrive://a/b/file.txt"));
        Assert.IsNotNull(file);
        CollectionAssert.AreEqual(data, file.Content);
    }

    [TestMethod]
    public async Task GetFolder_ResolvesNestedFolder()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/b/"));

        var folder = memoryDrive.GetFolder(new Url("memdrive://a/b/"));

        Assert.IsNotNull(folder);
        Assert.AreEqual("b", folder.FolderName);
    }

    [TestMethod]
    public async Task MoveAsync_RenamesNestedFile()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/old.txt"), new MemoryStream([9]));

        await memoryDrive.MoveAsync(new Url("memdrive://a/old.txt"), new Url("memdrive://a/new.txt"), false);

        Assert.IsFalse(memoryDrive.ExistsFile(new Url("memdrive://a/old.txt")));
        Assert.IsTrue(memoryDrive.ExistsFile(new Url("memdrive://a/new.txt")));
    }

    [TestMethod]
    public async Task CopyAsync_CopiesFileContentAndKeepsSource()
    {
        var memoryDrive = new MemoryDriveService();
        var data = new byte[] { 5, 6, 7 };
        await memoryDrive.WriteFileAsync(new Url("memdrive://source.txt"), new MemoryStream(data));

        await memoryDrive.CopyAsync(new Url("memdrive://source.txt"), new Url("memdrive://copy.txt"), false);

        var copy = await memoryDrive.ReadFileAsync(new Url("memdrive://copy.txt"));
        Assert.IsNotNull(copy);
        CollectionAssert.AreEqual(data, copy.Content);
        Assert.IsTrue(memoryDrive.ExistsFile(new Url("memdrive://source.txt")));
    }

    [TestMethod]
    public async Task DeleteFolder_NonRecursive_RefusesWhenNotEmpty()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/file.txt"), new MemoryStream([1]));

        var result = memoryDrive.DeleteFolder(new Url("memdrive://a/"), recursive: false, moveToRecycleBin: false);

        Assert.IsFalse(result);
        Assert.IsTrue(memoryDrive.ExistsFolder(new Url("memdrive://a/")));
    }

    [TestMethod]
    public async Task DeleteFolder_Recursive_DeletesNonEmptyFolder()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/file.txt"), new MemoryStream([1]));

        var result = memoryDrive.DeleteFolder(new Url("memdrive://a/"), recursive: true, moveToRecycleBin: false);

        Assert.IsTrue(result);
        Assert.IsFalse(memoryDrive.ExistsFolder(new Url("memdrive://a/")));
    }

    [TestMethod]
    public async Task CopyAsync_FolderIsDeepClonedAndSourceUntouched()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/file.txt"), new MemoryStream([4, 2]));

        await memoryDrive.CopyAsync(new Url("memdrive://a/"), new Url("memdrive://acopy/"), false);

        var copiedFile = await memoryDrive.ReadFileAsync(new Url("memdrive://acopy/file.txt"));
        Assert.IsNotNull(copiedFile);
        CollectionAssert.AreEqual(new byte[] { 4, 2 }, copiedFile.Content);
        Assert.IsTrue(memoryDrive.ExistsFile(new Url("memdrive://a/file.txt")));
    }

    /// <summary>
    /// The copy dialog hands over the target as typed, without a trailing slash. The copied folder
    /// must still be a folder: its URL used to keep the missing slash, so it parsed as a file and the
    /// panel listed it as one, could not enter it, and a later copy of it failed.
    /// </summary>
    [TestMethod]
    public async Task CopyFolderToATargetWithoutATrailingSlashStaysAFolder()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/file.txt"), new MemoryStream([1]));

        await memoryDrive.CopyAsync(new Url("memdrive://a/"), new Url("memdrive://acopy"), false);

        var copy = memoryDrive.GetFolder(new Url("memdrive://acopy/"));
        Assert.IsNotNull(copy);
        Assert.AreEqual(PathType.Folder, copy.Url.PathType);
        Assert.IsTrue(memoryDrive.ExistsFile(new Url("memdrive://acopy/file.txt")));
    }

    [TestMethod]
    public async Task MoveFolderToATargetWithoutATrailingSlashStaysAFolder()
    {
        var memoryDrive = new MemoryDriveService();
        await memoryDrive.CreateFolderAsync(new Url("memdrive://a/"));
        await memoryDrive.WriteFileAsync(new Url("memdrive://a/file.txt"), new MemoryStream([1]));

        await memoryDrive.MoveAsync(new Url("memdrive://a/"), new Url("memdrive://moved"), false);

        var moved = memoryDrive.GetFolder(new Url("memdrive://moved/"));
        Assert.IsNotNull(moved);
        Assert.AreEqual(PathType.Folder, moved.Url.PathType);
        Assert.IsTrue(memoryDrive.ExistsFile(new Url("memdrive://moved/file.txt")));
    }
}
