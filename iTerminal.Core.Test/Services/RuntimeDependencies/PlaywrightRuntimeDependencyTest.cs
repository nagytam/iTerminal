using System.IO.Compression;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Test.Services.RuntimeDependencies;

[TestClass]
public class PlaywrightRuntimeDependencyTest
{
    private static readonly Version Built = new(1, 63, 0);

    private static readonly List<string> Roots = [];

    private static string NewRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"iTerminal.Runtimes.{Guid.NewGuid():N}");
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

    private static PlaywrightRuntimeDependency NewDependency(string root) =>
        new(root, new NuGetPackageFeed(new HttpClient()), Built);

    /// <summary>A version folder as a finished install leaves it: the driver, Node.js for this machine, and the Chromium marker.</summary>
    private static void MakeInstalled(string root, string version, bool browserMarker = true)
    {
        string directory = Path.Combine(root, version);
        string cli = Path.Combine(directory, ".playwright", "package", "cli.js");
        string node = Path.Combine(directory, ".playwright", "node", PlaywrightRuntimeDependency.PlatformFolder(), OperatingSystem.IsWindows() ? "node.exe" : "node");
        Directory.CreateDirectory(Path.GetDirectoryName(cli)!);
        Directory.CreateDirectory(Path.GetDirectoryName(node)!);
        File.WriteAllText(cli, "// driver");
        File.WriteAllText(node, "node");
        if (browserMarker)
        {
            File.WriteAllText(Path.Combine(directory, PlaywrightRuntimeDependency.CompleteMarker), version);
        }
    }

    private static void AddEntry(ZipArchive archive, string name, string content = "x")
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    [TestMethod]
    public void AVersionIsCompatibleOnlyWithinItsMajorAndMinor()
    {
        var dependency = NewDependency(NewRoot());

        Assert.IsTrue(dependency.IsCompatible(new Version(1, 63, 7)));
        Assert.IsFalse(dependency.IsCompatible(new Version(1, 64, 0)));
        Assert.IsFalse(dependency.IsCompatible(new Version(2, 63, 0)));
        Assert.AreEqual("1.63.0", dependency.ExpectedVersion);
    }

    [TestMethod]
    public void TheInstalledVersionIsTheNewestCompleteOneOnTheBuildsLine()
    {
        string root = NewRoot();
        MakeInstalled(root, "1.63.0");
        MakeInstalled(root, "1.63.2");
        MakeInstalled(root, "1.62.0");                 // Another line: it would not be loaded by this build.
        MakeInstalled(root, "1.63.3", browserMarker: false); // Newest, but Chromium never finished.
        Directory.CreateDirectory(Path.Combine(root, "browsers", "chromium-1243"));

        Assert.AreEqual("1.63.2", NewDependency(root).InstalledVersion());
    }

    [TestMethod]
    public void NothingInstalledMeansNoInstalledVersion()
    {
        Assert.IsNull(NewDependency(NewRoot()).InstalledVersion());
    }

    [TestMethod]
    public void ActivatePointsTheLibraryAtTheNewestInstalledDriverAndTheSharedBrowsers()
    {
        string root = NewRoot();
        MakeInstalled(root, "1.63.0");
        MakeInstalled(root, "1.63.2");
        string? previousDriver = Environment.GetEnvironmentVariable(PlaywrightRuntimeDependency.DriverSearchPathVariable);
        string? previousBrowsers = Environment.GetEnvironmentVariable(PlaywrightRuntimeDependency.BrowsersPathVariable);
        try
        {
            NewDependency(root).Activate();

            Assert.AreEqual(Path.Combine(root, "1.63.2"), Environment.GetEnvironmentVariable(PlaywrightRuntimeDependency.DriverSearchPathVariable));
            Assert.AreEqual(Path.Combine(root, "browsers"), Environment.GetEnvironmentVariable(PlaywrightRuntimeDependency.BrowsersPathVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PlaywrightRuntimeDependency.DriverSearchPathVariable, previousDriver);
            Environment.SetEnvironmentVariable(PlaywrightRuntimeDependency.BrowsersPathVariable, previousBrowsers);
        }
    }

    [TestMethod]
    public void InvalidatingKeepsTheDriverButReportsTheBrowserMissing()
    {
        string root = NewRoot();
        MakeInstalled(root, "1.63.0");
        var dependency = NewDependency(root);

        dependency.Invalidate();

        Assert.IsNull(dependency.InstalledVersion());
        Assert.IsTrue(File.Exists(Path.Combine(root, "1.63.0", ".playwright", "package", "cli.js")), "Only the marker goes; the driver is reused.");
    }

    [TestMethod]
    public void ExtractDriverKeepsTheDriverAndOnlyThisPlatformsNode()
    {
        string root = NewRoot();
        string package = Path.Combine(root, "package.nupkg");
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            AddEntry(archive, ".playwright/package/cli.js");
            AddEntry(archive, ".playwright/package/lib/driver/index.js");
            AddEntry(archive, ".playwright/node/linux-x64/node");
            AddEntry(archive, ".playwright/node/win32_x64/node.exe");
            AddEntry(archive, ".playwright/node/LICENSE");
            AddEntry(archive, ".playwright/unrelated/readme.txt");
            AddEntry(archive, "lib/net8.0/Microsoft.Playwright.dll");
            AddEntry(archive, ".playwright/package/../../../escaped.txt");
        }
        string destination = Path.Combine(root, "extracted");

        PlaywrightRuntimeDependency.ExtractDriver(package, destination, "linux-x64");

        Assert.IsTrue(File.Exists(Path.Combine(destination, ".playwright", "package", "cli.js")));
        Assert.IsTrue(File.Exists(Path.Combine(destination, ".playwright", "package", "lib", "driver", "index.js")));
        Assert.IsTrue(File.Exists(Path.Combine(destination, ".playwright", "node", "linux-x64", "node")));
        Assert.IsTrue(File.Exists(Path.Combine(destination, ".playwright", "node", "LICENSE")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, ".playwright", "node", "win32_x64", "node.exe")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, ".playwright", "unrelated", "readme.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(destination, "lib", "net8.0", "Microsoft.Playwright.dll")));
        Assert.IsFalse(File.Exists(Path.Combine(root, "escaped.txt")), "An entry that climbs out of the destination is skipped.");
    }

    [TestMethod]
    public void ExtractDriverRefusesAPackageWithoutNodeForThisPlatform()
    {
        string root = NewRoot();
        string package = Path.Combine(root, "package.nupkg");
        using (ZipArchive archive = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            AddEntry(archive, ".playwright/package/cli.js");
            AddEntry(archive, ".playwright/node/win32_x64/node.exe");
        }

        Assert.Throws<InvalidOperationException>(() =>
            PlaywrightRuntimeDependency.ExtractDriver(package, Path.Combine(root, "extracted"), "linux-x64"));
    }

    /// <summary>
    /// Downloads the driver for the library's version and installs Chromium into a temporary folder. It fetches about
    /// 350 MB from the internet, so it is kept off the regular run: remove the Ignore to run it by hand.
    /// </summary>
    [TestMethod]
    [Ignore("Downloads the Playwright driver (about 210 MB) and Chromium (about 150 MB) from the internet; run it by hand.")]
    public async Task InstallAsyncDownloadsTheDriverAndChromiumAndActivatesThem()
    {
        string root = NewRoot();
        try
        {
            var dependency = new PlaywrightRuntimeDependency(root);
            var lines = new List<string>();

            await dependency.InstallAsync(dependency.ExpectedVersion, new DelegateProgress(lines.Add), CancellationToken.None);

            Assert.AreEqual(dependency.ExpectedVersion, dependency.InstalledVersion());
            Assert.IsTrue(Directory.EnumerateDirectories(Path.Combine(root, "browsers"), "chromium-*").Any());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class DelegateProgress(Action<string> onLine) : IProgress<string>
    {
        public void Report(string value) => onLine(value);
    }
}
