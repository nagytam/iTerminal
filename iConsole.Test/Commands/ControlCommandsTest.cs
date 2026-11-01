using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using Moq;

namespace iConsole.Test.Commands;

[TestClass]
public class ControlCommandsTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void CloseCommandKey()
    {
        var command = new CloseCommand();
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.Escape, command.Key.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CloseCommandCanExecute()
    {
        var command = new CloseCommand();
        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task CloseCommandExecute()
    {
        var mockControl = new Mock<IControl>();
        var command = new CloseCommand();
        command.Control = mockControl.Object;

        await command.ExecuteAsync(null);

        mockControl.Verify(c => c.Close(), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CloseCommandExecuteWithoutControl()
    {
        var command = new CloseCommand();
        command.Control = null;

        var task = command.ExecuteAsync(null);
        Assert.IsNotNull(task);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void QuitCommandKey()
    {
        var command = new QuitCommand();
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F10, command.Key.ConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void QuitCommandCanExecute()
    {
        var command = new QuitCommand();
        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task QuitCommandExecute()
    {
        var command = new QuitCommand();

        var task = command.ExecuteAsync(null);
        Assert.IsNotNull(task);
        await task;
    }
}
