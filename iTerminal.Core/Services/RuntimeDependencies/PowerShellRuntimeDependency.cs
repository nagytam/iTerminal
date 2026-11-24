using iConsole.Localization;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// PowerShell 7, for the remote PowerShell sessions (Alt+F2) and for Ctrl+O on Windows. The program comes from the
/// GitHub release of <see cref="Built"/>'s line (the newest 7.x is offered), and a PowerShell that is installed on
/// the system is used as it is and never downloaded over.
/// </summary>
/// <remarks>
/// Layout under <c>&lt;root&gt;</c>: <c>&lt;version&gt;/</c> is the release as unpacked, with <c>pwsh</c> (or
/// <c>pwsh.exe</c> on Windows) at its top. Only a version whose program starts is moved into place.
/// Activate() prepends a folder to the process PATH, so the program is found by its name everywhere that resolves
/// <c>pwsh</c> with <see cref="GitRuntimeDependency.FindOnPath"/>.
/// </remarks>
public sealed class PowerShellRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "powershell";

    /// <summary>The PowerShell this build is written against. Its major version is the line that is compatible.</summary>
    internal static readonly Version Built = new(7, 7, 0);

    internal const string Repository = "PowerShell/PowerShell";

    private const string DownloadsFolder = "downloads";
    private static readonly TimeSpan SystemVersionTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VerifyTimeout = TimeSpan.FromSeconds(30);

    private readonly string _root;
    private readonly GitHubReleaseFeed _feed;
    private readonly Version _expected;
    private readonly Func<string, string?> _findOnPath;
    private readonly IReadOnlyList<string> _systemDirectories;
    private readonly Func<string, TimeSpan, Task<string?>> _readVersion;
    private readonly bool _isWindows;
    private readonly Lock _gate = new();
    private (string Path, DateTime Stamp, string? Version)? _cached;

    public PowerShellRuntimeDependency(string rootDirectory)
        : this(
            rootDirectory,
            new GitHubReleaseFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }),
            Built,
            GitRuntimeDependency.FindOnPath,
            DefaultSystemDirectories(),
            ReadProgramVersionAsync,
            OperatingSystem.IsWindows())
    {
    }

    /// <param name="findOnPath">Finds a program by its bare name on the PATH; null when there is none. Injected so a test does not depend on the machine.</param>
    /// <param name="systemDirectories">The usual folders a system PowerShell is installed in, searched when it is not on the PATH.</param>
    /// <param name="readVersion">Runs a program and returns the version it reports, or null; injected so a test does not start one.</param>
    internal PowerShellRuntimeDependency(
        string rootDirectory,
        GitHubReleaseFeed feed,
        Version expected,
        Func<string, string?> findOnPath,
        IEnumerable<string> systemDirectories,
        Func<string, TimeSpan, Task<string?>> readVersion,
        bool isWindows)
    {
        _root = rootDirectory;
        _feed = feed;
        _expected = expected;
        _findOnPath = findOnPath;
        _systemDirectories = systemDirectories.ToList();
        _readVersion = readVersion;
        _isWindows = isWindows;
    }

    public string Id => DependencyId;

    public string Name => "PowerShell";

    public string Purpose => UiText.Get("Dep.PowerShellPurpose", "Running PowerShell: Ctrl+O on Windows and remote PowerShell sessions (Alt+F2)");

    public string ExpectedVersion => _expected.ToString(3);

    /// <summary>PowerShell 7 is one line: a 7.x release runs the same scripts and the same remoting.</summary>
    public bool IsCompatible(Version version) => version.Major == _expected.Major;

    /// <summary>A PowerShell on the system counts as an existing install, used as it is and never updated.</summary>
    public bool IsExistingInstall => SystemProgram() is not null;

    public string? ExistingInstallNote =>
        SystemProgram() is { } system
            ? string.Format(UiText.Get("Dep.PowerShellExisting", "PowerShell is already installed ({0}); it is used as it is."), system)
            : null;

    /// <summary>
    /// The version of the PowerShell on the system (read once by a short process, cached by path and write time),
    /// otherwise the newest complete install in the data folder. Called from the background status build only.
    /// </summary>
    public string? InstalledVersion() =>
        (SystemProgram() is { } system ? SystemVersion(system) : null)
        ?? CompleteVersions().OrderDescending().Select(v => v.ToString(3)).FirstOrDefault();

    /// <summary>Nothing is published to look up here: the list is the release list, and the newest compatible one is offered.</summary>
    public async Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken) =>
        await _feed.ListVersionsAsync(Repository, cancellationToken);

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Version parsed = Version.Parse(version); // A malformed version is refused before anything is written.
        if (!IsCompatible(parsed))
        {
            throw new InvalidOperationException($"PowerShell {version} is not in the {_expected.Major}.x line this build supports.");
        }

        if (SystemProgram() is { } system)
        {
            progress.Report(string.Format(UiText.Get("Dep.PowerShellAsIs", "PowerShell is already installed ({0}); it is used as it is"), system));
            Activate();
            return;
        }

        string target = VersionDirectory(version);
        if (FindBinary(target) is null)
        {
            GitHubRelease release = await _feed.GetReleaseAsync(Repository, version, cancellationToken);
            GitHubAsset asset = AssetFor(release, OperatingSystem.IsWindows(), OperatingSystem.IsMacOS(), RuntimeInformation.ProcessArchitecture)
                ?? throw new InvalidOperationException($"PowerShell {version} has no download for {PlatformName()}.");

            string downloads = Path.Combine(_root, DownloadsFolder);
            Directory.CreateDirectory(downloads);
            string package = Path.Combine(downloads, asset.Name);
            try
            {
                await _feed.DownloadAsync(asset, package, progress, cancellationToken);
                progress.Report($"Unpacking PowerShell {version}");
                string staging = target + ".staging";
                DeleteDirectory(staging);
                ReleaseArchive.Extract(package, staging);
                string binary = FindBinary(staging)
                    ?? throw new InvalidDataException($"The PowerShell {version} download has no program in it.");

                // Checked before it is moved into place: a program that does not start must not be left as an install.
                progress.Report($"Checking that PowerShell {version} starts");
                    try
                {
                    string? reported = await _readVersion(binary, VerifyTimeout);
                    if (reported is null || !Version.TryParse(reported, out Version? runs) || runs.ToString(3) != parsed.ToString(3))
                    {
                        throw new InvalidOperationException(
                            $"PowerShell {version} did not start. On Linux the usual cause is a missing ICU library (install libicu, or set DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1).");
                    }

                    DeleteDirectory(target);
                    Directory.Move(staging, target);
                }
                catch
                {
                    // Whatever failed, the unverified or half-moved folder is not left behind.
                    DeleteDirectory(staging);
                    throw;
                }

                PruneOlderVersions(version);
            }
            finally
            {
                TryDeleteFile(package);
                TryDeleteEmptyDirectory(downloads);
            }
        }

        Invalidate();
        Activate();
        progress.Report(string.Format(UiText.Get("Dep.PowerShellReady", "PowerShell {0} is ready"), version));
    }

    /// <summary>
    /// Points the process at PowerShell. A system PowerShell that is not on the PATH has its folder added, as
    /// <see cref="SystemToolRuntimeDependency"/> does; without one, the newest download's folder is added. Only file
    /// lookups happen here (the constructor calls this), never the version process.
    /// </summary>
    public void Activate()
    {
        if (SystemProgram() is { } system)
        {
            // Judged by the folder being on the PATH, not by what the PATH resolves to: a download put first by an
            // earlier Activate must not hide the system copy.
            PrependToPath(Path.GetDirectoryName(system));
        }
        else if (CompleteVersions().OrderDescending().FirstOrDefault() is { } newest)
        {
            PrependToPath(VersionDirectory(newest.ToString(3)));
        }

        Invalidate();
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    private string ProgramName => _isWindows ? "pwsh.exe" : "pwsh";

    /// <summary>A PowerShell outside iTerminal's own folder: on the PATH first, then in the usual install folders. Null when there is none.</summary>
    internal string? SystemProgram()
    {
        if (_findOnPath("pwsh") is { } onPath && !IsOwn(onPath) && File.Exists(onPath))
        {
            return onPath;
        }

        foreach (string directory in _systemDirectories)
        {
            string candidate = Path.Combine(directory, ProgramName);
            if (File.Exists(candidate) && !IsOwn(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private bool IsOwn(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private string? SystemVersion(string program)
    {
        DateTime stamp = SafeWriteTime(program);
        lock (_gate)
        {
            if (_cached is { } cached && cached.Path == program && cached.Stamp == stamp)
            {
                return cached.Version;
            }
        }

        // A PowerShell that is there but reports no version still counts as installed (0.0.0 is the placeholder), so
        // the offer does not repeat for an install that is used as it is.
        string? version = _readVersion(program, SystemVersionTimeout).GetAwaiter().GetResult() ?? "0.0.0";
        lock (_gate)
        {
            _cached = (program, stamp, version);
        }

        return version;
    }

    private IEnumerable<Version> CompleteVersions()
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (string directory in Directory.EnumerateDirectories(_root))
        {
            if (Version.TryParse(Path.GetFileName(directory), out Version? version)
                && IsCompatible(version)
                && FindBinary(directory) is not null)
            {
                yield return version;
            }
        }
    }

    private string VersionDirectory(string version) => Path.Combine(_root, version);

    /// <summary>The program in an unpacked release, at its top; null when there is none.</summary>
    internal static string? FindBinary(string directory)
    {
        string[] candidates = [Path.Combine(directory, "pwsh"), Path.Combine(directory, "pwsh.exe")];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The release asset for a platform. Windows ships a zip, Linux and macOS a tar.gz; the .pkg and .msi installers
    /// are not used. The names are GitHub's, and an asset missing from the release is reported rather than guessed.
    /// </summary>
    internal static GitHubAsset? AssetFor(GitHubRelease release, bool windows, bool macOS, Architecture architecture)
    {
        string? name = AssetName(windows, macOS, architecture);
        return name is null ? null : release.Assets.FirstOrDefault(asset => asset.Name == name);
    }

    /// <summary>The asset name for a platform, without the version in the platform part; null for an unsupported architecture.</summary>
    internal static string? AssetName(bool windows, bool macOS, Architecture architecture)
    {
        return (windows, macOS, architecture) switch
        {
            (true, _, Architecture.X64) => $"PowerShell-{Built.ToString(3)}-win-x64.zip",
            (true, _, Architecture.Arm64) => $"PowerShell-{Built.ToString(3)}-win-arm64.zip",
            (false, true, Architecture.X64) => $"powershell-{Built.ToString(3)}-osx-x64.tar.gz",
            (false, true, Architecture.Arm64) => $"powershell-{Built.ToString(3)}-osx-arm64.tar.gz",
            (false, false, Architecture.X64) => $"powershell-{Built.ToString(3)}-linux-x64.tar.gz",
            (false, false, Architecture.Arm64) => $"powershell-{Built.ToString(3)}-linux-arm64.tar.gz",
            _ => null,
        };
    }

    private static string PlatformName() =>
        $"{(OperatingSystem.IsWindows(), OperatingSystem.IsMacOS()) switch { (true, _) => "Windows", (_, true) => "macOS", _ => "Linux" }} {RuntimeInformation.ProcessArchitecture}";

    /// <summary>The usual folders a system PowerShell 7 is installed in, per platform.</summary>
    internal static IEnumerable<string> DefaultSystemDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/usr/local/bin";
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/microsoft/powershell/7";
        }
        else
        {
            yield return "/usr/bin";
            yield return "/usr/local/bin";
            yield return "/opt/microsoft/powershell/7";
        }
    }

    /// <summary>Runs the program with <c>$PSVersionTable</c> printed, stdin closed, and reads the version it reports; null when it does not start or does not finish in time.</summary>
    internal static async Task<string?> ReadProgramVersionAsync(string program, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo(program)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-Command");
            startInfo.ArgumentList.Add("$PSVersionTable.PSVersion.ToString()");

            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            using var cts = new CancellationTokenSource(timeout);
            Task<string> output = process.StandardOutput.ReadToEndAsync(cts.Token);
            Task<string> errors = process.StandardError.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch {
                    // Deliberately ignored: the failure leaves nothing to undo, so the operation is best effort.
                }
                return null;
            }

            Match match = Regex.Match(await output + " " + await errors, @"\d+\.\d+\.\d+", RegexOptions.None, TimeSpan.FromSeconds(1));
            return match.Success ? match.Value : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>Puts a folder first on the process PATH, unless it already is.</summary>
    private static void PrependToPath(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        string current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (current.StartsWith(directory + Path.PathSeparator, StringComparison.Ordinal) || current == directory)
        {
            return;
        }

        Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + current);
    }

    /// <summary>Removes the versions on this line that are older than the one just installed.</summary>
    private void PruneOlderVersions(string installed)
    {
        Version current = Version.Parse(installed);
        foreach (Version older in CompleteVersions().Where(v => v < current).ToList())
        {
            DeleteDirectory(VersionDirectory(older.ToString(3)));
        }
    }

    private static DateTime SafeWriteTime(string path)
    {
        try
        {
            return File.GetLastWriteTimeUtc(path);
        }
        catch
        {
            return DateTime.MinValue;
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
            // A file in it is in use (a PowerShell still running from it). The next install tries again.
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
            // Intentionally empty: there is nothing to do for this case.
        }
    }
}
