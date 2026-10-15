using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Tasks;
using iConsole.Test.Core;
using iTerminal.Core.Controls.PopUpWindows;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Controls.PopUpWindows;

/// <summary>What the copy/move/delete progress window says, and what its one button does.</summary>
[TestClass]
public class ProgressWindowPresentationTest
{
    private sealed class FakeTask : BackgroundTask
    {
        private readonly string? error;

        public FakeTask(string description, string? error = null)
        {
            Description = description;
            this.error = error;
        }

        public override Task ExecuteAsync()
        {
            Error = error;
            return Task.CompletedTask;
        }
    }

    private static (Control Parent, ProgressWindow Window) Show(BackgroundTask task)
    {
        var parent = new Control(TestTheme()) { ViewSize = new Size(120, 40) };
        var window = new ProgressWindow(task, TestTheme()) { IsVisible = true };
        parent.AddControl(window);
        window.OnInitializeLayout();
        return (parent, window);
    }

    private static ScreenBuffer Render(ProgressWindow window)
    {
        var buffer = new ScreenBuffer(120, 40);
        window.Draw(buffer);
        return buffer;
    }

    [TestMethod]
    public void WhileTheTaskRunsTheButtonCancels()
    {
        var task = new FakeTask("Copying report.txt");
        var (parent, window) = Show(task);

        Assert.AreEqual("Cancel", window.CancelButtonTitleForTest);

        var button = window.Controls.AsSnapshot().OfType<Button>().Single(b => b is not ActionButton);
        button.TriggerHotKeyAsync().GetAwaiter().GetResult();

        Assert.IsTrue(task.CancellationTokenSource.IsCancellationRequested);
        Assert.IsFalse(parent.Controls.AsSnapshot().Contains(window));
    }

    [TestMethod]
    public async Task OnceTheTaskHasFailedTheButtonCloses()
    {
        // A failed run keeps the window up to show why; a "Cancel" under it read as if something
        // were still going on.
        var task = new FakeTask("Copying report.txt", error: "Access denied.");
        var (parent, window) = Show(task);
        await task.RunAsync();

        Assert.AreEqual("Close", window.CancelButtonTitleForTest);

        var button = window.Controls.AsSnapshot().OfType<Button>().Single(b => b is not ActionButton);
        await button.TriggerHotKeyAsync();

        Assert.IsFalse(task.CancellationTokenSource.IsCancellationRequested, "nothing left to cancel");
        Assert.IsFalse(parent.Controls.AsSnapshot().Contains(window));
    }

    [TestMethod]
    public async Task ALongErrorIsWrappedNotCutAtTheBorder()
    {
        var error = "The process cannot access the file because it is being used by another process, " +
                    "so the copy stopped after 12 of 40 files; nothing was overwritten.";
        var task = new FakeTask("Copying", error);
        var (_, window) = Show(task);
        await task.RunAsync();

        var screen = Render(window);

        AssertScreenContains(screen, "overwritten.", "the end of the message is on screen");
    }

    [TestMethod]
    public async Task TheErrorIsDrawnInTheErrorColour()
    {
        var task = new FakeTask("Copying", "Access denied.");
        var (_, window) = Show(task);
        await task.RunAsync();

        var screen = Render(window);

        Assert.AreEqual(TestTheme().CurrentColorScheme.Error, screen.CellOf("Task failed").ForegroundColor);
    }

    [TestMethod]
    public void EveryDetailRowIsPaintedEdgeToEdge()
    {
        // A shorter line used to leave the end of the longer one before it on screen.
        var (_, window) = Show(new FakeTask("x"));
        var screen = Render(window);

        var x0 = (int)window.AbsolutePosition.X + 1;
        var width = (int)window.ViewSize.Width - 2;
        var row = (int)window.AbsolutePosition.Y + 5;

        Assert.IsTrue(Enumerable.Range(x0, width).All(x => screen.IsWritten(x, row)));
    }

    [TestMethod]
    public void DetailLinesWrapEachLineOnItsOwn()
    {
        var lines = ProgressWindow.DetailLines(string.Join(Environment.NewLine, "alpha beta gamma", string.Empty, "delta"), 10);

        CollectionAssert.AreEqual(new[] { "alpha beta", "gamma", "", "delta" }, lines);
    }
}
