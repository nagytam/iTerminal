using iConsole.Controls;
using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs.UnitConverter;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class UnitConverterDialogCommandTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new iConsole.Display.ColorScheme());
        return mock.Object;
    }

    private static UnitConverterDialog CreateDialog(IThemeService themeService) =>
        new(Mock.Of<IBeepService>(), Mock.Of<IDateTimeProviderService>(), themeService);

    [TestMethod]
    public void Constructor_SetsCtrlEKey()
    {
        var themeService = CreateThemeService();
        var command = new UnitConverterDialogCommand(CreateDialog(themeService));

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.E, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
    }

    [TestMethod]
    public void CanExecute_AlwaysReturnsTrue()
    {
        var themeService = CreateThemeService();
        var command = new UnitConverterDialogCommand(CreateDialog(themeService));

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithNoControlAttached_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var command = new UnitConverterDialogCommand(CreateDialog(themeService));

        await command.ExecuteAsync(null);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithControlAttached_OpensDialog()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var command = new UnitConverterDialogCommand(dialog);
        var root = new Control(themeService) { Name = "Root", ViewSize = new Size(120, 40) };
        command.Control = root;

        await command.ExecuteAsync(null);

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }
}
