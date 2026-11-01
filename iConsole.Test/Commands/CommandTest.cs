using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Model;
using Moq;

namespace iConsole.Test.Commands;

[TestClass]
public class CommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void CommandHasTitle()
    {
        var mockControl = new Mock<IControl>();
        var mockScrollableModel = new Mock<IScrollableModel>();
        var mockTextModel = new Mock<ITextModel>();

        // BackspaceCommand is a concrete Command for testing
        var command = new BackspaceCommand(mockTextModel.Object, mockScrollableModel.Object);
        Assert.IsNotNull(command.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandHasKey()
    {
        var mockControl = new Mock<IControl>();
        var mockScrollableModel = new Mock<IScrollableModel>();
        var mockTextModel = new Mock<ITextModel>();

        var command = new BackspaceCommand(mockTextModel.Object, mockScrollableModel.Object);
        Assert.IsNotNull(command.Key);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandSetControl()
    {
        var mockControl = new Mock<IControl>();
        var mockScrollableModel = new Mock<IScrollableModel>();
        var mockTextModel = new Mock<ITextModel>();

        var command = new BackspaceCommand(mockTextModel.Object, mockScrollableModel.Object);
        command.Control = mockControl.Object;
        Assert.AreEqual(mockControl.Object, command.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandCanExecute()
    {
        var mockControl = new Mock<IControl>();
        var mockScrollableModel = new Mock<IScrollableModel>();
        var mockTextModel = new Mock<ITextModel>();

        var command = new BackspaceCommand(mockTextModel.Object, mockScrollableModel.Object);
        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CommandInterface()
    {
        var mockScrollableModel = new Mock<IScrollableModel>();
        var mockTextModel = new Mock<ITextModel>();

        var command = new BackspaceCommand(mockTextModel.Object, mockScrollableModel.Object);
        Assert.IsInstanceOfType(command, typeof(ICommand));
    }

    /// <summary>Help listed "Ctrl+Oem5" — the virtual-key constant, not a key anyone can find.</summary>
    [Timeout(60_000)]
    [TestMethod]
    [DataRow(ConsoleKey.Oem5, "Ctrl+\\")]
    [DataRow(ConsoleKey.D3, "Ctrl+3")]
    [DataRow(ConsoleKey.OemPeriod, "Ctrl+.")]
    [DataRow(ConsoleKey.Add, "Ctrl+Num+")]
    [DataRow(ConsoleKey.F7, "Ctrl+F7")]
    [DataRow(ConsoleKey.PageUp, "Ctrl+PgUp")]
    public void KeysAreNamedAsTheyArePrinted(ConsoleKey consoleKey, string expected) =>
        Assert.AreEqual(expected, Command.FormatKey(new Key(consoleKey, false, false, true)));
}
