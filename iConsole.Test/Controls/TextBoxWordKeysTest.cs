using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>The shell and editor keys on Ctrl in a one-line box: word movement, word and line deletion.</summary>
[TestClass]
public class TextBoxWordKeysTest
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
            ViewSize = new Size(30, 1),
            IsReadOnly = readOnly,
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = text;
        box.SetFocus();
        box.ScrollableModel.CursorPosition.X = cursor;
        return box;
    }

    private static Task<bool> Ctrl(TextBox box, ConsoleKey key, bool shift = false) =>
        box.HandleKeyEventAsync(new KeyEvent('\0', key, shift, false, true));

    [TestMethod]
    public void WordStart_SkipsBlanksThenTheWord()
    {
        Assert.AreEqual(6, TextBox.WordStart("alpha beta gamma", 10));
        Assert.AreEqual(6, TextBox.WordStart("alpha beta gamma", 11));
        Assert.AreEqual(0, TextBox.WordStart("alpha beta", 3));
        Assert.AreEqual(3, TextBox.WordStart("   alpha", 5));
        Assert.AreEqual(0, TextBox.WordStart("   alpha", 3), "leading blanks are skipped too");
        Assert.AreEqual(0, TextBox.WordStart("alpha", 0));
        Assert.AreEqual(0, TextBox.WordStart(string.Empty, 4), "a position past the end is clamped");
        Assert.AreEqual(0, TextBox.WordStart("alpha   ", 8));
    }

    [TestMethod]
    public void WordEnd_SkipsTheWordThenBlanks()
    {
        Assert.AreEqual(6, TextBox.WordEnd("alpha beta gamma", 0));
        Assert.AreEqual(6, TextBox.WordEnd("alpha beta gamma", 3));
        Assert.AreEqual(11, TextBox.WordEnd("alpha beta gamma", 6));
        Assert.AreEqual(16, TextBox.WordEnd("alpha beta gamma", 11));
        Assert.AreEqual(5, TextBox.WordEnd("alpha", 5));
        Assert.AreEqual(8, TextBox.WordEnd("alpha   ", 0));
        Assert.AreEqual(0, TextBox.WordEnd(string.Empty, -3));
    }

    [TestMethod]
    public async Task CtrlLeftAndRight_MoveByAWord()
    {
        TextBox box = NewBox("alpha beta gamma", 16);

        Assert.IsTrue(await Ctrl(box, ConsoleKey.LeftArrow));
        Assert.AreEqual(11L, box.ScrollableModel.CursorPosition.X);
        await Ctrl(box, ConsoleKey.LeftArrow);
        Assert.AreEqual(6L, box.ScrollableModel.CursorPosition.X);

        Assert.IsTrue(await Ctrl(box, ConsoleKey.RightArrow));
        Assert.AreEqual(11L, box.ScrollableModel.CursorPosition.X);
        await Ctrl(box, ConsoleKey.RightArrow);
        await Ctrl(box, ConsoleKey.RightArrow);
        Assert.AreEqual(16L, box.ScrollableModel.CursorPosition.X, "it stops at the end");
    }

    [TestMethod]
    public async Task CtrlShiftLeftAndRight_SelectByAWord()
    {
        TextBox box = NewBox("alpha beta gamma", 16);

        await Ctrl(box, ConsoleKey.LeftArrow, shift: true);
        Assert.AreEqual("gamma", box.SelectedText);
        await Ctrl(box, ConsoleKey.LeftArrow, shift: true);
        Assert.AreEqual("beta gamma", box.SelectedText);
        await Ctrl(box, ConsoleKey.RightArrow, shift: true);
        Assert.AreEqual("gamma", box.SelectedText);
    }

    [TestMethod]
    public async Task CtrlLeft_WithASelection_DropsItAndMoves()
    {
        TextBox box = NewBox("alpha beta", 10);
        await Ctrl(box, ConsoleKey.LeftArrow, shift: true);
        Assert.IsTrue(box.HasSelection);

        await Ctrl(box, ConsoleKey.LeftArrow);

        Assert.IsFalse(box.HasSelection);
        Assert.AreEqual(0L, box.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task CtrlW_AndCtrlBackspace_DeleteTheWordBeforeTheCursor()
    {
        TextBox box = NewBox("alpha beta gamma", 16);
        Assert.IsTrue(await Ctrl(box, ConsoleKey.W));
        Assert.AreEqual("alpha beta ", box.Text);
        Assert.AreEqual(11L, box.ScrollableModel.CursorPosition.X);

        await Ctrl(box, ConsoleKey.Backspace);
        Assert.AreEqual("alpha ", box.Text);

        TextBox middle = NewBox("alpha beta gamma", 9);
        await Ctrl(middle, ConsoleKey.W);
        Assert.AreEqual("alpha a gamma", middle.Text, "only the part of the word before the cursor goes");
        Assert.AreEqual(6L, middle.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task CtrlDelete_DeletesTheWordAfterTheCursor()
    {
        TextBox box = NewBox("alpha beta gamma", 0);

        Assert.IsTrue(await Ctrl(box, ConsoleKey.Delete));

        Assert.AreEqual("beta gamma", box.Text);
        Assert.AreEqual(0L, box.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task CtrlU_DeletesEverythingBeforeTheCursor()
    {
        TextBox u = NewBox("alpha beta gamma", 11);
        await Ctrl(u, ConsoleKey.U);
        Assert.AreEqual("gamma", u.Text);
        Assert.AreEqual(0L, u.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task TheDeleteKeys_WithASelection_RemoveTheSelectionOnly()
    {
        TextBox box = NewBox("alpha beta gamma", 16);
        await Ctrl(box, ConsoleKey.LeftArrow, shift: true);
        Assert.AreEqual("gamma", box.SelectedText);

        await Ctrl(box, ConsoleKey.W);

        Assert.AreEqual("alpha beta ", box.Text);
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public async Task TheDeleteKeys_AtTheEdges_AreHandledAndChangeNothing()
    {
        TextBox box = NewBox("alpha", 0);
        Assert.IsTrue(await Ctrl(box, ConsoleKey.W), "nothing before the cursor, but the key is the box's");
        Assert.IsTrue(await Ctrl(box, ConsoleKey.U));
        Assert.AreEqual("alpha", box.Text);

        box.ScrollableModel.CursorPosition.X = 5;
        Assert.IsTrue(await Ctrl(box, ConsoleKey.Delete));
        Assert.AreEqual("alpha", box.Text);
    }

    [TestMethod]
    public async Task ReadOnly_MovesByWordButDeletesNothing()
    {
        TextBox box = NewBox("alpha beta", 10, readOnly: true);

        Assert.IsTrue(await Ctrl(box, ConsoleKey.LeftArrow));
        Assert.AreEqual(6L, box.ScrollableModel.CursorPosition.X);
        await Ctrl(box, ConsoleKey.W);
        await Ctrl(box, ConsoleKey.U);

        Assert.AreEqual("alpha beta", box.Text);
    }

    [TestMethod]
    public async Task Disabled_IgnoresTheKeys()
    {
        TextBox box = NewBox("alpha beta", 10);
        box.IsEnabled = false;

        await Ctrl(box, ConsoleKey.W);
        await Ctrl(box, ConsoleKey.LeftArrow);

        Assert.AreEqual("alpha beta", box.Text);
        Assert.AreEqual(10L, box.ScrollableModel.CursorPosition.X);
    }

    [TestMethod]
    public async Task ShiftedDeleteKeys_AreNotWordKeys()
    {
        TextBox box = NewBox("alpha beta", 10);

        await Ctrl(box, ConsoleKey.W, shift: true);

        Assert.AreEqual("alpha beta", box.Text);
    }
}
