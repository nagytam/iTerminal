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
    [TestMethod]
    public void Calendar_ShowWeekNumbers_AddsIsoWeekColumn()
    {
        MockBuffer buffer = new(new Size(23, 8));
        Calendar calendar = Create();
        calendar.ShowWeekNumbers = true;
        calendar.Draw(buffer);
        string[] lines = Lines(buffer);
        Assert.AreEqual(23, (int)calendar.ViewSize.Width);
        Assert.IsTrue(lines[1].StartsWith("Wk Mo Tu"));
        Assert.AreEqual("40 28 29 30  1  2  3  4", lines[2].TrimEnd());
        Assert.AreEqual("42 12 13 14 15 16 17 18", lines[4].TrimEnd().Substring(0, 23));
    }

    [TestMethod]
    public void Calendar_AdjacentDaysOff_LeavesThemBlank()
    {
        MockBuffer buffer = new(new Size(20, 8));
        Calendar calendar = Create();
        calendar.ShowAdjacentDays = false;
        calendar.Draw(buffer);
        Assert.AreEqual("          1  2  3  4", Lines(buffer)[2].TrimEnd());
    }

    [TestMethod]
    public async Task Calendar_ClickOnBlankAdjacentDay_DoesNothing()
    {
        Calendar calendar = Create();
        calendar.ShowAdjacentDays = false;
        await calendar.HandleMouseEventAsync(Click(0, 2));
        Assert.AreEqual(new DateOnly(2026, 10, 18), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_WeekNumbersShiftTheClickColumns()
    {
        Calendar calendar = Create();
        calendar.ShowWeekNumbers = true;
        await calendar.HandleMouseEventAsync(Click(3, 2));
        Assert.AreEqual(new DateOnly(2026, 9, 28), calendar.SelectedDate);
        await calendar.HandleMouseEventAsync(Click(22, 0));
        Assert.AreEqual(new DateOnly(2026, 10, 28), calendar.SelectedDate);
    }

    private sealed class SharedModel : iConsole.Model.ICalendarModel
    {
        public DateOnly Today { get; set; } = new(2026, 10, 1);

        public DateOnly SelectedDate { get; private set; } = new(2026, 10, 1);

        public event EventHandler? Changed;

        public void Select(DateOnly date)
        {
            SelectedDate = date;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [TestMethod]
    public void Calendar_BoundModel_IsTheSourceOfTheSelection()
    {
        SharedModel model = new();
        Calendar calendar = new(Theme()) { Model = model };
        int changed = 0;
        calendar.SelectedDateChanged += (_, _) => changed++;
        model.Select(new DateOnly(2026, 11, 5));
        Assert.AreEqual(new DateOnly(2026, 11, 5), calendar.SelectedDate);
        Assert.AreEqual(1, changed);
        calendar.SelectedDate = new DateOnly(2026, 12, 1);
        Assert.AreEqual(new DateOnly(2026, 12, 1), model.SelectedDate);
    }

    [TestMethod]
    public void Calendar_ReplacedModel_StopsListeningToTheOldOne()
    {
        SharedModel first = new();
        SharedModel second = new();
        Calendar calendar = new(Theme()) { Model = first };
        calendar.Model = second;
        int changed = 0;
        calendar.SelectedDateChanged += (_, _) => changed++;
        first.Raise();
        Assert.AreEqual(0, changed);
        second.Raise();
        Assert.AreEqual(1, changed);
    }

    [TestMethod]
    public async Task Calendar_CtrlPageUpAndDown_MoveByAYear()
    {
        Calendar calendar = Create();
        calendar.SetFocus();

        Assert.IsTrue(await calendar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageDown, false, false, true)));
        Assert.AreEqual(new DateOnly(2027, 10, 18), calendar.SelectedDate);
        await calendar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageUp, false, false, true));
        await calendar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageUp, false, false, true));
        Assert.AreEqual(new DateOnly(2025, 10, 18), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_CtrlPageDown_FromALeapDay_ClampsToTheShorterMonth()
    {
        Calendar calendar = Create();
        calendar.SelectedDate = new DateOnly(2028, 2, 29);
        calendar.SetFocus();

        await calendar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageDown, false, false, true));

        Assert.AreEqual(new DateOnly(2029, 2, 28), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_T_JumpsBackToToday_InEitherCase()
    {
        Calendar calendar = Create();
        calendar.SetFocus();
        calendar.SelectedDate = new DateOnly(2030, 1, 5);

        Assert.IsTrue(await calendar.HandleKeyEventAsync(new KeyEvent('t', ConsoleKey.T, false, false, false)));
        Assert.AreEqual(new DateOnly(2026, 10, 18), calendar.SelectedDate);

        calendar.SelectedDate = new DateOnly(2030, 1, 5);
        await calendar.HandleKeyEventAsync(new KeyEvent('T', ConsoleKey.T, true, false, false));
        Assert.AreEqual(new DateOnly(2026, 10, 18), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_ReadOnlyOrCtrlT_IgnoresTheNewKeys()
    {
        Calendar calendar = Create();
        calendar.SetFocus();
        calendar.SelectedDate = new DateOnly(2030, 1, 5);

        await calendar.HandleKeyEventAsync(new KeyEvent('t', ConsoleKey.T, false, false, true));
        Assert.AreEqual(new DateOnly(2030, 1, 5), calendar.SelectedDate, "Ctrl+T is not Today");

        calendar.IsReadOnly = true;
        await calendar.HandleKeyEventAsync(new KeyEvent('t', ConsoleKey.T, false, false, false));
        await calendar.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.PageDown, false, false, true));
        Assert.AreEqual(new DateOnly(2030, 1, 5), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task Calendar_Space_ActivatesTheDateLikeEnter()
    {
        Calendar calendar = Create();
        calendar.SetFocus();
        List<DateOnly> activated = [];
        calendar.DateActivated += (_, date) => activated.Add(date);

        Assert.IsTrue(await calendar.HandleKeyEventAsync(new KeyEvent(' ', ConsoleKey.Spacebar, false, false, false)));
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 18) }, activated);
    }
}
