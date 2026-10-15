using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class IPhoneBrowserDialogCommandTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static IPhoneBrowserDialog CreateDialog(IThemeService themeService)
    {
        var deviceService = new Mock<IIPhoneDeviceService>();
        deviceService.Setup(s => s.GetDevices()).Returns([]);
        return new IPhoneBrowserDialog(deviceService.Object, Mock.Of<IContentModelService>(), themeService);
    }

    [TestMethod]
    public void Constructor_SetsAltF11Key()
    {
        var command = new IPhoneBrowserDialogCommand(CreateDialog(CreateThemeService()));

        // Not Alt+I: the focused file panel takes every Alt+letter for type-to-search.
        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.F11, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Alt);
        Assert.IsFalse(command.Key.Control);
        Assert.IsFalse(command.Key.Shift);
    }

    [TestMethod]
    public void CanExecute_AlwaysReturnsTrue()
    {
        var command = new IPhoneBrowserDialogCommand(CreateDialog(CreateThemeService()));

        Assert.IsTrue(command.CanExecute());
    }

    [TestMethod]
    public async Task ExecuteAsync_WithNoControlAttached_DoesNotThrow()
    {
        var command = new IPhoneBrowserDialogCommand(CreateDialog(CreateThemeService()));

        await command.ExecuteAsync(null);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithControlAttached_OpensDialog()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var command = new IPhoneBrowserDialogCommand(dialog);
        var root = new Control(themeService) { Name = "Root", ViewSize = new Size(120, 40) };
        command.Control = root;

        await command.ExecuteAsync(null);

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }
}
