using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Playwright, which the quote indicator drives in a headless Chromium. The .NET library is shipped;
/// its driver (a Node.js runtime and Playwright's JavaScript package) and the Chromium it drives are
/// not. The driver comes out of the Microsoft.Playwright NuGet package for the version this build
/// was compiled against, and Chromium is installed by that driver's own <c>install</c> command.
/// </summary>
/// <remarks>
/// Layout under <c>&lt;root&gt;</c>:
/// <list type="bullet">
/// <item><c>&lt;version&gt;/.playwright/package</c> and <c>&lt;version&gt;/.playwright/node/&lt;platform&gt;</c>: the driver.</item>
/// <item><c>&lt;version&gt;/chromium.installed</c>: written once that version's Chromium is in place.</item>
/// <item><c>browsers</c>: the Chromium builds, shared by every version (each names its own).</item>
/// </list>
/// The library finds the driver through <c>PLAYWRIGHT_DRIVER_SEARCH_PATH</c> and the browsers through
/// <c>PLAYWRIGHT_BROWSERS_PATH</c>, so nothing has to be written next to the application.
/// </remarks>
public sealed class PlaywrightRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "playwright";

    internal const string PackageId = "Microsoft.Playwright";
    internal const string DriverSearchPathVariable = "PLAYWRIGHT_DRIVER_SEARCH_PATH";
    internal const string BrowsersPathVariable = "PLAYWRIGHT_BROWSERS_PATH";
    internal const string CompleteMarker = "chromium.installed";

    private const string BrowsersFolder = "browsers";
    private const string DownloadsFolder = "downloads";
    private const string DriverFolder = ".playwright";

    private static readonly UnixFileMode ExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private readonly string _root;
    private readonly NuGetPackageFeed _feed;
    private readonly Version _expected;

    public PlaywrightRuntimeDependency(string rootDirectory)
        : this(rootDirectory, new NuGetPackageFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }), LibraryVersion())
    {
    }

    internal PlaywrightRuntimeDependency(string rootDirectory, NuGetPackageFeed feed, Version expected)
    {
        _root = rootDirectory;
        _feed = feed;
        _expected = expected;
    }

    public string Id => DependencyId;

    public string Name => "Playwright";

    public string Purpose => "Chromium for the quote indicator (Swissquote)";

    public string ExpectedVersion => _expected.ToString(3);

    public bool IsCompatible(Version version) => version.Major == _expected.Major && version.Minor == _expected.Minor;

    /// <summary>The version of the Microsoft.Playwright library this build references.</summary>
    internal static Version LibraryVersion() => typeof(Microsoft.Playwright.IPlaywright).Assembly.GetName().Version!;

    internal string BrowsersPath => Path.Combine(_root, BrowsersFolder);

    public string? InstalledVersion() => CompleteVersions().OrderDescending().Select(v => v.ToString(3)).FirstOrDefault();

    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> published = await _feed.ListVersionsAsync(PackageId, cancellationToken);
        return published.Where(version => !version.Contains('-')).ToList();
    }

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Version.Parse(version); // A malformed version is refused before anything is written.
        string target = VersionDirectory(version);

        if (!DriverPresent(target))
        {
            Directory.CreateDirectory(Path.Combine(_root, DownloadsFolder));
            string package = Path.Combine(_root, DownloadsFolder, $"{PackageId}.{version}.nupkg");
            try
            {
                await _feed.DownloadAsync(PackageId, version, package, progress, cancellationToken);
                progress.Report($"Unpacking the Playwright {version} driver");
                string staging = target + ".staging";
                DeleteDirectory(staging);
                ExtractDriver(package, staging, PlatformFolder());
                DeleteDirectory(target);
                Directory.Move(staging, target);
            }
            finally
            {
                TryDeleteFile(package);
                TryDeleteEmptyDirectory(Path.Combine(_root, DownloadsFolder));
            }
        }

        if (!File.Exists(Path.Combine(target, CompleteMarker)))
        {
            progress.Report($"Installing the Chromium that Playwright {version} drives (this can take a few minutes)");
            await InstallBrowserAsync(target, progress, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(target, CompleteMarker), version, cancellationToken);
        }

        PruneOlderVersions(version);
        Activate();
        progress.Report($"Playwright {version} is ready");
    }

    public void Activate()
    {
        Environment.SetEnvironmentVariable(BrowsersPathVariable, BrowsersPath);
        string? installed = InstalledVersion();
        Environment.SetEnvironmentVariable(
            DriverSearchPathVariable,
            installed is null ? null : VersionDirectory(installed));
    }

    public void Invalidate()
    {
        foreach (Version version in CompleteVersions())
        {
            TryDeleteFile(Path.Combine(VersionDirectory(version.ToString(3)), CompleteMarker));
        }
    }

    private IEnumerable<Version> CompleteVersions() =>
        CompatibleVersions().Where(version =>
        {
            string directory = VersionDirectory(version.ToString(3));
            return File.Exists(Path.Combine(directory, CompleteMarker)) && DriverPresent(directory);
        });

    /// <summary>Every version folder on this line, complete or not.</summary>
    private IEnumerable<Version> CompatibleVersions()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (string directory in Directory.EnumerateDirectories(_root))
        {
            if (Version.TryParse(Path.GetFileName(directory), out Version? version) && IsCompatible(version))
            {
                yield return version;
            }
        }
    }

    private string VersionDirectory(string version) => Path.Combine(_root, version);

    private static bool DriverPresent(string directory) =>
        File.Exists(CliPath(directory)) && File.Exists(NodePath(directory));

    private static string CliPath(string directory) => Path.Combine(directory, DriverFolder, "package", "cli.js");

    private static string NodePath(string directory) => NodePath(directory, PlatformFolder());

    /// <summary>The Node.js executable the package holds for <paramref name="platformFolder"/> — which need not be this machine's.</summary>
    private static string NodePath(string directory, string platformFolder) =>
        Path.Combine(directory, DriverFolder, "node", platformFolder, NodeFileName(platformFolder));

    private static string NodeFileName(string platformFolder) => platformFolder.StartsWith("win32", StringComparison.Ordinal) ? "node.exe" : "node";

    /// <summary>The folder inside the package's <c>.playwright/node</c> that holds this machine's Node.js.</summary>
    internal static string PlatformFolder()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
        };

        if (OperatingSystem.IsWindows())
        {
            return $"win32_{architecture}";
        }

        return OperatingSystem.IsMacOS() ? $"darwin-{architecture}" : $"linux-{architecture}";
    }

    /// <summary>
    /// Unpacks the parts of the NuGet package the driver runs from: its JavaScript package and the Node.js
    /// for one platform. The package carries Node.js for every platform (about 210 MB in all); the rest
    /// is left in the download. Entries that would land outside <paramref name="destination"/> are skipped.
    /// </summary>
    internal static void ExtractDriver(string package, string destination, string platformFolder)
    {
        string nodeFolder = $"{DriverFolder}/node/{platformFolder}/";
        string destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(package);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string name = entry.FullName;
            bool wanted = name.StartsWith($"{DriverFolder}/package/", StringComparison.Ordinal)
                || name.StartsWith(nodeFolder, StringComparison.Ordinal)
                || name == $"{DriverFolder}/node/LICENSE";
            if (!wanted || name.EndsWith('/'))
            {
                continue;
            }

            string target = Path.GetFullPath(Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(destinationRoot, StringComparison.Ordinal))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                // The zip does not keep the execute bit; Node.js and the package's bin scripts need it.
                File.SetUnixFileMode(target, ExecutableMode);
            }
        }

        if (!File.Exists(NodePath(destination, platformFolder)))
        {
            throw new InvalidOperationException($"The Playwright package has no Node.js for {platformFolder}.");
        }
    }

    /// <summary>
    /// Runs the driver's own <c>install chromium</c>, the command <c>playwright install</c> runs, against the
    /// shared browsers folder. Its progress bar arrives on stdout and stderr with carriage returns, so each
    /// piece is reported as a line of its own.
    /// </summary>
    private async Task InstallBrowserAsync(string driverDirectory, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(BrowsersPath);
        var start = new ProcessStartInfo(NodePath(driverDirectory))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = driverDirectory,
        };
        start.ArgumentList.Add(CliPath(driverDirectory));
        start.ArgumentList.Add("install");
        start.ArgumentList.Add("chromium");
        start.Environment[BrowsersPathVariable] = BrowsersPath;

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException("The Node.js runtime for Playwright could not be started.");

        string lastLine = string.Empty;
        void Report(string line)
        {
            lastLine = line;
            progress.Report(line);
        }

        Task output = PumpAsync(process.StandardOutput, Report);
        Task error = PumpAsync(process.StandardError, Report);

        using CancellationTokenRegistration stop = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // It exited between the check and the kill.
            }
        });

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(output, error);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Installing Chromium failed (exit code {process.ExitCode}): {lastLine}");
        }
    }

    private static async Task PumpAsync(StreamReader reader, Action<string> onLine)
    {
        char[] buffer = new char[1024];
        StringBuilder line = new();
        int read;
        while ((read = await reader.ReadAsync(buffer)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] is '\r' or '\n')
                {
                    EmitLine(line, onLine);
                }
                else
                {
                    line.Append(buffer[i]);
                }
            }
        }

        EmitLine(line, onLine);
    }

    private static void EmitLine(StringBuilder line, Action<string> onLine)
    {
        string text = line.ToString().Trim();
        line.Clear();
        if (text.Length > 0)
        {
            onLine(text);
        }
    }

    /// <summary>Removes the versions on this line that are older than the one just installed. Their Chromium stays in the shared folder.</summary>
    private void PruneOlderVersions(string installed)
    {
        Version current = Version.Parse(installed);
        foreach (Version version in CompatibleVersions().Where(version => version < current))
        {
            DeleteDirectory(VersionDirectory(version.ToString(3)));
        }
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file in it is in use (on Windows, a driver still running). The next install tries again.
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            Directory.Delete(path);
        }
        catch (IOException)
        {
            // Not empty, or in use: the next install reuses it.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
