using iConsole.Data;

namespace iConsole.Test.Data;

/// <summary>
/// A file's extension decides how it is read (the viewer, the preview, the document index), so it must be the same
/// whichever drive the file is on. Each remote scheme is checked with the same Office document.
/// </summary>
[TestClass]
public class UrlExtensionTest
{
    [Timeout(60_000)]
    [TestMethod]
    [DataRow("smb://host/Share/report.docx")]
    [DataRow("smb://user@host/Share/dir/report.docx")]
    [DataRow("ssh://user@host:/home/user/report.docx")]
    [DataRow("ftp://host/dir/report.docx")]
    [DataRow("dav://host/dir/report.docx")]
    [DataRow("davs://host/dir/report.docx")]
    [DataRow("s3://AKIAEXAMPLE@photos/2026/report.docx")]
    [DataRow("/home/user/dir/report.docx")]
    public void FileOnAnyDrive_HasItsExtension(string address)
    {
        var url = new Url(address);

        Assert.AreEqual(PathType.File, url.PathType, address);
        Assert.AreEqual("docx", url.Extension, address);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("smb://host/Share/dir/")]
    [DataRow("smb://host/Share/dir.docx/")]
    public void FolderOnAShare_HasNoExtension(string address)
    {
        // A folder named like a file is still a folder, and must not be read as one.
        var url = new Url(address);

        Assert.AreEqual(string.Empty, url.Extension, address);
    }
}
