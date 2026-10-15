using iConsole.Controls;
using iConsole.Data;
using iConsole.Inputs;
using iConsole.Screens;
using iConsole.Services;
using iConsole.Tasks;
using iTerminal.Core.Controls.Contents;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Factories;
using iTerminal.Core.Model;
using iTerminal.Core.Model.ContentModels;
using iTerminal.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace iTerminal.Core.Test.Controls.StatusBar;

/// <summary>
/// End-to-end proof that Ctrl+F1 opens the drive/folder selector (<see cref="FolderSelectorDialogCommand"/>)
/// on Linux: raw stdin bytes for the real xterm-style Ctrl+F1 sequence go through
/// <see cref="TerminalInputUtf8Decoder"/> exactly as <c>iConsole.Inputs.Posix</c> would deliver them,
/// then the decoded <see cref="KeyEvent"/> is routed through a real <see cref="Screen"/> with a focused
/// file panel next to the real status bar, mirroring <c>QuitHotKeyRoutingTest</c>.
///
/// <para>Before the CSI decoder fix, <c>ESC[1;5P</c> (the modified counterpart of the unmodified SS3
/// <c>ESC O P</c> sequence) had no parsing branch at all and was silently discarded, so this chord never
/// reached the command regardless of how it was wired up.</para>
/// </summary>
[TestClass]
public class DriveSelectorHotKeyRoutingTest
{
    private sealed class Harness : IAsyncDisposable
    {
        public required Screen Screen;
        public required FilePanel FilePanel;
        public required string TempRoot;

        public ValueTask DisposeAsync()
        {
            try { Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task<Harness> BuildAsync()
    {
        var themeService = new ThemeService();

        var tempRoot = Path.Combine(Path.GetTempPath(), "DriveSelectorHotKeyTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempRoot);

        var taskManager = new TaskManager();
        var memoryCacheService = new MemoryCacheService();
        var settingsService = new SettingsService();
        var folderHistory = new FolderHistoryService(settingsService);
        var frequentGuard = new Mock<IFrequentExecutionGuardService>();
        frequentGuard.Setup(g => g.Execute(It.IsAny<Action>())).Callback<Action>(a => a());
        var fileSystemService = new FileSystemService();
        var logger = Serilog.Core.Logger.None;

        settingsService.Set(ContentModelService.Left, "file://" + tempRoot);

        var contentModelService = new ContentModelService(
            taskManager, settingsService, fileSystemService,
            Mock.Of<IMemoryDriveService>(), Mock.Of<ISshFileSystemService>(),
            Mock.Of<ISmbFileSystemService>(), Mock.Of<IIPhoneFileSystemService>(), Mock.Of<INetworkProfileService>(), Mock.Of<IPasswordManager>(),
            Mock.Of<IArchiveFileSystemService>(),
            folderHistory, frequentGuard.Object, memoryCacheService, logger);

        var terminalModel = new TerminalModel(taskManager, contentModelService);
        var factory = new FilePanelFactory(taskManager, contentModelService, terminalModel, memoryCacheService, settingsService, logger, themeService);

        var filePanel = factory.Create(ContentModelService.Left);
        filePanel.ViewSize = new Size(80, 24);
        await filePanel.OnInitializeAsync();

        var driveInfoModel = new DriveInfoModel();
        var folderIndexModel = new FolderIndexModel();

        var hotKeys = new iTerminal.Core.Controls.StatusBar.HotKeys(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            contentModelService, folderHistory, driveInfoModel, folderIndexModel, Mock.Of<IBeepService>(), new DateTimeProviderService(),
            settingsService, frequentGuard.Object, themeService);
        var statusBar = new iTerminal.Core.Controls.StatusBar.TerminalStatusBar(
            new iConsole.Controls.PopUpWindows.DatePopUpWindow(themeService), hotKeys, themeService);

        var screen = new Screen(NullConsoleDriver.Instance, themeService) { ViewSize = new Size(120, 40) };
        screen.AddControl(filePanel);
        screen.AddControl(statusBar);
        await screen.OnInitializeAsync();

        filePanel.SetFocus();
        Assert.IsTrue(filePanel.IsFocused, "Precondition: file panel must actually be focused");

        return new Harness
        {
            Screen = screen,
            FilePanel = filePanel,
            TempRoot = tempRoot,
        };
    }

    [TestMethod]
    public async Task CtrlF1RawBytes_WithFilePanelFocused_OpensFolderSelectorDialog()
    {
        await using var h = await BuildAsync();

        // The real xterm-style byte sequence a Linux terminal sends for Ctrl+F1.
        var decoder = new TerminalInputUtf8Decoder();
        decoder.Append("\x1b[1;5P"u8);
        KeyEvent? decoded = null;
        decoder.Drain(k => decoded = k, _ => { });

        Assert.IsNotNull(decoded, "The raw byte sequence must decode to a KeyEvent");
        Assert.AreEqual(ConsoleKey.F1, decoded!.ConsoleKey);
        Assert.IsTrue(decoded.Control);
        Assert.IsFalse(decoded.Shift);
        Assert.IsFalse(decoded.Alt);

        var logger = new iConsole.Test.Core.InMemoryCommandLogger();
        h.Screen.SetCommandLogger(logger);

        var isHandled = await h.Screen.HandleKeyEventAsync(decoded);

        Assert.IsTrue(isHandled,
            "Ctrl+F1 should be reported as handled by the screen's control tree. Commands run: "
                + string.Join(" | ", logger.Records.Select(r => $"{r.CommandName} canExecute={r.CanExecute} executed={r.Executed} err={r.ExceptionMessage}")));

        var all = Flatten(h.Screen).ToList();
        Assert.IsTrue(
            all.Any(c => c is FolderSelectorDialog),
            "Ctrl+F1 should open the FolderSelectorDialog even though the file panel has focus. Commands run: "
                + string.Join(" | ", logger.Records.Select(r => $"{r.CommandName} canExecute={r.CanExecute} executed={r.Executed} err={r.ExceptionMessage}"))
                + " Tree: " + string.Join(",", all.Select(c => c.GetType().Name)));
    }

    private static IEnumerable<IControl> Flatten(IControl c)
    {
        yield return c;
        foreach (var child in c.Controls.AsSnapshot())
        {
            foreach (var d in Flatten(child))
            {
                yield return d;
            }
        }
    }
}
