using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Inputs;
using iConsole.Services;
using iConsole.Test.Core;
using Moq;

namespace iConsole.Test.Controls;

/// <summary>Selecting with the mouse: a press in the text, a drag, a release — and how the terminal reports one.</summary>
[TestClass]
public class MouseSelectionTest
{
    private static IThemeService Theme()
    {
        Mock<IThemeService> m = new();
        m.Setup(x => x.CurrentColorScheme).Returns(new ColorScheme());
        return m.Object;
    }

    private static MouseEvent Press(long x, long y) => new() { Position = new Position(x, y), LeftClicked = true };

    private static MouseEvent Drag(long x, long y) => new() { Position = new Position(x, y), IsDrag = true };

    private static MouseEvent Release(long x, long y) => new() { Position = new Position(x, y), IsRelease = true };

    // ---- How a terminal reports it ----

    private static List<MouseEvent> Decode(string input)
    {
        List<MouseEvent> mice = [];
        TerminalInputUtf8Decoder decoder = new();
        decoder.Append(System.Text.Encoding.UTF8.GetBytes(input));
        decoder.Drain(_ => { }, mice.Add);
        return mice;
    }

    [TestMethod]
    public void Decoder_MotionWithTheLeftButtonHeld_IsADrag()
    {
        MouseEvent drag = Decode("\x1b[<32;12;5M").Single();
        Assert.IsTrue(drag.IsDrag);
        Assert.IsFalse(drag.LeftClicked);
        Assert.IsFalse(drag.IsRelease);
        Assert.AreEqual(11L, drag.Position.X);
        Assert.AreEqual(4L, drag.Position.Y);
    }

    [TestMethod]
    public void Decoder_OtherMotionAndPresses_AreNotDrags()
    {
        Assert.IsFalse(Decode("\x1b[<34;1;1M").Single().IsDrag, "right button held");
        Assert.IsFalse(Decode("\x1b[<35;1;1M").Single().IsDrag, "no button held");
        Assert.IsFalse(Decode("\x1b[<0;1;1M").Single().IsDrag, "a press");
        Assert.IsFalse(Decode("\x1b[<0;1;1m").Single().IsDrag, "a release");
        Assert.IsFalse(Decode("\x1b[<64;1;1M").Single().IsDrag, "the wheel");
    }

    // ---- TextBox ----

    private static TextBox NewBox(string text)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextBox box = new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), Theme())
        {
            Position = new Position(0, 0),
            ViewSize = new Size(20, 1),
        };
        root.AddControl(box);
        box.OnInitializeLayout();
        box.Text = text;
        box.SetFocus();
        return box;
    }

    /// <summary>With no label the well's first character is one cell in.</summary>
    private static long ColumnOf(int character) => character + 1;

    [TestMethod]
    public async Task TextBox_DraggingSelectsFromThePressToThePointer()
    {
        TextBox box = NewBox("hello world");
        await box.HandleMouseEventAsync(Press(ColumnOf(2), 0));
        Assert.IsFalse(box.HasSelection, "a press alone selects nothing");
        await box.HandleMouseEventAsync(Drag(ColumnOf(6), 0));
        Assert.AreEqual("llo ", box.SelectedText);
        Assert.AreEqual(6L, box.ScrollableModel.CursorPosition.X);
        await box.HandleMouseEventAsync(Release(ColumnOf(6), 0));
        Assert.AreEqual("llo ", box.SelectedText, "the selection stays after the release");
    }

    [TestMethod]
    public async Task TextBox_DraggingBackwards_AndBackToThePress()
    {
        TextBox box = NewBox("hello world");
        await box.HandleMouseEventAsync(Press(ColumnOf(6), 0));
        await box.HandleMouseEventAsync(Drag(ColumnOf(2), 0));
        Assert.AreEqual("llo ", box.SelectedText);
        await box.HandleMouseEventAsync(Drag(ColumnOf(6), 0));
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public async Task TextBox_DraggingPastTheEndOfTheText_SelectsToTheEnd()
    {
        TextBox box = NewBox("hello");
        await box.HandleMouseEventAsync(Press(ColumnOf(1), 0));
        await box.HandleMouseEventAsync(Drag(ColumnOf(9), 0));
        Assert.AreEqual("ello", box.SelectedText);
    }

    [TestMethod]
    public async Task TextBox_DraggingPastTheEdgesOfTheWell_StepsAndScrolls()
    {
        TextBox box = NewBox(new string('x', 60));
        await box.HandleMouseEventAsync(Press(ColumnOf(0), 0));
        for (int i = 0; i < 25; i++)
        {
            await box.HandleMouseEventAsync(Drag(40, 0));
        }

        Assert.AreEqual(25, box.SelectedText.Length);
        long cursor = box.ScrollableModel.CursorPosition.X;
        long scroll = box.ScrollableModel.ScrollPosition.X;
        Assert.IsTrue(cursor >= scroll && cursor - scroll <= box.ScrollableModel.ViewSize.Width, $"cursor {cursor}, scroll {scroll}");

        for (int i = 0; i < 10; i++)
        {
            await box.HandleMouseEventAsync(Drag(0, 0));
        }

        Assert.AreEqual(15, box.SelectedText.Length);
    }

    [TestMethod]
    public async Task TextBox_ADragWithoutAPressInTheWell_DoesNothing_AndNeitherDoesOneAfterTheRelease()
    {
        TextBox box = NewBox("hello world");
        Assert.IsFalse(await box.HandleMouseEventAsync(Drag(ColumnOf(5), 0)));
        Assert.IsFalse(box.HasSelection);

        await box.HandleMouseEventAsync(Press(ColumnOf(1), 0));
        await box.HandleMouseEventAsync(Release(ColumnOf(1), 0));
        await box.HandleMouseEventAsync(Drag(ColumnOf(5), 0));
        Assert.IsFalse(box.HasSelection);
    }

    [TestMethod]
    public async Task TextBox_ANewPress_StartsOver()
    {
        TextBox box = NewBox("hello world");
        await box.HandleMouseEventAsync(Press(ColumnOf(0), 0));
        await box.HandleMouseEventAsync(Drag(ColumnOf(4), 0));
        await box.HandleMouseEventAsync(Press(ColumnOf(6), 0));
        Assert.IsFalse(box.HasSelection);
        await box.HandleMouseEventAsync(Drag(ColumnOf(9), 0));
        Assert.AreEqual("wor", box.SelectedText);
    }

    [TestMethod]
    public async Task TextBox_DisabledIgnoresTheDrag()
    {
        TextBox box = NewBox("hello world");
        await box.HandleMouseEventAsync(Press(ColumnOf(1), 0));
        box.IsEnabled = false;
        await box.HandleMouseEventAsync(Drag(ColumnOf(6), 0));
        Assert.IsFalse(box.HasSelection);
    }

    // ---- Editor ----

    private static readonly string[] Poem = ["roses are red", "violets are blue", "sugar is sweet", "and so are you"];

    private static TextEditorControl NewEditor(uint height = 6)
    {
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, height) };
        root.AddControl(editor);
        foreach (string line in Poem)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.OnInitializeLayout();
        editor.Refresh();
        editor.SetFocus();
        return editor;
    }

    private static MouseEvent At(TextEditorControl editor, long column, long row, bool drag = false, bool press = false)
        => new()
        {
            Position = new Position(editor.AbsolutePosition.X + column, editor.AbsolutePosition.Y + row),
            IsDrag = drag,
            LeftClicked = press,
        };

    [TestMethod]
    public async Task Editor_DraggingSelectsAcrossLines()
    {
        TextEditorControl editor = NewEditor();
        await editor.HandleMouseEventAsync(At(editor, 6, 0, press: true));
        Assert.IsFalse(editor.HasSelection);
        await editor.HandleMouseEventAsync(At(editor, 4, 2, drag: true));
        Assert.AreEqual("are red\nviolets are blue\nsuga", editor.SelectedText);
        await editor.HandleMouseEventAsync(new MouseEvent { Position = At(editor, 4, 2).Position, IsRelease = true });
        Assert.IsTrue(editor.HasSelection);
    }

    [TestMethod]
    public async Task Editor_DraggingUpwardsAndBackToThePress()
    {
        TextEditorControl editor = NewEditor();
        await editor.HandleMouseEventAsync(At(editor, 5, 2, press: true));
        await editor.HandleMouseEventAsync(At(editor, 2, 0, drag: true));
        Assert.AreEqual("ses are red\nviolets are blue\nsugar", editor.SelectedText);
        await editor.HandleMouseEventAsync(At(editor, 5, 2, drag: true));
        Assert.IsFalse(editor.HasSelection);
    }

    [TestMethod]
    public async Task Editor_DraggingPastTheEndOfALineOrTheText_SelectsToTheEnd()
    {
        TextEditorControl editor = NewEditor();
        await editor.HandleMouseEventAsync(At(editor, 3, 0, press: true));
        await editor.HandleMouseEventAsync(At(editor, 20, 0, drag: true));
        Assert.AreEqual("es are red", editor.SelectedText);
    }

    [TestMethod]
    public async Task Editor_DraggingBelowTheView_StepsDownAndScrolls()
    {
        string[] many = Enumerable.Range(0, 30).Select(i => "line " + i).ToArray();
        Window root = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(80, 25) };
        TextEditorControl editor = new(Theme()) { Position = new Position(0, 0), ViewSize = new Size(30, 6) };
        root.AddControl(editor);
        foreach (string line in many)
        {
            editor.TextModel.Lines.Add(line);
        }

        editor.OnInitializeLayout();
        editor.Refresh();
        editor.SetFocus();
        await editor.HandleMouseEventAsync(At(editor, 0, 0, press: true));
        for (int i = 0; i < 12; i++)
        {
            await editor.HandleMouseEventAsync(At(editor, 2, 20, drag: true));
        }

        long cursor = editor.ScrollableModel.CursorPosition.Y;
        long scroll = editor.ScrollableModel.ScrollPosition.Y;
        Assert.AreEqual(12L, cursor);
        Assert.IsTrue(cursor >= scroll && cursor < scroll + editor.ScrollableModel.ViewSize.Height, $"cursor {cursor}, scroll {scroll}");
        Assert.AreEqual(12, editor.SelectedText.Count(c => c == '\n'));
    }

    [TestMethod]
    public async Task Editor_AFreshPressEndsTheOldSelection_AndADragWithoutAPressDoesNothing()
    {
        TextEditorControl editor = NewEditor();
        Assert.IsFalse(await editor.HandleMouseEventAsync(At(editor, 5, 1, drag: true)));
        Assert.IsFalse(editor.HasSelection);

        await editor.HandleMouseEventAsync(At(editor, 0, 0, press: true));
        await editor.HandleMouseEventAsync(At(editor, 5, 1, drag: true));
        Assert.IsTrue(editor.HasSelection);
        await editor.HandleMouseEventAsync(At(editor, 2, 3, press: true));
        Assert.IsFalse(editor.HasSelection);
    }

    [TestMethod]
    public async Task Editor_TypingAfterADragReplacesTheSelection()
    {
        TextEditorControl editor = NewEditor();
        await editor.HandleMouseEventAsync(At(editor, 0, 0, press: true));
        await editor.HandleMouseEventAsync(At(editor, 5, 0, drag: true));
        await editor.HandleKeyEventAsync(new KeyEvent('R', ConsoleKey.R, true, false, false));
        Assert.AreEqual("R are red", editor.TextModel.Lines[0]);
    }
}
