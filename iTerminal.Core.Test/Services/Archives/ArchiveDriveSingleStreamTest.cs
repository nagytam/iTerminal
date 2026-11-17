using System.IO.Compression;
using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using iConsole.Data;
using FileIO = System.IO.File;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// A lone .gz and a lone .xz browse as one file named after the archive; a .tar.xz is not browsed
/// at all (it is unpacked, not opened), and Enter leaves it to its file association.
/// </summary>
[TestClass]
public class ArchiveDriveSingleStreamTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "ArchiveDriveSingleStreamTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
        }
    }

    private static void RequireSevenZip()
    {
        if (SevenZipArchiveWriter.FindExecutable() is null)
        {
            Assert.Inconclusive("7-Zip is not installed on this machine.");
        }
    }

    private static ArchiveDriveService Browser() => new(new ExternalProcessRunner());

    private static ArchiveDriveService BrowserWithoutSevenZip() => new(new ExternalProcessRunner(), () => null);

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALoneGzBrowsesAsOneFileNamedAfterTheArchive()
    {
        var archive = Path.Combine(_root, "notes.txt.gz");
        using (var file = FileIO.Create(archive))
        using (var gzip = new GZipOutputStream(file))
        {
            gzip.Write(Encoding.UTF8.GetBytes("hello from gzip"));
        }

        var service = BrowserWithoutSevenZip();
        var rows = await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("notes.txt", rows[0].DisplayName, "named after the archive, without .gz");
        using var buffer = new MemoryStream();
        await service.StreamToAsync(rows[0].Url, buffer, null, CancellationToken.None);
        Assert.AreEqual("hello from gzip", Encoding.UTF8.GetString(buffer.ToArray()));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ALoneXzBrowsesAsOneFileNamedAfterTheArchive()
    {
        RequireSevenZip();
        var source = Path.Combine(_root, "notes.txt");
        FileIO.WriteAllText(source, "hello from xz");
        var archive = Path.Combine(_root, "notes.txt.xz");
        await new ArchiveCompressionService(new ExternalProcessRunner()).CompressAsync(
            archive, [new("notes.txt", false, new FileInfo(source).Length, DateTime.Now, source)],
            new CompressionOptions(ArchiveFormat.Xz, 6), null, CancellationToken.None);

        var service = Browser();
        var rows = await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None);

        Assert.AreEqual(1, rows.Count);
        Assert.AreEqual("notes.txt", rows[0].DisplayName, "named after the archive, whatever 7-Zip reports");
        using var buffer = new MemoryStream();
        await service.StreamToAsync(rows[0].Url, buffer, null, CancellationToken.None);
        Assert.AreEqual("hello from xz", Encoding.UTF8.GetString(buffer.ToArray()));
    }

    [TestMethod]
    public void ATarXzIsNotBrowsedButALoneXzIs()
    {
        var service = BrowserWithoutSevenZip();

        Assert.IsFalse(ArchiveDriveService.LooksBrowsable("backup.tar.xz"));
        Assert.IsFalse(service.CanBrowse("backup.tar.xz"));
        Assert.IsTrue(ArchiveDriveService.LooksBrowsable("notes.xz"));
        Assert.IsTrue(ArchiveDriveService.LooksBrowsable("notes.gz"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATarXzIsRefusedWithAMessageNotPassedTo7Zip()
    {
        var archive = Path.Combine(_root, "backup.tar.xz");
        FileIO.WriteAllBytes(archive, [0x00]);

        var error = await AsyncAssert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserWithoutSevenZip().ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None));
        StringAssert.Contains(error.Message, "not browsed");
    }
}
