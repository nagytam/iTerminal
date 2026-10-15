using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Tasks;
using iTerminal.Core.Commands.CommandLine;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using Moq;

namespace iTerminal.Core.Test.Model;

[TestClass]
public class TerminalCommandLineTest
{
    private string _folder = string.Empty;
    private readonly Mock<IContentModelService> _service = new();
    private readonly Mock<IContentModel> _panel = new();
    private TaskManager _tasks = null!;
    private TerminalModel _terminal = null!;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "iterminal-cl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_folder, "sub dir"));
        _panel.SetupGet(p => p.Name).Returns("LeftPanel");
        _panel.SetupGet(p => p.Url).Returns(new Url(_folder + Path.DirectorySeparatorChar));
        _service.Setup(s => s.GetLastContentModel()).Returns(_panel.Object);
        _service.Setup(s => s.GetFocusedModel()).Returns(_panel.Object);
        _service.Setup(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>())).Returns(Task.CompletedTask);
        _tasks = new TaskManager();
        _terminal = new TerminalModel(_tasks, _service.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _tasks.Dispose();
        Directory.Delete(_folder, true);
    }

    [TestMethod]
    public void QuoteName_QuotesOnlyNamesWithBlanks()
    {
        Assert.AreEqual("readme.txt", TerminalModel.QuoteName("readme.txt"));
        Assert.AreEqual("\"my file.txt\"", TerminalModel.QuoteName("my file.txt"));
    }

    [TestMethod]
    public void InsertText_SeparatesWithABlank()
    {
        _terminal.Command.Command = "cp";

        Assert.IsTrue(_terminal.InsertText("a.txt"));
        Assert.IsTrue(_terminal.InsertText("b.txt"));

        Assert.AreEqual("cp a.txt b.txt", _terminal.Command.Command);
    }

    [TestMethod]
    public void RecallPreviousAndNext_WalkTheHistoryAndGiveBackTheDraft()
    {
        _terminal.History.Add(new TerminalCommand { Command = "first" });
        _terminal.History.Add(new TerminalCommand { Command = "second" });
        _terminal.Command.Command = "half typed";

        Assert.IsTrue(_terminal.RecallPrevious());
        Assert.AreEqual("second", _terminal.Command.Command);
        Assert.IsTrue(_terminal.RecallPrevious());
        Assert.AreEqual("first", _terminal.Command.Command);
        Assert.IsTrue(_terminal.RecallPrevious(), "already at the oldest: stays put");
        Assert.AreEqual("first", _terminal.Command.Command);

        Assert.IsTrue(_terminal.RecallNext());
        Assert.AreEqual("second", _terminal.Command.Command);
        Assert.IsTrue(_terminal.RecallNext());
        Assert.AreEqual("half typed", _terminal.Command.Command);
        Assert.IsFalse(_terminal.RecallNext(), "nothing newer than the draft");
    }

    [TestMethod]
    public void Cd_NavigatesThePanelInsteadOfRunningAShell()
    {
        _terminal.Command.Command = "cd \"sub dir\"";

        _terminal.StartCommand();

        _service.Verify(s => s.SetUrlAsync(
            "LeftPanel",
            It.Is<Url>(u => u.Path.TrimEnd(Path.DirectorySeparatorChar).EndsWith("sub dir"))), Times.Once);
        Assert.AreEqual(1, _terminal.History.Count);
        Assert.AreEqual("cd \"sub dir\"", _terminal.History[0].Command);
        Assert.AreEqual(string.Empty, _terminal.Command.Command);
    }

    [TestMethod]
    public void Cd_ToAMissingFolder_SaysSoAndStays()
    {
        _terminal.Command.Command = "cd nowhere";

        _terminal.StartCommand();

        _service.Verify(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()), Times.Never);
        Assert.IsTrue(_terminal.History[0].Error!.Contains("no such directory"));
    }

    [TestMethod]
    public void ACdChainedToSomethingElse_IsLeftToTheShell()
    {
        _terminal.Command.Command = "cd sub && ls";

        Assert.IsFalse(_terminal.TryChangeDirectory());
    }

    [TestMethod]
    public void InsertCommand_CopiesTheFileNameAndTheFolderPath()
    {
        var entry = new FileSystemEntry(Path.Combine(_folder, "sub dir", "x.txt"));
        _panel.Setup(p => p.GetFocusedEntry()).Returns(entry);
        var revealed = 0;

        var name = new InsertIntoCommandLineCommand(
            CommandLineInsert.FileName, new Key(ConsoleKey.Enter, false, true, false),
            _panel.Object, _service.Object, _terminal, () => revealed++);
        var path = new InsertIntoCommandLineCommand(
            CommandLineInsert.FolderPath, new Key(ConsoleKey.Enter, true, false, true),
            _panel.Object, _service.Object, _terminal);

        Assert.IsTrue(name.CanExecute());
        name.ExecuteAsync(null).Wait();
        path.ExecuteAsync(null).Wait();

        Assert.AreEqual(1, revealed);
        Assert.AreEqual("x.txt " + TerminalModel.QuoteName(_folder), _terminal.Command.Command);
    }
}
