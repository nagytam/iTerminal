using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using iTerminal.Core.Services.Translation;

namespace iTerminal.Core.Test.Services.Translation;

[TestClass]
public class LibreTranslateProviderTest
{
    private sealed class StubHandler(Func<HttpRequestMessage, string, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
    {
        public List<(Uri? Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri, body));
            (HttpStatusCode status, string reply) = respond(request, body);
            return new HttpResponseMessage(status) { Content = new StringContent(reply, Encoding.UTF8, "application/json") };
        }
    }

    private static LibreTranslateProvider Provider(StubHandler handler, string url = "https://translate.test/", string? key = null) =>
        new(url, () => key, new HttpClient(handler));

    [Timeout(60_000)]
    [TestMethod]
    public async Task ATranslationIsPostedAsJsonAndTheAnswerRead()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, """{"translatedText":"Hallo Welt","detectedLanguage":{"confidence":97,"language":"en"}}"""));

        TranslationResult result = await Provider(handler, key: "secret").TranslateAsync("Hello world", null, "de");

        Assert.AreEqual("Hallo Welt", result.Text);
        Assert.AreEqual("en", result.DetectedSource);
        Assert.AreEqual("https://translate.test/translate", handler.Requests[0].Uri!.ToString());
        JsonNode sent = JsonNode.Parse(handler.Requests[0].Body)!;
        Assert.AreEqual("Hello world", (string?)sent["q"]);
        Assert.AreEqual("auto", (string?)sent["source"], "no source language means the server detects it");
        Assert.AreEqual("de", (string?)sent["target"]);
        Assert.AreEqual("secret", (string?)sent["api_key"]);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AGivenSourceLanguageIsSentAsItIs()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, """{"translatedText":"x"}"""));

        TranslationResult result = await Provider(handler).TranslateAsync("a", "fr", "de");

        Assert.AreEqual("fr", (string?)JsonNode.Parse(handler.Requests[0].Body)!["source"]);
        Assert.IsNull(result.DetectedSource);
        Assert.IsNull(JsonNode.Parse(handler.Requests[0].Body)!["api_key"], "no key is sent when none is set");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task DetectionPicksTheMostConfidentHitAndScalesItToOne()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, """[{"confidence":12,"language":"nl"},{"confidence":88,"language":"de"}]"""));

        LanguageDetection? detection = await Provider(handler).DetectAsync("Guten Tag");

        Assert.AreEqual("de", detection!.Code);
        Assert.AreEqual(0.88, detection.Confidence, 0.001);
        Assert.AreEqual("https://translate.test/detect", handler.Requests[0].Uri!.ToString());
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task NoHitsMeansNoDetection()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, "[]"));

        Assert.IsNull(await Provider(handler).DetectAsync("?"));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARefusedRequestSaysWhatToDo()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.Forbidden, """{"error":"Visit libretranslate.com to get an API key"}"""));

        TranslationException exception = await Assert.ThrowsExactlyAsync<TranslationException>(() => Provider(handler).TranslateAsync("a", null, "de"));

        StringAssert.Contains(exception.Message, "Visit libretranslate.com to get an API key");
        StringAssert.Contains(exception.Message, "403");
        StringAssert.Contains(exception.Message, "LIBRETRANSLATE_API_KEY");
    }

    [Timeout(60_000)]
    [TestMethod]
    [DataRow(HttpStatusCode.Forbidden, false, "an API key is needed")]
    [DataRow(HttpStatusCode.Unauthorized, true, "the API key was refused")]
    [DataRow(HttpStatusCode.NotFound, false, "Translation:ApiUrl")]
    [DataRow(HttpStatusCode.TooManyRequests, false, "rate limited")]
    [DataRow(HttpStatusCode.BadGateway, false, "server error")]
    public void EachStatusHasItsOwnAdvice(HttpStatusCode status, bool keySet, string expected)
    {
        StringAssert.Contains(LibreTranslateProvider.Describe(status, "not json", keySet), expected);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnreachableServerIsReportedNotThrownRaw()
    {
        var provider = new LibreTranslateProvider("https://translate.test", () => null, new HttpClient(new ThrowingHandler()));

        TranslationException exception = await Assert.ThrowsExactlyAsync<TranslationException>(() => provider.TranslateAsync("a", null, "de"));

        StringAssert.Contains(exception.Message, "unreachable");
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("no route to host");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WithoutAnApiUrlNothingIsSent()
    {
        var handler = new StubHandler((_, _) => (HttpStatusCode.OK, "{}"));
        LibreTranslateProvider provider = Provider(handler, url: "  ");

        Assert.IsNotNull(provider.UnavailableReason);
        await Assert.ThrowsExactlyAsync<TranslationException>(() => provider.TranslateAsync("a", null, "de"));
        Assert.AreEqual(0, handler.Requests.Count);
    }

    [Timeout(60_000)]
    [TestMethod]
    public void OnlyAnHttpAddressIsAccepted()
    {
        Assert.IsNotNull(new LibreTranslateProvider("file:///etc/passwd", () => null).UnavailableReason);
        Assert.IsNotNull(new LibreTranslateProvider("not a url", () => null).UnavailableReason);
        Assert.IsNull(new LibreTranslateProvider("http://localhost:5000", () => null).UnavailableReason);
    }
}
