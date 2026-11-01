using iConsole.Controls;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;
using Moq;

namespace iTerminal.Core.Test.Commands;

[TestClass]
public class NetworkBrowserDialogCommandTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private static NetworkBrowserDialog CreateDialog(IThemeService themeService)
    {
        var networkDiscoveryService = new Mock<INetworkDiscoveryService>();
        networkDiscoveryService.Setup(s => s.ScanAsync(It.IsAny<CancellationToken>())).Returns(EmptyHosts());
        var networkProfileService = new Mock<INetworkProfileService>();
        networkProfileService.Setup(s => s.GetAll()).Returns([]);

        return new NetworkBrowserDialog(
            networkDiscoveryService.Object,
            networkProfileService.Object,
            Mock.Of<IPasswordManager>(),
            Mock.Of<IContentModelService>(),
            Mock.Of<IBeepService>(),
            Mock.Of<IDateTimeProviderService>(),
            themeService);
    }

    private static async IAsyncEnumerable<DiscoveredHost> EmptyHosts()
    {
        await Task.CompletedTask;
        yield break;
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_SetsCtrlNKey()
    {
        var themeService = CreateThemeService();
        var command = new NetworkBrowserDialogCommand(CreateDialog(themeService));

        Assert.IsNotNull(command.Key);
        Assert.AreEqual(ConsoleKey.N, command.Key!.ConsoleKey);
        Assert.IsTrue(command.Key.Control);
        Assert.IsFalse(command.Key.Shift);
        Assert.IsFalse(command.Key.Alt);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void CanExecute_AlwaysReturnsTrue()
    {
        var themeService = CreateThemeService();
        var command = new NetworkBrowserDialogCommand(CreateDialog(themeService));

        Assert.IsTrue(command.CanExecute());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithNoControlAttached_DoesNotThrow()
    {
        var themeService = CreateThemeService();
        var command = new NetworkBrowserDialogCommand(CreateDialog(themeService));

        await command.ExecuteAsync(null);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ExecuteAsync_WithControlAttached_OpensDialog()
    {
        var themeService = CreateThemeService();
        var dialog = CreateDialog(themeService);
        var command = new NetworkBrowserDialogCommand(dialog);
        var root = new Control(themeService) { Name = "Root", ViewSize = new iConsole.Data.Size(120, 40) };
        command.Control = root;

        await command.ExecuteAsync(null);

        Assert.IsTrue(root.Controls.AsSnapshot().Contains(dialog));
    }
}
