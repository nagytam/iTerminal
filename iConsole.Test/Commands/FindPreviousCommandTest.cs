using Microsoft.VisualStudio.TestTools.UnitTesting;
using iConsole.Commands;
using iConsole.Model;
using iConsole.Data;
using Moq;

namespace iConsole.Commands.Test;

[TestClass]
public class FindPreviousCommandTest
{
    [TestMethod]
    public async Task ExecuteAsync_ShouldMoveCursorToPreviousHit()
    {
        var hits = new iConsole.Data.ConcurrentObservableCollection<Position>(new[] { new Position(0,0), new Position(5,1), new Position(2,2) });
        var search = new Mock<ISearchModel>();
        search.SetupGet(s => s.Hits).Returns(hits);

        var scrollable = new ScrollableModel();
        scrollable.CursorPosition = new Position(2,2);

        var cmd = new FindPreviousCommand(search.Object, scrollable);
        await cmd.ExecuteAsync(null);

        Assert.AreEqual(5, scrollable.CursorPosition.X);
        Assert.AreEqual(1, scrollable.CursorPosition.Y);
    }
}

