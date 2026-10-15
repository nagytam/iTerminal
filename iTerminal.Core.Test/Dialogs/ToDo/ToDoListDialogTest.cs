using iConsole.Services;
using iTerminal.Core.Dialogs.ToDo;
using iTerminal.Core.Model.ToDo;
using iTerminal.Core.Services;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.ToDo;

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

        Assert.AreEqual(0, fixture.Store.Visible(true).Count);
        Assert.IsNull(fixture.Dialog.SelectedForTest);
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
}
