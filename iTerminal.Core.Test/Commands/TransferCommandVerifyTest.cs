using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Checksums;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Commands.Test;

/// <summary>How <see cref="TransferCommand"/> routes a transfer once "Verify after copy" is ticked.</summary>
[TestClass]
public class TransferCommandVerifyTest
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "src", "sub"));
        File.WriteAllText(Path.Combine(_dir, "src", "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(_dir, "src", "sub", "b.txt"), "beta");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static T Command<T>(Func<TaskManager, ProgressWindowFactory, IErrorPromptService, IConflictPromptService, FileSystemResolver, IContentModelService, T> make)
    {
        var contentModels = Mock.Of<IContentModelService>();
        // The one local drive is a real file system, so a one-drive transfer is genuinely one drive.
        var resolver = new FileSystemResolver(new FileSystemService(), Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(), Mock.Of<IArchiveFileSystemService>(), contentModels);
        return make(new Mock<TaskManager>().Object, new ProgressWindowFactory(TestTheme()), new FakeErrorPromptService(), Mock.Of<IConflictPromptService>(), resolver, contentModels);
    }

    private static CopyCommand Copy(CountingChecksumService counter) =>
        Command((t, p, e, c, r, m) => new CopyCommand(t, p, e, c, r, m, counter));

    private static MoveCommand Move(CountingChecksumService counter) =>
        Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m, counter));

    private static FileConflictResolver Resolver() => ConflictResolvers.Of(true);

    [Timeout(60_000)]
    [TestMethod]
    public async Task SameDriveFileMove_WithVerifier_IsCheckedBeforeTheSourceGoes()
    {
        CountingChecksumService counter = new();
        string source = Path.Combine(_dir, "src", "a.txt");
        string target = Path.Combine(_dir, "moved.txt");

        var (discovery, task) = Move(counter).CreateTasks(new Url(source), new Url(target), Resolver(), new TransferVerifier(counter));
        Assert.IsNull(discovery);
        await task!.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(2, counter.Calls, "the streamed copy was hashed at both ends");
        Assert.IsFalse(File.Exists(source));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SameDriveFileMove_WithoutVerifier_HashesNothing()
    {
        CountingChecksumService counter = new();
        string source = Path.Combine(_dir, "src", "a.txt");

        var (_, task) = Move(counter).CreateTasks(new Url(source), new Url(Path.Combine(_dir, "moved.txt")), Resolver(), verifier: null);
        await task!.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(0, counter.Calls);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "moved.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SameDriveFolderCopy_WithVerifier_GetsADiscoveryTaskAndChecksEveryFile()
    {
        CountingChecksumService counter = new();
        string source = Path.Combine(_dir, "src");
        string target = Path.Combine(_dir, "copy");

        var (discovery, task) = Copy(counter).CreateTasks(new Url(source), new Url(target), Resolver(), new TransferVerifier(counter));
        Assert.IsNotNull(discovery, "a one-drive folder is walked file by file when it is verified");
        _ = discovery.ExecuteAsync();
        await task!.ExecuteAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual(4, counter.Calls);
        Assert.AreEqual("beta", File.ReadAllText(Path.Combine(target, "sub", "b.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SameDriveFolderCopy_WithoutVerifier_StaysOneNativeCall()
    {
        var (discovery, task) = Copy(new CountingChecksumService()).CreateTasks(new Url(Path.Combine(_dir, "src")), new Url(Path.Combine(_dir, "copy")), Resolver(), verifier: null);

        Assert.IsNull(discovery);
        Assert.IsNotNull(task);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FolderCopiedIntoItself_StaysNativeEvenWhenVerifying()
    {
        CountingChecksumService counter = new();

        var (discovery, _) = Copy(counter).CreateTasks(new Url(Path.Combine(_dir, "src")), new Url(Path.Combine(_dir, "src", "sub", "again")), Resolver(), new TransferVerifier(counter));

        Assert.IsNull(discovery, "streaming a tree into itself would never end");
    }
}
