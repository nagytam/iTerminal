using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Controls.Editors;
using iConsole.Model;
using iConsole.Data;
using iConsole.Services;
using iConsole.Display;
using Moq;

namespace iConsole.Commands.Test;

[TestClass]
public class BackspaceCommandTest
{
    private static BackspaceCommand NewCommand(TextModel textModel, long x, long y, out ScrollableModel scrollable)
    {
        scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(x, y);
        return new BackspaceCommand(textModel, scrollable);
    }

    [TestMethod]
    public async Task ExecuteAsync_ShouldDeleteCharacterBeforeCursor()
    {
        var textModel = new TextModel("hello");
        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(5, 0);
        var cmd = new BackspaceCommand(textModel, scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("hell", textModel.Lines[0]);
        Assert.AreEqual(4, scrollable.CursorPosition.X);
    }

    [TestMethod]
    public async Task ExecuteAsync_InTheMiddleOfALine_RemovesOnlyThePrecedingCharacter()
    {
        var textModel = new TextModel("hello");
        var cmd = NewCommand(textModel, 2, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("hllo", textModel.Lines[0]);
        Assert.AreEqual(1, scrollable.CursorPosition.X);
    }

    [TestMethod]
    public async Task ExecuteAsync_AtTheStartOfALine_JoinsItOntoTheLineAbove()
    {
        var textModel = new TextModel("ab");
        textModel.Lines.Add("cd");
        var cmd = NewCommand(textModel, 0, 1, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual(1, textModel.Lines.Count);
        Assert.AreEqual("abcd", textModel.Lines[0]);
        Assert.AreEqual(2, scrollable.CursorPosition.X);
        Assert.AreEqual(0, scrollable.CursorPosition.Y);
    }

    [TestMethod]
    public async Task ExecuteAsync_AtTheStartOfTheDocument_ChangesNothing()
    {
        var textModel = new TextModel("ab");
        var cmd = NewCommand(textModel, 0, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("ab", textModel.Lines[0]);
        Assert.AreEqual(0, scrollable.CursorPosition.X);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithACursorPastTheEndOfTheLine_RemovesTheLastCharacter()
    {
        var textModel = new TextModel("abc");
        var cmd = NewCommand(textModel, 9, 0, out var scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("ab", textModel.Lines[0]);
        Assert.AreEqual(2, scrollable.CursorPosition.X);
    }

    [TestMethod]
    public async Task ExecuteAsync_OnAnEmptyDocument_DoesNothing()
    {
        var textModel = new TextModel();
        textModel.Lines.Clear();
        var cmd = NewCommand(textModel, 0, 0, out _);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual(0, textModel.Lines.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_MarksTheEditorDirty_OnlyWhenSomethingChanged()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var editor = new TextEditorControl(theme.Object);
        editor.TextModel.Lines.Add("ab");
        var cmd = new BackspaceCommand(editor.TextModel, editor.ScrollableModel) { Control = editor };

        editor.ScrollableModel.CursorPosition.X = 0;
        await cmd.ExecuteAsync(null);
        Assert.IsFalse(editor.IsDirty);

        editor.ScrollableModel.CursorPosition.X = 2;
        await cmd.ExecuteAsync(null);
        Assert.IsTrue(editor.IsDirty);
        Assert.AreEqual("a", editor.TextModel.Lines[0]);
    }

    [TestMethod]
    public void CanExecute_IsFalseInAReadOnlyEditor()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var editor = new TextEditorControl(theme.Object) { IsReadOnly = true };
        var cmd = new BackspaceCommand(editor.TextModel, editor.ScrollableModel) { Control = editor };

        Assert.IsFalse(cmd.CanExecute());
        editor.IsReadOnly = false;
        Assert.IsTrue(cmd.CanExecute());
    }

    [TestMethod]
    public async Task TheEditor_RoutesBackspaceToThisCommand_AndTypeCommandLeavesItAlone()
    {
        var theme = new Mock<IThemeService>();
        theme.Setup(t => t.CurrentColorScheme).Returns(new ColorScheme());
        var editor = new TextEditorControl(theme.Object);
        editor.TextModel.Lines.Add("abc");
        editor.ScrollableModel.CursorPosition.X = 3;

        var typeCommand = new TypeCommand { Control = editor };
        var backspace = new KeyEvent('\0', ConsoleKey.Backspace, false, false, false);
        Assert.IsFalse(await typeCommand.HandleKeyEventAsync(backspace));
        Assert.AreEqual("abc", editor.TextModel.Lines[0]);

        Assert.IsTrue(await editor.HandleKeyEventAsync(backspace));
        Assert.AreEqual("ab", editor.TextModel.Lines[0]);
        Assert.IsTrue(editor.Commands.AsSnapshot().OfType<BackspaceCommand>().Any());
    }
}
