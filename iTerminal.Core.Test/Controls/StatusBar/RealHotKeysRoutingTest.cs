using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls.StatusBar;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
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

    private static async Task<Harness> BuildAsync()
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
            contentModelService.Object, null!, null!, null!, null!, null!, NullSettingsService.Instance, null!, themeService);

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

    [TestMethod]
    public async Task AltF11_ReachesTheExistingIPhoneCommand()
    {
        var harness = await BuildAsync();

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F11, false, true, false));

        Assert.IsTrue(harness.IPhoneDialog.IsVisible,
            "Baseline: an existing AddCommand-based hot key must route through the real status bar");
    }

    [TestMethod]
    public async Task ControlShiftM_ReachesTheMusicPanelsCommand()
    {
        var harness = await BuildAsync();

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\r', ConsoleKey.M, true, false, true));

        AssertMusicPanelsWereRequested(harness);
    }

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

    [TestMethod]
    public async Task ControlF9_PointsTheFocusedPanelAtTheContainers()
    {
        var harness = await BuildAsync();
        WithPanels(harness);

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F9, false, false, true));

        harness.ContentModelService.Verify(s => s.SetUrlAsync("LeftPanel", It.Is<Url>(url => url.FullUrl == Container.DOCKER)), Times.Once);
    }

    [TestMethod]
    public async Task ControlF6_PointsTheFocusedPanelAtTheGitDrive()
    {
        var harness = await BuildAsync();
        WithPanels(harness);

        await harness.Root.HandleKeyEventAsync(new KeyEvent('\0', ConsoleKey.F6, false, false, true));

        harness.ContentModelService.Verify(s => s.SetUrlAsync("LeftPanel", It.Is<Url>(url => url.ContainerType == ContainerType.Git)), Times.Once);
    }

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
}
