using System.Diagnostics;
using System.Text.RegularExpressions;
using iTerminal.Core.Audio.Decoders;
using iTerminal.Core.Services.PackageManager;
using iTerminal.Core.Video;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// <c>ffmpeg</c> and <c>ffprobe</c>, which decode every audio format the managed decoders do not (FLAC, Ogg, Opus,
/// M4A, …), play video in the F3 video viewer and show pictures in the image viewer. Like git they are never
/// downloaded by iTerminal itself: when missing, the operating system's own package manager installs them (winget,
/// Homebrew, apt, dnf, zypper, pacman, apk), so they land on the PATH and are updated with the rest of the system.
/// Both programs ship in the one package; the dependency counts as installed only when both are found, because the
/// viewers cannot work with one of them. Programs that are there already are used as they are and never offered an
/// update.
/// </summary>
public sealed partial class FfmpegRuntimeDependency : IRuntimeDependency
{
    public const string DependencyId = "ffmpeg";

    /// <summary>The oldest line the app is written for: rawvideo output, <c>-ss</c> before <c>-i</c> and JSON from ffprobe all exist from 4.0.</summary>
    internal const string MinimumVersion = "4.0.0";

    internal static readonly SystemPackageNames PackageNames = new(
        WinGet: "Gyan.FFmpeg",
        Homebrew: "ffmpeg",
        Apt: "ffmpeg",
        // Fedora's own repositories carry the patent-free build as ffmpeg-free; the full ffmpeg needs RPM Fusion.
        Dnf: "ffmpeg-free",
        Zypper: "ffmpeg",
        Pacman: "ffmpeg",
        Apk: "ffmpeg");

    private readonly Func<string, string?> _findOnPath;
    private readonly IReadOnlyList<string> _extraDirectories;
    private readonly Func<string, TimeSpan, Task<string?>> _readVersion;
    private readonly Func<SystemPackageInstallPlan, Action<string>, CancellationToken, Task<int>> _run;
    private readonly bool _isWindows;
    private readonly bool _isMac;
    private readonly bool _isPrivileged;
    private readonly object _gate = new();
    private (string Path, DateTime Stamp, string? Version)? _cached;

    public FfmpegRuntimeDependency()
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

    internal FfmpegRuntimeDependency(
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

    public string Name => "FFmpeg";

    public string Purpose => "Video and picture viewers, FLAC/Ogg/Opus/M4A playback";

    public string ExpectedVersion => MinimumVersion;

    public bool IsCompatible(Version version) => version.Major >= 4;

    /// <summary>A system install: used as found, and the system updates it.</summary>
    public bool IsExistingInstall => InstalledVersion() is not null;

    public string? ExistingInstallNote =>
        FindTool("ffmpeg") is { } path && InstalledVersion() is not null ? $"Using the ffmpeg that is installed ({path})." : null;

    public string? InstalledVersion()
    {
        string? ffmpeg = FindTool("ffmpeg");
        if (ffmpeg is null || FindTool("ffprobe") is null)
        {
            return null;
        }

        DateTime stamp = SafeWriteTime(ffmpeg);
        lock (_gate)
        {
            if (_cached is { } cached && cached.Path == ffmpeg && cached.Stamp == stamp)
            {
                return cached.Version;
            }
        }

        // One short process per program found (cached by path and write time), because the version is not in the
        // file name. A program that is not a working ffmpeg reads as not installed.
        string? version = _readVersion(ffmpeg, TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        lock (_gate)
        {
            _cached = (ffmpeg, stamp, version);
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
                "No supported package manager was found (winget, apt, dnf, zypper, pacman, apk or brew). Install ffmpeg from https://ffmpeg.org/download.html.");

        progress.Report($"Installing ffmpeg with {plan.DisplayName}: {plan.CommandLine}");
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
            throw new InvalidOperationException($"{plan.DisplayName} finished, but ffmpeg and ffprobe were not found afterwards. Restart iTerminal or add them to the PATH.");
        }
    }

    /// <summary>
    /// Programs installed in a standard place that is not on this process's PATH (winget's link folder is added to
    /// the user's PATH, not to the one of the program that ran it) are added to it, so the decoders find them, and
    /// the "is ffmpeg here" answers cached before the install are dropped.
    /// </summary>
    public void Activate()
    {
        foreach (string tool in new[] { "ffmpeg", "ffprobe" })
        {
            string? path = FindTool(tool);
            if (path is null || _findOnPath(tool) is not null)
            {
                continue;
            }

            string? directory = Path.GetDirectoryName(path);
            if (directory is not null)
            {
                Environment.SetEnvironmentVariable(
                    "PATH",
                    directory + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
            }
        }

        ResetAvailabilityProbes();
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cached = null;
        }

        ResetAvailabilityProbes();
    }

    /// <summary>The decoders probe for the programs once per process; after an install that answer is stale.</summary>
    private static void ResetAvailabilityProbes()
    {
        FfmpegAudioDecoder.ResetAvailability();
        FfmpegVideoDecoder.ResetProbeAvailability();
    }

    private string? FindTool(string tool)
    {
        if (_findOnPath(tool) is { } onPath)
        {
            return onPath;
        }

        string file = _isWindows ? tool + ".exe" : tool;
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
            string winGet = Path.Combine(local, "Microsoft", "WinGet");
            List<string> directories = [Path.Combine(winGet, "Links")];

            // The portable package winget unpacks keeps its programs in <package>\ffmpeg-<version>-full_build\bin.
            try
            {
                string packages = Path.Combine(winGet, "Packages");
                if (Directory.Exists(packages))
                {
                    foreach (string package in Directory.EnumerateDirectories(packages, "Gyan.FFmpeg*"))
                    {
                        foreach (string build in Directory.EnumerateDirectories(package, "ffmpeg-*"))
                        {
                            directories.Add(Path.Combine(build, "bin"));
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Looked for, not required: the PATH is the first place anyway.
            }

            return directories;
        }

        return ["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"];
    }

    internal static async Task<string?> ReadVersionAsync(string ffmpegPath, TimeSpan timeout)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-version");
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

    /// <summary>
    /// "ffmpeg version 7.1.1-essentials_build-www.gyan.dev", "ffmpeg version n6.0", "ffmpeg version 6.1.1-3ubuntu5"
    /// and "ffmpeg version 7.0" read as the first numbers. A development build ("N-117000-g1a2b3c4") has no number
    /// but is a working ffmpeg that is newer than any release, so it reads as a very high version rather than as missing.
    /// </summary>
    internal static string? ParseVersion(string output)
    {
        Match match = VersionRegex().Match(output);
        if (match.Success)
        {
            string patch = match.Groups[3].Success ? match.Groups[3].Value : "0";
            return $"{match.Groups[1].Value}.{match.Groups[2].Value}.{patch}";
        }

        return DevelopmentBuildRegex().IsMatch(output) ? "99.0.0" : null;
    }

    [GeneratedRegex(@"ffmpeg version [nN]?(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"ffmpeg version N-\d+")]
    private static partial Regex DevelopmentBuildRegex();
}
