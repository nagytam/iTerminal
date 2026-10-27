using iConsole.Controls;
using iConsole.Controls.Progress;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using StringAssert = Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert;

namespace iConsole.Test.Controls;

[TestClass]
public class ProgressGroupTest
{
    private readonly TestThemeService _theme = new();
    private readonly VirtualTimeProvider _time = new();

    private ProgressGroup Create(int width = 60, int height = 5) =>
        new(_theme, _time) { ViewSize = new Size(width, height) };

    private static ScreenBuffer Draw(Control control, int width, int height)
    {
        var screen = new ScreenBuffer(width, height);
        control.Draw(screen);
        control.Draw(screen);
        return screen;
    }

    [TestMethod]
    public void Task_TracksFractionSpeedAndRemainingTimeOnTheVirtualClock()
    {
        ProgressGroup group = Create();
        ProgressTask task = group.AddTask("Copy", 200);

        Assert.AreEqual(0, task.Speed);
        Assert.IsNull(task.Remaining, "nothing done yet, so no estimate");

        _time.Advance(TimeSpan.FromSeconds(10));
        task.Increment(50);

        Assert.AreEqual(0.25, task.Fraction, 1e-9);
        Assert.AreEqual(5, task.Speed, 1e-9);
        Assert.AreEqual(30, task.Remaining!.Value.TotalSeconds, 1e-6);
        Assert.AreEqual(TimeSpan.FromSeconds(10), task.Elapsed);
    }

    [TestMethod]
    public void Value_IsHeldBetweenZeroAndTheMaximum_AndNonFiniteInputIsIgnored()
    {
        ProgressTask task = Create().AddTask("x", 10);

        task.Increment(99);
        Assert.AreEqual(10, task.Value);
        task.Increment(double.NaN);
        task.Value = -4;
        Assert.AreEqual(0, task.Value);
        task.MaxValue = 4;
        task.Value = 3;
        task.MaxValue = 2;
        Assert.AreEqual(2, task.Value);
    }

    [TestMethod]
    public void Complete_FillsTheBarAndStopsTheClock()
    {
        ProgressGroup group = Create();
        ProgressTask task = group.AddTask("Build", 100);
        _time.Advance(TimeSpan.FromSeconds(4));

        task.Complete();
        _time.Advance(TimeSpan.FromSeconds(60));

        Assert.AreEqual(ProgressState.Succeeded, task.State);
        Assert.AreEqual(1, task.Fraction);
        Assert.AreEqual(TimeSpan.FromSeconds(4), task.Elapsed);
        Assert.IsNull(task.Remaining);
        Assert.IsTrue(group.IsCompleted);
    }

    [TestMethod]
    public void Fail_KeepsTheWorkDoneAndTheMessage_AndAFinishedTaskDoesNotChangeAgain()
    {
        ProgressTask task = Create().AddTask("Upload", 100);
        task.Increment(30);

        task.Fail("disk full");
        task.Complete();

        Assert.AreEqual(ProgressState.Failed, task.State);
        Assert.AreEqual("disk full", task.Message);
        Assert.AreEqual(30, task.Value);
    }

    [TestMethod]
    public void IsCompleted_NeedsEveryTaskFinished()
    {
        ProgressGroup group = Create();
        Assert.IsFalse(group.IsCompleted);
        ProgressTask a = group.AddTask("a");
        ProgressTask b = group.AddTask("b");
        a.Complete();
        Assert.IsFalse(group.IsCompleted);
        b.Fail();
        Assert.IsTrue(group.IsCompleted);
    }

    [TestMethod]
    public void Draw_ShowsOneRowPerTaskWithDescriptionBarAndPercent()
    {
        ProgressGroup group = Create();
        ProgressTask a = group.AddTask("Download", 100);
        group.AddTask("Unpack", 100);
        a.Increment(50);

        ScreenBuffer screen = Draw(group, 60, 5);

        StringAssert.Contains(screen.Line(0), "Download");
        StringAssert.Contains(screen.Line(0), "50%");
        StringAssert.Contains(screen.Line(1), "Unpack");
        StringAssert.Contains(screen.Line(1), "0%");
        Assert.IsTrue(screen.Line(0).Count(c => c == '█') > 5);
        Assert.IsTrue(screen.Line(0).Count(c => c == '░') > 5);
        Assert.AreEqual(Color.Cyan, screen.ForegroundAt(screen.Line(0).IndexOf('█'), 0));
    }

    [TestMethod]
    public void Draw_MarksFinishedTasksWithATickOrACross()
    {
        ProgressGroup group = Create();
        group.AddTask("ok", 10).Complete();
        group.AddTask("bad", 10).Fail("nope");

        ScreenBuffer screen = Draw(group, 60, 5);

        Assert.AreEqual('✔', screen.CharAt(0, 0));
        Assert.AreEqual(Color.Green, screen.ForegroundAt(0, 0));
        Assert.AreEqual('✘', screen.CharAt(0, 1));
        Assert.AreEqual(Color.Red, screen.ForegroundAt(0, 1));
        StringAssert.Contains(screen.Line(1), "bad: nope");
    }

    [TestMethod]
    public void Draw_ShowsSpeedElapsedAndEstimateWhenAsked()
    {
        ProgressGroup group = Create(90, 3);
        group.Columns = ProgressColumns.Description | ProgressColumns.Bar | ProgressColumns.Speed | ProgressColumns.Elapsed | ProgressColumns.Remaining;
        group.Unit = "MB";
        ProgressTask task = group.AddTask("Copy", 100);
        _time.Advance(TimeSpan.FromSeconds(10));
        task.Increment(25);

        ScreenBuffer screen = Draw(group, 90, 3);

        StringAssert.Contains(screen.Line(0), "2.5 MB/s");
        StringAssert.Contains(screen.Line(0), "0:10");
        StringAssert.Contains(screen.Line(0), "ETA 0:30");
    }

    [TestMethod]
    public void Draw_AnIndeterminateBarSweepsWithTheClock()
    {
        ProgressGroup group = Create(40, 2);
        group.AddTask("Scan", 0, isIndeterminate: true);
        int First() => Draw(group, 40, 2).Line(0).IndexOf('█');

        int before = First();
        _time.Advance(TimeSpan.FromMilliseconds(300));
        int after = First();

        Assert.AreNotEqual(before, after);
        StringAssert.DoesNotMatch(Draw(group, 40, 2).Line(0), new System.Text.RegularExpressions.Regex("%"));
    }

    [TestMethod]
    public void Draw_WithMoreTasksThanRows_ShowsTheNewest()
    {
        ProgressGroup group = Create(40, 2);
        group.AddTask("first");
        group.AddTask("second");
        group.AddTask("third");

        ScreenBuffer screen = Draw(group, 40, 2);

        Assert.IsFalse(screen.Contains("first"));
        StringAssert.Contains(screen.Line(1), "third");
    }

    [TestMethod]
    public void Draw_HideFinishedDropsDoneRows_AndOverallSumsTheTasks()
    {
        ProgressGroup group = Create(60, 4);
        group.HideFinished = true;
        group.ShowOverall = true;
        group.AddTask("done", 100).Complete();
        ProgressTask running = group.AddTask("busy", 100);
        running.Increment(50);

        ScreenBuffer screen = Draw(group, 60, 4);

        Assert.IsFalse(screen.Contains("done"));
        StringAssert.Contains(screen.Line(0), "busy");
        StringAssert.Contains(screen.Line(1), "Total");
        StringAssert.Contains(screen.Line(1), "75%");
    }

    [TestMethod]
    public void Draw_NoTasksSaysSo_AndTinyOrNarrowSizesDoNotThrow()
    {
        ProgressGroup group = Create();
        Assert.IsTrue(Draw(group, 60, 5).Contains("No tasks"));

        group.AddTask("A fairly long description indeed", 10).Increment(5);
        group.Columns = ProgressColumns.Description | ProgressColumns.Bar | ProgressColumns.Percent | ProgressColumns.Speed | ProgressColumns.Elapsed | ProgressColumns.Remaining;
        foreach ((int w, int h) in new[] { (60, 5), (20, 2), (8, 1), (3, 1), (1, 1) })
        {
            group.ViewSize = new Size(w, h);
            Draw(group, w, h);
        }

        group.ViewSize = new Size(60, 5);
        Draw(group, 60, 5).AssertFillsBounds(group);
    }

    [TestMethod]
    public void OnIdle_RepaintsOnAChangeOrAPassingMoment_NotWhenNothingMoves()
    {
        ProgressGroup group = Create();
        ProgressTask task = group.AddTask("x", 10);
        Draw(group, 60, 5);
        group.HasStateChanged = false;

        group.OnIdle();
        Assert.IsFalse(group.HasStateChanged, "nothing changed since the draw");

        task.Increment(1);
        group.OnIdle();
        Assert.IsTrue(group.HasStateChanged);

        Draw(group, 60, 5);
        group.HasStateChanged = false;
        _time.Advance(TimeSpan.FromSeconds(1));
        group.OnIdle();
        Assert.IsTrue(group.HasStateChanged, "the clock moved while a task runs");

        task.Complete();
        Draw(group, 60, 5);
        group.HasStateChanged = false;
        _time.Advance(TimeSpan.FromSeconds(5));
        group.OnIdle();
        Assert.IsFalse(group.HasStateChanged, "all finished: time alone repaints nothing");
    }

    [TestMethod]
    public async Task ManyThreadsUpdatingOneTask_AddUpExactly()
    {
        ProgressGroup group = Create();
        ProgressTask task = group.AddTask("n", 100_000);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 1000; i++)
            {
                task.Increment(1);
            }
        })));

        Assert.AreEqual(8000, task.Value);
    }

    [TestMethod]
    [DataRow(null, "--:--")]
    [DataRow(0, "0:00")]
    [DataRow(75, "1:15")]
    [DataRow(3725, "1:02:05")]
    [DataRow(-5, "--:--")]
    public void FormatDuration_ReadsAsMinutesOrHours(int? seconds, string expected)
    {
        Assert.AreEqual(expected, ProgressGroup.FormatDuration(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)));
    }

    [TestMethod]
    public void FormatSpeed_UsesFewDigits()
    {
        Assert.AreEqual("250/s", ProgressGroup.FormatSpeed(250, string.Empty));
        Assert.AreEqual("12/s", ProgressGroup.FormatSpeed(12.4, string.Empty));
        Assert.AreEqual("3.5 MB/s", ProgressGroup.FormatSpeed(3.5, "MB"));
        Assert.AreEqual("0.25/s", ProgressGroup.FormatSpeed(0.25, string.Empty));
    }
}
