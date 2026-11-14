using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls.StatusBar;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Git;
using Moq;

namespace iTerminal.Core.Test.Controls.StatusBar;

/// <summary>
/// Routes hot keys through the real <see cref="HotKeys"/> control inside a real status bar, rather
/// than a stand-in control tree. Compares the music chord against Alt+F11, an existing
/// <c>AddCommand</c>-based hot key, so a failure says whether the problem is specific to the music
/// binding or shared by every command attached the same way.
/// </summary>
[TestClass]
public class RealHotKeysRoutingTest
{
    private static IThemeService CreateThemeService()
    {
        var mock = new Mock<IThemeService>();
        mock.Setup(m => m.CurrentColorScheme).Returns(new ColorScheme());
        return mock.Object;
    }

    private sealed record Harness(
        Control Root,
        HotKeys HotKeys,
        Mock<IContentModelService> ContentModelService,
        IPhoneBrowserDialog IPhoneDialog);

    private static async Task<Harness> BuildAsync(IGitService? gitService = null)
    {
        var themeService = CreateThemeService();
        var contentModelService = new Mock<IContentModelService>();
        contentModelService
            .Setup(s => s.SetUrlAsync(It.IsAny<string>(), It.IsAny<Url>()))
            .Returns(Task.CompletedTask);

        var iPhoneDeviceService = new Mock<IIPhoneDeviceService>();
        iPhoneDeviceService.Setup(s => s.GetDevices()).Returns([]);
        var iPhoneDialog = new IPhoneBrowserDialog(iPhoneDeviceService.Object, contentModelService.Object, themeService);

        // The real HotKeys, built positionally exactly as TerminalStatusBar's fallback does. Only
        // the dialog under test and the services HotKeys dereferences need to be real.
        var hotKeys = new HotKeys(
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, iPhoneDialog, null!, null!, null!, null!, null!, null!,
            contentModelService.Object, null!, null!, null!, null!, null!, NullSettingsService.Instance, null!, themeService, gitService: gitService);

        var statusBar = new iConsole.Controls.StatusBar.StatusBar(themeService) { ViewSize = new Size(120, 1) };
        statusBar.AddControl(hotKeys);

        var root = new Control(themeService) { Name = "Screen", ViewSize = new Size(120, 40) };
        var panel = new Control(themeService) { Name = "Panel", CanFocus = true, ViewSize = new Size(120, 30) };
        root.AddControl(panel);
        root.AddControl(statusBar);
        await root.OnInitializeAsync();

        panel.SetFocus();
        Assert.IsTrue(panel.IsFocused, "Precondition: the panel must hold focus");

        return new Harness(root, hotKeys, contentModelService, iPhoneDialog);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AltF11_ReachesTheExistingIPhoneCommand()
    {
        var harness = await BuildAsync();

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F11, false, true, false));

        Assert.IsTrue(harness.IPhoneDialog.IsVisible,
            "Baseline: an existing AddCommand-based hot key must route through the real status bar");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlShiftM_ReachesTheMusicPanelsCommand()
    {
        var harness = await BuildAsync();

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.M, true, false, true));

        AssertMusicPanelsWereRequested(harness);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlF7_ReachesTheMusicPanelsCommand()
    {
        var harness = await BuildAsync();

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F7, false, false, true));

        AssertMusicPanelsWereRequested(harness);
    }

    /// <summary>Gives the harness two panels on the local disk, the left one focused.</summary>
    private static void WithPanels(Harness harness)
    {
        var left = new Mock<IContentModel>();
        left.SetupGet(m => m.Name).Returns("LeftPanel");
        left.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        var right = new Mock<IContentModel>();
        right.SetupGet(m => m.Name).Returns("RightPanel");
        right.SetupGet(m => m.Url).Returns(new Url(Path.GetTempPath()));
        harness.ContentModelService.Setup(s => s.GetFocusedModel()).Returns(left.Object);
        harness.ContentModelService.SetupGet(s => s.ContentModels).Returns([left.Object, right.Object]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlF9_PointsTheFocusedPanelAtTheContainers()
    {
        var harness = await BuildAsync();
        WithPanels(harness);

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F9, false, false, true));

        harness.ContentModelService.Verify(s => s.SetUrlAsync("LeftPanel", It.Is<Url>(url => url.FullUrl == Container.DOCKER)), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlF6_PointsTheFocusedPanelAtTheGitDrive()
    {
        var harness = await BuildAsync();
        WithPanels(harness);

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F6, false, false, true));

        harness.ContentModelService.Verify(s => s.SetUrlAsync("LeftPanel", It.Is<Url>(url => url.ContainerType == ContainerType.Git)), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlF3_OpensThePreviewInTheOtherPanel()
    {
        var harness = await BuildAsync();
        WithPanels(harness);

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F3, false, false, true));

        harness.ContentModelService.Verify(s => s.SetUrlAsync("RightPanel", It.Is<Url>(url => url.FullUrl == Container.PREVIEW)), Times.Once,
            "the focused panel keeps its listing; the other one previews");
    }

    private static void AssertMusicPanelsWereRequested(Harness harness)
    {
        harness.ContentModelService.Verify(
            s => s.SetUrlAsync("LeftPanel", It.Is<Url>(url => url.ContainerType == ContainerType.MusicCatalog)),
            Times.Once,
            "The catalog belongs in the left panel");
        harness.ContentModelService.Verify(
            s => s.SetUrlAsync("RightPanel", It.Is<Url>(url => url.ContainerType == ContainerType.MusicPlaylist)),
            Times.Once,
            "The playlist belongs in the right panel");
    }

    /// <summary>Ctrl+Y runs the git sync in the focused panel's working tree, pulling first and then pushing.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlD_SyncsTheFocusedWorkingTree()
    {
        string root = Path.Combine(Path.GetTempPath(), "iTerminal-hotkey-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            int pulls = 0;
            int pushes = 0;
            Mock<IGitService> git = new();
            git.Setup(g => g.GetRepositoryRootAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(root);
            git.Setup(g => g.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GitRepositoryStatus("main", "origin/main", 0, 0, [], []));
            git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref pulls)).ReturnsAsync(GitResult.Ok());
            git.Setup(g => g.PushAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref pushes)).ReturnsAsync(GitResult.Ok());

            var harness = await BuildAsync(git.Object);
            Mock<IContentModel> model = new();
            model.SetupGet(m => m.Url).Returns(new Url(root));
            harness.ContentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);

            await harness.Root.HandleKeyEventAsync(new KeyEvent('\u0004', ConsoleKey.D, false, false, true));

            await WaitUntilAsync(() => Volatile.Read(ref pushes) == 1);
            Assert.AreEqual(1, Volatile.Read(ref pulls), "the pull runs first");
            Assert.AreEqual(1, Volatile.Read(ref pushes), "and the push after it");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Outside a working tree the key does nothing: there is no repository to sync.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task ControlD_OutsideAWorkingTree_PullsNothing()
    {
        string plain = Path.Combine(Path.GetTempPath(), "iTerminal-hotkey-plain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(plain);
        try
        {
            int pulls = 0;
            Mock<IGitService> git = new();
            git.Setup(g => g.PullRebaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback(() => Interlocked.Increment(ref pulls)).ReturnsAsync(GitResult.Ok());

            var harness = await BuildAsync(git.Object);
            Mock<IContentModel> model = new();
            model.SetupGet(m => m.Url).Returns(new Url(plain));
            harness.ContentModelService.Setup(s => s.GetFocusedModel()).Returns(model.Object);

            await harness.Root.HandleKeyEventAsync(new KeyEvent('\u0004', ConsoleKey.D, false, false, true));
            await Task.Delay(300);

            Assert.AreEqual(0, Volatile.Read(ref pulls));
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>The bottom bar has the git sync as Ctrl-D-Sync when it has a git service, and no such key without one.</summary>
    [Timeout(60_000)]
    [TestMethod]
    public async Task TheBottomBarOffersGitSyncOnCtrlDOnlyWhenGitIsAvailable()
    {
        Mock<IGitService> git = new();
        var withGit = await BuildAsync(git.Object);
        var without = await BuildAsync();

        var sync = withGit.HotKeys.Controls.AsSnapshot().OfType<Button>().Single(b => b.Title == "Ctrl-D-Sync");
        Assert.AreEqual(ConsoleKey.D, sync.Commands.First().Key!.ConsoleKey);
        Assert.IsTrue(sync.Commands.First().Key!.Control);
        Assert.IsFalse(without.HotKeys.Controls.AsSnapshot().OfType<Button>().Any(b => b.Title == "Ctrl-D-Sync"));
    }
}
