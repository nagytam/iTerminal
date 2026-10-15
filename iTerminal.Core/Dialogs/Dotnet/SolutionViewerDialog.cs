using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Dialogs.Dotnet;

/// <summary>
/// Full-screen modal solution viewer for <c>.slnx</c>/<c>.sln</c> files: the parsed project
/// list (plus a whole-solution entry) on the left, live <c>dotnet build/test/run</c> output on
/// the right. The CLI runs on a background task; the key handler and <see cref="Draw"/> never
/// block on it.
/// </summary>
public class SolutionViewerDialog : SplitPaneDialog
{
    /// <summary>One row of the project list. <see cref="Project"/> is null for the whole-solution row.</summary>
    private sealed record ProjectRow(string DisplayName, SolutionProject? Project, bool Selected);

    /// <summary>Aggregate <c>dotnet test</c> result counts, parsed from the VSTest console summary line.</summary>
    private sealed record TestTotals(int Passed, int Failed, int Skipped, int Total, int ProjectsReported)
    {
        public static readonly TestTotals Empty = new(0, 0, 0, 0, 0);
    }

    /// <summary>Drives footer status coloring without relying on brittle substring matching of <see cref="_status"/>.</summary>
    private enum StatusSeverity { Info, Success, Error }

    private sealed class DotnetProjectListBox(IListBoxModel<ProjectRow> listModel, IThemeService themeService)
        : ListBox<ProjectRow>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var row = ListModel.ListItems[index].Item;
            if (row.Project is null) return Color.Cyan;
            if (row.Selected) return Color.Green;
            return null;
        }
    }

    private sealed class DotnetOutputListBox(IListBoxModel<string> listModel, IThemeService themeService)
        : ListBox<string>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var line = ListModel.ListItems[index].Item;
            if (line.StartsWith("$ ", StringComparison.Ordinal)) return Color.Cyan;
            if (line.Contains(": error", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Error", StringComparison.Ordinal)
                || line.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Failed!", StringComparison.Ordinal)) return Color.Red;
            if (line.Contains(": warning", StringComparison.OrdinalIgnoreCase)) return Color.Yellow;
            if (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Passed!", StringComparison.Ordinal)) return Color.Green;
            return null;
        }
    }

    // VSTest console summary line, e.g. "Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 1 s - Foo.dll"
    private static readonly Regex TestSummaryLineRegex = new(
        @"^(?:Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)",
        RegexOptions.Compiled);

    private readonly IDotnetSolutionService _dotnetSolutionService;

    private readonly ConcurrentObservableCollection<ProjectRow> _projectItems = new();
    private readonly ConcurrentObservableCollection<string> _outputItems = new();
    private readonly object _outputLock = new();
    private readonly List<string> _output = [];

    private readonly DotnetProjectListBox _projectListBox;
    private readonly DotnetOutputListBox _outputListBox;

    /// <summary>Checked projects. Empty means "use the cursor row (or the whole solution)" for build/test.</summary>
    private readonly HashSet<SolutionProject> _selectedProjects = [];

    private SolutionInfo _solution = SolutionInfo.Empty;
    private string _loadError = string.Empty;

    private bool _followTail = true;
    private bool _syncingScroll;

    private bool _busy;
    private string _status = string.Empty;
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private CancellationTokenSource? _runCts;

    /// <summary>Verb of the run in progress ("build"/"test"/"run"); drives test-summary parsing in <see cref="AppendOutput"/>.</summary>
    private string _currentVerb = string.Empty;
    private TestTotals _testTotals = TestTotals.Empty;

    /// <summary>Absolute path to the solution file. Set by the caller before the dialog is shown.</summary>
    public string SolutionPath { get; set; } = string.Empty;

    public SolutionViewerDialog(
        IDotnetSolutionService dotnetSolutionService,
        IThemeService themeService)
        : base(nameof(SolutionViewerDialog), themeService)
    {
        _dotnetSolutionService = dotnetSolutionService;
        Title = "Solution";

        _projectListBox = new DotnetProjectListBox(
            new ListBoxModel<ProjectRow>(_projectItems) { TransformItem = TransformProjectRow },
            themeService);
        _outputListBox = new DotnetOutputListBox(
            new ListBoxModel<string>(_outputItems) { TransformItem = TransformOutputLine },
            themeService)
        {
            CanFocus = false,
        };

        AddControl(_projectListBox);
        AddControl(_outputListBox);

        _outputListBox.ScrollableModel.PropertyChanged += (_, e) =>
        {
            if (!_syncingScroll && e.PropertyName == nameof(IScrollableModel.CursorPosition))
            {
                _followTail = false;
            }
        };
    }

    private static ListItem<ProjectRow> TransformProjectRow(ProjectRow row, CancellationToken cancellationToken) =>
        new(row) { Value = row.DisplayName, IsEnabled = true };

    private static ListItem<string> TransformOutputLine(string line, CancellationToken cancellationToken) =>
        new(line) { Value = line, IsEnabled = true };

    #region Selection

    /// <summary>Rebuilds the project list rows from <see cref="_solution"/> and <see cref="_selectedProjects"/>. Rows are
    /// plain data (no mutable checkbox state), so any selection change is applied by replacing the whole list.</summary>
    private void RebuildProjectRows()
    {
        var rows = new List<ProjectRow> { new($" [Solution] {_solution.Name}", null, false) };
        rows.AddRange(_solution.Projects.Select(project =>
        {
            var selected = _selectedProjects.Contains(project);
            return new ProjectRow(FormatProjectRowName(project, selected), project, selected);
        }));
        _projectItems.ReplaceAll(rows);
        _projectListBox.UpdateModel();
    }

    private static string FormatProjectRowName(SolutionProject project, bool selected)
    {
        var mark = selected ? 'x' : ' ';
        return $"   [{mark}] {project.Name}{(project.IsTestProject ? "  [test]" : string.Empty)}";
    }

    private void ToggleSelectionAtCursor()
    {
        var project = SelectedProject;
        if (project is null)
        {
            return;
        }

        if (!_selectedProjects.Remove(project))
        {
            _selectedProjects.Add(project);
        }

        RebuildProjectRows();
        StateHasChanged();
    }

    /// <summary>Selects every project, or clears the selection when everything is already selected.</summary>
    private void SelectAllProjects()
    {
        if (_solution.Projects.Count == 0)
        {
            return;
        }

        if (_selectedProjects.Count == _solution.Projects.Count)
        {
            _selectedProjects.Clear();
        }
        else
        {
            foreach (var project in _solution.Projects)
            {
                _selectedProjects.Add(project);
            }
        }

        RebuildProjectRows();
        StateHasChanged();
    }

    /// <summary>Selects every test project (<see cref="SolutionProject.IsTestProject"/>), or clears them when all are already selected.</summary>
    private void SelectAllTestProjects()
    {
        var testProjects = _solution.Projects.Where(project => project.IsTestProject).ToList();
        if (testProjects.Count == 0)
        {
            // Said rather than ignored, or T reads as a key that does nothing.
            _status = "No test projects in this solution.";
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        if (testProjects.All(_selectedProjects.Contains))
        {
            foreach (var project in testProjects)
            {
                _selectedProjects.Remove(project);
            }
        }
        else
        {
            foreach (var project in testProjects)
            {
                _selectedProjects.Add(project);
            }
        }

        RebuildProjectRows();
        StateHasChanged();
    }

    #endregion

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _output.Clear();
        _outputItems.ReplaceAll([]);
        _followTail = true;
        _busy = false;
        _status = string.Empty;
        _statusSeverity = StatusSeverity.Info;
        _runCts = null;
        _selectedProjects.Clear();

        try
        {
            _solution = _dotnetSolutionService.ParseSolution(SolutionPath);
            _loadError = _solution.Projects.Count == 0
                ? "No projects found in solution."
                : string.Empty;
            Title = string.IsNullOrEmpty(_solution.Name) ? "Solution" : $"Solution: {_solution.Name}";
        }
        catch (Exception exception)
        {
            _solution = SolutionInfo.Empty;
            _loadError = $"Failed to read solution: {exception.Message}";
        }

        RebuildProjectRows();

        // The load error goes on the status line. It used to be written over the first row of the
        // project list — which is the [Solution] row, so the list repainted over it the next time it
        // rendered on its own and the error vanished.
        if (!string.IsNullOrEmpty(_loadError))
        {
            _status = _loadError;
            _statusSeverity = StatusSeverity.Error;
        }

        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.95);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            var midY = 2; // interior offset (1) + header row (1)
            var midH = ih - 2; // minus header row and footer row
            if (midH > 0)
            {
                var leftW = Math.Max(24, iw * 38 / 100);
                var rightX = 1 + leftW + 1;
                var rightW = iw - leftW - 1;

                _projectListBox.Position = new Position(1, midY + 1); // +1 reserves the section-header band
                _projectListBox.ViewSize = new Size(leftW, Math.Max(0, midH - 1));

                _outputListBox.Position = new Position(rightX, midY + 1);
                _outputListBox.ViewSize = rightW > 0 ? new Size(rightW, Math.Max(0, midH - 1)) : new Size(0, 0);
            }
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them (each control's own OnInitializeLayout sizes its ScrollableModel from
        // its own ViewSize) and focuses the first focusable child.
        base.OnInitializeLayout();

        ResyncTailScroll();
    }

    #region Running dotnet (background)

    /// <summary>The selected project, or null when the whole-solution row is selected.</summary>
    private SolutionProject? SelectedProject
    {
        get
        {
            var items = _projectListBox.ListModel.ListItems;
            var index = (int)_projectListBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < items.Count ? items[index].Item.Project : null;
        }
    }

    private void RunBuild() => RunDotnet("build", ResolveTargets());

    private void RunTest() => RunDotnet("test", ResolveTargets());

    private void RunProject()
    {
        var project = SelectedProject;
        if (project is null)
        {
            _status = "Select a project to run (the whole solution cannot be run).";
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        RunDotnet("run", [project]);
    }

    /// <summary>The checked projects, or the cursor row (project, or the whole solution when the solution row is
    /// selected) when nothing is checked.</summary>
    private IReadOnlyList<SolutionProject?> ResolveTargets()
    {
        if (_selectedProjects.Count > 0)
        {
            return [.. _solution.Projects.Where(_selectedProjects.Contains).Cast<SolutionProject?>()];
        }

        return [SelectedProject];
    }

    private static IReadOnlyList<string> BuildArguments(string verb, string targetPath) =>
        verb == "run" ? ["run", "--project", targetPath] : [verb, targetPath];

    /// <summary>Runs <c>dotnet &lt;verb&gt;</c> once per target, sequentially, aggregating output and (for "test") the
    /// pass/fail/skip summary across every target into one run.</summary>
    private void RunDotnet(string verb, IReadOnlyList<SolutionProject?> targets)
    {
        if (_busy)
        {
            // Only one dotnet command at a time; say so rather than swallowing the key.
            _status = $"dotnet {_currentVerb} is still running — x cancels it.";
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        if (string.IsNullOrEmpty(_solution.Directory) || targets.Count == 0)
        {
            return;
        }

        var workingDirectory = _solution.Directory;
        var targetLabel = targets.Count == 1
            ? (targets[0]?.Name ?? _solution.Name)
            : $"{targets.Count} projects";

        var cts = new CancellationTokenSource();
        _runCts = cts;
        _busy = true;
        _currentVerb = verb;
        _testTotals = TestTotals.Empty;
        _status = $"dotnet {verb} {targetLabel}...";
        _statusSeverity = StatusSeverity.Info;
        lock (_outputLock)
        {
            _output.Clear();
        }
        _followTail = true;
        SyncOutputItems();
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            var overallExitCode = 0;
            foreach (var target in targets)
            {
                if (cts.IsCancellationRequested)
                {
                    break;
                }

                var targetName = target?.Name ?? _solution.Name;
                var targetPath = target?.AbsolutePath ?? _solution.Path;
                var arguments = BuildArguments(verb, targetPath);

                if (targets.Count > 1)
                {
                    _status = $"dotnet {verb} {targetName}...";
                    StateHasChanged();
                }
                AppendOutput($"$ dotnet {string.Join(' ', arguments)}");

                int exitCode;
                try
                {
                    exitCode = await _dotnetSolutionService.RunAsync(
                        workingDirectory, arguments, AppendOutput, cts.Token);
                }
                catch (Exception exception)
                {
                    AppendOutput($"Error: {exception.Message}");
                    exitCode = -1;
                }

                if (exitCode != 0)
                {
                    overallExitCode = exitCode;
                }
                AppendOutput(string.Empty);
            }

            _busy = false;
            ReportRunOutcome(verb, targets.Count, overallExitCode, cts.IsCancellationRequested);
            StateHasChanged();
        });
    }

    /// <summary>Sets the final <see cref="_status"/>/<see cref="_statusSeverity"/> for a finished run, appending a
    /// pass/fail/skip summary line to the output when the run was <c>dotnet test</c> and produced one.</summary>
    private void ReportRunOutcome(string verb, int targetCount, int exitCode, bool wasCancelled)
    {
        if (wasCancelled)
        {
            _status = $"dotnet {verb} cancelled.";
            _statusSeverity = StatusSeverity.Info;
            return;
        }

        if (verb == "test" && _testTotals.ProjectsReported > 0)
        {
            var totals = _testTotals;
            var summary = $"Tests: {totals.Passed} passed, {totals.Failed} failed, {totals.Skipped} skipped " +
                $"({totals.Total} total, {totals.ProjectsReported} project{(totals.ProjectsReported == 1 ? string.Empty : "s")})";
            AppendOutput(summary);
            _status = summary;
            _statusSeverity = totals.Failed > 0 || exitCode != 0 ? StatusSeverity.Error : StatusSeverity.Success;
            return;
        }

        var targetLabel = targetCount == 1 ? string.Empty : $" ({targetCount} projects)";
        if (exitCode == 0)
        {
            _status = $"dotnet {verb} succeeded{targetLabel}.";
            _statusSeverity = StatusSeverity.Success;
        }
        else
        {
            _status = $"dotnet {verb} failed (exit {exitCode}){targetLabel}.";
            _statusSeverity = StatusSeverity.Error;
        }
    }

    private void AppendOutput(string line)
    {
        lock (_outputLock)
        {
            _output.Add(line);
        }

        if (_currentVerb == "test")
        {
            var match = TestSummaryLineRegex.Match(line);
            if (match.Success)
            {
                _testTotals = new TestTotals(
                    _testTotals.Passed + int.Parse(match.Groups[2].Value),
                    _testTotals.Failed + int.Parse(match.Groups[1].Value),
                    _testTotals.Skipped + int.Parse(match.Groups[3].Value),
                    _testTotals.Total + int.Parse(match.Groups[4].Value),
                    _testTotals.ProjectsReported + 1);
            }
        }

        SyncOutputItems();
        StateHasChanged();
    }

    /// <summary>Pushes <see cref="_output"/> into the output list box's model and keeps the tail visible when following.</summary>
    private void SyncOutputItems()
    {
        string[] lines;
        lock (_outputLock)
        {
            lines = [.. _output];
        }

        _outputItems.ReplaceAll(lines);
        _outputListBox.UpdateModel();
        ResyncTailScroll();
    }

    /// <summary>When following the tail, keeps the output list box scrolled to the last line.</summary>
    private void ResyncTailScroll()
    {
        if (!_followTail)
        {
            return;
        }

        _syncingScroll = true;
        try
        {
            var count = _outputListBox.ListModel.ListItems.Count;
            var rows = (int)_outputListBox.ViewSize.Height;
            _outputListBox.ScrollableModel.ScrollPosition.Y = Math.Max(0, count - rows);
            _outputListBox.ScrollableModel.CursorPosition.Y = Math.Max(0, count - 1);
        }
        finally
        {
            _syncingScroll = false;
        }
    }

    private void Cancel()
    {
        if (_busy && _runCts is not null)
        {
            _status = "Cancelling...";
            _statusSeverity = StatusSeverity.Info;
            _runCts.Cancel();
            StateHasChanged();
        }
    }

    #endregion

    #region Input

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _runCts?.Cancel();
                Close();
                return true;
            case ConsoleKey.Spacebar:
                ToggleSelectionAtCursor();
                return true;
            case ConsoleKey.PageUp:
                ScrollOutput(-10);
                return true;
            case ConsoleKey.PageDown:
                ScrollOutput(10);
                return true;
            case ConsoleKey.Home:
                _followTail = false;
                _syncingScroll = true;
                try
                {
                    _outputListBox.ScrollableModel.ScrollPosition.Y = 0;
                    _outputListBox.ScrollableModel.CursorPosition.Y = 0;
                }
                finally
                {
                    _syncingScroll = false;
                }
                StateHasChanged();
                return true;
            case ConsoleKey.End:
                _followTail = true;
                ResyncTailScroll();
                StateHasChanged();
                return true;
        }

        // Case-sensitive: 'T' selects all test projects, distinct from lower-case 't' which runs tests.
        switch (keyEvent.KeyChar)
        {
            case 'T':
                SelectAllTestProjects();
                return true;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'a':
                SelectAllProjects();
                return true;
            case 'b':
                RunBuild();
                return true;
            case 't':
                RunTest();
                return true;
            case 'r':
                RunProject();
                return true;
            case 'x':
                Cancel();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void ScrollOutput(int delta)
    {
        _followTail = false;
        var rows = (int)_outputListBox.ViewSize.Height;
        var count = _outputListBox.ListModel.ListItems.Count;
        _syncingScroll = true;
        try
        {
            var newScroll = Math.Clamp((int)_outputListBox.ScrollableModel.ScrollPosition.Y + delta, 0, Math.Max(0, count - rows));
            var newCursor = Math.Clamp((int)_outputListBox.ScrollableModel.CursorPosition.Y + delta, 0, Math.Max(0, count - 1));
            _outputListBox.ScrollableModel.ScrollPosition.Y = newScroll;
            _outputListBox.ScrollableModel.CursorPosition.Y = newCursor;
        }
        finally
        {
            _syncingScroll = false;
        }
        StateHasChanged();
    }

    #endregion

    #region Test seams

    internal string StatusForTest => _status;
    internal int OutputScrollForTest => (int)_outputListBox.ScrollableModel.ScrollPosition.Y;
    internal int OutputRowsForTest => (int)_outputListBox.ViewSize.Height;
    internal bool IsBusyForTest => _busy;
    internal IReadOnlyList<string> OutputForTest
    {
        get
        {
            lock (_outputLock)
            {
                return [.. _output];
            }
        }
    }

    #endregion

    #region Draw

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        var ix = (int)AbsolutePosition.X + 1;
        var iy = (int)AbsolutePosition.Y + 1;
        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        DrawHeader(buffer, ix, iy, iw, colorScheme);
        DrawFooter(buffer, ix, iy + ih - 1, iw, colorScheme);

        var midY = iy + 1;
        var midH = ih - 2;
        if (midH <= 0)
        {
            return;
        }

        var leftW = Math.Max(24, iw * 38 / 100);
        var rightX = ix + leftW + 1;
        var rightW = iw - leftW - 1;

        DrawSectionHeader(buffer, ix, midY, leftW, "Projects", true, colorScheme);

        if (rightW > 0)
        {
            DrawSectionHeader(buffer, rightX, midY, rightW, "Output", false, colorScheme);
        }
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var busy = _busy ? "  *" : string.Empty;
        var selected = _selectedProjects.Count > 0 ? $", {_selectedProjects.Count} selected" : string.Empty;
        var text = string.IsNullOrEmpty(_solution.Path)
            ? SolutionPath
            : $"{_solution.Path}   ({_solution.Projects.Count} projects{selected}){busy}";
        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    internal const string KeyHints =
        "b:build  t:test  r:run  x:cancel  Space:check  a:all  T:tests  PgUp/PgDn/Home/End:output  Esc:close";

    /// <summary>
    /// Status (coloured by severity) followed by the key hints. The hints used to be shown only
    /// while there was no status — and every run leaves one — so after the first build the dialog
    /// never told the user its keys again.
    /// </summary>
    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        DrawStatusAndHints(buffer, x, y, w, _status, _statusSeverity switch
        {
            StatusSeverity.Error => colorScheme.Error,
            StatusSeverity.Success => Color.Green,
            _ => colorScheme.Foreground,
        }, KeyHints, colorScheme);
    }

    #endregion
}
