using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Portfolio;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Portfolio;
using Moq;

namespace iTerminal.Core.Test.Dialogs.Portfolio;

[TestClass]
public class PortfolioManagerDialogTest
{
    private static PortfolioManagerDialog CreateDialog()
    {
        return new PortfolioManagerDialog(
            Mock.Of<IPortfolioService>(),
            new OnlineQuoteService(),
            Mock.Of<IQuoteProvider>(),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            NullThemeService.Instance);
    }

    [TestMethod]
    public void Constructor_CreatesInstance()
    {
        var dialog = CreateDialog();

        Assert.IsNotNull(dialog);
        Assert.IsTrue(dialog.IsModal);
    }

    [TestMethod]
    public async Task HandleKeyEventAsync_Escape_ClosesDialog()
    {
        var dialog = CreateDialog();

        var handled = await dialog.HandleKeyEventAsync(
            new KeyEvent('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        Assert.IsTrue(handled, "Escape should be handled and close the dialog");
    }

    [TestMethod]
    public async Task OpeningKeepsTheInlineInputHiddenSoTheFooterIsDrawn()
    {
        // Control.ShowAsync makes every child visible on the way in. The footer was keyed off the
        // input box's visibility, so it — key hints and the delete confirmation — never appeared.
        var dialog = CreateDialog();
        var root = new iConsole.Controls.Control(NullThemeService.Instance) { Name = "Root", ViewSize = new Size(120, 40) };

        await root.ShowDialogAsync(dialog);

        Assert.IsFalse(dialog.IsInputActiveForTest);
        Assert.IsFalse(dialog.IsInputBoxVisibleForTest);
    }

    [TestMethod]
    public async Task AShowsTheInlineInputAndEscapeHidesItWithoutClosing()
    {
        var dialog = CreateDialog();
        var root = new iConsole.Controls.Control(NullThemeService.Instance) { Name = "Root", ViewSize = new Size(120, 40) };
        await root.ShowDialogAsync(dialog);

        await dialog.HandleKeyEventAsync(new KeyEvent('a', ConsoleKey.A, shift: false, alt: false, control: false));

        Assert.IsTrue(dialog.IsInputActiveForTest);
        Assert.IsTrue(dialog.IsInputBoxVisibleForTest);

        await dialog.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.Escape, shift: false, alt: false, control: false));

        Assert.IsFalse(dialog.IsInputActiveForTest);
        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog), "Esc backs out of the input, not the dialog");
    }
}
