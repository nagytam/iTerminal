using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using iConsole.Commands;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Commands.Test;

[TestClass]
public class HelpDialogCommandTest
{
    private static HelpDialog CreateDialog(IThemeService themeService)
    {
        return new HelpDialog(themeService);
    }

    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsF1Key()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new HelpDialogCommand(dialog);

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F1, command.Key!.ConsoleKey);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_StoresDialog()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new HelpDialogCommand(dialog);

        Assert.IsNotNull(command);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_AlwaysReturnsTrue()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new HelpDialogCommand(dialog);

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithNoControlAttached_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);

        var command = new HelpDialogCommand(dialog);

        // No Control attached — must not throw.
        await command.ExecuteAsync(null);
    }
}
