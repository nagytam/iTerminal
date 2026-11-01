using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using Moq;

namespace iConsole.Commands.Test;

[TestClass]
public class DeleteCharacterCommandTest
{
    private static DeleteCharacterCommand NewCommand(TextModel textModel, long x, long y, out ScrollableModel scrollable)
    {
        scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(x, y);
        return new DeleteCharacterCommand(textModel, scrollable);
    }

    private static TextEditorControl NewEditor()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        return new TextEditorControl(theme.Object);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_ShouldRemoveTheCharacterUnderTheCursor_AndLeaveTheCursorWhereItIs()
    {
        var textModel = new TextModel("abcd");
        var cmd = NewCommand(textModel, 1, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("acd", textModel.Lines[0]);
        Assert.AreEqual(1, scrollable.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_AtTheStartOfALine_RemovesItsFirstCharacter()
    {
        var textModel = new TextModel("abcd");
        var cmd = NewCommand(textModel, 0, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("bcd", textModel.Lines[0]);
        Assert.AreEqual(0, scrollable.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_AtTheEndOfALine_PullsTheNextLineUp()
    {
        var textModel = new TextModel("ab");
        textModel.Lines.Add("cd");
        textModel.Lines.Add("ef");
        var cmd = NewCommand(textModel, 2, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { "abcd", "ef" }, textModel.Lines.AsSnapshot().ToArray());
        Assert.AreEqual(2, scrollable.CursorPosition.X);
        Assert.AreEqual(0, scrollable.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_AtTheEndOfTheDocument_ChangesNothing()
    {
        var textModel = new TextModel("ab");
        var cmd = NewCommand(textModel, 2, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("ab", textModel.Lines[0]);
        Assert.AreEqual(2, scrollable.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithACursorPastTheEndOfTheLine_JoinsTheNextLine()
    {
        var textModel = new TextModel("ab");
        textModel.Lines.Add("cd");
        var cmd = NewCommand(textModel, 9, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("abcd", textModel.Lines[0]);
        Assert.AreEqual(2, scrollable.CursorPosition.X);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_OnAnEmptyDocument_DoesNothing()
    {
        var textModel = new TextModel();
        textModel.Lines.Clear();
        var cmd = NewCommand(textModel, 0, 0, out _);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_MarksTheEditorDirty_OnlyWhenSomethingChanged()
    {
        var editor = NewEditor();
        editor.TextModel.Lines.Add("ab");
        var cmd = new DeleteCharacterCommand(editor.TextModel, editor.ScrollableModel) { Control = editor };

        editor.ScrollableModel.CursorPosition.X = 2;
        await cmd.ExecuteAsync(null);
        Assert.IsFalse(editor.IsDirty);

        editor.ScrollableModel.CursorPosition.X = 0;
        await cmd.ExecuteAsync(null);
        Assert.IsTrue(editor.IsDirty);
        Assert.AreEqual("b", editor.TextModel.Lines[0]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_IsFalseInAReadOnlyEditor()
    {
        var editor = NewEditor();
        editor.IsReadOnly = true;
        var cmd = new DeleteCharacterCommand(editor.TextModel, editor.ScrollableModel) { Control = editor };

        Assert.IsFalse(cmd.CanExecute());
        editor.IsReadOnly = false;
        Assert.IsTrue(cmd.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheEditor_RoutesDeleteToThisCommand_AndTypeCommandLeavesItAlone()
    {
        var editor = NewEditor();
        editor.TextModel.Lines.Add("abc");
        editor.ScrollableModel.CursorPosition.X = 1;
        var delete = new KeyEvent('\0', ConsoleKey.Delete, false, false, false);

        var typeCommand = new TypeCommand { Control = editor };
        Assert.IsFalse(await typeCommand.HandleKeyEventAsync(delete));
        Assert.AreEqual("abc", editor.TextModel.Lines[0]);

        Assert.IsTrue(await editor.HandleKeyEventAsync(delete));
        Assert.AreEqual("ac", editor.TextModel.Lines[0]);
    }
}
