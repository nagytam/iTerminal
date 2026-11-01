using iConsole.Data;
using iTerminal.Core.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FolderSelectorDrivesTabPageTest
{
    private static readonly Size PageSize = new(60, 20);

    private static async Task<(FolderSelectorDrivesTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync(
        params Drive[] drives)
    {
        var harness = new FolderSelectorTestHarness();
        foreach (var drive in drives)
        {
            harness.DriveInfoModel.Drives.Add(drive);
        }
        var page = new FolderSelectorDrivesTabPage(
            harness.DriveInfoModel, harness.ContentModelService, harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        page.OnIdle();
        return (page, harness);
    }

    /// <summary>
    /// A drive with no <see cref="DriveInfo"/>. Only the memory drive is enabled without one, which
    /// makes it the one virtual drive a test can build without touching the machine's volumes.
    /// </summary>
    private static Drive MemoryDrive() => new()
    {
        DriveInfo = null,
        Name = Container.MEMORY_DRIVE,
        Url = Container.MEMORY_DRIVE
    };

    /// <summary>
    /// A drive that is listed but cannot be opened. Named under the temp folder rather than as a
    /// drive letter: <see cref="Url"/> probes the file system as it parses, and an unmapped letter
    /// costs a network timeout before the test even starts.
    /// </summary>
    private static Drive UnreadyDrive(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), name);
        return new Drive
        {
            DriveInfo = null,
            Name = path,
            Url = new Url(path)
        };
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SelectedDrive_FollowsTheCursorNotAPreviousEnter()
    {
        // ListBox.SelectedItem only reports a row whose IsSelected flag was set, which nothing does
        // until Enter has already been pressed once — so a page read that way answers null the first
        // time it matters.
        var (page, _) = await CreatePageAsync(MemoryDrive());

        Assert.IsNotNull(page.SelectedDrive);
        Assert.AreEqual(Container.MEMORY_DRIVE, page.SelectedDrive!.Name);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SelectedDrive_WithNoDrives_IsNull()
    {
        var (page, _) = await CreatePageAsync();

        Assert.IsNull(page.SelectedDrive);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_GoesToTheDriveUnderTheCursor()
    {
        var (page, harness) = await CreatePageAsync(MemoryDrive());
        page.SetFocus();

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { Container.MEMORY_DRIVE }, harness.Navigations);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnADriveThatIsNotReady_NavigatesNowhere()
    {
        var (page, harness) = await CreatePageAsync(UnreadyDrive("unready-z"));
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithNoDrives_NavigatesNowhere()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Down_MovesTheCursor()
    {
        var (page, _) = await CreatePageAsync(MemoryDrive(), UnreadyDrive("unready-y"), UnreadyDrive("unready-z"));
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow));

        Assert.AreEqual(1, (int)page.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ListsReadyDrivesBeforeTheRest()
    {
        var (page, _) = await CreatePageAsync(UnreadyDrive("unready-z"), MemoryDrive());

        Assert.AreEqual(Container.MEMORY_DRIVE, page.SelectedDrive!.Name,
            "The cursor starts at row zero, which should be a drive that can actually be opened.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_WithoutADriveModel_StillWorks()
    {
        var harness = new FolderSelectorTestHarness();

        var page = new FolderSelectorDrivesTabPage(null!, harness.ContentModelService, harness.ThemeService);

        Assert.IsNull(page.SelectedDrive);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorDrivesTabPage(
            harness.DriveInfoModel, harness.ContentModelService, harness.ThemeService)
        {
            ViewSize = new Size(1, 1)
        };

        page.OnInitializeLayout();

        foreach (var control in page.Controls.AsSnapshot())
        {
            Assert.IsTrue(control.ViewSize.Width >= 0 && control.ViewSize.Height >= 0);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetKeyHints_DocumentEnterAndEscape()
    {
        var (page, _) = await CreatePageAsync(MemoryDrive());

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
        Assert.IsTrue(hints.Any(h => h.Key == "Esc"));
    }
}
