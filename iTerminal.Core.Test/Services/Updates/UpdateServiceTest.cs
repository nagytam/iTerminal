using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
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

    /// <summary>A Linux portable archive as release.yml builds it: a folder named after the release, holding the marker and the program.</summary>
    private static readonly byte[] LinuxPortable = BuildTarGz(
        "iTerminal-1.2.0-linux-x64-portable/",
        ("iTerminal-1.2.0-linux-x64-portable/iTerminal.portable", []),
        ("iTerminal-1.2.0-linux-x64-portable/iTerminal", [1, 2, 3]),
        ("iTerminal-1.2.0-linux-x64-portable/Assets/readme.txt", "hi"u8.ToArray()));

    private static byte[] BuildTarGz(string folder, params (string Name, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, folder) { Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute });
            foreach ((string name, byte[] bytes) in entries)
            {
                PaxTarEntry entry = new(TarEntryType.RegularFile, name)
                {
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    DataStream = new MemoryStream(bytes),
                };
                tar.WriteEntry(entry);
            }
        }

        return memory.ToArray();
    }

    private static HttpResponseMessage Respond(HttpRequestMessage request, string tags, byte[] portable, byte[] linux)
    {
        string url = request.RequestUri!.ToString();
        if (url.Contains("/releases/tags/"))
        {
            return Json($$"""
                { "tag_name": "v1.2.0", "assets": [
                  { "name": "iTerminal-1.2.0-x64.msi", "browser_download_url": "https://downloads.test/a.msi", "size": 4, "digest": "sha256:{{Sha256(Msi)}}" },
                  { "name": "iTerminal-1.2.0-x64-portable.zip", "browser_download_url": "https://downloads.test/p.zip", "size": {{portable.Length}}, "digest": "sha256:{{Sha256(portable)}}" },
                  { "name": "iTerminal-1.2.0-linux-x64-portable.tar.gz", "browser_download_url": "https://downloads.test/l.tar.gz", "size": {{linux.Length}}, "digest": "sha256:{{Sha256(linux)}}" }
                ] }
                """);
        }

        if (url.Contains("/releases"))
        {
            return Json(tags);
        }

        byte[] body = url.EndsWith(".zip", StringComparison.Ordinal) ? portable
            : url.EndsWith(".tar.gz", StringComparison.Ordinal) ? linux
            : Msi;
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
    }

    private static GitHubReleaseFeed FeedFor(byte[]? portable = null, byte[]? linux = null) =>
        new(new HttpClient(new StubHandler(request => Respond(request, Tags, portable ?? Portable, linux ?? LinuxPortable))), Api);

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
            new GitHubReleaseFeed(new HttpClient(new StubHandler(request => Respond(request, tags, Portable, LinuxPortable))), Api),
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
        Func<string, bool>? startInstaller = null,
        bool windows = true,
        string? portableSuffix = UpdateService.PortableArchiveSuffix) =>
        new(
            new SettingsService(),
            feed ?? FeedFor(),
            "1.1.0",
            startInstaller ?? (_ => throw new InvalidOperationException("The MSI must not start for a portable copy.")),
            directory,
            UpdateInstallKind.PortableArchive,
            authenticode ?? Signed(),
            startScript,
            installDirectory,
            windows,
            portableSuffix);

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
    public void AWinGetInstallIsLeftToWinGetAndAPortableCopyStillUpdatesItself()
    {
        string directory = NewDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            Assert.AreEqual(UpdateInstallKind.PackageManager, UpdateService.KindFor(windows: true, directory, managedByWinGet: true), "The WinGet marker makes the install WinGet's.");
            Assert.AreEqual(UpdateInstallKind.Installer, UpdateService.KindFor(windows: true, directory, managedByWinGet: false));
            Assert.AreEqual(UpdateInstallKind.Unsupported, UpdateService.KindFor(windows: false, directory, managedByWinGet: true), "The marker is a Windows registry value; elsewhere it means nothing.");

            File.WriteAllText(Path.Combine(directory, UpdateService.PortableMarker), string.Empty);
            Assert.AreEqual(UpdateInstallKind.PortableArchive, UpdateService.KindFor(windows: true, directory, managedByWinGet: true), "A portable copy is never WinGet's.");
        }
        finally
        {
            DeleteQuietly(directory);
        }
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
            Assert.AreEqual(UpdateInstallKind.PortableArchive, UpdateService.KindFor(windows: false, directory), "A portable Linux copy updates from its archive too.");

            File.Delete(Path.Combine(directory, UpdateService.PortableMarker));
            Assert.AreEqual(UpdateInstallKind.Unsupported, UpdateService.KindFor(windows: false, directory), "A Linux package is only announced.");
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
    public async Task AnInstallFolderWithAPercentSignIsEscapedForTheScript()
    {
        string directory = NewDirectory();
        string install = Path.Combine(directory, "100%app");
        string? script = null;
        try
        {
            // The batch file reads a bare percent sign as a variable, so it is doubled in the script.
            UpdateService service = NewPortable(directory, install, path => { script = path; return true; });
            await service.CheckAsync(CancellationToken.None);

            Assert.IsTrue(await service.InstallAsync(CancellationToken.None));
            string text = File.ReadAllText(script!);
            StringAssert.Contains(text, $"\"{Path.Combine(directory, "backup")}\" /E", "The install folder is backed up first.");
            StringAssert.Contains(text, $"start \"\" \"{Path.Combine(install.Replace("%", "%%"), "iTerminal.exe")}\"");
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task AnInstallFolderThatCannotTakeTheFilesIsRefusedBeforeITerminalCloses()
    {
        string directory = NewDirectory();
        string? script = null;
        try
        {
            // A file where the install folder's parent should be: no folder can be created inside it, on any system.
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "blocked"), string.Empty);
            UpdateService service = NewPortable(directory, Path.Combine(directory, "blocked", "app"), path => { script = path; return true; });
            await service.CheckAsync(CancellationToken.None);

            Assert.IsFalse(await service.InstallAsync(CancellationToken.None));
            Assert.IsNull(script, "The script is not started, so iTerminal does not close.");
            Assert.IsTrue(service.Log.Any(line => line.Contains("cannot write to")));
            Assert.IsFalse(Directory.Exists(Path.Combine(directory, "staging")), "The unpacked files are removed.");
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void TheLinuxUpdateFolderIsInThePersonsDataFolderNotTheSharedTemp()
    {
        Assert.AreEqual(Path.Combine(Path.GetTempPath(), "iTerminal-update"), UpdateService.DefaultDownloadDirectory(windows: true));
        Assert.AreEqual(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "iTerminal", "update"),
            UpdateService.DefaultDownloadDirectory(windows: false),
            "The shared temp folder is not used, so another user cannot pre-create the update folder.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task ALinuxPortableCopyDownloadsItsTarArchiveAndSchedulesAShellScript()
    {
        string directory = NewDirectory();
        string install = Path.Combine(directory, "app");
        string? script = null;
        try
        {
            var verifier = Signed();
            UpdateService service = NewPortable(
                directory,
                install,
                path => { script = path; return true; },
                authenticode: verifier,
                windows: false,
                portableSuffix: "-linux-x64-portable.tar.gz");

            UpdateInfo? update = await service.CheckAsync(CancellationToken.None);
            Assert.AreEqual("iTerminal-1.2.0-linux-x64-portable.tar.gz", update?.Installer?.Name, "A Linux portable copy takes the tar.gz of its architecture.");
            Assert.IsTrue(await service.InstallAsync(CancellationToken.None));

            Assert.AreEqual(0, verifier.Calls, "Linux releases carry no Authenticode signature to check.");
            Assert.AreEqual(Path.Combine(directory, "apply-update.sh"), script);
            string text = File.ReadAllText(script!);
            StringAssert.Contains(text, $"PID={Environment.ProcessId}");
            StringAssert.Contains(text, "kill -0 \"$PID\"", "The script waits for iTerminal to close before it copies.");
            string root = Path.Combine(directory, "staging", "iTerminal-1.2.0-linux-x64-portable");
            StringAssert.Contains(text, $"cp -a '{root}/.' '{install}/'");
            StringAssert.Contains(text, "rm -f \"$0\"");
            Assert.IsFalse(text.Contains("start ", StringComparison.Ordinal), "A Linux update is not restarted by the script.");

            Assert.IsTrue(File.Exists(Path.Combine(root, "iTerminal")));
            Assert.IsTrue(File.Exists(Path.Combine(root, "Assets", "readme.txt")));
            Assert.IsTrue(service.Log.Any(line => line.Contains("SHA-256 digest")));
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void TheLinuxArchiveIsTheOneForTheArchitectureOfThisSystem()
    {
        Assert.AreEqual("-linux-x64-portable.tar.gz", UpdateService.PortableSuffixFor(windows: false, Architecture.X64, macOS: false));
        Assert.AreEqual("-linux-arm64-portable.tar.gz", UpdateService.PortableSuffixFor(windows: false, Architecture.Arm64, macOS: false));
        Assert.IsNull(UpdateService.PortableSuffixFor(windows: false, Architecture.X86, macOS: false), "No portable archive is published for other architectures.");
        Assert.AreEqual(UpdateService.PortableArchiveSuffix, UpdateService.PortableSuffixFor(windows: true, Architecture.X64, macOS: false));
    }

    [TestMethod]
    public void TheMacArchiveIsNeverTheLinuxOne()
    {
        // A portable copy on macOS must look for its own archive, not a Linux one that would never be found there.
        Assert.AreEqual("-osx-x64-portable.tar.gz", UpdateService.PortableSuffixFor(windows: false, Architecture.X64, macOS: true));
        Assert.AreEqual("-osx-arm64-portable.tar.gz", UpdateService.PortableSuffixFor(windows: false, Architecture.Arm64, macOS: true));
        Assert.IsNull(UpdateService.PortableSuffixFor(windows: false, Architecture.X86, macOS: true));
    }

    [TestMethod]
    public void TheShellScriptQuotesItsPathsBacksUpAndRestoresTheInstallFolder()
    {
        string script = UpdateService.ShellUpdateScript(4242, "/home/me/stage/app", "/home/me/it's here", "/home/me/stage.tar.gz", "/home/me/stage", "/home/me/backup", "/home/me/update.log");

        StringAssert.Contains(script, "PID=4242");
        StringAssert.Contains(script, "exec >'/home/me/update.log' 2>&1", "The output goes to the log, not to /dev/null.");
        StringAssert.Contains(script, "cp -a '/home/me/it'\\''s here/.' '/home/me/backup/'", "The install folder is backed up first.");
        StringAssert.Contains(script, "cp -a '/home/me/stage/app/.' '/home/me/it'\\''s here/'");
        StringAssert.Contains(script, "cp -a '/home/me/backup/.' '/home/me/it'\\''s here/'", "A failed copy is restored from the backup.");
        StringAssert.Contains(script, "rm -rf '/home/me/stage'");
        StringAssert.Contains(script, "rm -f '/home/me/stage.tar.gz'");
        StringAssert.Contains(script, "rm -rf '/home/me/backup'");
    }

    [TestMethod]
    public void TheScriptBacksUpRestoresAndRemovesOnlyTheStagedFilesAndItself()
    {
        string script = UpdateService.UpdateScript(4242, @"C:\Temp\staging", @"C:\Tools\iTerminal", @"C:\Temp\iTerminal.zip", @"C:\Temp\backup", @"C:\Temp\apply-update.log");

        StringAssert.Contains(script, "set \"PID=4242\"");
        StringAssert.Contains(script, "robocopy \"C:\\Tools\\iTerminal\" \"C:\\Temp\\backup\" /E", "The install folder is backed up first.");
        StringAssert.Contains(script, "robocopy \"C:\\Temp\\staging\" \"C:\\Tools\\iTerminal\" /E");
        StringAssert.Contains(script, "robocopy \"C:\\Temp\\backup\" \"C:\\Tools\\iTerminal\" /E", "A failed copy is restored from the backup.");
        StringAssert.Contains(script, "rd /s /q \"C:\\Temp\\staging\"");
        StringAssert.Contains(script, "del \"C:\\Temp\\iTerminal.zip\"");
        StringAssert.Contains(script, "rd /s /q \"C:\\Temp\\backup\"");
        StringAssert.Contains(script, ">> \"C:\\Temp\\apply-update.log\"", "Progress is written to the log.");
        StringAssert.Contains(script, $"start \"\" \"{Path.Combine(@"C:\Tools\iTerminal", "iTerminal.exe")}\"");
        StringAssert.Contains(script, "del \"%~f0\"");
        Assert.IsFalse(script.Contains("/MIR", StringComparison.Ordinal), "Mirroring would delete the person's own files in the install folder.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLinuxScriptRunsAndReplacesTheInstallFolderThenCleansUp()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // The script runs for real with /bin/sh: a process id that does not exist means iTerminal has already closed.
        string directory = NewDirectory();
        try
        {
            string install = Path.Combine(directory, "app");
            string staging = Path.Combine(directory, "staging");
            string root = Path.Combine(staging, "iTerminal-1.2.0-linux-x64-portable");
            string backup = Path.Combine(directory, "backup");
            string archive = Path.Combine(directory, "iTerminal-1.2.0-linux-x64-portable.tar.gz");
            string log = Path.Combine(directory, "apply-update.log");
            string script = Path.Combine(directory, "apply-update.sh");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "iTerminal"), "old");
            File.WriteAllText(Path.Combine(install, "Local.txt"), "the person's own file");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "iTerminal"), "new");
            File.WriteAllText(archive, "archive");
            File.WriteAllText(script, UpdateService.ShellUpdateScript(int.MaxValue, root, install, archive, staging, backup, log));

            using (Process process = Process.Start(new ProcessStartInfo("/bin/sh") { UseShellExecute = false, ArgumentList = { script } })!)
            {
                process.WaitForExit();
            }

            Assert.AreEqual("new", File.ReadAllText(Path.Combine(install, "iTerminal")));
            Assert.AreEqual("the person's own file", File.ReadAllText(Path.Combine(install, "Local.txt")), "Files the release does not replace are kept.");
            Assert.IsFalse(Directory.Exists(staging), "The staged files are removed.");
            Assert.IsFalse(File.Exists(archive), "The archive is removed.");
            Assert.IsFalse(Directory.Exists(backup), "The backup is removed after a finished update.");
            Assert.IsFalse(File.Exists(script), "The script removes itself.");
            StringAssert.Contains(File.ReadAllText(log), "The update is in place.");
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void TheLinuxScriptRestoresThePreviousFilesWhenTheCopyFails()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        // A folder in the new release where the install folder has a file: cp cannot copy over it, so the copy fails
        // after the other files were already replaced. The backup must put every previous file back.
        string directory = NewDirectory();
        try
        {
            string install = Path.Combine(directory, "app");
            string staging = Path.Combine(directory, "staging");
            string root = Path.Combine(staging, "iTerminal-1.2.0-linux-x64-portable");
            string backup = Path.Combine(directory, "backup");
            string archive = Path.Combine(directory, "archive.tar.gz");
            string log = Path.Combine(directory, "apply-update.log");
            string script = Path.Combine(directory, "apply-update.sh");
            Directory.CreateDirectory(install);
            File.WriteAllText(Path.Combine(install, "iTerminal"), "old");
            File.WriteAllText(Path.Combine(install, "Assets"), "a file, not a folder");
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            File.WriteAllText(Path.Combine(root, "Assets", "readme.txt"), "a file inside the folder, so cp really fails on it");
            File.WriteAllText(Path.Combine(root, "iTerminal"), "new");
            File.WriteAllText(archive, "archive");
            File.WriteAllText(script, UpdateService.ShellUpdateScript(int.MaxValue, root, install, archive, staging, backup, log));

            using (Process process = Process.Start(new ProcessStartInfo("/bin/sh") { UseShellExecute = false, ArgumentList = { script } })!)
            {
                process.WaitForExit();
            }

            Assert.AreEqual("old", File.ReadAllText(Path.Combine(install, "iTerminal")), "The previous program is back in place.");
            Assert.AreEqual("a file, not a folder", File.ReadAllText(Path.Combine(install, "Assets")));
            Assert.IsTrue(File.Exists(archive), "A failed update keeps the archive for inspection.");
            Assert.IsTrue(Directory.Exists(staging), "A failed update keeps the staged files.");
            StringAssert.Contains(File.ReadAllText(log), "The previous files are back in place.");
        }
        finally
        {
            DeleteQuietly(directory);
        }
    }

    [TestMethod]
    public void TheScriptDoublesPercentSignsSoTheBatchFileReadsThemLiterally()
    {
        string script = UpdateService.UpdateScript(4242, @"C:\Temp\staging", @"C:\Apps\100%", @"C:\Temp\iTerminal.zip", @"C:\Temp\backup", @"C:\Temp\apply-update.log");

        StringAssert.Contains(script, "robocopy \"C:\\Temp\\staging\" \"C:\\Apps\\100%%\" /E");
    }
}
