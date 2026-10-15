using System.Net;
using System.Text;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class NuGetPackageFeedTest
{
    /// <summary>Answers every request with one canned response and remembers the addresses asked for.</summary>
    private sealed class StubHandler(Func<Uri, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request.RequestUri!));
        }
    }

    [TestMethod]
    public async Task ListVersionsReadsThePublishedVersionsFromTheLowerCaseIndex()
    {
        const string index = """{"versions":["1.62.0","1.63.0","1.64.0-alpha.1"]}""";
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(index, Encoding.UTF8, "application/json") });
        var feed = new NuGetPackageFeed(new HttpClient(handler));

        IReadOnlyList<string> versions = await feed.ListVersionsAsync("Microsoft.Playwright", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "1.62.0", "1.63.0", "1.64.0-alpha.1" }, versions.ToList());
        Assert.AreEqual("https://api.nuget.org/v3-flatcontainer/microsoft.playwright/index.json", handler.Requests.Single().ToString());
    }

    [TestMethod]
    public async Task ListVersionsFailsWhenTheFeedDoesNotAnswer()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var feed = new NuGetPackageFeed(new HttpClient(handler));

        await Assert.ThrowsAsync<HttpRequestException>(() => feed.ListVersionsAsync("Microsoft.Playwright", CancellationToken.None));
    }

    private static string Sha512Of(byte[] content) => Convert.ToBase64String(System.Security.Cryptography.SHA512.HashData(content));

    /// <summary>The registration and catalog answers for a package with the given hash; anything else is the package itself.</summary>
    private static HttpResponseMessage AnswerDownload(Uri uri, string hash, byte[] package)
    {
        string address = uri.ToString();
        if (address.Contains("/registration5-semver1/", StringComparison.Ordinal))
        {
            return Json("""{"catalogEntry":"https://api.nuget.org/v3/catalog0/data/example/microsoft.playwright.1.63.0.json"}""");
        }

        if (address.Contains("/catalog0/", StringComparison.Ordinal))
        {
            return Json($$"""{"packageHash":"{{hash}}","packageHashAlgorithm":"SHA512","version":"1.63.0"}""");
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package) };
    }

    private static HttpResponseMessage Json(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };

    [TestMethod]
    public async Task DownloadWritesThePackageAfterItsChecksumMatches()
    {
        byte[] package = Enumerable.Range(0, 300_000).Select(i => (byte)i).ToArray();
        var handler = new StubHandler(uri => AnswerDownload(uri, Sha512Of(package), package));
        var feed = new NuGetPackageFeed(new HttpClient(handler));
        string destination = Path.Combine(Path.GetTempPath(), $"iTerminal.Feed.{Guid.NewGuid():N}.nupkg");
        var lines = new List<string>();
        try
        {
            await feed.DownloadAsync("Microsoft.Playwright", "1.63.0", destination, new DelegateProgress(lines.Add), CancellationToken.None);

            CollectionAssert.AreEqual(package, File.ReadAllBytes(destination));
            CollectionAssert.Contains(handler.Requests.Select(uri => uri.ToString()).ToList(),
                "https://api.nuget.org/v3-flatcontainer/microsoft.playwright/1.63.0/microsoft.playwright.1.63.0.nupkg");
            StringAssert.StartsWith(lines.Last(), "Downloaded Microsoft.Playwright 1.63.0");
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [TestMethod]
    public async Task ADownloadThatDoesNotMatchItsPublishedChecksumIsDeleted()
    {
        byte[] package = Enumerable.Range(0, 1_000).Select(i => (byte)i).ToArray();
        var handler = new StubHandler(uri => AnswerDownload(uri, Sha512Of([1, 2, 3]), package));
        var feed = new NuGetPackageFeed(new HttpClient(handler));
        string destination = Path.Combine(Path.GetTempPath(), $"iTerminal.Feed.{Guid.NewGuid():N}.nupkg");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            feed.DownloadAsync("Microsoft.Playwright", "1.63.0", destination, null, CancellationToken.None));

        Assert.IsFalse(File.Exists(destination));
    }

    private sealed class DelegateProgress(Action<string> onLine) : IProgress<string>
    {
        public void Report(string value) => onLine(value);
    }
}
