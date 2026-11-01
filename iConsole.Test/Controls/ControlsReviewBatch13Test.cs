using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Usability fixes found in a review of the small input controls: clicks on blank cells, bad steps, stale repaints and entries that vanish.</summary>
[TestClass]
public class ControlsReviewBatch13Test
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static KeyEvent Digit(int d) => Key(ConsoleKey.D0 + d, (char)('0' + d));

    private static MouseEvent Click(long x) => new() { Position = new Position(x, 0), LeftClicked = true };

    [Timeout(60_000)]
    [TestMethod]
    public async Task Slider_NonPositiveStep_IsRaisedToOne()
    {
        Slider slider = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 1), Step = 0, LargeStep = -5, Value = 50 };
        slider.SetFocus();
        await slider.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(51, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(52, slider.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NumericUpDown_NegativeStep_DoesNotReverseTheArrows()
    {
        NumericUpDown box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 1), Step = -3, LargeStep = 0, Value = 10 };
        box.SetFocus();
        await box.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(11, box.Value);
        await box.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(10, box.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void NumericUpDown_Label_RepaintsWhenChanged()
    {
        NumericUpDown box = new(Theme()) { Label = "Old" };
        box.HasStateChanged = false;
        box.Label = "New";
        Assert.IsTrue(box.HasStateChanged);
        box.Label = null!;
        Assert.AreEqual(string.Empty, box.Label);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rating_ClickOnBlankCellsRightOfTheStars_ChangesNothing()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0), Value = 2 };
        rating.ViewSize = new Size(10, 1);
        await rating.HandleMouseEventAsync(Click(8));
        Assert.AreEqual(2, rating.Value);
        Assert.IsTrue(rating.IsFocused);
        await rating.HandleMouseEventAsync(Click(4));
        Assert.AreEqual(5, rating.Value);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TimePicker_SecondDigitThatOverflowsTheField_StartsTheNextEntry()
    {
        TimePicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        await picker.HandleKeyEventAsync(Digit(2));
        await picker.HandleKeyEventAsync(Digit(5));
        Assert.AreEqual(5, picker.Value.Hour, "25 is not an hour: the 5 is kept, not clamped to 23");
        Assert.AreEqual(1, picker.ActiveField);
        await picker.HandleKeyEventAsync(Digit(1));
        await picker.HandleKeyEventAsync(Digit(2));
        Assert.AreEqual(12, picker.Value.Minute);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Chips_TurningReadOnlyOn_MovesTheHighlightOffTheAddSlot()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        chips.Add("a");
        chips.Add("b");
        chips.ActiveIndex = 2;
        chips.IsReadOnly = true;
        Assert.AreEqual(1, chips.ActiveIndex);
        Assert.IsTrue(chips.Visible(30).Any(v => v.Index == 1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Chips_KeysThatDoNothing_AreLeftToTheHost()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        chips.Add("a");
        chips.SetFocus();
        chips.ActiveIndex = 0;
        Assert.IsFalse(await chips.HandleKeyEventAsync(Key(ConsoleKey.Enter)), "Enter on a chip is the dialog's default button");
        chips.ActiveIndex = 1;
        Assert.IsFalse(await chips.HandleKeyEventAsync(Key(ConsoleKey.Delete)), "nothing to remove on the [+] slot");
        int added = 0;
        chips.AddRequested += (_, _) => added++;
        Assert.IsTrue(await chips.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        Assert.AreEqual(1, added);
        chips.ActiveIndex = 0;
        Assert.IsTrue(await chips.HandleKeyEventAsync(Key(ConsoleKey.Delete)));
        Assert.AreEqual(0, chips.Items.Count);
        chips.Add("b");
        chips.IsReadOnly = true;
        Assert.IsFalse(await chips.HandleKeyEventAsync(Key(ConsoleKey.Backspace)));
        Assert.AreEqual(1, chips.Items.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Chips_ChipWiderThanTheControl_StillShows()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(6, 1) };
        chips.Add("a-very-long-chip");
        chips.ActiveIndex = 0;
        List<(int Index, int Start)> visible = chips.Visible(6);
        Assert.AreEqual(1, visible.Count);
        Assert.AreEqual((0, 0), visible[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SegmentedControl_OptionWiderThanTheControl_StillShows()
    {
        SegmentedControl control = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(5, 1), Options = ["Short", "An extremely long option"], SelectedIndex = 1 };
        List<(int Index, int Start)> visible = control.VisibleSegments(5);
        Assert.AreEqual(1, visible.Count);
        Assert.AreEqual((1, 0), visible[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void SegmentedControl_ShorterOptionList_RaisesSelectionChangedWhenTheChoiceMoves()
    {
        SegmentedControl control = new(Theme()) { Options = ["a", "b", "c"], SelectedIndex = 2 };
        int raised = 0;
        control.SelectionChanged += (_, _) => raised++;
        control.Options = ["a", "b", "c", "d"];
        Assert.AreEqual(0, raised, "the choice stayed where it was");
        control.Options = ["a", "b"];
        Assert.AreEqual(1, raised);
        Assert.AreEqual("b", control.SelectedOption);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Expander_Header_RepaintsWhenChanged()
    {
        Expander expander = new(Theme()) { Header = "Old" };
        expander.HasStateChanged = false;
        expander.Header = "New";
        Assert.IsTrue(expander.HasStateChanged);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Expander_ContentHeight_ResizesAnOpenSection()
    {
        Label content = new("content", Theme());
        Expander expander = new(Theme()) { Content = content, ContentHeight = 2, IsExpanded = true };
        Assert.AreEqual(3u, expander.ViewSize.Height);
        expander.ContentHeight = 5;
        Assert.AreEqual(6u, expander.ViewSize.Height);
        Assert.AreEqual(5u, content.ViewSize.Height);
        expander.IsExpanded = false;
        expander.ContentHeight = 7;
        Assert.AreEqual(1u, expander.ViewSize.Height);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Countdown_Label_RepaintsWhenChanged()
    {
        Countdown countdown = new(Theme(), Mock.Of<IDateTimeProviderService>());
        countdown.HasStateChanged = false;
        countdown.Label = "Retry in";
        Assert.IsTrue(countdown.HasStateChanged);
    }
}
