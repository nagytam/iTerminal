using iConsole.Data;
using iConsole.Drivers;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Dialogs.Git;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Screens;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Git;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Screens.Test;

[TestClass]
public class TerminalScreenTest
{
    private static (TerminalScreen Screen, GitClientDialog GitDialog, NetworkBrowserDialog NetworkBrowserDialog) CreateScreen()
    {
        var themeService = NullThemeService.Instance;
        var gitDialog = new GitClientDialog(
            new GitService(),
            NullContentModelService.Instance,
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            themeService);
        var settingsService = new SettingsService();
        var networkDiscoveryService = new Mock<INetworkDiscoveryService>();
        networkDiscoveryService.Setup(s => s.ScanAsync(It.IsAny<CancellationToken>())).Returns(EmptyDiscoveredHosts());
        var networkProfileService = new Mock<INetworkProfileService>();
        networkProfileService.Setup(s => s.GetAll()).Returns([]);
        var networkBrowserDialog = new NetworkBrowserDialog(
            networkDiscoveryService.Object,
            networkProfileService.Object,
            Mock.Of<IPasswordManager>(),
            Mock.Of<IContentModelService>(),
            Mock.Of<IBeepService>(),
            new DateTimeProviderService(),
            themeService);
        var screen = new TerminalScreen(
            NullConsoleDriver.Instance,
            menuBar: null!,
            content: null!,
            statusBar: null!,
            themeService,
            new StartupWarningService(),
            gitDialog,
            networkBrowserDialog,
            settingsService);
        return (screen, gitDialog, networkBrowserDialog);
    }

    private static async IAsyncEnumerable<DiscoveredHost> EmptyDiscoveredHosts()
    {
        await Task.CompletedTask;
        yield break;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_CtrlG_OpensGitClientDialog()
    {
        var (screen, gitDialog, _) = CreateScreen();
        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(gitDialog), "dialog should not be open initially");

        var handled = await screen.HandleKeyEventAsync(
            new KeyEvent('\a', ConsoleKey.G, shift: false, alt: false, control: true));

        Assert.IsTrue(handled, "Ctrl+G should be handled by the screen");
        Assert.IsTrue(screen.Controls.AsSnapshot().Contains(gitDialog), "Ctrl+G should open the Git client dialog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_PlainG_DoesNotOpenGitClientDialog()
    {
        var (screen, gitDialog, _) = CreateScreen();

        await screen.HandleKeyEventAsync(
            new KeyEvent('g', ConsoleKey.G, shift: false, alt: false, control: false));

        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(gitDialog),
            "the Git client must only open on Ctrl+G, not a plain 'g'");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_CtrlShiftG_DoesNotOpenGitClientDialog()
    {
        var (screen, gitDialog, _) = CreateScreen();

        await screen.HandleKeyEventAsync(
            new KeyEvent('\a', ConsoleKey.G, shift: true, alt: false, control: true));

        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(gitDialog),
            "the Git client should require exactly Ctrl+G (no extra modifiers)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_CtrlN_OpensNetworkBrowserDialog()
    {
        var (screen, _, networkBrowserDialog) = CreateScreen();
        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(networkBrowserDialog), "dialog should not be open initially");

        var handled = await screen.HandleKeyEventAsync(
            new KeyEvent('', ConsoleKey.N, shift: false, alt: false, control: true));

        Assert.IsTrue(handled, "Ctrl+N should be handled by the screen");
        Assert.IsTrue(screen.Controls.AsSnapshot().Contains(networkBrowserDialog), "Ctrl+N should open the Network Browser dialog");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_PlainN_DoesNotOpenNetworkBrowserDialog()
    {
        var (screen, _, networkBrowserDialog) = CreateScreen();

        await screen.HandleKeyEventAsync(
            new KeyEvent('n', ConsoleKey.N, shift: false, alt: false, control: false));

        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(networkBrowserDialog),
            "the Network Browser must only open on Ctrl+N, not a plain 'n'");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task HandleKeyEventAsync_CtrlShiftN_DoesNotOpenNetworkBrowserDialog()
    {
        var (screen, _, networkBrowserDialog) = CreateScreen();

        await screen.HandleKeyEventAsync(
            new KeyEvent('', ConsoleKey.N, shift: true, alt: false, control: true));

        Assert.IsFalse(screen.Controls.AsSnapshot().Contains(networkBrowserDialog),
            "the Network Browser should require exactly Ctrl+N (no extra modifiers)");
    }
}
