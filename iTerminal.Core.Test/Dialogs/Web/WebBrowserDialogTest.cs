using iConsole.Controls;
using iConsole.Data;
using iConsole.Test.Core;
using iTerminal.Core.Dialogs.Web;
using iTerminal.Core.Model.Web;
using iTerminal.Core.Services.Web;
using iTerminal.Core.Test.Model.Web;
using static iTerminal.Core.Test.Helper.UiTestHelper;

namespace iTerminal.Core.Test.Dialogs.Web;

// Some tests put text on the ambient clipboard and paste it back, so the class does not run beside others that do.
[DoNotParallelize]
[TestClass]
public class WebBrowserDialogTest
{
    private sealed class Fixture
    {
        public required WebBrowserDialog Dialog { get; init; }
        public required Control Root { get; init; }
        public required FakeWebFetcher Fetcher { get; init; }
        public required WebBrowserStore Store { get; init; }
        public WebTab Tab => Dialog.Model.Active;
    }

    private static async Task<Fixture> OpenAsync(Action<FakeWebFetcher>? pages = null, Size? size = null)
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<title>Start page</title><h1>Welcome</h1><p>Plain words.</p><a href='/two'>second page</a> <a href='/long'>long page</a>");
        fetcher.Html("https://a.test/two", "<title>Second</title><h1>The second page</h1><a href='/'>home</a>");
        fetcher.Html("https://a.test/long", "<title>Long</title>" + string.Concat(Enumerable.Range(0, 120).Select(i => $"<p>line number {i}</p>")));
        fetcher.Html("https://a.test/form", "<title>Form</title><form action='/results' method='get'>Search: <input type='text' name='q' size='12'> <input type='checkbox' name='safe' checked> safe <input type='submit' value='Go'></form>");
        fetcher.Html("https://a.test/results", "<title>Results</title><h1>Results page</h1>");
        pages?.Invoke(fetcher);

        var store = new WebBrowserStore(new MemorySettings());
        var dialog = new WebBrowserDialog(fetcher, store, TestTheme());
        var root = await ShowAsync(dialog, size);
        await dialog.Model.Active.LoadTask;
        return new Fixture { Dialog = dialog, Root = root, Fetcher = fetcher, Store = store };
    }

    private static async Task PressAsync(Fixture f, ConsoleKey key, char c = '\0', bool shift = false, bool alt = false, bool control = false) =>
        await f.Dialog.HandleKeyEventAsync(Key(key, c, shift, alt, control));

    private static async Task TypeTextAsync(Fixture f, string text)
    {
        foreach (var c in text)
        {
            await f.Dialog.HandleKeyEventAsync(Char(c));
        }
    }

    private static async Task GoToAsync(Fixture f, string address)
    {
        await PressAsync(f, ConsoleKey.L, 'l', control: true);
        await TypeTextAsync(f, address);
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await f.Tab.LoadTask;
    }

    /// <summary>Draws, lets the layout the draw asked for finish, and draws again — the first draw only starts it.</summary>
    private static async Task<ScreenBuffer> DrawAsync(Fixture f, Size? size = null)
    {
        Render(f.Root, size);
        await f.Tab.LayoutTask;
        return Render(f.Root, size);
    }

    [TestMethod]
    public async Task OpensOnTheHomePage()
    {
        var f = await OpenAsync();

        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "Web Browser");
        AssertScreenContains(screen, "about:home");
        AssertScreenContains(screen, "Search the web");
        AssertScreenContains(screen, "Pages are shown as text");
    }

    [TestMethod]
    public async Task TheAddressBarNavigatesAndShowsThePage()
    {
        var f = await OpenAsync();

        await GoToAsync(f, "a.test");
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "https://a.test/");
        AssertScreenContains(screen, "Welcome");
        AssertScreenContains(screen, "second page");
        Assert.IsFalse(f.Dialog.IsTyping);
    }

    [TestMethod]
    public async Task TheTitleOfThePageIsShown()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await DrawAsync(f);

        f.Dialog.OnIdle();
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "Web Browser — Start page");
    }

    [TestMethod]
    public async Task EscapeInTheAddressBarCancelsTheEdit()
    {
        var f = await OpenAsync();
        await PressAsync(f, ConsoleKey.L, 'l', control: true);
        await TypeTextAsync(f, "nowhere");
        Assert.AreEqual("nowhere", f.Dialog.AddressText);

        await PressAsync(f, ConsoleKey.Escape);

        Assert.IsFalse(f.Dialog.IsTyping);
        Assert.AreEqual(1, f.Fetcher.Requests.Count, "only the home page was fetched");
    }

    [TestMethod]
    public async Task TabAndEnterFollowALink()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.Tab, '\t');
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await f.Tab.LoadTask;
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "The second page");
        AssertScreenDoesNotContain(screen, "Plain words", "the old page must not show through");
        Assert.AreEqual("https://a.test/two", f.Tab.Url);
    }

    [TestMethod]
    public async Task ShiftTabStartsFromTheLastLinkAndTheStatusLineNamesTheTarget()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.Tab, '\t', shift: true);
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "https://a.test/long");
    }

    [TestMethod]
    public async Task BackspaceAndAltRightWalkTheHistory()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await GoToAsync(f, "https://a.test/two");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.Backspace, '\b');
        var back = await DrawAsync(f);
        AssertScreenContains(back, "Welcome");

        await PressAsync(f, ConsoleKey.RightArrow, alt: true);
        var forward = await DrawAsync(f);
        AssertScreenContains(forward, "The second page");
    }

    [TestMethod]
    public async Task TypingIntoAFormFieldAndPressingEnterSubmitsIt()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/form");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.Tab, '\t');
        await PressAsync(f, ConsoleKey.Enter, '\r');
        Assert.IsTrue(f.Dialog.IsTyping);
        await TypeTextAsync(f, "cats");
        var typing = await DrawAsync(f);
        AssertScreenContains(typing, "[cats");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await f.Tab.LoadTask;
        var screen = await DrawAsync(f);

        Assert.AreEqual("https://a.test/results?q=cats&safe=on", f.Fetcher.Requests[^1].Url);
        AssertScreenContains(screen, "Results page");
    }

    [TestMethod]
    public async Task SpaceTogglesACheckboxAndTheButtonSubmits()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/form");
        await DrawAsync(f);
        await PressAsync(f, ConsoleKey.Tab, '\t');
        await PressAsync(f, ConsoleKey.Tab, '\t');

        await PressAsync(f, ConsoleKey.Spacebar, ' ');
        var screen = await DrawAsync(f);
        AssertScreenContains(screen, "[ ] safe");

        await PressAsync(f, ConsoleKey.Tab, '\t');
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await f.Tab.LoadTask;

        Assert.AreEqual("https://a.test/results?q=", f.Fetcher.Requests[^1].Url);
    }

    [TestMethod]
    public async Task ScrollingKeysMoveThroughALongPage()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/long");
        var top = await DrawAsync(f);
        AssertScreenContains(top, "line number 0");

        await PressAsync(f, ConsoleKey.PageDown);
        await PressAsync(f, ConsoleKey.PageDown);
        var middle = await DrawAsync(f);
        AssertScreenDoesNotContain(middle, "line number 0");

        await PressAsync(f, ConsoleKey.End);
        var bottom = await DrawAsync(f);
        AssertScreenContains(bottom, "line number 119");
        AssertScreenContains(bottom, "100%");

        await PressAsync(f, ConsoleKey.Home);
        var home = await DrawAsync(f);
        AssertScreenContains(home, "line number 0");
    }

    [TestMethod]
    public async Task TheMouseWheelScrollsAndAClickFollowsALink()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/long");
        await DrawAsync(f);

        await f.Dialog.HandleMouseEventAsync(new MouseEvent { WheelDelta = -1, Position = new Position(5, 10) });
        Assert.AreEqual(3, f.Tab.Scroll);

        await GoToAsync(f, "a.test");
        var screen = await DrawAsync(f);
        var row = Enumerable.Range(0, 40).First(y => Enumerable.Range(0, 118).Select(x => screen.Cell(x, y).Char).Aggregate("", (a, c) => a + c).Contains("second page"));
        var column = string.Concat(Enumerable.Range(0, 118).Select(x => screen.Cell(x, row).Char)).IndexOf("second page", StringComparison.Ordinal);
        await f.Dialog.HandleMouseEventAsync(new MouseEvent { LeftClicked = true, Position = new Position(column + 2, row) });
        await f.Tab.LoadTask;

        Assert.AreEqual("https://a.test/two", f.Tab.Url);
    }

    [TestMethod]
    public async Task FindJumpsToTheMatchAndMarksIt()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/long");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.F, 'f', control: true);
        await TypeTextAsync(f, "number 77");
        var prompt = await DrawAsync(f);
        AssertScreenContains(prompt, "Find: number 77");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "line number 77");
        Assert.IsTrue(f.Tab.Scroll > 50);
        var cell = Enumerable.Range(0, 40).SelectMany(y => Enumerable.Range(0, 118).Select(x => (x, y))).First(p => screen.Cell(p.x, p.y).Char == 'n' && screen.Cell(p.x + 7, p.y).Char == '7' && screen.Cell(p.x + 8, p.y).Char == '7');
        Assert.AreEqual(iConsole.Display.Color.Yellow, screen.Cell(cell.x, cell.y).BackgroundColor);

        await PressAsync(f, ConsoleKey.F3);
        Assert.AreNotEqual(0, f.Tab.Scroll);
    }

    [TestMethod]
    public async Task FindReportsWhenThereIsNoMatch()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.F, 'f', control: true);
        await TypeTextAsync(f, "zebra");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "“zebra” was not found.");
    }

    [TestMethod]
    public async Task TabsCanBeOpenedSwitchedAndClosed()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");

        await PressAsync(f, ConsoleKey.T, 't', control: true);
        await f.Tab.LoadTask;
        Assert.AreEqual(2, f.Dialog.Model.Tabs.Count);
        await PressAsync(f, ConsoleKey.Escape);
        await GoToAsync(f, "https://a.test/two");
        var screen = await DrawAsync(f);
        AssertScreenContains(screen, " 1 Start page");
        AssertScreenContains(screen, " 2 Second");

        await PressAsync(f, ConsoleKey.D1, '1', alt: true);
        Assert.AreEqual(0, f.Dialog.Model.ActiveIndex);
        await PressAsync(f, ConsoleKey.PageDown, control: true);
        Assert.AreEqual(1, f.Dialog.Model.ActiveIndex);
        await PressAsync(f, ConsoleKey.W, 'w', control: true);
        Assert.AreEqual(1, f.Dialog.Model.Tabs.Count);
        Assert.AreEqual("Start page", f.Tab.Title);
    }

    [TestMethod]
    public async Task ControlEnterOpensALinkInANewTab()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await DrawAsync(f);
        await PressAsync(f, ConsoleKey.Tab, '\t');

        await PressAsync(f, ConsoleKey.Enter, '\r', control: true);
        await f.Tab.LoadTask;

        Assert.AreEqual(2, f.Dialog.Model.Tabs.Count);
        Assert.AreEqual("https://a.test/two", f.Tab.Url);
        Assert.AreEqual("https://a.test/", f.Dialog.Model.Tabs[0].Url);
    }

    [TestMethod]
    public async Task BookmarksCanBeAddedListedOpenedAndRemoved()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/two");
        await PressAsync(f, ConsoleKey.D, 'd', control: true);
        Assert.IsTrue(f.Store.IsBookmarked("https://a.test/two"));
        await GoToAsync(f, "a.test");

        await PressAsync(f, ConsoleKey.F2);
        var list = await DrawAsync(f);
        AssertScreenContains(list, "Bookmarks (1)");
        AssertScreenContains(list, "Second");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        await f.Tab.LoadTask;
        Assert.AreEqual("https://a.test/two", f.Tab.Url);

        await PressAsync(f, ConsoleKey.F2);
        await PressAsync(f, ConsoleKey.Delete);
        Assert.IsTrue(f.Dialog.ConfirmingRemoveBookmark);
        Assert.IsTrue(f.Store.IsBookmarked("https://a.test/two"), "Delete only asks");
        AssertScreenContains(await DrawAsync(f), "Remove the bookmark \"Second\"? (y/n)");
        await PressAsync(f, ConsoleKey.Y, 'y');
        var empty = await DrawAsync(f);
        AssertScreenContains(empty, "No bookmarks yet");
        Assert.IsFalse(f.Store.IsBookmarked("https://a.test/two"));
        await PressAsync(f, ConsoleKey.Escape);
        Assert.IsFalse(f.Dialog.ShowsBookmarks);
    }

    [TestMethod]
    public async Task HistoryListsVisitedPagesNewestFirst()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "a.test");
        await GoToAsync(f, "https://a.test/two");

        await PressAsync(f, ConsoleKey.F4);
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "History (2)");
        var text = screen.ToString();
        Assert.IsTrue(text.IndexOf("https://a.test/two", StringComparison.Ordinal) < text.LastIndexOf("https://a.test/", StringComparison.Ordinal)
            || text.Contains("Second"));
    }

    [TestMethod]
    public async Task PageSourceCanBeToggled()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/two");
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.F9);
        var source = await DrawAsync(f);
        AssertScreenContains(source, "<h1>The second page</h1>");

        await PressAsync(f, ConsoleKey.F9);
        var rendered = await DrawAsync(f);
        AssertScreenDoesNotContain(rendered, "<h1>");
        AssertScreenContains(rendered, "The second page");
    }

    [TestMethod]
    public async Task HelpIsShownAndAnyKeyReturns()
    {
        var f = await OpenAsync();
        await DrawAsync(f);

        await PressAsync(f, ConsoleKey.F1);
        var help = await DrawAsync(f);
        AssertScreenContains(help, "Ctrl+L, F6");

        await PressAsync(f, ConsoleKey.Spacebar, ' ');
        var back = await DrawAsync(f);
        AssertScreenDoesNotContain(back, "Ctrl+L, F6");
        Assert.IsTrue(f.Root.Controls.AsSnapshot().Contains(f.Dialog), "any key leaves the help, it does not close the browser");
    }

    [TestMethod]
    public async Task EscapeStopsALoadFirstAndThenClosesTheBrowser()
    {
        var f = await OpenAsync();
        f.Fetcher.Gate = new TaskCompletionSource();
        await PressAsync(f, ConsoleKey.L, 'l', control: true);
        await TypeTextAsync(f, "https://a.test/two");
        await PressAsync(f, ConsoleKey.Enter, '\r');
        Assert.IsTrue(f.Tab.IsLoading);
        var loading = await DrawAsync(f);
        AssertScreenContains(loading, "Loading https://a.test/two");

        await PressAsync(f, ConsoleKey.Escape);
        Assert.IsFalse(f.Tab.IsLoading);
        Assert.IsTrue(f.Root.Controls.AsSnapshot().Contains(f.Dialog), "the first Esc only stops the load");
        f.Fetcher.Gate.SetResult();

        await PressAsync(f, ConsoleKey.Escape);

        Assert.IsFalse(f.Root.Controls.AsSnapshot().Contains(f.Dialog));
    }

    [TestMethod]
    public async Task ReopeningKeepsTheTabsAndThePage()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/two");
        await PressAsync(f, ConsoleKey.Escape);

        await f.Root.ShowDialogAsync(f.Dialog);
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "The second page");
        Assert.AreEqual(1, f.Fetcher.Requests.Count(r => r.Url == "about:home"), "the home page is opened once, not on every show");
    }

    [TestMethod]
    public async Task AnUnreachablePageShowsAReadableError()
    {
        var f = await OpenAsync();

        await GoToAsync(f, "https://nowhere.test/");
        var screen = await DrawAsync(f);

        AssertScreenContains(screen, "Cannot open this page");
        AssertScreenContains(screen, "No such host");
    }

    [TestMethod]
    public async Task ASmallTerminalStillDraws()
    {
        var size = new Size(30, 10);
        var f = await OpenAsync(size: size);
        await GoToAsync(f, "a.test");

        var screen = await DrawAsync(f, size);

        AssertScreenContains(screen, "Welcome");
    }

    [TestMethod]
    public async Task ASaveWritesTheFileAndSaysWhere()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/two");
        var directory = Path.Combine(Path.GetTempPath(), "iterminal-web-save-" + Guid.NewGuid().ToString("N"));
        try
        {
            var dialog = new WebBrowserDialog(new WebBrowserModel(f.Fetcher, f.Store, directory), TestTheme());
            var root = await ShowAsync(dialog);
            await dialog.Model.Active.LoadTask;
            await dialog.Model.NavigateAsync(dialog.Model.Active, "https://a.test/two");

            await dialog.HandleKeyEventAsync(Key(ConsoleKey.F7));

            Assert.IsTrue(dialog.Model.Active.Message.Contains(directory, StringComparison.Ordinal), dialog.Model.Active.Message);
            Assert.AreEqual(1, Directory.GetFiles(directory, "*.html").Length);
            Assert.IsNotNull(root);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public void FitHints_DropsTheLeastUsefulHintsWholeAndKeepsEscClose()
    {
        string[] hints = ["F1:help", "Ctrl+L:address", "Tab:links", "Ctrl+T:new tab", "F2:bookmarks", "Esc:close"];
        int[] dropOrder = [4, 3, 2, 1, 0, 5];

        string wide = WebBrowserDialog.FitHints(hints, dropOrder, 200);
        string narrow = WebBrowserDialog.FitHints(hints, dropOrder, 40);

        Assert.AreEqual(string.Join("  ", hints), wide);
        Assert.IsTrue(narrow.Length <= 40, narrow);
        Assert.IsTrue(narrow.EndsWith("Esc:close", StringComparison.Ordinal), narrow);
        Assert.IsFalse(narrow.Contains("bookmarks", StringComparison.Ordinal), narrow);
    }

    [TestMethod]
    public async Task AnyKeyButYWithdrawsTheBookmarkRemoval_AndEscapeAlsoLeavesTheList()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/two");
        await PressAsync(f, ConsoleKey.D, 'd', control: true);
        await PressAsync(f, ConsoleKey.F2);

        await PressAsync(f, ConsoleKey.Delete);
        await PressAsync(f, ConsoleKey.N, 'n');
        Assert.IsFalse(f.Dialog.ConfirmingRemoveBookmark);
        Assert.IsTrue(f.Store.IsBookmarked("https://a.test/two"));
        Assert.IsTrue(f.Dialog.ShowsBookmarks);
        AssertScreenContains(await DrawAsync(f), "Bookmarks (1)");

        await PressAsync(f, ConsoleKey.Delete);
        await PressAsync(f, ConsoleKey.Escape);
        Assert.IsTrue(f.Store.IsBookmarked("https://a.test/two"));
        Assert.IsFalse(f.Dialog.ShowsBookmarks);
        Assert.IsFalse(f.Dialog.ConfirmingRemoveBookmark);
    }

    [TestMethod]
    public async Task ForgettingAVisitInHistoryNeedsNoQuestion()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/two");
        await PressAsync(f, ConsoleKey.F4);
        Assert.IsTrue(f.Dialog.ShowsHistory);

        await PressAsync(f, ConsoleKey.Delete);

        Assert.IsFalse(f.Dialog.ConfirmingRemoveBookmark);
    }

    [TestMethod]
    public async Task CtrlVPastesIntoTheAddressBar_AtTheCursor_OnOneLine()
    {
        var f = await OpenAsync();
        await iConsole.Services.TextClipboard.Current.SetTextAsync("a.test/" + (char)10 + "two ");
        await PressAsync(f, ConsoleKey.L, 'l', control: true);
        await TypeTextAsync(f, "https://");
        await PressAsync(f, ConsoleKey.LeftArrow);
        await PressAsync(f, ConsoleKey.LeftArrow);

        await PressAsync(f, ConsoleKey.V, 'v', control: true);

        Assert.AreEqual("https:a.test/ two//", f.Dialog.AddressText);
        AssertScreenContains(await DrawAsync(f), "Ctrl+V:paste");
    }

    [TestMethod]
    public async Task ShiftInsertPastesToo_AndAFreshAddressBarIsReplacedNotAppendedTo()
    {
        var f = await OpenAsync();
        await iConsole.Services.TextClipboard.Current.SetTextAsync("https://a.test/two");
        await PressAsync(f, ConsoleKey.L, 'l', control: true);

        await PressAsync(f, ConsoleKey.Insert, shift: true);
        Assert.AreEqual("https://a.test/two", f.Dialog.AddressText);
        await PressAsync(f, ConsoleKey.Enter, (char)13);
        await f.Tab.LoadTask;

        Assert.AreEqual("https://a.test/two", f.Tab.Url);
    }

    [TestMethod]
    public async Task PastingAnEmptyClipboardChangesNothing()
    {
        var f = await OpenAsync();
        await iConsole.Services.TextClipboard.Current.SetTextAsync(string.Empty);
        await PressAsync(f, ConsoleKey.L, 'l', control: true);
        await TypeTextAsync(f, "abc");

        await PressAsync(f, ConsoleKey.V, 'v', control: true);

        Assert.AreEqual("abc", f.Dialog.AddressText);
    }

    [TestMethod]
    public async Task CtrlVPastesIntoTheFindBox()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/long");
        await DrawAsync(f);
        await iConsole.Services.TextClipboard.Current.SetTextAsync("number 77");
        await PressAsync(f, ConsoleKey.F, 'f', control: true);

        await PressAsync(f, ConsoleKey.V, 'v', control: true);

        AssertScreenContains(await DrawAsync(f), "Find: number 77");
        await PressAsync(f, ConsoleKey.Enter, (char)13);
        AssertScreenContains(await DrawAsync(f), "line number 77");
    }

    [TestMethod]
    public async Task CtrlVPastesIntoAFormField()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/form");
        await DrawAsync(f);
        await iConsole.Services.TextClipboard.Current.SetTextAsync("black cats" + (char)10 + "and dogs");
        await PressAsync(f, ConsoleKey.Tab, (char)9);
        await PressAsync(f, ConsoleKey.Enter, (char)13);

        await PressAsync(f, ConsoleKey.V, 'v', control: true);
        await PressAsync(f, ConsoleKey.Enter, (char)13);
        await f.Tab.LoadTask;

        string url = f.Fetcher.Requests[^1].Url;
        Assert.IsTrue(url.StartsWith("https://a.test/results?q=black"), url);
        Assert.IsTrue(url.Contains("dogs"), url);
    }

    [TestMethod]
    public async Task CtrlWDeletesTheWordBeforeTheCursorInTheAddressBar()
    {
        var f = await OpenAsync();
        await PressAsync(f, ConsoleKey.L, 'l', control: true);
        await TypeTextAsync(f, "alpha beta gamma");
        await PressAsync(f, ConsoleKey.LeftArrow);
        await PressAsync(f, ConsoleKey.LeftArrow);

        await PressAsync(f, ConsoleKey.W, 'w', control: true);

        Assert.AreEqual("alpha beta ma", f.Dialog.AddressText);
        await PressAsync(f, ConsoleKey.Backspace, control: true);
        Assert.AreEqual("alpha ma", f.Dialog.AddressText);
    }

    [TestMethod]
    public async Task CtrlWOnAFreshAddressBarClearsItAll()
    {
        var f = await OpenAsync();
        await PressAsync(f, ConsoleKey.L, 'l', control: true);

        await PressAsync(f, ConsoleKey.W, 'w', control: true);

        Assert.AreEqual(string.Empty, f.Dialog.AddressText);
    }

    [TestMethod]
    public async Task CtrlWDeletesTheLastWordOfTheFindBox()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/long");
        await DrawAsync(f);
        await PressAsync(f, ConsoleKey.F, 'f', control: true);
        await TypeTextAsync(f, "line number");

        await PressAsync(f, ConsoleKey.W, 'w', control: true);

        AssertScreenContains(await DrawAsync(f), "Find: line ");
    }

    [TestMethod]
    public async Task CtrlWDeletesTheLastWordOfAFormField()
    {
        var f = await OpenAsync();
        await GoToAsync(f, "https://a.test/form");
        await DrawAsync(f);
        await PressAsync(f, ConsoleKey.Tab, (char)9);
        await PressAsync(f, ConsoleKey.Enter, (char)13);
        await TypeTextAsync(f, "black cats");

        await PressAsync(f, ConsoleKey.W, 'w', control: true);
        await PressAsync(f, ConsoleKey.Enter, (char)13);
        await f.Tab.LoadTask;

        string url = f.Fetcher.Requests[^1].Url;
        Assert.IsTrue(url.Contains("q=black"), url);
        Assert.IsFalse(url.Contains("cats"), url);
    }
}
