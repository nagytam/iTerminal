using iConsole.Controls;
using iConsole.Controls.Experimental;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls.Experimental;

/// <summary>The bouncing label's movement: turning round at the edges, never sticking, and the background task.</summary>
[TestClass]
public class BouncingLabelMotionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static BouncingLabel NewLabel(long x, long y, string text = "ab", int dx = 1, int dy = 1)
        => new(new Position(x, y), text, Theme(), autoStart: false) { BounceX = dx, BounceY = dy };

    [Timeout(60_000)]
    [TestMethod]
    public void Step_MovesOneCellAlongTheDirection()
    {
        BouncingLabel label = NewLabel(5, 2);
        Assert.IsTrue(label.Step(20, 10));
        Assert.AreEqual(6L, label.Position.X);
        Assert.AreEqual(3L, label.Position.Y);
        label.BounceX = -1;
        label.BounceY = -1;
        label.Step(20, 10);
        Assert.AreEqual(5L, label.Position.X);
        Assert.AreEqual(2L, label.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_AtTheRightEdge_TurnsRoundInsteadOfSticking()
    {
        // "ab" at column 17 ends at 18; one cell must stay clear, so column 18 is off limits in a 20-wide parent.
        BouncingLabel label = NewLabel(17, 2, dy: 1);
        Assert.IsTrue(label.Step(20, 10));
        Assert.AreEqual(16L, label.Position.X);
        Assert.AreEqual(-1, label.BounceX);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_AtTheLeftEdge_TurnsRound()
    {
        BouncingLabel label = NewLabel(0, 2, dx: -1);
        label.Step(20, 10);
        Assert.AreEqual(1L, label.Position.X);
        Assert.AreEqual(1, label.BounceX);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_AtTheBottomAndTopEdges_TurnsRound()
    {
        BouncingLabel bottom = NewLabel(5, 9, dy: 1);
        bottom.Step(20, 10);
        Assert.AreEqual(8L, bottom.Position.Y);
        Assert.AreEqual(-1, bottom.BounceY);

        BouncingLabel top = NewLabel(5, 0, dy: -1);
        top.Step(20, 10);
        Assert.AreEqual(1L, top.Position.Y);
        Assert.AreEqual(1, top.BounceY);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_ManyTimes_StaysInsideAndReachesEveryEdge()
    {
        BouncingLabel label = NewLabel(3, 1, text: "abc", dx: 1, dy: 1);
        long minX = long.MaxValue, maxX = long.MinValue, minY = long.MaxValue, maxY = long.MinValue;
        for (int i = 0; i < 400; i++)
        {
            label.Step(20, 6);
            Assert.IsTrue(label.Position.X >= 0 && label.Position.X + 3 < 20, $"x {label.Position.X}");
            Assert.IsTrue(label.Position.Y >= 0 && label.Position.Y < 6, $"y {label.Position.Y}");
            minX = Math.Min(minX, label.Position.X);
            maxX = Math.Max(maxX, label.Position.X);
            minY = Math.Min(minY, label.Position.Y);
            maxY = Math.Max(maxY, label.Position.Y);
        }

        Assert.AreEqual(0L, minX);
        Assert.AreEqual(16L, maxX);
        Assert.AreEqual(0L, minY);
        Assert.AreEqual(5L, maxY);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_WithoutADirection_StartsMovingDownAndRight()
    {
        BouncingLabel label = NewLabel(2, 2, dx: 0, dy: 0);
        label.Step(20, 10);
        Assert.AreEqual(3L, label.Position.X);
        Assert.AreEqual(3L, label.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_InAParentTooSmallForTheLabel_DoesNotMove()
    {
        BouncingLabel label = NewLabel(0, 0, text: "a long label");
        label.Step(5, 10);
        Assert.AreEqual(0L, label.Position.X);
        BouncingLabel flat = NewLabel(0, 0);
        flat.Step(20, 0);
        Assert.AreEqual(0L, flat.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Step_MovingOneAxisOnly_StillReportsAMove()
    {
        BouncingLabel label = NewLabel(0, 0);
        Assert.IsTrue(label.Step(3, 10));
        Assert.AreEqual(0L, label.Position.X);
        Assert.AreEqual(1L, label.Position.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Draw_AfterMoving_ShowsTheTextAtTheNewPosition()
    {
        Window parent = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        BouncingLabel label = NewLabel(2, 1);
        label.ViewSize = new Size(10, 1);
        parent.AddControl(label);
        label.Step(20, 5);
        MockBuffer buffer = new(new Size(20, 5));
        label.Draw(buffer);
        string[] lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[2].Contains("ab"), buffer.ContentAsString);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheBackgroundTask_MovesTheLabelOnceItHasAParent_AndStopsWhenDisposed()
    {
        Window parent = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 10) };
        BouncingLabel label = new(new Position(5, 3), "ab", Theme());
        long startX = label.Position.X;
        long startY = label.Position.Y;
        parent.AddControl(label);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (label.Position.X == startX && label.Position.Y == startY && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.IsTrue(label.Position.X != startX || label.Position.Y != startY, "the label never moved");

        label.Dispose();
        await Task.Delay(250);
        long settledX = label.Position.X;
        long settledY = label.Position.Y;
        await Task.Delay(350);
        Assert.AreEqual(settledX, label.Position.X);
        Assert.AreEqual(settledY, label.Position.Y);
        label.Dispose();
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Dispose_BeforeTheLabelHasAParent_EndsTheWaitingTask()
    {
        BouncingLabel label = new(new Position(0, 0), "ab", Theme());
        label.Dispose();
        label.Dispose();
        Assert.IsNull(label.Parent);
    }
}
