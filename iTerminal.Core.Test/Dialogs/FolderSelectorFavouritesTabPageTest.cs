using iConsole.Controls;
using iConsole.Data;
using ScreenBuffer = iConsole.Test.Core.ScreenBuffer;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using static iTerminal.Core.Test.Helper.UiTestHelper;

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

    [Timeout(60_000)]
    [TestMethod]
    public async Task CtrlD_AsksBeforeRemovingTheFavourite()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        Assert.IsTrue(page.IsAskingToDeleteForTest);
        Assert.AreEqual(2, page.Favourites.Count, "nothing is removed until y");
        StringAssert.Contains(page.ConfirmTextForTest, @"C:\one");
        StringAssert.Contains(page.ConfirmTextForTest, "y:yes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Y_RemovesTheFavouriteUnderTheCursor()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        await page.HandleKeyEventAsync(new KeyEvent('y', ConsoleKey.Y, false, false, false));

        CollectionAssert.AreEqual(new[] { new Url(@"C:\two").FullUrl }, page.Favourites.Select(f => f.FullUrl).ToArray());
        Assert.IsFalse(page.IsAskingToDeleteForTest);
        Assert.AreEqual(string.Empty, page.ConfirmTextForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task N_KeepsTheFavouriteAndDropsTheQuestion()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        await page.HandleKeyEventAsync(new KeyEvent('n', ConsoleKey.N, false, false, false));

        Assert.AreEqual(2, page.Favourites.Count);
        Assert.IsFalse(page.IsAskingToDeleteForTest);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterWhileAsking_DropsTheQuestionWithoutGoingThere()
    {
        var (page, harness) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(2, page.Favourites.Count);
        Assert.IsFalse(page.IsAskingToDeleteForTest);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ThePageDrawsTheQuestionAboveTheButtons()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.OnInitializeLayout();
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        Render(page);
        ScreenBuffer again = Render(page);

        AssertScreenContains(again, "Remove ");
        AssertScreenContains(again, "from the favourites?");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALongPath_StillLeavesTheNoAnswerOnTheStatusLine()
    {
        var longPath = @"C:\" + string.Concat(Enumerable.Repeat("folder-", 12));
        var (page, _) = await CreatePageAsync(longPath);
        page.OnInitializeLayout();
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        Render(page);
        ScreenBuffer again = Render(page);

        AssertScreenContains(again, "n:no");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Esc_DropsTheQuestionAndKeepsTheFavourite()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));
        ITransientOverlayHost host = page;

        Assert.IsTrue(host.IsOverlayOpen);
        host.CloseOverlay();

        Assert.IsFalse(host.IsOverlayOpen);
        Assert.AreEqual(2, page.Favourites.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ShiftY_AlsoAnswersYes()
    {
        var (page, _) = await CreatePageAsync(@"C:\one", @"C:\two");
        page.SetFocus();
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.D, control: true));

        await page.HandleKeyEventAsync(new KeyEvent('Y', ConsoleKey.Y, true, false, false));

        Assert.AreEqual(1, page.Favourites.Count);
        Assert.IsFalse(page.IsAskingToDeleteForTest);
    }
}
