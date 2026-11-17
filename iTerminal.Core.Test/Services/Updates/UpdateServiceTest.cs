using System.IO.Compression;
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

    /// <summary>A portable archive as release.yml builds it: the marker, the program and a folder of its own.</summary>
    private static readonly byte[] Portable = BuildArchive(true, ("iTerminal.exe", [1, 2, 3]), ("Assets/readme.txt", "hi"u8.ToArray()));

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

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] BuildArchive(bool marker, params (string Name, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (marker)
            {
                zip.CreateEntry("iTerminal.portable").Open().Dispose();
            }

            foreach ((string name, byte[] bytes) in entries)
            {
                using Stream stream = zip.CreateEntry(name).Open();
                stream.Write(bytes);
            }
        }

        return memory.ToArray();
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, string tags, byte[] portable)
    {
        string url = request.RequestUri!.ToString();
        if (url.Contains("/releases/tags/"))
        {
            return Json($$"""
                { "tag_name": "v1.2.0", "assets": [
                  { "name": "iTerminal-1.2.0-x64.msi", "browser_download_url": "https://downloads.test/a.msi", "size": 4, "digest": "sha256:{{Sha256(Msi)}}" },
                  { "name": "iTerminal-1.2.0-x64-portable.zip", "browser_download_url": "https://downloads.test/p.zip", "size": {{portable.Length}}, "digest": "sha256:{{Sha256(portable)}}" }
                ] }
                """);
        }

        if (url.Contains("/releases"))
        {
            return Json(tags);
        }

        byte[] body = url.EndsWith(".zip", StringComparison.Ordinal) ? portable : Msi;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    private static GitHubReleaseFeed FeedFor(byte[]? portable = null) =>
        new(new HttpClient(new StubHandler(request => Respond(request, Tags, portable ?? Portable))), Api);

    private static UpdateService NewService(
        string current,
        string tags,
        ISettingsService? settings = null,
        Func<string, bool>? start = null,
        UpdateInstallKind kind = UpdateInstallKind.Installer,
        string? directory = null,
        IAuthenticodeVerifier? authenticode = null) =>
        new(
            settings ?? new SettingsService(),
            new GitHubReleaseFeed(new HttpClient(new StubHandler(request => Respond(request, tags, Portable))), Api),
            current,
            start ?? (_ => true),
            directory ?? Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}"),
            kind,
            authenticode ?? Signed());

    private static UpdateService NewPortable(
        string directory,
        string installDirectory,
        Func<string, bool> startScript,
        GitHubReleaseFeed? feed = null,
        IAuthenticodeVerifier? authenticode = null,
        Func<string, bool>? startInstaller = null) =>
        new(
            new SettingsService(),
            feed ?? FeedFor(),
            "1.1.0",
            startInstaller ?? (_ => throw new InvalidOperationException("The MSI must not start for a portable copy.")),
            directory,
            UpdateInstallKind.PortableArchive,
            authenticode ?? Signed(),
            startScript,
            installDirectory);

    private static string NewDirectory() => Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}");

    private static void DeleteQuietly(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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
            UpdateInstallKind.Installer);

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
        string directory = NewDirectory();
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
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AVerifiedInstallerLogsItsSignerAndStarts()
    {
        string directory = NewDirectory();
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
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnUnsignedInstallerIsWarnedAboutAndStillStarted()
    {
        string directory = NewDirectory();
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
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallerWithAnInvalidSignatureIsNotStartedAndIsRemoved()
    {
        string directory = NewDirectory();
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
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallerWhoseSignatureCannotBeCheckedIsNotStarted()
    {
        string directory = NewDirectory();
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
            DeleteQuietly(directory);
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
        var service = new UpdateService(new SettingsService(), feed, "1.1.0", _ => started = true, Path.Combine(Path.GetTempPath(), $"iTerminal.Update.{Guid.NewGuid():N}"), UpdateInstallKind.Installer);
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
        UpdateService service = NewService("1.1.0", Tags, kind: UpdateInstallKind.Unsupported);

        UpdateInfo? update = await service.CheckAsync(CancellationToken.None);

        Assert.AreEqual("1.2.0", update?.Version);
        Assert.IsNull(update?.Installer);
        Assert.AreEqual(UpdateInstallKind.Unsupported, service.InstallKind);
        Assert.IsFalse(service.CanInstall);
        Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
    }

    [TestMethod]
    public void TheMarkerBesideTheProgramMakesAnUnzippedCopyAPortableOne()
    {
        string directory = NewDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            Assert.AreEqual(UpdateInstallKind.Installer, UpdateService.KindFor(windows: true, directory), "A copy without the marker came from the installer.");

            File.WriteAllText(Path.Combine(directory, UpdateService.PortableMarker), string.Empty);
            Assert.AreEqual(UpdateInstallKind.PortableArchive, UpdateService.KindFor(windows: true, directory));
            Assert.AreEqual(UpdateInstallKind.Unsupported, UpdateService.KindFor(windows: false, directory), "Only Windows updates itself.");
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APortableCopyDownloadsTheArchiveAndSchedulesItsReplacement()
    {
        string directory = NewDirectory();
        string install = Path.Combine(directory, "app");
        string? script = null;
        try
        {
            UpdateService service = NewPortable(directory, install, path => { script = path; return true; });

            UpdateInfo? update = await service.CheckAsync(CancellationToken.None);
            Assert.AreEqual("iTerminal-1.2.0-x64-portable.zip", update?.Installer?.Name, "A portable copy takes the archive, not the MSI.");
            Assert.IsTrue(await service.InstallAsync(CancellationToken.None));

            Assert.AreEqual(Path.Combine(directory, "apply-update.cmd"), script);
            string text = File.ReadAllText(script!);
            StringAssert.Contains(text, $"set \"PID={Environment.ProcessId}\"");
            StringAssert.Contains(text, "PID eq %PID%", "The script waits for iTerminal to close before it copies.");
            StringAssert.Contains(text, $"robocopy \"{Path.Combine(directory, "staging")}\" \"{install}\" /E");
            StringAssert.Contains(text, $"start \"\" \"{Path.Combine(install, "iTerminal.exe")}\"");
            Assert.IsTrue(File.Exists(Path.Combine(directory, "staging", "iTerminal.exe")));
            Assert.IsTrue(File.Exists(Path.Combine(directory, "staging", "Assets", "readme.txt")));
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnArchiveWithoutThePortableMarkerIsNotAPortableRelease()
    {
        string directory = NewDirectory();
        string? script = null;
        try
        {
            var feed = FeedFor(BuildArchive(false, ("iTerminal.exe", [1, 2, 3])));
            UpdateService service = NewPortable(directory, Path.Combine(directory, "app"), path => { script = path; return true; }, feed);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsNull(script);
            Assert.IsTrue(service.Log.Any(line => line.Contains("not a portable iTerminal release")));
            Assert.IsFalse(Directory.Exists(Path.Combine(directory, "staging")), "The unpacked files are removed.");
            Assert.IsFalse(File.Exists(Path.Combine(directory, "iTerminal-1.2.0-x64-portable.zip")), "The download is removed.");
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnEntryThatWouldLeaveTheUpdateFolderStopsTheUnpacking()
    {
        string directory = NewDirectory();
        string? script = null;
        try
        {
            var feed = FeedFor(BuildArchive(true, ("iTerminal.exe", [1]), ("../escape.txt", [2])));
            UpdateService service = NewPortable(directory, Path.Combine(directory, "app"), path => { script = path; return true; }, feed);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsNull(script);
            Assert.IsFalse(File.Exists(Path.Combine(directory, "escape.txt")), "Nothing is written outside the update folder.");
            Assert.IsTrue(service.Log.Any(line => line.Contains("outside the update folder")));
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task APortableProgramWhoseSignatureIsNotAcceptedIsNotScheduled()
    {
        string directory = NewDirectory();
        string? script = null;
        try
        {
            var verifier = new FakeAuthenticodeVerifier(new(AuthenticodeStatus.Invalid, "CN=Someone Else", "The signature does not match the file."));
            UpdateService service = NewPortable(directory, Path.Combine(directory, "app"), path => { script = path; return true; }, authenticode: verifier);
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsNull(script);
            Assert.IsTrue(service.Log.Any(line => line.Contains("was not accepted")));
            Assert.IsFalse(Directory.Exists(Path.Combine(directory, "staging")));
            Assert.IsFalse(File.Exists(Path.Combine(directory, "iTerminal-1.2.0-x64-portable.zip")));
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallFolderThatCannotGoIntoTheScriptIsRefused()
    {
        string directory = NewDirectory();
        string? script = null;
        try
        {
            // A percent sign would be read as a variable by the batch file, so the copy is not attempted.
            UpdateService service = NewPortable(directory, Path.Combine(directory, "100%app"), path => { script = path; return true; });
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsNull(script);
            Assert.IsTrue(service.Log.Any(line => line.Contains("cannot be applied")));
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void TheScriptRemovesOnlyTheStagedFilesAndItself()
    {
        string script = UpdateService.UpdateScript(4242, @"C:\Temp\staging", @"C:\Tools\iTerminal", @"C:\Temp\iTerminal.zip");

        StringAssert.Contains(script, "set \"PID=4242\"");
        StringAssert.Contains(script, "robocopy \"C:\\Temp\\staging\" \"C:\\Tools\\iTerminal\" /E");
        StringAssert.Contains(script, "rd /s /q \"C:\\Temp\\staging\"");
        StringAssert.Contains(script, "del \"C:\\Temp\\iTerminal.zip\"");
        StringAssert.Contains(script, $"start \"\" \"{Path.Combine(@"C:\Tools\iTerminal", "iTerminal.exe")}\"");
        StringAssert.Contains(script, "del \"%~f0\"");
        Assert.IsFalse(script.Contains("/MIR", StringComparison.Ordinal), "Mirroring would delete the person's own files in the install folder.");
    }
}
