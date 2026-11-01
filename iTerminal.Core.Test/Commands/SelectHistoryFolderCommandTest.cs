using iTerminal.Core.Commands;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class SelectHistoryFolderCommandTest
{
    private static SelectHistoryFolderCommand Create() => new(null!, null!);

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

        Assert.AreEqual("Select folder", command.Title);
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
}
