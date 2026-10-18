using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class CalendarAndRuleTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static KeyEvent Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    private static Calendar Create()
        => new(Theme()) { Position = new Position(0, 0), SelectedDate = new DateOnly(2026, 10, 18), Today = new DateOnly(2026, 10, 18) };

    [TestMethod]
    public void Calendar_GridStart_IsTheFirstColumnOnOrBeforeTheFirst()
    {
        Assert.AreEqual(new DateOnly(2026, 9, 28), Calendar.GridStart(new DateOnly(2026, 10, 18), DayOfWeek.Monday));
        Assert.AreEqual(new DateOnly(2026, 9, 27), Calendar.GridStart(new DateOnly(2026, 10, 18), DayOfWeek.Sunday));
    }

    [TestMethod]
    public void Calendar_Draw_ShowsMonthWeekdaysAndDays()
    {
        MockBuffer buffer = new(new Size(20, 8));
        Create().Draw(buffer);
        string[] lines = Lines(buffer);
        StringAssert_Contains(lines[0], "October 2026");
        Assert.IsTrue(lines[1].StartsWith("Mo Tu We Th Fr Sa Su"));
        Assert.AreEqual("28 29 30  1  2  3  4", lines[2].TrimEnd());
        Assert.AreEqual("12 13 14 15 16 17 18", lines[4].TrimEnd());
    }

    private static void StringAssert_Contains(string text, string part)
        => Assert.IsTrue(text.Contains(part), $"[{text}] lacks [{part}]");

    [TestMethod]
    public async Task Calendar_Keys_MoveByDayWeekAndMonth()
    {
        Calendar calendar = Create();
        calendar.SetFocus();
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(new DateOnly(2026, 10, 19), calendar.SelectedDate);
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(new DateOnly(2026, 10, 26), calendar.SelectedDate);
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(new DateOnly(2026, 11, 26), calendar.SelectedDate);
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(new DateOnly(2026, 11, 30), calendar.SelectedDate);
    }

    [TestMethod]
    public void Calendar_AddMonths_ClampsToShorterMonth()
    {
        Assert.AreEqual(new DateOnly(2026, 2, 28), Calendar.AddMonths(new DateOnly(2026, 1, 31), 1));
    }

    [TestMethod]
    public async Task Calendar_ClickDay_SelectsIt()
    {
        Calendar calendar = Create();
        await calendar.HandleMouseEventAsync(Click(0, 2));
        Assert.AreEqual(new DateOnly(2026, 9, 28), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_ClickArrows_ChangeMonth()
    {
        Calendar calendar = Create();
        await calendar.HandleMouseEventAsync(Click(19, 0));
        Assert.AreEqual(new DateOnly(2026, 11, 18), calendar.SelectedDate);
        await calendar.HandleMouseEventAsync(Click(0, 0));
        Assert.AreEqual(new DateOnly(2026, 10, 18), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_ReadOnly_IgnoresInput()
    {
        Calendar calendar = Create();
        calendar.IsReadOnly = true;
        calendar.SetFocus();
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await calendar.HandleMouseEventAsync(Click(0, 2));
        Assert.AreEqual(new DateOnly(2026, 10, 18), calendar.SelectedDate);
    }

    [TestMethod]
    public void Separator_BuildRule_SetsTitleIn()
    {
        string rule = Separator.BuildRule("Advanced", 20);
        Assert.AreEqual(20, rule.Length);
        Assert.IsTrue(rule.StartsWith(Border.HorizontalSingle + Border.HorizontalSingle + " Advanced " + Border.HorizontalSingle));
    }

    [TestMethod]
    public void Separator_BuildRule_WithoutTitleOrRoom_IsPlain()
    {
        Assert.AreEqual(string.Concat(Enumerable.Repeat(Border.HorizontalSingle, 10)), Separator.BuildRule("", 10));
        Assert.AreEqual(string.Concat(Enumerable.Repeat(Border.HorizontalSingle, 5)), Separator.BuildRule("Advanced", 5));
    }

    [TestMethod]
    public void Meter_ShowPercent_OverlaysValue()
    {
        MockBuffer buffer = new(new Size(20, 1));
        new Meter(Theme()) { Value = 0.5, ShowPercent = true, Position = new Position(0, 0), ViewSize = new Size(20, 1) }.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[0].Contains(" 50% "));
    }
}
