using iTerminal.Core.Services;

namespace iTerminal.Core.Services.RuntimeDependencies;

/// <summary>
/// Keeps the list of runtime dependencies: what is installed, what the publisher offers, what is
/// being installed. Settings remember the newest version seen (so the start-up offer needs no network),
/// the version the person skipped, and the version that was announced by a notification.
/// </summary>
public sealed class RuntimeDependencyService : IRuntimeDependencyService
{
    public const string LatestSettingPrefix = "RuntimeDependencies.Latest.";
    public const string SkippedSettingPrefix = "RuntimeDependencies.Skipped.";
    public const string AnnouncedSettingPrefix = "RuntimeDependencies.Announced.";

    internal const int MaxLogLines = 50;

    private readonly IReadOnlyList<IRuntimeDependency> _dependencies;
    private readonly ISettingsService _settings;
    private readonly object _gate = new();
    private readonly Dictionary<string, VersionCheck> _checks = [];
    private readonly List<string> _log = [];
    private IReadOnlyList<RuntimeDependencyStatus> _statuses = [];
    private bool _installing;
    private readonly Task _initialBuild;
    private int _ticket;
    private int _assignedTicket;

    public RuntimeDependencyService(ISettingsService settings, IEnumerable<IRuntimeDependency> dependencies)
    {
        _settings = settings;
        _dependencies = dependencies.ToList();

        foreach (IRuntimeDependency dependency in _dependencies)
        {
            // The newest version seen by an earlier run is what the start-up offer compares against.
            _checks[dependency.Id] = new VersionCheck(settings.Get(LatestSettingPrefix + dependency.Id), null, null);
            dependency.Activate();
        }

        // Finding out what is installed starts programs and walks the PATH, which took seconds on the start-up path
        // and nothing waits for it until the first status is read, which is long after the splash.
        _initialBuild = Task.Run(() =>
        {
            try
            {
                Rebuild();
            }
            catch (Exception)
            {
                // A probe that fails leaves the list empty rather than failing every later read; the next rebuild tries again.
            }
        });
    }

    /// <summary>The statuses, waiting for the first build if it is still running.</summary>
    private IReadOnlyList<RuntimeDependencyStatus> Current
    {
        get
        {
            _initialBuild.Wait();
            return _statuses;
        }
    }

    public IReadOnlyList<RuntimeDependencyStatus> Statuses => Current;

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

    public bool ShouldOfferAtStartup => Current.Any(IsOffered);

    public bool IsInstalled(string id) => Current.Any(status => status.Id == id && status.State != RuntimeDependencyState.Missing);

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        bool stored = false;
        foreach (IRuntimeDependency dependency in _dependencies)
        {
            VersionCheck previous = CheckFor(dependency.Id);
            VersionCheck check;
            try
            {
                IReadOnlyList<string> published = await dependency.ListVersionsAsync(cancellationToken);
                Version expected = Version.Parse(dependency.ExpectedVersion);
                string? latest = Newest(published, version => dependency.IsCompatible(version));
                string? newerLine = Newest(published, version => !dependency.IsCompatible(version) && version > expected);
                check = new VersionCheck(latest ?? previous.Latest, newerLine, null);

                if (latest is not null)
                {
                    _settings.Set(LatestSettingPrefix + dependency.Id, latest);
                    stored = true;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                check = new VersionCheck(previous.Latest, previous.NewerLine, $"Could not check for updates: {exception.Message}");
            }

            lock (_gate)
            {
                _checks[dependency.Id] = check;
            }
        }

        if (stored)
        {
            _settings.Write();
        }

        Rebuild();
    }

    public void StartInstall() => StartInstall(null);

    public void StartInstall(IReadOnlyCollection<string>? ids)
    {
        if (IsBusy)
        {
            return;
        }

        _ = Task.Run(() => InstallAsync(CancellationToken.None, ids));
    }

    public Task InstallAsync(CancellationToken cancellationToken) => InstallAsync(cancellationToken, null);

    public async Task InstallAsync(CancellationToken cancellationToken, IReadOnlyCollection<string>? ids)
    {
        lock (_gate)
        {
            if (_installing)
            {
                return;
            }

            _installing = true;
        }

        try
        {
            foreach (RuntimeDependencyStatus status in Current.Where(status => status.State != RuntimeDependencyState.Installed && Wanted(ids, status.Id)).ToList())
            {
                IRuntimeDependency dependency = _dependencies.First(d => d.Id == status.Id);
                string target = status.TargetVersion ?? dependency.ExpectedVersion;
                AddLog($"{status.Name}: installing {target}");
                try
                {
                    await dependency.InstallAsync(target, new LineProgress(AddLog), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AddLog($"{status.Name}: cancelled");
                    throw;
                }
                catch (Exception exception)
                {
                    AddLog($"{status.Name} could not be installed: {exception.Message}");
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _installing = false;
            }

            Rebuild();
        }
    }

    public void SkipOffers() => SkipOffers(null);

    public void SkipOffers(IReadOnlyCollection<string>? ids)
    {
        foreach (RuntimeDependencyStatus status in Current.Where(status => status.TargetVersion is not null && Wanted(ids, status.Id)))
        {
            _settings.Set(SkippedSettingPrefix + status.Id, status.TargetVersion!);
        }

        _settings.Write();
    }

    public void InvalidateInstalled(string id)
    {
        _dependencies.FirstOrDefault(d => d.Id == id)?.Invalidate();
        Rebuild();
    }

    /// <summary>Whether a dependency is among those asked for; no list at all means every one.</summary>
    private static bool Wanted(IReadOnlyCollection<string>? ids, string id) => ids is null || ids.Contains(id);

    private bool IsOffered(RuntimeDependencyStatus status) =>
        status.State != RuntimeDependencyState.Installed
        && status.TargetVersion is not null
        && _settings.Get(SkippedSettingPrefix + status.Id) != status.TargetVersion;

    private void Rebuild()
    {
        int ticket = Interlocked.Increment(ref _ticket);
        List<RuntimeDependencyStatus> statuses = _dependencies.Select(Describe).ToList();
        lock (_gate)
        {
            // A build that started later has seen later state: an earlier one finishing last must not replace it.
            if (ticket > _assignedTicket)
            {
                _assignedTicket = ticket;
                _statuses = statuses;
            }
        }
    }

    private RuntimeDependencyStatus Describe(IRuntimeDependency dependency)
    {
        VersionCheck check = CheckFor(dependency.Id);
        string? installed = dependency.InstalledVersion();

        if (installed is null)
        {
            // The newest version known on this build's line, which is the build's own version until a check has said otherwise.
            return new RuntimeDependencyStatus(
                dependency.Id,
                dependency.Name,
                dependency.Purpose,
                InstalledVersion: null,
                TargetVersion: check.Latest ?? dependency.ExpectedVersion,
                State: RuntimeDependencyState.Missing,
                Note: dependency.ExistingInstallNote ?? (check.NewerLine is null ? null : NewerLineNote(dependency, check.NewerLine)),
                Problem: check.Problem);
        }

        if (dependency.IsExistingInstall)
        {
            // Somebody else's install: used as it is, so there is nothing of ours to update.
            return new RuntimeDependencyStatus(
                dependency.Id,
                dependency.Name,
                dependency.Purpose,
                installed,
                TargetVersion: null,
                State: RuntimeDependencyState.Installed,
                Note: dependency.ExistingInstallNote,
                Problem: check.Problem);
        }

        bool updateAvailable = check.Latest is not null && IsNewer(check.Latest, installed);
        return new RuntimeDependencyStatus(
            dependency.Id,
            dependency.Name,
            dependency.Purpose,
            installed,
            TargetVersion: updateAvailable ? check.Latest : null,
            State: updateAvailable ? RuntimeDependencyState.UpdateAvailable : RuntimeDependencyState.Installed,
            Note: check.NewerLine is null ? null : NewerLineNote(dependency, check.NewerLine),
            Problem: check.Problem);
    }

    private static string NewerLineNote(IRuntimeDependency dependency, string newerLine) =>
        $"{newerLine} is out, but this build of iTerminal uses {dependency.ExpectedVersion}.";

    private VersionCheck CheckFor(string id)
    {
        lock (_gate)
        {
            return _checks.TryGetValue(id, out VersionCheck? check) ? check : new VersionCheck(null, null, null);
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

    private static string? Newest(IEnumerable<string> versions, Func<Version, bool> accept)
    {
        Version? best = null;
        string? bestText = null;
        foreach (string text in versions)
        {
            if (!Version.TryParse(text, out Version? version) || !accept(version))
            {
                continue;
            }

            if (best is null || version > best)
            {
                best = version;
                bestText = version.ToString(3);
            }
        }

        return bestText;
    }

    private static bool IsNewer(string candidate, string current) =>
        Version.TryParse(candidate, out Version? newer)
        && Version.TryParse(current, out Version? older)
        && newer > older;

    private sealed record VersionCheck(string? Latest, string? NewerLine, string? Problem);

    private sealed class LineProgress(Action<string> onLine) : IProgress<string>
    {
        public void Report(string value) => onLine(value);
    }
}
