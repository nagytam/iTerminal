using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Sdk;

namespace iTerminal.Core.Test.Services.Sdk;

[TestClass]
public class SdkManagerTest
{
    private string _folder = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "SdkManager_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string Runtimes => Path.Combine(_folder, "runtimes");

    private sealed class StubHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    /// <summary>A settings store held in memory: the Java choice is the only setting these tests read.</summary>
    private sealed class MemorySettings : ISettingsService
    {
        private readonly Dictionary<string, string> _values = [];

        public bool IsDirty => false;

        public string? Get(string key) => _values.GetValueOrDefault(key);

        public bool? GetAsBool(string key, bool? defaultValue = false) => defaultValue;

        public void Set(string key, string value) => _values[key] = value;

        public void Write()
        {
        }
    }

    /// <summary>A .tar.gz of the given files, built the way a publisher builds one: entries are relative to the archive root.</summary>
    private static byte[] TarGz(params (string Path, string Content)[] files)
    {
        string staging = Path.Combine(Path.GetTempPath(), "SdkArchive_" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var (path, content) in files)
            {
                string full = Path.Combine(staging, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content);
            }

            using var memory = new MemoryStream();
            using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
            {
                TarFile.CreateFromDirectory(staging, gzip, includeBaseDirectory: false);
            }

            return memory.ToArray();
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static string Hex(HashAlgorithm algorithm, byte[] bytes) => Convert.ToHexString(algorithm.ComputeHash(bytes)).ToLowerInvariant();

    private static AvailableSdk DotNet(byte[] archive, string digest, string version = "10.0.404") => new(
        SdkKind.DotNet, version, "10.0", "https://example.test/" + version + ".tar.gz", version + ".tar.gz", digest, HashAlgorithmName.SHA512, archive.Length, IsLatest: true);

    [TestMethod]
    public async Task InstallsADotNetSdkWhoseDigestMatchesAndListsIt()
    {
        byte[] archive = TarGz(("dotnet", "host"), ("sdk/10.0.404/dotnet.dll", "sdk"));
        var manager = new SdkManager(new HttpClient(new StubHandler(archive)), Runtimes, new MemorySettings());

        string home = await manager.InstallAsync(DotNet(archive, Hex(SHA512.Create(), archive)), null, CancellationToken.None);

        Assert.AreEqual(Path.Combine(Runtimes, "dotnet", "10.0.404"), home);
        Assert.IsTrue(File.Exists(Path.Combine(home, "dotnet")));
        var installed = manager.InstalledDotNetSdks();
        Assert.AreEqual(1, installed.Count);
        Assert.AreEqual("10.0.404", installed[0].Version);
        Assert.AreEqual(home, installed[0].Root, "the SDK's host runs from its install folder");
        Assert.AreEqual(0, Directory.Exists(Path.Combine(Runtimes, "downloads")) ? Directory.GetFiles(Path.Combine(Runtimes, "downloads")).Length : 0, "the archive is removed once it is installed");
        Assert.IsFalse(Directory.Exists(home + ".staging"));
    }

    [TestMethod]
    public async Task RefusesAnArchiveWhoseDigestDoesNotMatchAndLeavesNothingBehind()
    {
        byte[] archive = TarGz(("dotnet", "host"), ("sdk/10.0.404/dotnet.dll", "sdk"));
        var manager = new SdkManager(new HttpClient(new StubHandler(archive)), Runtimes, new MemorySettings());

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            manager.InstallAsync(DotNet(archive, new string('0', 128)), null, CancellationToken.None));

        Assert.IsFalse(Directory.Exists(Path.Combine(Runtimes, "dotnet", "10.0.404")), "nothing is installed");
        Assert.AreEqual(0, manager.InstalledDotNetSdks().Count);
        string downloads = Path.Combine(Runtimes, "downloads");
        Assert.IsTrue(!Directory.Exists(downloads) || Directory.GetFiles(downloads).Length == 0, "the rejected download is deleted");
    }

    [TestMethod]
    public async Task RefusesAnArchiveThatIsNotAnSdk()
    {
        byte[] archive = TarGz(("readme.txt", "not an SDK"));
        var manager = new SdkManager(new HttpClient(new StubHandler(archive)), Runtimes, new MemorySettings());

        var error = await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            manager.InstallAsync(DotNet(archive, Hex(SHA512.Create(), archive)), null, CancellationToken.None));

        StringAssert.Contains(error.Message, "10.0.404");
        Assert.IsFalse(Directory.Exists(Path.Combine(Runtimes, "dotnet", "10.0.404")));
        Assert.IsFalse(Directory.Exists(Path.Combine(Runtimes, "dotnet", "10.0.404.staging")));
    }

    [TestMethod]
    public async Task InstallsAJdkWhoseArchiveHasItsFolderAndFindsItsHome()
    {
        string javac = OperatingSystem.IsWindows() ? "javac.exe" : "javac";
        byte[] archive = TarGz(("jdk-21.0.13.1+2/bin/" + javac, "javac"), ("jdk-21.0.13.1+2/bin/java", "java"));
        var jdk = new AvailableSdk(SdkKind.Jdk, "jdk-21.0.13.1+2", "21", "https://example.test/jdk.tar.gz", "jdk.tar.gz",
            Hex(SHA256.Create(), archive), HashAlgorithmName.SHA256, archive.Length, IsLatest: true);
        var manager = new SdkManager(new HttpClient(new StubHandler(archive)), Runtimes, new MemorySettings());

        string folder = await manager.InstallAsync(jdk, null, CancellationToken.None);

        var installed = manager.InstalledJdks();
        Assert.AreEqual(1, installed.Count);
        Assert.AreEqual("jdk-21.0.13.1+2", installed[0].Version);
        Assert.AreEqual(folder, installed[0].Folder);
        Assert.AreEqual(Path.Combine(folder, "jdk-21.0.13.1+2"), installed[0].Home, "the JDK home is the archive's own folder");
        Assert.IsTrue(File.Exists(Path.Combine(installed[0].Home, "bin", javac)));
    }

    [TestMethod]
    public async Task AnInstalledVersionIsNotDownloadedAgain()
    {
        byte[] archive = TarGz(("dotnet", "host"), ("sdk/10.0.404/dotnet.dll", "sdk"));
        var manager = new SdkManager(new HttpClient(new StubHandler(archive)), Runtimes, new MemorySettings());
        var sdk = DotNet(archive, Hex(SHA512.Create(), archive));
        string first = await manager.InstallAsync(sdk, null, CancellationToken.None);

        // A second install with a wrong digest would fail if it downloaded; the existing folder is returned instead.
        string second = await manager.InstallAsync(sdk with { Digest = new string('0', 128) }, null, CancellationToken.None);

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public void TheChosenJavaIsRememberedAndForgottenWhenItsFolderIsRemoved()
    {
        string javac = OperatingSystem.IsWindows() ? "javac.exe" : "javac";
        string home = Path.Combine(Runtimes, "jdk", "jdk-21.0.13.1+2", "jdk-21.0.13.1+2");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", javac), "javac");
        var settings = new MemorySettings();
        var manager = new SdkManager(new HttpClient(), Runtimes, settings);

        manager.UseJavaHome(home);
        Assert.AreEqual(home, manager.ActiveJavaHome);

        manager.Uninstall(Path.Combine(Runtimes, "jdk", "jdk-21.0.13.1+2"));

        Assert.IsNull(manager.ActiveJavaHome, "a JDK that was removed is no longer the Java in use");
        Assert.AreEqual(0, manager.InstalledJdks().Count);
    }

    [TestMethod]
    public void UninstallRemovesOnlyAFolderUnderTheManagedRoots()
    {
        string outside = Path.Combine(_folder, "keep-me");
        Directory.CreateDirectory(outside);
        var manager = new SdkManager(new HttpClient(), Runtimes, new MemorySettings());

        Assert.ThrowsExactly<InvalidOperationException>(() => manager.Uninstall(outside));
        Assert.IsTrue(Directory.Exists(outside));
        Assert.ThrowsExactly<InvalidOperationException>(() => manager.Uninstall(Path.Combine(Runtimes, "dotnet")),
            "the root itself is not an install");
    }

    [TestMethod]
    public void AUnsafeVersionBecomesOneFileName()
    {
        Assert.AreEqual("jdk-21.0.13.1+2", SdkManager.SafeName("jdk-21.0.13.1+2"));
        Assert.AreEqual("_.._..evil", SdkManager.SafeName("../..evil"), "a separator never reaches a path");
        Assert.AreEqual("_", SdkManager.SafeName(string.Empty));
    }
}
