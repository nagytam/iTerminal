using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using Moq;
using iConsole.Controls.Editors;
using iConsole.Services;
using iConsole.Data;

namespace iConsole.Commands.Test;

[TestClass]
public class TypeCommandTest
{
    [TestMethod]
    public async Task HandleKeyEventAsync_ShouldInsertCharacter()
    {
        var theme = new Mock<IThemeService>();
        theme.SetupGet(t => t.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        var editor = new TextEditorControl(theme.Object);
        var lines = new iConsole.Data.ConcurrentList<string>();
        lines.Add(string.Empty);
        editor.TextModel.Lines = lines;
        editor.ScrollableModel.CursorPosition = new Position(0, 0);

        var cmd = new TypeCommand();
        cmd.Control = editor;

        var keyEvent = new KeyEvent('x', ConsoleKey.X, false, false, false);
        var handled = await cmd.HandleKeyEventAsync(keyEvent);

        Assert.IsTrue(handled);
        Assert.AreEqual("x", editor.TextModel.Lines[0]);
        Assert.IsTrue(editor.IsDirty);
    }
}

