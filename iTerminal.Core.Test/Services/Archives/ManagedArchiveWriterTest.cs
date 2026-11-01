using System.IO.Compression;
using FileIO = System.IO.File;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using iTerminal.Core.Services.Archives;
using SharpZipFile = ICSharpCode.SharpZipLib.Zip.ZipFile;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// ZIP and the TAR family, written for real and read back — with the BCL where it can read the
/// result (so the archives open in tools other than the one that wrote them), with SharpZipLib for
/// what the BCL cannot open (encrypted ZIP, TAR.BZ2).
/// </summary>
[TestClass]
public class ManagedArchiveWriterTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "ManagedArchiveWriterTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "docs", "deep"));
        FileIO.WriteAllText(Path.Combine(_root, "src", "a.txt"), "alpha");
        FileIO.WriteAllText(Path.Combine(_root, "src", "docs", "b.md"), "beta");
        FileIO.WriteAllText(Path.Combine(_root, "src", "docs", "deep", "c.bin"), new string('c', 50_000));
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

    private string Src(params string[] parts) => Path.Combine([_root, "src", .. parts]);

    private string Archive(string name) => Path.Combine(_root, name);

    /// <summary>a.txt and the docs folder, as <c>CompressTask</c> would list them.</summary>
    private List<ArchiveItem> Items() =>
    [
        Entry("a.txt", Src("a.txt")),
        Folder("docs/", Src("docs")),
        Entry("docs/b.md", Src("docs", "b.md")),
        Folder("docs/deep/", Src("docs", "deep")),
        Entry("docs/deep/c.bin", Src("docs", "deep", "c.bin")),
    ];

    private static ArchiveItem Entry(string name, string path) =>
        new(name, false, new FileInfo(path).Length, new DateTime(2024, 5, 6, 7, 8, 10), path);

    private static ArchiveItem Folder(string name, string path) =>
        new(name, true, 0, new DateTime(2024, 5, 6, 7, 8, 10), path);

    /// <summary>A file that is not on the local disk: no path, only a way to write its bytes.</summary>
    private static ArchiveItem Remote(string name, string content) =>
        new(name, false, 0, DateTime.Now, null, async (stream, ct) => await stream.WriteAsync(Encoding.UTF8.GetBytes(content), ct));

    private static Task<CompressionResult> Write(string path, IReadOnlyList<ArchiveItem> items, CompressionOptions options) =>
        ManagedArchiveWriter.WriteAsync(path, items, options, null, CancellationToken.None);

    private static Dictionary<string, string> ReadZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(
            e => e.FullName,
            e => e.FullName.EndsWith('/') ? string.Empty : new StreamReader(e.Open()).ReadToEnd());
    }

    private static Dictionary<string, string> ReadTar(Stream decompressed)
    {
        var entries = new Dictionary<string, string>();
        using var tar = new TarInputStream(decompressed, Encoding.UTF8);
        while (tar.GetNextEntry() is { } entry)
        {
            using var content = new MemoryStream();
            tar.CopyEntryContents(content);
            entries[entry.Name] = Encoding.UTF8.GetString(content.ToArray());
        }

        return entries;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AZipHoldsEveryFileAndFolderUnderItsName()
    {
        var path = Archive("out.zip");

        var result = await Write(path, Items(), new CompressionOptions(ArchiveFormat.Zip, 6));

        var entries = ReadZip(path);
        CollectionAssert.AreEquivalent(new[] { "a.txt", "docs/", "docs/b.md", "docs/deep/", "docs/deep/c.bin" }, entries.Keys.ToArray());
        Assert.AreEqual("alpha", entries["a.txt"]);
        Assert.AreEqual(50_000, entries["docs/deep/c.bin"].Length);
        Assert.AreEqual(3, result.Added);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheLevelChangesTheSize()
    {
        await Write(Archive("stored.zip"), Items(), new CompressionOptions(ArchiveFormat.Zip, 0));
        await Write(Archive("smallest.zip"), Items(), new CompressionOptions(ArchiveFormat.Zip, 9));

        Assert.IsTrue(new FileInfo(Archive("stored.zip")).Length > 50_000, "level 0 stores");
        Assert.IsTrue(new FileInfo(Archive("smallest.zip")).Length < 5_000, "level 9 compresses 50 000 'c's to almost nothing");
        Assert.AreEqual("alpha", ReadZip(Archive("stored.zip"))["a.txt"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnAesZipOpensOnlyWithItsPassword()
    {
        var path = Archive("secret.zip");

        await Write(path, Items(), new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret", ArchiveEncryption.Aes256));

        using (var zip = new SharpZipFile(path) { Password = "s3cret" })
        {
            var entry = zip.GetEntry("a.txt");
            Assert.IsTrue(entry.IsCrypted);
            Assert.AreEqual(256, entry.AESKeySize);
            Assert.AreEqual("alpha", new StreamReader(zip.GetInputStream(entry)).ReadToEnd());
        }

        using (var zip = new SharpZipFile(path) { Password = "wrong" })
        {
            Assert.ThrowsExactly<ICSharpCode.SharpZipLib.Zip.ZipException>(() => new StreamReader(zip.GetInputStream(zip.GetEntry("a.txt"))).ReadToEnd());
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AZipCryptoZipIsTheOldCipher()
    {
        var path = Archive("legacy.zip");

        await Write(path, Items(), new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret", ArchiveEncryption.ZipCrypto));

        using var zip = new SharpZipFile(path) { Password = "s3cret" };
        var entry = zip.GetEntry("docs/b.md");
        Assert.IsTrue(entry.IsCrypted);
        Assert.AreEqual(0, entry.AESKeySize, "ZipCrypto, which Windows Explorer can open");
        Assert.AreEqual("beta", new StreamReader(zip.GetInputStream(entry)).ReadToEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AddingToAZipKeepsWhatWasThereAndReplacesTheSameName()
    {
        var path = Archive("grow.zip");
        await Write(path, [Remote("old.txt", "old"), Remote("a.txt", "previous a")], new CompressionOptions(ArchiveFormat.Zip, 6));

        var result = await Write(path, [Entry("a.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.Zip, 6));

        var entries = ReadZip(path);
        Assert.AreEqual("old", entries["old.txt"]);
        Assert.AreEqual("alpha", entries["a.txt"], "replaced");
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(new CompressionResult(1, 1, 0), result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithReplacingOffTheArchivesCopyIsKept()
    {
        var path = Archive("keep.zip");
        await Write(path, [Remote("a.txt", "previous a")], new CompressionOptions(ArchiveFormat.Zip, 6));

        var result = await Write(path, [Entry("a.txt", Src("a.txt")), Entry("docs/b.md", Src("docs", "b.md"))],
            new CompressionOptions(ArchiveFormat.Zip, 6, ReplaceExisting: false));

        var entries = ReadZip(path);
        Assert.AreEqual("previous a", entries["a.txt"]);
        Assert.AreEqual("beta", entries["docs/b.md"]);
        Assert.AreEqual(new CompressionResult(1, 1, 1), result);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AddingToAnEncryptedZipNeedsItsPasswordAndLeavesItAloneWithout()
    {
        var path = Archive("locked.zip");
        await Write(path, [Remote("old.txt", "old")], new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret"));
        var before = await FileIO.ReadAllBytesAsync(path);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => Write(path, [Entry("a.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.Zip, 6)));

        CollectionAssert.AreEqual(before, await FileIO.ReadAllBytesAsync(path), "the archive is untouched");
        Assert.AreEqual(1, Directory.GetFiles(_root).Length, "and no temporary file is left beside it");

        await Write(path, [Entry("a.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.Zip, 6, "s3cret"));
        using var zip = new SharpZipFile(path) { Password = "s3cret" };
        Assert.AreEqual("old", new StreamReader(zip.GetInputStream(zip.GetEntry("old.txt"))).ReadToEnd());
        Assert.AreEqual("alpha", new StreamReader(zip.GetInputStream(zip.GetEntry("a.txt"))).ReadToEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATarGzHoldsEverythingAndCanBeAddedTo()
    {
        var path = Archive("out.tar.gz");
        await Write(path, Items(), new CompressionOptions(ArchiveFormat.TarGz, 9));

        await Write(path, [Remote("later.txt", "added later")], new CompressionOptions(ArchiveFormat.TarGz, 9));

        using var file = FileIO.OpenRead(path);
        var entries = ReadTar(new GZipInputStream(file));
        Assert.AreEqual("alpha", entries["a.txt"]);
        Assert.AreEqual(50_000, entries["docs/deep/c.bin"].Length);
        Assert.AreEqual("added later", entries["later.txt"], "a file with no local path is staged to learn its size");
        Assert.IsTrue(entries.ContainsKey("docs/"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATarBz2AndAPlainTarRoundTrip()
    {
        await Write(Archive("out.tar.bz2"), Items(), new CompressionOptions(ArchiveFormat.TarBz2, 9));
        await Write(Archive("out.tar"), Items(), new CompressionOptions(ArchiveFormat.Tar, 0));

        using (var file = FileIO.OpenRead(Archive("out.tar.bz2")))
        {
            Assert.AreEqual("beta", ReadTar(new BZip2InputStream(file))["docs/b.md"]);
        }

        // The BCL's own TAR reader: what other tools will see.
        using var reader = new System.Formats.Tar.TarReader(FileIO.OpenRead(Archive("out.tar")));
        var names = new List<string>();
        while (reader.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        CollectionAssert.Contains(names, "docs/deep/c.bin");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CancellingLeavesNoArchive()
    {
        var path = Archive("cancelled.zip");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ManagedArchiveWriter.WriteAsync(path, Items(), new CompressionOptions(ArchiveFormat.Zip, 6), null, cancellation.Token));

        Assert.AreEqual(0, Directory.GetFiles(_root).Length);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProgressCountsTheBytesAndTheFiles()
    {
        var reports = new List<ArchiveProgress>();
        await ManagedArchiveWriter.WriteAsync(Archive("p.zip"), Items(), new CompressionOptions(ArchiveFormat.Zip, 6),
            new SynchronousProgress(reports.Add), CancellationToken.None);

        var last = reports[^1];
        Assert.AreEqual(last.BytesTotal, last.BytesDone);
        Assert.AreEqual(50_009, last.BytesTotal);
        Assert.AreEqual(5, last.ItemsDone);
    }

    private sealed class SynchronousProgress(Action<ArchiveProgress> report) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value) => report(value);
    }
}
