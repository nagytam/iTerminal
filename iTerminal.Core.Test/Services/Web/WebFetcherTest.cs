using System.Net;
using System.Text;
using iTerminal.Core.Services.Web;

namespace iTerminal.Core.Test.Services.Web;

[TestClass]
public class WebFetcherTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static HttpResponseMessage Ok(string body, string type = "text/html; charset=utf-8") =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, type.Split(';')[0]) };

    [Timeout(60_000)]
    [TestMethod]
    public async Task AGetCarriesTheBrowsersHeadersAndReturnsTheBytes()
    {
        var handler = new StubHandler(_ => Ok("<p>hi</p>"));
        using var fetcher = new WebFetcher(handler);

        var response = await fetcher.FetchAsync(new WebFetchRequest("https://a.test/x?y=1", Referer: "https://b.test/"));

        Assert.AreEqual(200, response.Status);
        Assert.AreEqual("<p>hi</p>", Encoding.UTF8.GetString(response.Content));
        var request = handler.Requests[0];
        Assert.AreEqual(WebFetcher.UserAgent, request.Headers.UserAgent.ToString());
        Assert.AreEqual("https://b.test/", request.Headers.Referrer?.ToString());
        Assert.AreEqual(HttpMethod.Get, request.Method);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APostSendsTheFormBody()
    {
        var handler = new StubHandler(_ => Ok("ok"));
        using var fetcher = new WebFetcher(handler);

        await fetcher.FetchAsync(new WebFetchRequest("https://a.test/login", "POST", "user=ada&pw=x"));

        Assert.AreEqual(HttpMethod.Post, handler.Requests[0].Method);
        Assert.AreEqual("user=ada&pw=x", handler.Bodies[0]);
        Assert.AreEqual("application/x-www-form-urlencoded", handler.Requests[0].Content!.Headers.ContentType!.MediaType);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ErrorStatusesAreReturnedWithTheirBodyNotThrown()
    {
        using var fetcher = new WebFetcher(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("gone") }));

        var response = await fetcher.FetchAsync(new WebFetchRequest("https://a.test/missing"));

        Assert.AreEqual(404, response.Status);
        Assert.IsFalse(response.IsSuccess);
        Assert.AreEqual("gone", Encoding.UTF8.GetString(response.Content));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANetworkFailureBecomesAReadableException()
    {
        using var fetcher = new WebFetcher(new StubHandler(_ => throw new HttpRequestException("boom", new IOException("reset"), HttpStatusCode.BadGateway)));

        var exception = await Assert.ThrowsExactlyAsync<WebFetchException>(() => fetcher.FetchAsync(new WebFetchRequest("https://a.test/")));

        Assert.IsTrue(exception.Message.Length > 0);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task UnsupportedSchemesAndBadAddressesAreRefused()
    {
        using var fetcher = new WebFetcher(new StubHandler(_ => Ok("")));

        var ftp = await Assert.ThrowsExactlyAsync<WebFetchException>(() => fetcher.FetchAsync(new WebFetchRequest("ftp://a.test/")));
        var bad = await Assert.ThrowsExactlyAsync<WebFetchException>(() => fetcher.FetchAsync(new WebFetchRequest("not a url")));

        StringAssert.Contains(ftp.Message, "ftp");
        StringAssert.Contains(bad.Message, "not a url");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DataAddressesAreDecoded()
    {
        using var fetcher = new WebFetcher(new StubHandler(_ => Ok("")));

        var plain = await fetcher.FetchAsync(new WebFetchRequest("data:text/html,%3Cb%3Ehi%3C/b%3E"));
        var base64 = await fetcher.FetchAsync(new WebFetchRequest("data:text/plain;base64,aGVsbG8="));

        Assert.AreEqual("<b>hi</b>", Encoding.UTF8.GetString(plain.Content));
        Assert.AreEqual("text/html", plain.ContentType);
        Assert.AreEqual("hello", Encoding.UTF8.GetString(base64.Content));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AboutHomeNeedsNoNetwork()
    {
        var handler = new StubHandler(_ => Ok(""));
        using var fetcher = new WebFetcher(handler);

        var home = await fetcher.FetchAsync(new WebFetchRequest("about:home"));
        var blank = await fetcher.FetchAsync(new WebFetchRequest("about:blank"));

        StringAssert.Contains(Encoding.UTF8.GetString(home.Content), "Web Browser");
        Assert.AreEqual(0, blank.Content.Length);
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task LocalFilesAndFoldersAreServed()
    {
        var folder = Path.Combine(Path.GetTempPath(), "iterminal-web-fetch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "sub"));
        File.WriteAllText(Path.Combine(folder, "page.html"), "<p>local</p>");
        try
        {
            using var fetcher = new WebFetcher(new StubHandler(_ => Ok("")));

            var file = await fetcher.FetchAsync(new WebFetchRequest(new Uri(Path.Combine(folder, "page.html")).AbsoluteUri));
            var listing = await fetcher.FetchAsync(new WebFetchRequest(new Uri(folder + Path.DirectorySeparatorChar).AbsoluteUri));
            var missing = await Assert.ThrowsExactlyAsync<WebFetchException>(() => fetcher.FetchAsync(new WebFetchRequest(new Uri(Path.Combine(folder, "none.html")).AbsoluteUri)));

            Assert.AreEqual("text/html", file.ContentType);
            Assert.AreEqual("<p>local</p>", Encoding.UTF8.GetString(file.Content));
            var text = Encoding.UTF8.GetString(listing.Content);
            StringAssert.Contains(text, "page.html");
            StringAssert.Contains(text, "sub/");
            StringAssert.Contains(missing.Message, "none.html");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
