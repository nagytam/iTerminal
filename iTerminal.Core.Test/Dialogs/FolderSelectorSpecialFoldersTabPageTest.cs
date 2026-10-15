using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FolderSelectorSpecialFoldersTabPageTest
{
    private static readonly Size PageSize = new(60, 20);

    private static async Task<(FolderSelectorSpecialFoldersTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorSpecialFoldersTabPage(harness.ContentModelService, harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        page.OnIdle();
        return (page, harness);
    }

    [TestMethod]
    public async Task Lists_TheOperatingSystemsOwnFolders()
    {
        var (page, _) = await CreatePageAsync();

        Assert.IsTrue(page.Places.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.IsTrue(page.Places.Contains(Path.GetTempPath()));
    }

    [TestMethod]
    public async Task Lists_TheDrivesThatOnlyExistInsideTheApp()
    {
        // Without these, the memory drive and the music drives are reachable only by someone who
        // already knows their URL scheme by heart.
        var (page, _) = await CreatePageAsync();

        CollectionAssert.IsSubsetOf(
            new[] { Container.MEMORY_DRIVE, Container.MUSIC_CATALOG, Container.MUSIC_PLAYLIST },
            page.Places.ToArray());
    }

    [TestMethod]
    public async Task Lists_NoBlankAndNoDuplicateRows()
    {
        // GetFolderPath answers with an empty string for a folder the platform does not have, and
        // several of them collapse onto one path on a stock install.
        var (page, _) = await CreatePageAsync();

        Assert.IsFalse(page.Places.Any(string.IsNullOrWhiteSpace));
        Assert.AreEqual(
            page.Places.Count,
            page.Places.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [TestMethod]
    public async Task Enter_GoesToThePlaceUnderTheCursor()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();
        var expected = page.Places[0];

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(expected).FullUrl }, harness.Navigations);
    }

    [TestMethod]
    public async Task Down_ThenEnter_GoesToTheNextPlace()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();
        var expected = page.Places[1];

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow));
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { new Url(expected).FullUrl }, harness.Navigations);
    }

    [TestMethod]
    public async Task Enter_OnAVirtualDrive_KeepsItsScheme()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();
        var musicCatalogRow = page.Places.ToList().IndexOf(Container.MUSIC_CATALOG);
        Assert.IsTrue(musicCatalogRow >= 0);

        for (int i = 0; i < musicCatalogRow; i++)
        {
            await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow));
        }
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { Container.MUSIC_CATALOG }, harness.Navigations);
    }

    [TestMethod]
    public async Task OnInitialize_FocusesTheList()
    {
        var (page, _) = await CreatePageAsync();

        page.SetFocus();

        Assert.IsInstanceOfType<iConsole.Controls.ListBox<string>>(page.GetFocusedControl());
    }

    [TestMethod]
    public async Task OnInitialize_Twice_DoesNotDoubleTheList()
    {
        var (page, _) = await CreatePageAsync();
        var firstCount = page.Places.Count;

        await page.OnInitializeAsync();

        Assert.AreEqual(firstCount, page.Places.Count);
    }

    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorSpecialFoldersTabPage(harness.ContentModelService, harness.ThemeService)
        {
            ViewSize = new Size(1, 1)
        };

        page.OnInitializeLayout();

        foreach (var control in page.Controls.AsSnapshot())
        {
            Assert.IsTrue(control.ViewSize.Width >= 0 && control.ViewSize.Height >= 0);
        }
    }

    [TestMethod]
    public async Task GetKeyHints_DocumentEnterAndEscape()
    {
        var (page, _) = await CreatePageAsync();

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
        Assert.IsTrue(hints.Any(h => h.Key == "Esc"));
    }
}
