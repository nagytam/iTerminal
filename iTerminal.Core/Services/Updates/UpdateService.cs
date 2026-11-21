using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Helper;
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

    /// <summary>The WinGet package identifier, as the manifests and <c>winget upgrade</c> name it.</summary>
    public const string WinGetPackageId = "TamasNagy.iTerminal";

    /// <summary>
    /// The registry value the MSI writes when it is installed with <c>MANAGEDBYWINGET=1</c>, which the WinGet manifest
    /// passes. A direct install or an update from the app does not pass it, so the value is removed again.
    /// </summary>
    internal const string WinGetRegistryKey = @"HKEY_LOCAL_MACHINE\Software\iTerminal";
    internal const string WinGetRegistryValue = "ManagedBy";
    internal const string WinGetRegistryData = "WinGet";

    internal const int MaxLogLines = 20;

    private const string ExecutableName = "iTerminal.exe";
    private const string LinuxExecutableName = "iTerminal";
    private const string StagingFolder = "staging";
    private const string BackupFolder = "backup";
    private const string UpdateLogName = "apply-update.log";
    private const string UpdateScriptName = "apply-update.cmd";
    private const string ShellUpdateScriptName = "apply-update.sh";

    private readonly ISettingsService _settings;
    private readonly GitHubReleaseFeed _feed;
    private readonly IAuthenticodeVerifier _authenticode;
    private readonly Version? _current;
    private readonly Func<string, bool> _startInstaller;
    private readonly Func<string, bool> _startPortableScript;
    private readonly string _downloadDirectory;
    private readonly string _installDirectory;
    private readonly UpdateInstallKind _kind;
    private readonly bool _windows;
    private readonly string? _portableSuffix;
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
            DefaultDownloadDirectory(OperatingSystem.IsWindows()),
            KindFor(OperatingSystem.IsWindows(), AppContext.BaseDirectory, OperatingSystem.IsWindows() && IsManagedByWinGet()),
            authenticode,
            OperatingSystem.IsWindows() ? StartPortableScript : StartShellScript,
            AppContext.BaseDirectory,
            OperatingSystem.IsWindows(),
            PortableSuffixFor(OperatingSystem.IsWindows(), RuntimeInformation.ProcessArchitecture))
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
        string installDirectory = "",
        bool windows = true,
        string? portableSuffix = PortableArchiveSuffix)
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
        _windows = windows;
        _portableSuffix = portableSuffix;
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

    public bool CanInstall => _kind is not (UpdateInstallKind.Unsupported or UpdateInstallKind.PackageManager);

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
    /// How the copy in <paramref name="baseDirectory"/> was installed. A copy with the portable marker beside it is a
    /// portable one on any system (it updates from its portable archive); on Windows anything else came from the
    /// installer; other systems' packages are only announced.
    /// </summary>
    internal static UpdateInstallKind KindFor(bool windows, string baseDirectory, bool managedByWinGet = false)
    {
        if (IsPortable(baseDirectory))
        {
            return UpdateInstallKind.PortableArchive;
        }

        if (windows && managedByWinGet)
        {
            return UpdateInstallKind.PackageManager;
        }

        return windows ? UpdateInstallKind.Installer : UpdateInstallKind.Unsupported;
    }

    /// <summary>Whether the MSI that installed this copy was run by WinGet (see <see cref="WinGetRegistryKey"/>).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    internal static bool IsManagedByWinGet() =>
        Microsoft.Win32.Registry.GetValue(WinGetRegistryKey, WinGetRegistryValue, null) as string == WinGetRegistryData;

    /// <summary>
    /// The end of the portable archive's name for this system: the zip on Windows, the tar.gz of the matching
    /// architecture on Linux. Null where no portable archive is published.
    /// </summary>
    internal static string? PortableSuffixFor(bool windows, Architecture architecture)
    {
        if (windows)
        {
            return PortableArchiveSuffix;
        }

        return architecture switch
        {
            Architecture.X64 => "-linux-x64-portable.tar.gz",
            Architecture.Arm64 => "-linux-arm64-portable.tar.gz",
            _ => null,
        };
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
            ? _portableSuffix is { } suffix && asset.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
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
    /// copies it over the install folder once iTerminal has closed. The install folder is backed up first and
    /// restored if the copy fails. Windows then starts iTerminal again; Linux does not. Nothing else in the install
    /// folder is removed, so the person's own files stay.
    /// </summary>
    private bool ApplyPortableArchive(string archive)
    {
        string staging = Path.Combine(_downloadDirectory, StagingFolder);
        bool started = false;
        try
        {
            if (_installDirectory.Length == 0)
            {
                AddLog("This copy of iTerminal sits in a folder the update cannot be applied to automatically. Download the archive and unzip it by hand.");
                return false;
            }

            DeleteDirectory(staging);
            ExtractArchive(archive, staging);

            string root = PortableRoot(staging);
            string executable = Path.Combine(root, _windows ? ExecutableName : LinuxExecutableName);
            if (!File.Exists(Path.Combine(root, PortableMarker)) || !File.Exists(executable))
            {
                AddLog($"The archive is not a portable iTerminal release: it has no {Path.GetFileName(executable)}.");
                return false;
            }

            if (_windows)
            {
                if (!AcceptSignature(_authenticode.Verify(executable)))
                {
                    return false;
                }
            }
            else
            {
                AddLog("Linux releases are not Authenticode-signed; the archive matches the SHA-256 digest GitHub publishes.");
            }

            // iTerminal closes before the script runs, so a folder it cannot write to is refused here, while it is still open.
            string installDirectory = Path.TrimEndingDirectorySeparator(_installDirectory);
            if (!CanWriteEverywhere(root, installDirectory))
            {
                AddLog($"iTerminal cannot write to {installDirectory}, so the update was not started and nothing was changed. Download the archive and unzip it there by hand, or run iTerminal with the rights to write to that folder.");
                return false;
            }

            string log = Path.Combine(_downloadDirectory, UpdateLogName);
            string backup = Path.Combine(_downloadDirectory, BackupFolder);
            DeleteDirectory(backup);

            string script;
            string text;
            if (_windows)
            {
                script = Path.Combine(_downloadDirectory, UpdateScriptName);
                text = UpdateScript(Environment.ProcessId, root, installDirectory, archive, backup, log);
            }
            else
            {
                script = Path.Combine(_downloadDirectory, ShellUpdateScriptName);
                text = ShellUpdateScript(Environment.ProcessId, root, installDirectory, archive, staging, backup, log);
            }

            File.WriteAllText(script, text, new UTF8Encoding(false));

            AddLog($"Starting the update. iTerminal closes so its files can be replaced. The script writes its progress to {log}");
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
    /// while it runs), copies the install folder to <paramref name="backup"/>, copies the new files over it and
    /// starts iTerminal from there. If the copy fails, the backup is copied back, so the previous version is what
    /// starts. Only a finished update deletes the staged files, the archive and the backup. Progress goes to
    /// <paramref name="log"/>. Each path is quoted for the batch file, and a <c>%</c> is doubled so it is not read as a variable.
    /// </summary>
    internal static string UpdateScript(int processId, string staging, string installDirectory, string archive, string backup, string log)
    {
        string executable = Path.Combine(installDirectory, ExecutableName);
        string logPath = BatchPath(log);
        return string.Join("\r\n",
        [
            "@echo off",
            "chcp 65001 >nul",
            $"set \"PID={processId}\"",
            ":wait",
            "tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul",
            "if not errorlevel 1 (ping -n 2 127.0.0.1 >nul & goto wait)",
            $"echo iTerminal has closed; backing up the install folder> {logPath}",
            $"robocopy {BatchPath(installDirectory)} {BatchPath(backup)} /E /R:10 /W:1 /NP >> {logPath} 2>&1",
            $"if errorlevel 8 (echo The install folder could not be backed up; nothing was changed.>> {logPath} & goto start)",
            $"echo Copying the new files>> {logPath}",
            $"robocopy {BatchPath(staging)} {BatchPath(installDirectory)} /E /R:10 /W:1 /NP >> {logPath} 2>&1",
            "if errorlevel 8 goto restore",
            $"rd /s /q {BatchPath(staging)}",
            $"del {BatchPath(archive)}",
            $"rd /s /q {BatchPath(backup)}",
            "goto start",
            ":restore",
            $"echo The new files could not be copied; restoring the previous ones.>> {logPath}",
            $"robocopy {BatchPath(backup)} {BatchPath(installDirectory)} /E /R:10 /W:1 /NP >> {logPath} 2>&1",
            ":start",
            $"start \"\" {BatchPath(executable)}",
            "(goto) 2>nul & del \"%~f0\"",
            string.Empty,
        ]);
    }

    /// <summary>
    /// The shell counterpart for Linux: waits for iTerminal to exit, backs up the install folder, copies the new
    /// files over it with <c>cp -a</c> (which keeps the executable bit), and restores the backup if that copy fails.
    /// Only a finished update removes the staged files, the archive and the backup; the script then deletes itself.
    /// Nothing is restarted: iTerminal is a terminal program, and there is no terminal for a script to reopen it in.
    /// Output goes to <paramref name="log"/>, because the script outlives the terminal it was started from.
    /// </summary>
    internal static string ShellUpdateScript(int processId, string source, string installDirectory, string archive, string staging, string backup, string log) =>
        string.Join("\n",
        [
            "#!/bin/sh",
            $"exec >{ShellQuote(log)} 2>&1 </dev/null",
            $"PID={processId}",
            "while kill -0 \"$PID\" 2>/dev/null; do sleep 1; done",
            $"if mkdir -p {ShellQuote(backup)} && cp -a {ShellQuote(installDirectory + "/.")} {ShellQuote(backup + "/")}; then",
            $"  if cp -a {ShellQuote(source + "/.")} {ShellQuote(installDirectory + "/")}; then",
            $"    rm -rf {ShellQuote(staging)}",
            $"    rm -f {ShellQuote(archive)}",
            $"    rm -rf {ShellQuote(backup)}",
            "    echo \"The update is in place.\"",
            "  else",
            "    echo \"Copying the new files failed; restoring the previous ones.\"",
            $"    if cp -a {ShellQuote(backup + "/.")} {ShellQuote(installDirectory + "/")}; then",
            "      echo \"The previous files are back in place.\"",
            "    else",
            $"      echo \"Restoring failed; the backup of the previous files is kept in the update folder.\"",
            "    fi",
            "  fi",
            "else",
            "  echo \"The install folder could not be backed up; nothing was changed.\"",
            "fi",
            "rm -f \"$0\"",
            string.Empty,
        ]);

    /// <summary>Quotes a value for a POSIX shell: single quotes, with any single quote written as <c>'\''</c>.</summary>
    internal static string ShellQuote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>Quotes a path for a batch file. A path cannot hold a double quote on Windows; a percent sign is doubled, so the batch file reads it literally.</summary>
    internal static string BatchPath(string path) => "\"" + path.Replace("%", "%%", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Whether the install folder, and every folder of the new release inside it, can take a file. The copy is
    /// refused up front when it cannot, so the app does not close for an update that then fails.
    /// </summary>
    private static bool CanWriteEverywhere(string root, string installDirectory)
    {
        try
        {
            IEnumerable<string> folders = new[] { root }.Concat(Directory.GetDirectories(root, "*", SearchOption.AllDirectories));
            foreach (string folder in folders)
            {
                string target = Path.Combine(installDirectory, Path.GetRelativePath(root, folder));
                Directory.CreateDirectory(target);
                string probe = Path.Combine(target, $".write-test-{Guid.NewGuid():N}");
                using (File.Create(probe))
                {
                }

                File.Delete(probe);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// The folder the update downloads and stages into. On Windows it is the per-user temp folder. On Linux the temp
    /// folder is shared between users, so the update goes under the person's local data folder instead.
    /// </summary>
    internal static string DefaultDownloadDirectory(bool windows)
    {
        if (windows)
        {
            return Path.Combine(Path.GetTempPath(), "iTerminal-update");
        }

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return local.Length > 0
            ? Path.Combine(local, "iTerminal", "update")
            : Path.Combine(Path.GetTempPath(), $"iTerminal-update-{Environment.UserName}");
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
    /// Unpacks a release archive into <paramref name="destination"/>: a zip (Windows) or a tar.gz (Linux, which keeps
    /// the executable bit). An entry that would land outside it stops the whole unpacking (an archive from GitHub is
    /// digest-checked, but the paths are still not trusted).
    /// </summary>
    private static void ExtractArchive(string archive, string destination)
    {
        Directory.CreateDirectory(destination);
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        if (archive.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            ExtractTarGz(archive, root);
            return;
        }

        using ZipArchive zip = ZipFile.OpenRead(archive);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string target = TargetInside(root, entry.FullName);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private static void ExtractTarGz(string archive, string root)
    {
        using FileStream file = File.OpenRead(archive);
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using TarReader tar = new(gzip);
        while (tar.GetNextEntry() is { } entry)
        {
            string target = TargetInside(root, entry.Name);
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(target);
                    break;

                case TarEntryType.RegularFile or TarEntryType.V7RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    entry.ExtractToFile(target, overwrite: true);
                    break;

                case TarEntryType.SymbolicLink or TarEntryType.HardLink:
                    // A link could point outside the folder, and an iTerminal release has none.
                    throw new InvalidDataException($"{entry.Name} is a link, which a portable iTerminal release does not contain.");
            }
        }
    }

    /// <summary>The full path of an archive entry under <paramref name="root"/>; throws when it would land outside.</summary>
    private static string TargetInside(string root, string entryName)
    {
        return ArchivePaths.Inside(root, entryName)
            ?? throw new InvalidDataException($"{entryName} would be unpacked outside the update folder.");
    }

    /// <summary>
    /// The folder that holds the portable files: the unpacked folder itself, or the one folder inside it. The Linux
    /// archive wraps its files in a folder named after the release; the Windows zip does not.
    /// </summary>
    private static string PortableRoot(string staging)
    {
        if (File.Exists(Path.Combine(staging, PortableMarker)))
        {
            return staging;
        }

        string[] folders = Directory.Exists(staging) ? Directory.GetDirectories(staging) : [];
        return folders.Length == 1 && File.Exists(Path.Combine(folders[0], PortableMarker)) ? folders[0] : staging;
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

    /// <summary>The Linux update script outlives this process too; it redirects its own output, so it keeps no terminal.</summary>
    private static bool StartShellScript(string script)
    {
        ProcessStartInfo start = new("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
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
