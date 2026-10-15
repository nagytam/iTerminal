using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Display;
using iConsole.Services;
using iConsole.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iTerminal.Core.Controls.PopUpWindows;

namespace iTerminal.Core.Controls.PopUpWindows.Test;

[TestClass]
public class ProgressWindowTest
{
    /// <summary>A task whose outcome the test decides, run through the real <see cref="BackgroundTask.RunAsync"/>
    /// so that <see cref="BackgroundTask.Status"/> is set the way a production task sets it.</summary>
    private sealed class FakeTask(string? error = null, bool cancel = false) : BackgroundTask
    {
        public override Task ExecuteAsync()
        {
            if (cancel)
            {
                CancellationTokenSource.Cancel();
            }
            Error = error;
            return Task.CompletedTask;
        }
    }

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    /// <summary>The window as it lives in production: a child of the root control.</summary>
    private static (Control Parent, ProgressWindow Window) CreateShownWindow(BackgroundTask task)
    {
        var themeService = CreateThemeService();
        var parent = new Control(themeService);
        var window = new ProgressWindow(task, themeService)
        {
            IsVisible = true
        };
        parent.AddControl(window);
        return (parent, window);
    }

    [TestMethod]
    public void OnIdle_WhileTaskIsRunning_KeepsTheWindowOpen()
    {
        var task = new FakeTask();
        var (parent, window) = CreateShownWindow(task);

        window.OnIdle();

        Assert.IsTrue(window.IsVisible);
        Assert.IsTrue(parent.Controls.Contains(window));
    }

    [TestMethod]
    public async Task OnIdle_AfterTaskCompletes_ClosesAndRemovesTheWindow()
    {
        var task = new FakeTask();
        var (parent, window) = CreateShownWindow(task);
        await task.RunAsync();

        window.OnIdle();

        Assert.IsFalse(window.IsVisible, "A finished operation should not leave its progress window on screen.");
        Assert.IsFalse(
            parent.Controls.Contains(window),
            "The window is modal, so hiding it is not enough — it has to leave the control tree or it keeps taking every key.");
    }

    [TestMethod]
    public async Task OnIdle_WhenTaskFinishedBeforeTheWindowWasBuilt_StillCloses()
    {
        // A small local file copy is regularly over before the command gets round to creating the
        // window, so there is no completion event left to listen for.
        var task = new FakeTask();
        await task.RunAsync();
        var (parent, window) = CreateShownWindow(task);

        window.OnIdle();

        Assert.IsFalse(parent.Controls.Contains(window));
    }

    [TestMethod]
    public async Task OnIdle_AfterTaskFails_LeavesTheWindowOpen()
    {
        var task = new FakeTask(error: "Access to the path is denied.");
        var (parent, window) = CreateShownWindow(task);
        await task.RunAsync();

        window.OnIdle();

        Assert.IsTrue(window.IsVisible, "The failure message lives only on this window; dismissing it would hide why nothing happened.");
        Assert.IsTrue(parent.Controls.Contains(window));
    }

    [TestMethod]
    public async Task OnIdle_AfterTaskIsCancelled_ClosesTheWindow()
    {
        var task = new FakeTask(cancel: true);
        var (parent, window) = CreateShownWindow(task);
        await task.RunAsync();

        Assert.AreEqual(iConsole.Data.TaskStatus.Canceled, task.Status);

        window.OnIdle();

        Assert.IsFalse(parent.Controls.Contains(window));
    }

    [TestMethod]
    public async Task OnIdle_AfterTheWindowWasAlreadyClosed_IsHarmless()
    {
        // Dismissed with [X] while the operation carries on, the window can still be idled once
        // more by a pass that snapshotted the tree before the removal.
        var task = new FakeTask();
        var (parent, window) = CreateShownWindow(task);

        window.Close();
        await task.RunAsync();
        window.OnIdle();

        Assert.IsFalse(parent.Controls.Contains(window));
    }

    [TestMethod]
    public async Task ActionButton_ClosesRatherThanHides()
    {
        var task = new FakeTask();
        var (parent, window) = CreateShownWindow(task);
        await window.OnInitializeAsync();

        var actionButton = window.Controls.OfType<ActionButton>().Single();
        var command = actionButton.Commands.First();

        Assert.IsInstanceOfType<CloseParentCommand>(command);

        await command.RunAsync(null);

        Assert.IsFalse(
            parent.Controls.Contains(window),
            "[X] must remove the modal window, not just hide it — a hidden modal still swallows every key.");
    }
}
