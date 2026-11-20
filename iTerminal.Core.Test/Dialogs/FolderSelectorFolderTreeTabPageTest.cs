using iConsole.Data;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs;

[TestClass]
public class FolderSelectorFolderTreeTabPageTest
{
    private static readonly Size PageSize = new(60, 20);

    private static async Task<(FolderSelectorFolderTreeTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync(
        params string[] folders)
    {
        var harness = new FolderSelectorTestHarness();
        foreach (var folder in folders)
        {
            harness.FolderIndexModel.Children.Add(new Folder(harness.FolderIndexModel, folder));
        }
        var page = new FolderSelectorFolderTreeTabPage(
            harness.ContentModelService,
            harness.FolderIndexModel,
            harness.FrequentExecutionGuardService,
            harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        return (page, harness);
    }

    /// <summary>Moves the tree's focus onto the named child of the root.</summary>
    private static void FocusChild(FolderIndexModel folderIndexModel, string name)
    {
        folderIndexModel.IsFocused = false;
        foreach (var child in folderIndexModel.Children.AsSnapshot())
        {
            child.IsFocused = child.Name == name;
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_GoesToTheFolderUnderTheCursor()
    {
        var (page, harness) = await CreatePageAsync(@"C:\projects");
        FocusChild(harness.FolderIndexModel, @"C:\projects");
        page.SetFocus();

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(@"C:\projects").FullUrl }, harness.Navigations);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_OnTheTreeRoot_NavigatesNowhere()
    {
        // "My Computer" is the tree's own root, not a folder any panel can be pointed at.
        var (page, harness) = await CreatePageAsync(@"C:\projects");
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Constructor_WithoutAFolderIndex_StillWorks()
    {
        var harness = new FolderSelectorTestHarness();

        var page = new FolderSelectorFolderTreeTabPage(
            harness.ContentModelService, null!, harness.FrequentExecutionGuardService, harness.ThemeService);

        Assert.AreEqual("4-Folder Tree", page.Name);
        Assert.AreEqual(ConsoleKey.D4, page.HotConsoleKey);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorFolderTreeTabPage(
            harness.ContentModelService,
            harness.FolderIndexModel,
            harness.FrequentExecutionGuardService,
            harness.ThemeService)
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
    public async Task Label_WhileTheWalkHasWorkQueued_SaysItIsIndexing()
    {
        var (page, harness) = await CreatePageAsync(@"C:\projects");
        harness.FolderIndexModel.IsWalking = true;
        page.OnInitializeLayout();

        Render(page);
        ScreenBuffer again = Render(page);

        AssertScreenContains(again, "Indexing...");
        AssertScreenDoesNotContain(again, "Number of folders:");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Label_WhenTheWalkHasCaughtUp_SaysTheNumberOfFolders()
    {
        var (page, _) = await CreatePageAsync(@"C:\projects");
        page.OnInitializeLayout();

        Render(page);
        ScreenBuffer again = Render(page);

        AssertScreenContains(again, "Number of folders:");
        AssertScreenDoesNotContain(again, "Indexing...");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Label_ChangesToTheCountWhenTheWalkCatchesUp()
    {
        var (page, harness) = await CreatePageAsync(@"C:\projects");
        harness.FolderIndexModel.IsWalking = true;
        page.OnInitializeLayout();
        AssertScreenContains(Render(page), "Indexing...");

        harness.FolderIndexModel.IsWalking = false;

        ScreenBuffer screen = Render(page);
        AssertScreenContains(screen, "Number of folders:");
        AssertScreenDoesNotContain(screen, "Indexing...");
    }
}
