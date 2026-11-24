using iConsole.Localization;
using System.Diagnostics;
using System.Text.RegularExpressions;
using iTerminal.Core.Services.PackageManager;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// A command-line program that the operating system's package manager installs and iTerminal only uses, never
/// downloads: the same contract as <see cref="FfmpegRuntimeDependency"/> for one program. It is found on the PATH,
/// then in the install folders in <see cref="ExtraDirectories"/> (Windows installers often leave a program off the
/// PATH), and a program that is there already is used as it is and never offered an update.
/// </summary>
internal abstract class SystemToolRuntimeDependency : IRuntimeDependency
{
    private readonly string _id;
    private readonly string _name;
    private readonly string _purposeKey;
    private readonly string _purposeFallback;
    private readonly string _unixTool;
    private readonly string _windowsTool;
    private readonly SystemPackageNames _packageNames;
    private readonly Func<string, string?> _findOnPath;
    private readonly IReadOnlyList<string> _extraDirectories;
    private readonly Func<string, TimeSpan, Task<string?>> _readVersion;
    private readonly Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> _run;
    private readonly bool _isWindows;
    private readonly bool _isMac;
    private readonly bool _isPrivileged;
    private readonly Lock _gate = new();
    private (string Path, DateTime Stamp, string? Version)? _cached;

    protected SystemToolRuntimeDependency(
        string id,
        string name,
        string purposeKey,
        string purposeFallback,
        string unixTool,
        string windowsTool,
        SystemPackageNames packageNames,
        IEnumerable<string> extraDirectories,
        Func<string, string?> findOnPath,
        Func<string, TimeSpan, Task<string?>> readVersion,
        Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> run,
        bool isWindows,
        bool isMac,
        bool isPrivileged)
    {
        _id = id;
        _name = name;
        _purposeKey = purposeKey;
        _purposeFallback = purposeFallback;
        _unixTool = unixTool;
        _windowsTool = windowsTool;
        _packageNames = packageNames;
        _extraDirectories = extraDirectories.ToList();
        _findOnPath = findOnPath;
        _readVersion = readVersion;
        _run = run;
        _isWindows = isWindows;
        _isMac = isMac;
        _isPrivileged = isPrivileged;
    }

    public string Id => _id;

    public string Name => _name;

    public string Purpose => UiText.Get(_purposeKey, _purposeFallback);

    /// <summary>The oldest line the app is written for: RAR 5 writes the format the app creates and reads both RAR 4 and 5.</summary>
    public string ExpectedVersion => "5.0.0";

    public bool IsCompatible(Version version) => version.Major >= 5;

    /// <summary>A program found on the system is an existing install, used as it is.</summary>
    public bool IsExistingInstall => InstalledVersion() is not null;

    public string? InstalledVersion()
    {
        string? tool = FindTool();
        if (tool is null)
        {
            return null;
        }

        DateTime stamp = SafeWriteTime(tool);
        lock (_gate)
        {
            if (_cached is { } cached && cached.Path == tool && cached.Stamp == stamp)
            {
                return cached.Version;
            }
        }

        // One short process, cached by path and write time: the version is not in the file name. A program that
        // does not print a version reads as not installed.
        string? version = _readVersion(tool, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        lock (_gate)
        {
            _cached = (tool, stamp, version);
        }

        return version;
    }

    /// <summary>Nothing is published to look up: the package manager installs whatever it offers.</summary>
    public Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public async Task InstallAsync(string version, IProgress<string> progress, CancellationToken cancellationToken)
    {
        SystemPackageInstallPlan plan = SystemPackageInstallPlan.For(_packageNames, _isWindows, _isMac, _isPrivileged, _findOnPath)
            ?? throw new InvalidOperationException(
                string.Format(UiText.Get("Dep.NoPackageManager", "No supported package manager was found (winget, apt, dnf, zypper, pacman, apk or brew). Install {0} from {1}."), _unixTool, "https://www.rarlab.com/"));

        progress.Report(string.Format(UiText.Get("Dep.InstallingWith", "Installing {0} with {1}: {2}"), _name, plan.DisplayName, plan.CommandLine));
        int exitCode = await _run(plan, progress.Report, cancellationToken);
        if (exitCode != 0 && !(_isWindows && FfmpegRuntimeDependency.IsWinGetAlreadyInstalledCode(exitCode)))
        {
            string manual = plan.ManualCommand is null ? string.Empty : $" Run it yourself in a shell: {plan.ManualCommand}";
            throw new InvalidOperationException($"{plan.DisplayName} exited with code {exitCode}.{manual}");
        }

        Invalidate();
        Activate();
        if (InstalledVersion() is null)
        {
            throw new InvalidOperationException($"{plan.DisplayName} finished, but {_name} was not found afterwards. Restart iTerminal or add it to the PATH.");
        }
    }

    /// <summary>
    /// A program found in an install folder that is not on the PATH is added to it for this process, so it runs by its
    /// name, and the "is it here" answer cached before the install is dropped.
    /// </summary>
    public void Activate()
    {
        string? tool = FindTool();
        if (tool is not null && _findOnPath(_unixTool) is null)
        {
            string? directory = Path.GetDirectoryName(tool);
            if (directory is not null)
            {
                Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            }
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

    /// <summary>The program on the PATH, else in one of the install folders.</summary>
    internal string? FindTool()
    {
        // The PATH lookup takes the bare name: it adds the Windows extensions itself.
        if (_findOnPath(_unixTool) is { } onPath)
        {
            return onPath;
        }

        string file = _isWindows ? _windowsTool : _unixTool;
        return _extraDirectories
            .Select(directory => Path.Combine(directory, file))
            .FirstOrDefault(File.Exists);
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

    /// <summary>The install folders of WinRAR, which holds both programs on Windows. Empty elsewhere.</summary>
    internal static IEnumerable<string> WinRarFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            yield return Path.Combine(Environment.GetFolderPath(root), "WinRAR");
        }
    }

    /// <summary>
    /// Runs the program with no arguments, its stdin closed, and reads the version from the banner it prints (group 1 of
    /// <paramref name="versionPattern"/>). A program that prints no version, or does not finish in
    /// <paramref name="timeout"/>, reads as not installed.
    /// </summary>
    internal static async Task<string?> ReadVersionAsync(string path, TimeSpan timeout, Regex versionPattern)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);
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

            Match match = versionPattern.Match(await output + "\n" + await errors);
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>RARLAB's <c>rar</c>: creates RAR archives. Its Windows program is <c>Rar.exe</c>, in the WinRAR folder.</summary>
internal sealed class RarRuntimeDependency : SystemToolRuntimeDependency
{
    public const string DependencyId = "rar";

    /// <summary>The packages that carry the program: WinRAR on Windows (it has the command line), <c>rar</c> elsewhere.</summary>
    internal static readonly SystemPackageNames PackageNames = new(
        WinGet: "RARLab.WinRAR",
        Homebrew: "rar",
        Apt: "rar",
        Dnf: "rar",
        Zypper: "rar",
        // rar is in the AUR, not the Arch repositories: pacman reports it missing and the install says so.
        Pacman: "rar",
        Apk: "rar");

    public RarRuntimeDependency()
        : this(
            GitRuntimeDependency.FindOnPath,
            WinRarFolders(),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            Environment.IsPrivilegedProcess,
            (plan, onLine, token) => PackageManagerProcess.RunStreamingAsync(plan.Program, plan.Arguments, onLine, token, plan.Environment))
    {
    }

    internal RarRuntimeDependency(
        Func<string, string?> findOnPath,
        IEnumerable<string> extraDirectories,
        bool isWindows,
        bool isMac,
        bool isPrivileged,
        Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> run,
        Func<string, TimeSpan, Task<string?>>? readVersion = null)
        : base(
            DependencyId, "RAR", "Dep.RarPurpose", "Creating RAR archives", "rar", "Rar.exe", PackageNames,
            extraDirectories, findOnPath,
            readVersion ?? ((path, timeout) => ReadVersionAsync(path, timeout, VersionPattern)),
            run, isWindows, isMac, isPrivileged)
    {
    }

    /// <summary>The banner <c>rar</c> prints with no arguments: "RAR 7.23   Copyright (c) 1993-2026 Alexander Roshal".</summary>
    private static readonly Regex VersionPattern = new(@"^\s*RAR (\d+(?:\.\d+)+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
}

/// <summary>RARLAB's <c>unrar</c>: reads RAR archives on a machine without 7-Zip. Its Windows program is <c>UnRAR.exe</c>.</summary>
internal sealed class UnrarRuntimeDependency : SystemToolRuntimeDependency
{
    public const string DependencyId = "unrar";

    internal static readonly SystemPackageNames PackageNames = new(
        WinGet: "RARLab.WinRAR",
        Homebrew: "unrar",
        Apt: "unrar",
        Dnf: "unrar",
        Zypper: "unrar",
        Pacman: "unrar",
        Apk: "unrar");

    public UnrarRuntimeDependency()
        : this(
            GitRuntimeDependency.FindOnPath,
            WinRarFolders(),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            Environment.IsPrivilegedProcess,
            (plan, onLine, token) => PackageManagerProcess.RunStreamingAsync(plan.Program, plan.Arguments, onLine, token, plan.Environment))
    {
    }

    internal UnrarRuntimeDependency(
        Func<string, string?> findOnPath,
        IEnumerable<string> extraDirectories,
        bool isWindows,
        bool isMac,
        bool isPrivileged,
        Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> run,
        Func<string, TimeSpan, Task<string?>>? readVersion = null)
        : base(
            DependencyId, "UNRAR", "Dep.UnrarPurpose", "Opening and unpacking RAR archives without 7-Zip", "unrar", "UnRAR.exe", PackageNames,
            extraDirectories, findOnPath,
            readVersion ?? ((path, timeout) => ReadVersionAsync(path, timeout, VersionPattern)),
            run, isWindows, isMac, isPrivileged)
    {
    }

    /// <summary>The banner <c>unrar</c> prints with no arguments: "UNRAR 7.23 freeware      Copyright (c) 1993-2026 Alexander Roshal".</summary>
    private static readonly Regex VersionPattern = new(@"^\s*UNRAR (\d+(?:\.\d+)+)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
}
