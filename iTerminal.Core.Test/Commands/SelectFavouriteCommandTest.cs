using iConsole.Data;
using iConsole.Model;
using iTerminal.Core.Commands;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class SelectFavouriteCommandTest
{
    private static SelectFavouriteCommand Create() =>
        new(null!, new ScrollableModel(), []);

    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsPlainEnter()
    {
        var command = Create();

        Assert.AreEqual(ConsoleKey.Enter, command.Key?.ConsoleKey);
        Assert.IsFalse(command.Key!.Control || command.Key.Alt || command.Key.Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TitleAndDescription_AreWrittenForHelp()
    {
        var command = Create();

        Assert.AreEqual("Select favourite", command.Title);
        Assert.AreNotEqual(command.Title, command.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WithNoControl_IsFalse()
    {
        Assert.IsFalse(Create().CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithNoControl_DoesNothing()
    {
        await Create().ExecuteAsync(null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RunAsync_WithNoControl_ReportsNotExecuted()
    {
        // Command.RunAsync returning false is what lets the tab page fall through to the focused
        // list instead of swallowing the key.
        Assert.IsFalse(await Create().RunAsync(null));
    }
}
