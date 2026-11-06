using iConsole.Data;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Test.Helper;

namespace iTerminal.Core.Tasks.Test;

/// <summary>"Verify after copy": a damaged copy is caught, kept, reported, and a move keeps its source.</summary>
[TestClass]
public class VerifyAfterCopyTaskTest
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private string Write(string relative, string content)
    {
        string path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static TransferVerifier Verifier(CountingChecksumService counter) => new(counter);

    // ---- single file ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task Copy_CleanCopy_IsVerifiedAndSucceeds()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");
        CountingChecksumService counter = new();

        ContentModelCopyFileTask task = new(new FileSystemService(), new CorruptingFileSystem(new FileSystemService()), new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(counter));
        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(2, counter.Calls, "one pass over the source and one over the copy");
        Assert.AreEqual("hello", File.ReadAllText(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Copy_CorruptedCopy_IsReportedAndKept()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");

        ContentModelCopyFileTask task = new(new FileSystemService(), new CorruptingFileSystem(new FileSystemService(), "b"), new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(new CountingChecksumService()));
        await task.ExecuteAsync();

        Assert.IsNotNull(task.Error);
        StringAssert.Contains(task.Error, "b.txt");
        Assert.IsTrue(File.Exists(target), "the bad copy is flagged, not silently removed");
        Assert.AreEqual("hello", File.ReadAllText(source));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_CorruptedCopy_KeepsTheSource()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");

        ContentModelMoveFileTask task = new(new FileSystemService(), new CorruptingFileSystem(new FileSystemService(), "b"), new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(new CountingChecksumService()));
        await task.ExecuteAsync();

        Assert.IsNotNull(task.Error);
        Assert.IsTrue(File.Exists(source), "a move must not delete a source whose copy failed verification");
        Assert.IsTrue(File.Exists(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_CleanCopy_RemovesTheSource()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");

        ContentModelMoveFileTask task = new(new FileSystemService(), new CorruptingFileSystem(new FileSystemService()), new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(new CountingChecksumService()));
        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.IsFalse(File.Exists(source));
        Assert.AreEqual("hello", File.ReadAllText(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Copy_EmptyFile_IsVerified()
    {
        string source = Write("empty.txt", string.Empty);
        string target = Path.Combine(_dir, "empty2.txt");
        CountingChecksumService counter = new();

        ContentModelCopyFileTask task = new(new FileSystemService(), new CorruptingFileSystem(new FileSystemService(), "empty"), new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(counter));
        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(2, counter.Calls);
        Assert.IsTrue(File.Exists(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_OneDrive_WithVerify_IsStreamedNotRenamed()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");
        CorruptingFileSystem drive = new(new FileSystemService());
        CountingChecksumService counter = new();

        ContentModelMoveFileTask task = new(drive, drive, new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(counter));
        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(0, drive.NativeMoves, "a rename cannot be checked before the source is gone");
        Assert.AreEqual(2, counter.Calls);
        Assert.IsFalse(File.Exists(source));
        Assert.AreEqual("hello", File.ReadAllText(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_OneDrive_WithVerify_CorruptedCopy_KeepsSource()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");
        CorruptingFileSystem drive = new(new FileSystemService(), "b");

        ContentModelMoveFileTask task = new(drive, drive, new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(new CountingChecksumService()));
        await task.ExecuteAsync();

        Assert.IsNotNull(task.Error);
        Assert.IsTrue(File.Exists(source));
        Assert.IsTrue(File.Exists(target));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_OneDrive_WithoutVerify_StillUsesTheNativeRename()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");
        CorruptingFileSystem drive = new(new FileSystemService());
        CountingChecksumService counter = new();

        ContentModelMoveFileTask task = new(drive, drive, new Url(source), new Url(target), ConflictResolvers.Of(true));
        await task.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(1, drive.NativeMoves);
        Assert.AreEqual(0, counter.Calls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Copy_OntoItself_WithVerify_DoesNotStreamTheFileIntoItself()
    {
        string source = Write("a.txt", "hello");
        CorruptingFileSystem drive = new(new FileSystemService());

        ContentModelCopyFileTask task = new(drive, drive, new Url(source), new Url(source), ConflictResolvers.Of(true), null, Verifier(new CountingChecksumService()));
        await task.ExecuteAsync();

        Assert.AreEqual("hello", File.ReadAllText(source), "streaming a file into itself would have emptied it");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Move_CancelledWhileVerifying_KeepsSourceAndTheCompleteCopy()
    {
        string source = Write("a.txt", "hello");
        string target = Path.Combine(_dir, "b.txt");
        CountingChecksumService counter = new();

        ContentModelMoveFileTask task = new(new FileSystemService(), new CorruptingFileSystem(new FileSystemService()), new Url(source), new Url(target), ConflictResolvers.Of(true), null, Verifier(counter));
        counter.BeforeCompute = call => task.CancellationTokenSource.Cancel();
        await task.ExecuteAsync();

        Assert.IsTrue(File.Exists(source), "unverified, so not removed");
        Assert.AreEqual("hello", File.ReadAllText(target), "the copy was complete and is not deleted as if it were partial");
    }

    // ---- folders ----

    private (string Src, string Dst) Tree()
    {
        string src = Path.Combine(_dir, "src");
        Write(Path.Combine("src", "good1.txt"), "one");
        Write(Path.Combine("src", "sub", "good2.txt"), "two");
        Write(Path.Combine("src", "sub", "bad.txt"), "three");
        Write(Path.Combine("src", "empty.txt"), string.Empty);
        return (src, Path.Combine(_dir, "dst"));
    }

    private static async Task Run(TransferTask task, FolderDiscoveryTask discovery)
    {
        _ = discovery.ExecuteAsync();
        await task.ExecuteAsync();
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FolderCopy_OnlyTheDamagedFile_IsReported_AndStaysOnDisk()
    {
        (string src, string dst) = Tree();
        FileSystemService source = new();
        CorruptingFileSystem target = new(new FileSystemService(), "bad");
        Url srcUrl = new(src), dstUrl = new(dst + Path.DirectorySeparatorChar);
        FolderDiscoveryTask discovery = new(source, target, srcUrl, dstUrl);
        FakeErrorPromptService prompt = new();
        CountingChecksumService counter = new();
        ContentModelCopyFolderTask task = new(source, target, srcUrl, dstUrl, true, discovery, prompt, Verifier(counter));

        await Run(task, discovery);

        Assert.IsNotNull(task.Error, "a verification failure is a failure of the run");
        StringAssert.Contains(task.Description, "bad.txt");
        Assert.AreEqual(1, prompt.PromptCount);
        Assert.IsTrue(File.Exists(Path.Combine(dst, "sub", "bad.txt")), "the bad copy is left for the user");
        Assert.AreEqual("one", File.ReadAllText(Path.Combine(dst, "good1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(dst, "empty.txt")));
        Assert.AreEqual(8, counter.Calls, "two passes for each of four files, the empty one included");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FolderMove_OneDamagedFile_KeepsItsSourceAndTheSourceFolder()
    {
        (string src, string dst) = Tree();
        FileSystemService source = new();
        CorruptingFileSystem target = new(new FileSystemService(), "bad");
        Url srcUrl = new(src), dstUrl = new(dst + Path.DirectorySeparatorChar);
        FolderDiscoveryTask discovery = new(source, target, srcUrl, dstUrl);
        ContentModelMoveFolderTask task = new(source, target, srcUrl, dstUrl, true, discovery, new FakeErrorPromptService(), Verifier(new CountingChecksumService()));

        await Run(task, discovery);

        Assert.IsNotNull(task.Error);
        Assert.IsTrue(File.Exists(Path.Combine(src, "sub", "bad.txt")), "source of the damaged copy is kept");
        Assert.IsFalse(File.Exists(Path.Combine(src, "good1.txt")), "verified files were moved");
        Assert.IsTrue(File.Exists(Path.Combine(dst, "sub", "bad.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task FolderMove_OneDrive_WithVerify_IsStreamedPerFile()
    {
        (string src, string dst) = Tree();
        CorruptingFileSystem drive = new(new FileSystemService());
        Url srcUrl = new(src), dstUrl = new(dst + Path.DirectorySeparatorChar);
        FolderDiscoveryTask discovery = new(drive, drive, srcUrl, dstUrl);
        CountingChecksumService counter = new();
        ContentModelMoveFolderTask task = new(drive, drive, srcUrl, dstUrl, true, discovery, new FakeErrorPromptService(), Verifier(counter));

        await Run(task, discovery);

        Assert.IsNull(task.Error);
        Assert.AreEqual(0, drive.NativeMoves);
        Assert.AreEqual(8, counter.Calls);
        Assert.IsFalse(Directory.Exists(src));
        Assert.AreEqual("two", File.ReadAllText(Path.Combine(dst, "sub", "good2.txt")));
    }

    // ---- the verifier itself ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task Verifier_DifferentSize_Fails()
    {
        string a = Write("a.bin", "abc");
        string b = Write("b.bin", "abcd");

        await Assert.ThrowsExactlyAsync<VerificationFailedException>(() =>
            new TransferVerifier(new ChecksumService()).VerifyAsync(new FileSystemService(), new FileSystemService(), new Url(a), new Url(b)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Verifier_SameBytes_Passes()
    {
        string a = Write("a.bin", "abc");
        string b = Write("b.bin", "abc");

        await new TransferVerifier(new ChecksumService()).VerifyAsync(new FileSystemService(), new FileSystemService(), new Url(a), new Url(b));
    }
}
