using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands.Marks;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Test.Helper;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Commands.Marks;

/// <summary>The keys that mark rows: Insert, Shift+Up/Down, Num*, Num+ and Num-.</summary>
[TestClass]
public class MarkCommandsTest
{
    private string _root = string.Empty;

    [TestInitialize]
    public void Initialize() =>
        _root = Path.Combine(Path.GetTempPath(), "MarkCommandsTest_" + Guid.NewGuid().ToString("N")[..8]);

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

    private static IContentModelService Focused(IContentModel? model)
    {
        var service = new Mock<IContentModelService>();
        service.Setup(s => s.GetFocusedModel()).Returns(model!);
        service.Setup(s => s.GetLastContentModel()).Returns(model!);
        return service.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InsertMarksTheRowAndStepsDown()
    {
        var model = MarkableTestModel.Create(_root);
        model.CursorPosition.Y = 2; // a.txt
        var command = new ToggleMarkCommand(Focused(model), model);

        Assert.IsTrue(await command.RunAsync(null));
        Assert.IsTrue(await command.RunAsync(null));

        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, model.MarkedNames);
        Assert.AreEqual(4, model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InsertOnTheParentRowOnlyStepsDown()
    {
        var model = MarkableTestModel.Create(_root);

        await new ToggleMarkCommand(Focused(model), model).RunAsync(null);

        Assert.AreEqual(0, model.MarkedCount);
        Assert.AreEqual(1, model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InsertOnTheLastRowStaysThere()
    {
        var model = MarkableTestModel.Create(_root);
        model.CursorPosition.Y = 4;

        await new ToggleMarkCommand(Focused(model), model).RunAsync(null);

        CollectionAssert.AreEqual(new[] { "c.jpg" }, model.MarkedNames);
        Assert.AreEqual(4, model.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheUnfocusedPanelDoesNotMark()
    {
        // Both panels carry the command; a key the focused panel declined is offered to the other.
        var model = MarkableTestModel.Create(_root);
        model.CursorPosition.Y = 2;
        var other = MarkableTestModel.Create(_root + "_other");
        try
        {
            var command = new ToggleMarkCommand(Focused(other), model);

            Assert.IsFalse(await command.RunAsync(null));
            Assert.AreEqual(0, model.MarkedCount);
        }
        finally
        {
            Directory.Delete(_root + "_other", recursive: true);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftUpAndDownMarkWhileMoving()
    {
        var model = MarkableTestModel.Create(_root);
        model.CursorPosition.Y = 2;
        var down = new MarkAndMoveCommand(Focused(model), 1, model);
        var up = new MarkAndMoveCommand(Focused(model), -1, model);

        Assert.AreEqual(ConsoleKey.DownArrow, down.Key!.ConsoleKey);
        Assert.IsTrue(down.Key.Shift);
        await down.RunAsync(null);
        await down.RunAsync(null);
        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, model.MarkedNames);

        await up.RunAsync(null); // c.jpg, then back onto b.txt
        Assert.AreEqual(3, model.CursorPosition.Y);
        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt", "c.jpg" }, model.MarkedNames);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NumStarInvertsTheFiles()
    {
        var model = MarkableTestModel.Create(_root);
        model.SetMark(model.Row("a.txt"), true);

        var command = new InvertMarksCommand(Focused(model), model);
        Assert.AreEqual(ConsoleKey.Multiply, command.Key!.ConsoleKey);
        await command.RunAsync(null);

        CollectionAssert.AreEqual(new[] { "b.txt", "c.jpg" }, model.MarkedNames);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MarkAllAndUnmarkAllFromTheMenu()
    {
        var model = MarkableTestModel.Create(_root);
        var service = Focused(model);

        Assert.IsFalse(new ClearMarksCommand(service).CanExecute(), "nothing to unmark yet");
        await new MarkAllCommand(service).RunAsync(null);
        CollectionAssert.AreEqual(new[] { "photos", "a.txt", "b.txt", "c.jpg" }, model.MarkedNames);

        await new ClearMarksCommand(service).RunAsync(null);
        Assert.AreEqual(0, model.MarkedCount);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePatternDialogMarksAndUnmarks()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = new MarkPatternDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        dialog.Target = model;
        await ShowAsync(dialog);

        dialog.Pattern = "*.txt";
        Assert.AreEqual(2, dialog.Apply());
        CollectionAssert.AreEqual(new[] { "a.txt", "b.txt" }, model.MarkedNames);

        dialog.Marking = false;
        dialog.Pattern = "a";
        dialog.Apply();
        CollectionAssert.AreEqual(new[] { "b.txt" }, model.MarkedNames);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterInThePatternDialogAppliesAndCloses()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = new MarkPatternDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        var command = new MarkByPatternDialogCommand(dialog, Focused(model), marking: true, model);
        Assert.AreEqual(ConsoleKey.Add, command.Key!.ConsoleKey);
        var root = await ShowAsync(dialog);
        dialog.Target = model;
        dialog.Pattern = "*.jpg";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { "c.jpg" }, model.MarkedNames);
        Assert.IsFalse(root.Controls.Contains(dialog), "the dialog closed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumMinusOpensTheUnmarkDialog()
    {
        var model = MarkableTestModel.Create(_root);
        var dialog = new MarkPatternDialog(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());

        var command = new MarkByPatternDialogCommand(dialog, Focused(model), marking: false, model);

        Assert.AreEqual(ConsoleKey.Subtract, command.Key!.ConsoleKey);
        Assert.AreEqual("Unmark by pattern", command.Title);
    }
}
