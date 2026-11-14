using System.Diagnostics;
using iTerminal.Core.Services.PackageManager;
using iTerminal.Core.Services.YouTube;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// <c>yt-dlp</c>, which the YouTube dialog uses to search and to find the stream it plays. Like ffmpeg and git it is never
/// downloaded by iTerminal: the operating system's package manager installs it, so it lands on the PATH and is updated with
/// the rest of the system. A program that is there already is used as it is and never offered an update.
///
/// <para>yt-dlp is dated (<c>2025.09.26</c>) and YouTube changes often, so the line that counts as compatible is the year: a
/// newer release in any later year is accepted as it is, and only a release from before the build's line is offered.</para>
/// </summary>
public sealed class YtDlpRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "yt-dlp";

    /// <summary>The oldest release the YouTube code is written for; the year is the compatibility line.</summary>
    internal const string MinimumVersion = "2025.01.01";

    internal static readonly SystemPackageNames PackageNames = new(
        WinGet: "yt-dlp.yt-dlp",
        Homebrew: "yt-dlp",
        Apt: "yt-dlp",
        Dnf: "yt-dlp",
        Zypper: "yt-dlp",
        Pacman: "yt-dlp",
        Apk: "yt-dlp");

    private readonly Func<string, string?> _findOnPath;
    private readonly IReadOnlyList<string> _extraDirectories;
    private readonly Func<string, TimeSpan, Task<string?>> _readVersion;
    private readonly Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> _run;
    private readonly bool _isWindows;
    private readonly bool _isMac;
    private readonly bool _isPrivileged;
    private readonly object _gate = new();
    private (string Path, DateTime Stamp, string? Version)? _cached;

    public YtDlpRuntimeDependency()
        : this(
            GitRuntimeDependency.FindOnPath,
            DefaultExtraDirectories(),
            ReadVersionAsync,
            (plan, onLine, token) => PackageManagerProcess.RunStreamingAsync(plan.Program, plan.Arguments, onLine, token, plan.Environment),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            Environment.IsPrivilegedProcess)
    {
    }

    internal YtDlpRuntimeDependency(
        Func<string, string?> findOnPath,
        IEnumerable<string> extraDirectories,
        Func<string, TimeSpan, Task<string?>> readVersion,
        Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> run,
        bool isWindows,
        bool isMac,
        bool isPrivileged)
    {
        _findOnPath = findOnPath;
        _extraDirectories = extraDirectories.ToList();
        _readVersion = readVersion;
        _run = run;
        _isWindows = isWindows;
        _isMac = isMac;
        _isPrivileged = isPrivileged;
    }

    public string Id => DependencyId;

    public string Name => "yt-dlp";

    public string Purpose => "YouTube search and playback (Tools > YouTube)";

    public string ExpectedVersion => MinimumVersion;

    public bool IsCompatible(Version version) => version.Major >= 2025;

    /// <summary>A system install: used as found, and the system updates it.</summary>
    public bool IsExistingInstall => InstalledVersion() is not null;

    public string? ExistingInstallNote =>
        FindTool() is { } path && InstalledVersion() is not null ? $"Using the yt-dlp that is installed ({path})." : null;

    public string? InstalledVersion()
    {
        string? path = FindTool();
        if (path is null)
        {
            return null;
        }

        DateTime stamp = SafeWriteTime(path);
        lock (_gate)
        {
            if (_cached is { } cached && cached.Path == path && cached.Stamp == stamp)
            {
                return cached.Version;
            }
        }

        // One short process, cached by path and write time: the version is not in the file name. A program that does not
        // answer --version is not a working yt-dlp and reads as not installed.
        string? version = _readVersion(path, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        lock (_gate)
        {
            _cached = (path, stamp, version);
        }

        return version;
    }

    /// <summary>Nothing is published to look up: the package manager installs whatever it offers.</summary>
    public Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        SystemPackageInstallPlan plan = SystemPackageInstallPlan.For(PackageNames, _isWindows, _isMac, _isPrivileged, _findOnPath)
            ?? throw new InvalidOperationException(
                "No supported package manager was found (winget, apt, dnf, zypper, pacman, apk or brew). Install yt-dlp from https://github.com/yt-dlp/yt-dlp.");

        progress.Report($"Installing yt-dlp with {plan.DisplayName}: {plan.CommandLine}");
        int exitCode = await _run(plan, progress.Report, cancellationToken);
        if (exitCode != 0)
        {
            string manual = plan.ManualCommand is null ? string.Empty : $" Run it yourself in a shell: {plan.ManualCommand}";
            throw new InvalidOperationException($"{plan.DisplayName} exited with code {exitCode}.{manual}");
        }

        Invalidate();
        Activate();
        if (InstalledVersion() is null)
        {
            throw new InvalidOperationException($"{plan.DisplayName} finished, but yt-dlp was not found afterwards. Restart iTerminal or add it to the PATH.");
        }
    }

    /// <summary>A program installed in a standard place that is not on this process's PATH is added to it, so the YouTube dialog finds it.</summary>
    public void Activate()
    {
        string? path = FindTool();
        if (path is null || _findOnPath(YtDlpYouTubeService.Executable) is not null)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    private string? FindTool()
    {
        if (_findOnPath(YtDlpYouTubeService.Executable) is { } onPath)
        {
            return onPath;
        }

        string file = _isWindows ? YtDlpYouTubeService.Executable + ".exe" : YtDlpYouTubeService.Executable;
        return _extraDirectories.Select(directory => Path.Combine(directory, file)).FirstOrDefault(File.Exists);
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

    private static IEnumerable<string> DefaultExtraDirectories()
    {
        if (OperatingSystem.IsWindows())
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return [Path.Combine(local, "Microsoft", "WinGet", "Links")];
        }

        return ["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"];
    }

    /// <summary>The first line of <c>yt-dlp --version</c>, when it is a version number.</summary>
    internal static async Task<string?> ReadVersionAsync(string path, TimeSpan timeout)
    {
        PackageManagerProcess.ProcessResult result = await PackageManagerProcess.RunCaptureAsync(path, timeout, CancellationToken.None, "--version");
        if (!result.Success)
        {
            return null;
        }

        string line = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return line.Length > 0 && char.IsDigit(line[0]) ? line : null;
    }
}
