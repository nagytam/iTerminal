using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// GZIP, BZIP2, XZ and TAR.XZ as formats: the names that select them, which ones hold one file and
/// which replace an existing archive, and how the service routes and refuses them.
/// </summary>
[TestClass]
public class ArchiveFormatSingleStreamTest
{
    [TestMethod]
    [DataRow("notes.gz", "GZIP")]
    [DataRow("notes.bz2", "BZIP2")]
    [DataRow("notes.xz", "XZ")]
    [DataRow("backup.tar.xz", "TAR.XZ")]
    [DataRow("backup.txz", "TAR.XZ")]
    [DataRow("backup.tar.gz", "TAR.GZ")]
    [DataRow("backup.tar.bz2", "TAR.BZ2")]
    public void TheEndingSelectsTheFormat(string name, string expected) =>
        Assert.AreEqual(expected, ArchiveFormat.FromFileName(name)?.Name);

    [TestMethod]
    public void TheSingleStreamFormatsHoldOneFileAndReplaceTheTarget()
    {
        foreach (var format in new[] { ArchiveFormat.Gzip, ArchiveFormat.Bzip2, ArchiveFormat.Xz })
        {
            Assert.IsTrue(format.OneFileOnly, format.Name);
            Assert.IsFalse(format.AddsToExisting, format.Name);
        }

        Assert.IsFalse(ArchiveFormat.TarXz.OneFileOnly, "a TAR.XZ holds a tree");
        Assert.IsFalse(ArchiveFormat.TarXz.AddsToExisting, "a compressed stream cannot take new entries");
    }

    [TestMethod]
    public void TheFormatsThatAddStillAdd()
    {
        foreach (var format in new[] { ArchiveFormat.Zip, ArchiveFormat.SevenZip, ArchiveFormat.TarGz, ArchiveFormat.TarBz2, ArchiveFormat.Tar })
        {
            Assert.IsTrue(format.AddsToExisting, format.Name);
            Assert.IsFalse(format.OneFileOnly, format.Name);
        }
    }

    [TestMethod]
    public void TheNewFormatsAreOfferedInTheList()
    {
        var all = ArchiveFormat.All.ToList();
        CollectionAssert.Contains(all, ArchiveFormat.Gzip);
        CollectionAssert.Contains(all, ArchiveFormat.Bzip2);
        CollectionAssert.Contains(all, ArchiveFormat.Xz);
        CollectionAssert.Contains(all, ArchiveFormat.TarXz);
        Assert.AreEqual(ArchiveFormat.Zip, all[0], "the default stays ZIP");
    }

    [TestMethod]
    public void ApplyingAFormatReplacesTheEnding()
    {
        Assert.AreEqual("backup.tar.xz", ArchiveFormat.TarXz.ApplyExtension("backup.zip"));
        Assert.AreEqual("notes.xz", ArchiveFormat.Xz.ApplyExtension("notes.tar.gz"));
        Assert.AreEqual("notes.gz", ArchiveFormat.Gzip.ApplyExtension("notes.gz"));
    }

    [TestMethod]
    public void WithoutSevenZipXzIsNamedAsUnavailable()
    {
        var service = new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => null));

        var reason = service.WhyUnavailable(ArchiveFormat.Xz);
        Assert.AreEqual("XZ needs 7-Zip installed", reason);
        Assert.AreEqual("TAR.XZ needs 7-Zip installed", service.WhyUnavailable(ArchiveFormat.TarXz));
        Assert.IsNull(service.WhyUnavailable(ArchiveFormat.Gzip), "GZIP is written in-process");
        CollectionAssert.DoesNotContain(service.AvailableFormats.ToList(), ArchiveFormat.Xz);
        CollectionAssert.Contains(service.AvailableFormats.ToList(), ArchiveFormat.Gzip);
    }

    [TestMethod]
    [DataRow("GZIP")]
    [DataRow("BZIP2")]
    public async Task AGzipOrBzip2OfTwoItemsIsRefused(string formatName)
    {
        var format = ArchiveFormat.All.Single(f => f.Name == formatName);
        var service = new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => null));
        var items = new List<ArchiveItem>
        {
            new("a.txt", false, 1, DateTime.Now, null, (s, ct) => s.WriteAsync(new byte[] { 1 }, 0, 1, ct)),
            new("b.txt", false, 1, DateTime.Now, null, (s, ct) => s.WriteAsync(new byte[] { 2 }, 0, 1, ct)),
        };

        var error = await AsyncAssert.ThrowsAsync<NotSupportedException>(() =>
            service.CompressAsync(Path.Combine(Path.GetTempPath(), "never.x"), items, new CompressionOptions(format, 6), null, CancellationToken.None));
        Assert.AreEqual($"{formatName} holds one file: compress one file, or choose an archive format.", error.Message);
    }

    [TestMethod]
    public async Task AFolderIsRefusedByAOneFileFormat()
    {
        var service = new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => null));
        var items = new List<ArchiveItem> { new("docs/", true, 0, DateTime.Now) };

        var error = await AsyncAssert.ThrowsAsync<NotSupportedException>(() =>
            service.CompressAsync(Path.Combine(Path.GetTempPath(), "never.gz"), items, new CompressionOptions(ArchiveFormat.Gzip, 6), null, CancellationToken.None));
        Assert.AreEqual("GZIP holds one file: compress one file, or choose an archive format.", error.Message);
    }
}
