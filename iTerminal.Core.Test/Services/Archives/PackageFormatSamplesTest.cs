using iConsole.Data;
using iConsole.Interfaces;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// The package and archive examples in Test/FileTypes, linked into this project. The ZIP-based packages (JAR, APK,
/// NUPKG, VSIX) are read by the managed ZIP reader; DEB, RPM, CHM, ARJ and LZMA go to 7-Zip, so those tests are
/// inconclusive where 7-Zip is not installed.
/// </summary>
[TestClass]
public class PackageFormatSamplesTest
{
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "FileTypes", name);

    private static ArchiveDriveService Browser() => new(new ExternalProcessRunner());

    private static IReadOnlyList<string> Names(IEnumerable<IEntry> rows) => rows.Select(row => row.DisplayName).ToList();

    private static void RequireSevenZip()
    {
        if (SevenZipArchiveWriter.FindExecutable() is null)
        {
            Assert.Inconclusive("7-Zip is not installed on this machine.");
        }
    }

    [TestMethod]
    [DataRow("example.jar")]
    [DataRow("example.apk")]
    [DataRow("example.nupkg")]
    [DataRow("example.vsix")]
    public void ZipBasedPackages_BrowseAndUnpackWithoutSevenZip(string name)
    {
        Assert.IsTrue(ArchiveDriveService.LooksBrowsable(name));
        Assert.IsTrue(new ArchiveDriveService(new ExternalProcessRunner(), () => null).CanBrowse(name));
        Assert.IsTrue(new ArchiveExtractionService(new ExternalProcessRunner(), () => null).IsArchive(name));
        Assert.IsNull(new ArchiveExtractionService(new ExternalProcessRunner(), () => null).WhyCannotExtract(name));
    }

    [TestMethod]
    [DataRow("example.deb")]
    [DataRow("example.rpm")]
    [DataRow("example.chm")]
    [DataRow("example.arj")]
    [DataRow("example.lzma")]
    public void SevenZipPackages_OfferedEvenWithoutSevenZip(string name)
    {
        Assert.IsTrue(ArchiveDriveService.LooksBrowsable(name), "offered even without 7-Zip, so the footer can say what is missing");
        Assert.IsFalse(new ArchiveDriveService(new ExternalProcessRunner(), () => null).CanBrowse(name));
        Assert.IsTrue(new ArchiveExtractionService(new ExternalProcessRunner(), () => null).IsArchive(name));
        Assert.IsNotNull(new ArchiveExtractionService(new ExternalProcessRunner(), () => null).WhyCannotExtract(name));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Jar_ListsItsEntriesAsFoldersAndFiles()
    {
        var rows = await Browser().ListAsync(ArchiveDriveUrl.Build(Sample("example.jar")), CancellationToken.None);

        CollectionAssert.Contains(Names(rows).ToList(), "META-INF");
        CollectionAssert.Contains(Names(rows).ToList(), "example");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Nupkg_ReadsItsNuspecAsText()
    {
        var service = Browser();
        string archive = Sample("example.nupkg");

        var nuspec = await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None);
        Assert.IsTrue(Names(nuspec).Contains("example.nuspec"));
        StringAssert.Contains(await Read(service, ArchiveDriveUrl.Build(archive, "example.nuspec", isFolder: false)), "<id>Example</id>");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Deb_ListsTheDebianArchiveMembers()
    {
        RequireSevenZip();

        var rows = await Browser().ListAsync(ArchiveDriveUrl.Build(Sample("example.deb")), CancellationToken.None);

        CollectionAssert.Contains(Names(rows).ToList(), "debian-binary");
        CollectionAssert.Contains(Names(rows).ToList(), "control.tar.gz");
        CollectionAssert.Contains(Names(rows).ToList(), "data.tar.gz");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rpm_ListsThePayloadThat7ZipReads()
    {
        RequireSevenZip();

        // 7-Zip presents an RPM's payload as one cpio file; the files inside it need a second step.
        var rows = await Browser().ListAsync(ArchiveDriveUrl.Build(Sample("example.rpm")), CancellationToken.None);

        Assert.AreEqual(1, rows.Count);
        StringAssert.EndsWith(rows[0].DisplayName, ".cpio");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chm_ListsTheHelpFilesInside()
    {
        RequireSevenZip();

        var rows = await Browser().ListAsync(ArchiveDriveUrl.Build(Sample("example.chm")), CancellationToken.None);

        CollectionAssert.Contains(Names(rows).ToList(), "html");
        CollectionAssert.Contains(Names(rows).ToList(), "Images");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Arj_ListsTheFilesInside()
    {
        RequireSevenZip();

        var rows = await Browser().ListAsync(ArchiveDriveUrl.Build(Sample("example.arj")), CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "test1.xml", "test2.xml" }, Names(rows).ToList());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Lzma_BrowsesAsItsOneFile()
    {
        RequireSevenZip();

        var rows = await Browser().ListAsync(ArchiveDriveUrl.Build(Sample("example.lzma")), CancellationToken.None);

        Assert.AreEqual(1, rows.Count, "a lone LZMA stream holds one file");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Jar_UnpacksItsEntries()
    {
        string target = Path.Combine(Path.GetTempPath(), "jar-unpack-" + Guid.NewGuid().ToString("N"));
        try
        {
            var extractor = new ArchiveExtractionService(new ExternalProcessRunner(), () => null);

            await extractor.ExtractAsync(Sample("example.jar"), target, new ExtractionOptions(), null, CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(target, "META-INF", "MANIFEST.MF")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "example", "Main.class")));
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }
        }
    }

    private static async Task<string> Read(ArchiveDriveService service, Url url)
    {
        using var buffer = new MemoryStream();
        await service.StreamToAsync(url, buffer, null, CancellationToken.None);
        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
