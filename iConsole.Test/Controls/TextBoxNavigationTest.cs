using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Editing and moving the caret in a one-line text box, especially in a value wider than its well.</summary>
[TestClass]
public class TextBoxNavigationTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    /// <summary>A box 14 wide, so its well shows 11 characters.</summary>
    private static TextBox NewBox(string text, long cursor)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(14, 1),
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = text;
        box.SetFocus();
        box.ScrollableModel.CursorPosition.X = cursor;
        return box;
    }

    private static async Task Press(TextBox box, ConsoleKey key, char c = '\0', int times = 1, bool control = false)
    {
        for (int i = 0; i < times; i++)
        {
            await box.HandleKeyEventAsync(new KeyEvent(c, key, false, false, control));
        }
    }

    private static string Drawn(TextBox box)
    {
        MockBuffer buffer = new(new Size(80, 1));
        box.Draw(buffer);
        return buffer.ContentAsString.Split(Environment.NewLine)[0].TrimEnd();
    }

    private static void AssertCaretInWell(TextBox box)
    {
        long cursor = box.ScrollableModel.CursorPosition.X;
        long scroll = box.ScrollableModel.ScrollPosition.X;
        Assert.IsTrue(cursor >= scroll && cursor - scroll <= box.ScrollableModel.ViewSize.Width, $"cursor {cursor}, scroll {scroll}, well {box.ScrollableModel.ViewSize.Width}");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Right_AcrossALongValue_KeepsMovingAndScrolls()
    {
        TextBox box = NewBox(new string('x', 40), 0);
        await Press(box, ConsoleKey.RightArrow, times: 25);
        Assert.AreEqual(25L, box.ScrollableModel.CursorPosition.X);
        AssertCaretInWell(box);
        Assert.IsTrue(Drawn(box).StartsWith("<"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Right_AtTheEnd_StaysAndLeft_AtTheStart_Stays()
    {
        TextBox end = NewBox("hello", 5);
        await Press(end, ConsoleKey.RightArrow);
        Assert.AreEqual(5L, end.ScrollableModel.CursorPosition.X);
        TextBox start = NewBox("hello", 0);
        await Press(start, ConsoleKey.LeftArrow);
        Assert.AreEqual(0L, start.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task End_ScrollsToTheEndOfALongValue_AndHomeBack()
    {
        TextBox box = NewBox(new string('x', 40), 0);
        await Press(box, ConsoleKey.End);
        Assert.AreEqual(40L, box.ScrollableModel.CursorPosition.X);
        AssertCaretInWell(box);
        await Press(box, ConsoleKey.Home);
        Assert.AreEqual(0L, box.ScrollableModel.CursorPosition.X);
        Assert.AreEqual(0L, box.ScrollableModel.ScrollPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Left_FromTheEndOfALongValue_ScrollsBackWithTheCaret()
    {
        TextBox box = NewBox(new string('x', 40), 0);
        await Press(box, ConsoleKey.End);
        await Press(box, ConsoleKey.LeftArrow, times: 35);
        Assert.AreEqual(5L, box.ScrollableModel.CursorPosition.X);
        AssertCaretInWell(box);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_PastTheWell_KeepsTheCaretInsideIt()
    {
        TextBox box = NewBox(string.Empty, 0);
        await Press(box, ConsoleKey.A, 'a', times: 30);
        Assert.AreEqual(30, box.TextLength);
        Assert.AreEqual(30L, box.ScrollableModel.CursorPosition.X);
        AssertCaretInWell(box);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_ShorteningAScrolledValue_PullsTheViewBack()
    {
        TextBox box = NewBox(new string('x', 40), 0);
        await Press(box, ConsoleKey.End);
        await Press(box, ConsoleKey.Backspace, times: 30);
        Assert.AreEqual(10, box.TextLength);
        Assert.AreEqual(0L, box.ScrollableModel.ScrollPosition.X);
        Assert.IsTrue(Drawn(box).Contains("xxxxxxxxxx"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ShorteningTheValueFromOutside_BringsTheCaretBackOntoIt()
    {
        TextBox box = NewBox(new string('x', 40), 40);
        box.Text = "ab";
        Drawn(box);
        Assert.AreEqual(2L, box.ScrollableModel.CursorPosition.X);
        Assert.AreEqual(0L, box.ScrollableModel.ScrollPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Backspace_InTheMiddle_RemovesOneCharacter()
    {
        TextBox box = NewBox("hello", 3);
        await Press(box, ConsoleKey.Backspace);
        Assert.AreEqual("helo", box.Text);
        Assert.AreEqual(2L, box.ScrollableModel.CursorPosition.X);
        TextBox start = NewBox("hello", 0);
        await Press(start, ConsoleKey.Backspace);
        Assert.AreEqual("hello", start.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Delete_RemovesTheCharacterUnderTheCaret_AndNothingAtTheEnd()
    {
        TextBox box = NewBox("hello", 1);
        await Press(box, ConsoleKey.Delete);
        Assert.AreEqual("hllo", box.Text);
        Assert.AreEqual(1L, box.ScrollableModel.CursorPosition.X);
        TextBox end = NewBox("hello", 5);
        await Press(end, ConsoleKey.Delete);
        Assert.AreEqual("hello", end.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Typing_InTheMiddle_InsertsAtTheCaret()
    {
        TextBox box = NewBox("hllo", 1);
        await Press(box, ConsoleKey.E, 'e');
        Assert.AreEqual("hello", box.Text);
        Assert.AreEqual(2L, box.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlLeftAndCtrlRight_MoveByWord()
    {
        TextBox box = NewBox("hello world", 11);
        await Press(box, ConsoleKey.LeftArrow, control: true);
        Assert.AreEqual(6L, box.ScrollableModel.CursorPosition.X);
        TextBox other = NewBox("hello world", 0);
        await Press(other, ConsoleKey.RightArrow, control: true);
        Assert.AreEqual(6L, other.ScrollableModel.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlK_ClearsTheValue()
    {
        TextBox box = NewBox("hello", 3);
        await Press(box, ConsoleKey.K, control: true);
        Assert.AreEqual(string.Empty, box.Text);
        Assert.AreEqual(0L, box.ScrollableModel.CursorPosition.X);
    }
}
