using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

// These tests set the process PATH, which every test in the assembly reads: they must not run beside other tests.
[TestClass]
[DoNotParallelize]
public class PowerShellRuntimeDependencyTest
{
    private const string Api = "https://api.github.com/";
    private const string Downloads = "https://downloads.test/";
    private const string Release = "https://api.github.com/repos/PowerShell/PowerShell/releases/tags/v7.7.0";
    private static readonly string ProgramName = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";

    private static readonly List<string> Roots = [];

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"iTerminal.PowerShell.{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        lock (Roots)
        {
            Roots.Add(root);
        }

        return root;
    }

    [ClassCleanup]
    public static void DeleteRoots()
    {
        lock (Roots)
        {
            foreach (string root in Roots)
            {
                try
                {
                    Directory.Delete(root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private sealed class Silent : IProgress<string>
    {
        public void Report(string value)
        {
        }
    }

    private static PowerShellRuntimeDependency New(
        string root,
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null,
        Func<string, string?>? findOnPath = null,
        IEnumerable<string>? systemDirectories = null,
        Func<string, TimeSpan, Task<string?>>? readVersion = null) =>
        new(
            root,
            new GitHubReleaseFeed(new HttpClient(new StubHandler(respond ?? (_ => throw new InvalidOperationException("no network in this test")))), Api),
            new Version(7, 7, 0),
            findOnPath ?? (_ => null),
            systemDirectories ?? [],
            readVersion ?? ((_, _) => Task.FromResult<string?>("7.7.0")),
            OperatingSystem.IsWindows());

    /// <summary>A version folder as a finished install leaves it: the program at its top.</summary>
    private static void MakeInstalled(string root, string version)
    {
        Directory.CreateDirectory(Path.Combine(root, version));
        File.WriteAllText(Path.Combine(root, version, ProgramName), "program");
    }

    /// <summary>The release archive for this platform: a zip on Windows, a tar.gz elsewhere, with the program at its top.</summary>
    private static (string Name, byte[] Bytes) MakeArchive(string name)
    {
        if (name.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var memory = new MemoryStream();
            using (ZipArchive archive = new(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry entry = archive.CreateEntry(ProgramName);
                using Stream stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes("program"));
            }

            return (name, memory.ToArray());
        }

        using var gzipped = new MemoryStream();
        using (var gzip = new GZipStream(gzipped, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, leaveOpen: true))
        {
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "pwsh")
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("program")),
            });
        }

        return (name, gzipped.ToArray());
    }

    /// <summary>The release JSON for one asset and the handler that serves it and the release.</summary>
    private static Func<HttpRequestMessage, HttpResponseMessage> Serve(string assetName, byte[] bytes)
    {
        string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string json = $$"""
            {"tag_name":"v7.7.0","assets":[{"name":"{{assetName}}","browser_download_url":"{{Downloads}}{{assetName}}","size":{{bytes.Length}},"digest":"sha256:{{digest}}"}]}
            """;
        return request => request.RequestUri!.ToString() switch
        {
            var uri when uri == Release => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") },
            var uri when uri == Downloads + assetName => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) },
            _ => throw new InvalidOperationException("Unexpected request: " + request.RequestUri),
        };
    }

    [Timeout(60_000)]
    [TestMethod]
    public void AssetName_PicksTheArchiveOfThePlatform()
    {
        Assert.AreEqual("PowerShell-7.7.0-win-x64.zip", PowerShellRuntimeDependency.AssetName(windows: true, macOS: false, Architecture.X64));
        Assert.AreEqual("PowerShell-7.7.0-win-arm64.zip", PowerShellRuntimeDependency.AssetName(windows: true, macOS: false, Architecture.Arm64));
        Assert.AreEqual("powershell-7.7.0-linux-x64.tar.gz", PowerShellRuntimeDependency.AssetName(windows: false, macOS: false, Architecture.X64));
        Assert.AreEqual("powershell-7.7.0-linux-arm64.tar.gz", PowerShellRuntimeDependency.AssetName(windows: false, macOS: false, Architecture.Arm64));
        Assert.AreEqual("powershell-7.7.0-osx-x64.tar.gz", PowerShellRuntimeDependency.AssetName(windows: false, macOS: true, Architecture.X64));
        Assert.AreEqual("powershell-7.7.0-osx-arm64.tar.gz", PowerShellRuntimeDependency.AssetName(windows: false, macOS: true, Architecture.Arm64));
        Assert.IsNull(PowerShellRuntimeDependency.AssetName(windows: false, macOS: false, Architecture.X86));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void IsCompatible_IsTheSevenLine()
    {
        var dependency = New(NewRoot());

        Assert.AreEqual("7.7.0", dependency.ExpectedVersion);
        Assert.IsTrue(dependency.IsCompatible(new Version(7, 0, 0)));
        Assert.IsTrue(dependency.IsCompatible(new Version(7, 99, 1)));
        Assert.IsFalse(dependency.IsCompatible(new Version(6, 7, 0)), "Windows PowerShell 5 and PowerShell 6 are not this line.");
        Assert.IsFalse(dependency.IsCompatible(new Version(8, 0, 0)));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void FindBinary_FindsTheProgramAtTheTopOfTheRelease()
    {
        string root = NewRoot();
        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(empty);
        MakeInstalled(root, "7.7.0");

        Assert.IsNotNull(PowerShellRuntimeDependency.FindBinary(Path.Combine(root, "7.7.0")));
        Assert.IsNull(PowerShellRuntimeDependency.FindBinary(empty));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Extract_UnpacksTheTarGzAndKeepsTheExecuteBit()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The tar.gz archive and its execute bit are Linux and macOS.");
        }

        string root = NewRoot();
        string package = Path.Combine(root, "pwsh.tar.gz");
        using (var file = File.Create(package))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "pwsh")
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("program")),
            });
            tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "../escaped.txt")
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes("outside")),
            });
        }

        string destination = Path.Combine(root, "out");
        ReleaseArchive.Extract(package, destination);

        string program = Path.Combine(destination, "pwsh");
        Assert.AreEqual("program", File.ReadAllText(program));
        Assert.IsTrue(File.GetUnixFileMode(program).HasFlag(UnixFileMode.UserExecute), "Without the execute bit the program cannot be started.");
        Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.txt")));
    }

    [Timeout(60_000)]
    [TestMethod]
    public void ExistingInstall_OnThePath_IsUsedAsItIsAndNotUpdated()
    {
        string root = NewRoot();
        string system = Path.Combine(NewRoot(), ProgramName);
        File.WriteAllText(system, "system");
        string? saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            string systemFolder = Path.GetDirectoryName(system)!;
            Environment.SetEnvironmentVariable("PATH", systemFolder + Path.PathSeparator + "/unchanged");
            var dependency = New(root, findOnPath: _ => system, readVersion: (_, _) => Task.FromResult<string?>("7.4.6"));

            Assert.IsTrue(dependency.IsExistingInstall);
            Assert.AreEqual("7.4.6", dependency.InstalledVersion(), "The version is the system's, read from it.");
            Assert.IsNotNull(dependency.ExistingInstallNote);

            dependency.Activate();

            Assert.AreEqual(systemFolder + Path.PathSeparator + "/unchanged", Environment.GetEnvironmentVariable("PATH"), "A folder already on the PATH is not added again.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", saved);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void UnreadableSystemProgram_CountsAsInstalledAtThePlaceholderVersion()
    {
        string system = NewRoot();
        File.WriteAllText(Path.Combine(system, ProgramName), "system");
        var dependency = New(NewRoot(), findOnPath: _ => null, systemDirectories: [system], readVersion: (_, _) => Task.FromResult<string?>(null));

        Assert.IsTrue(dependency.IsExistingInstall, "A PowerShell that cannot report its version is still there, so it is not downloaded over.");
        Assert.AreEqual("0.0.0", dependency.InstalledVersion(), "The placeholder keeps the start-up offer from repeating.");
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Activate_WithASystemProgramOffThePath_PrependsItsFolder()
    {
        string system = NewRoot();
        File.WriteAllText(Path.Combine(system, ProgramName), "system");
        string? saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/unchanged");
            var dependency = New(NewRoot(), findOnPath: _ => null, systemDirectories: [system]);

            dependency.Activate();

            Assert.IsTrue(Environment.GetEnvironmentVariable("PATH")!.StartsWith(system + Path.PathSeparator, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", saved);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void Activate_WithoutASystemProgram_PrependsTheNewestDownload()
    {
        string root = NewRoot();
        MakeInstalled(root, "7.6.0");
        MakeInstalled(root, "7.7.0");
        string? saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/unchanged");
            var dependency = New(root, findOnPath: _ => null);

            dependency.Activate();

            Assert.IsTrue(Environment.GetEnvironmentVariable("PATH")!.StartsWith(Path.Combine(root, "7.7.0") + Path.PathSeparator, StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", saved);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public void InstalledVersion_IsTheNewestCompleteDownloadOfTheLine()
    {
        string root = NewRoot();
        MakeInstalled(root, "7.6.0");
        MakeInstalled(root, "8.0.0");
        Directory.CreateDirectory(Path.Combine(root, "7.8.0"));
        var dependency = New(root, findOnPath: _ => null);

        Assert.AreEqual("7.6.0", dependency.InstalledVersion(), "A folder without a program, or outside the line, is not an install.");
        Assert.IsFalse(dependency.IsExistingInstall);
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_DownloadsVerifiesMovesIntoPlaceAndActivates()
    {
        string? asset = PowerShellRuntimeDependency.AssetName(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture);
        if (asset is null)
        {
            Assert.Inconclusive("This platform has no PowerShell asset in the test.");
        }

        string root = NewRoot();
        var (name, bytes) = MakeArchive(asset);
        string? saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/unchanged");
            var dependency = New(root, respond: Serve(name, bytes), findOnPath: _ => null);

            await dependency.InstallAsync("7.7.0", new Silent(), CancellationToken.None);

            Assert.IsTrue(File.Exists(Path.Combine(root, "7.7.0", ProgramName)));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "7.7.0.staging")), "The staging folder is moved, not left behind.");
            Assert.IsTrue(Environment.GetEnvironmentVariable("PATH")!.StartsWith(Path.Combine(root, "7.7.0") + Path.PathSeparator, StringComparison.Ordinal),
                "The install is activated at once, without a restart.");
            Assert.AreEqual("7.7.0", dependency.InstalledVersion());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", saved);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_AProgramThatDoesNotStart_LeavesNoInstallAndPathUnchanged()
    {
        string? asset = PowerShellRuntimeDependency.AssetName(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture);
        if (asset is null)
        {
            Assert.Inconclusive("This platform has no PowerShell asset in the test.");
        }

        string root = NewRoot();
        var (name, bytes) = MakeArchive(asset);
        string? saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/unchanged");
            var dependency = New(root, respond: Serve(name, bytes), findOnPath: _ => null, readVersion: (_, _) => Task.FromResult<string?>(null));

            bool threw = false;
            try
            {
                await dependency.InstallAsync("7.7.0", new Silent(), CancellationToken.None);
            }
            catch (InvalidOperationException exception)
            {
                threw = true;
                StringAssert.Contains(exception.Message, "did not start");
            }

            Assert.IsTrue(threw, "A program that does not report its version is not installed.");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "7.7.0")), "The version folder is not moved into place.");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, "7.7.0.staging")), "The staging folder is removed.");
            Assert.AreEqual("/unchanged", Environment.GetEnvironmentVariable("PATH"));
            Assert.IsNull(dependency.InstalledVersion());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", saved);
        }
    }

    [Timeout(60_000)]
    [TestMethod]
    public async Task Install_WithASystemProgram_DownloadsNothing()
    {
        string system = Path.Combine(NewRoot(), ProgramName);
        File.WriteAllText(system, "system");
        string? saved = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", "/unchanged");
            var dependency = New(NewRoot(), respond: _ => throw new InvalidOperationException("No download when a system PowerShell exists."), findOnPath: _ => system);

            await dependency.InstallAsync("7.7.0", new Silent(), CancellationToken.None);

            Assert.AreEqual(Path.GetDirectoryName(system) + Path.PathSeparator + "/unchanged", Environment.GetEnvironmentVariable("PATH"),
                "The system copy's folder is put on the PATH, and nothing is downloaded.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", saved);
        }
    }
}
