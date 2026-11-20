using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using iTerminal.Core.Tasks;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Commands.Test;

/// <summary>A file copied or moved onto itself is refused plainly, not offered an overwrite of itself.</summary>
[TestClass]
public class TransferOntoItselfTest
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "alpha");
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
        var resolver = new FileSystemResolver(new FileSystemService(), Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(), Mock.Of<IArchiveFileSystemService>(), contentModels);
        return make(new Mock<TaskManager>().Object, new ProgressWindowFactory(TestTheme()), new FakeErrorPromptService(), Mock.Of<IConflictPromptService>(), resolver, contentModels);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMoveOntoItself_IsRefusedAndLeavesTheFile()
    {
        var move = Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m));
        var file = new Url(Path.Combine(_dir, "a.txt"));

        var (_, task) = move.CreateTasks(file, file, ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        StringAssert.Contains(task.Error, "same file");
        Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(_dir, "a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACopyOntoItself_IsRefusedAndLeavesTheFile()
    {
        var copy = Command((t, p, e, c, r, m) => new CopyCommand(t, p, e, c, r, m));
        var file = new Url(Path.Combine(_dir, "a.txt"));

        var (_, task) = copy.CreateTasks(file, file, ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        StringAssert.Contains(task.Error, "same file");
        Assert.AreEqual("alpha", File.ReadAllText(Path.Combine(_dir, "a.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMoveToAnotherName_StillWorks()
    {
        var move = Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m));

        var (_, task) = move.CreateTasks(new Url(Path.Combine(_dir, "a.txt")), new Url(Path.Combine(_dir, "b.txt")), ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        Assert.IsNull(task.Error);
        Assert.IsTrue(File.Exists(Path.Combine(_dir, "b.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsSameFile_ComparesExactNamesWithEitherSeparator()
    {
        Assert.IsTrue(ContentModelFileTransferTask.IsSameFile(new Url(@"C:\x\a.txt"), new Url(@"C:\x\a.txt")));
        Assert.IsTrue(ContentModelFileTransferTask.IsSameFile(new Url(@"C:\x\a.txt"), new Url("C:/x/a.txt")));
        Assert.IsFalse(ContentModelFileTransferTask.IsSameFile(new Url(@"C:\x\a.txt"), new Url(@"C:\x\A.txt")), "a change of case is a rename, not the same name");
        Assert.IsFalse(ContentModelFileTransferTask.IsSameFile(new Url(@"C:\x\a.txt"), new Url(@"C:\x\b.txt")));
    }

    private string MakeTree()
    {
        string tree = Path.Combine(_dir, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "sub"));
        File.WriteAllText(Path.Combine(tree, "keep.txt"), "kept");
        return tree;
    }

    private static Url Folder(string path) => new(path + Path.DirectorySeparatorChar);

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderMovedOntoItselfWithOverwrite_IsRefusedAndKeptWithItsFiles()
    {
        // Overwrite used to delete the target first, and the target was the folder being moved.
        string tree = MakeTree();
        var move = Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m));

        var (_, task) = move.CreateTasks(Folder(tree), Folder(tree), ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        StringAssert.Contains(task.Error, "into itself");
        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(tree, "keep.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderMovedIntoItsOwnSubfolder_IsRefusedAndKept()
    {
        string tree = MakeTree();
        var move = Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m));

        var (_, task) = move.CreateTasks(Folder(tree), Folder(Path.Combine(tree, "sub", "again")), ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        StringAssert.Contains(task.Error, "into itself");
        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(tree, "keep.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderCopiedIntoItsOwnSubfolder_IsRefusedInsteadOfRunningUntilTheStackIsGone()
    {
        string tree = MakeTree();
        var copy = Command((t, p, e, c, r, m) => new CopyCommand(t, p, e, c, r, m));

        var (_, task) = copy.CreateTasks(Folder(tree), Folder(Path.Combine(tree, "sub", "again")), ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        StringAssert.Contains(task.Error, "into itself");
        Assert.IsFalse(Directory.Exists(Path.Combine(tree, "sub", "again")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFolderCopiedToASiblingWithTheSamePrefix_StillWorks()
    {
        string tree = MakeTree();
        var copy = Command((t, p, e, c, r, m) => new CopyCommand(t, p, e, c, r, m));

        var (_, task) = copy.CreateTasks(Folder(tree), Folder(tree + "2"), ConflictResolvers.Of(true), verifier: null);
        await task!.RunAsync();

        Assert.IsNull(task.Error);
        Assert.AreEqual("kept", File.ReadAllText(Path.Combine(tree + "2", "keep.txt")));
    }
}
