using iTerminal.Core.Commands;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// The command behind the Drives page's Enter. Its behaviour with a page attached is covered by
/// <see cref="Dialogs.FolderSelectorDrivesTabPageTest"/>; what is left here is the contract the page
/// relies on — the key it claims, the words help prints, and what it does when it is not on a page
/// at all, which is what a stray Enter routed through the control tree looks like.
/// </summary>
[TestClass]
public class SelectDriveCommandTest
{
    [Timeout(60_000)]
    [TestMethod]
    public void Key_IsPlainEnter()
    {
        var command = new SelectDriveCommand(null!);

        Assert.AreEqual(ConsoleKey.Enter, command.Key?.ConsoleKey);
        Assert.IsFalse(command.Key!.Control || command.Key.Alt || command.Key.Shift);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TitleAndDescription_AreWrittenForHelpNotDerivedFromTheTypeName()
    {
        var command = new SelectDriveCommand(null!);

        Assert.AreEqual("Select drive", command.Title);
        Assert.AreNotEqual(command.Title, command.Description);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_WithNoControl_IsFalse()
    {
        var command = new SelectDriveCommand(null!);

        Assert.IsFalse(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithNoControl_DoesNothing()
    {
        var command = new SelectDriveCommand(null!);

        await command.ExecuteAsync(null);
    }
}
