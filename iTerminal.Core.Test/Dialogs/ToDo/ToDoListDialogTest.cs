using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.ToDo;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.ToDo;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class ToDoListDialogTest
{
    private sealed class Fixture
    {
        public required ToDoListDialog Dialog { get; init; }
        public required ToDoStore Store { get; init; }
    }

    private static Fixture Create()
    {
        var dateTime = new Mock<IDateTimeProviderService>();
        dateTime.Setup(d => d.Now).Returns(new DateTime(2026, 10, 14, 10, 0, 0));
        var store = new ToDoStore(new Mock<ISettingsService>().Object);
        return new Fixture { Dialog = new ToDoListDialog(dateTime.Object, store, TestTheme()), Store = store };
    }

    private static async Task TypeAsync(ToDoListDialog dialog, string text)
    {
        foreach (var c in text)
        {
            await dialog.HandleKeyEventAsync(Char(c));
        }
    }

    [TestMethod]
    public async Task ATypedTaskIsAddedAndHighlighted()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await TypeAsync(fixture.Dialog, "!Renew passport @2026-11-01");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        Assert.AreEqual("Renew passport", fixture.Dialog.SelectedForTest!.Subject);
        AssertScreenContains(Render(fixture.Dialog), "2026-11-01");
    }

    [TestMethod]
    public async Task SpaceMarksTheHighlightedTaskDoneAndTheHighlightFollowsIt()
    {
        var fixture = Create();
        fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "First", null, ToDoPriority.Normal, false));
        fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "Second", null, ToDoPriority.Normal, false));
        await ShowAsync(fixture.Dialog);
        var first = fixture.Dialog.SelectedForTest!;

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' '));

        Assert.IsTrue(fixture.Store.Visible(true).Single(t => t.Id == first.Id).IsComplete);
        Assert.AreEqual(first.Id, fixture.Dialog.SelectedForTest!.Id);
    }

    [TestMethod]
    public async Task PCyclesThePriorityOfTheHighlightedTask()
    {
        var fixture = Create();
        fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "Task", null, ToDoPriority.Normal, false));
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('p'));

        Assert.AreEqual(ToDoPriority.High, fixture.Dialog.SelectedForTest!.Priority);
    }

    [TestMethod]
    public async Task DeleteRemovesTheHighlightedTask()
    {
        var fixture = Create();
        fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "Only", null, ToDoPriority.Normal, false));
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.AreEqual(1, fixture.Store.Visible(true).Count, "Delete only asks");
        AssertScreenContains(Render(fixture.Dialog), "Delete \"Only\"? (y/n)");

        await fixture.Dialog.HandleKeyEventAsync(Char('y'));

        Assert.AreEqual(0, fixture.Store.Visible(true).Count);
        Assert.IsNull(fixture.Dialog.SelectedForTest);
    }

    [TestMethod]
    public async Task AnyKeyButYWithdrawsTheDeletion()
    {
        var fixture = Create();
        fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "Keep", null, ToDoPriority.Normal, false));
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        Assert.IsTrue(fixture.Dialog.ConfirmingDeleteForTest);
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));

        Assert.IsFalse(fixture.Dialog.ConfirmingDeleteForTest);
        Assert.AreEqual(1, fixture.Store.Visible(true).Count);
        AssertScreenDoesNotContain(Render(fixture.Dialog), "(y/n)");

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.AreEqual(1, fixture.Store.Visible(true).Count);
    }

    [TestMethod]
    public async Task DeleteWithNothingListedAsksNothing()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.IsFalse(fixture.Dialog.ConfirmingDeleteForTest);
    }

    private static Fixture CreateWithTasks(int count)
    {
        var fixture = Create();
        for (int i = 0; i < count; i++)
        {
            fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "Task " + i.ToString("00"), null, ToDoPriority.Normal, false));
        }

        return fixture;
    }

    [TestMethod]
    public async Task PageDownAndPageUpMoveASceneAtATime_AndStopAtTheEnds()
    {
        var fixture = CreateWithTasks(40);
        await ShowAsync(fixture.Dialog);
        var first = fixture.Dialog.SelectedForTest!.Subject;

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        var afterPage = fixture.Dialog.SelectedForTest!.Subject;
        Assert.AreNotEqual(first, afterPage);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        var last = fixture.Dialog.SelectedForTest!.Subject;
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(last, fixture.Dialog.SelectedForTest!.Subject);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreNotEqual(last, fixture.Dialog.SelectedForTest!.Subject);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(first, fixture.Dialog.SelectedForTest!.Subject);
    }

    // The list starts after the border, the header and one blank line of padding; columns after the border and a space.
    private static MouseEvent AtTask(ToDoListDialog dialog, int row, int column = 20, bool click = true, int wheel = 0) => new()
    {
        Position = new iConsole.Data.Position(dialog.AbsolutePosition.X + 2 + column, dialog.AbsolutePosition.Y + 2 + row),
        LeftClicked = click && wheel == 0,
        WheelDelta = wheel,
    };

    [TestMethod]
    public async Task TheWheelMovesTheHighlightThreeTasks()
    {
        var fixture = CreateWithTasks(10);
        await ShowAsync(fixture.Dialog);
        var first = fixture.Dialog.SelectedForTest!.Subject;

        Assert.IsTrue(await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 2, wheel: -1)));
        var after = fixture.Dialog.SelectedForTest!.Subject;
        Assert.AreNotEqual(first, after);
        Assert.AreEqual(fixture.Store.Visible(true)[3].Subject, after);

        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 2, wheel: 1));
        Assert.AreEqual(first, fixture.Dialog.SelectedForTest!.Subject);
    }

    [TestMethod]
    public async Task AClickHighlightsATask_AndAClickOnItsBoxMarksItDone()
    {
        var fixture = CreateWithTasks(5);
        await ShowAsync(fixture.Dialog);
        var third = fixture.Store.Visible(true)[2];

        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 2));
        Assert.AreEqual(third.Subject, fixture.Dialog.SelectedForTest!.Subject);
        Assert.IsFalse(fixture.Store.Visible(true).First(t => t.Id == third.Id).IsComplete);

        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 2, column: 1));
        Assert.IsTrue(fixture.Store.Visible(true).First(t => t.Id == third.Id).IsComplete);
        Assert.AreEqual(third.Subject, fixture.Dialog.SelectedForTest!.Subject);
    }

    [TestMethod]
    public async Task AClickBelowTheLastTaskOrOutsideTheDialogDoesNothing()
    {
        var fixture = CreateWithTasks(3);
        await ShowAsync(fixture.Dialog);
        var first = fixture.Dialog.SelectedForTest!.Subject;

        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 9));
        Assert.IsFalse(await fixture.Dialog.HandleMouseEventAsync(new MouseEvent { Position = new iConsole.Data.Position(0, 0), WheelDelta = -1 }));

        Assert.AreEqual(first, fixture.Dialog.SelectedForTest!.Subject);
    }

    [TestMethod]
    public async Task TheMouseLeavesTheListAloneWhileATaskIsBeingTypedOrADeletionAsked()
    {
        var fixture = CreateWithTasks(5);
        await ShowAsync(fixture.Dialog);
        var first = fixture.Dialog.SelectedForTest!.Subject;

        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 3));
        Assert.AreEqual(first, fixture.Dialog.SelectedForTest!.Subject);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        await fixture.Dialog.HandleKeyEventAsync(Char('d'));
        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 3, wheel: -1));
        Assert.AreEqual(first, fixture.Dialog.SelectedForTest!.Subject);
    }

    [TestMethod]
    public async Task WithMoreTasksThanRows_AClickLandsOnTheRowShownNotTheOneAtTheTop()
    {
        var fixture = CreateWithTasks(40);
        await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.End));
        var all = fixture.Store.Visible(true);

        await fixture.Dialog.HandleMouseEventAsync(AtTask(fixture.Dialog, 0));

        Assert.AreEqual(all[all.Count - 16].Subject, fixture.Dialog.SelectedForTest!.Subject);
    }

    [TestMethod]
    public async Task EPutsTheHighlightedTaskInTheEntryLine_AndEnterReplacesIt()
    {
        var fixture = Create();
        var id = Guid.NewGuid();
        fixture.Store.Add(new ToDoTask(id, "Renew passport", new DateOnly(2026, 11, 1), ToDoPriority.High, false));
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('e'));
        Assert.AreEqual("!Renew passport @2026-11-01", fixture.Dialog.EntryForTest);
        AssertScreenContains(Render(fixture.Dialog), "Change task: !Renew passport @2026-11-01");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        await TypeAsync(fixture.Dialog, "9");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        var tasks = fixture.Store.Visible(true);
        Assert.AreEqual(1, tasks.Count);
        Assert.AreEqual(id, tasks[0].Id);
        Assert.AreEqual(new DateOnly(2026, 11, 9), tasks[0].DueDate);
        Assert.AreEqual(ToDoPriority.High, tasks[0].Priority);
        Assert.AreEqual(id, fixture.Dialog.SelectedForTest!.Id);
    }

    [TestMethod]
    public async Task ChangingATask_KeepsItDone_DropsHighWhenTheBangGoes_AndKeepsLow()
    {
        var fixture = Create();
        var done = Guid.NewGuid();
        var low = Guid.NewGuid();
        fixture.Store.Add(new ToDoTask(done, "Shipped", null, ToDoPriority.High, true));
        fixture.Store.Add(new ToDoTask(low, "Maybe", null, ToDoPriority.Low, false));
        await ShowAsync(fixture.Dialog);

        // Open tasks sort first: "Maybe" is highlighted.
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.F2));
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        Assert.AreEqual(ToDoPriority.Low, fixture.Store.Visible(true).First(t => t.Id == low).Priority);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await fixture.Dialog.HandleKeyEventAsync(Char('e'));
        Assert.AreEqual("!Shipped", fixture.Dialog.EntryForTest);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Home));
        // The entry line takes no cursor keys; the bang goes by retyping the line.
        for (int i = 0; i < 8; i++)
        {
            await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        }

        await TypeAsync(fixture.Dialog, "Shipped");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));

        var changed = fixture.Store.Visible(true).First(t => t.Id == done);
        Assert.IsTrue(changed.IsComplete);
        Assert.AreEqual(ToDoPriority.Normal, changed.Priority);
    }

    [TestMethod]
    public async Task ARefusedChange_LosesNothing_AndEscapeKeepsTheTask()
    {
        var fixture = Create();
        var id = Guid.NewGuid();
        fixture.Store.Add(new ToDoTask(id, "Keep me", null, ToDoPriority.Normal, false));
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('e'));
        for (int i = 0; i < 20; i++)
        {
            await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        }

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        Assert.IsNotNull(fixture.Dialog.EntryErrorForTest);
        Assert.AreEqual("Keep me", fixture.Store.Visible(true).Single().Subject);

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsNull(fixture.Dialog.EntryForTest);
        Assert.AreEqual("Keep me", fixture.Store.Visible(true).Single().Subject);

        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        AssertScreenContains(Render(fixture.Dialog), "New task:");
    }

    [TestMethod]
    public async Task EWithNothingListedDoesNothing()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('e'));

        Assert.IsNull(fixture.Dialog.EntryForTest);
    }

    [TestMethod]
    public async Task HHidesAndShowsDoneTasks()
    {
        var fixture = Create();
        fixture.Store.Add(new ToDoTask(Guid.NewGuid(), "Finished", null, ToDoPriority.Normal, true));
        await ShowAsync(fixture.Dialog);

        await fixture.Dialog.HandleKeyEventAsync(Char('h'));
        Assert.IsNull(fixture.Dialog.SelectedForTest);

        await fixture.Dialog.HandleKeyEventAsync(Char('h'));
        Assert.AreEqual("Finished", fixture.Dialog.SelectedForTest!.Subject);
    }

    [TestMethod]
    public async Task EscapeAbandonsAnEntryButClosesTheListWithNoEntry()
    {
        var fixture = Create();
        var root = await ShowAsync(fixture.Dialog);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r'));
        await TypeAsync(fixture.Dialog, "Half typed");

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsNull(fixture.Dialog.EntryForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(fixture.Dialog));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));
        Assert.IsFalse(root.Controls.AsSnapshot().Contains(fixture.Dialog));
    }

    [TestMethod]
    public async Task AnEmptyListSaysHowToAddATask()
    {
        var fixture = Create();

        await ShowAsync(fixture.Dialog);

        AssertScreenContains(Render(fixture.Dialog), "No tasks. Press N to add one");
    }

    [TestMethod]
    public async Task CtrlVPastesTheTaskLine_AndCtrlUClearsIt()
    {
        var fixture = Create();
        await ShowAsync(fixture.Dialog);
        await iConsole.Services.TextClipboard.Current.SetTextAsync("!Renew passport" + (char)10 + "@2026-11-01");
        await fixture.Dialog.HandleKeyEventAsync(Char('n'));

        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.V, 'v', control: true));
        Assert.AreEqual("!Renew passport @2026-11-01", fixture.Dialog.EntryForTest);
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, (char)13));
        Assert.AreEqual("Renew passport", fixture.Dialog.SelectedForTest!.Subject);

        await fixture.Dialog.HandleKeyEventAsync(Char('n'));
        await TypeAsync(fixture.Dialog, "oops");
        await fixture.Dialog.HandleKeyEventAsync(Key(ConsoleKey.U, 'u', control: true));
        Assert.AreEqual(string.Empty, fixture.Dialog.EntryForTest);
    }
}
