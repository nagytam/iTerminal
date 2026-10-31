using System.Net;
using iTerminal.Core.Services.Encyclopedia;

namespace iTerminal.Core.Test.Services.Encyclopedia;

[TestClass]
public class EncyclopediaTest
{
    internal sealed class FakeEncyclopedia : IEncyclopediaProvider
    {
        public List<EncyclopediaHit> Hits { get; } = [new("Tulip", "A genus of flowers"), new("Tulip mania", "A speculative bubble")];
        public int SearchCalls { get; private set; }
        public int ArticleCalls { get; private set; }
        public EncyclopediaSource? LastSource { get; private set; }
        public string? LastQuery { get; private set; }
        public Exception? Failure { get; set; }
        public Task? Gate { get; set; }

        public async Task<IReadOnlyList<EncyclopediaHit>> SearchAsync(EncyclopediaSource source, string query, CancellationToken cancellationToken = default)
        {
            SearchCalls++;
            LastSource = source;
            LastQuery = query;
            if (Gate is not null)
            {
                await Gate.WaitAsync(cancellationToken);
            }

            return Failure is not null ? throw Failure : Hits;
        }

        public Task<EncyclopediaArticle> GetArticleAsync(EncyclopediaSource source, string title, bool full, CancellationToken cancellationToken = default)
        {
            ArticleCalls++;
            if (Failure is not null)
            {
                throw Failure;
            }

            return Task.FromResult(new EncyclopediaArticle(title, (full ? "Whole " : "Intro ") + title, $"https://{source.Address}/wiki/{title}", full));
        }
    }

    private sealed class FixedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static readonly EncyclopediaSource English = EncyclopediaSources.Find("wikipedia-en");

    #region Sources

    [TestMethod]
    public void TheDefaultSourceIsEnglishWikipedia_AndAnUnknownIdFallsBackToIt()
    {
        Assert.AreEqual("wikipedia-en", EncyclopediaSources.All[0].Id);
        Assert.AreEqual("wikipedia-en", EncyclopediaSources.Find("gone").Id);
        Assert.AreEqual("wikipedia-en", EncyclopediaSources.Find(null).Id);
        Assert.AreEqual("wiktionary-en", EncyclopediaSources.Find("wiktionary-en").Id);
    }

    [TestMethod]
    public void EverySourceHasAnIdAndAName_AndTheIdsAreUnique()
    {
        Assert.AreEqual(EncyclopediaSources.All.Count, EncyclopediaSources.All.Select(s => s.Id).Distinct().Count());
        Assert.IsTrue(EncyclopediaSources.All.All(s => s.Id.Length > 0 && s.Name.Length > 0 && s.Address.Length > 0));
        Assert.IsTrue(EncyclopediaSources.All.Any(s => s.Kind == EncyclopediaKind.LinkOut), "Britannica is reachable as a link");
    }

    [TestMethod]
    public void APageAddressEscapesTheTerm()
    {
        Assert.AreEqual("https://en.wikipedia.org/wiki/Tulip_mania", English.PageUrl("Tulip mania"));
        Assert.AreEqual("https://www.britannica.com/search?query=tulip%20mania", EncyclopediaSources.Find("britannica").PageUrl("tulip mania"));
    }

    #endregion

    #region The provider

    [TestMethod]
    public void ASearchAnswerGivesTitlesWithTheirTagsDroppedFromTheSnippet()
    {
        IReadOnlyList<EncyclopediaHit> hits = WikiEncyclopediaProvider.ParseSearch(
            """{"query":{"search":[{"title":"Tulip","snippet":"A <span class=\"searchmatch\">tulip</span> &amp; its kin"},{"title":"","snippet":"x"},{"title":"Tulip mania"}]}}""");

        Assert.AreEqual(2, hits.Count, "an entry without a title is dropped");
        Assert.AreEqual(new EncyclopediaHit("Tulip", "A tulip & its kin"), hits[0]);
        Assert.AreEqual(new EncyclopediaHit("Tulip mania", string.Empty), hits[1]);
    }

    [TestMethod]
    public void ASearchAnswerWithNothingInItGivesNoHits()
    {
        Assert.AreEqual(0, WikiEncyclopediaProvider.ParseSearch("""{"batchcomplete":true}""").Count);
        Assert.AreEqual(0, WikiEncyclopediaProvider.ParseSearch("""{"query":{"search":[]}}""").Count);
    }

    [TestMethod]
    public void AnAnswerThatIsNotJsonIsReportedAsSuch()
    {
        Assert.ThrowsExactly<EncyclopediaException>(() => WikiEncyclopediaProvider.ParseSearch("<html>"));
        Assert.ThrowsExactly<EncyclopediaException>(() => WikiEncyclopediaProvider.ParseArticle(English, "x", false, "<html>"));
    }

    [TestMethod]
    public void AnArticleAnswerGivesTheTitleTheTextAndTheAddress()
    {
        EncyclopediaArticle article = WikiEncyclopediaProvider.ParseArticle(
            English, "tulip", false, """{"query":{"pages":[{"title":"Tulip","extract":"  Tulips are flowers.\n","fullurl":"https://en.wikipedia.org/wiki/Tulip"}]}}""");

        Assert.AreEqual("Tulip", article.Title, "the title after a redirect");
        Assert.AreEqual("Tulips are flowers.", article.Text);
        Assert.AreEqual("https://en.wikipedia.org/wiki/Tulip", article.Url);
        Assert.IsFalse(article.IsFull);
    }

    [TestMethod]
    public void AnArticleWithoutAnAddressGetsItsPageAddress()
    {
        EncyclopediaArticle article = WikiEncyclopediaProvider.ParseArticle(
            English, "tulip mania", true, """{"query":{"pages":[{"title":"Tulip mania","extract":"Text"}]}}""");

        Assert.AreEqual("https://en.wikipedia.org/wiki/Tulip_mania", article.Url);
        Assert.IsTrue(article.IsFull);
    }

    [TestMethod]
    public void AMissingArticleIsAnError()
    {
        EncyclopediaException exception = Assert.ThrowsExactly<EncyclopediaException>(() => WikiEncyclopediaProvider.ParseArticle(
            English, "Xyzzy", false, """{"query":{"pages":[{"title":"Xyzzy","missing":true}]}}"""));

        StringAssert.Contains(exception.Message, "Wikipedia (English) has no article called \"Xyzzy\"");
    }

    [TestMethod]
    public void TheRequestAddressesAskForTheIntroductionOrAllOfIt()
    {
        string intro = WikiEncyclopediaProvider.ArticleUrl(English, "Tulip mania", full: false);
        string whole = WikiEncyclopediaProvider.ArticleUrl(English, "Tulip mania", full: true);

        StringAssert.StartsWith(intro, "https://en.wikipedia.org/w/api.php?");
        StringAssert.Contains(intro, "exintro=1");
        StringAssert.Contains(intro, "titles=Tulip%20mania");
        Assert.IsFalse(whole.Contains("exintro", StringComparison.Ordinal));
        StringAssert.Contains(WikiEncyclopediaProvider.SearchUrl(English, "a&b c"), "srsearch=a%26b%20c");
    }

    [TestMethod]
    public async Task TheProviderAsksTheSourcesHost_AndSaysWhoIsAsking()
    {
        FixedHandler handler = new(_ => Json("""{"query":{"search":[{"title":"Tulip","snippet":"s"}]}}"""));
        using HttpClient client = new(handler);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("iTerminal/1.0");
        WikiEncyclopediaProvider provider = new(client);

        IReadOnlyList<EncyclopediaHit> hits = await provider.SearchAsync(EncyclopediaSources.Find("wiktionary-en"), "tulip");

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("en.wiktionary.org", handler.Requests[0].RequestUri!.Host);
        Assert.IsTrue(handler.Requests[0].Headers.UserAgent.ToString().Contains("iTerminal", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ANetworkFailureBecomesAnEncyclopediaException()
    {
        FixedHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using HttpClient client = new(handler);
        WikiEncyclopediaProvider provider = new(client);

        EncyclopediaException exception = await Assert.ThrowsExactlyAsync<EncyclopediaException>(() => provider.SearchAsync(English, "tulip"));

        StringAssert.Contains(exception.Message, "Could not reach Wikipedia (English)");
    }

    #endregion

    #region The service

    [TestMethod]
    public async Task ABlankTermFindsNothingWithoutAskingTheProvider()
    {
        FakeEncyclopedia provider = new();
        EncyclopediaService service = new(provider);

        Assert.AreEqual(0, (await service.SearchAsync(English, "   ")).Count);
        Assert.AreEqual(0, provider.SearchCalls);
    }

    [TestMethod]
    public async Task TheTermIsTrimmedBeforeItIsSearched()
    {
        FakeEncyclopedia provider = new();
        EncyclopediaService service = new(provider);

        await service.SearchAsync(English, "  tulip ");

        Assert.AreEqual("tulip", provider.LastQuery);
    }

    [TestMethod]
    public async Task ALinkOutSourceAnswersWithTheTermItself_AndAnArticleThatPointsToTheBrowser()
    {
        FakeEncyclopedia provider = new();
        EncyclopediaService service = new(provider);
        EncyclopediaSource britannica = EncyclopediaSources.Find("britannica");

        IReadOnlyList<EncyclopediaHit> hits = await service.SearchAsync(britannica, "tulip mania");
        EncyclopediaArticle article = await service.GetArticleAsync(britannica, hits[0].Title, false);

        Assert.AreEqual(1, hits.Count);
        Assert.AreEqual("tulip mania", hits[0].Title);
        StringAssert.Contains(article.Text, "Britannica cannot be read from here");
        Assert.AreEqual("https://www.britannica.com/search?query=tulip%20mania", article.Url);
        Assert.AreEqual(0, provider.SearchCalls + provider.ArticleCalls, "Britannica is never asked by a program");
    }

    [TestMethod]
    public async Task AnArticleReadTwiceIsFetchedOnce_ButTheIntroductionAndTheWholeAreSeparate()
    {
        FakeEncyclopedia provider = new();
        EncyclopediaService service = new(provider);

        await service.GetArticleAsync(English, "Tulip", false);
        await service.GetArticleAsync(English, "Tulip", false);
        Assert.AreEqual(1, provider.ArticleCalls);

        await service.GetArticleAsync(English, "Tulip", true);
        Assert.AreEqual(2, provider.ArticleCalls);
    }

    [TestMethod]
    public async Task AnArticleWithNoIntroductionIsReadWhole()
    {
        EmptyIntroProvider provider = new();
        EncyclopediaService service = new(provider);

        EncyclopediaArticle article = await service.GetArticleAsync(EncyclopediaSources.Find("wiktionary-en"), "tulip", false);

        Assert.AreEqual("The entry", article.Text);
        Assert.IsTrue(article.IsFull);
    }

    private sealed class EmptyIntroProvider : IEncyclopediaProvider
    {
        public Task<IReadOnlyList<EncyclopediaHit>> SearchAsync(EncyclopediaSource source, string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EncyclopediaHit>>([]);

        public Task<EncyclopediaArticle> GetArticleAsync(EncyclopediaSource source, string title, bool full, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EncyclopediaArticle(title, full ? "The entry" : string.Empty, "https://x", full));
    }

    [TestMethod]
    public async Task AFailedArticleIsNotRemembered()
    {
        FakeEncyclopedia provider = new() { Failure = new EncyclopediaException("down") };
        EncyclopediaService service = new(provider);

        await Assert.ThrowsExactlyAsync<EncyclopediaException>(() => service.GetArticleAsync(English, "Tulip", false));
        provider.Failure = null;
        EncyclopediaArticle article = await service.GetArticleAsync(English, "Tulip", false);

        Assert.AreEqual("Intro Tulip", article.Text);
    }

    [TestMethod]
    public async Task TheMemoryHasALimit()
    {
        FakeEncyclopedia provider = new();
        EncyclopediaService service = new(provider);

        for (int i = 0; i < EncyclopediaService.CacheLimit + 5; i++)
        {
            await service.GetArticleAsync(English, "A" + i, false);
        }

        await service.GetArticleAsync(English, "A0", false);

        Assert.AreEqual(EncyclopediaService.CacheLimit + 6, provider.ArticleCalls, "the oldest one was forgotten and fetched again");
    }

    #endregion
}
