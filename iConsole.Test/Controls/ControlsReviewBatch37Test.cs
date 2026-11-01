using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Controls that left old cells on screen when they shrank or changed shape, took no focus on a click, or looked editable while read-only.</summary>
[TestClass]
public class ControlsReviewBatch37Test
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
    public void Badge_WithShorterText_AsksForARepaint_AndPaintsItsWholeRectangle()
    {
        Badge badge = new(Theme()) { Position = new Position(0, 0), Text = "a long badge" };
        long before = Control.TreeVersion;

        badge.Text = "ok";

        Assert.IsTrue(Control.TreeVersion > before);
        badge.Text = string.Empty;
        badge.ViewSize = new Size(10, 1);
        ScreenBuffer screen = new(10, 1);
        screen.Write(0, new Position(0, 0), new Size(10, 1), new ConxelString("QQQQQQQQQQ", Color.White, Color.Black));
        badge.Draw(screen);
        Assert.IsFalse(screen.Line(0).Contains('Q'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void LinkLabel_WithShorterOrNoText_AsksForARepaint_AndPaintsItsWholeRectangle()
    {
        LinkLabel link = new(Theme()) { Position = new Position(0, 0), Text = "a long link text" };
        long before = Control.TreeVersion;

        link.Text = "go";

        Assert.IsTrue(Control.TreeVersion > before);
        link.Text = string.Empty;
        link.ViewSize = new Size(10, 1);
        ScreenBuffer screen = new(10, 1);
        screen.Write(0, new Position(0, 0), new Size(10, 1), new ConxelString("QQQQQQQQQQ", Color.White, Color.Black));
        link.Draw(screen);
        Assert.IsFalse(screen.Line(0).Contains('Q'));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LogView_AClickInsideGivesItTheFocus_AClickOutsideDoesNot()
    {
        LogView log = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 3) };
        log.Append("a line");

        await log.HandleMouseEventAsync(new MouseEvent { Position = new Position(40, 9), LeftClicked = true });
        Assert.IsFalse(log.IsFocused);

        await log.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 1), LeftClicked = true });
        Assert.IsTrue(log.IsFocused);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorPicker_ReadOnly_ShowsNoCursor()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 4) };
        picker.SetFocus();
        Assert.IsTrue(ScreenBuffer.Render(picker, 40, 4).Line(0).Contains('['));

        picker.IsReadOnly = true;

        Assert.IsFalse(ScreenBuffer.Render(picker, 40, 4).Line(0).Contains('['));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Calendar_ReadOnly_IsNotEmphasisedAsFocused()
    {
        Calendar calendar = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 8) };
        calendar.SetFocus();
        ScreenBuffer editable = ScreenBuffer.Render(calendar, 20, 8);

        calendar.IsReadOnly = true;
        ScreenBuffer locked = ScreenBuffer.Render(calendar, 20, 8);

        bool differs = false;
        for (int y = 2; y < 8 && !differs; y++)
        {
            for (int x = 0; x < 20 && !differs; x++)
            {
                differs = editable.BackgroundAt(x, y) != locked.BackgroundAt(x, y);
            }
        }

        Assert.IsTrue(differs);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Calendar_HidingTheWeekNumbers_AsksForARepaint()
    {
        Calendar calendar = new(Theme()) { ShowWeekNumbers = true };
        long before = Control.TreeVersion;

        calendar.ShowWeekNumbers = false;

        Assert.IsTrue(Control.TreeVersion > before);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TimePicker_DroppingTheSeconds_AsksForARepaint()
    {
        TimePicker picker = new(Theme()) { ShowSeconds = true };
        long before = Control.TreeVersion;

        picker.ShowSeconds = false;

        Assert.IsTrue(Control.TreeVersion > before);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Separator_Turning_AsksForARepaint()
    {
        Separator separator = new(Theme());
        long before = Control.TreeVersion;

        separator.Orientation = Orientation.Vertical;
        Assert.IsTrue(Control.TreeVersion > before);
    }
}
