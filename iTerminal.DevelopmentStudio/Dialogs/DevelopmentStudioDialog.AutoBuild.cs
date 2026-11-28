using System.Diagnostics;
using System.Globalization;
using iConsole.Localization;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The background build and test: with <see cref="StudioSettings.AutoBuildTestKey"/> on, the studio builds the solutions, then
/// tests them, without the user asking. It starts when the studio opens, when a file is saved and when the folder is refreshed.
/// A forced build, test, run or debug pauses it: its process is stopped, and it starts again from the build once the forced
/// command has finished (and the debugged program, if any, has ended). The header shows what it is doing.
/// </summary>
public partial class DevelopmentStudioDialog
{
    /// <summary>What the background build and test is doing: nothing, building, testing, or paused by a forced command.</summary>
    public enum BackgroundPhase { Idle, Building, Testing, Paused }

    /// <summary>Whether the studio builds and tests in the background. Read from the settings when the studio opens and when they change.</summary>
    private bool _autoBuildTest = true;

    /// <summary>The phase, written by the background run and read by the header. Volatile: the run is on a worker thread.</summary>
    private volatile BackgroundPhase _backgroundPhase = BackgroundPhase.Idle;

    /// <summary>The project or solution the background run is building or testing, for the header.</summary>
    private volatile string _backgroundTarget = string.Empty;

    /// <summary>The background run in progress, cancelled when a forced command pauses it or a newer run replaces it.</summary>
    private CancellationTokenSource? _backgroundCts;

    /// <summary>The last background run, which the next one waits for, so two builds never write to the same folders at once.</summary>
    private Task _backgroundTask = Task.CompletedTask;

    /// <summary>The clock of the background run, shown in the header as it runs.</summary>
    private Stopwatch? _backgroundClock;

    /// <summary>The whole second the header last showed for the background run; the dialog redraws when the second changes.</summary>
    private long _backgroundShownSeconds = -1;

    /// <summary>Set by a trigger (open, save, refresh, settings) and cleared when a run starts, so triggers coalesce into one run.</summary>
    private bool _backgroundRequested;

    /// <summary>Set while a forced command holds the background off. It is cleared once the command and any debugging have ended.</summary>
    private bool _backgroundPaused;

    /// <summary>The phase the header and the tests read: what the background run is doing now.</summary>
    public BackgroundPhase Background => _backgroundPhase;

    /// <summary>Whether the background build and test is on (the setting).</summary>
    public bool AutoBuildTest => _autoBuildTest;

    /// <summary>Reads the setting. Missing or unreadable means on, the default.</summary>
    private void ReadAutoBuildSetting()
    {
        _autoBuildTest = !bool.TryParse(_settings.Get(StudioSettings.AutoBuildTestKey), out bool stored) || stored;
    }

    /// <summary>
    /// Asks for a background build and test. It runs on the next idle pass, when no forced command or debugging holds it off, so a
    /// burst of triggers (a save, then a refresh) starts one run.
    /// </summary>
    private void RequestBackgroundBuild()
    {
        if (_autoBuildTest) _backgroundRequested = true;
    }

    /// <summary>
    /// Pauses the background run for a forced build, test, run or debug: its process is cancelled, and no new run starts until
    /// <see cref="ApplyBackgroundRequests"/> finds the studio free again. Does nothing when the setting is off.
    /// </summary>
    private void PauseBackground()
    {
        if (!_autoBuildTest) return;
        _backgroundPaused = true;
        _backgroundRequested = false;
        _backgroundPhase = BackgroundPhase.Paused;
        _backgroundCts?.Cancel();
    }

    /// <summary>
    /// Runs on the UI thread's idle pass. It resumes a paused background once no command and no debugging hold it off, and starts a
    /// requested run when the studio is free. Commands finish on worker threads, so the dialog polls here rather than being told.
    /// </summary>
    private void ApplyBackgroundRequests()
    {
        if (_backgroundPaused && !_busy && _session is null)
        {
            _backgroundPaused = false;
            _backgroundRequested = true;
        }

        if (!_autoBuildTest || _backgroundPaused || _busy || _session is not null || !_backgroundRequested) return;

        _backgroundRequested = false;
        StartBackgroundBuildAndTest();
    }

    /// <summary>Starts a run of the solutions' build, then their tests. A run in progress is cancelled first, and this one waits for it.</summary>
    private void StartBackgroundBuildAndTest()
    {
        var targets = BackgroundTargets();
        if (targets.Count == 0)
        {
            _backgroundPhase = BackgroundPhase.Idle;
            return;
        }

        _backgroundCts?.Cancel();
        var cts = new CancellationTokenSource();
        _backgroundCts = cts;
        var previous = _backgroundTask;
        _backgroundPhase = BackgroundPhase.Building;
        _backgroundClock = Stopwatch.StartNew();
        _backgroundShownSeconds = -1;
        _backgroundTask = Task.Run(async () =>
        {
            await previous;
            await BackgroundBuildAndTestAsync(targets, cts.Token);
        }, CancellationToken.None);
    }

    /// <summary>
    /// The items the background run builds and tests: the .NET solutions, or the .NET projects when the folder has no solution. Each
    /// solution is built and tested whole, so the tests cover every project it lists.
    /// </summary>
    private IReadOnlyList<DevelopmentProject> BackgroundTargets()
    {
        var dotNet = _items.Where(item => item.Language == DevelopmentLanguage.DotNet).ToList();
        var solutions = dotNet.Where(item => item.Kind == DevelopmentProjectKind.Solution).ToList();
        return solutions.Count > 0 ? solutions : dotNet.Where(item => item.Kind == DevelopmentProjectKind.DotNetProject).ToList();
    }

    /// <summary>
    /// The build of each target, then, when it passed, the tests. A cancelled run leaves the phase to whoever cancelled it: a pause
    /// sets it to paused, and a newer run sets its own.
    /// </summary>
    private async Task BackgroundBuildAndTestAsync(IReadOnlyList<DevelopmentProject> targets, CancellationToken token)
    {
        try
        {
            // The bar counts a build, a test listing and a test run for each target: one step each.
            var testProjects = TestProjectsOf(targets);
            BeginProgress(targets.Count * 2 + testProjects.Count);
            _backgroundPhase = BackgroundPhase.Building;
            int buildFailures = 0;
            foreach (var item in targets)
            {
                token.ThrowIfCancellationRequested();
                _backgroundTarget = item.Name;
                var build = ToolchainFor(item).Build(item);
                if (build is not null)
                {
                    int exitCode = await _runner.RunAsync(build, _ => { }, token);
                    if (exitCode != 0) buildFailures++;
                    lock (_stateLock) _buildOutcomes[item.Path] = exitCode == 0;
                }

                StepProgress(string.Format(CultureInfo.InvariantCulture, UiText.Get("DevelopmentStudio.BackgroundBuilding", "building {0} in the background"), item.Name));
            }

            if (buildFailures > 0)
            {
                SetStatus(string.Format(UiText.Get("DevelopmentStudio.BackgroundBuildFailed", "Background build failed for {0} item(s)."), buildFailures), StatusSeverity.Error);
                return;
            }

            // The tests are listed from the build that has just passed, before they run, so the Test Explorer can show their names.
            _backgroundPhase = BackgroundPhase.Testing;
            foreach (var discovered in testProjects)
            {
                token.ThrowIfCancellationRequested();
                _backgroundTarget = discovered.Name;
                await DiscoverTestsAsync(discovered, token);
                StepProgress(UiText.Get("DevelopmentStudio.BackgroundTesting", "testing in the background"));
            }

            var lines = new List<string>();
            foreach (var item in targets)
            {
                token.ThrowIfCancellationRequested();
                _backgroundTarget = item.Name;
                var test = ToolchainFor(item).Test(item);
                if (test is not null) await _runner.RunAsync(test, line => { lock (lines) lines.Add(line); }, token);
                StepProgress(UiText.Get("DevelopmentStudio.BackgroundTesting", "testing in the background"));
            }

            TestTotals totals;
            lock (lines) totals = TestOutputParser.Parse(lines);
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.BackgroundTests", "Background tests: {0} passed, {1} failed, {2} skipped."), totals.Passed, totals.Failed, totals.Skipped),
                totals.Failed == 0 ? StatusSeverity.Success : StatusSeverity.Error);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Paused or replaced: the one that cancelled the run sets the phase.
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            SetStatus(exception.Message, StatusSeverity.Error);
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _backgroundPhase = BackgroundPhase.Idle;
            }

            EndProgress();
            StateHasChanged();
        }
    }

    /// <summary>
    /// The .NET test projects the background run has built: those of each target solution, or the target project itself. The
    /// Test Explorer lists test projects, so the names are kept per project.
    /// </summary>
    private IReadOnlyList<DevelopmentProject> TestProjectsOf(IReadOnlyList<DevelopmentProject> targets) =>
        _items.Where(item => item.IsTest && item.Kind == DevelopmentProjectKind.DotNetProject
            && targets.Any(target => target.Kind == DevelopmentProjectKind.Solution
                ? item.SolutionPath?.Equals(target.Path, StringComparison.OrdinalIgnoreCase) == true
                : target.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>
    /// Lists the names of one test project's tests and keeps them for the Test Explorer. A failed listing leaves the project with
    /// no names, so a stale list never outlives its build.
    /// </summary>
    private async Task DiscoverTestsAsync(DevelopmentProject item, CancellationToken token)
    {
        var command = ToolchainFor(item).ListTests(item);
        if (command is null) return;

        var lines = new List<string>();
        int exitCode = await _runner.RunAsync(command, line => { lock (lines) lines.Add(line); }, token);

        IReadOnlyList<string> names = [];
        if (exitCode == 0) lock (lines) names = TestDiscoveryParser.Parse(item.Language, lines);
        lock (_stateLock) _discoveredTests[item.Path] = names;
        RebuildExplorer();
    }

    /// <summary>The header's note on the background run, or empty when it is idle (and off): what it builds, tests, or that it is paused.</summary>
    private string BackgroundText()
    {
        return _backgroundPhase switch
        {
            BackgroundPhase.Building => "   ◐ " + string.Format(CultureInfo.InvariantCulture, UiText.Get("DevelopmentStudio.BackgroundBuilding", "building {0} in the background"), _backgroundTarget) + BackgroundElapsedText(),
            BackgroundPhase.Testing => "   ◐ " + UiText.Get("DevelopmentStudio.BackgroundTesting", "testing in the background") + BackgroundElapsedText(),
            BackgroundPhase.Paused => "   ❚❚ " + UiText.Get("DevelopmentStudio.BackgroundPaused", "background build paused"),
            _ => string.Empty,
        };
    }

    private string BackgroundElapsedText() => _backgroundClock is { IsRunning: true } clock ? " " + FormatElapsed(clock.Elapsed) : string.Empty;

    /// <summary>The header's background note moves while the run goes, so the dialog redraws when the second on screen changes.</summary>
    private void RedrawBackgroundClock()
    {
        if (_backgroundPhase is not (BackgroundPhase.Building or BackgroundPhase.Testing) || _backgroundClock is not { IsRunning: true } clock) return;
        long seconds = (long)clock.Elapsed.TotalSeconds;
        if (seconds == _backgroundShownSeconds) return;
        _backgroundShownSeconds = seconds;
        StateHasChanged();
    }

    /// <summary>
    /// Applies a change of the setting: turning it off stops the run and forgets any pause; turning it on starts a run as the studio
    /// does when it opens.
    /// </summary>
    private void ApplyAutoBuildSetting(bool enabled)
    {
        if (enabled == _autoBuildTest) return;
        _autoBuildTest = enabled;
        if (enabled)
        {
            RequestBackgroundBuild();
            return;
        }

        _backgroundRequested = false;
        _backgroundPaused = false;
        _backgroundCts?.Cancel();
        _backgroundPhase = BackgroundPhase.Idle;
    }
}
