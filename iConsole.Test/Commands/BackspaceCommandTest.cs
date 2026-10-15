using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Model;
using iConsole.Data;

namespace iConsole.Commands.Test;

[TestClass]
public class BackspaceCommandTest
{
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
}

