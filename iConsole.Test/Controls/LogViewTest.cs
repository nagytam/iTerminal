using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class LogViewTest
{
    private static IThemeService Theme()
    {
        var themeService = new Mock<IThemeService>();
        themeService.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return themeService.Object;
    }

    private static LogView Create(int height = 4)
    {
        var view = new LogView(Theme()) { ViewSize = new Size(20, height) };
        view.SetFocus();
        return view;
    }

    private static void AppendNumbered(LogView view, int count, int from = 0)
    {
        for (int i = from; i < from + count; i++)
        {
            view.Append($"line {i:00}");
        }
    }

    private static KeyEvent Key(ConsoleKey key) => new(' ', key, false, false, false);

    [TestMethod]
    public void Append_SplitsOnLineBreaksAndKeepsBlankLines()
    {
        LogView view = Create();

        view.Append("a\r\nb\n\nc");

        CollectionAssert.AreEqual(new[] { "a", "b", "", "c" }, view.VisibleEntries.Select(e => e.Text).ToArray());
    }

    [TestMethod]
    public void Following_ShowsTheNewestLines()
    {
        LogView view = Create();
        AppendNumbered(view, 10);

        var screen = new ScreenBuffer(20, 4);
        view.Draw(screen);

        Assert.AreEqual("line 06", screen.Line(0).TrimEnd());
        Assert.AreEqual("line 09", screen.Line(3).TrimEnd());
        Assert.IsTrue(view.Follow);
    }

    [TestMethod]
    public async Task ScrollingUp_StopsFollowing_AndCountsUnreadLines()
    {
        LogView view = Create();
        AppendNumbered(view, 10);

        await view.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        AppendNumbered(view, 3, 10);

        Assert.IsFalse(view.Follow);
        Assert.AreEqual(5, view.TopIndex);
        Assert.AreEqual(3, view.UnreadCount);
        var screen = new ScreenBuffer(20, 4);
        view.Draw(screen);
        Assert.AreEqual("line 05", screen.Line(0).TrimEnd());
        StringAssert_Contains(screen.Line(3), "▼ 3 new");
    }

    [TestMethod]
    public async Task End_FollowsAgainAndClearsTheUnreadCount()
    {
        LogView view = Create();
        AppendNumbered(view, 10);
        await view.HandleKeyEventAsync(Key(ConsoleKey.Home));
        AppendNumbered(view, 2, 10);

        await view.HandleKeyEventAsync(Key(ConsoleKey.End));

        Assert.IsTrue(view.Follow);
        Assert.AreEqual(0, view.UnreadCount);
        Assert.AreEqual(8, view.TopIndex);
    }

    [TestMethod]
    public async Task ScrollingDownToTheBottom_FollowsAgain()
    {
        LogView view = Create();
        AppendNumbered(view, 10);
        await view.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.IsFalse(view.Follow);

        await view.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));

        Assert.IsTrue(view.Follow);
    }

    [TestMethod]
    public void MaxLines_DropsTheOldestLines()
    {
        LogView view = Create();
        view.MaxLines = 5;

        AppendNumbered(view, 12);

        Assert.AreEqual(5, view.Count);
        Assert.AreEqual("line 07", view.VisibleEntries[0].Text);
        Assert.AreEqual("line 11", view.VisibleEntries[^1].Text);
    }

    [TestMethod]
    public async Task WhenNotFollowing_DroppedLinesDoNotMoveTheView()
    {
        LogView view = Create();
        view.MaxLines = 20;
        AppendNumbered(view, 20);
        await view.HandleKeyEventAsync(Key(ConsoleKey.Home));
        await view.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        var before = new ScreenBuffer(20, 4);
        view.Draw(before);
        string firstShown = before.Line(0).TrimEnd();

        AppendNumbered(view, 2, 20);

        // Two lines were dropped from the top, so the same line is now two rows higher.
        var after = new ScreenBuffer(20, 4);
        view.Draw(after);
        Assert.AreEqual(firstShown, after.Line(0).TrimEnd());
    }

    [TestMethod]
    public void MinimumLevel_HidesLowerLevelsWithoutDeletingThem()
    {
        LogView view = Create();
        view.Append("noise", LogEntryLevel.Debug);
        view.Append("fine");
        view.Append("bad", LogEntryLevel.Error);

        view.MinimumLevel = LogEntryLevel.Warning;

        CollectionAssert.AreEqual(new[] { "bad" }, view.VisibleEntries.Select(e => e.Text).ToArray());
        Assert.AreEqual(3, view.Count);

        view.MinimumLevel = LogEntryLevel.Trace;
        Assert.AreEqual(3, view.VisibleEntries.Count);
    }

    [TestMethod]
    public void FilterText_MatchesIgnoringCaseAndAppliesToNewLines()
    {
        LogView view = Create();
        view.Append("Copied a.txt");
        view.Append("Skipped b.txt");

        view.FilterText = "copied";
        view.Append("COPIED c.txt");
        view.Append("other");

        CollectionAssert.AreEqual(new[] { "Copied a.txt", "COPIED c.txt" }, view.VisibleEntries.Select(e => e.Text).ToArray());
    }

    [TestMethod]
    public void Draw_ColoursLinesByLevel_AndFillsItsBounds()
    {
        LogView view = Create();
        view.Append("info");
        view.Append("warn", LogEntryLevel.Warning);
        view.Append("err", LogEntryLevel.Error);
        ColorScheme scheme = new();
        var screen = new ScreenBuffer(20, 4);

        view.Draw(screen);
        view.Draw(screen);

        Assert.AreEqual(scheme.Foreground, screen.ForegroundAt(0, 0));
        Assert.AreEqual(Color.Yellow, screen.ForegroundAt(0, 1));
        Assert.AreEqual(scheme.Error, screen.ForegroundAt(0, 2));
        screen.AssertFillsBounds(view);
    }

    [TestMethod]
    public void Draw_EmptyViewSaysSo_AndClearLeavesNoStaleText()
    {
        LogView view = Create();
        var screen = new ScreenBuffer(20, 4);
        view.Draw(screen);
        StringAssert_Contains(screen.Line(0), "No log lines");

        AppendNumbered(view, 4);
        view.Draw(screen);
        view.Clear();
        view.Draw(screen);

        Assert.IsFalse(screen.Contains("line 00"));
        StringAssert_Contains(screen.Line(0), "No log lines");
    }

    [TestMethod]
    public async Task LeftAndRight_ScrollSideways()
    {
        LogView view = Create();
        view.Append("0123456789012345678901234567890123456789");

        await view.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        var screen = new ScreenBuffer(20, 4);
        view.Draw(screen);

        Assert.AreEqual("89012345678901234567", screen.Line(0).TrimEnd());
    }

    [TestMethod]
    public async Task Wheel_ScrollsThreeRows_AndUnfocusedViewIgnoresKeys()
    {
        LogView view = Create(4);
        AppendNumbered(view, 20);

        await view.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 1), WheelDelta = 1 });
        Assert.AreEqual(13, view.TopIndex);

        var other = new LogView(Theme()) { ViewSize = new Size(20, 4) };
        AppendNumbered(other, 20);
        bool handled = await other.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.IsFalse(handled);
        Assert.IsTrue(other.Follow);
    }

    [TestMethod]
    public async Task AppendsFromManyThreads_AreAllKept()
    {
        LogView view = Create();
        view.MaxLines = 100_000;

        await Task.WhenAll(Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            for (int i = 0; i < 500; i++)
            {
                view.Append($"t{t} {i}");
            }
        })));

        Assert.AreEqual(4000, view.Count);
    }

    [TestMethod]
    public void OnIdle_RepaintsOnlyWhenSomethingChanged()
    {
        LogView view = Create();
        var screen = new ScreenBuffer(20, 4);
        view.Draw(screen);
        view.HasStateChanged = false;
        view.OnIdle();
        Assert.IsFalse(view.HasStateChanged);

        view.Append("x");
        view.OnIdle();

        Assert.IsTrue(view.HasStateChanged);
    }

    private static void StringAssert_Contains(string value, string expected)
    {
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(value, expected);
    }
}
