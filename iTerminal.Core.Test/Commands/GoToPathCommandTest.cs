using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.Validations;
using Moq;

namespace iTerminal.Core.Test.Commands;

/// <summary>
/// The command behind the Path page's Enter. Navigating with it is covered by
/// <see cref="Dialogs.FolderSelectorPathTabPageTest"/>; here is the contract — the key, the words
/// help prints, and the refusal to run on text the validator has rejected.
/// </summary>
[TestClass]
public class GoToPathCommandTest
{
    private static GoToPathCommand Create(string text = "")
    {
        var dateTimeProviderService = new Mock<IDateTimeProviderService>();
        dateTimeProviderService.SetupGet(m => m.Now).Returns(DateTime.Now);
        var validator = new FolderUrlValidationModel(new TextModel(text), dateTimeProviderService.Object);
        return new GoToPathCommand(null!, validator);
    }

    [TestMethod]
    public void Key_IsPlainEnter()
    {
        var command = Create();

        Assert.AreEqual(ConsoleKey.Enter, command.Key?.ConsoleKey);
        Assert.IsFalse(command.Key!.Control || command.Key.Alt || command.Key.Shift);
    }

    [TestMethod]
    public void TitleAndDescription_AreWrittenForHelp()
    {
        var command = Create();

        Assert.AreEqual("Go to path", command.Title);
        Assert.AreNotEqual(command.Title, command.Description);
    }

    [TestMethod]
    public void CanExecute_WithNoControl_IsFalse()
    {
        Assert.IsFalse(Create("memdrive://").CanExecute());
    }

    [TestMethod]
    public void CanExecute_WithInvalidText_IsFalse()
    {
        Assert.IsFalse(Create(string.Empty).CanExecute());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithNoControl_DoesNothing()
    {
        await Create("memdrive://").ExecuteAsync(null);
    }
}
