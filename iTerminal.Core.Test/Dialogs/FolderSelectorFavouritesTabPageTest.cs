using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FolderSelectorFavouritesTabPageTest
{
    private static readonly Size PageSize = new(60, 20);

    private static async Task<(FolderSelectorFavouritesTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync(
        params string[] favourites)
    {
        var settingsService = new SettingsService();
        if (favourites.Length > 0)
        {
            settingsService.Set("Favourites", string.Join(';', favourites));
        }
        var harness = new FolderSelectorTestHarness(settingsService);
        var page = new FolderSelectorFavouritesTabPage(
            harness.ContentModelService, harness.SettingsService, harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        page.OnIdle();
        return (page, harness);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitialize_ReadsTheSavedFavourites()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");

        CollectionAssert.AreEqual(
            new[] { new Url(@"C:\one").FullUrl, new Url(@"C:\two").FullUrl },
            page.Favourites.Select(f => f.FullUrl).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitialize_FocusesTheList()
    {
        // Not the "Add to favourites" button: focus there answers the arrow keys with nothing and
        // turns Enter into the wrong verb.
        var (page, _) = await CreatePageAsync(@"C:\one");

        page.SetFocus();

        Assert.IsInstanceOfType<iConsole.Controls.ListBox<Url>>(page.GetFocusedControl());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Down_MovesTheCursor()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two", @"C:\three");
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow));

        Assert.AreEqual(1, (int)page.ScrollableModel.CursorPosition.Y);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_GoesToTheFavouriteUnderTheCursor()
    {
        var (page, harness) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow));

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(@"C:\two").FullUrl }, harness.Navigations);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithNothingListed_NavigatesNowhere()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetKeyHints_NameTheAddAndRemoveKeys()
    {
        var (page, _) = await CreatePageAsync(@"C:\one");

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Ctrl+A"));
        Assert.IsTrue(hints.Any(h => h.Key == "Ctrl+D"));
        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnInitializeLayout_KeepsEveryChildOnThePage()
    {
        var (page, _) = await CreatePageAsync(@"C:\one");

        foreach (var control in page.Controls.AsSnapshot())
        {
            Assert.IsTrue(control.Position.Y < PageSize.Height, $"{control.Name} is drawn below the page.");
            Assert.IsTrue(control.ViewSize.Width <= PageSize.Width, $"{control.Name} is wider than the page.");
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorFavouritesTabPage(
            harness.ContentModelService, harness.SettingsService, harness.ThemeService)
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
    public void Constructor_WithoutASettingsService_StillWorks()
    {
        var harness = new FolderSelectorTestHarness();

        var page = new FolderSelectorFavouritesTabPage(harness.ContentModelService, null!, harness.ThemeService);

        Assert.AreEqual(0, page.Favourites.Count);
    }
}
