using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>ColorPicker keys and an unknown colour, Timeline edges, and horizontal ScrollBar clicks.</summary>
[TestClass]
public class ColorTimelineScrollInteractionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static KeyEvent Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);

    private static MouseEvent Click(long x, long y = 0) => new() { Position = new Position(x, y), LeftClicked = true };

    // ---- ColorPicker ----

    [TestMethod]
    public async Task ColorPicker_Arrows_MoveTheCursorWithoutPickingAndStopAtTheEdges()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        Color before = picker.SelectedColor;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        int start = picker.CursorIndex;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
        Assert.AreEqual(start + 1 + ColorPicker.GridColumns, picker.CursorIndex);
        Assert.AreEqual(before, picker.SelectedColor);
        for (int i = 0; i < 20; i++)
        {
            await picker.HandleKeyEventAsync(Key(ConsoleKey.DownArrow));
            await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        }

        int last = Enum.GetValues<Color>().Length - 1;
        Assert.AreEqual(last, picker.CursorIndex);
        Assert.IsFalse(await picker.HandleKeyEventAsync(Key(ConsoleKey.A, 'a')));
    }

    [TestMethod]
    public async Task ColorPicker_EnterAndSpace_PickTheCursorColour_ReadOnlyDoesNot()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        int raised = 0;
        picker.SelectedColorChanged += (_, _) => raised++;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Color picked = picker.SelectedColor;
        Assert.AreEqual(Enum.GetValues<Color>()[picker.CursorIndex], picked);
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Spacebar));
        Assert.AreEqual(1, raised);
        picker.IsReadOnly = true;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        Assert.AreEqual(picked, picker.SelectedColor);
    }

    [TestMethod]
    public async Task ColorPicker_UnknownColour_DoesNotBreakTheCursor()
    {
        ColorPicker picker = new(Theme()) { Position = new Position(0, 0) };
        picker.SetFocus();
        picker.SelectedColor = (Color)9999;
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.RightArrow));
        await picker.HandleKeyEventAsync(Key(ConsoleKey.Enter));
        picker.Draw(new MockBuffer(new Size(12, 4)));
        Assert.IsTrue(picker.CursorIndex >= 0);
    }

    [TestMethod]
    public void ColorPicker_Move_StopsAtTheGridEdges()
    {
        Assert.AreEqual(0, ColorPicker.Move(0, -1, -1, 16));
        Assert.AreEqual(3, ColorPicker.Move(3, 1, 0, 16));
        Assert.AreEqual(15, ColorPicker.Move(15, 1, 1, 16));
        Assert.AreEqual(5, ColorPicker.Move(1, 0, 1, 16));
    }

    // ---- Timeline ----

    [TestMethod]
    public void Timeline_RowsNeeded_AndAdd_FollowTheEventCount()
    {
        Timeline timeline = new(Theme());
        Assert.AreEqual(0, timeline.RowsNeeded);
        timeline.Add(new TimelineEvent("10:00", "Start"));
        Assert.AreEqual(1, timeline.RowsNeeded);
        timeline.Add(new TimelineEvent("10:05", "Done", BadgeKind.Success));
        Assert.AreEqual(3, timeline.RowsNeeded);
        timeline.SetEvents([]);
        Assert.AreEqual(0, timeline.Events.Count);
    }

    [TestMethod]
    public void Timeline_Draw_CutsLongTitlesAndHandlesTinyViews()
    {
        Timeline timeline = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(16, 3) };
        timeline.SetEvents([new TimelineEvent("10:00", "A title that is far too long"), new TimelineEvent("10:05", "Next")]);
        MockBuffer buffer = new(new Size(16, 3));
        timeline.Draw(buffer);
        string[] lines = buffer.ContentAsString.Split(Environment.NewLine);
        Assert.IsTrue(lines[0].StartsWith("● 10:00  A titl"));
        Assert.IsTrue(lines[1].StartsWith("│"));
        Assert.AreEqual(0, Timeline.FirstVisibleEvent(0, 5));
        Assert.AreEqual(3, Timeline.FirstVisibleEvent(3, 0));
        timeline.ViewSize = new Size(2, 1);
        timeline.Draw(new MockBuffer(new Size(2, 1)));
    }

    // ---- Horizontal ScrollBar ----

    private static global::iConsole.Controls.ScrollBar.ScrollBar NewHorizontal(ScrollableModel model) => new(Theme())
    {
        ScrollableModel = model,
        ScrollBarOrientation = Orientation.Horizontal,
        Position = new Position(0, 0),
        ViewSize = new Size(10, 1),
    };

    [TestMethod]
    public async Task ScrollBar_Horizontal_ArrowsAndWheelMoveTheColumnCursor()
    {
        ScrollableModel model = new() { ScrollableSize = new Size(50, 1), ViewSize = new Size(10, 1) };
        var bar = NewHorizontal(model);
        await bar.HandleMouseEventAsync(Click(9));
        Assert.AreEqual(1, (int)model.CursorPosition.X);
        await bar.HandleMouseEventAsync(Click(0));
        Assert.AreEqual(0, (int)model.CursorPosition.X);
        await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(4, 0), WheelDelta = -1 });
        Assert.AreEqual(3, (int)model.CursorPosition.X);
        await bar.HandleMouseEventAsync(new MouseEvent { Position = new Position(4, 0), WheelDelta = 1 });
        Assert.AreEqual(0, (int)model.CursorPosition.X);
    }

    [TestMethod]
    public async Task ScrollBar_ClickOutsideOrWithoutModel_IsIgnored()
    {
        ScrollableModel model = new() { ScrollableSize = new Size(50, 1), ViewSize = new Size(10, 1) };
        var bar = NewHorizontal(model);
        Assert.IsFalse(await bar.HandleMouseEventAsync(Click(30)));
        bar.ScrollableModel = null!;
        Assert.IsFalse(await bar.HandleMouseEventAsync(Click(3)));
    }

    [TestMethod]
    public void ScrollBar_CursorForClick_DegenerateSizes()
    {
        Assert.AreEqual(0, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(3, 10, 0, 4));
        Assert.AreEqual(7, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(1, 3, 100, 7));
        Assert.AreEqual(7, global::iConsole.Controls.ScrollBar.ScrollBar.CursorForClick(3, 10, 1, 7));
    }
}
