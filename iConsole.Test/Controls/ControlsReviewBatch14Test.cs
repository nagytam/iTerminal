using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Usability fixes found in a review of the small display and input controls: clipped times, hidden cursors and highlights, lost minus signs and clicks that did not focus.</summary>
[TestClass]
public class ControlsReviewBatch14Test
{
    private sealed class FakeClock : IDateTimeProviderService
    {
        public DateTime Time { get; set; } = new(2026, 10, 18, 12, 0, 0);

        public DateTime Now => Time;

        public DateTime UtcNow => Time.ToUniversalTime();
    }

    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static KeyEvent Digit(int d) => Key(ConsoleKey.D0 + d, (char)('0' + d));

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private static string FirstLine(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine)[0];

    [TestMethod]
    public void ElapsedClock_LongLabel_GivesWayToTheTime()
    {
        FakeClock clock = new();
        ElapsedClock elapsed = new(Theme(), clock) { Position = new Position(0, 0), ViewSize = new Size(12, 1), Label = "Build duration so far" };
        MockBuffer buffer = new(new Size(12, 1));
        elapsed.Draw(buffer);
        Assert.AreEqual("Build  00:00", FirstLine(buffer));
    }

    [TestMethod]
    public void ElapsedClock_ShortLabel_IsKept()
    {
        ElapsedClock elapsed = new(Theme(), new FakeClock()) { Position = new Position(0, 0), ViewSize = new Size(20, 1), Label = "Run" };
        MockBuffer buffer = new(new Size(20, 1));
        elapsed.Draw(buffer);
        Assert.AreEqual("Run 00:00", FirstLine(buffer).TrimEnd());
    }

    [TestMethod]
    public void ElapsedClock_LabelWiderThanTheControl_StillShowsTheTime()
    {
        ElapsedClock elapsed = new(Theme(), new FakeClock()) { Position = new Position(0, 0), ViewSize = new Size(5, 1), Label = "Anything" };
        MockBuffer buffer = new(new Size(5, 1));
        elapsed.Draw(buffer);
        Assert.AreEqual("00:00", FirstLine(buffer).TrimEnd());
    }

    [TestMethod]
    public void CheckList_ReplacingTheItems_KeepsTheCursorOnScreen()
    {
        CheckList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        list.SetItems(["a", "b", "c", "d", "e", "f", "g", "h"]);
        list.SetFocus();
        list.HandleKeyEventAsync(Key(ConsoleKey.End)).GetAwaiter().GetResult();
        Assert.AreEqual(7, list.CursorIndex);

        list.SetItems(["a", "b", "c", "d", "e", "f", "g", "h"]);
        MockBuffer buffer = new(new Size(20, 3));
        list.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("[ ] h"));
    }

    [TestMethod]
    public void CheckList_ReplacingWithFewerItems_ClampsAndShowsTheCursor()
    {
        CheckList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        list.SetItems(["a", "b", "c", "d", "e", "f"]);
        list.SetFocus();
        list.HandleKeyEventAsync(Key(ConsoleKey.End)).GetAwaiter().GetResult();
        list.SetItems(["x", "y", "z", "w"]);
        Assert.AreEqual(3, list.CursorIndex);
        MockBuffer buffer = new(new Size(20, 3));
        list.Draw(buffer);
        Assert.IsTrue(buffer.ContentAsString.Contains("[ ] w"));
    }

    [TestMethod]
    public void TimePicker_SettingOnlyHiddenSeconds_RaisesNothing()
    {
        TimePicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.Value = new TimeOnly(10, 30, 0);
        int raised = 0;
        picker.ValueChanged += (_, _) => raised++;
        picker.Value = new TimeOnly(10, 30, 45);
        Assert.AreEqual(0, raised);
        Assert.AreEqual(new TimeOnly(10, 30, 0), picker.Value);
    }

    [TestMethod]
    public void TimePicker_WithSeconds_StillTakesTheSeconds()
    {
        TimePicker picker = new(Theme()) { Position = new Position(0, 0), ShowSeconds = true };
        int raised = 0;
        picker.ValueChanged += (_, _) => raised++;
        picker.Value = new TimeOnly(10, 30, 45);
        Assert.AreEqual(1, raised);
        Assert.AreEqual(new TimeOnly(10, 30, 45), picker.Value);
    }

    [TestMethod]
    public void Meter_LongLabel_IsCutSoTheBarStaysVisible()
    {
        Meter meter = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(12, 1), Label = "Memory consumption", Value = 1.0 };
        MockBuffer buffer = new(new Size(12, 1));
        meter.Draw(buffer);
        string line = FirstLine(buffer);
        Assert.AreEqual("Memory ████", line.TrimEnd());
    }

    [TestMethod]
    public void Meter_ShortLabel_IsKeptWhole()
    {
        Meter meter = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(12, 1), Label = "CPU", Value = 0.0 };
        MockBuffer buffer = new(new Size(12, 1));
        meter.Draw(buffer);
        Assert.AreEqual("CPU ░░░░░░░░", FirstLine(buffer).TrimEnd());
    }

    [TestMethod]
    public void Meter_LabelWiderThanTheControl_DropsTheLabel()
    {
        Meter meter = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(4, 1), Label = "Memory", Value = 1.0 };
        MockBuffer buffer = new(new Size(4, 1));
        meter.Draw(buffer);
        Assert.AreEqual("████", FirstLine(buffer).TrimEnd());
    }

    [TestMethod]
    public async Task NumericUpDown_MinusThenDigits_TypesANegativeNumber()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = -100, Maximum = 100, Value = 0 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));
        await box.HandleKeyEventAsync(Digit(5));
        Assert.AreEqual(-5, box.Value);
        await box.HandleKeyEventAsync(Digit(2));
        Assert.AreEqual(-52, box.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_MinusTwice_CancelsThePendingSign()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = -100, Maximum = 100, Value = 0 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));
        await box.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));
        await box.HandleKeyEventAsync(Digit(5));
        Assert.AreEqual(5, box.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_PendingMinus_IsDroppedByAnotherKey()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = -100, Maximum = 100, Value = 0 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));
        await box.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(1, box.Value);
        await box.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await box.HandleKeyEventAsync(Digit(7));
        Assert.AreEqual(7, box.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_MinusWithoutNegativeRange_IsIgnored()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = 0, Maximum = 100, Value = 0 };
        box.SetFocus();
        Assert.IsFalse(await box.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-')));
        await box.HandleKeyEventAsync(Digit(4));
        Assert.AreEqual(4, box.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_DigitPastTheMaximum_StartsANewEntry()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = 0, Maximum = 100, Value = 50 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Digit(7));
        Assert.AreEqual(7, box.Value, "507 is out of range: the 7 begins a new number instead of sticking to 100");
        await box.HandleKeyEventAsync(Digit(3));
        Assert.AreEqual(73, box.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_DigitPastTheMinimum_StartsANewNegativeEntry()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = -50, Maximum = 50, Value = -40 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Digit(9));
        Assert.AreEqual(-9, box.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_DigitThatCannotFit_StillClamps()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Minimum = 0, Maximum = 3, Value = 0 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Digit(8));
        Assert.AreEqual(3, box.Value);
    }

    [TestMethod]
    public async Task Chips_ClickOnTheBlankCells_FocusesTheControl()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("rock");
        Assert.IsFalse(chips.IsFocused);
        await chips.HandleMouseEventAsync(Click(35));
        Assert.IsTrue(chips.IsFocused);
    }

    [TestMethod]
    public async Task Chips_ClickOnAChip_FocusesAndHighlightsIt()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("rock");
        chips.Add("jazz");
        await chips.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(chips.IsFocused);
        Assert.AreEqual(0, chips.ActiveIndex);
    }

    [TestMethod]
    public async Task SegmentedControl_ClickOnTheBlankCells_FocusesWithoutChangingTheChoice()
    {
        SegmentedControl control = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1), Options = ["Day", "Week"], SelectedIndex = 1 };
        await control.HandleMouseEventAsync(Click(35));
        Assert.IsTrue(control.IsFocused);
        Assert.AreEqual(1, control.SelectedIndex);
    }

    [TestMethod]
    public async Task Breadcrumb_ClickOnTheSeparator_FocusesWithoutActivating()
    {
        Breadcrumb crumb = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1), Segments = ["Home", "Projects"] };
        int activated = 0;
        crumb.SegmentActivated += (_, _) => activated++;
        await crumb.HandleMouseEventAsync(Click(5));
        Assert.IsTrue(crumb.IsFocused);
        Assert.AreEqual(0, activated);
    }

    [TestMethod]
    public void Breadcrumb_Layout_KeepsTheHighlightedSegmentOutOfTheFold()
    {
        string[] segments = ["Home", "Projects", "iTerminal", "Controls", "Breadcrumb"];
        List<(int Index, string Text)> plain = Breadcrumb.Layout(segments, 30);
        Assert.IsFalse(plain.Any(p => p.Index == 1), "without a highlight the second segment folds away");

        List<(int Index, string Text)> kept = Breadcrumb.Layout(segments, 30, 1);
        Assert.IsTrue(kept.Any(p => p.Index == 1), "the highlighted segment must stay drawn");
        Assert.AreEqual(0, kept[0].Index);
    }

    [TestMethod]
    public void Breadcrumb_Layout_KeepFallsBackToFirstFoldAndHighlighted()
    {
        string[] segments = ["Home", "Projects", "iTerminal", "Controls", "Breadcrumb"];
        List<(int Index, string Text)> kept = Breadcrumb.Layout(segments, 20, 2);
        Assert.IsTrue(kept.Any(p => p.Index == 2));
        Assert.AreEqual(-1, kept[1].Index);
    }

    [TestMethod]
    public void Breadcrumb_Layout_TheLastSegmentStaysWhenItIsTheHighlight()
    {
        string[] segments = ["Home", "Projects", "iTerminal", "Controls", "Breadcrumb"];
        List<(int Index, string Text)> kept = Breadcrumb.Layout(segments, 30, 4);
        Assert.AreEqual(4, kept[^1].Index);
    }

    [TestMethod]
    public void Breadcrumb_Draw_ShowsTheHighlightedSegmentAfterMovingLeft()
    {
        Breadcrumb crumb = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 1), Segments = ["Home", "Projects", "iTerminal", "Controls", "Breadcrumb"] };
        crumb.ActiveIndex = 1;
        MockBuffer buffer = new(new Size(30, 1));
        crumb.Draw(buffer);
        Assert.IsTrue(FirstLine(buffer).Contains("Projects"));
    }

    [TestMethod]
    public async Task Rating_DigitAboveTheMaximum_IsIgnored()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0), Value = 2 };
        rating.SetFocus();
        Assert.IsTrue(await rating.HandleKeyEventAsync(Digit(9)));
        Assert.AreEqual(2, rating.Value);
        await rating.HandleKeyEventAsync(Digit(5));
        Assert.AreEqual(5, rating.Value);
    }

    [TestMethod]
    public async Task Rating_DigitWithinALargerMaximum_Sets()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0), Maximum = 9 };
        rating.SetFocus();
        await rating.HandleKeyEventAsync(Digit(9));
        Assert.AreEqual(9, rating.Value);
    }

    [TestMethod]
    public async Task Table_HeaderClick_FocusesTheTableAndRequestsTheSort()
    {
        ConcurrentObservableCollection<string> rows = new(["a", "b"]);
        Table<string> table = new(rows, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 4) };
        table.Columns.Add(new ColumnDefinition<string>("Name", 10, s => s));
        table.OnInitializeLayout();
        int requested = -1;
        table.SortRequested += (_, column) => requested = column;
        await table.HandleMouseEventAsync(Click(2, 0));
        Assert.AreEqual(0, requested);
        Assert.IsTrue(table.IsFocused);
    }
}
