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

    [TestMethod]
    public async Task TheTaskKeysAreOnlyNamedWhenATaskIsUnderTheCursor()
    {
        using var empty = new TaskManager();
        var emptyDialog = new TaskManagerDialog(empty, TestTheme());
        await ShowAsync(emptyDialog);

        Assert.IsFalse(emptyDialog.GetKeyHints().Any(hint => hint.Key == "Del"), "with no task, Del does nothing and is not named");

        using var taskManager = new TaskManager();
        taskManager.Tasks.Add(new NamedTask("Copy report.txt"));
        var dialog = await ShowWithOneTaskAsync(taskManager);

        Assert.IsTrue(dialog.GetKeyHints().Any(hint => hint.Key == "Del"));
    }

    [TestMethod]
    public async Task DeleteAsksBeforeCancellingTheTask()
    {
        using var taskManager = new TaskManager();
        var task = new NamedTask("Copy report.txt");
        taskManager.Tasks.Add(task);
        var dialog = await ShowWithOneTaskAsync(taskManager);

        Assert.IsTrue(await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete)));

        Assert.IsFalse(task.CancellationTokenSource.IsCancellationRequested, "a single Delete must only ask");
        dialog.OnIdle();
        StringAssert.Contains(dialog.Title, "Copy report.txt");
        Assert.IsTrue(dialog.GetKeyHints().Any(hint => hint.Key == "y / Del"), "the footer names the answers");
    }

    [TestMethod]
    public async Task YesCancelsTheAskedTask()
    {
        using var taskManager = new TaskManager();
        var task = new NamedTask("Copy report.txt");
        taskManager.Tasks.Add(task);
        var dialog = await ShowWithOneTaskAsync(taskManager);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Y, 'y'));

        Assert.IsTrue(task.CancellationTokenSource.IsCancellationRequested);
    }

    [TestMethod]
    public async Task DeleteAgainAlsoCancelsTheAskedTask()
    {
        using var taskManager = new TaskManager();
        var task = new NamedTask("Copy report.txt");
        taskManager.Tasks.Add(task);
        var dialog = await ShowWithOneTaskAsync(taskManager);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        Assert.IsTrue(task.CancellationTokenSource.IsCancellationRequested);
    }

    [TestMethod]
    public async Task AnyOtherKeyKeepsTheTaskRunning()
    {
        using var taskManager = new TaskManager();
        var task = new NamedTask("Copy report.txt");
        taskManager.Tasks.Add(task);
        var dialog = await ShowWithOneTaskAsync(taskManager);
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.N, 'n'));

        Assert.IsFalse(task.CancellationTokenSource.IsCancellationRequested);
        Assert.IsFalse(dialog.GetKeyHints().Any(hint => hint.Key == "y / Del"), "the question is answered");
    }

    [TestMethod]
    public async Task EscapeAnswersTheQuestionWithoutClosingTheDialog()
    {
        using var taskManager = new TaskManager();
        var task = new NamedTask("Copy report.txt");
        taskManager.Tasks.Add(task);
        var root = await ShowAsync(new TaskManagerDialog(taskManager, TestTheme()));
        var dialog = root.Controls.AsSnapshot().OfType<TaskManagerDialog>().Single();
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Title.Contains("(1)");
        }, "the list to pick up the task");
        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        Assert.IsTrue(dialog.GetKeyHints().Any(hint => hint.Key == "y / Del"), "the question is up before Esc");

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Escape));

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "Esc only takes the question away");
        Assert.IsFalse(task.CancellationTokenSource.IsCancellationRequested);
    }

    private static async Task<TaskManagerDialog> ShowWithOneTaskAsync(TaskManager taskManager)
    {
        var dialog = new TaskManagerDialog(taskManager, TestTheme());
        await ShowAsync(dialog);
        await WaitUntilAsync(() =>
        {
            dialog.OnIdle();
            return dialog.Title.Contains("(1)");
        }, "the list to pick up the task");
        return dialog;
    }
}
