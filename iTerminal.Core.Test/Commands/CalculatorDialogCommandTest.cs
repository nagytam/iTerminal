using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.Calculator;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class CalculatorDialogCommandTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return mock.Object;
    }

    [TestMethod]
    public void Constructor_SetsCtrlAKey()
    {
        var themeService = CreateThemeService();
        var command = new CalculatorDialogCommand(new CalculatorDialog(themeService));

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.A, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
    }

    [TestMethod]
    public void CanExecute_AlwaysReturnsTrue()
    {
        var themeService = CreateThemeService();
        var command = new CalculatorDialogCommand(new CalculatorDialog(themeService));

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithNoControlAttached_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var command = new CalculatorDialogCommand(new CalculatorDialog(themeService));

        await command.ExecuteAsync(null);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithControlAttached_OpensDialog()
    {
        var themeService = CreateThemeService();
        var dialog = new CalculatorDialog(themeService);
        var command = new CalculatorDialogCommand(dialog);
        var root = new Control(themeService) { Name = "Root", ViewSize = new Size(120, 40) };
        command.Control = root;

        await command.ExecuteAsync(null);

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }
}
