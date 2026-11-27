using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iTerminal.Core.Services.Sdk;

namespace iTerminal.Core.Test.Services.Sdk;

[TestClass]
public class DotNetSdkFeedTest
{
    private const string Releases = """
        {"releases":[
          {"release-version":"10.0.4","sdks":[
            {"version":"10.0.404","files":[
              {"name":"dotnet-sdk-linux-x64.tar.gz","rid":"linux-x64","url":"https://example.test/dotnet-sdk-10.0.404-linux-x64.tar.gz","hash":"AA11"},
              {"name":"dotnet-sdk-win-x64.zip","rid":"win-x64","url":"https://example.test/dotnet-sdk-10.0.404-win-x64.zip","hash":"BB22"},
              {"name":"dotnet-sdk-win-x64.exe","rid":"win-x64","url":"https://example.test/dotnet-sdk-10.0.404-win-x64.exe","hash":"CC33"}]},
            {"version":"10.0.302","files":[
              {"name":"dotnet-sdk-linux-x64.tar.gz","rid":"linux-x64","url":"https://example.test/dotnet-sdk-10.0.302-linux-x64.tar.gz","hash":"DD44"}]}]}]}
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [TestMethod]
    public void ParseKeepsTheArchivesOfThePlatformAndMarksTheLatestSdk()
    {
        using var document = JsonDocument.Parse(Releases);

        var sdks = DotNetSdkFeed.Parse(document.RootElement, "10.0", "10.0.404", "linux-x64", zip: false);

        Assert.AreEqual(2, sdks.Count);
        Assert.AreEqual("10.0.404", sdks[0].Version);
        Assert.AreEqual("10.0", sdks[0].Channel);
        Assert.AreEqual("dotnet-sdk-10.0.404-linux-x64.tar.gz", sdks[0].FileName);
        Assert.AreEqual("aa11", sdks[0].Digest, "a digest is kept in lower case, for the comparison");
        Assert.AreEqual(System.Security.Cryptography.HashAlgorithmName.SHA512, sdks[0].Algorithm);
        Assert.IsTrue(sdks[0].IsLatest);
        Assert.IsFalse(sdks[1].IsLatest, "only the channel's latest SDK is marked");
        Assert.AreEqual("10.0.302", sdks[1].Version);
    }

    [TestMethod]
    public void WindowsTakesTheZipArchiveAndNotTheInstaller()
    {
        using var document = JsonDocument.Parse(Releases);

        var sdks = DotNetSdkFeed.Parse(document.RootElement, "10.0", "10.0.404", "win-x64", zip: true);

        Assert.AreEqual(1, sdks.Count);
        Assert.AreEqual("dotnet-sdk-10.0.404-win-x64.zip", sdks[0].FileName);
    }

    [TestMethod]
    public void AnUnknownPlatformListsNothing()
    {
        using var document = JsonDocument.Parse(Releases);

        Assert.AreEqual(0, DotNetSdkFeed.Parse(document.RootElement, "10.0", "10.0.404", "osx-arm64", zip: false).Count);
    }

    [TestMethod]
    public void TheCurrentPlatformIsNamedAsTheFeedNamesIt()
    {
        // Linux names its distribution in RuntimeInformation.RuntimeIdentifier; the feed lists only the generic names.
        StringAssert.Matches(DotNetSdkFeed.CurrentRuntimeIdentifier(), new System.Text.RegularExpressions.Regex("^(linux|linux-musl|win|osx)-(x64|x86|arm64|arm)$"));
    }

    [TestMethod]
    public async Task ListReadsOnlyTheSupportedChannels()
    {
        const string index = """
            {"releases-index":[
              {"channel-version":"11.0","support-phase":"active","latest-sdk":"11.0.100","releases.json":"https://example.test/11.0/releases.json"},
              {"channel-version":"9.0","support-phase":"eol","latest-sdk":"9.0.321","releases.json":"https://example.test/9.0/releases.json"}]}
            """;
        var requested = new List<string>();
        var handler = new StubHandler(request =>
        {
            requested.Add(request.RequestUri!.ToString());
            string body = request.RequestUri.ToString().EndsWith("releases-index.json", StringComparison.Ordinal) ? index : Releases;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
        var feed = new DotNetSdkFeed(new HttpClient(handler), "https://example.test/releases-index.json");

        var sdks = await feed.ListAsync("linux-x64", zip: false, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "https://example.test/releases-index.json", "https://example.test/11.0/releases.json" }, requested.ToArray(),
            "an end-of-life channel is not even fetched");
        Assert.AreEqual(2, sdks.Count);
        Assert.AreEqual("11.0", sdks[0].Channel);
    }
}
