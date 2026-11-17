using iTerminal.Core.Services.Archives;
using iTerminal.Core.Services.Processes;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Test.Services.Archives;

[TestClass]
public class UnrarArchiveReaderTest
{
    /// <summary>What <c>unrar lt</c> printed here for an archive holding a.txt and docs/readme.md.</summary>
    private const string Listing = """

        UNRAR 7.23 freeware      Copyright (c) 1993-2026 Alexander Roshal

        Archive: docs.rar
        Details: RAR 5

         Name: a.txt
         Type: File
         Size: 6
         Packed size: 32
         Ratio: 533%
         mtime: 2026-11-17 18:54:46,680353970
         Attributes: -rw-rw-r--
         CRC32: 363A3020
         Host OS: Unix
         Compression: RAR 5.0(v50) -m3 -md=128k
         Flags: 

         Name: docs
         Type: Directory
         Size: 0
         mtime: 2026-11-17 18:55:01,000000000

         Name: docs/readme.md
         Type: File
         Size: 12
         mtime: 2026-11-17 18:55:02,000000000

        """;

    private static UnrarArchiveReader Reader(FakeProcessRunner runner) => new(runner, () => "/usr/bin/unrar");

    [Timeout(60_000)]
    [TestMethod]
    public void ParseListingReadsNamesFoldersSizesAndTimes()
    {
        var entries = UnrarArchiveReader.ParseListing(Listing);

        Assert.AreEqual(3, entries.Count);
        Assert.AreEqual("a.txt", entries[0].Path);
        Assert.IsFalse(entries[0].IsFolder);
        Assert.AreEqual(6, entries[0].Size);
        Assert.AreEqual(new DateTime(2026, 11, 17, 18, 54, 46), entries[0].Modified);
        Assert.IsTrue(entries[1].IsFolder);
        Assert.AreEqual("docs/readme.md", entries[2].Path);
        Assert.AreEqual(12, entries[2].Size);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ParseListingTurnsWindowsSeparatorsIntoSlashes()
    {
        var entries = UnrarArchiveReader.ParseListing(" Name: docs\\readme.md\n Type: File\n Size: 1\n");

        Assert.AreEqual("docs/readme.md", entries.Single().Path);
        Assert.AreEqual("docs\\readme.md", entries.Single().RawPath, "the raw name is kept for reading the entry back");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListAsyncReadsTheListingWithoutAPassword()
    {
        var runner = new FakeProcessRunner().AnswerBytes("/usr/bin/unrar lt", System.Text.Encoding.UTF8.GetBytes(Listing));

        var entries = await Reader(runner).ListAsync("docs.rar", CancellationToken.None);

        Assert.AreEqual(3, entries.Count);
        CollectionAssert.Contains(runner.Calls.Single().Arguments.ToList(), "-p-", "never asks for a password");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListAsyncSaysWhenTheNamesAreEncrypted()
    {
        var runner = new FakeProcessRunner().AnswerBytes(
            "/usr/bin/unrar lt",
            System.Text.Encoding.UTF8.GetBytes("\nUNRAR 7.23 freeware\n\nArchive: docs.rar\nDetails: RAR 5, encrypted headers\n\n"));

        var error = await AssertAsync.ThrowsAsync<IOException>(() => Reader(runner).ListAsync("docs.rar", CancellationToken.None));

        Assert.AreEqual("docs.rar hides its file names behind a password: unpack it with Shift+F5.", error.Message);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExtractAsyncSendsThePasswordOnStdinOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "unrar-reader-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var runner = new FakeProcessRunner().Answer("/usr/bin/unrar x", "All OK\n");

            await Reader(runner).ExtractAsync("docs.rar", root, new ExtractionOptions("hunter2"), null, CancellationToken.None);

            var call = runner.Calls.Single();
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("hunter2" + Environment.NewLine), call.StandardInput);
            CollectionAssert.DoesNotContain(call.Arguments.ToList(), "hunter2");
            CollectionAssert.Contains(call.Arguments.ToList(), "-p");
            CollectionAssert.Contains(call.Arguments.ToList(), "-o-", "a file the folder already has is skipped by default");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExtractAsyncWithoutAPasswordSaysSo()
    {
        var root = Path.Combine(Path.GetTempPath(), "unrar-reader-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var runner = new FakeProcessRunner().Answer("/usr/bin/unrar x", "", exitCode: 10, stderr: "Incorrect password for a.txt\n");

            var error = await AssertAsync.ThrowsAsync<IOException>(() =>
                Reader(runner).ExtractAsync("docs.rar", root, new ExtractionOptions(), null, CancellationToken.None));

            Assert.AreEqual("docs.rar is encrypted: unpack it with Shift+F5 and the password.", error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheReaderIsUnavailableWithoutTheProgram()
    {
        Assert.IsFalse(new UnrarArchiveReader(new FakeProcessRunner(), () => null).IsAvailable);
    }
}
