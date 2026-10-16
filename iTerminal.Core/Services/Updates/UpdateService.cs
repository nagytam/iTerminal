using System.Diagnostics;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services.RuntimeDependencies;

namespace iTerminal.Core.Services.Updates;

/// <summary>
/// Compares the running version with the stable releases of the iTerminal repository. The installer is the
/// <c>.msi</c> attached to the release; it is checked against the digest GitHub records for it before it is run.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    public const string Repository = "nagytam/iTerminal";
    public const string SkippedSetting = "Updates.Skipped";

    internal const int MaxLogLines = 20;

    private readonly ISettingsService _settings;
    private readonly GitHubReleaseFeed _feed;
    private readonly Version? _current;
    private readonly Func<string, bool> _startInstaller;
    private readonly string _downloadDirectory;
    private readonly bool _canInstall;
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private UpdateInfo? _available;
    private bool _installing;

    public UpdateService(ISettingsService settings)
        : this(
            settings,
            new GitHubReleaseFeed(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }),
            AboutDialog.Version,
            StartWindowsInstaller,
            Path.Combine(Path.GetTempPath(), "iTerminal-update"),
            OperatingSystem.IsWindows())
    {
    }

    internal UpdateService(
        ISettingsService settings,
        GitHubReleaseFeed feed,
        string currentVersion,
        Func<string, bool> startInstaller,
        string downloadDirectory,
        bool canInstall)
    {
        _settings = settings;
        _feed = feed;
        CurrentVersion = currentVersion;
        _current = ParseVersion(currentVersion);
        _startInstaller = startInstaller;
        _downloadDirectory = downloadDirectory;
        _canInstall = canInstall;
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

    public bool CanInstall => _canInstall;

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
            if (_canInstall)
            {
                GitHubRelease release = await _feed.GetReleaseAsync(Repository, text, cancellationToken);
                installer = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase));
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
        if (update is null || !_canInstall)
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
                AddLog($"iTerminal {update.Version} has no Windows installer. Download it from {ReleasesAddress}");
                return false;
            }

            Directory.CreateDirectory(_downloadDirectory);
            // The name comes from GitHub: only its file name is used, never a path.
            string package = Path.Combine(_downloadDirectory, Path.GetFileName(asset.Name));
            await _feed.DownloadAsync(asset, package, new LineProgress(AddLog), cancellationToken);

            AddLog("Starting the installer. iTerminal closes so its files can be replaced.");
            if (!_startInstaller(package))
            {
                AddLog("The installer was not started.");
                return false;
            }

            return true;
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
