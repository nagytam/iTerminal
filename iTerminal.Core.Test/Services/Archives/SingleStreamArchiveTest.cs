using System.Text;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using iTerminal.Core.Services.Archives;
using FileIO = System.IO.File;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// GZIP and BZIP2 hold one file, compressed as a single stream, written in-process and read back
/// with SharpZipLib. A new file replaces an existing one: nothing of the old content survives.
/// </summary>
[TestClass]
public class SingleStreamArchiveTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "SingleStreamArchiveTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private string Archive(string name) => Path.Combine(_root, name);

    /// <summary>A file on disk, as <c>CompressTask</c> lists it.</summary>
    private string Source(string name, string content)
    {
        var path = Path.Combine(_root, name);
        FileIO.WriteAllText(path, content);
        return path;
    }

    private static ArchiveItem Item(string path) => new(Path.GetFileName(path), false, new FileInfo(path).Length, DateTime.Now, path);

    private static Task<CompressionResult> Write(string archive, string source, ArchiveFormat format) =>
        ManagedArchiveWriter.WriteSingleStreamAsync(archive, [Item(source)], new CompressionOptions(format, format.DefaultLevel), null, CancellationToken.None);

    [Timeout(60_000)]
    [TestMethod]
    public async Task AGzipHoldsTheFileAndReadsBack()
    {
        var source = Source("notes.txt", new string('g', 20_000));
        var archive = Archive("notes.txt.gz");

        var result = await Write(archive, source, ArchiveFormat.Gzip);

        Assert.AreEqual(1, result.Added);
        using var file = FileIO.OpenRead(archive);
        using var gzip = new GZipInputStream(file);
        using var text = new StreamReader(gzip, Encoding.UTF8);
        Assert.AreEqual(new string('g', 20_000), text.ReadToEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABzip2HoldsTheFileAndReadsBack()
    {
        var source = Source("notes.txt", "bzip2 content");
        var archive = Archive("notes.txt.bz2");

        await Write(archive, source, ArchiveFormat.Bzip2);

        using var file = FileIO.OpenRead(archive);
        using var bzip2 = new BZip2InputStream(file);
        using var text = new StreamReader(bzip2, Encoding.UTF8);
        Assert.AreEqual("bzip2 content", text.ReadToEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewFileReplacesTheOldOneAndKeepsNone()
    {
        var archive = Archive("notes.txt.gz");
        await Write(archive, Source("old.txt", "previous content that must not survive"), ArchiveFormat.Gzip);

        await Write(archive, Source("new.txt", "fresh"), ArchiveFormat.Gzip);

        using var file = FileIO.OpenRead(archive);
        using var gzip = new GZipInputStream(file);
        using var text = new StreamReader(gzip, Encoding.UTF8);
        Assert.AreEqual("fresh", text.ReadToEnd(), "replaced, not added to");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACancelledWriteLeavesTheOldArchiveAndNoTemporaryFile()
    {
        var archive = Archive("notes.txt.bz2");
        await Write(archive, Source("old.txt", "old"), ArchiveFormat.Bzip2);
        var before = FileIO.ReadAllBytes(archive);
        var source = Source("new.txt", "new");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await AsyncAssert.ThrowsAsync<TaskCanceledException>(() =>
            ManagedArchiveWriter.WriteSingleStreamAsync(archive, [Item(source)], new CompressionOptions(ArchiveFormat.Bzip2, 9), null, cancelled.Token));

        CollectionAssert.AreEqual(before, FileIO.ReadAllBytes(archive), "the old archive is untouched");
        Assert.AreEqual(0, Directory.GetFiles(_root, ".*.tmp").Length, "no temporary file is left behind");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ProgressCountsTheBytesOfTheOneFile()
    {
        var source = Source("notes.txt", new string('p', 5_000));
        var reports = new List<ArchiveProgress>();
        var progress = new Collector(reports.Add);

        await ManagedArchiveWriter.WriteSingleStreamAsync(Archive("p.gz"), [Item(source)], new CompressionOptions(ArchiveFormat.Gzip, 6), progress, CancellationToken.None);

        Assert.IsTrue(reports.Count > 0);
        Assert.AreEqual(5_000, reports[^1].BytesTotal);
        Assert.AreEqual(1, reports[^1].ItemsTotal);
    }

    private sealed class Collector(Action<ArchiveProgress> report) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value) => report(value);
    }
}
