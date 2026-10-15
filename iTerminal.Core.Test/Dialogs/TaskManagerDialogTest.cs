using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Tasks;
using iTerminal.Core.Dialogs;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Dialogs.Test;

[TestClass]
public class TaskManagerDialogTest
{
    private sealed class NamedTask(string name) : BackgroundTask
    {
        public override string DisplayName => name;

        public override Task ExecuteAsync() => Task.CompletedTask;
    }

    [TestMethod]
    public async Task WithNothingRunningItSaysSo()
    {
        using var taskManager = new TaskManager();
        var dialog = new TaskManagerDialog(taskManager, TestTheme());
        await ShowAsync(dialog);

        AssertScreenContains(Render(dialog), "No background tasks are running.");
    }

    [TestMethod]
    public async Task ListsTheQueuedTasksAndCountsThemInTheTitle()
    {
        using var taskManager = new TaskManager();
        taskManager.Tasks.Add(new NamedTask("Copy report.txt"));
        taskManager.Tasks.Add(new NamedTask("Index documents"));
        var dialog = new TaskManagerDialog(taskManager, TestTheme());
        await ShowAsync(dialog);

        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Title == "Task Manager (2)";
        }, "the list to pick up both tasks");
        var screen = Render(dialog);
        AssertScreenContains(screen, "Copy report.txt");
        AssertScreenContains(screen, "Index documents");
    }

    [TestMethod]
    public async Task TheTitleIsSpelledTheSameBeforeAndAfterTheFirstRefresh()
    {
        using var taskManager = new TaskManager();
        var dialog = new TaskManagerDialog(taskManager, TestTheme());

        StringAssert.StartsWith(dialog.Title, "Task Manager");
        await ShowAsync(dialog);
        dialog.OnIdle();
        StringAssert.StartsWith(dialog.Title, "Task Manager");
    }

    [TestMethod]
    public async Task TheListFillsTheDialog()
    {
        using var taskManager = new TaskManager();
        var dialog = new TaskManagerDialog(taskManager, TestTheme());
        await ShowAsync(dialog);

        var list = dialog.Controls.AsSnapshot().OfType<iConsole.Controls.ListBox<IBackgroundTask>>().Single();

        Assert.AreEqual(dialog.ViewSize.Width - 2, list.ViewSize.Width);
        Assert.AreEqual(dialog.ViewSize.Height - 2, list.ViewSize.Height);
    }

    [TestMethod]
    public async Task EscapeClosesIt()
    {
        using var taskManager = new TaskManager();
        var dialog = new TaskManagerDialog(taskManager, TestTheme());
        var root = await ShowAsync(dialog);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsFalse(root.Controls.AsSnapshot().Contains(dialog));
    }
}
