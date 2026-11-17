using System.Text;
using ICSharpCode.SharpZipLib.Tar;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using FileIO = System.IO.File;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// XZ and TAR.XZ, compressed by the 7-Zip command line. Each is read back through 7-Zip itself, so
/// the test does not trust the writer's own output. Without 7-Zip installed the tests are inconclusive.
/// </summary>
[TestClass]
public class SevenZipStreamArchiveTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "SevenZipStreamArchiveTest_" + Guid.NewGuid().ToString("N")[..8]);
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

    private static void RequireSevenZip()
    {
        if (SevenZipArchiveWriter.FindExecutable() is null)
        {
            Assert.Inconclusive("7-Zip is not installed on this machine.");
        }
    }

    private ArchiveCompressionService Service() => new(new ExternalProcessRunner());

    private string Src(params string[] parts) => Path.Combine([_root, "src", .. parts]);

    private static ArchiveItem Entry(string name, string path) => new(name, false, new FileInfo(path).Length, DateTime.Now, path);

    /// <summary>Decompresses an XZ or TAR.XZ with 7-Zip, to the bytes of the single stream it holds.</summary>
    private static async Task<byte[]> DecompressWithSevenZip(string archive)
    {
        using var output = new MemoryStream();
        var result = await new ExternalProcessRunner().RunToStreamAsync(
            SevenZipArchiveWriter.FindExecutable()!, ["x", "-so", "-bd", "--", archive], output, null, null, CancellationToken.None);
        Assert.IsTrue(result.Success, result.StandardError);
        return output.ToArray();
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AnXzHoldsTheFileAndReadsBackThroughSevenZip()
    {
        RequireSevenZip();
        var archive = Path.Combine(_root, "notes.txt.xz");

        await Service().CompressAsync(archive, [Entry("notes.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.Xz, 6), null, CancellationToken.None);

        Assert.AreEqual("alpha", Encoding.UTF8.GetString(await DecompressWithSevenZip(archive)));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ATarXzHoldsEverythingAndReadsBackThroughSevenZip()
    {
        RequireSevenZip();
        var archive = Path.Combine(_root, "out.tar.xz");

        await Service().CompressAsync(archive,
            [new("docs/", true, 0, DateTime.Now), Entry("a.txt", Src("a.txt")), Entry("docs/b.md", Src("docs", "b.md"))],
            new CompressionOptions(ArchiveFormat.TarXz, 6), null, CancellationToken.None);

        using var tar = new TarInputStream(new MemoryStream(await DecompressWithSevenZip(archive)), Encoding.UTF8);
        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        CollectionAssert.Contains(names, "a.txt");
        CollectionAssert.Contains(names, "docs/b.md");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task AnXzReplacesTheOldFileAndKeepsNone()
    {
        RequireSevenZip();
        var archive = Path.Combine(_root, "notes.txt.xz");
        await Service().CompressAsync(archive, [Entry("notes.txt", Src("docs", "b.md"))], new CompressionOptions(ArchiveFormat.Xz, 6), null, CancellationToken.None);

        await Service().CompressAsync(archive, [Entry("notes.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.Xz, 6), null, CancellationToken.None);

        Assert.AreEqual("alpha", Encoding.UTF8.GetString(await DecompressWithSevenZip(archive)));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ATarXzReplacesTheOldArchiveAndKeepsNone()
    {
        RequireSevenZip();
        var archive = Path.Combine(_root, "out.tar.xz");
        await Service().CompressAsync(archive, [Entry("old.txt", Src("docs", "b.md"))], new CompressionOptions(ArchiveFormat.TarXz, 6), null, CancellationToken.None);

        await Service().CompressAsync(archive, [Entry("a.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.TarXz, 6), null, CancellationToken.None);

        using var tar = new TarInputStream(new MemoryStream(await DecompressWithSevenZip(archive)), Encoding.UTF8);
        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry)
        {
            names.Add(entry.Name);
        }

        CollectionAssert.AreEqual(new[] { "a.txt" }, names, "replaced, not added to");
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ACancelledXzWriteLeavesNoArchiveAndNoTemporaryFile()
    {
        RequireSevenZip();
        var archive = Path.Combine(_root, "cancelled.xz");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await AsyncAssert.ThrowsAsync<TaskCanceledException>(() =>
            Service().CompressAsync(archive, [Entry("a.txt", Src("a.txt"))], new CompressionOptions(ArchiveFormat.Xz, 6), null, cancelled.Token));

        Assert.IsFalse(FileIO.Exists(archive));
        Assert.AreEqual(0, Directory.GetFiles(_root, ".*.xz").Length, "no temporary output is left beside the target");
    }
}
