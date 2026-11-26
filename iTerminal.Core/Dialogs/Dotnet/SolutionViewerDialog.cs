using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Editors;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.Core.Services.Dotnet;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace iTerminal.Core.Dialogs.Dotnet;

/// <summary>
/// Full-screen modal solution viewer for <c>.slnx</c>/<c>.sln</c> files. The left column is the
/// parsed project list (plus a whole-solution entry). The right column holds three panes: the live
/// <c>dotnet build/test/run</c> output, the errors of that output (opened at their line in the
/// editor with Enter), and the code editor, which opens a project file or an error's file and saves
/// it with Ctrl+S. Tab moves between the panes, and the footer's hints name the keys the focused pane
/// answers to. The CLI runs on a background task; the key handler and <see cref="Draw"/> never block on it.
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

    /// <summary>The pane that has the keys. Tab cycles through them; the editor is in the cycle only while it is open.</summary>
    private enum Pane { Projects, Output, Errors, Editor }

    /// <summary>Where an error line points: the file and its line (1-based). Null when the line names no file.</summary>
    private sealed record ErrorLocation(string Path, int Line);

    /// <summary>A file read in the background, waiting for the next idle pass to be shown in the editor.</summary>
    private sealed record LoadedFile(string Path, int Line, string[] Lines, string LineEnding, Encoding Encoding, string? Error);

    /// <summary>The outcome of a background save, applied on the next idle pass.</summary>
    private sealed record SaveOutcome(string Path, string Text, string? Error);

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

    // VSTest console summary line, e.g. "Passed!  - Failed: 0, Passed: 42, Skipped: 0, Total: 42, Duration: 1 s - Foo.dll"
    private static readonly Regex TestSummaryLineRegex = new(
        @"^(?:Passed|Failed)!\s*-\s*Failed:\s*(\d+),\s*Passed:\s*(\d+),\s*Skipped:\s*(\d+),\s*Total:\s*(\d+)",
        RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // MSBuild diagnostic, e.g. "C:\src\App\Program.cs(12,5): error CS1002: ; expected [C:\src\App\App.csproj]".
    private static readonly Regex ErrorLocationRegex = new(
        @"^(?<path>.+?)\((?<line>\d+)(?:,\d+)?\):\s*error\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private readonly IDotnetSolutionService _dotnetSolutionService;
    private IReadOnlyList<DotnetSdk> _sdks = [];
    private int _sdkIndex = -1;

    private readonly ConcurrentObservableCollection<ProjectRow> _projectItems = new();

    private readonly SolutionOutputPane _output;
    private readonly SolutionOutputPane _errors;

    /// <summary>One entry per line of the Error pane, in the same order. Guarded by <see cref="_outputLock"/>.</summary>
    private readonly List<ErrorLocation?> _errorLocations = [];
    private readonly Lock _outputLock = new();

    private readonly DotnetProjectListBox _projectListBox;

    /// <summary>Checked projects. Empty means "use the cursor row (or the whole solution)" for build/test.</summary>
    private readonly HashSet<SolutionProject> _selectedProjects = [];

    private SolutionInfo _solution = SolutionInfo.Empty;
    private string _loadError = string.Empty;

    private volatile bool _busy;
    private string _status = string.Empty;
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private CancellationTokenSource? _runCts;

    /// <summary>The serialisation <c>s</c> writes the SBOM in. XML by default; <c>f</c> switches it to JSON.</summary>
    private SbomFormat _sbomFormat = SbomFormat.Xml;

    /// <summary>Verb of the run in progress ("build"/"test"/"run"); drives test-summary parsing in <see cref="AppendOutput"/>.</summary>
    private string _currentVerb = string.Empty;
    private TestTotals _testTotals = TestTotals.Empty;

    private Pane _pane = Pane.Projects;

    private readonly TextEditorControl _editor;
    private bool _editorOpen;
    private string _editorPath = string.Empty;
    private string _editorLineEnding = "\n";
    private Encoding _editorEncoding = new UTF8Encoding(false);

    /// <summary>Set by an Esc on an editor with unsaved text: the next Esc discards them.</summary>
    private bool _escArmed;

    private volatile LoadedFile? _loadedFile;
    private volatile SaveOutcome? _saveOutcome;

    /// <summary>Absolute path to the solution file. Set by the caller before the dialog is shown.</summary>
    public string SolutionPath { get; set; } = string.Empty;

    public SolutionViewerDialog(
        IDotnetSolutionService dotnetSolutionService,
        IThemeService themeService)
        : base(nameof(SolutionViewerDialog), themeService)
    {
        _dotnetSolutionService = dotnetSolutionService;
        Title = iConsole.Localization.UiText.Get("Dialog.Solution", "Solution");

        _projectListBox = new DotnetProjectListBox(
            new ListBoxModel<ProjectRow>(_projectItems) { TransformItem = TransformProjectRow },
            themeService);

        _output = new SolutionOutputPane(themeService);
        _errors = new SolutionOutputPane(themeService);
        _editor = new TextEditorControl(themeService);

        AddControl(_projectListBox);
        AddControl(_output.ListBox);
        AddControl(_errors.ListBox);
    }

    private static ListItem<ProjectRow> TransformProjectRow(ProjectRow row, CancellationToken cancellationToken) =>
        new(row) { Value = row.DisplayName, IsEnabled = true };

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
            _status = iConsole.Localization.UiText.Get("Solution.NoTests", "No test projects in this solution.");
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

        CloseEditorWithoutAsking();
        _pane = Pane.Projects;
        _escArmed = false;
        _loadedFile = null;
        _saveOutcome = null;

        _output.Clear();
        ClearErrors();
        _busy = false;
        _status = string.Empty;
        _statusSeverity = StatusSeverity.Info;
        _runCts = null;
        _selectedProjects.Clear();

        // The SDKs are listed off the UI thread (dotnet --list-sdks takes a moment), and the newest stable one is selected.
        _sdks = await Task.Run(() => _dotnetSolutionService.ListSdks().ToList(), CancellationToken.None);
        DotnetSdk? defaultSdk = DotnetSdk.Default(_sdks);
        _sdkIndex = defaultSdk is null ? -1 : _sdks.ToList().IndexOf(defaultSdk);

        try
        {
            _solution = _dotnetSolutionService.ParseSolution(SolutionPath);
            _loadError = _solution.Projects.Count == 0
                ? UiText.Get("Solution.NoProjects", "No projects found in solution.")
                : string.Empty;
            Title = string.IsNullOrEmpty(_solution.Name) ? "Solution" : $"Solution: {_solution.Name}";
        }
        catch (Exception exception)
        {
            _solution = SolutionInfo.Empty;
            _loadError = string.Format(iConsole.Localization.UiText.Get("Solution.ReadFailed", "Failed to read solution: {0}"), exception.Message);
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
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            var midY = 2; // interior offset (1) + header row (1)
            var columns = ComputeColumns(iw, ih);
            if (columns.MidHeight > 0)
            {
                _projectListBox.Position = new Position(1, midY + 1); // +1 reserves the section-header band
                _projectListBox.ViewSize = new Size(columns.LeftWidth, Math.Max(0, columns.MidHeight - 1));

                var rightX = columns.RightX;
                var rightW = columns.RightWidth;
                _output.ListBox.Position = new Position(rightX, midY + 1);
                _output.ListBox.ViewSize = rightW > 0 && columns.OutputRows > 1 ? new Size(rightW, columns.OutputRows - 1) : new Size(0, 0);

                _errors.ListBox.Position = new Position(rightX, midY + columns.OutputRows + 1);
                _errors.ListBox.ViewSize = rightW > 0 && columns.ErrorRows > 1 ? new Size(rightW, columns.ErrorRows - 1) : new Size(0, 0);

                _editor.Position = new Position(rightX, midY + 1);
                _editor.ViewSize = rightW > 0 && columns.MidHeight > 1 ? new Size(rightW, columns.MidHeight - 1) : new Size(0, 0);
            }
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them (each control's own OnInitializeLayout sizes its ScrollableModel from
        // its own ViewSize) and focuses the first focusable child.
        base.OnInitializeLayout();

        _output.ResyncTailScroll();
        _errors.ResyncTailScroll();
    }

    /// <summary>The widths and heights of the three columns' parts, from the interior size (the frame excluded).</summary>
    private readonly record struct Columns(int LeftWidth, int RightX, int RightWidth, int MidHeight, int OutputRows, int ErrorRows);

    /// <summary>The left column takes 38% (at least 24 columns); the right column is split into Output (three
    /// fifths of the height) and Errors (the rest). On a very short dialog the Errors pane gets no rows.</summary>
    private static Columns ComputeColumns(int interiorWidth, int interiorHeight)
    {
        var midHeight = interiorHeight - 2;
        var leftWidth = Math.Max(24, interiorWidth * 38 / 100);
        var rightX = 1 + leftWidth + 1;
        var rightWidth = interiorWidth - leftWidth - 1;
        var outputRows = midHeight >= 4 ? midHeight * 3 / 5 : Math.Max(0, midHeight);
        var errorRows = Math.Max(0, midHeight - outputRows);
        return new Columns(leftWidth, rightX, rightWidth, Math.Max(0, midHeight), outputRows, errorRows);
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

    private void RunClean() => RunDotnet("clean", ResolveTargets());

    private void RunTest() => RunDotnet("test", ResolveTargets());

    private void RunProject()
    {
        var project = SelectedProject;
        if (project is null)
        {
            _status = iConsole.Localization.UiText.Get("Solution.SelectToRun", "Select a project to run (the whole solution cannot be run).");
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
        var dotnetHost = DotnetHost;
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
        _output.Clear();
        ClearErrors();
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
                AppendOutput($"$ dotnet {string.Join(' ', arguments)}" + (SelectedSdk is { } sdk ? $"   (.NET SDK {sdk.Version})" : string.Empty));

                int exitCode;
                try
                {
                    exitCode = await _dotnetSolutionService.RunAsync(
                        workingDirectory, dotnetHost, arguments, AppendOutput, cts.Token);
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

            // Busy ends last: whoever sees the run as over (a test, the next key) must find its outcome
            // already in the status line, not the text of the run before.
            ReportRunOutcome(verb, targets.Count, overallExitCode, cts.IsCancellationRequested);
            _busy = false;
            StateHasChanged();
        }, CancellationToken.None);
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

    /// <summary>Called from the run's background task. Adds a line to the Output pane, and to the Error pane when it is an error.</summary>
    private void AppendOutput(string line)
    {
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

        _output.Append(line);
        if (IsErrorLine(line))
        {
            lock (_outputLock)
            {
                _errorLocations.Add(ParseErrorLocation(line));
            }

            _errors.Append(line);
        }

        StateHasChanged();
    }

    private static bool IsErrorLine(string line) =>
        ErrorLocationRegex.IsMatch(line)
        || line.Contains(": error", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("Error", StringComparison.Ordinal);

    /// <summary>The file and line an MSBuild error names, resolved against the solution folder. Null when it names none.</summary>
    private ErrorLocation? ParseErrorLocation(string line)
    {
        var match = ErrorLocationRegex.Match(line);
        if (!match.Success || !int.TryParse(match.Groups["line"].Value, out var lineNumber) || lineNumber < 1)
        {
            return null;
        }

        var path = match.Groups["path"].Value.Trim();
        if (path.Length == 0)
        {
            return null;
        }

        var fullPath = Path.IsPathRooted(path) || string.IsNullOrEmpty(_solution.Directory)
            ? path
            : Path.Combine(_solution.Directory, path);
        return new ErrorLocation(fullPath, lineNumber);
    }

    private void ClearErrors()
    {
        _errors.Clear();
        lock (_outputLock)
        {
            _errorLocations.Clear();
        }
    }

    private void Cancel()
    {
        if (_busy && _runCts is not null)
        {
            _status = iConsole.Localization.UiText.Get("Msg.Cancelling", "Cancelling...");
            _statusSeverity = StatusSeverity.Info;
            _runCts.Cancel();
            StateHasChanged();
        }
    }

    #endregion

    #region Editor

    /// <summary>Enter on a project row: opens the project's file in the editor.</summary>
    private void OpenProjectFile()
    {
        var project = SelectedProject;
        if (project is null)
        {
            _status = iConsole.Localization.UiText.Get("Solution.NoProjectFile", "Select a project to open its project file.");
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        OpenInEditor(project.AbsolutePath, 1);
    }

    /// <summary>Enter on an error line: opens its file at the line it names.</summary>
    private void OpenErrorLine()
    {
        var index = (int)_errors.ListBox.ScrollableModel.CursorPosition.Y;
        ErrorLocation? location;
        lock (_outputLock)
        {
            location = index >= 0 && index < _errorLocations.Count ? _errorLocations[index] : null;
        }

        if (location is null)
        {
            _status = iConsole.Localization.UiText.Get("Solution.NoErrorLocation", "This error names no file, so there is nothing to open.");
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        OpenInEditor(location.Path, location.Line);
    }

    /// <summary>Reads the file on a background task; the editor shows it on the next idle pass.</summary>
    private void OpenInEditor(string path, int line)
    {
        if (_editorOpen && _editor.IsDirty)
        {
            // Opening another file would throw away the unsaved text of this one.
            _status = iConsole.Localization.UiText.Get("Solution.UnsavedEscape", "Unsaved changes: Ctrl+S saves them, Esc again discards them.");
            _statusSeverity = StatusSeverity.Error;
            StateHasChanged();
            return;
        }

        _ = Task.Run(() => _loadedFile = ReadFile(path, line), CancellationToken.None);
    }

    /// <summary>Reads a file as UTF-8, keeping a byte order mark only when the file had one, so a save writes it back unchanged.</summary>
    private static LoadedFile ReadFile(string path, int line)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var encoding = hasBom ? Encoding.UTF8 : new UTF8Encoding(false);
            var skip = hasBom ? 3 : 0;
            var content = encoding.GetString(bytes, skip, bytes.Length - skip);
            var lineEnding = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var lines = content.ReplaceLineEndings("\n").Split('\n');
            return new LoadedFile(path, line, lines, lineEnding, encoding, null);
        }
        catch (Exception exception)
        {
            return new LoadedFile(path, line, [], "\n", new UTF8Encoding(false), exception.Message);
        }
    }

    /// <summary>Shows a file that the background read has finished with, and moves the cursor to its line.</summary>
    private void ShowLoadedFile(LoadedFile loaded)
    {
        if (loaded.Error is not null)
        {
            _status = string.Format(iConsole.Localization.UiText.Get("Solution.OpenFailed", "Could not open {0}: {1}"), Path.GetFileName(loaded.Path), loaded.Error);
            _statusSeverity = StatusSeverity.Error;
            StateHasChanged();
            return;
        }

        if (_editorOpen)
        {
            RemoveControl(_editor);
        }

        _editorOpen = true;
        _editorPath = loaded.Path;
        _editorLineEnding = loaded.LineEnding;
        _editorEncoding = loaded.Encoding;
        _escArmed = false;

        AddControl(_editor);
        _editor.OnInitializeLayout();
        _editor.ReplaceAllText(loaded.Lines, loaded.LineEnding, loaded.Encoding);
        _editor.IsDirty = false;

        var rows = Math.Max(1, (int)_editor.ViewSize.Height);
        var target = Math.Max(0, loaded.Line - 1);
        _editor.ScrollableModel.CursorPosition.Y = target;
        _editor.ScrollableModel.ScrollPosition.Y = Math.Max(0, target - rows / 2);

        _output.ListBox.IsVisible = false;
        _errors.ListBox.IsVisible = false;
        _status = string.Empty;
        _statusSeverity = StatusSeverity.Info;
        SetPane(Pane.Editor);
    }

    /// <summary>Writes the editor's text to its file on a background task. The editor's dirty flag is cleared when it lands.</summary>
    private void SaveEditor()
    {
        if (!_editorOpen)
        {
            return;
        }

        var lines = _editor.TextModel.Lines.AsSnapshot().ToArray();
        var text = string.Join(_editorLineEnding, lines);
        var path = _editorPath;
        var encoding = _editorEncoding;
        _status = iConsole.Localization.UiText.Get("Solution.Saving", "Saving...");
        _statusSeverity = StatusSeverity.Info;
        StateHasChanged();

        _ = Task.Run(() =>
        {
            string? error = null;
            try
            {
                File.WriteAllText(path, text, encoding);
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            _saveOutcome = new SaveOutcome(path, text, error);
        }, CancellationToken.None);
    }

    /// <summary>Applies a finished save: the editor is clean only if it still holds the text that was written.</summary>
    private void ApplySaveOutcome(SaveOutcome outcome)
    {
        if (outcome.Error is not null)
        {
            _status = string.Format(iConsole.Localization.UiText.Get("Solution.SaveFailed", "Save failed: {0}"), outcome.Error);
            _statusSeverity = StatusSeverity.Error;
        }
        else
        {
            var current = string.Join(_editorLineEnding, _editor.TextModel.Lines.AsSnapshot());
            if (current == outcome.Text)
            {
                _editor.IsDirty = false;
            }

            _status = string.Format(iConsole.Localization.UiText.Get("Solution.Saved", "Saved {0}."), Path.GetFileName(outcome.Path));
            _statusSeverity = StatusSeverity.Success;
        }

        StateHasChanged();
    }

    /// <summary>Esc in the editor: the first press on unsaved text only warns; the next one closes and discards.</summary>
    private void CloseOrWarnEditor()
    {
        if (_editor.IsDirty && !_escArmed)
        {
            _escArmed = true;
            _status = iConsole.Localization.UiText.Get("Solution.UnsavedEscape", "Unsaved changes: Ctrl+S saves them, Esc again discards them.");
            _statusSeverity = StatusSeverity.Error;
            StateHasChanged();
            return;
        }

        CloseEditorWithoutAsking();
        SetPane(Pane.Projects);
    }

    /// <summary>Takes the editor out of the layout and gives the output panes their rows back. Does not ask about unsaved text.</summary>
    private void CloseEditorWithoutAsking()
    {
        if (!_editorOpen)
        {
            return;
        }

        RemoveControl(_editor);
        _editorOpen = false;
        _escArmed = false;
        _output.ListBox.IsVisible = true;
        _errors.ListBox.IsVisible = true;
    }

    #endregion

    #region Panes

    /// <summary>The pane the scroll keys move: the Errors pane when it has the keys, the Output pane otherwise.</summary>
    private SolutionOutputPane FocusedOutputPane => _pane == Pane.Errors ? _errors : _output;

    private void CyclePane()
    {
        var next = _pane switch
        {
            Pane.Projects => Pane.Output,
            Pane.Output => Pane.Errors,
            Pane.Errors => _editorOpen ? Pane.Editor : Pane.Projects,
            _ => Pane.Projects,
        };
        SetPane(next);
    }

    private void SetPane(Pane pane)
    {
        _pane = pane;
        switch (pane)
        {
            case Pane.Editor:
                _editor.SetFocus();
                break;
            case Pane.Projects:
                _projectListBox.SetFocus();
                break;
        }

        StateHasChanged();
    }

    #endregion

    #region SBOM

    /// <summary>
    /// s: writes the solution's SBOM in the chosen format. It reads the assets files off the UI thread and is refused
    /// while a dotnet command is running, since that command owns the status line.
    /// </summary>
    private void GenerateSbom()
    {
        if (_busy)
        {
            _status = string.Format(iConsole.Localization.UiText.Get("Solution.SbomBusy", "dotnet {0} is still running; the SBOM can be written when it finishes."), _currentVerb);
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        if (string.IsNullOrEmpty(_solution.Directory))
        {
            return;
        }

        var solution = _solution;
        var format = _sbomFormat;
        _status = iConsole.Localization.UiText.Get("Solution.SbomWriting", "Writing the SBOM...");
        _statusSeverity = StatusSeverity.Info;
        StateHasChanged();

        _ = Task.Run(() =>
        {
            string message;
            var severity = StatusSeverity.Success;
            try
            {
                var result = _dotnetSolutionService.GenerateSbom(solution, format);
                if (result.Path.Length == 0)
                {
                    message = iConsole.Localization.UiText.Get("Solution.SbomNoAssets", "No project.assets.json found: build or restore the solution first.");
                    severity = StatusSeverity.Error;
                }
                else
                {
                    message = string.Format(iConsole.Localization.UiText.Get("Solution.SbomWritten", "Wrote an SBOM of {0} packages to {1}."), result.ComponentCount, result.Path);
                    if (result.ProjectsWithoutAssets > 0)
                    {
                        message += " " + string.Format(iConsole.Localization.UiText.Get("Solution.SbomMissingSome", "{0} projects had no assets file and are left out."), result.ProjectsWithoutAssets);
                    }
                }
            }
            catch (Exception exception)
            {
                message = string.Format(iConsole.Localization.UiText.Get("Solution.SbomFailed", "SBOM failed: {0}"), exception.Message);
                severity = StatusSeverity.Error;
            }

            _status = message;
            _statusSeverity = severity;
            StateHasChanged();
        }, CancellationToken.None);
    }

    /// <summary>f: switches the SBOM between XML and JSON. Takes effect on the next s.</summary>
    private void CycleSbomFormat()
    {
        _sbomFormat = _sbomFormat == SbomFormat.Xml ? SbomFormat.Json : SbomFormat.Xml;
        _status = string.Format(iConsole.Localization.UiText.Get("Solution.SbomFormat", "SBOM format: {0}."), _sbomFormat == SbomFormat.Json ? "JSON" : "XML");
        _statusSeverity = StatusSeverity.Info;
        StateHasChanged();
    }

    #endregion

    #region Input

    /// <summary>The keys the focused pane answers to, named the way the footer shows them.</summary>
    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Tab", UiText.Get("DialogHint.NextPane", "Switch the pane"));

        switch (_pane)
        {
            case Pane.Editor:
                yield return ("Ctrl+S", UiText.Get("Viewer.HintSave", "Save the file"));
                yield return ("Esc", UiText.Get("DialogHint.CloseEditor", "Close the editor"));
                yield break;
            case Pane.Output:
            case Pane.Errors:
                if (_pane == Pane.Errors)
                {
                    yield return ("Enter", UiText.Get("DialogHint.OpenErrorLine", "Open the error's file at its line"));
                }

                yield return ("PageUp/PageDown", UiText.Get("DialogHint.ScrollOutput", "Scroll the output"));
                yield return ("Home/End", UiText.Get("DialogHint.TopOfOutputOrFollow", "Top of the output, or follow the end"));
                yield break;
        }

        yield return ("Space", UiText.Get("DialogHint.SelectProject", "Select or unselect the project"));
        yield return ("A", UiText.Get("DialogHint.SelectAllProjects", "Select all projects"));
        yield return ("Shift+T", UiText.Get("DialogHint.SelectAllTestProjects", "Select all test projects"));
        yield return ("B", UiText.Get("DialogHint.BuildSelected", "Build the selected projects"));
        yield return ("C", UiText.Get("DialogHint.CleanSelected", "Clean the selected projects"));
        yield return ("T", UiText.Get("DialogHint.RunTests", "Run the tests"));
        yield return ("R", UiText.Get("DialogHint.RunProject", "Run the project"));
        yield return ("X", UiText.Get("DialogHint.CancelRunningCommand", "Cancel the running command"));
        yield return ("V", UiText.Get("DialogHint.NextDotnetSdk", "Switch to the next .NET SDK"));
        yield return ("S", UiText.Get("DialogHint.GenerateSbom", "Write a CycloneDX SBOM of the solution"));
        yield return ("F", UiText.Get("DialogHint.SbomFormat", "Switch the SBOM between XML and JSON"));
        yield return ("Enter", UiText.Get("DialogHint.OpenProjectFile", "Open the project file in the editor"));
        yield return ("PageUp/PageDown", UiText.Get("DialogHint.ScrollOutput", "Scroll the output"));
        yield return ("Home/End", UiText.Get("DialogHint.TopOfOutputOrFollow", "Top of the output, or follow the end"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // The editor takes every key while it has the focus; its letters are text, not commands.
        if (_editorOpen && _pane == Pane.Editor)
        {
            return await HandleEditorKeyAsync(keyEvent);
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                if (_runCts is not null) await _runCts.CancelAsync();
                Close();
                return true;
            case ConsoleKey.Tab when !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift:
                CyclePane();
                return true;
            case ConsoleKey.Enter when !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift:
                if (_pane == Pane.Projects)
                {
                    OpenProjectFile();
                    return true;
                }

                if (_pane == Pane.Errors)
                {
                    OpenErrorLine();
                    return true;
                }
                break;
            case ConsoleKey.Spacebar when _pane == Pane.Projects:
                ToggleSelectionAtCursor();
                return true;
            case ConsoleKey.UpArrow when _pane is Pane.Output or Pane.Errors:
                FocusedOutputPane.Scroll(-1);
                StateHasChanged();
                return true;
            case ConsoleKey.DownArrow when _pane is Pane.Output or Pane.Errors:
                FocusedOutputPane.Scroll(1);
                StateHasChanged();
                return true;
            case ConsoleKey.PageUp:
                FocusedOutputPane.Scroll(-10);
                StateHasChanged();
                return true;
            case ConsoleKey.PageDown:
                FocusedOutputPane.Scroll(10);
                StateHasChanged();
                return true;
            case ConsoleKey.Home:
                FocusedOutputPane.ScrollToTop();
                StateHasChanged();
                return true;
            case ConsoleKey.End:
                FocusedOutputPane.FollowTail();
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
            case 'c':
                RunClean();
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
            case 'v':
                CycleSdk();
                return true;
            case 's':
                GenerateSbom();
                return true;
            case 'f':
                CycleSbomFormat();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private async Task<bool> HandleEditorKeyAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            CloseOrWarnEditor();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.S && keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            SaveEditor();
            return true;
        }

        _escArmed = false;
        if (await _editor.HandleKeyEventAsync(keyEvent))
        {
            StateHasChanged();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>Runs on the UI thread between frames: shows a file that was read and applies a finished save.</summary>
    public override void OnIdle()
    {
        base.OnIdle();

        if (_loadedFile is { } loaded)
        {
            _loadedFile = null;
            ShowLoadedFile(loaded);
        }

        if (_saveOutcome is { } outcome)
        {
            _saveOutcome = null;
            ApplySaveOutcome(outcome);
        }
    }

    #endregion

    #region Test seams

    internal string StatusForTest => _status;
    internal string SdkVersionForTest => SelectedSdk?.Version ?? string.Empty;
    internal string DotnetHostForTest => DotnetHost;
    internal int OutputScrollForTest => (int)_output.ListBox.ScrollableModel.ScrollPosition.Y;
    internal int OutputRowsForTest => (int)_output.ListBox.ViewSize.Height;
    internal bool IsBusyForTest => _busy;
    internal IReadOnlyList<string> OutputForTest => _output.Lines;
    internal IReadOnlyList<string> ErrorsForTest => _errors.Lines;
    internal bool EditorOpenForTest => _editorOpen;
    internal string EditorTextForTest => string.Join('\n', _editor.TextModel.Lines.AsSnapshot());
    internal bool EditorDirtyForTest => _editor.IsDirty;
    internal string PaneForTest => _pane.ToString();
    internal IReadOnlyList<string> KeyHintsForTest => [.. GetKeyHints().Select(hint => hint.Key)];

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
        var columns = ComputeColumns(iw, ih);
        if (columns.MidHeight <= 0)
        {
            return;
        }

        var leftW = columns.LeftWidth;
        var rightX = ix + columns.RightX - 1; // the layout's column is measured from the frame's edge; ix is one in
        var rightW = columns.RightWidth;

        DrawSectionHeader(buffer, ix, midY, leftW, UiText.Get("Solution.Projects", "Projects"), _pane == Pane.Projects, colorScheme);

        if (rightW > 0)
        {
            if (_editorOpen)
            {
                var name = Path.GetFileName(_editorPath) + (_editor.IsDirty ? " *" : string.Empty);
                var title = $"{UiText.Get("Solution.Editor", "Editor")}: {name}";
                DrawSectionHeader(buffer, rightX, midY, rightW, title, _pane == Pane.Editor, colorScheme);
            }
            else
            {
                DrawSectionHeader(buffer, rightX, midY, rightW, UiText.Get("Packages.Output", "Output"), _pane == Pane.Output, colorScheme);
                if (columns.ErrorRows > 0)
                {
                    var errorsTitle = $"{UiText.Get("Solution.Errors", "Errors")} ({_errors.Count})";
                    DrawSectionHeader(buffer, rightX, midY + columns.OutputRows, rightW, errorsTitle, _pane == Pane.Errors, colorScheme);
                }
            }
        }
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        var busy = _busy ? "  *" : string.Empty;
        var selected = _selectedProjects.Count > 0 ? $", {_selectedProjects.Count} selected" : string.Empty;
        var sdk = SelectedSdk is { } chosen ? $"   .NET SDK {chosen.Version}" : "   .NET SDK not found";
        var text = string.IsNullOrEmpty(_solution.Path)
            ? SolutionPath
            : $"{_solution.Path}   ({_solution.Projects.Count} projects{selected}){sdk}{busy}";
        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    /// <summary>The hints of the focused pane, in the short form the footer uses. Projects: the run keys; Output and
    /// Errors: scrolling; the editor: its save and close keys.</summary>
    internal static string KeyHints => iConsole.Localization.UiText.Get("Hints.SolutionViewer", "b:build c:clean t:test r:run x:cancel v:SDK s:SBOM f:XML/JSON Space:check a:all T:tests Enter:open Tab:pane Esc:close");

    /// <summary>The footer's hints for the pane that has the keys. A run in progress adds x:cancel, except in the editor, where x is a letter to type.</summary>
    private string ContextHints
    {
        get
        {
            var hints = _pane switch
            {
                Pane.Editor => iConsole.Localization.UiText.Get("Hints.SolutionEditor", "Ctrl+S:save Esc:close the editor"),
                Pane.Errors => iConsole.Localization.UiText.Get("Hints.SolutionErrors", "Enter:open at line PgUp/PgDn/Home/End:scroll Tab:pane Esc:close"),
                Pane.Output => iConsole.Localization.UiText.Get("Hints.SolutionOutput", "PgUp/PgDn/Home/End:scroll Up/Down:line Tab:pane Esc:close"),
                _ => KeyHints,
            };
            return _busy && _pane != Pane.Editor ? "x:cancel  " + hints : hints;
        }
    }

    /// <summary>The SDK new commands run with: the one chosen with v, the newest stable one by default.</summary>
    private DotnetSdk? SelectedSdk => _sdkIndex >= 0 && _sdkIndex < _sdks.Count ? _sdks[_sdkIndex] : null;

    /// <summary>The <c>dotnet</c> host to run: the selected SDK's, or the <c>dotnet</c> on the PATH when none was found.</summary>
    private string DotnetHost => SelectedSdk?.HostPath ?? "dotnet";

    /// <summary>
    /// v: the next installed SDK. A command already running keeps the SDK it started with; the next one uses this.
    /// </summary>
    private void CycleSdk()
    {
        if (_sdks.Count == 0)
        {
            _status = iConsole.Localization.UiText.Get("Solution.NoSdk", "No .NET SDK was found; commands run with the dotnet on the PATH.");
        }
        else if (_sdks.Count == 1)
        {
            _status = string.Format(iConsole.Localization.UiText.Get("Solution.OnlySdk", "Only .NET SDK {0} is installed."), SelectedSdk!.Version);
        }
        else
        {
            _sdkIndex = (_sdkIndex + 1) % _sdks.Count;
            _status = string.Format(iConsole.Localization.UiText.Get("Solution.UsingSdk", "Using .NET SDK {0}."), SelectedSdk!.IsPrerelease ? string.Format(iConsole.Localization.UiText.Get("Solution.SdkPreview", "{0} (preview)"), SelectedSdk.Version) : SelectedSdk.Version);
        }

        _statusSeverity = StatusSeverity.Info;
        StateHasChanged();
    }

    /// <summary>
    /// Status (coloured by severity) followed by the hints of the focused pane. The hints used to be shown only
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
        }, ContextHints, colorScheme);
    }

    #endregion
}
