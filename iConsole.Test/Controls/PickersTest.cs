using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

[TestClass]
public class PickersTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Click(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static string[] Lines(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine);

    [Timeout(60_000)]
    [TestMethod]
    public void TimePicker_Wrap_StaysInRange()
    {
        Assert.AreEqual(0, TimePicker.Wrap(23, 1, 23));
        Assert.AreEqual(59, TimePicker.Wrap(0, -1, 59));
        Assert.AreEqual(5, TimePicker.Wrap(55, 10, 59));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TimePicker_Draw_ShowsFields()
    {
        MockBuffer buffer = new(new Size(10, 1));
        new TimePicker(Theme()) { Value = new TimeOnly(14, 5), Position = new Position(0, 0) }.Draw(buffer);
        Assert.AreEqual("[14:05]", Lines(buffer)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TimePicker_ShowSeconds_AddsThirdField()
    {
        MockBuffer buffer = new(new Size(12, 1));
        new TimePicker(Theme()) { ShowSeconds = true, Value = new TimeOnly(1, 2, 3), Position = new Position(0, 0) }.Draw(buffer);
        Assert.AreEqual("[01:02:03]", Lines(buffer)[0].TrimEnd());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_DigitAfterArrowStep_StartsANewEntry()
    {
        TimePicker picker = new(Theme()) { Value = new TimeOnly(0, 0), Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        picker.SetFocus();

        await picker.HandleKeyEventAsync(new KeyEvent('1', ConsoleKey.D1, false, false, false));
        await picker.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.UpArrow, false, false, false));
        await picker.HandleKeyEventAsync(new KeyEvent('1', ConsoleKey.D1, false, false, false));

        // Typing 1, stepping to 2, then typing 1 gives hour 1; joining the digit onto the stepped 2 would give 21.
        Assert.AreEqual(1, picker.Value.Hour);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Calendar_ClickOnTheChosenDay_ActivatesIt()
    {
        var calendar = new Calendar(Theme()) { Position = new Position(0, 0) };
        var target = new DateOnly(2026, 10, 16);
        calendar.SelectedDate = new DateOnly(2026, 10, 15);
        var activated = new List<DateOnly>();
        calendar.DateActivated += (_, day) => activated.Add(day);
        var (x, y) = CellOf(calendar, target);

        await calendar.HandleMouseEventAsync(Click(x, y));
        Assert.AreEqual(target, calendar.SelectedDate);
        Assert.AreEqual(0, activated.Count, "The first click only chooses the day.");

        await calendar.HandleMouseEventAsync(Click(x, y));
        CollectionAssert.AreEqual(new[] { target }, activated);
    }

    /// <summary>The screen cell (column, row) of a day in the grid, found from the calendar's own layout.</summary>
    private static (long X, long Y) CellOf(Calendar calendar, DateOnly day)
    {
        for (var row = 0; row < 6; row++)
        {
            for (var column = 0; column < 7; column++)
            {
                if (calendar.CellDate(row, column) == day)
                {
                    return (column * 3, row + 2);
                }
            }
        }

        throw new InvalidOperationException($"{day} is not in the grid.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_UpDown_StepsActiveFieldWithWrap()
    {
        TimePicker picker = new(Theme()) { Value = new TimeOnly(23, 0) };
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(new TimeOnly(0, 0), picker.Value);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(new TimeOnly(0, 59), picker.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_TypingDigits_SetsFieldAndAdvances()
    {
        TimePicker picker = new(Theme());
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D1, '1'));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D4, '4'));
        Assert.AreEqual(14, picker.Value.Hour);
        Assert.AreEqual(1, picker.ActiveField);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D7, '7'));
        Assert.AreEqual(7, picker.Value.Minute);
        Assert.AreEqual(1, picker.ActiveField);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_ClickMinutes_SelectsField()
    {
        TimePicker picker = new(Theme()) { Position = new Position(0, 0) };
        await picker.HandleMouseEventAsync(Click(4, 0));
        Assert.AreEqual(1, picker.ActiveField);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorPicker_Move_StopsAtEdges()
    {
        Assert.AreEqual(0, ColorPicker.Move(0, -1, -1, 16));
        Assert.AreEqual(15, ColorPicker.Move(15, 1, 1, 16));
        Assert.AreEqual(5, ColorPicker.Move(1, 0, 1, 16));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_EnterPicksCursorColor()
    {
        ColorPicker picker = new(Theme());
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(Color.DarkBlue, picker.SelectedColor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ColorPicker_Click_PicksSwatch()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        await picker.HandleMouseEventAsync(Click(6, 2));
        Assert.AreEqual(Color.Green, picker.SelectedColor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ColorPicker_Draw_MarksSelectedSwatch()
    {
        MockBuffer buffer = new(new Size(12, 4));
        new ColorPicker(Theme()) { SelectedColor = Color.Green, Position = new Position(0, 0) }.Draw(buffer);
        Assert.IsTrue(Lines(buffer)[2].Contains('✓'));
        Assert.IsFalse(Lines(buffer)[0].Contains('✓'));
    }

    private static ComboBox<string> CreateCombo()
    {
        ComboBox<string> combo = new(Theme());
        combo.SetItems(["Apple", "Banana", "Blueberry", "Cherry"]);
        combo.SetFocus();
        return combo;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_TypeAhead_JumpsToNextMatchAndWraps()
    {
        ComboBox<string> combo = CreateCombo();
        await combo.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        Assert.AreEqual("Banana", combo.SelectedItem);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        Assert.AreEqual("Blueberry", combo.SelectedItem);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.B, 'b'));
        Assert.AreEqual("Banana", combo.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_TypeAhead_NoMatchLeavesSelection()
    {
        ComboBox<string> combo = CreateCombo();
        Assert.IsFalse(await combo.HandleKeyEventAsync(Key(ConsoleKey.Z, 'z')));
        Assert.AreEqual("Apple", combo.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ComboBox_HomeEnd_SelectFirstAndLast()
    {
        ComboBox<string> combo = CreateCombo();
        await combo.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual("Cherry", combo.SelectedItem);
        await combo.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual("Apple", combo.SelectedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_HomeAndEnd_JumpToTheFirstAndLastField()
    {
        TimePicker picker = new(Theme()) { ShowSeconds = true };
        picker.SetFocus();

        Assert.IsTrue(await picker.HandleKeyEventAsync(Key(ConsoleKey.End)));
        Assert.AreEqual(2, picker.ActiveField);

        Assert.IsTrue(await picker.HandleKeyEventAsync(Key(ConsoleKey.Home)));
        Assert.AreEqual(0, picker.ActiveField);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_WithoutSeconds_EndStopsAtTheMinutes()
    {
        TimePicker picker = new(Theme());
        picker.SetFocus();

        await picker.HandleKeyEventAsync(Key(ConsoleKey.End));

        Assert.AreEqual(1, picker.ActiveField);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_ColonAndDot_MoveToTheNextField_SoATimeCanBeTypedAsItIsWritten()
    {
        TimePicker picker = new(Theme());
        picker.SetFocus();

        await picker.HandleKeyEventAsync(Key(ConsoleKey.D9, '9'));
        Assert.AreEqual(1, picker.ActiveField, "9 cannot start a longer hour, so it completes the field");
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Home));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.D1, '1'));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Oem1, ':'));
        Assert.AreEqual(1, picker.ActiveField, "a colon after one digit leaves the hour at 1");
        Assert.AreEqual(1, picker.Value.Hour);

        await picker.HandleKeyEventAsync(Key(ConsoleKey.D3, '3'));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.OemPeriod, '.'));
        Assert.AreEqual(1, picker.ActiveField, "there is no field after the minutes");
        Assert.AreEqual(3, picker.Value.Minute);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_Delete_ZeroesTheActiveField_AndForgetsAHalfTypedValue()
    {
        TimePicker picker = new(Theme()) { Value = new TimeOnly(14, 35) };
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));

        Assert.IsTrue(await picker.HandleKeyEventAsync(Key(ConsoleKey.Delete)));

        Assert.AreEqual(new TimeOnly(14, 0), picker.Value);
        Assert.AreEqual(1, picker.ActiveField);

        picker.Value = new TimeOnly(14, 35);
        Assert.IsTrue(await picker.HandleKeyEventAsync(Key(ConsoleKey.Backspace)));
        Assert.AreEqual(new TimeOnly(14, 0), picker.Value, "Backspace does the same");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_ReadOnly_IgnoresHomeEndColonAndDelete()
    {
        TimePicker picker = new(Theme()) { Value = new TimeOnly(14, 35), IsReadOnly = true };
        picker.SetFocus();

        await picker.HandleKeyEventAsync(Key(ConsoleKey.End));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Oem1, ':'));

        Assert.AreEqual(0, picker.ActiveField);
        Assert.AreEqual(new TimeOnly(14, 35), picker.Value);
    }
}
