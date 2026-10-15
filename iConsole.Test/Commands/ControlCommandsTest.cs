using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using Moq;

namespace iConsole.Test.Commands;

[TestClass]
public class ControlCommandsTest
{
    [TestMethod]
    public void CloseCommandKey()
    {
        var command = new CloseCommand();
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.Escape, command.Key.ConsoleKey);
    }

    [TestMethod]
    public void CloseCommandCanExecute()
    {
        var command = new CloseCommand();
        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public async Task CloseCommandExecute()
    {
        var mockControl = new Mock<IControl>();
        var command = new CloseCommand();
        command.Control = mockControl.Object;

        await command.ExecuteAsync(null);

        mockControl.Verify(c => c.Close(), Times.Once);
    }

    [TestMethod]
    public void CloseCommandExecuteWithoutControl()
    {
        var command = new CloseCommand();
        command.Control = null;

        var task = command.ExecuteAsync(null);
        Assert.IsNotNull(task);
    }

    [TestMethod]
    public void QuitCommandKey()
    {
        var command = new QuitCommand();
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F10, command.Key.ConsoleKey);
    }

    [TestMethod]
    public void QuitCommandCanExecute()
    {
        var command = new QuitCommand();
        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public async Task QuitCommandExecute()
    {
        var command = new QuitCommand();

        var task = command.ExecuteAsync(null);
        Assert.IsNotNull(task);
        await task;
    }
}
