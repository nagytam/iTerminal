using iConsole.Data;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Model.Validations;

namespace iTerminal.Core.Test.Dialogs;

/// <summary>
/// The Path page — the one place in the selector that reaches every drive kind the app has, because
/// it hands the text straight to <see cref="Url"/> instead of knowing about schemes itself.
/// </summary>
[TestClass]
public class FolderSelectorPathTabPageTest
{
    private static readonly Size PageSize = new(80, 20);

    private static async Task<(FolderSelectorPathTabPage page, FolderSelectorTestHarness harness)> CreatePageAsync(
        string? path = null)
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorPathTabPage(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        if (path is not null)
        {
            Type(page, path);
        }
        return (page, harness);
    }

    /// <summary>
    /// Types into the box and settles the validator. The text change also kicks the validator off on
    /// a background task, but nothing here waits for it: a wait is a race the test loses on a busy
    /// machine, and the verdict is a pure function of the text. The stamp is pushed a minute ahead so
    /// the background pass — which computes the same answer from the same text — cannot land after
    /// this one and be mistaken for a later result.
    /// </summary>
    private static void Type(FolderSelectorPathTabPage page, string path)
    {
        page.TextBoxPath.TextModel.Lines[0] = path;
        page.TextBoxPath.TextModel.TriggerPropertyChanged();
        page.PathValidator.Validate(DateTime.Now.AddMinutes(1));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithALocalPath_GoesThere()
    {
        var tempPath = Path.GetTempPath();
        var (page, harness) = await CreatePageAsync(tempPath);
        page.SetFocus();

        var handled = await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsTrue(handled);
        CollectionAssert.AreEqual(new[] { new Url(tempPath).FullUrl }, harness.Navigations);
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow("ssh://tamas@example.com:/home/tamas/")]
    [DataRow("smb://server/share/folder/")]
    [DataRow("iphone://0123456789abcdef/DCIM/")]
    [DataRow("memdrive://")]
    [DataRow("musiccatalog://")]
    [DataRow("musicplaylist://")]
    [DataRow("zip://archive.zip@/")]
    [DataRow("ftp://ftp.example.com/pub/")]
    public async Task Enter_ReachesEveryDriveKindTheAppHas(string url)
    {
        // The other pages between them cover local drives, favourites, history and SSH; everything
        // else in Container has no page of its own and would be unreachable without this one.
        var (page, harness) = await CreatePageAsync(url);
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        CollectionAssert.AreEqual(new[] { url }, harness.Navigations);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithAnEmptyBox_GoesNowhere()
    {
        var (page, harness) = await CreatePageAsync();
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithAFolderThatDoesNotExist_GoesNowhere()
    {
        var (page, harness) = await CreatePageAsync();
        Type(page, Path.Combine(Path.GetTempPath(), "no-such-folder-" + Guid.NewGuid()));
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.IsFalse(page.PathValidator.IsValid);
        Assert.AreEqual(0, harness.Navigations.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_WithAnUnknownScheme_GoesNowhere()
    {
        var (page, harness) = await CreatePageAsync();
        Type(page, "gopher://example.com/");
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        Assert.AreEqual(0, harness.Navigations.Count);
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(page.PathValidator.ErrorMessage, "scheme");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Enter_RemembersThePathForTabCompletion()
    {
        var tempPath = Path.GetTempPath();
        var (page, _) = await CreatePageAsync(tempPath);
        page.SetFocus();

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Enter));

        var completions = page.TextBoxPath.AutoCompletion?.CompletionModel.Completions.ToList();
        Assert.IsNotNull(completions);
        CollectionAssert.Contains(completions, tempPath.Trim());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TabCompletion_OffersTheFoldersThePanelsHaveVisited()
    {
        var harness = new FolderSelectorTestHarness();
        harness.FolderHistoryService.Add(new Url(@"C:\projects\crypton"));
        var page = new FolderSelectorPathTabPage(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();

        var completions = page.TextBoxPath.AutoCompletion?.CompletionModel.Completions.ToList();

        Assert.IsNotNull(completions);
        Assert.IsTrue(completions!.Any(c => c.Contains("crypton")),
            "A folder the panels have been in should complete without being typed here first.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Tab_CompletesTheTypedPrefix()
    {
        var harness = new FolderSelectorTestHarness();
        harness.FolderHistoryService.Add(new Url(@"C:\projects\crypton"));
        var page = new FolderSelectorPathTabPage(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
            harness.ThemeService)
        {
            ViewSize = PageSize
        };
        await page.OnInitializeAsync();
        page.SetFocus();
        Type(page, "crypton");

        await page.HandleKeyEventAsync(FolderSelectorTestHarness.Key(ConsoleKey.Tab));

        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(page.Path, "crypton");
        Assert.AreNotEqual("crypton", page.Path, "Tab should have expanded the prefix to the full path.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void GetSchemesHint_ListsEverySchemeTheValidatorAccepts()
    {
        var hint = FolderSelectorPathTabPage.GetSchemesHint();

        foreach (var scheme in FolderUrlValidationModel.SupportedSchemes)
        {
            Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(hint, scheme);
        }
        Microsoft.VisualStudio.TestTools.UnitTesting.StringAssert.Contains(hint, "Tab completes");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetKeyHints_NameTabCompletionAndTheSchemes()
    {
        var (page, _) = await CreatePageAsync();

        var hints = page.GetKeyHints().ToList();

        Assert.IsTrue(hints.Any(h => h.Key == "Tab"));
        Assert.IsTrue(hints.Any(h => h.Key == "Enter"));
        Assert.IsTrue(hints.Any(h => h.Description.Contains(Container.SMB)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnInitializeLayout_InANarrowPage_DoesNotProduceNegativeGeometry()
    {
        var harness = new FolderSelectorTestHarness();
        var page = new FolderSelectorPathTabPage(
            harness.ContentModelService,
            harness.FolderHistoryService,
            harness.BeepService,
            harness.DateTimeProviderService,
            harness.SettingsService,
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
}
