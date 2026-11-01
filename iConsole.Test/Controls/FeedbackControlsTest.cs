using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class FeedbackControlsTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    private sealed class FakeClock : IDateTimeProviderService
    {
        public DateTime Time { get; set; } = new(2026, 10, 18, 12, 0, 0);

        public DateTime Now => Time;

        public DateTime UtcNow => Time.ToUniversalTime();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Countdown_Format_RoundsUpAndShowsHours()
    {
        Assert.AreEqual("01:30", Countdown.Format(TimeSpan.FromSeconds(90)));
        Assert.AreEqual("00:01", Countdown.Format(TimeSpan.FromMilliseconds(200)));
        Assert.AreEqual("1:01:05", Countdown.Format(TimeSpan.FromSeconds(3665)));
        Assert.AreEqual("00:00", Countdown.Format(TimeSpan.FromSeconds(-4)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Countdown_Idle_ShowsDashes()
    {
        MockBuffer buffer = new(new Size(8, 1));
        new Countdown(Theme(), new FakeClock()) { Position = new Position(0, 0) }.Draw(buffer);
        Assert.AreEqual("--:--", Lines(buffer)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Countdown_OnIdle_CountsDownAndRaisesFinishedOnce()
    {
        FakeClock clock = new();
        Countdown countdown = new(Theme(), clock) { Position = new Position(0, 0) };
        int finished = 0;
        countdown.Finished += (_, _) => finished++;
        countdown.Start(TimeSpan.FromSeconds(5));

        MockBuffer buffer = new(new Size(8, 1));
        countdown.Draw(buffer);
        Assert.AreEqual("00:05", Lines(buffer)[0].TrimEnd());

        clock.Time = clock.Time.AddSeconds(3);
        countdown.OnIdle();
        countdown.Draw(buffer);
        Assert.AreEqual("00:02", Lines(buffer)[0].TrimEnd());
        Assert.AreEqual(0, finished);

        clock.Time = clock.Time.AddSeconds(3);
        countdown.OnIdle();
        countdown.OnIdle();
        Assert.AreEqual(1, finished);
        Assert.IsTrue(countdown.IsFinished);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Skeleton_LineLength_LastLineIsShorter()
    {
        Assert.AreEqual(30, Skeleton.LineLength(0, 3, 30));
        Assert.AreEqual(18, Skeleton.LineLength(2, 3, 30));
        Assert.AreEqual(30, Skeleton.LineLength(0, 1, 30));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Skeleton_BuildLine_PutsBandAtOffsetAndWraps()
    {
        Assert.AreEqual("▒▒▒▒░░░░░░", Skeleton.BuildLine(10, 0));
        Assert.AreEqual("░░▒▒▒▒░░░░", Skeleton.BuildLine(10, 2));
        Assert.AreEqual("▒▒░░░░░░░░", Skeleton.BuildLine(10, 12));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Skeleton_OnIdle_AdvancesBandWithTheClock()
    {
        FakeClock clock = new();
        Skeleton skeleton = new(Theme(), clock);
        skeleton.OnIdle();
        Assert.AreEqual(0, skeleton.Offset);
        clock.Time = clock.Time.AddMilliseconds(100);
        skeleton.OnIdle();
        Assert.AreEqual(1, skeleton.Offset);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Skeleton_Inactive_BlanksItsRows()
    {
        MockBuffer buffer = new(new Size(30, 3));
        Skeleton skeleton = new(Theme(), new FakeClock()) { Position = new Position(0, 0) };
        skeleton.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[0].Contains('░') || Lines(buffer)[0].Contains('▒'));
        skeleton.IsActive = false;
        skeleton.Draw(buffer);
        Assert.AreEqual(string.Empty, Lines(buffer)[0].Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Alert_Dismissible_ClickOnCrossClearsAndRaises()
    {
        Alert alert = new(Theme()) { Text = "Saved", Position = new Position(0, 0), ViewSize = new Size(20, 1), IsDismissible = true };
        int dismissed = 0;
        alert.Dismissed += (_, _) => dismissed++;
        await alert.HandleMouseEventAsync(Click(19, 0));
        Assert.AreEqual(string.Empty, alert.Text);
        Assert.AreEqual(1, dismissed);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Alert_NotDismissible_IgnoresClick()
    {
        Alert alert = new(Theme()) { Text = "Saved", Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        await alert.HandleMouseEventAsync(Click(19, 0));
        Assert.AreEqual("Saved", alert.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Alert_Dismissible_EscapeDismissesWhenFocused()
    {
        Alert alert = new(Theme()) { Text = "Saved", ViewSize = new Size(20, 1), IsDismissible = true };
        alert.SetFocus();
        await alert.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, false, false, false));
        Assert.AreEqual(string.Empty, alert.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Alert_Dismissible_DrawsCrossAndKeepsMessageClear()
    {
        MockBuffer buffer = new(new Size(20, 1));
        new Alert(Theme()) { Text = "Saved", Position = new Position(0, 0), ViewSize = new Size(20, 1), IsDismissible = true }.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[0].TrimEnd().EndsWith("×"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StepIndicator_ClickOnGlyph_RaisesStepClicked()
    {
        StepIndicator steps = new(Theme()) { Count = 4, Position = new Position(0, 0) };
        int clicked = -1;
        steps.StepClicked += (_, i) => clicked = i;
        await steps.HandleMouseEventAsync(Click(1, 0));
        Assert.AreEqual(-1, clicked);
        await steps.HandleMouseEventAsync(Click(4, 0));
        Assert.AreEqual(2, clicked);
    }
}
