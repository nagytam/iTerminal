using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>A disabled control looks disabled, and a control stays inside the rectangle it was given.</summary>
[TestClass]
public class ControlsReviewBatch36Test
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme
        {
            Foreground = Color.White,
            Background = Color.Black,
            DisabledForeground = Color.DarkGray,
            DisabledBackground = Color.Black,
            SelectedForeground = Color.Black,
            SelectedBackground = Color.Gray,
            SelectedFocusedForeground = Color.Black,
            SelectedFocusedBackground = Color.Cyan,
        });
        return m.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Disabled_GreysItsWell()
    {
        TextBox box = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 1), Text = "abc" };

        box.IsEnabled = false;
        ScreenBuffer screen = ScreenBuffer.Render(box, 20, 1);

        Assert.AreEqual(Color.DarkGray, screen.ForegroundAt(1, 0));
        Assert.AreEqual(Color.Black, screen.BackgroundAt(1, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TextBox_Enabled_KeepsItsEditableWell()
    {
        TextBox box = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 1), Text = "abc" };

        ScreenBuffer screen = ScreenBuffer.Render(box, 20, 1);

        Assert.AreEqual(Color.Yellow, screen.BackgroundAt(1, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ElapsedClock_Disabled_DrawsTheTimeGreyed()
    {
        ElapsedClock clock = new(Theme(), new DateTimeProviderService()) { Position = new Position(0, 0), ViewSize = new Size(12, 1) };

        clock.IsEnabled = false;
        ScreenBuffer screen = ScreenBuffer.Render(clock, 12, 1);

        Assert.AreEqual(Color.DarkGray, screen.ForegroundAt(0, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioGroup_Disabled_GreysItsOptions()
    {
        RadioGroup<string> group = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 2) };
        group.SetItems(["one", "two"]);

        group.IsEnabled = false;
        ScreenBuffer screen = ScreenBuffer.Render(group, 20, 2);

        Assert.AreEqual(Color.DarkGray, screen.ForegroundAt(3, 0));
        Assert.AreEqual(Color.DarkGray, screen.ForegroundAt(3, 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void RadioGroup_MadeNarrower_DoesNotDrawPastItsEdge()
    {
        RadioGroup<string> group = new(Theme()) { Position = new Position(10, 0), ViewSize = new Size(20, 2) };
        group.SetItems(["a long option", "another option"]);
        group.ViewSize = new Size(12, 2);

        ScreenBuffer screen = new(40, 2);
        group.Draw(screen);

        Assert.IsTrue(screen.Line(0)[22..].Trim().Length == 0, screen.Line(0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void HexViewer_Disabled_GreysTheCursorRowToo()
    {
        HexViewer viewer = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(60, 3), Data = [1, 2, 3, 4, 5, 6, 7, 8] };

        viewer.IsEnabled = false;
        ScreenBuffer screen = ScreenBuffer.Render(viewer, 60, 3);

        Assert.AreEqual(Color.DarkGray, screen.ForegroundAt(0, 0));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Calendar_GivenFewerRowsThanItsEight_DrawsOnlyThose()
    {
        Calendar calendar = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        ScreenBuffer screen = new(20, 8);
        for (int y = 0; y < 8; y++)
        {
            screen.Write(0, new Position(0, y), new Size(20, 1), new ConxelString(new string('Ø', 20), Color.White, Color.Black));
        }

        calendar.Draw(screen);

        Assert.IsFalse(screen.Line(2).Contains('Ø'));
        Assert.IsTrue(screen.Line(3).All(c => c == 'Ø'), screen.Line(3));
    }
}
