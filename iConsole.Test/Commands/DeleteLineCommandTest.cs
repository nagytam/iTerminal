using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Model;
using iConsole.Data;

namespace iConsole.Commands.Test;

[TestClass]
public class DeleteLineCommandTest
{
    [TestMethod]
    public async Task ExecuteAsync_ShouldClearLineAndResetScroll()
    {
        var textModel = new TextModel("line1");
        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(3, 0);
        scrollable.ScrollPosition = new Position(1, 0);

        var cmd = new DeleteLineCommand(textModel, scrollable);
        await cmd.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, textModel.Lines[0]);
        Assert.AreEqual(0, scrollable.CursorPosition.X);
        Assert.AreEqual(0, scrollable.ScrollPosition.X);
    }
}

