using iConsole.Model;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>
/// Keyboard, mouse and focus behaviour shared by the one-line input controls: a click focuses the control,
/// a disabled or read-only control ignores both devices, and the arrow/Home/End keys stay within bounds.
/// </summary>
[TestClass]
public class ControlInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    private static MouseEvent Wheel(long x, int delta) => new() { Position = new Position(x, 0), WheelDelta = delta };

    private static string FirstLine(MockBuffer buffer) => buffer.ContentAsString.Split(Environment.NewLine)[0];

    // ---- Slider ----

    private static Slider NewSlider() => new(Theme()) { Minimum = 0, Maximum = 100, Position = new Position(0, 0), ViewSize = new Size(11, 1) };

    [TestMethod]
    public async Task Slider_Click_FocusesAndMovesThumb()
    {
        Slider slider = NewSlider();
        Assert.IsFalse(slider.IsFocused);
        await slider.HandleMouseEventAsync(Click(5));
        Assert.IsTrue(slider.IsFocused);
        Assert.AreEqual(50, slider.Value);
    }

    [TestMethod]
    public async Task Slider_Click_OnValueReadout_FocusesWithoutMovingTheThumb()
    {
        Slider slider = NewSlider();
        slider.ShowValue = true;
        slider.ViewSize = new Size(14, 1);
        slider.Value = 30;
        await slider.HandleMouseEventAsync(Click(13));
        Assert.IsTrue(slider.IsFocused);
        Assert.AreEqual(30, slider.Value);
    }

    [TestMethod]
    public async Task Slider_Disabled_IgnoresMouseAndKeys()
    {
        Slider slider = NewSlider();
        slider.SetFocus();
        slider.IsEnabled = false;
        Assert.IsFalse(await slider.HandleMouseEventAsync(Click(5)));
        Assert.IsFalse(await slider.HandleMouseEventAsync(Wheel(5, 1)));
        Assert.IsFalse(await slider.HandleKeyEventAsync(Key(ConsoleKey.RightArrow)));
        Assert.AreEqual(0, slider.Value);
    }

    [TestMethod]
    public async Task Slider_ReadOnly_IgnoresMouseAndKeys()
    {
        Slider slider = NewSlider();
        slider.SetFocus();
        slider.IsReadOnly = true;
        await slider.HandleMouseEventAsync(Click(5));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(0, slider.Value);
    }

    [TestMethod]
    public async Task Slider_Keys_StepJumpAndStayInBounds()
    {
        Slider slider = NewSlider();
        slider.SetFocus();
        await slider.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(1, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(11, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(100, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(100, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, slider.Value);
    }

    [TestMethod]
    public async Task Slider_UnrelatedKey_IsNotHandled()
    {
        Slider slider = NewSlider();
        slider.SetFocus();
        Assert.IsFalse(await slider.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task Slider_Wheel_StepsByStep()
    {
        Slider slider = NewSlider();
        slider.Step = 5;
        Assert.IsTrue(await slider.HandleMouseEventAsync(Wheel(3, 1)));
        Assert.AreEqual(5, slider.Value);
        await slider.HandleMouseEventAsync(Wheel(3, -1));
        Assert.AreEqual(0, slider.Value);
    }

    [TestMethod]
    public async Task Slider_ClickOutside_IsIgnored()
    {
        Slider slider = NewSlider();
        Assert.IsFalse(await slider.HandleMouseEventAsync(Click(40)));
        Assert.AreEqual(0, slider.Value);
    }

    [TestMethod]
    public void Slider_BoundsCrossing_DragsTheOtherBound()
    {
        Slider slider = new(Theme()) { Minimum = 0, Maximum = 10 };
        slider.Minimum = 20;
        Assert.AreEqual(20, slider.Maximum);
        Assert.AreEqual(20, slider.Value);
        slider.Maximum = 5;
        Assert.AreEqual(5, slider.Minimum);
    }

    [TestMethod]
    public void Slider_ThumbColumn_And_ValueAt_DegenerateRanges()
    {
        Assert.AreEqual(0, Slider.ThumbColumn(5, 5, 5, 10));
        Assert.AreEqual(0, Slider.ThumbColumn(5, 0, 10, 1));
        Assert.AreEqual(5, Slider.ValueAt(3, 5, 5, 10));
        Assert.AreEqual(0, Slider.ValueAt(3, 0, 10, 1));
        Assert.AreEqual(10, Slider.ValueAt(99, 0, 10, 5));
    }

    [TestMethod]
    public void Slider_ShowValue_DrawsNumberRightOfTrack()
    {
        MockBuffer buffer = new(new Size(9, 1));
        new Slider(Theme()) { Minimum = 0, Maximum = 100, Value = 100, ShowValue = true, Position = new Position(0, 0), ViewSize = new Size(9, 1) }.Draw(buffer);
        Assert.IsTrue(FirstLine(buffer).TrimEnd().EndsWith("100"));
    }

    [TestMethod]
    public void Slider_Colors_FollowState()
    {
        Slider slider = NewSlider();
        ColorScheme scheme = new();
        slider.GetColor(scheme, VisualState.Focused, out Color focusedFg, out _);
        Assert.AreEqual(scheme.SelectedFocusedForeground, focusedFg);
        slider.IsReadOnly = true;
        slider.GetColor(scheme, VisualState.Focused, out Color readOnlyFg, out _);
        Assert.AreEqual(scheme.DisabledForeground, readOnlyFg);
    }

    // ---- SegmentedControl ----

    private static SegmentedControl NewSegments() => new(Theme()) { Options = ["Day", "Week", "Month"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };

    [TestMethod]
    public async Task SegmentedControl_Keys_MoveAndStayInBounds()
    {
        SegmentedControl control = NewSegments();
        control.SetFocus();
        await control.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, control.SelectedIndex);
        await control.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual("Week", control.SelectedOption);
        await control.HandleKeyEventAsync(Key(ConsoleKey.End));
        await control.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, control.SelectedIndex);
        await control.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, control.SelectedIndex);
    }

    [TestMethod]
    public async Task SegmentedControl_Click_FocusesControl()
    {
        SegmentedControl control = NewSegments();
        await control.HandleMouseEventAsync(Click(7));
        Assert.IsTrue(control.IsFocused);
        Assert.AreEqual(1, control.SelectedIndex);
    }

    [TestMethod]
    public async Task SegmentedControl_ClickOnGap_KeepsSelection()
    {
        SegmentedControl control = NewSegments();
        await control.HandleMouseEventAsync(Click(5));
        Assert.AreEqual(0, control.SelectedIndex);
    }

    [TestMethod]
    public async Task SegmentedControl_Wheel_StepsBothWays()
    {
        SegmentedControl control = NewSegments();
        await control.HandleMouseEventAsync(Wheel(2, -1));
        Assert.AreEqual(1, control.SelectedIndex);
        await control.HandleMouseEventAsync(Wheel(2, 1));
        Assert.AreEqual(0, control.SelectedIndex);
    }

    [TestMethod]
    public async Task SegmentedControl_DisabledAndReadOnly_IgnoreInput()
    {
        SegmentedControl control = NewSegments();
        control.SetFocus();
        control.IsEnabled = false;
        Assert.IsFalse(await control.HandleMouseEventAsync(Click(7)));
        Assert.IsFalse(await control.HandleKeyEventAsync(Key(ConsoleKey.RightArrow)));
        control.IsEnabled = true;
        control.IsReadOnly = true;
        await control.HandleMouseEventAsync(Click(7));
        await control.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(0, control.SelectedIndex);
    }

    [TestMethod]
    public void SegmentedControl_Options_ClampSelectionAndRaiseEvent()
    {
        SegmentedControl control = NewSegments();
        int raised = 0;
        control.SelectionChanged += (_, _) => raised++;
        control.SelectedIndex = 2;
        control.SelectedIndex = 2;
        Assert.AreEqual(1, raised);
        control.Options = ["A"];
        Assert.AreEqual(0, control.SelectedIndex);
        control.Options = [];
        Assert.IsNull(control.SelectedOption);
    }

    [TestMethod]
    public void SegmentedControl_Draw_ListsOptionsAndPadsLine()
    {
        MockBuffer buffer = new(new Size(30, 1));
        NewSegments().Draw(buffer);
        Assert.AreEqual("Day   Week   Month", FirstLine(buffer).Trim().Replace("  ", "  "));
    }

    // ---- Rating ----

    [TestMethod]
    public async Task Rating_Keys_ChangeValueWithinBounds()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0) };
        rating.SetFocus();
        await rating.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, rating.Value);
        await rating.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(1, rating.Value);
        await rating.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(5, rating.Value);
        await rating.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, rating.Value);
        await rating.HandleKeyEventAsync(Key(ConsoleKey.D3, '3'));
        Assert.AreEqual(3, rating.Value);
        await rating.HandleKeyEventAsync(Key(ConsoleKey.D9, '9'));
        Assert.AreEqual(3, rating.Value, "9 is past the five stars: ignored, not clamped");
        Assert.IsFalse(await rating.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task Rating_Click_FocusesControl()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0) };
        await rating.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(rating.IsFocused);
        Assert.AreEqual(2, rating.Value);
    }

    [TestMethod]
    public async Task Rating_DisabledAndReadOnly_IgnoreInput()
    {
        Rating rating = new(Theme()) { Position = new Position(0, 0), Value = 2 };
        rating.SetFocus();
        rating.IsEnabled = false;
        await rating.HandleMouseEventAsync(Click(4));
        await rating.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(2, rating.Value);
        rating.IsEnabled = true;
        rating.IsReadOnly = true;
        await rating.HandleMouseEventAsync(Click(4));
        await rating.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(2, rating.Value);
    }

    [TestMethod]
    public void Rating_Maximum_ResizesAndClampsValue()
    {
        Rating rating = new(Theme()) { Value = 5 };
        rating.Maximum = 3;
        Assert.AreEqual(3, rating.Value);
        Assert.AreEqual(3, (int)rating.ViewSize.Width);
        rating.Maximum = 0;
        Assert.AreEqual(1, rating.Maximum);
    }

    // ---- Breadcrumb ----

    [TestMethod]
    public async Task Breadcrumb_Keys_MoveActiveSegmentAndActivate()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["A", "B", "C"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        crumb.SetFocus();
        Assert.AreEqual(2, crumb.ActiveIndex);
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, crumb.ActiveIndex);
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(1, crumb.ActiveIndex);
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, crumb.ActiveIndex);
        int activated = -1;
        crumb.SegmentActivated += (_, i) => activated = i;
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(0, activated);
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(2, crumb.ActiveIndex);
        Assert.IsFalse(await crumb.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task Breadcrumb_Empty_IgnoresKeys()
    {
        Breadcrumb crumb = new(Theme()) { Position = new Position(0, 0) };
        crumb.SetFocus();
        Assert.IsFalse(await crumb.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        Assert.AreEqual(0, crumb.ActiveIndex);
    }

    [TestMethod]
    public async Task Breadcrumb_Click_FocusesAndSkipsFoldedMiddle()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["Home", "Projects", "Deep", "Folder", "Leaf"], Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        List<int> activated = [];
        crumb.SegmentActivated += (_, i) => activated.Add(i);
        await crumb.HandleMouseEventAsync(Click(0));
        Assert.IsTrue(crumb.IsFocused);
        Assert.AreEqual(0, crumb.ActiveIndex);
        await crumb.HandleMouseEventAsync(Click(7));
        CollectionAssert.AreEqual(new[] { 0 }, activated);
        await crumb.HandleMouseEventAsync(Click(12));
        CollectionAssert.AreEqual(new[] { 0, 4 }, activated);
    }

    [TestMethod]
    public async Task Breadcrumb_Disabled_IgnoresClick()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["A", "B"], Position = new Position(0, 0), ViewSize = new Size(30, 1), IsEnabled = false };
        int activated = 0;
        crumb.SegmentActivated += (_, _) => activated++;
        await crumb.HandleMouseEventAsync(Click(0));
        Assert.AreEqual(0, activated);
    }

    [TestMethod]
    public void Breadcrumb_Layout_TooNarrow_KeepsOnlyEllipsisAndLast()
    {
        List<(int Index, string Text)> parts = Breadcrumb.Layout(["Home", "Projects", "Deep", "Leaf"], 5);
        Assert.AreEqual(2, parts.Count);
        Assert.AreEqual(-1, parts[0].Index);
        Assert.AreEqual(3, parts[1].Index);
        Assert.AreEqual(0, Breadcrumb.Layout([], 10).Count);
    }

    [TestMethod]
    public void Breadcrumb_Draw_FoldedTrailShowsEllipsis()
    {
        MockBuffer buffer = new(new Size(20, 1));
        Breadcrumb crumb = new(Theme()) { Segments = ["Home", "Projects", "Deep", "Folder", "Leaf"], Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        crumb.SetFocus();
        crumb.Draw(buffer);
        string line = FirstLine(buffer).TrimEnd();
        Assert.IsTrue(line.Contains('…'));
        Assert.IsTrue(line.StartsWith("Home"));
        Assert.IsTrue(line.EndsWith("Leaf"));
    }

    // ---- Pagination ----

    private static Pagination NewPager() => new(Theme()) { PageSize = 10, TotalItems = 35, Position = new Position(0, 0), ViewSize = new Size(30, 1) };

    [TestMethod]
    public async Task Pagination_Keys_MoveAndStayInBounds()
    {
        Pagination pager = NewPager();
        pager.SetFocus();
        await pager.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, pager.Page);
        await pager.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        await pager.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, pager.Page);
        await pager.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(1, pager.Page);
        await pager.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(3, pager.Page);
        await pager.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(3, pager.Page);
        await pager.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, pager.Page);
        Assert.IsFalse(await pager.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task Pagination_ClickArrows_StepAndFocus()
    {
        Pagination pager = NewPager();
        int right = pager.Caption.Length + 3;
        await pager.HandleMouseEventAsync(Click(right));
        Assert.AreEqual(1, pager.Page);
        Assert.IsTrue(pager.IsFocused);
        await pager.HandleMouseEventAsync(Click(0));
        Assert.AreEqual(0, pager.Page);
        await pager.HandleMouseEventAsync(Click(5));
        Assert.AreEqual(0, pager.Page);
    }

    [TestMethod]
    public async Task Pagination_ClickPaddingBesideCaption_DoesNotStep()
    {
        // The space between the caption and the right arrow is not the arrow: a click there must not page.
        Pagination pager = NewPager();
        await pager.HandleMouseEventAsync(Click(pager.Caption.Length + 2));
        Assert.AreEqual(0, pager.Page);
    }

    [TestMethod]
    public void Pagination_Draw_RightArrowSitsAtClickColumn()
    {
        Pagination pager = NewPager();
        var buffer = new MockBuffer(new Size(30, 1));
        pager.Draw(buffer);

        string line = FirstLine(buffer);
        Assert.AreEqual('›', line[pager.Caption.Length + 3]);
    }

    [TestMethod]
    public async Task Pagination_Wheel_StepsPages()
    {
        Pagination pager = NewPager();
        await pager.HandleMouseEventAsync(Wheel(3, -1));
        Assert.AreEqual(1, pager.Page);
        await pager.HandleMouseEventAsync(Wheel(3, 1));
        Assert.AreEqual(0, pager.Page);
    }

    [TestMethod]
    public async Task Pagination_Disabled_IgnoresInput()
    {
        Pagination pager = NewPager();
        pager.SetFocus();
        pager.IsEnabled = false;
        await pager.HandleMouseEventAsync(Wheel(3, -1));
        await pager.HandleMouseEventAsync(Click(pager.Caption.Length + 3));
        await pager.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(0, pager.Page);
    }

    [TestMethod]
    public void Pagination_ShrinkingTotal_PullsPageBack()
    {
        Pagination pager = NewPager();
        pager.Page = 3;
        pager.TotalItems = 12;
        Assert.AreEqual(1, pager.Page);
        pager.PageSize = 0;
        Assert.AreEqual(1, pager.PageSize);
        pager.TotalItems = -4;
        Assert.AreEqual(0, pager.Page);
        Assert.AreEqual(1, pager.PageCount);
    }

    // ---- Expander ----

    private static Expander NewExpander()
    {
        Expander expander = new(Theme()) { Header = "More", Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        expander.Content = new Label("inner", Theme());
        return expander;
    }

    [TestMethod]
    public async Task Expander_Arrows_ExpandAndCollapse()
    {
        Expander expander = NewExpander();
        expander.SetFocus();
        await expander.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.IsTrue(expander.IsExpanded);
        Assert.AreEqual(4, (int)expander.ViewSize.Height);
        await expander.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.IsFalse(expander.IsExpanded);
        Assert.AreEqual(1, (int)expander.ViewSize.Height);
        await expander.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.IsTrue(expander.IsExpanded);
        Assert.IsFalse(await expander.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task Expander_ClickHeader_TogglesAndFocuses_ClickBodyDoesNot()
    {
        Expander expander = NewExpander();
        await expander.HandleMouseEventAsync(Click(2, 0));
        Assert.IsTrue(expander.IsExpanded);
        Assert.IsTrue(expander.IsFocused);
        await expander.HandleMouseEventAsync(Click(2, 2));
        Assert.IsTrue(expander.IsExpanded);
    }

    [TestMethod]
    public async Task Expander_Disabled_IgnoresClickAndKeys()
    {
        Expander expander = NewExpander();
        expander.SetFocus();
        expander.IsEnabled = false;
        await expander.HandleMouseEventAsync(Click(2, 0));
        await expander.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        Assert.IsFalse(expander.IsExpanded);
    }

    [TestMethod]
    public void Expander_ExpandedChanged_FiresOncePerChange()
    {
        Expander expander = NewExpander();
        List<bool> seen = [];
        expander.ExpandedChanged += (_, e) => seen.Add(e);
        expander.IsExpanded = true;
        expander.IsExpanded = true;
        expander.IsExpanded = false;
        CollectionAssert.AreEqual(new[] { true, false }, seen);
    }

    [TestMethod]
    public void Expander_ReplacingContent_DetachesOldOne()
    {
        Expander expander = NewExpander();
        IControl first = expander.Content!;
        expander.IsExpanded = true;
        Assert.IsTrue(expander.Controls.Contains(first));
        Label second = new("new", Theme());
        expander.Content = second;
        Assert.IsFalse(expander.Controls.Contains(first));
        expander.IsExpanded = false;
        expander.IsExpanded = true;
        Assert.IsTrue(expander.Controls.Contains(second));
    }

    [TestMethod]
    public void Expander_Draw_ShowsArrowForState()
    {
        Expander expander = NewExpander();
        MockBuffer collapsed = new(new Size(20, 1));
        expander.Draw(collapsed);
        Assert.AreEqual("▶ More", FirstLine(collapsed).TrimEnd());
        expander.IsExpanded = true;
        MockBuffer open = new(new Size(20, 4));
        expander.Draw(open);
        Assert.AreEqual("▼ More", FirstLine(open).TrimEnd());
    }

    // ---- LinkLabel ----

    [TestMethod]
    public async Task LinkLabel_EnterSpaceAndClick_Activate()
    {
        LinkLabel link = new(Theme()) { Text = "docs", Position = new Position(0, 0) };
        int activated = 0;
        link.Activated += (_, _) => activated++;
        Assert.IsFalse(await link.HandleKeyEventAsync(Key(ConsoleKey.Enter)));
        link.SetFocus();
        await link.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await link.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        Assert.IsFalse(await link.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
        Assert.AreEqual(2, activated);
        await link.HandleMouseEventAsync(Click(1));
        Assert.AreEqual(3, activated);
    }

    [TestMethod]
    public async Task LinkLabel_ClickFocusesAndOutsideClickIsIgnored()
    {
        LinkLabel link = new(Theme()) { Text = "docs", Position = new Position(0, 0) };
        int activated = 0;
        link.Activated += (_, _) => activated++;
        await link.HandleMouseEventAsync(Click(20));
        Assert.AreEqual(0, activated);
        await link.HandleMouseEventAsync(Click(0));
        Assert.IsTrue(link.IsFocused);
        Assert.AreEqual(1, activated);
    }

    [TestMethod]
    public async Task LinkLabel_Disabled_DoesNotActivate()
    {
        LinkLabel link = new(Theme()) { Text = "docs", Position = new Position(0, 0) };
        link.SetFocus();
        link.IsEnabled = false;
        int activated = 0;
        link.Activated += (_, _) => activated++;
        await link.HandleMouseEventAsync(Click(1));
        await link.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(0, activated);
    }

    [TestMethod]
    public void LinkLabel_Text_SizesControlAndDraws()
    {
        LinkLabel link = new(Theme()) { Text = "docs", Target = "https://example.com", Position = new Position(0, 0) };
        Assert.AreEqual(4, (int)link.ViewSize.Width);
        MockBuffer buffer = new(new Size(8, 1));
        link.Draw(buffer);
        Assert.AreEqual("docs", FirstLine(buffer).TrimEnd());
        link.Text = null!;
        Assert.AreEqual(0, (int)link.ViewSize.Width);
        link.Draw(new MockBuffer(new Size(8, 1)));
    }

    // ---- CheckList ----

    private static CheckList NewCheckList()
    {
        CheckList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        list.SetItems(["a", "b", "c", "d", "e"], ["b"]);
        return list;
    }

    [TestMethod]
    public async Task CheckList_Keys_NavigateScrollAndToggle()
    {
        CheckList list = NewCheckList();
        list.SetFocus();
        await list.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, list.CursorIndex);
        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await list.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        Assert.AreEqual(0, list.CheckedItems.Count());
        await list.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(3, list.CursorIndex);
        await list.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(4, list.CursorIndex);
        await list.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(2, list.CursorIndex);
        await list.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(0, list.CursorIndex);
    }

    [TestMethod]
    public async Task CheckList_Star_InvertsAndEnterActivates()
    {
        CheckList list = NewCheckList();
        list.SetFocus();
        await list.HandleKeyEventAsync(Key(ConsoleKey.D8, '*'));
        CollectionAssert.AreEqual(new[] { "a", "c", "d", "e" }, list.CheckedItems.ToArray());
        int activated = -1;
        list.Activated += (_, i) => activated = i;
        await list.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await list.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(1, activated);
        Assert.IsFalse(await list.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task CheckList_ReadOnly_MovesButDoesNotToggle()
    {
        CheckList list = NewCheckList();
        list.SetFocus();
        list.IsReadOnly = true;
        await list.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        await list.HandleKeyEventAsync(Key(ConsoleKey.D8, '*'));
        await list.HandleMouseEventAsync(Click(2, 2));
        Assert.AreEqual(2, list.CursorIndex);
        CollectionAssert.AreEqual(new[] { "b" }, list.CheckedItems.ToArray());
    }

    [TestMethod]
    public async Task CheckList_ClickRow_FocusesMovesAndToggles()
    {
        CheckList list = NewCheckList();
        int changed = 0;
        list.CheckedChanged += (_, _) => changed++;
        await list.HandleMouseEventAsync(Click(2, 0));
        Assert.IsTrue(list.IsFocused);
        Assert.AreEqual(1, changed);
        Assert.IsTrue(list.CheckedItems.Contains("a"));
        await list.HandleMouseEventAsync(Click(2, 2));
        Assert.AreEqual(2, list.CursorIndex);
    }

    [TestMethod]
    public async Task CheckList_ClickBelowLastItem_DoesNotToggle()
    {
        CheckList list = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        list.SetItems(["a", "b"]);
        await list.HandleMouseEventAsync(Click(2, 4));
        Assert.AreEqual(0, list.CheckedItems.Count());
    }

    [TestMethod]
    public async Task CheckList_Wheel_MovesThreeRows_AndDisabledIgnoresMouse()
    {
        CheckList list = NewCheckList();
        await list.HandleMouseEventAsync(Wheel(2, -1));
        Assert.AreEqual(3, list.CursorIndex);
        list.IsEnabled = false;
        await list.HandleMouseEventAsync(Click(2, 0));
        await list.HandleMouseEventAsync(Wheel(2, 1));
        Assert.AreEqual(3, list.CursorIndex);
        Assert.AreEqual(1, list.CheckedItems.Count());
    }

    [TestMethod]
    public void CheckList_InvertAll_OnEmptyList_RaisesNothing()
    {
        CheckList list = new(Theme());
        int changed = 0;
        list.CheckedChanged += (_, _) => changed++;
        list.InvertAll();
        list.SetChecked(3, true);
        Assert.AreEqual(0, changed);
    }

    [TestMethod]
    public void CheckList_Draw_ShowsItemsAndEmptyText()
    {
        CheckList list = NewCheckList();
        MockBuffer buffer = new(new Size(20, 3));
        list.Draw(buffer);
        string[] lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].Contains('a'));
        Assert.IsTrue(lines[1].Contains('b'));

        CheckList empty = new(Theme()) { EmptyText = "Nothing to pick", Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        MockBuffer emptyBuffer = new(new Size(20, 3));
        empty.Draw(emptyBuffer);
        Assert.IsTrue(FirstLine(emptyBuffer).Contains("Nothing to pick"));
    }

    // ---- Chips ----

    private static Chips NewChips()
    {
        Chips chips = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        chips.Add("rock");
        chips.Add("jazz");
        return chips;
    }

    [TestMethod]
    public void Chips_Add_TrimsBeforeCheckingDuplicates()
    {
        Chips chips = NewChips();
        Assert.IsFalse(chips.Add("  ROCK "));
        Assert.IsTrue(chips.Add("  pop "));
        Assert.AreEqual("pop", chips.Items[^1]);
        Assert.IsFalse(chips.Add(null!));
    }

    [TestMethod]
    public async Task Chips_Keys_MoveStayInBoundsAndRemove()
    {
        Chips chips = NewChips();
        chips.SetFocus();
        await chips.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual(0, chips.ActiveIndex);
        await chips.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await chips.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await chips.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        Assert.AreEqual(2, chips.ActiveIndex);
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        Assert.AreEqual(2, chips.Items.Count);
        await chips.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        CollectionAssert.AreEqual(new[] { "rock" }, chips.Items.ToArray());
        Assert.IsFalse(await chips.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task Chips_Enter_OnlyAddsFromPlusChip()
    {
        Chips chips = NewChips();
        chips.SetFocus();
        int requests = 0;
        chips.AddRequested += (_, _) => requests++;
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(0, requests);
        chips.ActiveIndex = 2;
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(1, requests);
    }

    [TestMethod]
    public async Task Chips_ReadOnly_HasNoPlusAndCannotRemove()
    {
        Chips chips = NewChips();
        chips.SetFocus();
        chips.IsReadOnly = true;
        chips.ActiveIndex = 9;
        Assert.AreEqual(1, chips.ActiveIndex);
        await chips.HandleKeyEventAsync(Key(ConsoleKey.Delete));
        await chips.HandleMouseEventAsync(Click(10));
        Assert.AreEqual(2, chips.Items.Count);
        MockBuffer buffer = new(new Size(40, 1));
        chips.Draw(buffer);
        Assert.AreEqual("[rock] [jazz]", FirstLine(buffer).TrimEnd());
    }

    [TestMethod]
    public async Task Chips_Click_PlusRequestsAddAndChipHighlights()
    {
        Chips chips = NewChips();
        int requests = 0;
        chips.AddRequested += (_, _) => requests++;
        await chips.HandleMouseEventAsync(Click(1));
        Assert.IsTrue(chips.IsFocused);
        Assert.AreEqual(0, chips.ActiveIndex);
        await chips.HandleMouseEventAsync(Click(19));
        Assert.AreEqual(1, requests);
        Assert.AreEqual(2, chips.ActiveIndex);
    }

    [TestMethod]
    public async Task Chips_Disabled_IgnoresClicks()
    {
        Chips chips = NewChips();
        chips.IsEnabled = false;
        Assert.IsFalse(await chips.HandleMouseEventAsync(Click(1)));
        Assert.IsFalse(chips.IsFocused);
    }

    [TestMethod]
    public void Chips_Visible_SlidesWindowToKeepActiveChipOnScreen()
    {
        Chips chips = new(Theme());
        for (int i = 0; i < 6; i++)
        {
            chips.Add("item" + i);
        }

        chips.ActiveIndex = 5;
        List<(int Index, int Start)> shown = chips.Visible(30);
        Assert.IsTrue(shown.Any(s => s.Index == 5));
        Assert.IsFalse(shown.Any(s => s.Index == 0));
        chips.ActiveIndex = 0;
        Assert.AreEqual(0, chips.Visible(30)[0].Index);
        Assert.AreEqual(1, chips.Visible(2).Count, "a chip wider than the control is cut, not dropped");
    }

    // ---- NumericUpDown ----

    private static NumericUpDown NewSpinner()
    {
        NumericUpDown spinner = new(Theme()) { Minimum = -50, Maximum = 100, Value = 5, Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        spinner.Draw(new MockBuffer(new Size(20, 1)));
        return spinner;
    }

    [TestMethod]
    public async Task NumericUpDown_ClickArrows_FocusAndStep()
    {
        NumericUpDown spinner = NewSpinner();
        string text = FirstLineOf(spinner);
        int up = text.IndexOf(Border.ArrowUp);
        Assert.IsTrue(up >= 0);
        await spinner.HandleMouseEventAsync(Click(up));
        Assert.IsTrue(spinner.IsFocused);
        Assert.AreEqual(6, spinner.Value);
        await spinner.HandleMouseEventAsync(Click(up + 1));
        Assert.AreEqual(5, spinner.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_Disabled_IgnoresMouseAndWheel()
    {
        NumericUpDown spinner = NewSpinner();
        spinner.IsEnabled = false;
        Assert.IsFalse(await spinner.HandleMouseEventAsync(Wheel(2, 1)));
        Assert.AreEqual(5, spinner.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_Typing_DigitsSignAndBackspace()
    {
        NumericUpDown spinner = NewSpinner();
        spinner.SetFocus();
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.D2, '2'));
        Assert.AreEqual(52, spinner.Value);
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));
        Assert.AreEqual(-50, spinner.Value); // -52 clamped to the minimum
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        Assert.AreEqual(-5, spinner.Value);
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.Home));
        Assert.AreEqual(-50, spinner.Value);
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.End));
        Assert.AreEqual(100, spinner.Value);
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.D9, '9'));
        Assert.AreEqual(9, spinner.Value, "1009 is out of range: the 9 starts a new entry");
    }

    [TestMethod]
    public async Task NumericUpDown_ReadOnly_IgnoresEverything()
    {
        NumericUpDown spinner = NewSpinner();
        spinner.SetFocus();
        spinner.IsReadOnly = true;
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await spinner.HandleMouseEventAsync(Wheel(2, 1));
        Assert.AreEqual(5, spinner.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_LargeStepAndOverflow_StayInRange()
    {
        NumericUpDown spinner = NewSpinner();
        spinner.SetFocus();
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.PageUp));
        Assert.AreEqual(15, spinner.Value);
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.PageDown));
        Assert.AreEqual(-5, spinner.Value);
        spinner.Maximum = long.MaxValue;
        spinner.Value = long.MaxValue;
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await spinner.HandleKeyEventAsync(Key(ConsoleKey.D1, '1'));
        Assert.AreEqual(long.MaxValue, spinner.Value);
    }

    private static string FirstLineOf(NumericUpDown spinner)
    {
        MockBuffer buffer = new(new Size(20, 1));
        spinner.Draw(buffer);
        return FirstLine(buffer);
    }

    [TestMethod]
    public async Task Slider_UpAndDown_StepLikeRightAndLeft()
    {
        Slider slider = NewSlider();
        slider.SetFocus();

        await slider.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(2, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(1, slider.Value);
        await slider.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await slider.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(0, slider.Value, "and it stops at the minimum");
    }

    [TestMethod]
    public async Task SegmentedControl_UpAndDown_MoveTheSelectionAndStayInBounds()
    {
        SegmentedControl control = NewSegments();
        control.SetFocus();

        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(0, control.SelectedIndex);
        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        await control.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(2, control.SelectedIndex);
        await control.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual(1, control.SelectedIndex);
    }

    [TestMethod]
    public async Task Breadcrumb_Space_ActivatesTheSegmentLikeEnter()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["A", "B", "C"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        crumb.SetFocus();
        List<int> activated = [];
        crumb.SegmentActivated += (_, index) => activated.Add(index);
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));

        Assert.IsTrue(await crumb.HandleKeyEventAsync(Key(ConsoleKey.Spacebar, ' ')));
        await crumb.HandleKeyEventAsync(Key(ConsoleKey.Enter));

        Assert.AreEqual(2, activated.Count);
        Assert.AreEqual(activated[0], activated[1]);
    }

    [TestMethod]
    public async Task Breadcrumb_Wheel_StepsTheActiveSegment_AndStopsAtTheEnds()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["A", "B", "C"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        crumb.SetFocus();
        crumb.ActiveIndex = 0;
        MouseEvent down = new() { Position = new Position(1, 0), WheelDelta = -1 };

        Assert.IsTrue(await crumb.HandleMouseEventAsync(down));
        Assert.AreEqual(1, crumb.ActiveIndex);
        await crumb.HandleMouseEventAsync(down);
        await crumb.HandleMouseEventAsync(down);
        Assert.AreEqual(2, crumb.ActiveIndex);

        await crumb.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = 1 });
        Assert.AreEqual(1, crumb.ActiveIndex);
    }

    [TestMethod]
    public async Task Breadcrumb_WheelElsewhereOrDisabled_IsIgnored()
    {
        Breadcrumb crumb = new(Theme()) { Segments = ["A", "B", "C"], Position = new Position(0, 0), ViewSize = new Size(30, 1) };
        crumb.ActiveIndex = 1;

        await crumb.HandleMouseEventAsync(new MouseEvent { Position = new Position(50, 9), WheelDelta = -1 });
        Assert.AreEqual(1, crumb.ActiveIndex);

        crumb.IsEnabled = false;
        await crumb.HandleMouseEventAsync(new MouseEvent { Position = new Position(1, 0), WheelDelta = -1 });
        Assert.AreEqual(1, crumb.ActiveIndex);
    }

    [TestMethod]
    public async Task CheckList_PlusChecksEverything_MinusUnchecksEverything()
    {
        CheckList list = NewCheckList();
        list.SetFocus();
        int changed = 0;
        list.CheckedChanged += (_, _) => changed++;

        Assert.IsTrue(await list.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+')));
        Assert.AreEqual(5, list.CheckedItems.Count());
        Assert.AreEqual(1, changed);

        Assert.IsTrue(await list.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-')));
        Assert.AreEqual(0, list.CheckedItems.Count());
        Assert.AreEqual(2, changed);
    }

    [TestMethod]
    public async Task CheckList_PlusWhenAllChecked_RaisesNothingNew()
    {
        CheckList list = NewCheckList();
        list.SetFocus();
        list.SetAll(true);
        int changed = 0;
        list.CheckedChanged += (_, _) => changed++;

        await list.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+'));

        Assert.AreEqual(0, changed);
        Assert.AreEqual(5, list.CheckedItems.Count());
    }

    [TestMethod]
    public async Task CheckList_ReadOnlyOrUnfocused_IgnoresPlusAndMinus()
    {
        CheckList list = NewCheckList();
        Assert.IsFalse(await list.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+')), "not focused");

        list.SetFocus();
        list.IsReadOnly = true;
        await list.HandleKeyEventAsync(Key(ConsoleKey.OemPlus, '+'));
        await list.HandleKeyEventAsync(Key(ConsoleKey.OemMinus, '-'));

        CollectionAssert.AreEqual(new[] { "b" }, list.CheckedItems.ToArray());
    }

    // ---- Click takes the focus (CheckBox, RadioButton, ToggleButton, Button) ----

    [TestMethod]
    public async Task CheckBox_Click_TakesFocusFromSibling()
    {
        Window root = new(Theme()) { ViewSize = new Size(30, 4) };
        CheckBox first = new(Theme()) { Label = "first", Position = new Position(0, 0), ViewSize = new Size(12, 1) };
        CheckBox second = new(Theme()) { Label = "second", Position = new Position(0, 1), ViewSize = new Size(12, 1) };
        root.AddControl(first);
        root.AddControl(second);
        first.SetFocus();

        await second.HandleMouseEventAsync(Click(3, 1));

        Assert.IsTrue(second.IsChecked);
        Assert.IsTrue(second.IsFocused);
        Assert.IsFalse(first.IsFocused);
    }

    [TestMethod]
    public async Task RadioButton_Click_TakesFocusFromSibling()
    {
        Window root = new(Theme()) { ViewSize = new Size(30, 4) };
        RadioButton first = new(Theme()) { Label = "first", Position = new Position(0, 0), ViewSize = new Size(12, 1) };
        RadioButton second = new(Theme()) { Label = "second", Position = new Position(0, 1), ViewSize = new Size(12, 1) };
        root.AddControl(first);
        root.AddControl(second);
        first.SetFocus();

        await second.HandleMouseEventAsync(Click(3, 1));

        Assert.IsTrue(second.IsSelected);
        Assert.IsTrue(second.IsFocused);
        Assert.IsFalse(first.IsFocused);
    }

    [TestMethod]
    public async Task ToggleButton_Click_TakesFocusAndFlips()
    {
        ToggleButton toggle = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };

        await toggle.HandleMouseEventAsync(Click(2));

        Assert.IsTrue(toggle.IsOn);
        Assert.IsTrue(toggle.IsFocused);
    }

    [TestMethod]
    public async Task ReadOnlyToggleButton_Click_DoesNotTakeFocus()
    {
        ToggleButton toggle = new(Theme()) { IsReadOnly = true, Position = new Position(0, 0), ViewSize = new Size(10, 1) };

        await toggle.HandleMouseEventAsync(Click(2));

        Assert.IsFalse(toggle.IsOn);
        Assert.IsFalse(toggle.IsFocused);
    }

    [TestMethod]
    public async Task Button_Click_TakesFocusFromSibling()
    {
        Window root = new(Theme()) { ViewSize = new Size(30, 4) };
        Mock<ICommand> command = new();
        command.Setup(c => c.CanExecute()).Returns(true);
        Button first = new("First", command.Object, Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        Button second = new("Second", command.Object, Theme()) { Position = new Position(0, 1), ViewSize = new Size(10, 1) };
        root.AddControl(first);
        root.AddControl(second);
        first.SetFocus();

        await second.HandleMouseEventAsync(Click(2, 1));

        Assert.IsTrue(second.IsFocused);
        Assert.IsFalse(first.IsFocused);
    }

    // ---- Key hints in Help (Rating, Expander, Toolbar, Slider, SegmentedControl, Chips) ----

    [TestMethod]
    public void Rating_KeyHints_ListedOnlyWhenEditable()
    {
        Rating rating = new(Theme()) { Maximum = 5 };
        CollectionAssert.Contains(rating.GetKeyHints().Select(h => h.Key).ToList(), "Left/Right");

        rating.IsReadOnly = true;
        Assert.AreEqual(0, rating.GetKeyHints().Count());
    }

    [TestMethod]
    public void Expander_KeyHints_NameEveryToggleKey()
    {
        var keys = NewExpander().GetKeyHints().Select(h => h.Key).ToList();

        CollectionAssert.AreEquivalent(new[] { "Enter/Space", "Right/Left", "+/-" }, keys);
    }

    [TestMethod]
    public void Toolbar_KeyHints_EmptyWithoutItems_ListedWithItems()
    {
        Toolbar toolbar = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(40, 1) };
        Assert.AreEqual(0, toolbar.GetKeyHints().Count());

        toolbar.Items = [new ToolbarItem("Open"), new ToolbarItem("Save")];

        CollectionAssert.Contains(toolbar.GetKeyHints().Select(h => h.Key).ToList(), "Enter/Space");
    }

    [TestMethod]
    public void Slider_KeyHints_EmptyWhenReadOnly()
    {
        Slider slider = NewSlider();
        CollectionAssert.Contains(slider.GetKeyHints().Select(h => h.Key).ToList(), "PageUp/PageDown");

        slider.IsReadOnly = true;
        Assert.AreEqual(0, slider.GetKeyHints().Count());
    }

    [TestMethod]
    public void SegmentedControl_KeyHints_EmptyWhenDisabled()
    {
        SegmentedControl control = NewSegments();
        CollectionAssert.Contains(control.GetKeyHints().Select(h => h.Key).ToList(), "Home/End");

        control.IsEnabled = false;
        Assert.AreEqual(0, control.GetKeyHints().Count());
    }

    [TestMethod]
    public void Chips_KeyHints_OmitEditKeysWhenReadOnly()
    {
        Chips chips = NewChips();
        var editable = chips.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(editable, "Delete/Backspace");
        CollectionAssert.Contains(editable, "Enter");

        chips.IsReadOnly = true;
        var readOnly = chips.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(readOnly, "Left/Right");
        CollectionAssert.DoesNotContain(readOnly, "Delete/Backspace");
        CollectionAssert.DoesNotContain(readOnly, "Enter");
    }

    // ---- Spinner: any click focuses, only the left button steps ----

    [TestMethod]
    public async Task NumericUpDown_ClickOnValueText_FocusesWithoutStepping()
    {
        NumericUpDown spinner = NewSpinner();
        Assert.IsFalse(spinner.IsFocused);

        await spinner.HandleMouseEventAsync(Click(2));

        Assert.IsTrue(spinner.IsFocused);
        Assert.AreEqual(5, spinner.Value);
    }

    [TestMethod]
    public async Task NumericUpDown_RightClick_DoesNotStep()
    {
        NumericUpDown spinner = NewSpinner();
        int up = FirstLineOf(spinner).IndexOf(Border.ArrowUp);

        await spinner.HandleMouseEventAsync(new MouseEvent { Position = new Position(up, 0), RightClicked = true });

        Assert.AreEqual(5, spinner.Value);
    }

    // ---- Key hints in Help (Pagination, NumericUpDown, Breadcrumb, ColorPicker, CheckList, Calendar, ComboBox) ----

    [TestMethod]
    public void Pagination_KeyHints_EmptyWhenDisabled()
    {
        Pagination pager = NewPager();
        CollectionAssert.Contains(pager.GetKeyHints().Select(h => h.Key).ToList(), "Home/End");

        pager.IsEnabled = false;
        Assert.AreEqual(0, pager.GetKeyHints().Count());
    }

    [TestMethod]
    public void NumericUpDown_KeyHints_EmptyWhenReadOnly()
    {
        NumericUpDown spinner = NewSpinner();
        CollectionAssert.Contains(spinner.GetKeyHints().Select(h => h.Key).ToList(), "PageUp/PageDown");

        spinner.IsReadOnly = true;
        Assert.AreEqual(0, spinner.GetKeyHints().Count());
    }

    [TestMethod]
    public void Breadcrumb_KeyHints_EmptyWithoutSegments()
    {
        Breadcrumb crumb = new(Theme()) { Position = new Position(0, 0) };
        Assert.AreEqual(0, crumb.GetKeyHints().Count());

        crumb.Segments = ["A", "B"];

        CollectionAssert.Contains(crumb.GetKeyHints().Select(h => h.Key).ToList(), "Enter/Space");
    }

    [TestMethod]
    public void ColorPicker_KeyHints_OmitChooseWhenReadOnly()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        CollectionAssert.Contains(picker.GetKeyHints().Select(h => h.Key).ToList(), "Enter/Space");

        picker.IsReadOnly = true;
        CollectionAssert.DoesNotContain(picker.GetKeyHints().Select(h => h.Key).ToList(), "Enter/Space");
    }

    [TestMethod]
    public void CheckList_KeyHints_OmitEditKeysWhenReadOnly()
    {
        CheckList list = NewCheckList();
        var editable = list.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(editable, "*");
        CollectionAssert.Contains(editable, "+/-");

        list.IsReadOnly = true;
        var readOnly = list.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(readOnly, "PageUp/PageDown");
        CollectionAssert.DoesNotContain(readOnly, "*");
        CollectionAssert.DoesNotContain(readOnly, "+/-");
    }

    [TestMethod]
    public void Calendar_KeyHints_OmitTodayAndYearWhenReadOnly()
    {
        Calendar calendar = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 8) };
        var editable = calendar.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(editable, "T");
        CollectionAssert.Contains(editable, "Ctrl+PageUp/PageDown");

        calendar.IsReadOnly = true;
        var readOnly = calendar.GetKeyHints().Select(h => h.Key).ToList();
        CollectionAssert.Contains(readOnly, "PageUp/PageDown");
        CollectionAssert.DoesNotContain(readOnly, "T");
        CollectionAssert.DoesNotContain(readOnly, "Ctrl+PageUp/PageDown");
    }

    [TestMethod]
    public void ComboBox_KeyHints_EmptyWhenDisabled()
    {
        ComboBox<string> combo = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        CollectionAssert.Contains(combo.GetKeyHints().Select(h => h.Key).ToList(), "Enter/Space/F4");

        combo.IsEnabled = false;
        Assert.AreEqual(0, combo.GetKeyHints().Count());
    }

    // ---- Key hints in Help (TextBox, SearchBox, TreeView, Table, TimePicker, RadioGroup, ToggleButton, LinkLabel, Alert) ----

    private static List<string> HintKeys(Control control) => control.GetKeyHints().Select(h => h.Key).ToList();

    [TestMethod]
    public void TextBox_KeyHints_EditKeysOnlyWhenEditable()
    {
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme()) { Position = new Position(0, 0) };
        CollectionAssert.Contains(HintKeys(box), "Ctrl+U");

        box.IsReadOnly = true;
        var readOnly = HintKeys(box);
        CollectionAssert.Contains(readOnly, "Home/End");
        CollectionAssert.DoesNotContain(readOnly, "Ctrl+U");
        CollectionAssert.DoesNotContain(readOnly, "Backspace/Delete");

        box.IsReadOnly = false;
        box.IsEnabled = false;
        Assert.AreEqual(0, box.GetKeyHints().Count());
    }

    [TestMethod]
    public void SearchBox_KeyHints_AddEscapeToTheTextKeys()
    {
        SearchBox search = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme()) { Position = new Position(0, 0) };

        var keys = HintKeys(search);

        CollectionAssert.Contains(keys, "Home/End");
        CollectionAssert.Contains(keys, "Esc");
    }

    [TestMethod]
    public void TreeView_KeyHints_EmptyWhenDisabled()
    {
        var root = new HierarchicalItemModel(null, "/") { IsOpened = true };
        TreeView tree = new(root, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        CollectionAssert.Contains(HintKeys(tree), "Up/Down");

        tree.IsEnabled = false;
        Assert.AreEqual(0, tree.GetKeyHints().Count());
    }

    [TestMethod]
    public void Table_KeyHints_EnterOnlyWhenThereAreRows()
    {
        var rows = new ConcurrentObservableCollection<string>();
        Table<string> table = new(rows, Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 5) };
        CollectionAssert.DoesNotContain(HintKeys(table), "Enter");

        rows.Add("first");

        CollectionAssert.Contains(HintKeys(table), "Enter");
    }

    [TestMethod]
    public void TimePicker_KeyHints_EditKeysOnlyWhenEditable()
    {
        TimePicker picker = new(Theme()) { Value = new TimeOnly(14, 5), Position = new Position(0, 0) };
        CollectionAssert.Contains(HintKeys(picker), "Backspace/Delete");

        picker.IsReadOnly = true;
        var readOnly = HintKeys(picker);
        CollectionAssert.Contains(readOnly, "Left/Right");
        CollectionAssert.DoesNotContain(readOnly, "Up/Down");
        CollectionAssert.DoesNotContain(readOnly, "Backspace/Delete");
    }

    [TestMethod]
    public void RadioGroup_KeyHints_EmptyWithoutOptions()
    {
        RadioGroup<string> group = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(20, 3) };
        Assert.AreEqual(0, group.GetKeyHints().Count());
    }

    [TestMethod]
    public void ToggleButton_KeyHints_EmptyWhenReadOnly()
    {
        ToggleButton toggle = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(10, 1) };
        CollectionAssert.Contains(HintKeys(toggle), "Space/Enter");

        toggle.IsReadOnly = true;
        Assert.AreEqual(0, toggle.GetKeyHints().Count());
    }

    [TestMethod]
    public void LinkLabel_KeyHints_EmptyWithoutText()
    {
        LinkLabel link = new(Theme()) { Text = string.Empty };
        Assert.AreEqual(0, link.GetKeyHints().Count());

        link.Text = "Docs";

        CollectionAssert.Contains(HintKeys(link), "Enter/Space");
    }

    [TestMethod]
    public void Alert_KeyHints_OnlyWhenDismissibleWithText()
    {
        Alert alert = new(Theme()) { Text = "Saved", Position = new Position(0, 0), ViewSize = new Size(20, 1) };
        Assert.AreEqual(0, alert.GetKeyHints().Count(), "a plain alert cannot be dismissed");

        alert.IsDismissible = true;
        CollectionAssert.Contains(HintKeys(alert), "Esc/Enter");

        alert.Text = string.Empty;
        Assert.AreEqual(0, alert.GetKeyHints().Count());
    }
}
