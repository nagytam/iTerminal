using iConsole.Data;
using iConsole.Services;
using iTerminal.Core.Dialogs.Encyclopedia;
using iTerminal.Core.Services.Encyclopedia;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Test.Services.Currency;
using iTerminal.Core.Test.Services.Encyclopedia;
using Moq;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Encyclopedia;

[TestClass]
[DoNotParallelize]
public class EncyclopediaDialogTest
{
    private sealed record Harness(
        EncyclopediaDialog Dialog,
        EncyclopediaTest.FakeEncyclopedia Provider,
        CurrencyTest.MemorySettings Settings,
        Mock<IFileLauncher> Launcher);

    private static Harness Create(EncyclopediaTest.FakeEncyclopedia? provider = null, CurrencyTest.MemorySettings? settings = null)
    {
        provider ??= new EncyclopediaTest.FakeEncyclopedia();
        settings ??= new CurrencyTest.MemorySettings();
        Mock<IFileLauncher> launcher = new();
        launcher.Setup(l => l.OpenUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(LaunchOutcome.Success);
        EncyclopediaDialog dialog = new(new EncyclopediaService(provider), settings, launcher.Object, Mock.Of<IBeepService>(), new DateTimeProviderService(), TestTheme());
        return new Harness(dialog, provider, settings, launcher);
    }

    /// <summary>Lets the request in flight finish and applies what it left; a search starts the first article, which is settled too.</summary>
    private static async Task SettleAsync(EncyclopediaDialog dialog, int rounds = 2)
    {
        for (int round = 0; round < rounds; round++)
        {
            await dialog.RequestTask;
            dialog.ApplyPendingForTest();
        }
    }

    private static async Task SearchAsync(Harness h, string term)
    {
        h.Dialog.QueryBox.Text = term;
        h.Dialog.StartSearch();
        await SettleAsync(h.Dialog);
    }

    private static Task KeyAsync(EncyclopediaDialog dialog, ConsoleKey key, char c = '\0') => dialog.HandleKeyEventAsync(Key(key, c));

    [TestInitialize]
    public void Prompt() => EncyclopediaDialog.CursorDelayMilliseconds = 0;

    [TestCleanup]
    public void Reset() => EncyclopediaDialog.CursorDelayMilliseconds = 250;

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASearchFillsTheResultsAndReadsTheFirstIntroduction()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        await SearchAsync(h, "tulip");

        Assert.AreEqual(2, h.Dialog.Results.ListModel.ListItems.Count);
        Assert.AreEqual("Tulip", h.Dialog.ArticleTitle);
        Assert.AreEqual("Intro Tulip", h.Dialog.ArticleText);
        Assert.IsFalse(h.Dialog.ArticleIsFull);
        string screen = Render(h.Dialog).ToString()!;
        StringAssert.Contains(screen, "Results (2)");
        StringAssert.Contains(screen, "Tulip mania");
        StringAssert.Contains(screen, "Intro Tulip");
        StringAssert.Contains(h.Dialog.Status, "Wikipedia (English)");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnTheTermSearches()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        Assert.IsTrue(h.Dialog.QueryBox.IsFocused, "the dialog opens on the term");
        h.Dialog.QueryBox.Text = "tulip";

        Assert.IsTrue(await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter, '\r')));
        await SettleAsync(h.Dialog);

        Assert.AreEqual(1, h.Provider.SearchCalls);
        Assert.AreEqual("tulip", h.Provider.LastQuery);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABlankTermAsksForOne_AndDoesNotSearch()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        h.Dialog.QueryBox.Text = "  ";
        h.Dialog.StartSearch();

        Assert.AreEqual("Type a term to look up.", h.Dialog.Status);
        Assert.AreEqual(0, h.Provider.SearchCalls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NothingFoundIsSaid_AndTheOldArticleIsGone()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        h.Provider.Hits.Clear();
        await SearchAsync(h, "zzz");

        Assert.AreEqual("Nothing found for \"zzz\".", h.Dialog.Status);
        Assert.AreEqual(string.Empty, h.Dialog.ArticleTitle);
        Assert.AreEqual(0, h.Dialog.Results.ListModel.ListItems.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedSearchShowsItsReason()
    {
        Harness h = Create(new EncyclopediaTest.FakeEncyclopedia { Failure = new EncyclopediaException("Could not reach Wikipedia (English): no network") });
        await ShowAsync(h.Dialog);

        await SearchAsync(h, "tulip");

        StringAssert.Contains(h.Dialog.Status, "no network");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F3ReadsTheWholeArticleAndGoesBackToTheIntroduction()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        await KeyAsync(h.Dialog, ConsoleKey.F3);
        await SettleAsync(h.Dialog, 1);
        Assert.AreEqual("Whole Tulip", h.Dialog.ArticleText);
        Assert.IsTrue(h.Dialog.ArticleIsFull);
        StringAssert.Contains(h.Dialog.Status, "the whole article");

        await KeyAsync(h.Dialog, ConsoleKey.F3);
        await SettleAsync(h.Dialog, 1);
        Assert.AreEqual("Intro Tulip", h.Dialog.ArticleText);
        Assert.IsFalse(h.Dialog.ArticleIsFull);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnTheResultsReadsTheWholeArticleUnderTheCursor()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");
        h.Dialog.Results.SetFocus();
        h.Dialog.Results.ScrollableModel.CursorPosition.Y = 1;

        await KeyAsync(h.Dialog, ConsoleKey.Enter, '\r');
        await SettleAsync(h.Dialog, 1);

        Assert.AreEqual("Whole Tulip mania", h.Dialog.ArticleText);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheCursorMovingOverTheResultsReadsEachIntroductionOnceItRests()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");
        int before = h.Provider.ArticleCalls;

        h.Dialog.Results.ScrollableModel.CursorPosition.Y = 1;
        h.Dialog.ApplyPendingForTest(); // sees the cursor move
        h.Dialog.ApplyPendingForTest(); // and that it has rested
        await SettleAsync(h.Dialog, 1);

        Assert.AreEqual("Tulip mania", h.Dialog.ArticleTitle);
        Assert.AreEqual(before + 1, h.Provider.ArticleCalls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ACursorStillMovingDoesNotFetchAnything()
    {
        EncyclopediaDialog.CursorDelayMilliseconds = 60_000;
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");
        int before = h.Provider.ArticleCalls;

        h.Dialog.Results.ScrollableModel.CursorPosition.Y = 1;
        h.Dialog.ApplyPendingForTest();
        h.Dialog.ApplyPendingForTest();

        Assert.AreEqual(before, h.Provider.ArticleCalls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F2OpensTheArticleInTheBrowser()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        await KeyAsync(h.Dialog, ConsoleKey.F2);
        await SettleAsync(h.Dialog, 1);

        h.Launcher.Verify(l => l.OpenUrlAsync("https://en.wikipedia.org/wiki/Tulip", It.IsAny<CancellationToken>()), Times.Once);
        StringAssert.Contains(h.Dialog.Status, "Opened https://en.wikipedia.org/wiki/Tulip in the browser");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task F2WithNothingToOpenSaysSo()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        await KeyAsync(h.Dialog, ConsoleKey.F2);

        Assert.AreEqual("There is no article to open yet.", h.Dialog.Status);
        h.Launcher.Verify(l => l.OpenUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ABrowserThatCannotBeStartedIsReported()
    {
        Harness h = Create();
        h.Launcher.Setup(l => l.OpenUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(LaunchOutcome.Failed("xdg-open is not installed"));
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        await KeyAsync(h.Dialog, ConsoleKey.F2);
        await SettleAsync(h.Dialog, 1);

        Assert.AreEqual("xdg-open is not installed", h.Dialog.Status);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingTheSourceLooksTheSameTermUpThere()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        await SearchAsync(h, "tulip");

        h.Dialog.SourceBox.SelectedIndex = 1;
        await SettleAsync(h.Dialog);

        Assert.AreEqual(2, h.Provider.SearchCalls);
        Assert.AreEqual("wikipedia-de", h.Provider.LastSource!.Id);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ChangingTheSourceWithNoTermSearchesNothing()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        h.Dialog.SourceBox.SelectedIndex = 1;

        Assert.AreEqual(0, h.Provider.SearchCalls);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BritannicaIsOpenedInTheBrowserNotRead()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        h.Dialog.SourceBox.SelectedIndex = h.Dialog.SourceBox.Items.ToList().FindIndex(s => s.Id == "britannica");

        await SearchAsync(h, "tulip");

        Assert.AreEqual(0, h.Provider.SearchCalls + h.Provider.ArticleCalls, "no program may ask Britannica");
        StringAssert.Contains(h.Dialog.ArticleText, "Press F2");
        await KeyAsync(h.Dialog, ConsoleKey.F2);
        await SettleAsync(h.Dialog, 1);
        h.Launcher.Verify(l => l.OpenUrlAsync("https://www.britannica.com/search?query=tulip", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnOutdatedAnswerIsDropped()
    {
        TaskCompletionSource gate = new();
        Harness h = Create(new EncyclopediaTest.FakeEncyclopedia { Gate = gate.Task });
        await ShowAsync(h.Dialog);
        h.Dialog.QueryBox.Text = "first";
        h.Dialog.StartSearch();
        Task first = h.Dialog.RequestTask;

        h.Provider.Gate = null;
        h.Dialog.QueryBox.Text = "second";
        h.Dialog.StartSearch();
        await h.Dialog.RequestTask;
        gate.SetResult();
        try
        {
            await first;
        }
        catch (OperationCanceledException)
        {
        }

        await SettleAsync(h.Dialog);

        Assert.AreEqual("second", h.Provider.LastQuery);
        Assert.AreEqual(2, h.Dialog.Results.ListModel.ListItems.Count, "the second search's results");
        Assert.IsFalse(h.Dialog.Status.Contains("Cancelled", StringComparison.Ordinal), "not the first one's cancellation");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ASearchThatIsCancelledLateDoesNotOverwriteTheNewerResults()
    {
        // The first search only reports its cancellation after the second has answered, so its outcome lands last.
        TaskCompletionSource gate = new();
        Harness h = Create(new EncyclopediaTest.FakeEncyclopedia { Gate = gate.Task, IgnoresCancellation = true });
        await ShowAsync(h.Dialog);
        h.Dialog.QueryBox.Text = "first";
        h.Dialog.StartSearch();
        Task first = h.Dialog.RequestTask;
        await WaitUntilAsync(() => h.Provider.SearchCalls == 1, "the first search to reach the provider");

        h.Provider.Gate = null;
        h.Dialog.QueryBox.Text = "second";
        h.Dialog.StartSearch();
        await h.Dialog.RequestTask;
        gate.SetResult();
        try
        {
            await first;
        }
        catch (OperationCanceledException)
        {
        }

        await SettleAsync(h.Dialog);

        Assert.AreEqual(2, h.Dialog.Results.ListModel.ListItems.Count, "the second search's results survive the first one's late cancellation");
        Assert.IsFalse(h.Dialog.Status.Contains("Cancelled", StringComparison.Ordinal));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task EnterOnAnEmptyResultListSaysThereIsNothingToReadYet()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        h.Dialog.Results.SetFocus();

        Assert.IsTrue(await h.Dialog.HandleKeyEventAsync(Key(ConsoleKey.Enter)));

        StringAssert.Contains(h.Dialog.Status, "Search for a term first");
        Assert.AreEqual(0, h.Provider.ArticleCalls, "nothing is fetched for a result that is not there");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheSourceAndTheTermAreRememberedWhenTheDialogCloses_AndComeBackWhenItOpens()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);
        h.Dialog.QueryBox.Text = "tulip";
        h.Dialog.SourceBox.SelectedIndex = 7;

        h.Dialog.Close();

        Assert.AreEqual("wiktionary-en", h.Settings.Get(EncyclopediaDialog.SourceKey));
        Assert.AreEqual("tulip", h.Settings.Get(EncyclopediaDialog.QueryKey));

        Harness again = Create(settings: h.Settings);
        await ShowAsync(again.Dialog);
        Assert.AreEqual("tulip", again.Dialog.QueryBox.Text);
        Assert.AreEqual("wiktionary-en", again.Dialog.SourceBox.SelectedItem!.Id);
        Assert.AreEqual(0, again.Provider.SearchCalls, "opening does not search by itself");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnknownRememberedSourceFallsBackToTheFirst()
    {
        CurrencyTest.MemorySettings settings = new();
        settings.Set(EncyclopediaDialog.SourceKey, "gone");
        Harness h = Create(settings: settings);

        await ShowAsync(h.Dialog);

        Assert.AreEqual("wikipedia-en", h.Dialog.SourceBox.SelectedItem!.Id);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheKeyHintsNameEnterF3AndF2()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        string[] keys = [.. h.Dialog.GetKeyHints().Select(hint => hint.Key)];

        CollectionAssert.IsSubsetOf(new[] { "Enter", "F3", "F2" }, keys);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheFooterShowsTheKeys()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog);

        StringAssert.Contains(Render(h.Dialog).ToString()!, "F3:whole");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnAnEightyColumnScreen_TheSourceNameAndEveryKeyHintFit()
    {
        Harness h = Create();
        await ShowAsync(h.Dialog, new Size(80, 24));

        string screen = Render(h.Dialog, new Size(80, 24)).ToString()!;

        // The box showed "Wikipedia (Engli" and the footer dropped F2 at this width.
        StringAssert.Contains(screen, "Wikipedia (English)");
        StringAssert.Contains(screen, "F2:browser");
        StringAssert.Contains(screen, "Esc:close");
    }
}
