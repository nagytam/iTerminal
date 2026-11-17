using iTerminal.Core.Test.Helper;
using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;

namespace iTerminal.Core.Test.Services.Archives;

/// <summary>
/// Writes and reads real RAR archives with the installed <c>rar</c> and <c>unrar</c>. Inconclusive where either is
/// missing, as the 7-Zip samples are: the managed tests above cover the arguments and the parsing without them.
/// </summary>
[TestClass]
public class RarToolRoundTripTest
{
    private string _root = null!;

    [TestInitialize]
    public void Setup() => _root = Path.Combine(Path.GetTempPath(), "rar-round-trip-" + Guid.NewGuid().ToString("N")[..8]);

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Litter in %TEMP%, not a failure of the test.
        }
    }

    private static void RequireTools()
    {
        if (RarArchiveWriter.FindExecutable() is null || UnrarArchiveReader.FindExecutable() is null)
        {
            Assert.Inconclusive("rar and unrar are not both installed on this machine.");
        }
    }

    private static CompressionOptions Options(string? password = null, bool encryptNames = false, bool replace = true) =>
        new(ArchiveFormat.Rar, 3, password, ArchiveEncryption.Aes256, encryptNames, replace);

    /// <summary>A folder with a.txt and sub/b.txt, as the compress dialog would hand over.</summary>
    private List<ArchiveItem> Sources()
    {
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(Path.Combine(src, "sub"));
        File.WriteAllText(Path.Combine(src, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(src, "sub", "b.txt"), "world");
        return
        [
            new ArchiveItem("a.txt", false, 5, DateTime.Now, LocalPath: Path.Combine(src, "a.txt")),
            new ArchiveItem("sub", true, 0, DateTime.Now, LocalPath: Path.Combine(src, "sub")),
            new ArchiveItem("sub/b.txt", false, 5, DateTime.Now, LocalPath: Path.Combine(src, "sub", "b.txt")),
        ];
    }

    private string Out(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Task Write(string archive, IReadOnlyList<ArchiveItem> items, CompressionOptions options) =>
        new RarArchiveWriter(new ExternalProcessRunner()).WriteAsync(archive, items, options, null, CancellationToken.None);

    [Timeout(120_000)]
    [TestMethod]
    public async Task AnArchiveWrittenByRarIsListedAndExtractedByUnrar()
    {
        RequireTools();
        var archive = Path.Combine(_root, "docs.rar");
        await Write(archive, Sources(), Options());

        var reader = new UnrarArchiveReader(new ExternalProcessRunner());
        var entries = await reader.ListAsync(archive, CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "a.txt", "sub", "sub/b.txt" }, entries.Select(e => e.Path).ToArray());

        var target = Out("extracted");
        await reader.ExtractAsync(archive, target, new ExtractionOptions(), null, CancellationToken.None);
        Assert.AreEqual("hello", File.ReadAllText(Path.Combine(target, "a.txt")));
        Assert.AreEqual("world", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task APasswordIsNeededToReadAndTheRightOneOpensIt()
    {
        RequireTools();
        var archive = Path.Combine(_root, "secret.rar");
        await Write(archive, Sources(), Options(password: "hunter2"));
        var reader = new UnrarArchiveReader(new ExternalProcessRunner());

        var noPassword = await AssertAsync.ThrowsAsync<IOException>(() =>
            reader.ExtractAsync(archive, Out("none"), new ExtractionOptions(), null, CancellationToken.None));
        Assert.AreEqual("secret.rar is encrypted: unpack it with Shift+F5 and the password.", noPassword.Message);

        var wrong = await AssertAsync.ThrowsAsync<IOException>(() =>
            reader.ExtractAsync(archive, Out("wrong"), new ExtractionOptions("nope"), null, CancellationToken.None));
        Assert.AreEqual("secret.rar: the password is wrong.", wrong.Message);

        var target = Out("right");
        await reader.ExtractAsync(archive, target, new ExtractionOptions("hunter2"), null, CancellationToken.None);
        Assert.AreEqual("world", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task EncryptedNamesHideTheListingUntilTheArchiveIsOpened()
    {
        RequireTools();
        var archive = Path.Combine(_root, "hidden.rar");
        await Write(archive, Sources(), Options(password: "hunter2", encryptNames: true));

        var error = await AssertAsync.ThrowsAsync<IOException>(() =>
            new UnrarArchiveReader(new ExternalProcessRunner()).ListAsync(archive, CancellationToken.None));
        Assert.AreEqual("hidden.rar hides its file names behind a password: unpack it with Shift+F5.", error.Message);
    }

    [Timeout(120_000)]
    [TestMethod]
    public async Task ASecondWriteReplacesTheSameNameAsRarDoes()
    {
        RequireTools();
        var archive = Path.Combine(_root, "again.rar");
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        var file = Path.Combine(src, "a.txt");
        File.WriteAllText(file, "one");
        await Write(archive, [new ArchiveItem("a.txt", false, 3, DateTime.Now, LocalPath: file)], Options());
        File.WriteAllText(file, "two");
        await Write(archive, [new ArchiveItem("a.txt", false, 3, DateTime.Now, LocalPath: file)], Options(replace: true));

        var target = Out("again");
        await new UnrarArchiveReader(new ExternalProcessRunner()).ExtractAsync(archive, target, new ExtractionOptions(Overwrite: true), null, CancellationToken.None);

        Assert.AreEqual("two", File.ReadAllText(Path.Combine(target, "a.txt")));
    }
}
