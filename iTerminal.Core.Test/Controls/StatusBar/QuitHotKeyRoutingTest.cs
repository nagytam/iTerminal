using iConsole.Controls;
using iConsole.Data;
using iConsole.Desktops;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Controls.StatusBar;

/// <summary>
/// Reproduces the real screen tree (a focused file panel sitting next to the status bar's
/// F10-Quit hot key) to determine whether F10 actually reaches <see cref="iConsole.Commands.QuitCommand"/>
/// when the file panel has focus, exactly as it does in the running application.
/// </summary>
[TestClass]
public class QuitHotKeyRoutingTest
{
    private static KeyEvent F10(bool alt = false) => new KeyEvent('\0', ConsoleKey.F10, shift: false, alt: alt, control: false);
    private static KeyEvent Char(char c, bool alt = false) => new KeyEvent(c, (ConsoleKey)char.ToUpperInvariant(c), shift: false, alt: alt, control: false);

    private sealed class Harness : IAsyncDisposable
    {
        public required Screen Screen;
        public required FilePanel FilePanel;
        public required ContentModelFileSystem Model;
        public required CancellationTokenSource DesktopCts;
        private readonly string _tempRoot = "";

        public required string TempRoot { init => _tempRoot = value; }

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(_tempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<Harness> BuildAsync()
    {
        var themeService = new ThemeService();

        // ── Real file panel, wired exactly like FilePanelIntegrationTest's AppHarness ──
        var tempRoot = Path.Combine(Path.GetTempPath(), "QuitHotKeyRoutingTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);

        var taskManager = new TaskManager();
        var memoryCacheService = new MemoryCacheService();
        var settingsService = new SettingsService();
        var folderHistory = new FolderHistoryService(settingsService);
        var frequentGuard = new FrequentExecutionGuardService(new DateTimeProviderService());
        var fileSystemService = new FileSystemService();
        var logger = Serilog.Core.Logger.None;

        settingsService.Set(ContentModelService.Left, "file://" + tempRoot);

        var contentModelService = new ContentModelService(
            taskManager, settingsService, fileSystemService,
            Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(), Mock.Of<IIPhoneFileSystemService>(), Mock.Of<INetworkProfileService>(), Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            folderHistory, frequentGuard, memoryCacheService, logger);

        var terminalModel = new TerminalModel(taskManager, contentModelService);
        var factory = new FilePanelFactory(taskManager, contentModelService, terminalModel, memoryCacheService, settingsService, logger, themeService);

        var filePanel = factory.Create(ContentModelService.Left);
        filePanel.ViewSize = new Size(80, 24);
        await filePanel.OnInitializeAsync();

        // ── Real status bar / hot keys, wired exactly like TerminalStatusBar's fallback ──
        var hotKeys = new iTerminal.Core.Controls.StatusBar.HotKeys(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            contentModelService, folderHistory, null!, null!, null!, null!, settingsService, null!, themeService);
        var statusBar = new iTerminal.Core.Controls.StatusBar.TerminalStatusBar(
            new iConsole.Controls.PopUpWindows.DatePopUpWindow(themeService), hotKeys, themeService);

        // ── Root screen hosting both, mirroring TerminalScreen's composition ──
        var screen = new Screen(NullConsoleDriver.Instance, themeService);
        screen.AddControl(filePanel);
        screen.AddControl(statusBar);
        await screen.OnInitializeAsync();

        filePanel.SetFocus();
        Assert.IsTrue(filePanel.IsFocused, "Precondition: file panel must actually be focused");

        var mockDesktop = new Mock<IDesktop>();
        var cts = new CancellationTokenSource();
        mockDesktop.SetupGet(d => d.CancellationTokenSource).Returns(cts);
        screen.Desktop = mockDesktop.Object;

        var model = (ContentModelFileSystem)contentModelService.GetModel(ContentModelService.Left)!;

        return new Harness
        {
            Screen = screen,
            FilePanel = filePanel,
            Model = model,
            DesktopCts = cts,
            TempRoot = tempRoot,
        };
    }

    [TestMethod]
    public async Task F10_WithFilePanelFocused_CancelsDesktop()
    {
        await using var h = await BuildAsync();

        var isHandled = await h.Screen.HandleKeyEventAsync(F10());

        Assert.IsTrue(isHandled, "F10 should be reported as handled by the screen's control tree");
        Assert.IsTrue(h.DesktopCts.IsCancellationRequested, "F10 should cancel the desktop token (Quit) even though the file panel has focus");
    }

    /// <summary>
    /// The file panel's search filter is started with ALT + a printable character. Windows'
    /// raw console input (dwControlKeyState on KEY_EVENT_RECORD) is known to sometimes keep
    /// reporting ALT as held on subsequent key events after an ALT-chord, even once the key
    /// has actually been released. Simulate that: start a filter with Alt+A, then send F10
    /// with a (stale) Alt flag still set, exactly as the OS would deliver it in that scenario.
    /// </summary>
    [TestMethod]
    public async Task F10_WithStaleAltFlagAfterUsingFilePanelSearch_StillCancelsDesktop()
    {
        await using var h = await BuildAsync();

        var filterStarted = await h.Screen.HandleKeyEventAsync(Char('a', alt: true));
        Assert.IsTrue(filterStarted, "Alt+A should have been consumed to start the panel's search filter");
        Assert.IsNotNull(h.Model.Filter, "Precondition: the search filter must actually be active");

        var isHandled = await h.Screen.HandleKeyEventAsync(F10(alt: true));

        Assert.IsTrue(isHandled, "F10 should be reported as handled even with a stray Alt flag");
        Assert.IsTrue(h.DesktopCts.IsCancellationRequested,
            "F10 should still cancel the desktop token (Quit) even if the console reports a stale Alt flag");
    }
}
