using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The error a failed command leaves in a form goes when the user starts typing again, and only then:
/// not on Tab, the arrows or Enter. Copy, Move, Compress, Delete, Rename and Mark.
/// </summary>
[TestClass]
public class FormErrorClearingTest
{
    private static IContentModelService Panels()
    {
        Mock<IContentModelService> service = new();
        service.SetupGet(s => s.ContentModels).Returns([]);
        return service.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Copy_AFailureIsShown_AndLeavesOnTyping()
    {
        CopyDialog dialog = new(null!, Panels(), null!, null!, null!, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!);
        dialog.ErrorMessage.Text = "The disk is full.";
        Assert.AreEqual("Error: The disk is full.", dialog.ErrorMessage.Text);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.LeftArrow));
        Assert.AreEqual("Error: The disk is full.", dialog.ErrorMessage.Text);

        await dialog.HandleKeyEventAsync(Char('x'));
        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Delete_TheErrorLeavesOnBackspace()
    {
        DeleteDialog dialog = new(Panels(), Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme(), null!, null!, null!, null!, null!, null!, null!);
        await ShowAsync(dialog);
        dialog.ErrorMessage.Text = "Access is denied.";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.UpArrow));
        Assert.AreEqual("Access is denied.", dialog.ErrorMessage.Text);

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.Backspace));
        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Rename_TheErrorLeavesOnTyping_ButNotOnAChord()
    {
        RenameDialog dialog = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), Panels(), TestTheme());
        await ShowAsync(dialog);
        dialog.ErrorMessage.Text = "Type the new name.";

        await dialog.HandleKeyEventAsync(Key(ConsoleKey.A, 'a', control: true));
        Assert.AreEqual("Type the new name.", dialog.ErrorMessage.Text);

        await dialog.HandleKeyEventAsync(Char('n'));
        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task MarkPattern_TheErrorLeavesOnTyping()
    {
        MarkPatternDialog dialog = new(Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        await ShowAsync(dialog);
        dialog.ErrorMessage.Text = "Type a pattern, such as *.jpg;*.png.";

        await dialog.HandleKeyEventAsync(Char('*'));

        Assert.AreEqual(string.Empty, dialog.ErrorMessage.Text);
    }
}
