using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Focus, enabled and read-only behaviour of RadioGroup and Toolbar, plus Badge and GroupBox edge cases.</summary>
[TestClass]
public class GroupAndToolbarInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private static string FirstLine(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine)[0];

    // ---- RadioGroup ----

    private static RadioGroup<string> NewGroup(Orientation orientation = Orientation.Vertical)
    {
        RadioGroup<string> group = new(Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 3),
            Orientation = orientation,
        };
        group.SetItems(["Small", "Medium", "Large"]);
        return group;
    }

    [TestMethod]
    public async Task RadioGroup_Disabled_IgnoresKeysAndClicks()
    {
        RadioGroup<string> group = NewGroup();
        group.SetFocus();
        group.IsEnabled = false;
        Assert.IsFalse(await group.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.IsFalse(await group.HandleMouseEventAsync(Click(2, 2)));
        Assert.AreEqual(0, group.SelectedIndex);
    }

    [TestMethod]
    public async Task RadioGroup_ReadOnly_IgnoresKeysAndClicks()
    {
        RadioGroup<string> group = NewGroup();
        group.SetFocus();
        group.IsReadOnly = true;
        await group.HandleKeyEventAsync(Key(ConsoleKey.End));
        await group.HandleMouseEventAsync(Click(2, 2));
        Assert.AreEqual(0, group.SelectedIndex);
    }

    [TestMethod]
    public async Task RadioGroup_ClickRightOfTheGroup_DoesNotSelect()
    {
        RadioGroup<string> group = NewGroup();
        Assert.IsFalse(await group.HandleMouseEventAsync(Click(30, 2)));
        Assert.AreEqual(0, group.SelectedIndex);
        Assert.IsFalse(group.IsFocused);
    }

    [TestMethod]
    public async Task RadioGroup_ClickRow_SelectsAndFocuses()
    {
        RadioGroup<string> group = NewGroup();
        Assert.IsTrue(await group.HandleMouseEventAsync(Click(2, 2)));
        Assert.AreEqual("Large", group.SelectedItem);
        Assert.IsTrue(group.IsFocused);
    }

    [TestMethod]
    public async Task RadioGroup_Horizontal_HitTestsEachOption()
    {
        RadioGroup<string> group = NewGroup(Orientation.Horizontal);
        group.OnInitializeLayout();
        // "◉ Small" is 7 cells, a two-cell gap, then "○ Medium" starts at column 9.
        await group.HandleMouseEventAsync(Click(10));
        Assert.AreEqual(1, group.SelectedIndex);
        await group.HandleMouseEventAsync(Click(8));
        Assert.AreEqual(1, group.SelectedIndex);
        Assert.IsFalse(await group.HandleMouseEventAsync(Click(10, 1)));
    }

    [TestMethod]
    public async Task RadioGroup_Keys_ClampAtEnds_AndRaiseEventOnce()
    {
        RadioGroup<string> group = NewGroup();
        group.SetFocus();
        int raised = 0;
        group.SelectionChanged += (_, _) => raised++;
        await group.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, raised);
        await group.HandleKeyEventAsync(Key(ConsoleKey.End));
        await group.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, group.SelectedIndex);
        await group.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, group.SelectedIndex);
        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public async Task RadioGroup_Empty_HasNoSelectionAndIgnoresInput()
    {
        RadioGroup<string> group = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        group.SetFocus();
        Assert.AreEqual(-1, group.SelectedIndex);
        Assert.IsNull(group.SelectedItem);
        Assert.IsFalse(await group.HandleKeyEventAsync(Key(ConsoleKey.DownArrow)));
        Assert.IsFalse(await group.HandleMouseEventAsync(Click(1, 0)));
        group.SelectedIndex = 4;
        Assert.AreEqual(-1, group.SelectedIndex);
    }

    [TestMethod]
    public void RadioGroup_Draw_ShowsEveryOption()
    {
        RadioGroup<string> group = NewGroup();
        group.SelectedIndex = 1;
        MockBuffer buffer = new(new Size(20, 3));
        group.Draw(buffer);
        string[] lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].Contains("Small"));
        Assert.IsTrue(lines[1].Contains("Medium"));
        Assert.IsTrue(lines[2].Contains("Large"));
    }

    // ---- Toolbar ----

    private static Toolbar NewToolbar()
    {
        Toolbar toolbar = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        toolbar.Items = [new ToolbarItem("Open", "F3"), new ToolbarItem("Save", IsEnabled: false), new ToolbarItem("Close")];
        return toolbar;
    }

    [TestMethod]
    public async Task Toolbar_HomeEnd_LandOnEnabledItems()
    {
        Toolbar toolbar = NewToolbar();
        toolbar.SetFocus();
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(2, toolbar.ActiveIndex);
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, toolbar.ActiveIndex);
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, toolbar.ActiveIndex);
        Assert.IsFalse(await toolbar.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public void Toolbar_ActiveIndex_RejectsDisabledAndOutOfRange()
    {
        Toolbar toolbar = NewToolbar();
        toolbar.ActiveIndex = 1;
        toolbar.ActiveIndex = 9;
        toolbar.ActiveIndex = -1;
        Assert.AreEqual(0, toolbar.ActiveIndex);
    }

    [TestMethod]
    public void Toolbar_Items_StartOnFirstEnabled()
    {
        Toolbar toolbar = new(Theme());
        toolbar.Items = [new ToolbarItem("A", IsEnabled: false), new ToolbarItem("B")];
        Assert.AreEqual(1, toolbar.ActiveIndex);
        toolbar.Items = null!;
        Assert.AreEqual(0, toolbar.Items.Count);
    }

    [TestMethod]
    public async Task Toolbar_SpaceInvokes_AndDisabledToolbarDoesNot()
    {
        Toolbar toolbar = NewToolbar();
        toolbar.SetFocus();
        List<int> invoked = [];
        toolbar.ItemInvoked += (_, i) => invoked.Add(i);
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        CollectionAssert.AreEqual(new[] { 0 }, invoked);
        toolbar.IsEnabled = false;
        await toolbar.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await toolbar.HandleMouseEventAsync(Click(1));
        Assert.AreEqual(1, invoked.Count);
    }

    [TestMethod]
    public async Task Toolbar_ClickFocusesAndInvokes_ClickOnDisabledItemDoesNothing()
    {
        Toolbar toolbar = NewToolbar();
        List<int> invoked = [];
        toolbar.ItemInvoked += (_, i) => invoked.Add(i);
        // " Open F3 " is 9 cells, a gap, then " Save " (disabled) spans columns 10..15.
        await toolbar.HandleMouseEventAsync(Click(12));
        Assert.AreEqual(0, invoked.Count);
        Assert.IsFalse(toolbar.IsFocused);
        await toolbar.HandleMouseEventAsync(Click(3));
        CollectionAssert.AreEqual(new[] { 0 }, invoked);
        Assert.IsTrue(toolbar.IsFocused);
        Assert.AreEqual(-1, toolbar.IndexAtColumn(39));
    }

    [TestMethod]
    public void Toolbar_Draw_StopsAtTheEdgeOfAShortToolbar()
    {
        Toolbar toolbar = NewToolbar();
        toolbar.ViewSize = new Size(12, 1);
        MockBuffer buffer = new(new Size(12, 1));
        toolbar.Draw(buffer);
        Assert.IsTrue(FirstLine(buffer).StartsWith(" Open F3"));
    }

    // ---- Badge ----

    [TestMethod]
    public void Badge_BackgroundFor_MapsEveryKind()
    {
        Color neutral = Color.Gray;
        Assert.AreEqual(neutral, Badge.BackgroundFor(BadgeKind.Neutral, neutral));
        Assert.AreEqual(Color.Cyan, Badge.BackgroundFor(BadgeKind.Info, neutral));
        Assert.AreEqual(Color.Green, Badge.BackgroundFor(BadgeKind.Success, neutral));
        Assert.AreEqual(Color.Yellow, Badge.BackgroundFor(BadgeKind.Warning, neutral));
        Assert.AreEqual(Color.Red, Badge.BackgroundFor(BadgeKind.Error, neutral));
    }

    [TestMethod]
    public void Badge_EmptyText_HasNoWidthAndDrawsNothing()
    {
        Badge badge = new(Theme()) { Text = "OK", Kind = BadgeKind.Success, Position = new Position(0, 0) };
        Assert.AreEqual(4, (int)badge.ViewSize.Width);
        badge.Text = null!;
        Assert.AreEqual(0, (int)badge.ViewSize.Width);
        MockBuffer buffer = new(new Size(6, 1));
        badge.Draw(buffer);
        Assert.AreEqual(string.Empty, FirstLine(buffer).Trim());
    }

    // ---- GroupBox ----

    [TestMethod]
    public void GroupBox_SetContent_ReplacesPreviousAndFillsInterior()
    {
        GroupBox box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 6) };
        Label first = new("one", Theme());
        Label second = new("two", Theme());
        box.SetContent(first);
        box.SetContent(second);
        Assert.IsFalse(box.Controls.Contains(first));
        Assert.IsTrue(box.Controls.Contains(second));
        Assert.AreEqual(new Position(1, 1), second.Position);
        Assert.AreEqual(18, (int)second.ViewSize.Width);
        Assert.AreEqual(4, (int)second.ViewSize.Height);
    }

    [TestMethod]
    public void GroupBox_TinySizes_NeverGoNegative()
    {
        GroupBox box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(1, 1) };
        Assert.AreEqual(0, (int)box.ContentSize.Width);
        Assert.AreEqual(0, (int)box.ContentSize.Height);
        box.Title = "Title";
        box.Title = "Title";
        box.Draw(new MockBuffer(new Size(1, 1)));
        box.ViewSize = new Size(0, 0);
        box.Draw(new MockBuffer(new Size(1, 1)));
    }

    [TestMethod]
    public void GroupBox_NoTitle_DrawsAPlainTopEdge()
    {
        GroupBox box = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 3) };
        MockBuffer buffer = new(new Size(10, 3));
        box.Draw(buffer);
        string top = FirstLine(buffer);
        Assert.IsFalse(top.Contains(' '));
        Assert.AreEqual(10, top.Length);
    }

    // ---- Toggles ----

    [TestMethod]
    public async Task CheckBox_Disabled_IgnoresClicks_EnabledToggles()
    {
        CheckBox box = new(Theme()) { Label = "Opt", Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        await box.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(box.IsChecked);
        box.IsEnabled = false;
        await box.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(box.IsChecked);
    }

    [TestMethod]
    public async Task RadioButton_Disabled_IgnoresClicks()
    {
        RadioButton radio = new(Theme()) { Label = "Opt", Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        radio.IsEnabled = false;
        await radio.HandleMouseEventAsync(Click(1));
        Assert.IsFalse(radio.IsSelected);
        radio.IsEnabled = true;
        await radio.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(radio.IsSelected);
    }

    [TestMethod]
    public async Task ToggleButton_Disabled_IgnoresClicks()
    {
        ToggleButton toggle = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        toggle.IsEnabled = false;
        await toggle.HandleMouseEventAsync(Click(1));
        Assert.IsFalse(toggle.IsOn);
        toggle.IsEnabled = true;
        await toggle.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(toggle.IsOn);
    }

    // ---- Disabled pickers ----

    [TestMethod]
    public async Task ColorPicker_Disabled_IgnoresClicks()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        Color before = picker.SelectedColor;
        picker.IsEnabled = false;
        await picker.HandleMouseEventAsync(Click(6, 2));
        Assert.AreEqual(before, picker.SelectedColor);
    }

    [TestMethod]
    public async Task TimePicker_Disabled_IgnoresClicksAndWheel()
    {
        TimePicker picker = new(Theme()) { Value = new TimeOnly(10, 0), Position = new Position(0, 0) };
        picker.IsEnabled = false;
        await picker.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 0), WheelDelta = 1 });
        Assert.AreEqual(new TimeOnly(10, 0), picker.Value);
        Assert.IsFalse(picker.IsFocused);
    }

    [TestMethod]
    public async Task Calendar_Disabled_IgnoresWheel()
    {
        Calendar calendar = new(Theme()) { SelectedDate = new DateOnly(2026, 5, 15), Position = new Position(0, 0) };
        calendar.IsEnabled = false;
        await calendar.HandleMouseEventAsync(new MouseEvent { Position = new Position(2, 2), WheelDelta = -1 });
        Assert.AreEqual(new DateOnly(2026, 5, 15), calendar.SelectedDate);
    }

    [TestMethod]
    public async Task ListBox_Disabled_ClickDoesNotMoveCursor()
    {
        ConcurrentObservableCollection<string> items = new(["a", "b", "c"]);
        ListBoxModel<string> model = new(items);
        model.UpdateListItems();
        ListBox<string> list = new(model, Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 3) };
        list.OnInitializeLayout();
        list.IsEnabled = false;
        await list.HandleMouseEventAsync(Click(1, 2));
        Assert.AreEqual(0, (int)list.ScrollableModel.CursorPosition.Y);
        list.IsEnabled = true;
        await list.HandleMouseEventAsync(Click(1, 2));
        Assert.AreEqual(2, (int)list.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public async Task Toolbar_Wheel_StepsOverTheEnabledItems_AndStopsAtTheEnds()
    {
        Toolbar toolbar = NewToolbar();
        MouseEvent down = new() { Position = new Position(1, 0), WheelDelta = -1 };
        MouseEvent up = new() { Position = new Position(1, 0), WheelDelta = 1 };
        Assert.AreEqual(0, toolbar.ActiveIndex);

        Assert.IsTrue(await toolbar.HandleMouseEventAsync(down));
        Assert.AreEqual(2, toolbar.ActiveIndex, "the disabled Save is skipped");
        await toolbar.HandleMouseEventAsync(down);
        Assert.AreEqual(2, toolbar.ActiveIndex);

        await toolbar.HandleMouseEventAsync(up);
        Assert.AreEqual(0, toolbar.ActiveIndex);
        await toolbar.HandleMouseEventAsync(up);
        Assert.AreEqual(0, toolbar.ActiveIndex);
    }

    [TestMethod]
    public async Task Toolbar_WheelElsewhereOrDisabled_IsIgnored()
    {
        Toolbar toolbar = NewToolbar();

        await toolbar.HandleMouseEventAsync(new MouseEvent { Position = new Position(60, 5), WheelDelta = -1 });
        Assert.AreEqual(0, toolbar.ActiveIndex);

        toolbar.IsEnabled = false;
        await toolbar.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = -1 });
        Assert.AreEqual(0, toolbar.ActiveIndex);
    }
}
