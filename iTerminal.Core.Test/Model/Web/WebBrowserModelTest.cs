using iTerminal.Core.Model.Web;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Web;
using Moq;

namespace iTerminal.Core.Test.Model.Web;

/// <summary>Answers from a dictionary and remembers what was asked, so no test reaches the network.</summary>
internal sealed class FakeWebFetcher : IWebFetcher
{
    public Dictionary<string, Func<WebFetchRequest, WebFetchResponse>> Pages { get; } = new(StringComparer.Ordinal);
    public List<WebFetchRequest> Requests { get; } = [];
    public TaskCompletionSource? Gate { get; set; }

    public void Html(string url, string html, int status = 200) =>
        Pages[url] = _ => new WebFetchResponse(url, status, status == 200 ? "OK" : "Not Found", "text/html; charset=utf-8", System.Text.Encoding.UTF8.GetBytes(html));

    public async Task<WebFetchResponse> FetchAsync(WebFetchRequest request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        if (Gate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (request.Url.StartsWith("about:", StringComparison.Ordinal))
        {
            return new WebFetchResponse(request.Url, 200, "OK", "text/html", System.Text.Encoding.UTF8.GetBytes(request.Url == "about:blank" ? string.Empty : WebHomePage.Html));
        }

        var key = WebAddress.WithoutFragment(request.Url);
        var query = key.IndexOf('?');
        key = query < 0 ? key : key[..query];
        if (Uri.TryCreate(key, UriKind.Absolute, out var parsed))
        {
            key = parsed.AbsoluteUri;
        }

        if (!Pages.TryGetValue(key, out var page))
        {
            throw new WebFetchException("No such host: " + request.Url);
        }

        return page(request);
    }
}

internal sealed class MemorySettings : ISettingsService
{
    private readonly Dictionary<string, string> _values = [];
    public bool IsDirty => false;
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;
    public void Set(string key, string value) => _values[key] = value;
    public void Write()
    {
    }
}

[TestClass]
public class WebBrowserModelTest
{
    private string _downloads = string.Empty;

    [TestInitialize]
    public void Setup() => _downloads = Path.Combine(Path.GetTempPath(), "iterminal-web-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_downloads))
        {
            Directory.Delete(_downloads, true);
        }
    }

    private WebBrowserModel Create(FakeWebFetcher fetcher, WebBrowserStore? store = null) =>
        new(fetcher, store ?? new WebBrowserStore(new MemorySettings()), _downloads);

    private static async Task LayoutAsync(WebBrowserModel model, WebTab tab, int width = 60)
    {
        model.EnsureLayout(tab, width);
        await tab.LayoutTask;
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NavigatingLoadsAndLaysOutThePage()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<title>Hello</title><h1>Welcome</h1><p>text</p>");
        var model = Create(fetcher);
        var tab = model.Active;

        await model.NavigateAsync(tab, "a.test");
        await LayoutAsync(model, tab);

        Assert.AreEqual("Hello", tab.Title);
        Assert.AreEqual("https://a.test/", tab.Url);
        Assert.IsFalse(tab.IsLoading);
        Assert.AreEqual("Welcome", tab.Page!.Lines[0].Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedLoadShowsAnErrorPageThatCanBeRetried()
    {
        var fetcher = new FakeWebFetcher();
        var model = Create(fetcher);
        var tab = model.Active;

        await model.NavigateAsync(tab, "https://missing.test/");
        await LayoutAsync(model, tab);

        Assert.AreEqual("Cannot open this page", tab.Title);
        StringAssert.Contains(string.Join("\n", tab.Page!.Lines.Select(l => l.Text)), "No such host");

        fetcher.Html("https://missing.test/", "<title>Back</title>ok");
        await model.ReloadAsync(tab);

        Assert.AreEqual("Back", tab.Title);
        Assert.AreEqual(1, tab.History.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task BackAndForwardWalkTheHistoryAndKeepTheScrollPosition()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", string.Concat(Enumerable.Range(0, 80).Select(i => $"<p>line {i}</p>")) + "<a href='/b'>next</a>");
        fetcher.Html("https://a.test/b", "<title>B</title>second");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");
        await LayoutAsync(model, tab);
        tab.ScrollBy(30, 10);
        await model.OpenLinkAsync(tab, 0);
        await LayoutAsync(model, tab);
        Assert.AreEqual("B", tab.Title);
        Assert.IsTrue(tab.CanGoBack);
        Assert.IsFalse(tab.CanGoForward);

        Assert.IsTrue(model.GoBack(tab));
        await LayoutAsync(model, tab);

        Assert.AreEqual("https://a.test/", tab.Url);
        Assert.AreEqual(30, tab.Scroll);
        Assert.IsTrue(tab.CanGoForward);
        Assert.IsTrue(model.GoForward(tab));
        Assert.AreEqual("B", tab.Title);
        Assert.AreEqual(1, fetcher.Requests.Count(r => r.Url == "https://a.test/"), "going back must not fetch the page again");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NavigatingAfterGoingBackDropsTheForwardHistory()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/1", "one");
        fetcher.Html("https://a.test/2", "two");
        fetcher.Html("https://a.test/3", "three");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/1");
        await model.NavigateAsync(tab, "https://a.test/2");
        model.GoBack(tab);

        await model.NavigateAsync(tab, "https://a.test/3");

        Assert.IsFalse(tab.CanGoForward);
        CollectionAssert.AreEqual(new[] { "https://a.test/1", "https://a.test/3" }, tab.History.Select(h => h.Url).ToArray());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFragmentLinkOnTheSamePageOnlyScrolls()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<a href='#end'>to the end</a>" + string.Concat(Enumerable.Range(0, 60).Select(i => $"<p>filler {i}</p>")) + "<h2 id='end'>End</h2>");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");
        await LayoutAsync(model, tab);

        await model.OpenLinkAsync(tab, 0);

        Assert.AreEqual(1, fetcher.Requests.Count);
        Assert.AreEqual("https://a.test/#end", tab.Url);
        Assert.IsTrue(tab.Scroll > 50, "scrolled to " + tab.Scroll);
        Assert.IsTrue(model.GoBack(tab));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task GetFormsAppendTheirFieldsToTheAddress()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/search", "<form action='/find?old=1' method=get><input name=q value=''><input type=checkbox name=exact checked><input type=submit name=go value=Search></form>");
        fetcher.Html("https://a.test/find", "<title>Results</title>results");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/search");
        await LayoutAsync(model, tab);
        var document = tab.Current!.Document!;
        model.SetValue(tab, document.Fields[0], "two words & more");

        await model.SubmitAsync(tab, document.Fields[2]);

        Assert.AreEqual("Results", tab.Title);
        Assert.AreEqual("https://a.test/find?q=two+words+%26+more&exact=on&go=Search", fetcher.Requests[^1].Url);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PostFormsSendABodyAndKeepItOutOfTheAddress()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/login", "<form action='/session' method=POST><input name=user><input type=password name=pw><input type=submit value=Go></form>");
        fetcher.Html("https://a.test/session", "<title>Welcome</title>hi");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/login");
        var fields = tab.Current!.Document!.Fields;
        model.SetValue(tab, fields[0], "ada");
        model.SetValue(tab, fields[1], "s3cret");

        await model.SubmitAsync(tab, fields[2]);

        var request = fetcher.Requests[^1];
        Assert.AreEqual("POST", request.Method);
        Assert.AreEqual("https://a.test/session", request.Url);
        Assert.AreEqual("user=ada&pw=s3cret", request.Body);
        Assert.AreEqual("Welcome", tab.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task RadioButtonsOfAGroupExcludeEachOther()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<form><input type=radio name=r value=1 checked><input type=radio name=r value=2></form>");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");
        var fields = tab.Current!.Document!.Fields;

        model.Toggle(tab, fields[1]);

        Assert.IsFalse(fields[0].Checked);
        Assert.IsTrue(fields[1].Checked);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AMetaRefreshIsFollowed()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/old", "<meta http-equiv='refresh' content='0; url=/new'>moved");
        fetcher.Html("https://a.test/new", "<title>New</title>here");
        var model = Create(fetcher);
        var tab = model.Active;

        await model.NavigateAsync(tab, "https://a.test/old");

        Assert.AreEqual("New", tab.Title);
        Assert.AreEqual("https://a.test/new", tab.Url);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task JavaScriptAndMailLinksAreReportedNotFollowed()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<a href='javascript:go()'>js</a><a href='mailto:x@y.test'>mail</a>");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");

        await model.OpenLinkAsync(tab, 0);
        StringAssert.Contains(tab.Message, "JavaScript");
        await model.OpenLinkAsync(tab, 1);
        StringAssert.Contains(tab.Message, "mailto:x@y.test");

        Assert.AreEqual(1, fetcher.Requests.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task StoppingALoadKeepsThePageThatWasShowing()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<title>Fast</title>fast");
        fetcher.Html("https://a.test/slow", "slow");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");
        fetcher.Gate = new TaskCompletionSource();

        _ = model.NavigateAsync(tab, "https://a.test/slow");
        Assert.IsTrue(tab.IsLoading);
        model.Stop(tab);
        fetcher.Gate.SetResult();
        await tab.LoadTask;

        Assert.IsFalse(tab.IsLoading);
        Assert.AreEqual("Fast", tab.Title);
        Assert.AreEqual(1, tab.History.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task OnlyTheNewestLoadOfATabLands()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/1", "<title>One</title>");
        fetcher.Html("https://a.test/2", "<title>Two</title>");
        var model = Create(fetcher);
        var tab = model.Active;
        fetcher.Gate = new TaskCompletionSource();
        var first = model.NavigateAsync(tab, "https://a.test/1");
        var second = model.NavigateAsync(tab, "https://a.test/2");

        fetcher.Gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.AreEqual("Two", tab.Title);
        Assert.AreEqual(1, tab.History.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TabsHaveTheirOwnHistoryAndTheLastOneIsNeverClosed()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<title>A</title>");
        fetcher.Html("https://b.test/", "<title>B</title>");
        var model = Create(fetcher);
        await model.NavigateAsync(model.Active, "https://a.test/");

        var second = model.NewTab();
        await model.NavigateAsync(second, "https://b.test/");

        Assert.AreEqual(2, model.Tabs.Count);
        Assert.AreEqual(1, model.ActiveIndex);
        model.SelectNext(1);
        Assert.AreEqual("A", model.Active.Title);
        Assert.IsTrue(model.CloseTab(0));
        Assert.AreEqual(1, model.Tabs.Count);
        Assert.AreEqual("B", model.Active.Title);
        Assert.IsFalse(model.CloseTab(0));
        Assert.AreEqual(1, model.Tabs.Count);
        Assert.AreEqual("New tab", model.Active.Title);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TextFilesAreShownAsTheyAreAndBinaryOnesCanBeSaved()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Pages["https://a.test/notes.txt"] = _ => new WebFetchResponse("https://a.test/notes.txt", 200, "OK", "text/plain; charset=utf-8", System.Text.Encoding.UTF8.GetBytes("plain\n  text"));
        fetcher.Pages["https://a.test/pic.png"] = _ => new WebFetchResponse("https://a.test/pic.png", 200, "OK", "image/png", [1, 2, 3, 4]);
        var model = Create(fetcher);
        var tab = model.Active;

        await model.NavigateAsync(tab, "https://a.test/notes.txt");
        await LayoutAsync(model, tab);
        CollectionAssert.AreEqual(new[] { "plain", "  text" }, tab.Page!.Lines.Select(l => l.Text).ToArray());

        await model.NavigateAsync(tab, "https://a.test/pic.png");
        await LayoutAsync(model, tab);
        StringAssert.Contains(string.Join("\n", tab.Page!.Lines.Select(l => l.Text)), "F7");

        var first = model.Save(tab);
        var second = model.Save(tab);
        Assert.AreEqual("pic.png", Path.GetFileName(first));
        Assert.AreEqual("pic (1).png", Path.GetFileName(second));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(first));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task PageSourceCanBeShownInPlaceOfThePage()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<h1>Rendered</h1>");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");
        await LayoutAsync(model, tab);
        Assert.AreEqual("Rendered", tab.Page!.Lines[0].Text);

        tab.ShowSource = true;
        await LayoutAsync(model, tab);

        Assert.AreEqual("<h1>Rendered</h1>", tab.Page!.Lines[0].Text);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ResizingRelaysOutTheSamePage()
    {
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<p>one two three four five six seven eight nine ten</p>");
        var model = Create(fetcher);
        var tab = model.Active;
        await model.NavigateAsync(tab, "https://a.test/");
        await LayoutAsync(model, tab, 60);
        var wide = tab.Page!.Lines.Count;

        await LayoutAsync(model, tab, 12);

        Assert.AreEqual(12, tab.Page!.Width);
        Assert.IsTrue(tab.Page.Lines.Count > wide);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task VisitsAndBookmarksArePersisted()
    {
        var settings = new MemorySettings();
        var fetcher = new FakeWebFetcher();
        fetcher.Html("https://a.test/", "<title>A</title>");
        var model = Create(fetcher, new WebBrowserStore(settings));

        await model.NavigateAsync(model.Active, "https://a.test/");
        model.Store.ToggleBookmark("https://a.test/", "A");

        var reopened = new WebBrowserStore(settings);
        Assert.AreEqual("https://a.test/", reopened.History[0].Url);
        Assert.AreEqual("A", reopened.History[0].Title);
        Assert.IsTrue(reopened.IsBookmarked("https://a.test/"));
        Assert.IsFalse(reopened.ToggleBookmark("https://a.test/", "A"));
        Assert.AreEqual(0, new WebBrowserStore(settings).Bookmarks.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheStoreIgnoresCorruptSettings()
    {
        var settings = new MemorySettings();
        settings.Set(WebBrowserStore.BookmarksKey, "{not json");

        var store = new WebBrowserStore(settings);

        Assert.AreEqual(0, store.Bookmarks.Count);
        Assert.AreEqual(WebBrowserStore.DefaultHome, store.Home);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheFocusMovesThroughLinksAndWraps()
    {
        var document = WebDocument.Parse("https://a.test/", "<a href=1>one</a> <a href=2>two</a><p><a href=3>three</a></p>");
        var tab = new WebTab();
        tab.History.Add(new WebHistoryEntry { Request = new WebFetchRequest("https://a.test/"), Url = "https://a.test/", Document = document, Page = new HtmlLayout(document).Layout(40) });
        tab.Index = 0;

        Assert.IsTrue(tab.MoveFocus(true, 10));
        Assert.AreEqual(0, tab.FocusedItem);
        tab.MoveFocus(true, 10);
        tab.MoveFocus(true, 10);
        Assert.AreEqual(2, tab.FocusedItem);
        tab.MoveFocus(true, 10);
        Assert.AreEqual(0, tab.FocusedItem);
        tab.MoveFocus(false, 10);
        Assert.AreEqual(2, tab.FocusedItem);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FocusingAnOffScreenLinkScrollsToIt()
    {
        var document = WebDocument.Parse("https://a.test/", string.Concat(Enumerable.Range(0, 50).Select(i => $"<p>line {i}</p>")) + "<a href=1>far</a>");
        var tab = new WebTab();
        tab.History.Add(new WebHistoryEntry { Request = new WebFetchRequest("https://a.test/"), Url = "https://a.test/", Document = document, Page = new HtmlLayout(document).Layout(40) });
        tab.Index = 0;

        tab.MoveFocus(true, 10);

        var line = tab.Page!.Items[0].Line;
        Assert.IsTrue(tab.Scroll <= line && line < tab.Scroll + 10, $"scroll {tab.Scroll}, link on line {line}");
    }
}
