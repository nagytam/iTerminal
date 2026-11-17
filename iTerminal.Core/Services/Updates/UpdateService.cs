using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Services.Updates;

/// <summary>
/// Compares the running version with the stable releases of the iTerminal repository. On Windows the release
/// is the <c>.msi</c> for an installed copy, or the portable archive for an unzipped one (the copy that carries
/// <see cref="PortableMarker"/>). Either download is checked against the digest GitHub records for it, and the
/// program in it against its own Authenticode signature, before it is run or copied over the running files.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string Repository = "nagytam/iTerminal";
    public const string SkippedSetting = "Updates.Skipped";

    /// <summary>The empty file an unzipped portable copy carries beside iTerminal.exe. Only the portable archive contains it.</summary>
    public const string PortableMarker = "iTerminal.portable";

    /// <summary>The end of the portable archive's name, as in <c>iTerminal-1.2.3-x64-portable.zip</c>.</summary>
    public const string PortableArchiveSuffix = "-x64-portable.zip";

    internal const int MaxLogLines = 20;

    private const string ExecutableName = "iTerminal.exe";
    private const string StagingFolder = "staging";
    private const string UpdateScriptName = "apply-update.cmd";

    private readonly ISettingsService _settings;
    private readonly GitHubReleaseFeed _feed;
    private readonly IAuthenticodeVerifier _authenticode;
    private readonly Version? _current;
    private readonly Func<string, bool> _startInstaller;
    private readonly Func<string, bool> _startPortableScript;
    private readonly string _downloadDirectory;
    private readonly string _installDirectory;
    private readonly UpdateInstallKind _kind;
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private UpdateInfo? _available;
    private bool _installing;

    public UpdateService(ISettingsService settings, IAuthenticodeVerifier authenticode)
        : this(
            settings,
            new GitHubReleaseFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }),
            AboutDialog.Version,
            StartWindowsInstaller,
            Path.Combine(Path.GetTempPath(), "iTerminal-update"),
            KindFor(OperatingSystem.IsWindows(), AppContext.BaseDirectory),
            authenticode,
            StartPortableScript,
            AppContext.BaseDirectory)
    {
    }

    internal UpdateService(
        ISettingsService settings,
        GitHubReleaseFeed feed,
        string currentVersion,
        Func<string, bool> startInstaller,
        string downloadDirectory,
        UpdateInstallKind kind,
        IAuthenticodeVerifier? authenticode = null,
        Func<string, bool>? startPortableScript = null,
        string installDirectory = "")
    {
        _settings = settings;
        _feed = feed;
        _authenticode = authenticode ?? new WinVerifyTrustAuthenticodeVerifier();
        CurrentVersion = currentVersion;
        _current = ParseVersion(currentVersion);
        _startInstaller = startInstaller;
        _startPortableScript = startPortableScript ?? (_ => false);
        _downloadDirectory = downloadDirectory;
        _installDirectory = installDirectory;
        _kind = kind;
    }

    public string CurrentVersion { get; }

    public UpdateInfo? Available
    {
        get
        {
            lock (_gate)
            {
                return _available;
            }
        }
    }

    public bool IsSkipped => Available is { } update && _settings.Get(SkippedSetting) == update.Version;

    public UpdateInstallKind InstallKind => _kind;

    public bool CanInstall => _kind != UpdateInstallKind.Unsupported;

    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return _installing;
            }
        }
    }

    public IReadOnlyList<string> Log
    {
        get
        {
            lock (_gate)
            {
                return _log.ToList();
            }
        }
    }

    public string ReleasesAddress => $"https://github.com/{Repository}/releases";

    /// <summary>
    /// How the copy in <paramref name="baseDirectory"/> was installed. Only Windows can update itself; there, a copy
    /// with the portable marker beside it is a portable one, and anything else came from the installer.
    /// </summary>
    internal static UpdateInstallKind KindFor(bool windows, string baseDirectory)
    {
        if (!windows)
        {
            return UpdateInstallKind.Unsupported;
        }

        return IsPortable(baseDirectory)
            ? UpdateInstallKind.PortableArchive
            : UpdateInstallKind.Installer;
    }

    /// <summary>Whether the copy in <paramref name="baseDirectory"/> is an unzipped portable one (on any system).</summary>
    internal static bool IsPortable(string baseDirectory) => File.Exists(Path.Combine(baseDirectory, PortableMarker));

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        if (_current is null)
        {
            return null;
        }

        try
        {
            IReadOnlyList<string> versions = await _feed.ListVersionsAsync(Repository, cancellationToken);
            Version? newest = versions.Select(ParseVersion).OfType<Version>().Where(version => version > _current).OrderDescending().FirstOrDefault();
            if (newest is null)
            {
                SetAvailable(null);
                return null;
            }

            string text = newest.ToString();
            GitHubAsset? installer = null;
            if (CanInstall)
            {
                GitHubRelease release = await _feed.GetReleaseAsync(Repository, text, cancellationToken);
                installer = release.Assets.FirstOrDefault(IsDownloadForThisInstall);
            }

            UpdateInfo update = new(text, installer);
            SetAvailable(update);
            return update;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Offline, rate limited, or a release this build cannot read: there is nothing to offer, and nothing to report.
            return null;
        }
    }

    public void Skip()
    {
        if (Available is { } update)
        {
            _settings.Set(SkippedSetting, update.Version);
            _settings.Write();
        }
    }

    public async Task<bool> InstallAsync(CancellationToken cancellationToken)
    {
        UpdateInfo? update = Available;
        if (update is null || !CanInstall)
        {
            return false;
        }

        lock (_gate)
        {
            if (_installing)
            {
                return false;
            }

            _installing = true;
        }

        try
        {
            if (update.Installer is not { } asset)
            {
                AddLog(_kind == UpdateInstallKind.PortableArchive
                    ? $"iTerminal {update.Version} has no portable archive. Download it from {ReleasesAddress}"
                    : $"iTerminal {update.Version} has no Windows installer. Download it from {ReleasesAddress}");
                return false;
            }

            Directory.CreateDirectory(_downloadDirectory);
            // The name comes from GitHub: only its file name is used, never a path.
            string package = Path.Combine(_downloadDirectory, Path.GetFileName(asset.Name));
            await _feed.DownloadAsync(asset, package, new LineProgress(AddLog), cancellationToken);

            return _kind == UpdateInstallKind.PortableArchive
                ? ApplyPortableArchive(package)
                : StartMsi(package);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AddLog($"The update could not be installed: {exception.Message}");
            return false;
        }
        finally
        {
            lock (_gate)
            {
                _installing = false;
            }
        }
    }

    private bool IsDownloadForThisInstall(GitHubAsset asset) =>
        _kind == UpdateInstallKind.PortableArchive
            ? asset.Name.EndsWith(PortableArchiveSuffix, StringComparison.OrdinalIgnoreCase)
            : asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);

    private bool StartMsi(string package)
    {
        if (!AcceptSignature(_authenticode.Verify(package)))
        {
            TryDelete(package);
            return false;
        }

        AddLog("Starting the installer. iTerminal closes so its files can be replaced.");
        if (!_startInstaller(package))
        {
            AddLog("The installer was not started.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Unpacks the portable archive beside the download, checks the program in it, and starts a script that
    /// copies it over the install folder once iTerminal has closed. The script restarts iTerminal and removes
    /// its own files; nothing else in the install folder is removed, so the person's own files stay.
    /// </summary>
    private bool ApplyPortableArchive(string archive)
    {
        string staging = Path.Combine(_downloadDirectory, StagingFolder);
        bool started = false;
        try
        {
            if (_installDirectory.Length == 0 || _installDirectory.Contains('%'))
            {
                AddLog("This copy of iTerminal sits in a folder the update cannot be applied to automatically. Download the archive and unzip it by hand.");
                return false;
            }

            DeleteDirectory(staging);
            ExtractArchive(archive, staging);

            if (!File.Exists(Path.Combine(staging, PortableMarker)) || !File.Exists(Path.Combine(staging, ExecutableName)))
            {
                AddLog($"The archive is not a portable iTerminal release: it has no {ExecutableName}.");
                return false;
            }

            if (!AcceptSignature(_authenticode.Verify(Path.Combine(staging, ExecutableName))))
            {
                return false;
            }

            string script = Path.Combine(_downloadDirectory, UpdateScriptName);
            File.WriteAllText(script, UpdateScript(Environment.ProcessId, staging, Path.TrimEndingDirectorySeparator(_installDirectory), archive), new UTF8Encoding(false));

            AddLog("Starting the installer. iTerminal closes so its files can be replaced.");
            if (!_startPortableScript(script))
            {
                AddLog("The installer was not started.");
                return false;
            }

            started = true;
            return true;
        }
        finally
        {
            if (!started)
            {
                TryDelete(archive);
                DeleteDirectory(staging);
            }
        }
    }

    /// <summary>
    /// The batch file that finishes a portable update. It waits until iTerminal has exited (its files are locked
    /// while it runs), copies the new files over the install folder, starts iTerminal from there and deletes
    /// the staged files and itself. <c>robocopy</c> exits with 8 or more only on a failure; then the staged
    /// files are kept and iTerminal starts the copy that is there.
    /// </summary>
    internal static string UpdateScript(int processId, string staging, string installDirectory, string archive)
    {
        string executable = Path.Combine(installDirectory, ExecutableName);
        return string.Join("\r\n",
        [
            "@echo off",
            "chcp 65001 >nul",
            $"set \"PID={processId}\"",
            ":wait",
            "tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul",
            "if not errorlevel 1 (ping -n 2 127.0.0.1 >nul & goto wait)",
            $"robocopy \"{staging}\" \"{installDirectory}\" /E /R:10 /W:1 /NFL /NDL /NJH /NJS /NP >nul",
            "set \"RESULT=%ERRORLEVEL%\"",
            $"if %RESULT% LSS 8 rd /s /q \"{staging}\"",
            $"if %RESULT% LSS 8 del \"{archive}\"",
            $"start \"\" \"{executable}\"",
            "(goto) 2>nul & del \"%~f0\"",
            string.Empty,
        ]);
    }

    /// <summary>
    /// Decides what to do with an Authenticode verdict. A valid signature is reported (with the signer when the
    /// verifier knows it); an unsigned program is a warning and still runs (release signing is not configured
    /// yet); anything else — a signature that does not match, an untrusted chain, or a check that could not be
    /// made — stops the update, and the caller removes the download.
    /// </summary>
    private bool AcceptSignature(AuthenticodeResult signature)
    {
        switch (signature.Status)
        {
            case AuthenticodeStatus.Valid:
                AddLog(string.IsNullOrEmpty(signature.Signer)
                    ? "Installer signature verified."
                    : $"Installer signature verified: {signature.Signer}.");
                return true;

            case AuthenticodeStatus.NotSigned:
                AddLog("Warning: the installer is not Authenticode-signed, so its publisher cannot be verified. Its SHA-256 matches the digest GitHub publishes.");
                return true;

            default:
                AddLog($"The installer's Authenticode signature was not accepted: {signature.Message} The installer was not started.");
                return false;
        }
    }

    /// <summary>
    /// Unpacks a release archive into <paramref name="destination"/>. An entry that would land outside it stops
    /// the whole unpacking (an archive from GitHub is digest-checked, but the paths are still not trusted).
    /// </summary>
    private static void ExtractArchive(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{entry.FullName} would be unpacked outside the update folder.");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that cannot be removed is left for inspection; it was never started.
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left in place: the next update starts by clearing it again.
        }
    }

    /// <summary>
    /// The installer is a per-machine MSI, so Windows asks for administrator rights itself. It runs with a
    /// progress bar and no wizard, and does not restart the machine on its own.
    /// </summary>
    private static bool StartWindowsInstaller(string package)
    {
        ProcessStartInfo start = new("msiexec.exe") { UseShellExecute = true };
        start.ArgumentList.Add("/i");
        start.ArgumentList.Add(package);
        start.ArgumentList.Add("/passive");
        start.ArgumentList.Add("/norestart");
        using Process? process = Process.Start(start);
        return process is not null;
    }

    /// <summary>The update script runs hidden, and outlives this process: it waits for it to close.</summary>
    private static bool StartPortableScript(string script)
    {
        ProcessStartInfo start = new("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add(script);
        using Process? process = Process.Start(start);
        return process is not null;
    }

    private void SetAvailable(UpdateInfo? update)
    {
        lock (_gate)
        {
            _available = update;
        }
    }

    private void AddLog(string line)
    {
        lock (_gate)
        {
            _log.Add(line);
            if (_log.Count > MaxLogLines)
            {
                _log.RemoveRange(0, _log.Count - MaxLogLines);
            }
        }
    }

    /// <summary>The numeric part of a version ("1.2.3-beta" is 1.2.3); null when there is none.</summary>
    internal static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string numeric = text.TrimStart('v');
        int cut = numeric.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            numeric = numeric[..cut];
        }

        return Version.TryParse(numeric, out Version? version) ? version : null;
    }

    private sealed class LineProgress(Action<string> onLine) : IProgress<string>
    {
        public void Report(string value) => onLine(value);
    }
}
