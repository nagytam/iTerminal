using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using iConsole.Data;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;
using FileIO = System.IO.File;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// The <c>archive://</c> drive: archives in every format browsed folder by folder and read back
/// entry by entry. The archives are written by Alt+F5's own writers, or by hand where a test needs
/// a shape those writers never produce.
/// </summary>
[TestClass]
public class ArchiveDriveServiceTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "ArchiveDriveServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "docs", "deep"));
        FileIO.WriteAllText(Path.Combine(_root, "src", "a.txt"), "alpha");
        FileIO.WriteAllText(Path.Combine(_root, "src", "docs", "b.md"), "beta");
        FileIO.WriteAllText(Path.Combine(_root, "src", "docs", "deep", "c.txt"), "gamma");
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

    private static ArchiveDriveService Service(Func<string?>? sevenZip = null) =>
        new(new FakeProcessRunner(), sevenZip ?? (() => null));

    private string Src(params string[] parts) => Path.Combine([_root, "src", .. parts]);

    private List<ArchiveItem> Items() =>
    [
        new("a.txt", false, 5, DateTime.Now, Src("a.txt")),
        new("docs/", true, 0, DateTime.Now, Src("docs")),
        new("docs/b.md", false, 4, DateTime.Now, Src("docs", "b.md")),
        new("docs/deep/", true, 0, DateTime.Now, Src("docs", "deep")),
        new("docs/deep/c.txt", false, 5, DateTime.Now, Src("docs", "deep", "c.txt")),
    ];

    private async Task<string> Pack(string name, ArchiveFormat format)
    {
        var path = Path.Combine(_root, name);
        await ManagedArchiveWriter.WriteAsync(path, Items(), new CompressionOptions(format, format.DefaultLevel), null, CancellationToken.None);
        return path;
    }

    private static string[] Names(IReadOnlyList<iConsole.Interfaces.IEntry> rows) => [.. rows.Select(r => r.DisplayName)];

    private static async Task<string> Read(ArchiveDriveService service, Url url)
    {
        using var buffer = new MemoryStream();
        await service.StreamToAsync(url, buffer, null, CancellationToken.None);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    [TestMethod]
    [DataRow("x.zip", "ZIP")]
    [DataRow("x.tar", "TAR")]
    [DataRow("x.tar.gz", "TAR.GZ")]
    [DataRow("x.tar.bz2", "TAR.BZ2")]
    public async Task AnArchiveIsBrowsedFolderByFolderAndReadBack(string name, string format)
    {
        var archive = await Pack(name, ArchiveFormat.All.Single(f => f.Name == format));
        var service = Service();

        var top = await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "docs", "a.txt" }, Names(top), "folders first");
        Assert.AreEqual(PathType.Folder, top[0].Url.PathType);

        var docs = await service.ListAsync(top[0].Url, CancellationToken.None);
        CollectionAssert.AreEqual(new[] { "deep", "b.md" }, Names(docs));

        Assert.AreEqual("gamma", await Read(service, ArchiveDriveUrl.Build(archive, "docs/deep/c.txt", isFolder: false)));
        Assert.AreEqual("b.md", docs[1].Url.Name, "a copy names its target after the entry");
        Assert.AreEqual(4, ((VirtualDriveEntry)docs[1]).Size);
    }

    [TestMethod]
    public async Task FoldersAZipOnlyImpliesAreListedToo()
    {
        // Most ZIPs store no entries for their folders, only "x/y/z.txt".
        var archive = Path.Combine(_root, "bare.zip");
        using (var zip = new ZipOutputStream(FileIO.Create(archive)))
        {
            zip.PutNextEntry(new ZipEntry("x/y/z.txt"));
            zip.Write(Encoding.UTF8.GetBytes("zed"));
            zip.CloseEntry();
        }

        var service = Service();

        CollectionAssert.AreEqual(new[] { "x" }, Names(await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None)));
        CollectionAssert.AreEqual(new[] { "y" }, Names(await service.ListAsync(ArchiveDriveUrl.Build(archive, "x"), CancellationToken.None)));
        Assert.IsTrue(service.ExistsFolder(ArchiveDriveUrl.Build(archive, "x/y")));
        Assert.IsTrue(service.ExistsFile(ArchiveDriveUrl.Build(archive, "x/y/z.txt", isFolder: false)));
        Assert.IsFalse(service.ExistsFile(ArchiveDriveUrl.Build(archive, "x/nope.txt", isFolder: false)));
    }

    [TestMethod]
    public async Task AFolderIsWalkedForACopyOut()
    {
        var archive = await Pack("x.tar.gz", ArchiveFormat.TarGz);

        var children = Service().ListChildren(ArchiveDriveUrl.Build(archive, "docs")).Select(u => u.FullUrl).ToArray();

        CollectionAssert.AreEqual(
            new[] { ArchiveDriveUrl.Build(archive, "docs/deep").FullUrl, ArchiveDriveUrl.Build(archive, "docs/b.md", isFolder: false).FullUrl },
            children);
    }

    [TestMethod]
    public async Task AnArchiveRewrittenSinceItWasListedIsReadAgain()
    {
        var archive = await Pack("x.zip", ArchiveFormat.Zip);
        var service = Service();
        Assert.AreEqual(2, (await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None)).Count);

        await ManagedArchiveWriter.WriteAsync(archive, [new ArchiveItem("later.txt", false, 5, DateTime.Now, Src("a.txt"))],
            new CompressionOptions(ArchiveFormat.Zip, 6), null, CancellationToken.None);
        FileIO.SetLastWriteTimeUtc(archive, DateTime.UtcNow.AddMinutes(1));

        CollectionAssert.Contains(Names(await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None)), "later.txt");
    }

    [TestMethod]
    public async Task AnEncryptedEntrySaysHowToGetAtIt()
    {
        var archive = Path.Combine(_root, "secret.zip");
        await ManagedArchiveWriter.WriteAsync(archive, Items(), new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret"), null, CancellationToken.None);
        var service = Service();

        CollectionAssert.Contains(Names(await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None)), "a.txt", "a ZIP's names are never encrypted");
        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => Read(service, ArchiveDriveUrl.Build(archive, "a.txt", isFolder: false)));
        StringAssert.Contains(exception.Message, "Shift+F5");
    }

    [TestMethod]
    public void TheDriveIsReadOnly()
    {
        var service = Service();

        Assert.IsTrue(service.IsReadOnly);
        var exception = Assert.ThrowsExactly<NotSupportedException>(() => service.DeleteFile(ArchiveDriveUrl.Build("x.zip", "a", false), false));
        StringAssert.Contains(exception.Message, "Alt+F5");
    }

    [TestMethod]
    public void SevenZipsTechnicalListingIsParsed()
    {
        var listing = string.Join(Environment.NewLine,
            "Path = docs",
            "Folder = +",
            "Size = 0",
            "Modified = 2024-05-06 07:08:10",
            "",
            "Path = docs\\b.md",
            "Folder = -",
            "Size = 4",
            "Modified = 2024-05-06 07:08:11",
            "Path = a.txt",
            "Size = 5",
            "Attributes = A",
            "");

        var entries = ArchiveDriveService.ParseSevenZipListing(listing);

        CollectionAssert.AreEqual(new[] { "docs", "docs\\b.md", "a.txt" }, entries.Select(e => e.RawPath).ToArray());
        Assert.IsTrue(entries[0].IsFolder);
        Assert.AreEqual(4, entries[1].Size);
        Assert.AreEqual(new DateTime(2024, 5, 6, 7, 8, 11), entries[1].Modified);
        Assert.IsFalse(entries[2].IsFolder, "no blank line between two entries, still two entries");
    }

    [TestMethod]
    public async Task ARealSevenZipArchiveIsBrowsedAndRead()
    {
        var writer = new SevenZipArchiveWriter(new ExternalProcessRunner());
        if (!writer.IsAvailable)
        {
            Assert.Inconclusive("7-Zip is not installed on this machine.");
        }

        var archive = Path.Combine(_root, "real.7z");
        await writer.WriteAsync(archive, Items(), new CompressionOptions(ArchiveFormat.SevenZip, 5), null, CancellationToken.None);
        var service = new ArchiveDriveService(new ExternalProcessRunner());

        CollectionAssert.AreEqual(new[] { "docs", "a.txt" }, Names(await service.ListAsync(ArchiveDriveUrl.Build(archive), CancellationToken.None)));
        CollectionAssert.AreEqual(new[] { "deep", "b.md" }, Names(await service.ListAsync(ArchiveDriveUrl.Build(archive, "docs"), CancellationToken.None)));
        Assert.AreEqual("gamma", await Read(service, ArchiveDriveUrl.Build(archive, "docs/deep/c.txt", isFolder: false)));

        // Names hidden behind a password: said in words, and nothing waits on a console.
        var locked = Path.Combine(_root, "locked.7z");
        await writer.WriteAsync(locked, Items(), new CompressionOptions(ArchiveFormat.SevenZip, 5, "s3cret", EncryptNames: true), null, CancellationToken.None);
        var exception = await Assert.ThrowsExactlyAsync<IOException>(() => service.ListAsync(ArchiveDriveUrl.Build(locked), CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(30));
        StringAssert.Contains(exception.Message, "password");
    }

    [TestMethod]
    public void TheArchiveUrlKeepsALocalPathInOneSegment()
    {
        var url = ArchiveDriveUrl.Build(@"C:\dl\my photos.zip", "2024/may", isFolder: true);

        Assert.AreEqual(ContainerType.Archive, url.ContainerType);
        Assert.AreEqual(PathType.Folder, url.PathType);
        Assert.AreEqual((@"C:\dl\my photos.zip", "2024/may"), ArchiveDriveUrl.Parse(url));
        Assert.AreEqual(ArchiveDriveUrl.Build(@"C:\dl\my photos.zip").FullUrl, url.RootUrl.FullUrl);
    }
}
