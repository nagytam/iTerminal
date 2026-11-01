using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Selecting text in a one-line box with Shift, and what typing and deleting do to a selection.</summary>
[TestClass]
public class TextBoxSelectionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static TextBox NewBox(string text, long cursor, bool readOnly = false)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1),
            IsReadOnly = readOnly,
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = text;
        box.SetFocus();
        box.ScrollableModel.CursorPosition.X = cursor;
        return box;
    }

    private static async Task Press(TextBox box, ConsoleKey key, char c = '\0', bool shift = false, int times = 1)
    {
        for (int i = 0; i < times; i++)
        {
            await box.HandleKeyEventAsync(new KeyEvent(c, key, shift, false, false));
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftRight_SelectsCharactersFromTheCursor()
    {
        TextBox box = NewBox("hello world", 0);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 5);
        Assert.IsTrue(box.HasSelection);
        Assert.AreEqual("hello", box.SelectedText);
        Assert.AreEqual(5L, box.ScrollableModel.CursorPosition.X);
        Assert.IsTrue(box.SelectionTextModel.IsSelected);
        Assert.AreEqual(0L, box.SelectionTextModel.Start!.X);
        Assert.AreEqual(4L, box.SelectionTextModel.End!.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftLeft_SelectsBackwards_AndShrinksWhenReversed()
    {
        TextBox box = NewBox("hello world", 11);
        await Press(box, ConsoleKey.LeftArrow, shift: true, times: 5);
        Assert.AreEqual("world", box.SelectedText);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 2);
        Assert.AreEqual("rld", box.SelectedText);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 3);
        Assert.IsFalse(box.HasSelection);
        Assert.IsFalse(box.SelectionTextModel.IsSelected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftHomeAndShiftEnd_SelectToTheEnds()
    {
        TextBox box = NewBox("hello world", 6);
        await Press(box, ConsoleKey.Home, shift: true);
        Assert.AreEqual("hello ", box.SelectedText);
        TextBox end = NewBox("hello world", 6);
        await Press(end, ConsoleKey.End, shift: true);
        Assert.AreEqual("world", end.SelectedText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftArrows_StopAtTheEndsOfTheValue()
    {
        TextBox box = NewBox("abc", 3);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 3);
        Assert.IsFalse(box.HasSelection);
        await Press(box, ConsoleKey.LeftArrow, shift: true, times: 9);
        Assert.AreEqual("abc", box.SelectedText);
        Assert.AreEqual(0L, box.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Left_WithASelection_CollapsesToItsStart_AndRightToItsEnd()
    {
        TextBox left = NewBox("hello world", 0);
        await Press(left, ConsoleKey.RightArrow, shift: true, times: 5);
        await Press(left, ConsoleKey.LeftArrow);
        Assert.IsFalse(left.HasSelection);
        Assert.AreEqual(0L, left.ScrollableModel.CursorPosition.X);

        TextBox right = NewBox("hello world", 0);
        await Press(right, ConsoleKey.RightArrow, shift: true, times: 5);
        await Press(right, ConsoleKey.LeftArrow, shift: true, times: 2);
        await Press(right, ConsoleKey.RightArrow);
        Assert.IsFalse(right.HasSelection);
        Assert.AreEqual(3L, right.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OtherMovement_EndsTheSelection()
    {
        foreach (ConsoleKey key in new[] { ConsoleKey.Home, ConsoleKey.End })
        {
            TextBox box = NewBox("hello world", 3);
            await Press(box, ConsoleKey.RightArrow, shift: true, times: 3);
            await Press(box, key);
            Assert.IsFalse(box.HasSelection, key.ToString());
            Assert.IsFalse(box.SelectionTextModel.IsSelected, key.ToString());
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_ReplacesTheSelection()
    {
        TextBox box = NewBox("hello world", 0);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 5);
        await Press(box, ConsoleKey.J, 'J');
        Assert.AreEqual("J world", box.Text);
        Assert.AreEqual(1L, box.ScrollableModel.CursorPosition.X);
        Assert.IsFalse(box.HasSelection);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BackspaceAndDelete_RemoveTheSelectionAndNothingElse()
    {
        TextBox backspace = NewBox("hello world", 11);
        await Press(backspace, ConsoleKey.LeftArrow, shift: true, times: 5);
        await Press(backspace, ConsoleKey.Backspace);
        Assert.AreEqual("hello ", backspace.Text);
        Assert.AreEqual(6L, backspace.ScrollableModel.CursorPosition.X);

        TextBox delete = NewBox("hello world", 2);
        await Press(delete, ConsoleKey.RightArrow, shift: true, times: 3);
        await Press(delete, ConsoleKey.Delete);
        Assert.AreEqual("he world", delete.Text);
        Assert.AreEqual(2L, delete.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ReadOnlyBox_CanSelectButNotChange()
    {
        TextBox box = NewBox("hello", 0, readOnly: true);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 3);
        Assert.AreEqual("hel", box.SelectedText);
        await Press(box, ConsoleKey.Backspace);
        await Press(box, ConsoleKey.X, 'x');
        Assert.AreEqual("hello", box.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ClickingOrSettingTheText_ClearsTheSelection()
    {
        TextBox clicked = NewBox("hello world", 0);
        await Press(clicked, ConsoleKey.RightArrow, shift: true, times: 3);
        await clicked.HandleMouseEventAsync(new MouseEvent { Position = new Position(4, 0), LeftClicked = true });
        Assert.IsFalse(clicked.HasSelection);

        TextBox set = NewBox("hello world", 0);
        await Press(set, ConsoleKey.RightArrow, shift: true, times: 3);
        set.Text = "other";
        Assert.IsFalse(set.HasSelection);
        Assert.AreEqual(string.Empty, set.SelectedText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Selection_IsDrawnHighlighted()
    {
        TextBox box = NewBox("hello world", 0);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 5);
        ScreenBuffer buffer = new(20, 1);
        box.Draw(buffer);
        ColorScheme scheme = new();
        // The well starts one cell in; the first five characters carry the selection colours.
        Assert.AreEqual(scheme.SelectedFocusedBackground, buffer.Cell(1, 0).BackgroundColor);
        Assert.AreNotEqual(scheme.SelectedFocusedBackground, buffer.Cell(8, 0).BackgroundColor);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Selecting_InALongValue_ScrollsTheViewWithTheCursor()
    {
        TextBox box = NewBox(new string('x', 60), 0);
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 40);
        Assert.AreEqual(40, box.SelectedText.Length);
        long cursor = box.ScrollableModel.CursorPosition.X;
        long scroll = box.ScrollableModel.ScrollPosition.X;
        Assert.IsTrue(cursor >= scroll && cursor - scroll <= box.ScrollableModel.ViewSize.Width, $"cursor {cursor} scroll {scroll}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PasswordBox_DeletesASelectionToo()
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        PasswordBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1),
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = "secret";
        box.SetFocus();
        box.ScrollableModel.CursorPosition.X = 0;
        await Press(box, ConsoleKey.RightArrow, shift: true, times: 3);
        await Press(box, ConsoleKey.Backspace);
        Assert.AreEqual("ret", box.Text);
    }
}
