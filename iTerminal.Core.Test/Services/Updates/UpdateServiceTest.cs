using System.Net;
using System.Security.Cryptography;
using System.Text;
using iTerminal.Core.Services;
using iTerminal.Core.Services.RuntimeDependencies;
using iTerminal.Core.Services.Updates;

namespace iTerminal.Core.Test.Services.Updates;

[TestClass]
public class UpdateServiceTest
{
    private const string Api = "https://api.github.com/";
    private static readonly byte[] Msi = [9, 8, 7, 6];

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class FakeAuthenticodeVerifier(AuthenticodeResult result) : IAuthenticodeVerifier
    {
        public int Calls { get; private set; }

        public AuthenticodeResult Verify(string filePath)
        {
            Calls++;
            return result;
        }
    }

    private static FakeAuthenticodeVerifier Signed(string signer = "CN=iTerminal Test") =>
        new(new(AuthenticodeStatus.Valid, signer, "The signature is valid."));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Respond(HttpRequestMessage request, string tags)
    {
        string url = request.RequestUri!.ToString();
        if (url.Contains("/releases/tags/"))
        {
            string digest = Convert.ToHexString(SHA256.HashData(Msi)).ToLowerInvariant();
            return Json($$"""
                { "tag_name": "v1.2.0", "assets": [
                  { "name": "iTerminal-1.2.0-x64.msi", "browser_download_url": "https://downloads.test/a.msi", "size": 4, "digest": "sha256:{{digest}}" }
                ] }
                """);
        }

        if (url.Contains("/releases"))
        {
            return Json(tags);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Msi) };
    }

    private static UpdateService NewService(
        string current,
        string tags,
        ISettingsService? settings = null,
        Func<string, bool>? start = null,
        bool canInstall = true,
        string? directory = null,
        IAuthenticodeVerifier? authenticode = null) =>
        new(
            settings ?? new SettingsService(),
            new GitHubReleaseFeed(new HttpClient(new StubHandler(request => Respond(request, tags))), Api),
            current,
            start ?? (_ => true),
            directory ?? Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}"),
            canInstall,
            authenticode ?? Signed());

    private const string Tags = """
        [
          { "tag_name": "v1.2.0", "draft": false, "prerelease": false, "assets": [] },
          { "tag_name": "v1.3.0-rc1", "draft": false, "prerelease": true, "assets": [] },
          { "tag_name": "v1.1.0", "draft": false, "prerelease": false, "assets": [] }
        ]
        """;

    [Timeout(60_000)]
    [TestMethod]
    public async Task ANewerReleaseIsAvailableWithItsInstaller()
    {
        UpdateService service = NewService("1.1.0", Tags);

        UpdateInfo? update = await service.CheckAsync(CancellationToken.None);

        Assert.AreEqual("1.2.0", update?.Version);
        Assert.AreEqual("iTerminal-1.2.0-x64.msi", update?.Installer?.Name);
        Assert.AreSame(update, service.Available);
        Assert.IsFalse(service.IsSkipped);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task TheBuildNumberAfterThreeZeros_IsComparedToo()
    {
        const string tags = """[ { "tag_name": "v0.0.0.12", "draft": false, "prerelease": false, "assets": [] } ]""";

        Assert.IsNotNull(await NewService("0.0.0.11", tags).CheckAsync(CancellationToken.None));
        Assert.IsNull(await NewService("0.0.0.12", tags).CheckAsync(CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ARunningVersionThatIsCurrentOrNewerHasNothingToUpdate()
    {
        Assert.IsNull(await NewService("1.2.0", Tags).CheckAsync(CancellationToken.None));
        Assert.IsNull(await NewService("2.0.0+abcdef", Tags).CheckAsync(CancellationToken.None));
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnOfflineCheckOffersNothingAndDoesNotThrow()
    {
        var service = new UpdateService(
            new SettingsService(),
            new GitHubReleaseFeed(new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline"))), Api),
            "1.0.0",
            _ => true,
            Path.GetTempPath(),
            canInstall: true);

        Assert.IsNull(await service.CheckAsync(CancellationToken.None));
        Assert.IsNull(service.Available);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task SkippingHidesThatReleaseOnly()
    {
        var settings = new SettingsService();
        UpdateService service = NewService("1.1.0", Tags, settings);
        await service.CheckAsync(CancellationToken.None);

        service.Skip();

        Assert.IsTrue(service.IsSkipped);
        UpdateService next = NewService("1.1.0", Tags, settings);
        await next.CheckAsync(CancellationToken.None);
        Assert.IsTrue(next.IsSkipped, "The skip is remembered by the next start.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task InstallDownloadsTheCheckedInstallerAndStartsIt()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}");
        string? started = null;
        try
        {
            UpdateService service = NewService("1.1.0", Tags, start: path => { started = path; return true; }, directory: directory);
            await service.CheckAsync(CancellationToken.None);

            bool result = await service.InstallAsync(CancellationToken.None);

            Assert.IsTrue(result);
            Assert.AreEqual(Path.Combine(directory, "iTerminal-1.2.0-x64.msi"), started);
            CollectionAssert.AreEqual(Msi, File.ReadAllBytes(started!));
            Assert.IsFalse(service.IsBusy);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AVerifiedInstallerLogsItsSignerAndStarts()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}");
        bool started = false;
        try
        {
            var verifier = Signed("CN=iTerminal Publisher");
            UpdateService service = NewService("1.1.0", Tags, start: _ => { started = true; return true; }, directory: directory, authenticode: verifier);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsTrue(await service.InstallAsync(CancellationToken.None));
            Assert.IsTrue(started);
            Assert.AreEqual(1, verifier.Calls);
            Assert.IsTrue(service.Log.Any(line => line.Contains("CN=iTerminal Publisher")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnsignedInstallerIsWarnedAboutAndStillStarted()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}");
        bool started = false;
        try
        {
            var verifier = new FakeAuthenticodeVerifier(new(AuthenticodeStatus.NotSigned, string.Empty, "The file is not Authenticode-signed."));
            UpdateService service = NewService("1.1.0", Tags, start: _ => { started = true; return true; }, directory: directory, authenticode: verifier);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsTrue(await service.InstallAsync(CancellationToken.None));
            Assert.IsTrue(started);
            Assert.IsTrue(service.Log.Any(line => line.Contains("not Authenticode-signed")));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallerWithAnInvalidSignatureIsNotStartedAndIsRemoved()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}");
        bool started = false;
        try
        {
            var verifier = new FakeAuthenticodeVerifier(new(AuthenticodeStatus.Invalid, "CN=Someone Else", "The signature does not match the file."));
            UpdateService service = NewService("1.1.0", Tags, start: _ => { started = true; return true; }, directory: directory, authenticode: verifier);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsFalse(started);
            Assert.IsTrue(service.Log.Any(line => line.Contains("was not accepted")));
            Assert.IsFalse(File.Exists(Path.Combine(directory, "iTerminal-1.2.0-x64.msi")), "A refused installer is removed.");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallerWhoseSignatureCannotBeCheckedIsNotStarted()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}");
        bool started = false;
        try
        {
            var verifier = new FakeAuthenticodeVerifier(new(AuthenticodeStatus.Unavailable, string.Empty, "WinVerifyTrust answered 0x80000000."));
            UpdateService service = NewService("1.1.0", Tags, start: _ => { started = true; return true; }, directory: directory, authenticode: verifier);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsFalse(started);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AFailedInstallIsLoggedAndNothingIsStarted()
    {
        bool started = false;
        // A release whose installer has no checksum is refused.
        var feed = new GitHubReleaseFeed(new HttpClient(new StubHandler(request => request.RequestUri!.ToString().Contains("/tags/")
            ? Json("""{ "assets": [ { "name": "a.msi", "browser_download_url": "https://downloads.test/a.msi", "size": 4 } ] }""")
            : Json(Tags))), Api);
        var service = new UpdateService(new SettingsService(), feed, "1.1.0", _ => started = true, Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}"), canInstall: true);
        await service.CheckAsync(CancellationToken.None);

        bool result = await service.InstallAsync(CancellationToken.None);

        Assert.IsFalse(result);
        Assert.IsFalse(started);
        StringAssert.Contains(service.Log.Last(), "could not be installed");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task WhereNoInstallerCanRun_OnlyTheReleaseIsReported()
    {
        UpdateService service = NewService("1.1.0", Tags, canInstall: false);

        UpdateInfo? update = await service.CheckAsync(CancellationToken.None);

        Assert.AreEqual("1.2.0", update?.Version);
        Assert.IsNull(update?.Installer);
        Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
    }
}
