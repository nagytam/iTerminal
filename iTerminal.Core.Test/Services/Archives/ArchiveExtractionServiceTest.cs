using System.Text;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Zip;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;
using FileIO = System.IO.File;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// Shift+F5: archives written by Alt+F5's own writers are unpacked and compared with what went in —
/// every format, with and without a password — plus the things an extractor must refuse.
/// </summary>
[TestClass]
public class ArchiveExtractionServiceTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "ArchiveExtractionServiceTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "docs"));
        FileIO.WriteAllText(Path.Combine(_root, "src", "a.txt"), "alpha");
        FileIO.WriteAllText(Path.Combine(_root, "src", "docs", "b.md"), "beta");
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

    private static ArchiveExtractionService Service(Func<string?>? sevenZip = null) =>
        new(new FakeProcessRunner(), sevenZip ?? (() => null));

    private List<ArchiveItem> Items() =>
    [
        new("a.txt", false, 5, new DateTime(2024, 5, 6, 7, 8, 10), Path.Combine(_root, "src", "a.txt")),
        new("docs/", true, 0, DateTime.Now, Path.Combine(_root, "src", "docs")),
        new("docs/b.md", false, 4, DateTime.Now, Path.Combine(_root, "src", "docs", "b.md")),
    ];

    private async Task<string> Pack(string name, CompressionOptions options)
    {
        var path = Path.Combine(_root, name);
        await ManagedArchiveWriter.WriteAsync(path, Items(), options, null, CancellationToken.None);
        return path;
    }

    private string Out(params string[] parts) => Path.Combine([_root, "out", .. parts]);

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("x.zip", "ZIP")]
    [DataRow("x.tar", "TAR")]
    [DataRow("x.tar.gz", "TAR.GZ")]
    [DataRow("x.tar.bz2", "TAR.BZ2")]
    public async Task EveryFormatAlt5WritesUnpacksToWhatWentIn(string name, string format)
    {
        var archiveFormat = ArchiveFormat.All.Single(f => f.Name == format);
        var archive = await Pack(name, new CompressionOptions(archiveFormat, archiveFormat.DefaultLevel));

        var result = await Service().ExtractAsync(archive, Out(), new ExtractionOptions(), null, CancellationToken.None);

        Assert.AreEqual(2, result.Extracted);
        Assert.AreEqual("alpha", FileIO.ReadAllText(Out("a.txt")));
        Assert.AreEqual("beta", FileIO.ReadAllText(Out("docs", "b.md")));
        Assert.AreEqual(new DateTime(2024, 5, 6, 7, 8, 10), FileIO.GetLastWriteTime(Out("a.txt")), "the entry's date is kept");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(ArchiveEncryption.Aes256)]
    [DataRow(ArchiveEncryption.ZipCrypto)]
    public async Task AnEncryptedZipOpensWithItsPasswordAndSaysSoWithoutIt(ArchiveEncryption encryption)
    {
        var archive = await Pack("secret.zip", new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret", encryption));
        var service = Service();

        var missing = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ExtractAsync(archive, Out(), new ExtractionOptions(), null, CancellationToken.None));
        Assert.AreEqual("secret.zip is encrypted: type its password.", missing.Message);

        var wrong = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.ExtractAsync(archive, Out(), new ExtractionOptions("nope"), null, CancellationToken.None));
        Assert.AreEqual("The password does not open secret.zip.", wrong.Message);
        Assert.IsFalse(FileIO.Exists(Out("a.txt")), "a file that failed its check is not left half-written");

        await service.ExtractAsync(archive, Out(), new ExtractionOptions("s3cret"), null, CancellationToken.None);
        Assert.AreEqual("alpha", FileIO.ReadAllText(Out("a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FilesAlreadyThereAreKeptUnlessOverwriting()
    {
        var archive = await Pack("x.zip", new CompressionOptions(ArchiveFormat.Zip, 6));
        Directory.CreateDirectory(Out());
        FileIO.WriteAllText(Out("a.txt"), "mine");

        var kept = await Service().ExtractAsync(archive, Out(), new ExtractionOptions(Overwrite: false), null, CancellationToken.None);
        Assert.AreEqual("mine", FileIO.ReadAllText(Out("a.txt")));
        Assert.AreEqual(new ExtractionResult(1, 1), kept);

        await Service().ExtractAsync(archive, Out(), new ExtractionOptions(Overwrite: true), null, CancellationToken.None);
        Assert.AreEqual("alpha", FileIO.ReadAllText(Out("a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEntryThatLeadsOutsideTheTargetIsRefused()
    {
        // The "zip slip": an entry named ../evil.txt written by a hostile archiver.
        var archive = Path.Combine(_root, "slip.zip");
        using (var zip = new ZipOutputStream(FileIO.Create(archive)))
        {
            foreach (var name in new[] { "../evil.txt", "fine.txt" })
            {
                zip.PutNextEntry(new ZipEntry(name));
                zip.Write(Encoding.UTF8.GetBytes("x"));
                zip.CloseEntry();
            }
        }

        var result = await Service().ExtractAsync(archive, Out(), new ExtractionOptions(), null, CancellationToken.None);

        Assert.AreEqual(new ExtractionResult(1, 0, 1), result);
        Assert.IsFalse(FileIO.Exists(Path.Combine(_root, "evil.txt")));
        Assert.IsTrue(FileIO.Exists(Out("fine.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("../x", false)]
    [DataRow("a/../../x", false)]
    [DataRow("/etc/passwd", true)]
    [DataRow("C:/Windows/x", false)]
    [DataRow("docs/b.md", true)]
    public void OnlyPathsInsideTheTargetAreSafe(string entry, bool safe)
    {
        var path = ArchiveExtractionService.SafePath(Out(), entry);

        Assert.AreEqual(safe, path is not null, path);
        if (path is not null)
        {
            StringAssert.StartsWith(path, Out());
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALoneGzHoldsOneFile()
    {
        var archive = Path.Combine(_root, "notes.txt.gz");
        using (var gzip = new GZipOutputStream(FileIO.Create(archive)))
        {
            gzip.Write(Encoding.UTF8.GetBytes("plain notes"));
        }

        await Service().ExtractAsync(archive, Out(), new ExtractionOptions(), null, CancellationToken.None);

        Assert.AreEqual("plain notes", FileIO.ReadAllText(Out("notes.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("backup.tar.gz", "backup")]
    [DataRow("backup.TGZ", "backup")]
    [DataRow("photos.zip", "photos")]
    [DataRow("disk.iso", "disk")]
    [DataRow("notes.txt.gz", "notes.txt")]
    public void TheOwnFolderIsTheNameWithoutTheArchiveEnding(string archive, string folder)
    {
        Assert.AreEqual(folder, Service().FolderNameFor(archive));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SevenZipFormatsAreArchivesThatSayWhatTheyNeed()
    {
        var service = Service();

        Assert.IsTrue(service.IsArchive("x.rar"));
        Assert.IsFalse(service.IsArchive("x.txt"));
        Assert.AreEqual(".rar archives need 7-Zip installed.", service.WhyCannotExtract("x.rar"));
        Assert.IsNull(service.WhyCannotExtract("x.zip"), "ZIP needs nothing installed");
        Assert.IsNull(Service(() => "7z").WhyCannotExtract("x.rar"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SevenZipIsGivenThePasswordOnStdinAndTheOverwriteRule()
    {
        var runner = new FakeProcessRunner().AnswerBytes("7z x", Encoding.UTF8.GetBytes("Everything is Ok\n"));
        var service = new ArchiveExtractionService(runner, () => "7z");

        await service.ExtractAsync(Path.Combine(_root, "x.7z"), Out(), new ExtractionOptions("s3cret", Overwrite: false), null, CancellationToken.None);

        var call = runner.Calls.Single();
        CollectionAssert.Contains(call.Arguments.ToArray(), "-aos", "skip what is already there");
        CollectionAssert.DoesNotContain(call.Arguments.ToArray(), "-p", "extracting, a bare -p means an empty password");
        Assert.IsFalse(call.Arguments.Any(a => a.Contains("s3cret")));
        StringAssert.StartsWith(Encoding.UTF8.GetString(call.StandardInput), "s3cret");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task A7zWrittenByAlt5UnpacksAndAWrongPasswordIsSaidInWords()
    {
        var writer = new SevenZipArchiveWriter(new ExternalProcessRunner());
        if (!writer.IsAvailable)
        {
            Assert.Inconclusive("7-Zip is not installed on this machine.");
        }

        var archive = Path.Combine(_root, "real.7z");
        await writer.WriteAsync(archive, Items(), new CompressionOptions(ArchiveFormat.SevenZip, 5, "s3cret", EncryptNames: true), null, CancellationToken.None);
        var service = new ArchiveExtractionService(new ExternalProcessRunner());

        var wrong = await Assert.ThrowsExactlyAsync<IOException>(
            () => service.ExtractAsync(archive, Out("wrong"), new ExtractionOptions("nope"), null, CancellationToken.None));
        Assert.AreEqual("The password does not open real.7z.", wrong.Message);

        var missing = await Assert.ThrowsExactlyAsync<IOException>(
            () => service.ExtractAsync(archive, Out("missing"), new ExtractionOptions(), null, CancellationToken.None));
        Assert.AreEqual("real.7z is encrypted: type its password.", missing.Message);

        var result = await service.ExtractAsync(archive, Out("right"), new ExtractionOptions("s3cret"), null, CancellationToken.None);
        Assert.AreEqual(2, result.Extracted);
        Assert.AreEqual("beta", FileIO.ReadAllText(Out("right", "docs", "b.md")));
    }
}
