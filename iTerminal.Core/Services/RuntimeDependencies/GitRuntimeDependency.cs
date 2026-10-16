using System.Diagnostics;
using System.Text.RegularExpressions;
using iTerminal.Core.Services.PackageManager;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// The <c>git</c> program, which the Git client, the git drive and the file-panel git colours run. It is never
/// downloaded by iTerminal itself: when it is missing, the operating system's own package manager installs it
/// (winget, apt, dnf, zypper, pacman, apk, Homebrew), so it lands where every other program expects it and is
/// updated with the rest of the system. A git that is there already is used as it is and never offered an update.
/// </summary>
public sealed partial class GitRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "git";

    /// <summary>The oldest line the app is written for; any 2.x is compatible.</summary>
    internal const string MinimumVersion = "2.0.0";

    private readonly Func<string, string?> _findOnPath;
    private readonly IReadOnlyList<string> _extraDirectories;
    private readonly Func<string, TimeSpan, Task<string?>> _readVersion;
    private readonly Func<GitInstallPlan, Action<string>, CancellationToken, Task<int>> _run;
    private readonly bool _isWindows;
    private readonly bool _isMac;
    private readonly bool _isPrivileged;
    private readonly object _gate = new();
    private (string Path, DateTime Stamp, string? Version)? _cached;

    public GitRuntimeDependency()
        : this(
            FindOnPath,
            DefaultExtraDirectories(),
            ReadVersionAsync,
            (plan, onLine, token) => PackageManagerProcess.RunStreamingAsync(plan.Program, plan.Arguments, onLine, token, plan.Environment),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            Environment.IsPrivilegedProcess)
    {
    }

    internal GitRuntimeDependency(
        Func<string, string?> findOnPath,
        IEnumerable<string> extraDirectories,
        Func<string, TimeSpan, Task<string?>> readVersion,
        Func<GitInstallPlan, Action<string>, CancellationToken, Task<int>> run,
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

    public string Name => "Git";

    public string Purpose => "Git client, git drive and git status colours";

    public string ExpectedVersion => MinimumVersion;

    public bool IsCompatible(Version version) => version.Major >= 2;

    /// <summary>A git is a system install: it is used as found and the system updates it.</summary>
    public bool IsExistingInstall => InstalledVersion() is not null;

    public string? ExistingInstallNote => FindGit() is { } path && InstalledVersion() is not null ? $"Using the git that is installed ({path})." : null;

    public string? InstalledVersion()
    {
        string? path = FindGit();
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

        // One short process per program found (cached by path and write time), because the version is not in the
        // file name. A program that is not a working git reads as not installed.
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
        GitInstallPlan plan = GitInstallPlan.For(_isWindows, _isMac, _isPrivileged, _findOnPath)
            ?? throw new InvalidOperationException(
                "No supported package manager was found (winget, apt, dnf, zypper, pacman, apk or brew). Install git from https://git-scm.com/downloads.");

        progress.Report($"Installing git with {plan.DisplayName}: {plan.CommandLine}");
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
            throw new InvalidOperationException($"{plan.DisplayName} finished, but git was not found afterwards. Restart iTerminal or add git to the PATH.");
        }
    }

    /// <summary>
    /// A git installed in a standard place that is not on this process's PATH (the Windows installer updates the
    /// machine PATH, not the one of the program that ran it) is added to it, so the git services find it.
    /// </summary>
    public void Activate()
    {
        string? path = FindGit();
        if (path is null || _findOnPath("git") is not null)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Environment.SetEnvironmentVariable(
                "PATH",
                directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }
    }

    private string? FindGit()
    {
        if (_findOnPath("git") is { } onPath)
        {
            return onPath;
        }

        string file = _isWindows ? "git.exe" : "git";
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
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return
            [
                Path.Combine(programFiles, "Git", "cmd"),
                Path.Combine(local, "Programs", "Git", "cmd"),
            ];
        }

        return ["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"];
    }

    internal static string? FindOnPath(string program)
    {
        string[] extensions = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)
            : [string.Empty];
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string extension in extensions)
            {
                try
                {
                    string candidate = Path.Combine(directory.Trim('"'), program + extension);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry.
                }
            }
        }

        return null;
    }

    internal static async Task<string?> ReadVersionAsync(string gitPath, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = gitPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("--version");
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            using var cts = new CancellationTokenSource(timeout);
            Task<string> output = process.StandardOutput.ReadToEndAsync(cts.Token);
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return process.ExitCode == 0 ? ParseVersion(await output) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>"git version 2.43.0", "git version 2.47.0.windows.1" and "git version 2.39.5 (Apple Git-154)" all read as the first three numbers.</summary>
    internal static string? ParseVersion(string output)
    {
        Match match = VersionRegex().Match(output);
        return match.Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}" : null;
    }

    [GeneratedRegex(@"git version (\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionRegex();
}

/// <summary>How to install git with the package manager of this system: a program, its arguments and its environment.</summary>
internal sealed record GitInstallPlan(
    string DisplayName,
    string Program,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment,
    bool RequiresSudo,
    string? ManualCommand = null)
{
    public string CommandLine => string.Join(' ', new[] { Program }.Concat(Arguments));

    /// <summary>
    /// The first package manager found, or null. Windows has winget, which asks for elevation itself. The others
    /// need root: when this process is not root the command runs under <c>sudo -n</c> (no password prompt, since a
    /// prompt has no terminal to appear on here), and a failure says what to run by hand.
    /// </summary>
    public static GitInstallPlan? For(bool isWindows, bool isMac, bool isPrivileged, Func<string, string?> findOnPath)
    {
        if (isWindows)
        {
            return findOnPath("winget") is null
                ? null
                : new GitInstallPlan(
                    "WinGet",
                    "winget",
                    ["install", "--id", "Git.Git", "-e", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
                    null,
                    false);
        }

        if (isMac)
        {
            return findOnPath("brew") is null ? null : new GitInstallPlan("Homebrew", "brew", ["install", "git"], null, false);
        }

        (string Display, string Tool, string[] Arguments, bool NonInteractive)[] candidates =
        [
            ("APT", "apt-get", ["install", "-y", "git"], true),
            ("DNF", "dnf", ["install", "-y", "git"], false),
            ("Zypper", "zypper", ["--non-interactive", "install", "git"], false),
            ("Pacman", "pacman", ["-S", "--noconfirm", "git"], false),
            ("APK", "apk", ["add", "git"], false),
        ];
        foreach ((string display, string tool, string[] arguments, bool nonInteractive) in candidates)
        {
            if (findOnPath(tool) is null)
            {
                continue;
            }

            Dictionary<string, string>? environment = nonInteractive ? new() { ["DEBIAN_FRONTEND"] = "noninteractive" } : null;
            if (isPrivileged)
            {
                return new GitInstallPlan(display, tool, arguments, environment, false);
            }

            return findOnPath("sudo") is null
                ? new GitInstallPlan(display, tool, arguments, environment, false)
                : new GitInstallPlan(display, "sudo", ["-n", .. (nonInteractive ? new[] { "env", "DEBIAN_FRONTEND=noninteractive" } : []), tool, .. arguments], null, true, $"sudo {tool} {string.Join(' ', arguments)}");
        }

        return null;
    }
}
