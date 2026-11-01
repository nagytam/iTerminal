using System.Text;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// 7z through the 7-Zip command-line tool: the command line it is given, the password on stdin, and
/// — when 7-Zip is installed on the machine running the tests — a real archive.
/// </summary>
[TestClass]
public class SevenZipArchiveWriterTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "SevenZipArchiveWriterTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(_root, "src", "docs"));
        File.WriteAllText(Path.Combine(_root, "src", "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(_root, "src", "docs", "b.md"), "beta");
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

    private List<ArchiveItem> Items() =>
    [
        new("a.txt", false, 5, DateTime.Now, Path.Combine(_root, "src", "a.txt")),
        new("docs/", true, 0, DateTime.Now, Path.Combine(_root, "src", "docs")),
        new("docs/b.md", false, 4, DateTime.Now, Path.Combine(_root, "src", "docs", "b.md")),
    ];

    [Timeout(60_000)]
    [TestMethod]
    public void TheCommandLineCarriesTheLevelAndNeverThePassword()
    {
        var arguments = SevenZipArchiveWriter.BuildArguments(
            "out.7z", new CompressionOptions(ArchiveFormat.SevenZip, 9, "s3cret", EncryptNames: true), ["a.txt", "docs"]);

        CollectionAssert.Contains(arguments, "-mx=9");
        CollectionAssert.Contains(arguments, "-p", "a bare -p: 7-Zip asks for the password, and is answered on stdin");
        CollectionAssert.Contains(arguments, "-mhe=on");
        Assert.IsFalse(arguments.Any(a => a.Contains("s3cret")), string.Join(' ', arguments));
        CollectionAssert.AreEqual(new[] { "--", "out.7z", "a.txt", "docs" }, arguments.TakeLast(4).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutAPasswordThereIsNoPasswordSwitch()
    {
        var arguments = SevenZipArchiveWriter.BuildArguments("out.7z", new CompressionOptions(ArchiveFormat.SevenZip, 5), ["a.txt"]);

        Assert.IsFalse(arguments.Contains("-p"));
        Assert.IsFalse(arguments.Contains("-mhe=on"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ReplacingOrKeepingIsTheUpdateSwitch()
    {
        var replace = SevenZipArchiveWriter.BuildArguments("x.7z", new CompressionOptions(ArchiveFormat.SevenZip, 5), ["a"]);
        var keep = SevenZipArchiveWriter.BuildArguments("x.7z", new CompressionOptions(ArchiveFormat.SevenZip, 5, ReplaceExisting: false), ["a"]);

        CollectionAssert.Contains(replace, "-up1q1r2x2y2z2");
        CollectionAssert.Contains(keep, "-up1q1r2x1y1z1");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePasswordIsWrittenToStdinAndLocalFilesAreAddedFromTheirFolder()
    {
        var runner = new FakeProcessRunner().AnswerBytes("7z a", Encoding.UTF8.GetBytes("  40%\b\b\b\b 100%\nEverything is Ok\n"));
        var writer = new SevenZipArchiveWriter(runner, () => "7z");
        var reports = new List<ArchiveProgress>();

        await writer.WriteAsync(Path.Combine(_root, "out.7z"), Items(), new CompressionOptions(ArchiveFormat.SevenZip, 5, "s3cret"),
            new InlineProgress(reports.Add), CancellationToken.None);

        var call = runner.Calls.Single();
        Assert.AreEqual("s3cret" + Environment.NewLine + "s3cret" + Environment.NewLine, Encoding.UTF8.GetString(call.StandardInput),
            "twice: adding to an archive with encrypted names, 7-Zip asks once to open it and once for the new entries");
        CollectionAssert.AreEqual(new[] { "a.txt", "docs" }, call.Arguments.TakeLast(2).ToArray(), "the top-level names, which 7-Zip recurses into");
        Assert.AreEqual(100, reports[^1].BytesDone * 100 / reports[^1].BytesTotal, "the percentages 7-Zip prints become progress");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailureIsSaidIn7ZipsWords()
    {
        var runner = new FakeProcessRunner().AnswerBytes("7z a", Encoding.UTF8.GetBytes("\nERROR: x.7z\nCan not open the file as archive\n"), exitCode: 2);
        var writer = new SevenZipArchiveWriter(runner, () => "7z");

        var exception = await Assert.ThrowsExactlyAsync<IOException>(() =>
            writer.WriteAsync(Path.Combine(_root, "x.7z"), Items(), new CompressionOptions(ArchiveFormat.SevenZip, 5), null, CancellationToken.None));

        StringAssert.StartsWith(exception.Message, "7-Zip: ERROR: x.7z");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WithoutSevenZipTheFormatIsNotOffered()
    {
        var service = new ArchiveCompressionService(new FakeProcessRunner(), new SevenZipArchiveWriter(new FakeProcessRunner(), () => null));

        CollectionAssert.DoesNotContain(service.AvailableFormats.ToArray(), ArchiveFormat.SevenZip);
        CollectionAssert.Contains(service.AvailableFormats.ToArray(), ArchiveFormat.Zip);
        Assert.AreEqual("7z needs 7-Zip installed", service.WhyUnavailable(ArchiveFormat.SevenZip));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARealEncrypted7zOpensOnlyWithItsPassword()
    {
        var writer = new SevenZipArchiveWriter(new ExternalProcessRunner());
        if (!writer.IsAvailable)
        {
            Assert.Inconclusive("7-Zip is not installed on this machine.");
        }

        var path = Path.Combine(_root, "real.7z");
        await writer.WriteAsync(path, Items(), new CompressionOptions(ArchiveFormat.SevenZip, 9, "s3cret", EncryptNames: true), null, CancellationToken.None);

        var runner = new ExternalProcessRunner();
        var withPassword = await runner.RunAsync(writer.Executable!, ["l", "-ps3cret", path]);
        var withoutPassword = await runner.RunAsync(writer.Executable!, ["l", "-pwrong", path]);
        StringAssert.Contains(withPassword.StandardOutput, "b.md");
        Assert.IsFalse(withoutPassword.StandardOutput.Contains("b.md"), "the names are encrypted too");

        // And added to: a second run keeps the first files.
        File.WriteAllText(Path.Combine(_root, "src", "later.txt"), "later");
        await writer.WriteAsync(path, [new("later.txt", false, 5, DateTime.Now, Path.Combine(_root, "src", "later.txt"))],
            new CompressionOptions(ArchiveFormat.SevenZip, 9, "s3cret", EncryptNames: true), null, CancellationToken.None);
        var listing = await runner.RunAsync(writer.Executable!, ["l", "-ps3cret", path]);
        StringAssert.Contains(listing.StandardOutput, "later.txt");
        StringAssert.Contains(listing.StandardOutput, "a.txt");

        // Without the password, adding to it is refused in words, not as exit code 255.
        var exception = await Assert.ThrowsExactlyAsync<IOException>(() => writer.WriteAsync(path,
            [new("later.txt", false, 5, DateTime.Now, Path.Combine(_root, "src", "later.txt"))],
            new CompressionOptions(ArchiveFormat.SevenZip, 9), null, CancellationToken.None));
        Assert.AreEqual("The archive is encrypted: type its password to add to it.", exception.Message);
    }

    private sealed class InlineProgress(Action<ArchiveProgress> report) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress value) => report(value);
    }
}
