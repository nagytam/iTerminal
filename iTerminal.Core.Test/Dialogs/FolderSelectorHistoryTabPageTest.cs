using iConsole.Data;
using iTerminal.Core.Dialogs;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FolderSelectorHistoryTabPageTest
{
    private static readonly Size PageSize = new(60, 20);

    private static async Task<(FolderSelectorHistoryTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync(
        params string[] visitedFolders)
    {
        var harness = new FolderSelectorTestHarness();
        foreach (var folder in visitedFolders)
        {
            harness.FolderHistoryService.Add(new Url(folder));
        }
        var page = new FolderSelectorHistoryTabPage(
            harness.FolderHistoryService, harness.ContentModelService, harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        page.OnIdle();
        return (page, harness);
    }

    [TestMethod]
    public async Task Enter_GoesBackToTheFolderUnderTheCursor()
    {
        // The history lists newest first, so the folder visited last is row zero.
        var (page, harness) = await CreatePageAsync(@"C:\first", @"C:\second");
        page.SetFocus();

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(@"C:\second").FullUrl }, harness.Navigations);
    }

    [TestMethod]
    public async Task Down_ThenEnter_GoesToTheOlderFolder()
    {
        var (page, harness) = await CreatePageAsync(@"C:\first", @"C:\second");
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.DownArrow));
        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { new Url(@"C:\first").FullUrl }, harness.Navigations);
    }

    [TestMethod]
    public async Task Enter_WithAnEmptyHistory_NavigatesNowhere()
    {
        // The cursor sits at row zero whether or not there is a row zero; indexing the history
        // straight from it threw here rather than doing nothing.
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [TestMethod]
    public async Task OnInitialize_FocusesTheList()
    {
        var (page, _) = await CreatePageAsync(@"C:\first");

        page.SetFocus();

        Assert.IsInstanceOfType<iConsole.Controls.ListBox<string>>(page.GetFocusedControl());
    }

    [TestMethod]
    public void Constructor_WithoutAHistoryService_StillWorks()
    {
        var harness = new FolderSelectorTestHarness();

        var page = new FolderSelectorHistoryTabPage(null!, harness.ContentModelService, harness.ThemeService);

        Assert.AreEqual(0, (int)page.ScrollableModel.CursorPosition.Y);
    }

    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorHistoryTabPage(
            harness.FolderHistoryService, harness.ContentModelService, harness.ThemeService)
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
        var (page, _) = await CreatePageAsync(@"C:\first");

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
        Assert.IsTrue(hints.Any(h => h.Key == "Esc"));
    }
}
