using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using System.IO.Compression;

namespace iTerminal.Core.Test.Model.ContentModels;

[TestClass]
public class FileSystemEntryTest
{
    [TestMethod]
    public void Constructor_FromUrlString_SetsUrl()
    {
        var entry = new FileSystemEntry(@"c:\windows");
        Assert.IsNotNull(entry.Url);
    }

    [TestMethod]
    public void Constructor_FromUrlString_SetsFileSize()
    {
        var entry = new FileSystemEntry(@"c:\windows");
        Assert.IsNotNull(entry.FileSize);
    }

    [TestMethod]
    public void Constructor_FromNameAndUrl_SetsDisplayName()
    {
        var entry = new FileSystemEntry("MyAlias", @"c:\windows");
        Assert.IsNotNull(entry.Url);
    }

    [TestMethod]
    public void Constructor_FromUrl_SetsUrl()
    {
        var url = new Url(@"c:\windows");
        var entry = new FileSystemEntry(url);
        Assert.AreEqual(url, entry.Url);
    }

    [TestMethod]
    public void FileSystemEntry_IsIEntry()
    {
        var entry = new FileSystemEntry(@"c:\windows");
        Assert.IsInstanceOfType(entry, typeof(IEntry));
    }

    [TestMethod]
    public void TwoEntries_WithSameUrl_AreEqual()
    {
        var a = new FileSystemEntry(@"c:\windows");
        var b = new FileSystemEntry(@"c:\windows");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void TwoEntries_WithDifferentUrls_AreNotEqual()
    {
        var a = new FileSystemEntry(@"c:\windows");
        var b = new FileSystemEntry(@"c:\temp");
        Assert.AreNotEqual(a, b);
    }
}

[TestClass]
public class EntryBaseFormattingTest
{
    [TestMethod]
    public void FormatItemName_ForFile_KeepsColumnLayout()
    {
        var entry = new FileSystemEntry("LongDisplayName.txt", @"c:\temp\LongDisplayName.txt")
        {
            FileSize = "1234567",
            Date = "2025-05-15",
            Time = "12:34"
        };

        var result = entry.FormatItemName(33, false, new ColorScheme(), VisualState.Normal);

        Assert.AreEqual("LongDis}123456│2025-05-15│12:34", result.Text);
    }

    [TestMethod]
    public void FormatItemName_ForFolder_AppliesItalicToWholeRow()
    {
        var entry = new FileSystemEntry("Docs", @"c:\temp\Docs\")
        {
            FileSize = "Folder",
            Date = "2025-05-15",
            Time = "12:34"
        };

        var result = entry.FormatItemName(30, false, new ColorScheme(), VisualState.Normal);

        Assert.IsTrue(result.String.All(conxel => conxel.IsItalic));
    }
}

[TestClass]
public class ArchiveEntryTest
{
    [TestMethod]
    public void Constructor_FromDisplayNameAndUrl_SetsUrl()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.IsNotNull(entry.Url);
    }

    [TestMethod]
    public void Constructor_FromDisplayNameAndUrl_NullZipEntry()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.IsNull(entry.ZipArchiveEntry);
    }

    [TestMethod]
    public void Exists_WithNullZipEntry_ReturnsFalse()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.IsFalse(entry.Exists);
    }

    [TestMethod]
    public void FileSize_WithNullZipEntry_ReturnsEmpty()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.AreEqual(string.Empty, entry.FileSize);
    }

    [TestMethod]
    public void Date_WithNullZipEntry_ReturnsEmpty()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.AreEqual(string.Empty, entry.Date);
    }

    [TestMethod]
    public void GetName_WithNullZipEntry_ReturnsEmpty()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.AreEqual(string.Empty, entry.GetName(20));
    }

    [TestMethod]
    public void GetFileSize_WithNullZipEntry_ReturnsEmpty()
    {
        var entry = new ArchiveEntry("readme.txt", @"c:\archive.zip");
        Assert.AreEqual(string.Empty, entry.GetFileSize(10));
    }

    [TestMethod]
    public void ArchiveEntry_WithZipEntry_ExistsIsTrue()
    {
        var tempZip = Path.GetTempFileName() + ".zip";
        try
        {
            using (var zip = ZipFile.Open(tempZip, ZipArchiveMode.Create))
            {
                zip.CreateEntry("hello.txt");
            }

            using var archive = ZipFile.OpenRead(tempZip);
            var zipEntry = archive.Entries[0];
            var entry = new ArchiveEntry(zipEntry);

            Assert.IsTrue(entry.Exists);
            Assert.AreEqual(zipEntry, entry.ZipArchiveEntry);
        }
        finally
        {
            if (File.Exists(tempZip)) File.Delete(tempZip);
        }
    }
}

[TestClass]
public class MemoryDriveEntryTest
{
    [TestMethod]
    public void Constructor_FromMemoryFile_SetsUrl()
    {
        var file = new MemoryFile("test.txt", new MemoryStream([1, 2, 3]));
        var entry = new MemoryDriveEntry(file);
        Assert.IsNotNull(entry.Url);
    }

    [TestMethod]
    public void Constructor_FromMemoryFile_SetsFileSize()
    {
        var file = new MemoryFile("test.txt", new MemoryStream([1, 2, 3]));
        var entry = new MemoryDriveEntry(file);
        Assert.IsFalse(string.IsNullOrEmpty(entry.FileSize));
    }

    [TestMethod]
    public void Constructor_FromMemoryFile_SetsDate()
    {
        var file = new MemoryFile("test.txt", new MemoryStream([1, 2, 3]));
        var entry = new MemoryDriveEntry(file);
        Assert.IsFalse(string.IsNullOrEmpty(entry.Date));
    }

    [TestMethod]
    public void Constructor_FromMemoryFolder_SetsFileSizeToFolder()
    {
        var folder = new MemoryFolder(new Url("memdrive://subfolder/"));
        var entry = new MemoryDriveEntry(folder);
        Assert.AreEqual("Folder", entry.FileSize);
    }

    [TestMethod]
    public void Constructor_FromName_SetsUrl()
    {
        var entry = new MemoryDriveEntry("memdrive://test.txt");
        Assert.IsNotNull(entry.Url);
    }
}

[TestClass]
public class MemoryFileTest
{
    [TestMethod]
    public void Constructor_FromStream_StoresContent()
    {
        var data = new byte[] { 10, 20, 30 };
        var file = new MemoryFile("data.bin", new MemoryStream(data));
        CollectionAssert.AreEqual(data, file.Content);
    }

    [TestMethod]
    public void Constructor_FromArray_StoresContent()
    {
        var data = new byte[] { 1, 2, 3 };
        var file = new MemoryFile("data.bin", data);
        CollectionAssert.AreEqual(data, file.Content);
    }

    [TestMethod]
    public void Constructor_SetsFileName()
    {
        var file = new MemoryFile("hello.txt", new MemoryStream());
        Assert.AreEqual("hello.txt", file.FileName);
    }

    [TestMethod]
    public void Rename_ReturnsFalse()
    {
        var result = MemoryFile.Rename(new Url("memdrive://a.txt"), new Url("memdrive://b.txt"), true);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void Constructor_NullArray_ContentIsNull()
    {
        var file = new MemoryFile("null.bin", (byte[]?)null);
        Assert.IsNull(file.Content);
    }
}

[TestClass]
public class MemoryFolderTest
{
    [TestMethod]
    public void Constructor_SetsUrl()
    {
        var url = new Url("memdrive://testfolder/");
        var folder = new MemoryFolder(url);
        Assert.AreEqual(url, folder.Url);
    }

    [TestMethod]
    public async Task WriteFileAsync_StoresFile()
    {
        var folder = new MemoryFolder(new Url("memdrive://root/"));
        var fileUrl = new Url("memdrive://hello.txt");
        await folder.WriteFileAsync(fileUrl, new MemoryStream([1, 2]));

        var result = await folder.ReadFileAsync(fileUrl);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public async Task ReadFileAsync_ReturnsNull_WhenNotFound()
    {
        var folder = new MemoryFolder(new Url("memdrive://root/"));
        var result = await folder.ReadFileAsync(new Url("memdrive://missing.txt"));
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task DeleteFile_ReturnsTrueWhenFound()
    {
        var folder = new MemoryFolder(new Url("memdrive://root/"));
        var fileUrl = new Url("memdrive://todelete.txt");
        await folder.WriteFileAsync(fileUrl, new MemoryStream([1]));

        var result = folder.DeleteFile(fileUrl);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void DeleteFile_ReturnsFalse_WhenNotFound()
    {
        var folder = new MemoryFolder(new Url("memdrive://root/"));
        var result = folder.DeleteFile(new Url("memdrive://notexist.txt"));
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void FolderName_MatchesDisplayName()
    {
        var url = new Url("memdrive://myfolder/");
        var folder = new MemoryFolder(url);
        Assert.IsNotNull(folder.FolderName);
    }
}
