using iConsole.Controls;
using iConsole.Controls.Viewers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class StatusAndClockTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private sealed class FakeClock : IDateTimeProviderService
    {
        public DateTime Time { get; set; } = new(2026, 10, 18, 12, 0, 0);

        public DateTime Now => Time;

        public DateTime UtcNow => Time.ToUniversalTime();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_KeyHints_NameSpaceForTheCurrentStateAndR()
    {
        var clock = new ElapsedClock(Theme(), new FakeClock()) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };

        var hints = clock.GetKeyHints().ToDictionary(h => h.Key, h => h.Description);
        Assert.AreEqual("Start the clock", hints["Space"]);
        Assert.IsTrue(hints.ContainsKey("R"));

        clock.Start();
        Assert.AreEqual("Stop the clock", clock.GetKeyHints().Single(h => h.Key == "Space").Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StatusDot_Draw_ShowsDotAndText()
    {
        MockBuffer buffer = new(new Size(20, 1));
        new StatusDot(Theme()) { Text = "Connected", Kind = BadgeKind.Success, Position = new Position(0, 0), ViewSize = new Size(20, 1) }.Draw(buffer);
        Assert.AreEqual("● Connected", Lines(buffer)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StatusDot_ShorterText_LeavesNothingBehind()
    {
        MockBuffer buffer = new(new Size(20, 1));
        StatusDot dot = new(Theme()) { Text = "Connected", Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        dot.Draw(buffer);
        dot.Text = "Off";
        dot.Draw(buffer);
        Assert.AreEqual("● Off", Lines(buffer)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void StatusDot_NaturalWidth_FollowsText()
    {
        Assert.AreEqual(1, new StatusDot(Theme()).NaturalWidth);
        Assert.AreEqual(11, new StatusDot(Theme()) { Text = "Connected" }.NaturalWidth);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_Format_RoundsDownAndShowsHours()
    {
        Assert.AreEqual("00:59", ElapsedClock.Format(TimeSpan.FromMilliseconds(59900)));
        Assert.AreEqual("1:01:05", ElapsedClock.Format(TimeSpan.FromSeconds(3665)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_AddsUpAcrossRestartsUntilReset()
    {
        FakeClock clock = new();
        ElapsedClock watch = new(Theme(), clock);
        watch.Start();
        clock.Time = clock.Time.AddSeconds(10);
        watch.Stop();
        clock.Time = clock.Time.AddSeconds(100);
        Assert.AreEqual(TimeSpan.FromSeconds(10), watch.Elapsed);

        watch.Start();
        clock.Time = clock.Time.AddSeconds(5);
        Assert.AreEqual(TimeSpan.FromSeconds(15), watch.Elapsed);

        watch.Reset();
        Assert.AreEqual(TimeSpan.Zero, watch.Elapsed);
        Assert.IsTrue(watch.IsRunning);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ElapsedClock_SpaceTogglesAndRResets()
    {
        FakeClock clock = new();
        ElapsedClock watch = new(Theme(), clock);
        watch.SetFocus();
        await watch.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        Assert.IsTrue(watch.IsRunning);
        clock.Time = clock.Time.AddSeconds(3);
        await watch.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false));
        Assert.IsFalse(watch.IsRunning);
        await watch.HandleKeyEventAsync(new KeyEvent('r', ConsoleKey.R, false, false, false));
        Assert.AreEqual(TimeSpan.Zero, watch.Elapsed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_Draw_ShowsLabelAndTime()
    {
        FakeClock clock = new();
        ElapsedClock watch = new(Theme(), clock) { Label = "Elapsed", Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        watch.Start();
        clock.Time = clock.Time.AddSeconds(75);
        MockBuffer buffer = new(new Size(20, 1));
        watch.Draw(buffer);
        Assert.AreEqual("Elapsed 01:15", Lines(buffer)[0].TrimEnd());
    }

    private static TextViewerControl CreateViewer(bool cursor)
    {
        TextViewerControl viewer = new(cursor, Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 6) };
        viewer.TextModel.Lines = ["alpha", "beta", "gamma delta", "x"];
        viewer.ScrollableModel.ScrollableSize = new Size(11, 4);
        viewer.ScrollableModel.ViewSize = new Size(39, 5);
        return viewer;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextViewer_ClickInEditor_PlacesTheCaret()
    {
        TextViewerControl viewer = CreateViewer(true);
        await viewer.HandleMouseEventAsync(Click(3, 2));
        Assert.AreEqual(2, (int)viewer.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual(3, (int)viewer.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextViewer_ClickPastTheEndOfALine_LandsOnItsEnd()
    {
        TextViewerControl viewer = CreateViewer(true);
        await viewer.HandleMouseEventAsync(Click(30, 1));
        Assert.AreEqual(1, (int)viewer.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual(4, (int)viewer.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextViewer_ClickInReadOnlyViewer_MovesTheRowOnly()
    {
        TextViewerControl viewer = CreateViewer(false);
        await viewer.HandleMouseEventAsync(Click(3, 3));
        Assert.AreEqual(3, (int)viewer.ScrollableModel.CursorPosition.Y);
        Assert.AreEqual(0, (int)viewer.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextViewer_ClickBelowTheLastLine_SelectsTheLastLine()
    {
        TextViewerControl viewer = CreateViewer(true);
        await viewer.HandleMouseEventAsync(Click(1, 4));
        Assert.AreEqual(3, (int)viewer.ScrollableModel.CursorPosition.Y);
    }
}
