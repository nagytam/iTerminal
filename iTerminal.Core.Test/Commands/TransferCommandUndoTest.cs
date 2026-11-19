using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Factories;
using iTerminal.Core.Services;
using iTerminal.Core.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Commands.Test;

/// <summary>What a move made from the Move dialog hands to Ctrl+Z (<see cref="RenameUndoService"/>).</summary>
[TestClass]
public class TransferCommandUndoTest
{
    private string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_dir, "src"));
        Directory.CreateDirectory(Path.Combine(_dir, "dst"));
        File.WriteAllText(Path.Combine(_dir, "src", "a.txt"), "alpha");
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

    private static Mock<IContentModel> Model(params string[] existing)
    {
        Mock<IContentModel> model = new();
        model.Setup(m => m.ExistsFile(It.IsAny<Url>())).Returns<Url>(u => existing.Contains(u.FullUrl));
        return model;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMoveThatEnds_IsRememberedForUndo()
    {
        var undo = new RenameUndoService();
        var move = Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m, renameUndo: undo));
        var source = new Url(Path.Combine(_dir, "src", "a.txt"));
        var target = new Url(Path.Combine(_dir, "dst", "a.txt"));
        var (_, task) = move.CreateTasks(source, target, ConflictResolvers.Of(true), verifier: null);

        move.RememberMovesForUndoWhenFinished(task!, Model().Object, [(source, target)]);
        Assert.IsNull(undo.Peek(), "nothing is remembered before the move has ended");
        await task!.RunAsync();

        Assert.IsNull(task.Error);
        var remembered = undo.Peek();
        Assert.IsNotNull(remembered);
        Assert.AreEqual(source.FullUrl, remembered.Source.FullUrl);
        Assert.AreEqual(target.FullUrl, remembered.Target.FullUrl);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMoveOntoAnExistingTarget_IsNotRemembered()
    {
        var undo = new RenameUndoService();
        var move = Command((t, p, e, c, r, m) => new MoveCommand(t, p, e, c, r, m, renameUndo: undo));
        var source = new Url(Path.Combine(_dir, "src", "a.txt"));
        var target = new Url(Path.Combine(_dir, "dst", "a.txt"));
        File.WriteAllText(Path.Combine(_dir, "dst", "a.txt"), "older");
        var (_, task) = move.CreateTasks(source, target, ConflictResolvers.Of(true), verifier: null);

        move.RememberMovesForUndoWhenFinished(task!, Model(target.FullUrl).Object, [(source, target)]);
        await task!.RunAsync();

        Assert.IsNull(undo.Peek(), "the older file was replaced, so a move back would not restore it");
    }
}
