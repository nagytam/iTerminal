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

    [Timeout(60_000)]
    [TestMethod]
    public async Task Lists_TheOperatingSystemsOwnFolders()
    {
        var (page, _) = await CreatePageAsync();

        Assert.IsTrue(page.Places.Contains(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.IsTrue(page.Places.Contains(Path.GetTempPath()));
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingALetter_JumpsToThePlaceStartingWithIt()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();
        var memoryRow = page.Places.ToList().IndexOf(Container.MEMORY_DRIVE);
        Assert.IsTrue(memoryRow > 0);

        // Typed quickly, the letters build one prefix ("mem") instead of each starting a new search.
        var handled = false;
        handled |= await page.HandleKeyEventAsync(new KeyEvent('m', ConsoleKey.M, false, false, false));
        handled |= await page.HandleKeyEventAsync(new KeyEvent('e', ConsoleKey.E, false, false, false));
        handled |= await page.HandleKeyEventAsync(new KeyEvent('m', ConsoleKey.M, false, false, false));
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { Container.MEMORY_DRIVE }, harness.Navigations);
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitialize_FocusesTheList()
    {
        var (page, _) = await CreatePageAsync();

        page.SetFocus();

        Assert.IsInstanceOfType<iConsole.Controls.ListBox<string>>(page.GetFocusedControl());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitialize_Twice_DoesNotDoubleTheList()
    {
        var (page, _) = await CreatePageAsync();
        var firstCount = page.Places.Count;

        await page.OnInitializeAsync();

        Assert.AreEqual(firstCount, page.Places.Count);
    }

    [Timeout(60_000)]
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

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetKeyHints_DocumentEnterAndEscape()
    {
        var (page, _) = await CreatePageAsync();

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
        Assert.IsTrue(hints.Any(h => h.Key == "Esc"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Label_ShowsTheFolderNameBeforeThePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "Downloads");

        Assert.AreEqual("Downloads  " + path, FolderSelectorPlaceText.Label(path));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Label_LeavesARootAndAnAppDriveAsTheyAre()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        Assert.AreEqual(root, FolderSelectorPlaceText.Label(root));
        Assert.AreEqual(Container.MUSIC_CATALOG, FolderSelectorPlaceText.Label(Container.MUSIC_CATALOG));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TypingALetter_JumpsToAPlaceWhoseFolderNameStartsWithIt()
    {
        // Type-ahead matches the row's label, which leads with the folder name, so a letter finds a place by its name.
        var (page, _) = await CreatePageAsync();
        var letter = FolderSelectorPlaceText.Label(page.Places[0])[0];
        page.SetFocus();

        await page.HandleKeyEventAsync(new KeyEvent(letter, (ConsoleKey)char.ToUpperInvariant(letter), false, false, false));

        var cursor = (int)page.ScrollableModel.CursorPosition.Y;
        StringAssert.StartsWith(FolderSelectorPlaceText.Label(page.Places[cursor]), letter.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Label_OfAFolderWithATrailingSeparator_StillLeadsWithTheName()
    {
        var place = Path.Combine(Path.GetTempPath(), "Downloads") + Path.DirectorySeparatorChar;

        StringAssert.StartsWith(FolderSelectorPlaceText.Label(place), "Downloads  ");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Label_OfAFileUrl_NamesTheLocalPathItPoints_To()
    {
        Assert.AreEqual("Projects  /tmp/Projects", FolderSelectorPlaceText.Label("file:///tmp/Projects"));
        Assert.AreEqual("My Docs  /tmp/My Docs", FolderSelectorPlaceText.Label("file:///tmp/My%20Docs"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Label_OfAWindowsFileUrl_UsesTheWindowsPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows drive and UNC paths only exist on Windows.");
        }
        Assert.AreEqual(@"Projects  C:\Users\Projects", FolderSelectorPlaceText.Label("file:///C:/Users/Projects"));
        Assert.AreEqual(@"alpha  \\fileserver\projects\alpha", FolderSelectorPlaceText.Label("file://fileserver/projects/alpha"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Label_OfAMalformedFileUrl_IsShownAsItIs()
    {
        Assert.AreEqual("file://%zz", FolderSelectorPlaceText.Label("file://%zz"));
    }
}
