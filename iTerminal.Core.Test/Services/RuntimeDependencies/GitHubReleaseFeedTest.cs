using iTerminal.Core.Test.Helper;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class GitHubReleaseFeedTest
{
    private const string Api = "https://api.github.com/";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static readonly string Releases = """
        [
          { "tag_name": "v0.40.4", "draft": false, "prerelease": false, "assets": [] },
          { "tag_name": "v0.41.0-rc1", "draft": false, "prerelease": true, "assets": [] },
          { "tag_name": "v0.39.9", "draft": true, "prerelease": false, "assets": [] },
          { "tag_name": "nightly", "draft": false, "prerelease": false, "assets": [] },
          { "tag_name": "v0.40.3", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;

    [TestMethod]
    public async Task ListVersions_KeepsStableReleasesOnly()
    {
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(_ => Json(Releases))), Api);

        IReadOnlyList<string> versions = await feed.ListVersionsAsync("ollama/ollama", CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "0.40.4", "0.40.3" }, versions.ToArray());
    }

    [TestMethod]
    public async Task ListVersions_SendsTheUserAgentGitHubRequires()
    {
        string? userAgent = null;
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Json("[]");
        })), Api);

        await feed.ListVersionsAsync("ollama/ollama", CancellationToken.None);

        StringAssert.Contains(userAgent, "iTerminal");
    }

    [TestMethod]
    public async Task GetRelease_ReadsEachAssetWithItsDigest()
    {
        const string release = """
            { "tag_name": "v0.40.4", "assets": [
              { "name": "ollama-linux-amd64.tar.zst", "browser_download_url": "https://downloads.test/a", "size": 12, "digest": "sha256:ABCDEF" },
              { "name": "sha256sum.txt", "browser_download_url": "https://downloads.test/b", "size": 3 }
            ] }
            """;
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(_ => Json(release))), Api);

        GitHubRelease published = await feed.GetReleaseAsync("ollama/ollama", "0.40.4", CancellationToken.None);

        Assert.AreEqual("v0.40.4", published.Tag);
        Assert.AreEqual("ollama-linux-amd64.tar.zst", published.Assets[0].Name);
        Assert.AreEqual("abcdef", published.Assets[0].Sha256);
        Assert.AreEqual(12, published.Assets[0].Size);
        Assert.IsNull(published.Assets[1].Sha256);
    }

    [TestMethod]
    public async Task Download_KeepsTheFileWhenItMatchesItsDigest()
    {
        byte[] content = [1, 2, 3, 4];
        var asset = new GitHubAsset("ollama.zip", "https://downloads.test/ollama.zip", content.Length, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(_ => Bytes(content))), Api);
        string destination = Path.Combine(Path.GetTempPath(), $"iTerminal.Feed.{Guid.NewGuid():N}.zip");
        try
        {
            await feed.DownloadAsync(asset, destination, null, CancellationToken.None);

            CollectionAssert.AreEqual(content, File.ReadAllBytes(destination));
        }
        finally
        {
            File.Delete(destination);
        }
    }

    [TestMethod]
    public async Task Download_DeletesAFileThatDoesNotMatchItsDigest()
    {
        var asset = new GitHubAsset("ollama.zip", "https://downloads.test/ollama.zip", 4, new string('0', 64));
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(_ => Bytes([1, 2, 3, 4]))), Api);
        string destination = Path.Combine(Path.GetTempPath(), $"iTerminal.Feed.{Guid.NewGuid():N}.zip");

        await AssertAsync.ThrowsAsync<InvalidDataException>(() =>
            feed.DownloadAsync(asset, destination, null, CancellationToken.None));

        Assert.IsFalse(File.Exists(destination));
    }

    [TestMethod]
    public async Task Download_RefusesAnAssetWithoutAPublishedDigest()
    {
        var asset = new GitHubAsset("ollama.zip", "https://downloads.test/ollama.zip", 4, Sha256: null);
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(_ => Bytes([1, 2, 3, 4]))), Api);

        await AssertAsync.ThrowsAsync<InvalidDataException>(() =>
            feed.DownloadAsync(asset, Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zip"), null, CancellationToken.None));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Bytes(byte[] body) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body),
    };
}
