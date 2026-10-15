using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Model;
using iConsole.Data;

namespace iConsole.Commands.Test;

[TestClass]
public class DeleteCharacterCommandTest
{
    [TestMethod]
    public async Task ExecuteAsync_ShouldRemoveCharacterAtCursor()
    {
        var textModel = new TextModel("abcd");
        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(1, 0);
        var cmd = new DeleteCharacterCommand(textModel, scrollable);

        await cmd.ExecuteAsync(null);

        Assert.AreEqual("bcd", textModel.Lines[0]);
        Assert.AreEqual(0, scrollable.CursorPosition.X);
    }
}

