using iConsole.Commands;
using iConsole.Data;
using iConsole.Model;
using Moq;

namespace iConsole.Test.Commands;

[TestClass]
public class ScrollCommandsTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void ScrollDownCommandKey()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        var command = new ScrollDownCommand(mockScrollableModel.Object);
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.DownArrow, command.Key.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollDownCommandCanExecute()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        var command = new ScrollDownCommand(mockScrollableModel.Object);
        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollDownCommandExecute()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        mockScrollableModel.Setup(m => m.ViewSize).Returns(new Size(80, 24));
        mockScrollableModel.Setup(m => m.ScrollableSize).Returns(new Size(80, 100));
        mockScrollableModel.Setup(m => m.CursorPosition).Returns(new Position(0, 0));
        mockScrollableModel.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));

        var command = new ScrollDownCommand(mockScrollableModel.Object);
        var task = command.ExecuteAsync(null);

        Assert.IsNotNull(task);
        mockScrollableModel.Verify(m => m.Update(), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollUpCommandKey()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        var command = new ScrollUpCommand(mockScrollableModel.Object);
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.UpArrow, command.Key.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollUpCommandCanExecute()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        var command = new ScrollUpCommand(mockScrollableModel.Object);
        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ScrollUpCommandExecute()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        mockScrollableModel.Setup(m => m.ViewSize).Returns(new Size(80, 24));
        mockScrollableModel.Setup(m => m.ScrollableSize).Returns(new Size(80, 100));
        mockScrollableModel.Setup(m => m.CursorPosition).Returns(new Position(0, 10));
        mockScrollableModel.Setup(m => m.ScrollPosition).Returns(new Position(0, 0));

        var command = new ScrollUpCommand(mockScrollableModel.Object);
        var task = command.ExecuteAsync(null);

        Assert.IsNotNull(task);
        mockScrollableModel.Verify(m => m.Update(), Times.Once);
    }
}
