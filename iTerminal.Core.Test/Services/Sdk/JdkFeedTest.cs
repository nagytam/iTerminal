using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iTerminal.Core.Services.Sdk;

namespace iTerminal.Core.Test.Services.Sdk;

[TestClass]
public class JdkFeedTest
{
    private const string Assets = """
        [{"release_name":"jdk-21.0.13.1+2","binaries":[
            {"os":"linux","architecture":"x64","image_type":"jre","heap_size":"normal","package":{"name":"jre.tar.gz","link":"https://example.test/jre.tar.gz","checksum":"0011","size":1}},
            {"os":"linux","architecture":"x64","image_type":"jdk","heap_size":"normal","package":{"name":"OpenJDK21U-jdk_x64_linux.tar.gz","link":"https://example.test/jdk.tar.gz","checksum":"ABCD","size":207869899}},
            {"os":"windows","architecture":"x64","image_type":"jdk","heap_size":"normal","package":{"name":"jdk.zip","link":"https://example.test/jdk.zip","checksum":"FFFF","size":2}}]},
         {"release_name":"jdk-21.0.12+7","binaries":[
            {"os":"linux","architecture":"x64","image_type":"jdk","heap_size":"normal","package":{"name":"old.tar.gz","link":"https://example.test/old.tar.gz","checksum":"1234"}}]}]
        """;

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [TestMethod]
    public void ParseKeepsTheJdkArchiveOfThePlatformAndMarksTheNewestBuild()
    {
        using var document = JsonDocument.Parse(Assets);

        var sdks = JdkFeed.Parse(document.RootElement, 21, "linux", "x64");

        Assert.AreEqual(2, sdks.Count);
        Assert.AreEqual(SdkKind.Jdk, sdks[0].Kind);
        Assert.AreEqual("jdk-21.0.13.1+2", sdks[0].Version);
        Assert.AreEqual("21", sdks[0].Channel);
        Assert.AreEqual("OpenJDK21U-jdk_x64_linux.tar.gz", sdks[0].FileName, "the jdk image, not the jre");
        Assert.AreEqual("abcd", sdks[0].Digest);
        Assert.AreEqual(HashAlgorithmName.SHA256, sdks[0].Algorithm);
        Assert.AreEqual(207869899L, sdks[0].Size);
        Assert.IsTrue(sdks[0].IsLatest);
        Assert.IsFalse(sdks[1].IsLatest);
        Assert.IsNull(sdks[1].Size, "a size the publisher does not list is null");
    }

    [TestMethod]
    public void EachFeatureVersionMarksItsOwnNewestBuild()
    {
        using var document = JsonDocument.Parse(Assets);

        var first = JdkFeed.Parse(document.RootElement, 21, "linux", "x64");
        var second = JdkFeed.Parse(document.RootElement, 17, "linux", "x64");

        Assert.IsTrue(first[0].IsLatest);
        Assert.IsTrue(second[0].IsLatest, "the newest build of a feature version is its latest, whatever the other feature versions list");
    }

    [TestMethod]
    public async Task ListAsksForTheLtsAndTheMostRecentFeatureVersions()
    {
        const string info = """{"available_lts_releases":[17,21],"most_recent_feature_release":27}""";
        var requested = new List<string>();
        var handler = new StubHandler(request =>
        {
            string url = request.RequestUri!.ToString();
            requested.Add(url);
            string body = url.EndsWith("available_releases", StringComparison.Ordinal) ? info : Assets;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        });
        var feed = new JdkFeed(new HttpClient(handler), "https://example.test/v3/");

        var sdks = await feed.ListAsync("linux", "x64", CancellationToken.None);

        Assert.IsTrue(requested.Any(url => url.Contains("feature_releases/27/ga")), "the most recent feature release is listed");
        Assert.IsTrue(requested.Any(url => url.Contains("feature_releases/21/ga")));
        Assert.IsTrue(requested.Any(url => url.Contains("feature_releases/17/ga")));
        Assert.IsFalse(requested.Any(url => url.Contains("feature_releases/11/ga")), "an LTS version that is not listed is not fetched");
        Assert.AreEqual(6, sdks.Count, "two builds for each of the three feature versions");
    }
}
