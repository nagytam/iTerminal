using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Tasks;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Progress;

/// <summary>The time readout follows its task: it redraws while the task runs, labels the total once it is over, and takes the theme's colours.</summary>
[TestClass]
public class ProgressTimeStateTest
{
    private sealed class FakeTask : BackgroundTask
    {
        public FakeTask() => Name = "task";

        public void SetStatus(iConsole.Data.TaskStatus status) => Status = status;

        public override Task ExecuteAsync() => Task.CompletedTask;
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static string Drawn(ProgressTime time)
    {
        MockBuffer buffer = new(new Size(60, 1));
        time.Draw(buffer);
        return buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void WhileTheTaskRuns_ItShowsElapsedAndRemaining_AndRedrawsOnIdle()
    {
        FakeTask task = new();
        ProgressTime time = new(task, Theme()) { Position = new Position(0, 0), ViewSize = new Size(60, 1) };
        MockBuffer buffer = new(new Size(60, 1));
        time.Render(buffer);
        Assert.IsTrue(time.Render(buffer), "nothing changed since the last frame");

        time.OnIdle();
        Assert.IsFalse(time.Render(buffer), "a running task is redrawn on idle, so its clock moves");

        string line = Drawn(time);
        Assert.IsTrue(line.StartsWith("Elapsed time: "));
        Assert.IsTrue(line.Contains("Remaining time: "));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnceTheTaskIsOver_ItShowsTheTotal_AndStopsRedrawing()
    {
        FakeTask task = new();
        ProgressTime time = new(task, Theme()) { Position = new Position(0, 0), ViewSize = new Size(60, 1) };
        task.SetStatus(iConsole.Data.TaskStatus.Completed);
        MockBuffer buffer = new(new Size(60, 1));
        time.Render(buffer);

        time.OnIdle();
        Assert.IsTrue(time.Render(buffer), "a finished task has nothing to redraw");
        string line = Drawn(time);
        Assert.IsTrue(line.StartsWith("Total time: "));
        Assert.IsFalse(line.Contains("Remaining"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLabelsCanBeChanged()
    {
        FakeTask task = new();
        ProgressTime time = new(task, Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(70, 1),
            LabelElapsed = "Gone",
            LabelRemaining = "Left",
        };
        string line = Drawn(time);
        Assert.IsTrue(line.StartsWith("Gone: "));
        Assert.IsTrue(line.Contains("Left: "));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetColor_FollowsTheVisualState()
    {
        ProgressTime time = new(new FakeTask(), Theme());
        ColorScheme scheme = new();
        time.GetColor(scheme, VisualState.Disabled, out Color disabledFg, out Color disabledBg);
        Assert.AreEqual((scheme.DisabledForeground, scheme.DisabledBackground), (disabledFg, disabledBg));
        time.GetColor(scheme, VisualState.Focused, out Color focusedFg, out Color focusedBg);
        Assert.AreEqual((scheme.SelectedFocusedForeground, scheme.SelectedBackground), (focusedFg, focusedBg));
        time.GetColor(scheme, VisualState.Selected, out Color selectedFg, out Color selectedBg);
        Assert.AreEqual((scheme.SelectedFocusedForeground, scheme.SelectedBackground), (selectedFg, selectedBg));
        time.GetColor(scheme, VisualState.Normal, out Color normalFg, out Color normalBg);
        Assert.AreEqual((scheme.Foreground, scheme.Background), (normalFg, normalBg));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ANarrowView_DoesNotThrow()
    {
        ProgressTime time = new(new FakeTask(), Theme()) { Position = new Position(0, 0), ViewSize = new Size(1, 1) };
        time.Draw(new MockBuffer(new Size(3, 1)));
        time.ViewSize = new Size(0, 1);
        time.Draw(new MockBuffer(new Size(3, 1)));
    }
}
