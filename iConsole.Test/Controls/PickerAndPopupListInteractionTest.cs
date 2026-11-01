using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Keyboard and mouse behaviour of TimePicker, Calendar at the ends of time, and PopupListView.</summary>
[TestClass]
public class PickerAndPopupListInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private static MouseEvent Wheel(long x, long y, int delta) => new() { Position = new Position(x, y), WheelDelta = delta };

    // ---- TimePicker ----

    private static TimePicker NewTime(bool seconds = false) => new(Theme())
    {
        Value = new TimeOnly(10, 30, 15),
        ShowSeconds = seconds,
        Position = new Position(0, 0),
    };

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_UpDown_StepAndWrapTheActiveField()
    {
        TimePicker picker = NewTime();
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(11, picker.Value.Hour);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(1, picker.Value.Hour);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(23, picker.Value.Hour);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(9, picker.Value.Hour);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_LeftRight_MoveBetweenFieldsWithinBounds()
    {
        TimePicker picker = NewTime(seconds: true);
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, picker.ActiveField);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, picker.ActiveField);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(16, picker.Value.Second);
        Assert.IsFalse(await picker.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_TypingDigits_FillsAFieldThenAdvances()
    {
        TimePicker picker = NewTime();
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D0, '0'));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D7, '7'));
        Assert.AreEqual(7, picker.Value.Hour);
        Assert.AreEqual(1, picker.ActiveField);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D4, '4'));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D5, '5'));
        Assert.AreEqual(45, picker.Value.Minute);
        Assert.AreEqual(1, picker.ActiveField);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_TypingADigitThatCannotStartATwoDigitValue_CompletesTheField()
    {
        TimePicker picker = NewTime();
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D9, '9'));
        Assert.AreEqual(9, picker.Value.Hour);
        Assert.AreEqual(1, picker.ActiveField);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D7, '7'));
        Assert.AreEqual(7, picker.Value.Minute);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_TypingAnOutOfRangePair_KeepsTheSecondDigit()
    {
        TimePicker picker = NewTime();
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D2, '2'));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D9, '9'));
        Assert.AreEqual(9, picker.Value.Hour);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_ClickPicksTheFieldUnderThePointer_AndWheelSteps()
    {
        TimePicker picker = NewTime(seconds: true);
        await picker.HandleMouseEventAsync(Click(7));
        Assert.IsTrue(picker.IsFocused);
        Assert.AreEqual(2, picker.ActiveField);
        await picker.HandleMouseEventAsync(Click(1));
        Assert.AreEqual(0, picker.ActiveField);
        await picker.HandleMouseEventAsync(Wheel(1, 0, 1));
        Assert.AreEqual(11, picker.Value.Hour);
        await picker.HandleMouseEventAsync(Wheel(1, 0, -1));
        Assert.AreEqual(10, picker.Value.Hour);
        Assert.IsFalse(await picker.HandleMouseEventAsync(Click(40)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_ReadOnlyAndDisabled_IgnoreKeys()
    {
        TimePicker picker = NewTime();
        picker.SetFocus();
        picker.IsReadOnly = true;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D5, '5'));
        picker.IsReadOnly = false;
        picker.IsEnabled = false;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(new TimeOnly(10, 30, 0), picker.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TimePicker_ShowSeconds_WidensAndKeepsTheFieldInRange()
    {
        TimePicker picker = NewTime(seconds: true);
        Assert.AreEqual(10, (int)picker.ViewSize.Width);
        picker.ShowSeconds = false;
        Assert.AreEqual(7, (int)picker.ViewSize.Width);
        int raised = 0;
        picker.ValueChanged += (_, _) => raised++;
        picker.Value = new TimeOnly(10, 30, 15);
        Assert.AreEqual(0, raised);
        picker.Value = new TimeOnly(1, 2, 3);
        Assert.AreEqual(1, raised);
    }

    // ---- Calendar at the ends of time ----

    [Timeout(60_000)]
    [TestMethod]
    public async Task Calendar_AtTheLastDay_KeysAndDrawDoNotThrow()
    {
        Calendar calendar = new(Theme()) { SelectedDate = DateOnly.MaxValue, Position = new Position(0, 0) };
        calendar.SetFocus();
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        calendar.Draw(new MockBuffer(new Size(20, 8)));
        Assert.AreEqual(DateOnly.MaxValue, calendar.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Calendar_AtTheFirstDay_KeysAndDrawDoNotThrow()
    {
        Calendar calendar = new(Theme()) { SelectedDate = DateOnly.MinValue, Position = new Position(0, 0) };
        calendar.SetFocus();
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await calendar.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        calendar.Draw(new MockBuffer(new Size(20, 8)));
        Assert.AreEqual(DateOnly.MinValue, calendar.SelectedDate);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Calendar_DateArithmetic_SaturatesAtTheEnds()
    {
        Assert.AreEqual(DateOnly.MaxValue, Calendar.AddDays(DateOnly.MaxValue, 5));
        Assert.AreEqual(DateOnly.MinValue, Calendar.AddDays(DateOnly.MinValue, -5));
        Assert.AreEqual(new DateOnly(2026, 10, 19), Calendar.AddDays(new DateOnly(2026, 10, 18), 1));
        Assert.AreEqual(DateOnly.MaxValue, Calendar.AddMonths(DateOnly.MaxValue, 1));
        Assert.AreEqual(DateOnly.MinValue, Calendar.AddMonths(DateOnly.MinValue, -1));
        Assert.AreEqual(DateOnly.MinValue, Calendar.GridStart(DateOnly.MinValue, DayOfWeek.Sunday));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Slider_AtTheLimitsOfLong_DoesNotWrapAround()
    {
        Slider slider = new(Theme()) { Minimum = long.MinValue, Maximum = long.MaxValue, Value = long.MaxValue, Position = new Position(0, 0) };
        slider.SetFocus();
        await slider.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        await slider.HandleMouseEventAsync(Wheel(1, 0, 1));
        Assert.AreEqual(long.MaxValue, slider.Value);
        slider.Value = long.MinValue;
        await slider.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(long.MinValue, slider.Value);
        Assert.AreEqual(0, Slider.ThumbColumn(long.MinValue, long.MinValue, long.MaxValue, 11));
        Assert.AreEqual(10, Slider.ThumbColumn(long.MaxValue, long.MinValue, long.MaxValue, 11));
        Assert.AreEqual(long.MaxValue, Slider.ValueAt(10, long.MinValue, long.MaxValue, 11));
        Assert.AreEqual(long.MinValue, Slider.ValueAt(0, long.MinValue, long.MaxValue, 11));
        slider.Draw(new MockBuffer(new Size(20, 1)));
    }

    // ---- PopupListView ----

    private static PopupListView NewList(int height = 3)
    {
        PopupListView list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, height) };
        list.SetItems(["one", "two", "three", "four", "five"]);
        return list;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PopupListView_Keys_MoveClampAndCommit()
    {
        PopupListView list = NewList();
        List<int> committed = [];
        list.Committed = committed.Add;
        await list.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, list.Highlight);
        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await list.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await list.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(4, list.Highlight);
        await list.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        await list.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(1, list.Highlight);
        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(4, list.Highlight);
        await list.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, list.Highlight);
        CollectionAssert.AreEqual(new[] { 1, 4 }, committed);
        Assert.IsFalse(await list.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PopupListView_Escape_Cancels()
    {
        PopupListView list = NewList();
        bool cancelled = false;
        list.Cancelled = () => cancelled = true;
        Assert.IsTrue(await list.HandleKeyEventAsync(Key(ConsoleKey.Escape)));
        Assert.IsTrue(cancelled);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PopupListView_Click_CommitsTheRowAccountingForScroll()
    {
        PopupListView list = NewList();
        List<int> committed = [];
        list.Committed = committed.Add;
        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        list.Draw(new MockBuffer(new Size(10, 3)));
        await list.HandleMouseEventAsync(Click(2, 0));
        CollectionAssert.AreEqual(new[] { 2 }, committed);
        await list.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 1), RightClicked = true });
        CollectionAssert.AreEqual(new[] { 2, 3 }, committed);
        Assert.IsFalse(await list.HandleMouseEventAsync(Click(30, 0)));
        Assert.IsFalse(await list.HandleMouseEventAsync(Click(2, 9)));
        Assert.AreEqual(2, committed.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PopupListView_Wheel_MovesTheHighlight()
    {
        PopupListView list = NewList();
        await list.HandleMouseEventAsync(Wheel(2, 1, -1));
        Assert.AreEqual(1, list.Highlight);
        await list.HandleMouseEventAsync(Wheel(2, 1, 1));
        Assert.AreEqual(0, list.Highlight);
        Assert.IsFalse(await list.HandleMouseEventAsync(Wheel(40, 1, -1)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PopupListView_Empty_IgnoresCommitAndMovement()
    {
        PopupListView list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 3) };
        list.SetItems([], highlight: 7);
        int committed = 0;
        list.Committed = _ => committed++;
        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        await list.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await list.HandleMouseEventAsync(Click(1, 0));
        Assert.AreEqual(0, committed);
        Assert.AreEqual(0, list.Highlight);
        list.Draw(new MockBuffer(new Size(10, 3)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void PopupListView_Draw_ScrollsTheHighlightIntoViewAndTruncatesLongRows()
    {
        PopupListView list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(6, 2) };
        list.SetItems(["alpha", "a very long row", "gamma"], highlight: 2);
        MockBuffer buffer = new(new Size(6, 2));
        list.Draw(buffer);
        string[] lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].StartsWith(" a ver"));
        Assert.IsTrue(lines[1].StartsWith(" gamma"));
        Assert.AreEqual(2, list.Highlight);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_HomeAndEnd_GoToTheStartAndEndOfTheRow()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        int count = Enum.GetValues<Color>().Length;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        int row = picker.CursorIndex / ColorPicker.GridColumns;

        Assert.IsTrue(await picker.HandleKeyEventAsync(Key(ConsoleKey.End)));
        Assert.AreEqual(Math.Min(row * ColorPicker.GridColumns + ColorPicker.GridColumns - 1, count - 1), picker.CursorIndex);

        Assert.IsTrue(await picker.HandleKeyEventAsync(Key(ConsoleKey.Home)));
        Assert.AreEqual(row * ColorPicker.GridColumns, picker.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_CtrlHomeAndEnd_GoToTheFirstAndLastSwatch()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        int count = Enum.GetValues<Color>().Length;

        Assert.IsTrue(await picker.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true)));
        Assert.AreEqual(count - 1, picker.CursorIndex);

        Assert.IsTrue(await picker.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Home, false, false, true)));
        Assert.AreEqual(0, picker.CursorIndex);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_HomeAndEnd_MoveTheCursorOnly_NotTheChoice()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        Color chosen = picker.SelectedColor;

        await picker.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true));

        Assert.AreEqual(chosen, picker.SelectedColor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_HomeAndEnd_UnfocusedOrDisabled_AreIgnored()
    {
        ColorPicker plain = new(Theme()) { Position = new Position(0, 0) };
        Assert.IsFalse(await plain.HandleKeyEventAsync(Key(ConsoleKey.End)));

        ColorPicker disabled = new(Theme()) { Position = new Position(0, 0) };
        disabled.SetFocus();
        disabled.IsEnabled = false;
        int start = disabled.CursorIndex;
        await disabled.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.End, false, false, true));
        Assert.AreEqual(start, disabled.CursorIndex);
    }
}
