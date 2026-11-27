using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.DevelopmentStudio.Services.Analysis;
using iTerminal.DevelopmentStudio.Services.Dap;
using iTerminal.DevelopmentStudio.Services.Lsp;
using iTerminal.Core.Services.Dotnet;
using iTerminal.Core.Services.Sdk;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The Development Studio (Tools &gt; Development studio): a Solution Explorer of the .NET solutions and projects, Python
/// and PowerShell scripts under <see cref="Folder"/>, a Test Explorer of the test items with their results, a source view
/// with breakpoints, the output of build, run, clean and test, a debug view (call stack, locals, watch, breakpoints) and the
/// Error List of the errors and warnings those commands printed. Each of these panels is its own component, docked to an
/// edge or the centre: F8 moves a panel to another edge (swapping with the panel there) and resizes it with Shift+arrows.
/// The keys follow Visual Studio and VS Code: F5 starts or continues, Shift+F5 stops, F9 toggles a breakpoint, F10 steps
/// over, F11 steps into and Shift+F11 steps out. Commands run on background tasks; key handlers and drawing never wait for them.
/// </summary>
public class DevelopmentStudioDialog : SplitPaneDialog
{
    /// <summary>What the left pane lists: the Solution Explorer, or the Test Explorer.</summary>
    private enum ExplorerMode { Solution, Tests, Files }

    /// <summary>The tabs of the bottom output panel: the output log, the debug view, the coverage and the profile.</summary>
    private enum OutputTab { Output, Debug, Coverage, Profile }

    private enum StatusSeverity { Info, Success, Error }

    /// <summary>One row of the explorer. <see cref="Item"/> is null for a heading row.</summary>
    /// <summary>One row of the explorer. A File Explorer row carries <see cref="FilePath"/> instead of a project.</summary>
    private sealed record ExplorerRow(string DisplayName, DevelopmentProject? Item, Color? Tint, string? FilePath = null, bool IsDirectory = false);

    /// <summary>A file or folder of the File Explorer tree. <see cref="Children"/> is empty for a file.</summary>
    private sealed record FileNode(string Path, bool IsDirectory, List<FileNode> Children);

    /// <summary>The result of the last test run of one test item.</summary>
    private sealed record TestOutcome(bool Passed, TestTotals Totals);

    /// <summary>Output lines kept for the log; the oldest are dropped past this.</summary>
    public const int MaxOutputLines = 5000;

    /// <summary>Source files larger than this are not opened in the source view.</summary>
    public const long MaxSourceBytes = 2 * 1024 * 1024;

    private sealed class ExplorerListBox(IListBoxModel<ExplorerRow> listModel, IThemeService themeService)
        : ListBox<ExplorerRow>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count) return null;
            return ListModel.ListItems[index].Item.Tint;
        }
    }

    private sealed class ViewListBox(IListBoxModel<string> listModel, IThemeService themeService)
        : ListBox<string>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count) return null;
            string line = ListModel.ListItems[index].Item;
            if (line.StartsWith("→", StringComparison.Ordinal)) return Color.Yellow;
            if (line.StartsWith("●", StringComparison.Ordinal)) return Color.Red;
            if (line.StartsWith("$ ", StringComparison.Ordinal)) return Color.Cyan;
            if (line.Contains(": error", StringComparison.OrdinalIgnoreCase) || line.Contains("FAILED", StringComparison.Ordinal)
                || line.Contains("Failed", StringComparison.Ordinal)) return Color.Red;
            if (line.Contains(": warning", StringComparison.OrdinalIgnoreCase)) return Color.Yellow;
            if (line.Contains("succeeded", StringComparison.OrdinalIgnoreCase) || line.Contains("passed", StringComparison.OrdinalIgnoreCase)) return Color.Green;
            return null;
        }

        /// <summary>The colours of the source rows, by the highlighter's palette. Null for the output and error lists.</summary>
        public ISyntaxHighlightingService? Highlighting { get; set; }

        /// <summary>The coloured stretches of each source row, in the row's own characters (the gutter already counted). Null for the other lists.</summary>
        public IReadOnlyList<IReadOnlyList<SyntaxSpan>>? RowSpans { get; set; }

        protected override void ColorizeItemText(ConxelString row, int index)
        {
            if (Highlighting is null || RowSpans is null || index < 0 || index >= RowSpans.Count) return;
            foreach (SyntaxSpan span in RowSpans[index])
            {
                if (span.Kind == SyntaxKind.Plain) continue;
                Color color = Highlighting.Palette.ColorOf(span.Kind, Color.White);
                int end = Math.Min(row.Count, span.Start + span.Length);
                for (int x = Math.Max(0, span.Start); x < end; x++)
                {
                    Conxel cell = row.String[x];
                    cell.ForegroundColor = color;
                    row.String[x] = cell;
                }
            }
        }
    }

    private readonly IDevelopmentWorkspaceScanner _scanner;
    private readonly IDevelopmentProcessRunner _runner;
    private readonly DevelopmentToolchains _toolchains;
    private readonly BreakpointStore _breakpoints;
    private readonly MainProgramStore _mainProgram;
    private readonly IDotnetSolutionService _dotnet;
    private readonly ISyntaxHighlightingService _highlighting;
    private readonly ISdkManager _sdkManager;

    /// <summary>The JDK home Java builds and runs use, or null for the javac and java on the PATH. Read when the studio opens and when the SDK manager closes.</summary>
    private string? _javaHome;

    /// <summary>The SDK list a background reload read, applied on the UI thread by <see cref="ApplySdkReload"/>.</summary>
    private readonly Lock _sdkGate = new();
    private SdkReload? _sdkReload;

    /// <summary>The .NET SDKs installed, newest first, and the one new .NET commands use (-1 when none was found).</summary>
    private IReadOnlyList<DotnetSdk> _sdks = [];
    private int _sdkIndex = -1;

    /// <summary>The .NET projects checked with Space. Empty means the cursor item is the target.</summary>
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The serialisation S writes the SBOM in. XML by default; F switches it to JSON.</summary>
    private SbomFormat _sbomFormat = SbomFormat.Xml;

    /// <summary>The solution the dialog was opened on (F3), or empty for the whole folder.</summary>
    private string _solutionPath = string.Empty;

    private readonly ConcurrentObservableCollection<ExplorerRow> _explorerItems = new();
    private readonly ConcurrentObservableCollection<string> _editorItems = new();
    private readonly ConcurrentObservableCollection<string> _outputItems = new();
    private readonly ExplorerListBox _explorerListBox;
    private readonly ViewListBox _editorListBox;
    private readonly ViewListBox _outputListBox;
    private readonly ConcurrentObservableCollection<string> _errorItems = new();
    private readonly ViewListBox _errorListBox;

    /// <summary>Guards everything the background tasks write: output, the debug state and the source.</summary>
    private readonly Lock _stateLock = new();
    private readonly List<string> _output = [];
    /// <summary>The errors and warnings the commands printed, in order. A line the tools print twice (MSBuild's summary) is listed once.</summary>
    private readonly List<BuildDiagnostic> _diagnostics = [];
    private readonly HashSet<string> _diagnosticTexts = new(StringComparer.Ordinal);
    /// <summary>The C# language server, shared by every dialog. The dialog reads it on its idle pass, never from its tasks.</summary>
    private readonly CSharpLanguageServer _languageServer;
    /// <summary>The server version the Error List and the gutter were last built from.</summary>
    private long _seenServerVersion = -1;
    private bool _startingShown;
    private bool _missingAnnounced;
    private readonly List<string> _sourceLines = [];
    private string _sourcePath = string.Empty;
    private string _sourceRequest = string.Empty;
    private IReadOnlyList<DebugStackFrame> _stack = [];
    private readonly List<DebugVariable> _locals = [];
    private readonly List<string> _watchExpressions = [];
    private readonly List<DebugVariable> _watchValues = [];
    private readonly Dictionary<int, int> _watchRows = [];
    private string _stopPath = string.Empty;
    private int _stopLine;

    private IReadOnlyList<DevelopmentProject> _items = [];
    private readonly Dictionary<string, TestOutcome> _testOutcomes = new(StringComparer.OrdinalIgnoreCase);
    private ExplorerMode _explorerMode = ExplorerMode.Solution;
    /// <summary>The panels, docked by <see cref="_dock"/> in the arrangement <see cref="_layout"/> gives them. F8 changes the arrangement.</summary>
    private readonly DockPanel _dock;
    private readonly Dictionary<StudioPanelKind, StudioPanel> _panels = [];
    private readonly StudioPanelLayout _layout = StudioPanelLayout.Default();
    private bool _layoutMode;
    private string _statusBeforeLayout = string.Empty;
    private StatusSeverity _statusSeverityBeforeLayout = StatusSeverity.Info;
    /// <summary>The tab the bottom output panel shows (F7 cycles it). The editor always shows the source.</summary>
    private OutputTab _outputTab = OutputTab.Output;

    private DebugSession? _session;

    /// <summary>Coverage of the last coverage run, and the file each Coverage row names (row 1 is the first file).</summary>
    private CoverageReport? _coverage;
    private readonly List<CoverageFile> _coverageFiles = [];

    /// <summary>The functions of the last profile, by cumulative time.</summary>
    private IReadOnlyList<ProfileEntry> _profile = [];

    /// <summary>The File Explorer tree of the folder, built once on first use (and again after a refresh).</summary>
    private IReadOnlyList<FileNode> _fileRoots = [];
    private bool _fileTreeBuilt;
    private readonly HashSet<string> _expandedFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The files and folders under each project of the Solution Explorer, by project path. They are read in the background the
    /// first time a project is expanded; <see cref="_loadingProjects"/> holds the ones being read.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<FileNode>> _projectFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _loadingProjects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Changed by every re-scan, so a project read that lands after it is dropped rather than shown stale.</summary>
    private int _treeGeneration;

    /// <summary>The folders the File Explorer does not list, and the tree's size and depth limits.</summary>
    private static readonly HashSet<string> SkippedFileFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".vscode", ".idea", "bin", "obj", "node_modules", ".venv", "venv", "__pycache__", ".pytest_cache", "TestResults", "packages",
    };

    public const int MaxFileNodes = 3000;
    public const int MaxFileDepth = 4;

    /// <summary>The files and folders listed under one project of the Solution Explorer.</summary>
    public const int MaxProjectNodes = 1000;
    private volatile bool _paused;
    private volatile bool _busy;
    private CancellationTokenSource? _runCts;
    private string _status = string.Empty;
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    /// <summary>The folder scanned for solutions and scripts. Set by the caller before the dialog is shown.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>
    /// The solution to open (F3 on a <c>.sln</c> or <c>.slnx</c>). Its folder is scanned, and only the solution and the
    /// projects it lists are shown. Empty opens the whole folder.
    /// </summary>
    public string SolutionPath
    {
        get => _solutionPath;
        set
        {
            _solutionPath = value;
            if (!string.IsNullOrEmpty(value)) Folder = Path.GetDirectoryName(value) ?? string.Empty;
        }
    }

    /// <summary>The .NET SDK new commands run with (the newest stable one by default), or null when none was found.</summary>
    public DotnetSdk? SelectedSdk => _sdkIndex >= 0 && _sdkIndex < _sdks.Count ? _sdks[_sdkIndex] : null;

    public DevelopmentStudioDialog(
        IDevelopmentWorkspaceScanner scanner,
        IDevelopmentProcessRunner runner,
        DevelopmentToolchains toolchains,
        BreakpointStore breakpoints,
        MainProgramStore mainProgram,
        IDotnetSolutionService dotnetSolutionService,
        IThemeService themeService,
        CSharpLanguageServer languageServer,
        ISyntaxHighlightingService highlighting,
        ISdkManager sdkManager)
        : base(nameof(DevelopmentStudioDialog), themeService)
    {
        _scanner = scanner;
        _languageServer = languageServer;
        _highlighting = highlighting;
        _runner = runner;
        _toolchains = toolchains;
        _breakpoints = breakpoints;
        _mainProgram = mainProgram;
        _dotnet = dotnetSolutionService;
        _sdkManager = sdkManager;
        Title = UiText.Get("DevelopmentStudio.Title", "Development studio");

        _explorerListBox = new ExplorerListBox(
            new ListBoxModel<ExplorerRow>(_explorerItems) { TransformItem = (row, _) => new ListItem<ExplorerRow>(row) { Value = row.DisplayName, IsEnabled = true } },
            themeService);
        _editorListBox = new ViewListBox(
            new ListBoxModel<string>(_editorItems) { TransformItem = (line, _) => new ListItem<string>(line) { Value = line, IsEnabled = true } },
            themeService)
        {
            Highlighting = highlighting,
        };
        _outputListBox = new ViewListBox(
            new ListBoxModel<string>(_outputItems) { TransformItem = (line, _) => new ListItem<string>(line) { Value = line, IsEnabled = true } },
            themeService);

        _errorListBox = new ViewListBox(
            new ListBoxModel<string>(_errorItems) { TransformItem = (line, _) => new ListItem<string>(line) { Value = line, IsEnabled = true } },
            themeService)
        {
            EmptyText = UiText.Get("DevelopmentStudio.NoErrors", "No errors or warnings. Build, run and test report theirs here."),
        };

        // Each panel is a component of its own; the dock places them in the order _layout gives.
        _dock = new DockPanel(themeService) { Gap = 1 };
        _panels[StudioPanelKind.Editor] = new StudioPanel(StudioPanelKind.Editor, _editorListBox, EditorTitle, themeService);
        _panels[StudioPanelKind.Explorer] = new StudioPanel(StudioPanelKind.Explorer, _explorerListBox, ExplorerHeading, themeService);
        _panels[StudioPanelKind.Output] = new StudioPanel(StudioPanelKind.Output, _outputListBox, () => string.Empty, themeService)
        {
            BandPainter = (buffer, x, y, width, colorScheme, focused) => DrawOutputTabs(buffer, x, y, width, colorScheme, focused),
        };
        _panels[StudioPanelKind.Errors] = new StudioPanel(StudioPanelKind.Errors, _errorListBox, ErrorHeading, themeService);
        AddControl(_dock);
        RebuildDock();
    }

    /// <summary>The status line text (the last command's outcome, or the reason a key did nothing).</summary>
    public string Status => _status;

    /// <summary>The breakpoints of the studio, shared by every opening of it.</summary>
    public BreakpointStore Breakpoints => _breakpoints;

    /// <summary>Whether a build, clean, test or run command (or a debug start) is running.</summary>
    public bool IsBusy => _busy;

    /// <summary>The explorer rows as they are drawn, in order.</summary>
    public IReadOnlyList<string> ExplorerRows => _explorerItems.Select(row => row.DisplayName).ToList();

    /// <summary>The lines of the editor as they are drawn, in order (the gutter included).</summary>
    public IReadOnlyList<string> EditorLines => [.. _editorItems];

    /// <summary>The lines of the bottom output panel as they are drawn, in order.</summary>
    public IReadOnlyList<string> OutputLines => [.. _outputItems];

    /// <summary>The Error List rows as they are drawn, in order.</summary>
    public IReadOnlyList<string> ErrorLines => [.. _errorItems];

    /// <summary>The panels in the order they are docked, each with its edge (or the centre) and its share of the space in percent.</summary>
    public IReadOnlyList<(StudioPanelKind Kind, DockSide Side, int Percent)> Panels => _layout.Docked();

    /// <summary>The panel whose list has the focus, or null when none has.</summary>
    public StudioPanelKind? FocusedPanel => FocusedPanelKind();

    /// <summary>Whether the panel layout mode (F8) is on.</summary>
    public bool IsLayoutMode => _layoutMode;

    /// <summary>The 1-based source line the editor's cursor is on, or 0 when no file is open.</summary>
    public int CursorLineNumber => CursorLine;

    /// <summary>Whether a debugging session is running (started, not yet ended).</summary>
    public bool IsDebugging => _session is not null;

    /// <summary>Whether the debugged program is stopped at a breakpoint or a step.</summary>
    public bool IsPaused => _paused;

    /// <summary>The items the scan found, in explorer order.</summary>
    public IReadOnlyList<DevelopmentProject> Items => _items;

    #region Lifecycle

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _checked.Clear();
        _testOutcomes.Clear();
        _treeGeneration++;
        _projectFiles.Clear();
        _loadingProjects.Clear();
        _items = await ScanAsync();
        ApplySdks(await Task.Run(ReadSdks, CancellationToken.None), keep: null);
        _javaHome = _sdkManager.ActiveJavaHome;
        _explorerMode = ExplorerMode.Solution;
        OpenSolutionsInTree();
        _outputTab = OutputTab.Output;
        _status = _items.Count == 0
            ? UiText.Get("DevelopmentStudio.NothingFound", "No solutions, projects or scripts were found in this folder.")
            : string.Format(UiText.Get("DevelopmentStudio.Found", "Found {0} items. Enter opens a file; F5 starts debugging."), _items.Count);
        _statusSeverity = StatusSeverity.Info;
        RebuildExplorer();
        SelectSolutionRow();
        RebuildView();
        StateHasChanged();
    }

    /// <summary>The solutions start expanded, as they do in Visual Studio: their projects show at once. The projects start collapsed.</summary>
    private void OpenSolutionsInTree()
    {
        foreach (var item in _items.Where(item => item.Kind == DevelopmentProjectKind.Solution))
        {
            _expandedFolders.Add(item.Path);
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = Parent.ViewSize;
            Position = new Position(0, 0);
        }

        // The panels fill the frame between the header row (row 1) and the footer row (the second to last).
        _dock.Position = new Position(1, 2);
        _dock.ViewSize = new Size(Math.Max(0, (int)ViewSize.Width - 2), Math.Max(0, (int)ViewSize.Height - 4));
        base.OnInitializeLayout();
        LayoutPanelBodies();
    }

    /// <summary>
    /// The studio opens on the Solution Explorer. The dialog's own focus goes to the first focusable control in the dock, which
    /// is the Error List when it is docked to the top, so the Explorer takes the focus after it: a second focused list would
    /// get the keys first.
    /// </summary>
    public override async Task ShowAsync()
    {
        await base.ShowAsync();

        // The dialog is added to its parent only after this returns, so the root cannot clear the flags the first focus left behind:
        // the dialog clears its own tree.
        ResetFocus();
        _explorerListBox.SetFocus();
    }

    /// <summary>Esc: stops a running command and the debugged program, then closes.</summary>
    private async Task CloseAsync()
    {
        if (_runCts is not null) await _runCts.CancelAsync();
        DebugSession? session;
        lock (_stateLock)
        {
            session = _session;
            _session = null;
        }

        if (session is not null)
        {
            _ = Task.Run(async () => { await session.TerminateAsync(); await session.DisposeAsync(); }, CancellationToken.None);
        }

        Close();
    }

    #endregion

    #region Keys

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F5", UiText.Get("DevelopmentStudio.HintStart", "Start debugging, or continue"));
        yield return ("Ctrl+F5", UiText.Get("DevelopmentStudio.HintRunNoDebug", "Run without debugging"));
        yield return ("Shift+F5", UiText.Get("DevelopmentStudio.HintStop", "Stop debugging"));
        yield return ("F9", UiText.Get("DevelopmentStudio.HintBreakpoint", "Toggle a breakpoint on the cursor line"));
        yield return ("F10 / F11", UiText.Get("DevelopmentStudio.HintStep", "Step over / step into"));
        yield return ("Shift+F11", UiText.Get("DevelopmentStudio.HintStepOut", "Step out"));
        yield return ("B / C / T / R", UiText.Get("DevelopmentStudio.HintCommands", "Build, clean, test, run the selected item"));
        yield return ("W", UiText.Get("DevelopmentStudio.HintWatch", "Watch the word under the cursor (in the debug view, remove it)"));
        yield return ("F6 / F7", UiText.Get("DevelopmentStudio.HintPanes", "Explorer tab (Solution, Tests, Files) / output panel tab (Output, Debug, Coverage, Profile)"));
        yield return ("F8", UiText.Get("DevelopmentStudio.HintLayout", "Move and resize the panels: arrows move the focused one to that edge, Shift+arrows resize it"));
        yield return ("Enter", UiText.Get("DevelopmentStudio.HintOpen", "Open the file (or run the test)"));
        yield return ("Left / Right", UiText.Get("DevelopmentStudio.HintExpand", "Collapse or expand the solution, project or folder under the cursor"));
        yield return ("M", UiText.Get("DevelopmentStudio.HintMain", "Make the item under the cursor the Main program: F5 and Ctrl+F5 start it"));
        yield return ("Space", UiText.Get("DevelopmentStudio.HintCheck", "Check or uncheck the .NET project; B, C, T and R then run on the checked ones"));
        yield return ("A / Shift+T", UiText.Get("DevelopmentStudio.HintCheckAll", "Check all projects / all test projects"));
        yield return ("V", UiText.Get("DevelopmentStudio.HintSdk", "Switch to the next .NET SDK"));
        yield return ("K", UiText.Get("DevelopmentStudio.HintSdkManager", "Install .NET SDKs and JDKs, and choose the JDK that Java builds and runs use"));
        yield return ("S / F", UiText.Get("DevelopmentStudio.HintSbom", "Write a CycloneDX SBOM of the solution / switch it between XML and JSON"));
        yield return ("O / P", UiText.Get("DevelopmentStudio.HintCoverage", "Measure the coverage of the selected item / profile the selected Python script"));
        yield return ("G", UiText.Get("DevelopmentStudio.HintGoToFile", "In the coverage view, open the file under the cursor"));
        yield return ("X", UiText.Get("DevelopmentStudio.HintCancel", "Cancel the running command"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // In the layout mode Esc ends the mode rather than closing the dialog.
        if (_layoutMode && HandleLayoutKey(keyEvent)) return true;

        bool plain = !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        bool shift = keyEvent.Shift && !keyEvent.Alt && !keyEvent.Control;
        bool ctrl = keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                await CloseAsync();
                return true;
            case ConsoleKey.F5 when ctrl:
                RunMainOrSelected("run");
                return true;
            case ConsoleKey.F5 when shift:
                StopDebugging();
                return true;
            case ConsoleKey.F5 when plain:
                StartOrContinueDebugging();
                return true;
            case ConsoleKey.F6 when plain:
                ToggleExplorerMode();
                return true;
            case ConsoleKey.F7 when plain:
                CycleOutputTab();
                return true;
            case ConsoleKey.F8 when plain:
                ToggleLayoutMode();
                return true;
            case ConsoleKey.F9 when plain:
                ToggleBreakpointAtCursor();
                return true;
            case ConsoleKey.F10 when plain:
                DebugStep(session => session.StepOverAsync());
                return true;
            case ConsoleKey.F11 when plain:
                DebugStep(session => session.StepInAsync());
                return true;
            case ConsoleKey.F11 when shift:
                DebugStep(session => session.StepOutAsync());
                return true;
            case ConsoleKey.Enter when plain:
                EnterPressed();
                return true;
            case ConsoleKey.Delete when plain:
                RemoveWatchAtCursor();
                return true;
            case ConsoleKey.R when ctrl:
                _ = RefreshAsync();
                return true;
            case ConsoleKey.R when plain:
                RunSelected("run");
                return true;
            case ConsoleKey.B when plain:
                RunSelected("build");
                return true;
            case ConsoleKey.C when plain:
                RunSelected("clean");
                return true;
            case ConsoleKey.T when plain:
                RunSelected("test");
                return true;
            case ConsoleKey.W when plain:
                ToggleWatchAtCursor();
                return true;
            case ConsoleKey.Spacebar when plain:
                ToggleCheckAtCursor();
                return true;
            case ConsoleKey.RightArrow when plain && _explorerListBox.IsFocused:
                ExpandOrCollapseAtCursor(expand: true);
                return true;
            case ConsoleKey.LeftArrow when plain && _explorerListBox.IsFocused:
                ExpandOrCollapseAtCursor(expand: false);
                return true;
            case ConsoleKey.M when plain:
                ToggleMainProgramAtCursor();
                return true;
            case ConsoleKey.A when plain:
                CheckAllProjects();
                return true;
            case ConsoleKey.T when shift:
                CheckAllTestProjects();
                return true;
            case ConsoleKey.V when plain:
                CycleSdk();
                return true;
            case ConsoleKey.S when plain:
                WriteSbom();
                return true;
            case ConsoleKey.F when plain:
                ToggleSbomFormat();
                return true;
            case ConsoleKey.O when plain:
                RunCoverage();
                return true;
            case ConsoleKey.P when plain:
                RunProfile();
                return true;
            case ConsoleKey.G when plain:
                OpenCoverageFileAtCursor();
                return true;
            case ConsoleKey.K when plain:
                await OpenSdkManagerAsync();
                return true;
            case ConsoleKey.X when plain:
                if (_runCts is not null) await _runCts.CancelAsync();
                return true;
        }

        // Keys the dialog does not use (the arrows, Page Up and Down) go to the focused list.
        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void EnterPressed()
    {
        if (_errorListBox.IsFocused)
        {
            OpenDiagnosticAtCursor();
            return;
        }

        if (_explorerMode == ExplorerMode.Files)
        {
            EnterFileRow();
            return;
        }

        var item = SelectedItem;
        if (_explorerMode == ExplorerMode.Tests)
        {
            if (item is not null) RunSelected("test");
            return;
        }

        // In the Solution Explorer a file row opens, and a solution, project or folder row expands or collapses.
        if (SelectedRow?.FilePath is not null)
        {
            EnterFileRow();
            return;
        }

        if (item is not null && IsContainer(item))
        {
            ToggleExpandedAt(item.Path);
            return;
        }

        if (item is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectFile", "Select a project or a script to open."), StatusSeverity.Info);
            return;
        }

        OpenSource(item.Path);
    }

    /// <summary>Enter on a tree file row opens it in the source view; on a folder row it expands or collapses the folder.</summary>
    private void EnterFileRow()
    {
        var row = SelectedRow;
        if (row?.FilePath is null) return;
        if (row.IsDirectory)
        {
            ToggleExpandedAt(row.FilePath);
            return;
        }

        OpenSource(row.FilePath);
    }

    /// <summary>Expands or collapses a solution, project or folder row, and rebuilds the tree.</summary>
    private void ToggleExpandedAt(string path)
    {
        if (!_expandedFolders.Remove(path)) _expandedFolders.Add(path);
        RebuildExplorer();
        StateHasChanged();
    }

    /// <summary>Left and Right: collapse or expand the container under the cursor. A file, or a row that is already so, does nothing.</summary>
    private void ExpandOrCollapseAtCursor(bool expand)
    {
        if (_explorerMode == ExplorerMode.Tests) return;
        var row = SelectedRow;
        if (row is null) return;

        string? path = row.Item?.Path ?? (row.IsDirectory ? row.FilePath : null);
        bool container = row.Item is { } item ? IsContainer(item) : row.IsDirectory;
        if (path is null || !container || _expandedFolders.Contains(path) == expand) return;
        ToggleExpandedAt(path);
    }

    /// <summary>F6 cycles the left pane: Solution Explorer, Test Explorer, File Explorer.</summary>
    private void ToggleExplorerMode()
    {
        _explorerMode = _explorerMode switch
        {
            ExplorerMode.Solution => ExplorerMode.Tests,
            ExplorerMode.Tests => ExplorerMode.Files,
            _ => ExplorerMode.Solution,
        };
        RebuildExplorer();
        if (_explorerMode == ExplorerMode.Files)
        {
            _ = LoadFileTreeAsync();
            SetStatus(UiText.Get("DevelopmentStudio.FileExplorer", "File Explorer: Enter expands a folder or opens a file."), StatusSeverity.Info);
            return;
        }

        SetStatus(_explorerMode == ExplorerMode.Tests
            ? UiText.Get("DevelopmentStudio.TestExplorer", "Test Explorer: Enter or T runs the test item under the cursor.")
            : UiText.Get("DevelopmentStudio.SolutionExplorer", "Solution Explorer."), StatusSeverity.Info);
    }

    /// <summary>Reads the folder's tree (bounded in count and depth) off the UI thread, then lists it.</summary>
    private async Task LoadFileTreeAsync()
    {
        string folder = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        var roots = await Task.Run(() =>
        {
            int budget = MaxFileNodes;
            return ReadFileFolder(folder, 0, ref budget);
        }, CancellationToken.None);
        _fileRoots = roots;
        _fileTreeBuilt = true;
        RebuildExplorer();
        StateHasChanged();
    }

    /// <summary>
    /// Folders first, then files, each by name; folders named in <see cref="SkippedFileFolders"/> are left out, and so are the
    /// folders in <paramref name="excludedFolders"/> (the folders other projects own).
    /// </summary>
    private static List<FileNode> ReadFileFolder(string folder, int depth, ref int budget, IReadOnlySet<string>? excludedFolders = null)
    {
        var nodes = new List<FileNode>();
        List<string> dirs;
        List<string> files;
        try
        {
            dirs = Directory.EnumerateDirectories(folder)
                .Where(dir => !SkippedFileFolders.Contains(Path.GetFileName(dir)) && excludedFolders?.Contains(dir) != true)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
            files = Directory.EnumerateFiles(folder)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return nodes;
        }

        foreach (string dir in dirs)
        {
            if (budget <= 0) return nodes;
            budget--;
            var node = new FileNode(dir, true, []);
            if (depth < MaxFileDepth) node.Children.AddRange(ReadFileFolder(dir, depth + 1, ref budget, excludedFolders));
            nodes.Add(node);
        }

        foreach (string file in files)
        {
            if (budget <= 0) return nodes;
            budget--;
            nodes.Add(new FileNode(file, false, []));
        }

        return nodes;
    }

    /// <summary>The File Explorer row under the cursor (null in the other panes or on the heading).</summary>
    private ExplorerRow? SelectedRow
    {
        get
        {
            var rows = _explorerListBox.ListModel.ListItems;
            var index = (int)_explorerListBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < rows.Count ? rows[index].Item : null;
        }
    }

    /// <summary>F7 cycles the output panel's tabs: Output, Debug, Coverage, Profile. The editor is not affected.</summary>
    private void CycleOutputTab()
    {
        _outputTab = _outputTab switch
        {
            OutputTab.Output => OutputTab.Debug,
            OutputTab.Debug => OutputTab.Coverage,
            OutputTab.Coverage => OutputTab.Profile,
            _ => OutputTab.Output,
        };
        RebuildOutputPanel();
        StateHasChanged();
    }

    /// <summary>G in the coverage tab: opens the file the cursor is on.</summary>
    private void OpenCoverageFileAtCursor()
    {
        if (_outputTab != OutputTab.Coverage) return;
        int row = (int)_outputListBox.ScrollableModel.CursorPosition.Y;
        CoverageFile? file;
        lock (_stateLock)
        {
            // Row 0 is the total; the files follow in order.
            file = row >= 1 && row - 1 < _coverageFiles.Count ? _coverageFiles[row - 1] : null;
        }

        if (file is null) return;
        OpenSource(file.Path);
    }

    #endregion

    #region Explorer and views

    private DevelopmentProject? SelectedItem
    {
        get
        {
            var rows = _explorerListBox.ListModel.ListItems;
            var index = (int)_explorerListBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < rows.Count ? rows[index].Item.Item : null;
        }
    }

    /// <summary>Rebuilds the explorer rows from the scan and, in the Test Explorer, the test results.</summary>
    /// <summary>Rebuilds the explorer rows: the Solution Explorer, the Test Explorer, or the File Explorer tree.</summary>
    private void RebuildExplorer()
    {
        var rows = new List<ExplorerRow>();
        if (_explorerMode == ExplorerMode.Files)
        {
            if (!_fileTreeBuilt)
            {
                rows.Add(new ExplorerRow(UiText.Get("DevelopmentStudio.ReadingFolder", "(reading the folder...)"), null, null));
            }

            AddFileRows(rows, _fileRoots, 0);
        }
        else if (_explorerMode == ExplorerMode.Solution)
        {
            AddSolutionTree(rows);
        }
        else
        {
            foreach (var item in _items.Where(i => i.IsTest))
            {
                string marker = "    ";
                Color? tint = null;
                if (_testOutcomes.TryGetValue(item.Path, out var outcome))
                {
                    marker = outcome.Passed ? "pass" : "fail";
                    tint = outcome.Passed ? Color.Green : Color.Red;
                }

                rows.Add(new ExplorerRow($" [{marker}] {LanguageLabel(item.Language)}  {item.Name}", item, tint));
            }
        }

        if (rows.Count == 0)
        {
            rows.Add(new ExplorerRow(UiText.Get("DevelopmentStudio.EmptyExplorer", "(nothing here)"), null, null));
        }

        _explorerItems.ReplaceAll(rows);
        _explorerListBox.UpdateModel();
    }

    /// <summary>
    /// The Solution Explorer tree, as Visual Studio draws it: a solution lists its projects, a project lists the files and folders
    /// under it, and the items that belong to no solution follow at the top level in scan order.
    /// </summary>
    private void AddSolutionTree(List<ExplorerRow> rows)
    {
        var solutionPaths = _items
            .Where(item => item.Kind == DevelopmentProjectKind.Solution)
            .Select(item => item.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _items)
        {
            // A project that a listed solution holds is drawn under that solution, not again at the top level.
            if (item.SolutionPath is { } owner && solutionPaths.Contains(owner)) continue;
            AddItemRows(rows, item, 0);
        }
    }

    /// <summary>One item of the tree and, when it is expanded, what it holds: its projects, or its files and folders.</summary>
    private void AddItemRows(List<ExplorerRow> rows, DevelopmentProject item, int depth)
    {
        bool container = IsContainer(item);
        bool expanded = container && _expandedFolders.Contains(item.Path);
        string marker = container ? (expanded ? "▾ " : "▸ ") : "  ";
        rows.Add(new ExplorerRow(new string(' ', depth * 2) + marker + ItemRowName(item), item, TintFor(item)));
        if (!expanded) return;

        if (item.Kind == DevelopmentProjectKind.Solution)
        {
            foreach (var project in _items.Where(child => child.Kind == DevelopmentProjectKind.DotNetProject
                && child.SolutionPath is { } owner && owner.Equals(item.Path, StringComparison.OrdinalIgnoreCase)))
            {
                AddItemRows(rows, project, depth + 1);
            }

            return;
        }

        if (_projectFiles.TryGetValue(item.Path, out var nodes))
        {
            AddFileRows(rows, nodes, depth + 1);
            return;
        }

        rows.Add(new ExplorerRow(new string(' ', (depth + 1) * 2) + UiText.Get("DevelopmentStudio.ReadingFolder", "(reading the folder...)"), null, null));
        LoadProjectFiles(item);
    }

    /// <summary>Adds the files and folders of a tree level. An expanded folder lists its own children below it.</summary>
    private void AddFileRows(List<ExplorerRow> rows, IEnumerable<FileNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            bool expanded = node.IsDirectory && _expandedFolders.Contains(node.Path);
            string name = Path.GetFileName(node.Path);
            string marker = node.IsDirectory ? (expanded ? "▾ " : "▸ ") : "  ";
            rows.Add(new ExplorerRow(new string(' ', depth * 2) + marker + name, null,
                node.IsDirectory ? Color.Cyan : null, node.Path, node.IsDirectory));
            if (expanded) AddFileRows(rows, node.Children, depth + 1);
        }
    }

    /// <summary>A solution, a .NET project or a Java project: a row that expands to show what it holds. A script has none.</summary>
    private static bool IsContainer(DevelopmentProject item) =>
        item.Kind is DevelopmentProjectKind.Solution or DevelopmentProjectKind.DotNetProject or DevelopmentProjectKind.JavaProject;

    /// <summary>
    /// Reads the files and folders of a project's folder in the background, then rebuilds the tree. The folders that other
    /// listed projects own are left out, since each of those is drawn under its own project.
    /// </summary>
    private void LoadProjectFiles(DevelopmentProject project)
    {
        if (_projectFiles.ContainsKey(project.Path) || !_loadingProjects.Add(project.Path)) return;

        int generation = _treeGeneration;
        var otherProjectFolders = _items
            .Where(item => IsContainer(item) && item.Kind != DevelopmentProjectKind.Solution && item.Path != project.Path)
            .Select(item => item.Directory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ = LoadProjectFilesAsync(project, generation, otherProjectFolders);
    }

    private async Task LoadProjectFilesAsync(DevelopmentProject project, int generation, HashSet<string> otherProjectFolders)
    {
        var nodes = await Task.Run(() =>
        {
            int budget = MaxProjectNodes;
            return ReadFileFolder(project.Directory, 0, ref budget, otherProjectFolders);
        }, CancellationToken.None);

        _loadingProjects.Remove(project.Path);
        if (generation != _treeGeneration) return;
        _projectFiles[project.Path] = nodes;
        RebuildExplorer();
        StateHasChanged();
    }

    /// <summary>The name of an item's row: its checkbox for a .NET project, and the tags for a test and for the Main program.</summary>
    private string ItemRowName(DevelopmentProject item)
    {
        string test = item.IsTest ? "  [test]" : string.Empty;
        string main = IsMain(item) ? $"  [{UiText.Get("DevelopmentStudio.MainTag", "main")}]" : string.Empty;
        return item.Kind switch
        {
            DevelopmentProjectKind.Solution => $"Solution  {item.Name}",
            DevelopmentProjectKind.DotNetProject => $"[{(_checked.Contains(item.Path) ? 'x' : ' ')}] {LanguageLabel(item.Language)}  {item.Name}{test}{main}",
            _ => $"{LanguageLabel(item.Language)}  {item.Name}{test}{main}",
        };
    }

    /// <summary>Whether the item is the Main program.</summary>
    private bool IsMain(DevelopmentProject item) =>
        item.Kind != DevelopmentProjectKind.Solution && _mainProgram.Path.Equals(item.Path, StringComparison.OrdinalIgnoreCase);

    /// <summary>The Main program, when one is set and the scan still lists it; otherwise null.</summary>
    private DevelopmentProject? MainItem => _items.FirstOrDefault(IsMain);

    /// <summary>M: makes the item under the cursor the Main program (a console, web project or script), or clears it when it already is.</summary>
    private void ToggleMainProgramAtCursor()
    {
        var item = SelectedItem;
        if (item is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectItem", "Select an item first."), StatusSeverity.Info);
            return;
        }

        if (item.Kind == DevelopmentProjectKind.Solution)
        {
            SetStatus(UiText.Get("DevelopmentStudio.MainSolution", "A solution cannot be the Main program. Select a project or a script."), StatusSeverity.Info);
            return;
        }

        if (!IsMain(item) && !item.IsStartable)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.MainNotStartable", "{0} has no program to start. Only a console or web project, or a script, can be the Main program."), item.Name), StatusSeverity.Info);
            return;
        }

        if (IsMain(item))
        {
            _mainProgram.Clear();
            SetStatus(UiText.Get("DevelopmentStudio.MainCleared", "No Main program: F5 starts the item under the cursor."), StatusSeverity.Info);
        }
        else
        {
            _mainProgram.Set(item.Path);
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.MainSet", "{0} is the Main program. F5 and Ctrl+F5 start it."), item.Name), StatusSeverity.Info);
        }

        RebuildExplorer();
        StateHasChanged();
    }

    private static Color? TintFor(DevelopmentProject item) =>
        item.Kind == DevelopmentProjectKind.Solution ? Color.Cyan : null;

    private static string LanguageLabel(DevelopmentLanguage language) => language switch
    {
        DevelopmentLanguage.DotNet => "C#",
        DevelopmentLanguage.Python => "Python",
        DevelopmentLanguage.Java => "Java",
        _ => "PowerShell",
    };

    /// <summary>Rebuilds both the editor and the output panel.</summary>
    private void RebuildView()
    {
        RebuildEditor();
        RebuildOutputPanel();
        RebuildErrorPanel();
    }

    /// <summary>Rebuilds the editor: the open file with its gutter (breakpoint, stop line, coverage), or the hint when none is open.</summary>
    private void RebuildEditor()
    {
        var lines = new List<string>();
        var rowSpans = new List<IReadOnlyList<SyntaxSpan>>();
        lock (_stateLock)
        {
            if (_sourcePath.Length == 0)
            {
                lines.Add(UiText.Get("DevelopmentStudio.NoSource", "Select a file in the explorer and press Enter to open it here."));
                rowSpans.Add([]);
            }

            // Java, C#, PowerShell, Python and the other languages the highlighter knows are coloured line by line; a block
            // comment carries from one line to the next, so the lines are tokenized in order.
            SyntaxLanguage? language = _highlighting.FindByFile(_sourcePath);
            bool inComment = false;
            var spans = new List<SyntaxSpan>();
            CoverageFile? covered = _coverage?.Files.FirstOrDefault(file => file.Path.Equals(_sourcePath, StringComparison.OrdinalIgnoreCase));
            Dictionary<int, string> liveMarks = LiveMarks(_sourcePath);
            for (int i = 0; i < _sourceLines.Count; i++)
            {
                int number = i + 1;
                bool isStop = number == _stopLine && _sourcePath.Equals(_stopPath, StringComparison.OrdinalIgnoreCase);
                string gutter = isStop ? "→"
                    : _breakpoints.Contains(_sourcePath, number) ? "●"
                    : liveMarks.TryGetValue(number, out string? liveMark) ? liveMark
                    : covered is not null && covered.Hits.TryGetValue(number, out int hits) ? (hits > 0 ? "+" : "-")
                    : " ";
                lines.Add($"{gutter}{number,5}  {_sourceLines[i]}");

                if (language is null)
                {
                    rowSpans.Add([]);
                    continue;
                }

                spans.Clear();
                inComment = _highlighting.Tokenize(language, _sourceLines[i], inComment, spans);
                rowSpans.Add([.. spans.Select(span => span with { Start = span.Start + GutterWidth })]);
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
            rowSpans.Add([]);
        }

        _editorItems.ReplaceAll(lines);
        _editorListBox.RowSpans = rowSpans;
        _editorListBox.UpdateModel();
    }

    /// <summary>Rebuilds the output panel for its tab: the output log, the debug view, the coverage or the profile.</summary>
    private void RebuildOutputPanel()
    {
        var lines = new List<string>();
        lock (_stateLock)
        {
            switch (_outputTab)
            {
                case OutputTab.Output:
                    lines.AddRange(_output);
                    break;
                case OutputTab.Coverage:
                    lines.AddRange(CoverageLines());
                    break;
                case OutputTab.Profile:
                    lines.AddRange(ProfileLines());
                    break;
                default:
                    lines.AddRange(DebugLines());
                    break;
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        _outputItems.ReplaceAll(lines);
        _outputListBox.UpdateModel();
    }

    /// <summary>The coverage view: the total, then one row per file (the rows <see cref="OpenCoverageFileAtCursor"/> reads). Caller holds the lock.</summary>
    private List<string> CoverageLines()
    {
        var lines = new List<string>();
        _coverageFiles.Clear();
        if (_coverage is null)
        {
            lines.Add(UiText.Get("DevelopmentStudio.NoCoverage", "No coverage yet. O measures the coverage of the selected item."));
            return lines;
        }

        lines.Add(string.Format(UiText.Get("DevelopmentStudio.CoverageTotal", "Coverage {0:0.0}%: {1} of {2} lines in {3} files."),
            _coverage.Percent, _coverage.CoveredLines, _coverage.ValidLines, _coverage.Files.Count));
        string root = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        foreach (var file in _coverage.Files)
        {
            _coverageFiles.Add(file);
            string shown = Path.GetRelativePath(root, file.Path);
            lines.Add($"{file.Percent,6:0.0}%  {file.CoveredLines,5}/{file.ValidLines,-5}  {shown}");
        }

        return lines;
    }

    /// <summary>The profile view: the functions by cumulative time. Caller holds the lock.</summary>
    private List<string> ProfileLines()
    {
        var lines = new List<string>();
        if (_profile.Count == 0)
        {
            lines.Add(UiText.Get("DevelopmentStudio.NoProfile", "No profile yet. P profiles the selected Python script."));
            return lines;
        }

        lines.Add(UiText.Get("DevelopmentStudio.ProfileHeader", "     calls   total s   cumul. s  function"));
        foreach (var entry in _profile)
        {
            lines.Add($"{entry.Calls,10}  {entry.TotalSeconds,8:0.000}  {entry.CumulativeSeconds,8:0.000}  {entry.Function}");
        }

        return lines;
    }

    /// <summary>The debug view's lines; <see cref="_watchRows"/> maps the watch rows to their watch so Delete can remove one. Caller holds the lock.</summary>
    private List<string> DebugLines()
    {
        var lines = new List<string>();
        _watchRows.Clear();

        string state = _session is null
            ? UiText.Get("DevelopmentStudio.NotDebugging", "Not debugging.")
            : _paused
                ? string.Format(UiText.Get("DevelopmentStudio.Paused", "Stopped at {0}:{1}"), Path.GetFileName(_stopPath), _stopLine)
                : UiText.Get("DevelopmentStudio.RunningState", "Running.");
        lines.Add(state);
        lines.Add(string.Empty);

        lines.Add(UiText.Get("DevelopmentStudio.CallStack", "Call stack"));
        foreach (var frame in _stack)
        {
            lines.Add($"   {frame.Name}  {(frame.Path.Length == 0 ? string.Empty : Path.GetFileName(frame.Path) + ":" + frame.Line)}");
        }

        lines.Add(string.Empty);
        lines.Add(UiText.Get("DevelopmentStudio.Locals", "Locals"));
        foreach (var variable in _locals)
        {
            lines.Add($"   {variable.Name} = {variable.Value}");
        }

        lines.Add(string.Empty);
        lines.Add(UiText.Get("DevelopmentStudio.Watch", "Watch"));
        for (int i = 0; i < _watchExpressions.Count; i++)
        {
            string value = i < _watchValues.Count ? _watchValues[i].Value : UiText.Get("DevelopmentStudio.NotEvaluated", "(not evaluated)");
            _watchRows[lines.Count] = i;
            lines.Add($"   {_watchExpressions[i]} = {value}");
        }

        lines.Add(string.Empty);
        lines.Add(UiText.Get("DevelopmentStudio.Breakpoints", "Breakpoints"));
        foreach (var (path, line) in _breakpoints.All())
        {
            lines.Add($"   ● {Path.GetFileName(path)}:{line}");
        }

        return lines;
    }

    private void AppendOutput(string line)
    {
        bool diagnosed = false;
        lock (_stateLock)
        {
            _output.Add(line);
            if (_output.Count > MaxOutputLines)
            {
                _output.RemoveRange(0, _output.Count - MaxOutputLines);
            }

            if (BuildDiagnosticParser.Parse(line) is { } diagnostic && _diagnosticTexts.Add(diagnostic.Text) && _diagnostics.Count < MaxOutputLines)
            {
                _diagnostics.Add(diagnostic);
                diagnosed = true;
            }
        }

        if (_outputTab == OutputTab.Output) RebuildOutputPanel();
        if (diagnosed) RebuildErrorPanel();
    }

    private void SetStatus(string text, StatusSeverity severity)
    {
        _status = text;
        _statusSeverity = severity;
        StateHasChanged();
    }

    /// <summary>The folder the language server works in: the studio's folder, or the current one when there is none.</summary>
    private string ServerFolder => string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;

    /// <summary>
    /// Redraws the Error List and the gutter when the language server changed. The server is read here, on the UI thread, so
    /// its tasks never touch the controls.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        ApplySdkReload();
        long version = _languageServer.Version;
        if (version == _seenServerVersion) return;
        _seenServerVersion = version;
        RebuildEditor();
        RebuildErrorPanel();
        ShowServerStatus();
    }

    /// <summary>The status line while a C# file is open: starting, then back to the file name, or not installed (once per dialog).</summary>
    private void ShowServerStatus()
    {
        if (!_sourcePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        switch (_languageServer.State)
        {
            case LanguageServerState.Starting:
                _startingShown = true;
                SetStatus(UiText.Get("DevelopmentStudio.LanguageServerStarting", "Starting the C# language server..."), StatusSeverity.Info);
                break;
            case LanguageServerState.Ready when _startingShown:
                _startingShown = false;
                SetStatus(Path.GetFileName(_sourcePath), StatusSeverity.Info);
                break;
            case LanguageServerState.Missing when !_missingAnnounced:
                _missingAnnounced = true;
                SetStatus(UiText.Get("DevelopmentStudio.LanguageServerMissing", "csharp-ls was not found. Install it with dotnet tool install -g csharp-ls."), StatusSeverity.Info);
                break;
        }
    }

    /// <summary>The gutter mark of each line the server reported on: '!' when the line has an error, '~' for a warning only.</summary>
    private Dictionary<int, string> LiveMarks(string path)
    {
        var marks = new Dictionary<int, string>();
        foreach (LiveDiagnostic diagnostic in _languageServer.DiagnosticsFor(path))
        {
            if (diagnostic.IsError || !marks.ContainsKey(diagnostic.Line))
            {
                marks[diagnostic.Line] = diagnostic.IsError ? "!" : "~";
            }
        }

        return marks;
    }

    /// <summary>
    /// The Error List's rows: the errors and warnings the commands printed, then the live ones of the server. A live diagnostic
    /// that repeats a build one (same file, line and message) is left out, so a compile error is listed once.
    /// </summary>
    private List<BuildDiagnostic> MergedDiagnostics()
    {
        List<BuildDiagnostic> build;
        lock (_stateLock) build = [.. _diagnostics];

        string folder = ServerFolder;
        var merged = new List<BuildDiagnostic>(build);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (BuildDiagnostic diagnostic in build)
        {
            seen.Add(DiagnosticKey(FullPathOf(diagnostic.File, folder), diagnostic.Line, diagnostic.Message));
        }

        foreach (LiveDiagnostic live in _languageServer.AllDiagnostics())
        {
            if (!seen.Add(DiagnosticKey(live.Path, live.Line, live.Message))) continue;
            string severity = live.IsError ? "error" : "warning";
            merged.Add(new BuildDiagnostic(live.IsError, live.Path, live.Line, live.Column, live.Message,
                $"{live.Path}({live.Line},{live.Column}): {severity} {live.Code}: {live.Message}"));
        }

        return merged;
    }

    private static string DiagnosticKey(string path, int line, string message) => $"{path.ToLowerInvariant()}|{line}|{message}";

    private static string FullPathOf(string file, string folder)
    {
        try
        {
            return Path.GetFullPath(file, folder);
        }
        catch (ArgumentException)
        {
            return file;
        }
    }

    /// <summary>Opens a source file in the editor. The read runs on a background task; the newest request wins.</summary>
    private void OpenSource(string path, int line = 1)
    {
        _sourceRequest = path;
        _ = Task.Run(() =>
        {
            string[] lines;
            string? error = null;
            try
            {
                if (new FileInfo(path).Length > MaxSourceBytes)
                {
                    lines = [];
                    error = UiText.Get("DevelopmentStudio.TooLarge", "The file is too large to show here.");
                }
                else
                {
                    lines = File.ReadAllLines(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lines = [];
                error = exception.Message;
            }

            lock (_stateLock)
            {
                if (_sourceRequest != path) return;
                _sourcePath = path;
                _sourceLines.Clear();
                _sourceLines.AddRange(lines);
            }

            if (error is null && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                _languageServer.OpenFile(ServerFolder, path, string.Join('\n', lines));
            }

            RebuildView();
            _editorListBox.ScrollableModel.CursorPosition.Y = Math.Clamp(line - 1, 0, Math.Max(0, lines.Length - 1));
            SetStatus(error ?? Path.GetFileName(path), error is null ? StatusSeverity.Info : StatusSeverity.Error);
        }, CancellationToken.None);
    }

    /// <summary>Columns before the text of a source row: the gutter mark, a five-column line number and two spaces.</summary>
    private const int GutterWidth = 8;

    /// <summary>The 1-based line under the source cursor, or 0 when the source view is not showing a file.</summary>
    private int CursorLine
    {
        get
        {
            var rows = _editorListBox.ListModel.ListItems;
            var index = (int)_editorListBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < rows.Count && rows.Count > 0 ? index + 1 : 0;
        }
    }

    #endregion

    #region Breakpoints and watches

    private void ToggleBreakpointAtCursor()
    {
        string path;
        int line;
        int sourceCount;
        lock (_stateLock)
        {
            path = _sourcePath;
            line = CursorLine;
            sourceCount = _sourceLines.Count;
        }

        if (path.Length == 0 || line < 1 || line > sourceCount)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NoBreakpointTarget", "Open a file (Enter) and put the cursor on a line to set a breakpoint."), StatusSeverity.Info);
            return;
        }

        bool set = _breakpoints.Toggle(path, line);
        RebuildView();
        SetStatus(string.Format(set
            ? UiText.Get("DevelopmentStudio.BreakpointSet", "Breakpoint set at line {0}.")
            : UiText.Get("DevelopmentStudio.BreakpointRemoved", "Breakpoint removed from line {0}."), line), StatusSeverity.Info);

        // While a program runs, the adapter gets the file's new list at once; a breakpoint set between sessions goes at the start.
        DebugSession? session = _session;
        if (session is not null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await session.SetBreakpointsAsync(path, _breakpoints.LinesIn(path));
                }
                catch (Exception exception) when (exception is DapException or OperationCanceledException or InvalidOperationException)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.BreakpointFailed", "The debugger did not take the breakpoint: {0}"), exception.Message), StatusSeverity.Error);
                }
            }, CancellationToken.None);
        }
    }

    /// <summary>W in the source view: adds the word under the cursor to the watch list.</summary>
    private void ToggleWatchAtCursor()
    {
        // W on the debug tab, with the output panel focused, removes the watch on its row; from the editor it adds one.
        if (_outputTab == OutputTab.Debug && _outputListBox.IsFocused)
        {
            RemoveWatchAtCursor();
            return;
        }

        string? word = WordUnderCursor();
        if (word is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NoWord", "Put the cursor on a name to watch it."), StatusSeverity.Info);
            return;
        }

        lock (_stateLock)
        {
            if (!_watchExpressions.Contains(word)) _watchExpressions.Add(word);
        }

        SetStatus(string.Format(UiText.Get("DevelopmentStudio.Watching", "Watching {0}."), word), StatusSeverity.Info);
        if (_session is not null && _paused) _ = RefreshWatchesAsync();
        RebuildView();
    }

    /// <summary>Delete in the debug tab, with the output panel focused: removes the watch on the cursor row.</summary>
    private void RemoveWatchAtCursor()
    {
        if (_outputTab != OutputTab.Debug || !_outputListBox.IsFocused) return;
        lock (_stateLock)
        {
            int row = (int)_outputListBox.ScrollableModel.CursorPosition.Y;
            if (!_watchRows.TryGetValue(row, out int watch)) return;
            _watchExpressions.RemoveAt(watch);
            if (watch < _watchValues.Count) _watchValues.RemoveAt(watch);
        }

        RebuildView();
    }

    private string? WordUnderCursor()
    {
        lock (_stateLock)
        {
            int line = CursorLine;
            if (line < 1 || line > _sourceLines.Count) return null;
            string text = _sourceLines[line - 1];
            // The rows are "<gutter><line number,5>  <text>": eight columns before the text, so the cursor's column is shifted back by them.
            int column = Math.Clamp((int)_editorListBox.ScrollableModel.CursorPosition.X - GutterWidth, 0, text.Length);
            int start = column, end = column;
            while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
            return end > start ? text[start..end] : null;
        }
    }

    #endregion

    #region Commands (build, clean, test, run)

    /// <summary>
    /// O: measures the coverage of the cursor item. The toolchain's commands run in order, each writing into a temporary
    /// results folder; the Cobertura report they leave is read into the coverage view and the source gutter.
    /// </summary>
    private void RunCoverage()
    {
        var item = SelectedItem;
        if (item is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectItem", "Select an item first."), StatusSeverity.Info);
            return;
        }

        if (_busy)
        {
            SetStatus(UiText.Get("DevelopmentStudio.BusyCoverage", "A command is still running; X cancels it before measuring coverage."), StatusSeverity.Info);
            return;
        }

        var toolchain = ToolchainFor(item);
        string resultsFolder = Path.Combine(Path.GetTempPath(), "DevelopmentStudio-coverage-" + Guid.NewGuid().ToString("N"));
        var commands = toolchain.Coverage(item, resultsFolder);
        if (commands.Count == 0)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.NoCoverageFor", "{0} has no coverage measurement here."), LanguageLabel(item.Language)), StatusSeverity.Info);
            return;
        }

        Directory.CreateDirectory(resultsFolder);
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _busy = true;
        lock (_stateLock) ClearOutputLocked();
        SetStatus(string.Format(UiText.Get("DevelopmentStudio.RunningCoverage", "Measuring the coverage of {0}..."), item.Name), StatusSeverity.Info);
        _outputTab = OutputTab.Output;
        RebuildView();

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var command in commands)
                {
                    if (cts.IsCancellationRequested) break;
                    AppendOutput($"$ {command.Display}");
                    // A failing test run still leaves a report for what did run, so the commands go on regardless.
                    await _runner.RunAsync(command, AppendOutput, cts.Token);
                }

                string? report = toolchain.FindCoverageReport(resultsFolder);
                if (cts.IsCancellationRequested)
                {
                    SetStatus(UiText.Get("DevelopmentStudio.CoverageCancelled", "Coverage measurement cancelled."), StatusSeverity.Info);
                }
                else if (report is null)
                {
                    SetStatus(UiText.Get("DevelopmentStudio.NoCoverageReport", "The run left no coverage report. Check the output."), StatusSeverity.Error);
                }
                else
                {
                    var coverage = CoberturaParser.Parse(await File.ReadAllTextAsync(report, cts.Token));
                    lock (_stateLock)
                    {
                        _coverage = coverage;
                        _outputTab = OutputTab.Coverage;
                    }

                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.CoverageSummary", "Coverage {0:0.0}%: {1} of {2} lines in {3} files."),
                        coverage.Percent, coverage.CoveredLines, coverage.ValidLines, coverage.Files.Count), StatusSeverity.Success);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or OperationCanceledException or InvalidOperationException)
            {
                SetStatus(exception.Message, StatusSeverity.Error);
            }
            finally
            {
                // The view is rebuilt before the run is marked finished, so anyone who waits for it sees the result.
                DeleteTemporaryFolder(resultsFolder);
                RebuildView();
                _busy = false;
                _runCts = null;
                StateHasChanged();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// P: profiles the cursor item with the toolchain's profiler (cProfile for Python), then reads the stats back as the
    /// profile view's rows of functions by cumulative time.
    /// </summary>
    private void RunProfile()
    {
        var item = SelectedItem;
        if (item is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectItem", "Select an item first."), StatusSeverity.Info);
            return;
        }

        if (_busy)
        {
            SetStatus(UiText.Get("DevelopmentStudio.BusyProfile", "A command is still running; X cancels it before profiling."), StatusSeverity.Info);
            return;
        }

        var toolchain = ToolchainFor(item);
        string folder = Path.Combine(Path.GetTempPath(), "DevelopmentStudio-profile-" + Guid.NewGuid().ToString("N"));
        string profilePath = Path.Combine(folder, "profile.prof");
        var command = toolchain.Profile(item, profilePath);
        var summary = toolchain.ProfileSummary(item, profilePath);
        if (command is null || summary is null)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.NoProfilerFor", "{0} has no profiler here."), LanguageLabel(item.Language)), StatusSeverity.Info);
            return;
        }

        Directory.CreateDirectory(folder);
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _busy = true;
        lock (_stateLock) ClearOutputLocked();
        SetStatus(string.Format(UiText.Get("DevelopmentStudio.RunningProfile", "Profiling {0}..."), item.Name), StatusSeverity.Info);
        _outputTab = OutputTab.Output;
        RebuildView();

        _ = Task.Run(async () =>
        {
            try
            {
                AppendOutput($"$ {command.Display}");
                await _runner.RunAsync(command, AppendOutput, cts.Token);
                if (!File.Exists(profilePath))
                {
                    SetStatus(UiText.Get("DevelopmentStudio.NoProfileWritten", "The profiler wrote no profile. Check the output."), StatusSeverity.Error);
                    return;
                }

                var lines = new List<string>();
                await _runner.RunAsync(summary, line => { lock (lines) lines.Add(line); }, cts.Token);
                var entries = PythonProfileParser.Parse(lines);
                lock (_stateLock)
                {
                    _profile = entries;
                    _outputTab = OutputTab.Profile;
                }

                var hottest = entries.OrderByDescending(entry => entry.CumulativeSeconds).FirstOrDefault();
                SetStatus(hottest is null
                    ? UiText.Get("DevelopmentStudio.ProfileEmpty", "The profile has no functions.")
                    : string.Format(UiText.Get("DevelopmentStudio.ProfileSummary", "Profiled {0}: {1} functions. Most time: {2}."), item.Name, entries.Count, hottest.Function),
                    StatusSeverity.Success);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
            {
                SetStatus(exception.Message, StatusSeverity.Error);
            }
            finally
            {
                DeleteTemporaryFolder(folder);
                RebuildView();
                _busy = false;
                _runCts = null;
                StateHasChanged();
            }
        }, CancellationToken.None);
    }

    /// <summary>Deletes a temporary results folder; a folder that cannot be deleted is left for the system's temp cleanup.</summary>
    private static void DeleteTemporaryFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the system's temp cleanup; the result has already been read.
        }
    }

    /// <summary>Runs one command on the checked projects, or on the cursor item when nothing is checked.</summary>
    private void RunSelected(string verb)
    {
        var targets = ResolveTargets();
        if (targets.Count == 0)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectItem", "Select an item first."), StatusSeverity.Info);
            return;
        }

        RunCommand(targets, verb);
    }

    /// <summary>Ctrl+F5: runs the Main program when one is set, whatever is checked or under the cursor; otherwise the same as R.</summary>
    private void RunMainOrSelected(string verb)
    {
        if (MainItem is not { } main)
        {
            RunSelected(verb);
            return;
        }

        RunCommand([main], verb);
    }

    /// <summary>The checked projects in explorer order, or the cursor item when none is checked (empty on a heading row).</summary>
    private IReadOnlyList<DevelopmentProject> ResolveTargets()
    {
        if (_checked.Count > 0)
        {
            return _items.Where(item => _checked.Contains(item.Path)).ToList();
        }

        var item = SelectedItem;
        return item is null ? [] : [item];
    }

    /// <summary>The toolchain of an item. A .NET item uses the SDK chosen with V, so each command runs on that SDK's host.</summary>
    private IDevelopmentToolchain ToolchainFor(DevelopmentProject item)
    {
        if (item.Language == DevelopmentLanguage.DotNet && SelectedSdk is { } sdk) return new DotNetToolchain { Host = sdk.HostPath };
        if (item.Language == DevelopmentLanguage.Java && _javaHome is not null) return new JavaToolchain { JavaHome = _javaHome };
        return _toolchains.For(item);
    }

    /// <summary>
    /// Runs the verb on each target, one after another, in one run: the output of all of them is one log, and a test run's
    /// totals are summed over the targets. A target without that command is noted and skipped.
    /// </summary>
    private void RunCommand(IReadOnlyList<DevelopmentProject> targets, string verb)
    {
        if (_busy)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.Busy", "A command is still running; X cancels it ({0} was not started)."), verb), StatusSeverity.Info);
            return;
        }

        var plan = new List<(DevelopmentProject Item, ProcessCommand Command)>();
        var cleanable = new List<DevelopmentProject>();
        var notes = new List<string>();
        foreach (var item in targets)
        {
            var toolchain = ToolchainFor(item);
            ProcessCommand? command = verb switch
            {
                "build" => toolchain.Build(item),
                "clean" => toolchain.Clean(item),
                "test" => toolchain.Test(item),
                _ => toolchain.Run(item),
            };

            if (command is not null)
            {
                plan.Add((item, command));
            }
            else if (verb == "clean" && toolchain.CacheFolders(item).Count > 0)
            {
                cleanable.Add(item);
            }
            else
            {
                notes.Add(string.Format(UiText.Get("DevelopmentStudio.NoCommand", "{0} cannot {1} {2}."), LanguageLabel(item.Language), verb, item.Name));
            }
        }

        if (plan.Count == 0)
        {
            foreach (var item in cleanable) CleanCacheFolders(item, ToolchainFor(item).CacheFolders(item));
            if (cleanable.Count == 0)
            {
                SetStatus(notes.Count > 0 ? string.Join(" ", notes) : UiText.Get("DevelopmentStudio.SelectItem", "Select an item first."), StatusSeverity.Info);
            }

            return;
        }

        string targetLabel = plan.Count == 1 ? plan[0].Item.Name : string.Format(UiText.Get("DevelopmentStudio.ProjectCount", "{0} projects"), plan.Count);
        var cts = new CancellationTokenSource();
        _runCts = cts;
        _busy = true;
        lock (_stateLock)
        {
            ClearOutputLocked();
        }

        SetStatus(string.Format(UiText.Get("DevelopmentStudio.Running", "Running {0} {1}..."), verb, targetLabel), StatusSeverity.Info);
        _outputTab = OutputTab.Output;
        RebuildView();

        _ = Task.Run(async () =>
        {
            int firstFailure = 0;
            var runTotals = TestTotals.Empty;
            try
            {
                foreach (var (item, command) in plan)
                {
                    if (cts.IsCancellationRequested) break;
                    int start;
                    lock (_stateLock) start = _output.Count;

                    string sdkNote = item.Language == DevelopmentLanguage.DotNet && SelectedSdk is { } sdk
                        ? string.Format(UiText.Get("DevelopmentStudio.SdkNote", "   (.NET SDK {0})"), sdk.Version)
                        : string.Empty;
                    AppendOutput($"$ {command.Display}{sdkNote}");
                    int exitCode = await _runner.RunAsync(command, AppendOutput, cts.Token);
                    if (exitCode != 0 && firstFailure == 0) firstFailure = exitCode;

                    if (verb == "test")
                    {
                        List<string> segment;
                        lock (_stateLock) segment = _output.Skip(start).ToList();
                        var totals = TestOutputParser.Parse(segment);
                        _testOutcomes[item.Path] = new TestOutcome(exitCode == 0 && totals.Failed == 0, totals);
                        runTotals = runTotals.Add(totals);
                        RebuildExplorer();
                    }

                    AppendOutput(string.Empty);
                }

                if (cts.IsCancellationRequested)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.Cancelled", "{0} cancelled."), verb), StatusSeverity.Info);
                }
                else if (verb == "test" && runTotals.Total > 0)
                {
                    string summary = string.Format(UiText.Get("DevelopmentStudio.TestTotals", "Tests: {0} passed, {1} failed, {2} skipped."), runTotals.Passed, runTotals.Failed, runTotals.Skipped);
                    AppendOutput(summary);
                    SetStatus(summary, runTotals.Failed == 0 && firstFailure == 0 ? StatusSeverity.Success : StatusSeverity.Error);
                }
                else if (firstFailure == 0)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.Succeeded", "{0} succeeded: {1}."), verb, targetLabel), StatusSeverity.Success);
                }
                else
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.FailedExit", "{0} failed (exit {1})."), verb, firstFailure), StatusSeverity.Error);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or IOException)
            {
                SetStatus(exception.Message, StatusSeverity.Error);
            }
            finally
            {
                _busy = false;
                _runCts = null;
                RebuildView();
                StateHasChanged();
            }
        }, CancellationToken.None);
    }

    /// <summary>Re-scans the folder. The Test Explorer keeps the results of the items still there.</summary>
    private async Task RefreshAsync()
    {
        _fileTreeBuilt = false;
        _treeGeneration++;
        _projectFiles.Clear();
        _loadingProjects.Clear();
        _items = await ScanAsync();
        OpenSolutionsInTree();
        RebuildExplorer();
        SetStatus(string.Format(UiText.Get("DevelopmentStudio.Refreshed", "Found {0} items."), _items.Count), StatusSeverity.Info);
    }

    /// <summary>The items to list: the folder's, or, when the dialog was opened on a solution, that solution and the projects it lists.</summary>
    private async Task<IReadOnlyList<DevelopmentProject>> ScanAsync()
    {
        string folder = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        var all = await Task.Run(() => _scanner.Scan(folder).ToList(), CancellationToken.None);
        if (string.IsNullOrEmpty(_solutionPath)) return all;

        var projectPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string solutionPath = _solutionPath;
            var solution = await Task.Run(() => _dotnet.ParseSolution(solutionPath), CancellationToken.None);
            projectPaths = solution.Projects.Select(project => project.AbsolutePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException)
        {
            SetStatus(string.Format(UiText.Get("Solution.ReadFailed", "Failed to read solution: {0}"), exception.Message), StatusSeverity.Error);
        }

        return all.Where(item => item.Path.Equals(_solutionPath, StringComparison.OrdinalIgnoreCase) || projectPaths.Contains(item.Path)).ToList();
    }

    /// <summary>Puts the explorer cursor on the row of the solution the dialog was opened on. Call it after the tree is built.</summary>
    private void SelectSolutionRow()
    {
        if (string.IsNullOrEmpty(_solutionPath)) return;
        int index = _explorerItems.ToList().FindIndex(row => row.Item is { Kind: DevelopmentProjectKind.Solution } item
            && item.Path.Equals(_solutionPath, StringComparison.OrdinalIgnoreCase));
        _explorerListBox.ScrollableModel.CursorPosition.Y = Math.Max(0, index);
    }

    /// <summary>Space: checks or unchecks the .NET project under the cursor.</summary>
    private void ToggleCheckAtCursor()
    {
        var item = SelectedItem;
        if (item is not { Kind: DevelopmentProjectKind.DotNetProject })
        {
            SetStatus(UiText.Get("DevelopmentStudio.OnlyCheckProjects", "Only .NET projects can be checked."), StatusSeverity.Info);
            return;
        }

        if (!_checked.Remove(item.Path)) _checked.Add(item.Path);
        RebuildExplorer();
        StateHasChanged();
    }

    /// <summary>A: checks every .NET project, or clears them when all are checked already.</summary>
    private void CheckAllProjects() => CheckProjects(_items.Where(item => item.Kind == DevelopmentProjectKind.DotNetProject).ToList());

    /// <summary>Shift+T: checks every .NET test project, or clears them when all are checked already.</summary>
    private void CheckAllTestProjects()
    {
        var tests = _items.Where(item => item.Kind == DevelopmentProjectKind.DotNetProject && item.IsTest).ToList();
        if (tests.Count == 0)
        {
            SetStatus(UiText.Get("Solution.NoTests", "No test projects in this solution."), StatusSeverity.Info);
            return;
        }

        CheckProjects(tests);
    }

    private void CheckProjects(IReadOnlyList<DevelopmentProject> projects)
    {
        if (projects.Count == 0)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NoProjectsToCheck", "No .NET projects to check."), StatusSeverity.Info);
            return;
        }

        if (projects.All(project => _checked.Contains(project.Path)))
        {
            foreach (var project in projects) _checked.Remove(project.Path);
        }
        else
        {
            foreach (var project in projects) _checked.Add(project.Path);
        }

        RebuildExplorer();
        StateHasChanged();
    }

    /// <summary>The .NET SDKs the system lists and the ones the SDK manager installed, one per host, newest first. Runs a process, so it is never called on the UI thread.</summary>
    private IReadOnlyList<DotnetSdk> ReadSdks() =>
        DotnetSdk.Newest(_dotnet.ListSdks().Concat(_sdkManager.InstalledDotNetSdks()).DistinctBy(sdk => sdk.HostPath, StringComparer.OrdinalIgnoreCase));

    /// <summary>Sets the SDK list; the SDK that was selected stays selected when it is still there, else the default one is.</summary>
    private void ApplySdks(IReadOnlyList<DotnetSdk> sdks, DotnetSdk? keep)
    {
        DotnetSdk? chosen = keep is null ? null : sdks.FirstOrDefault(sdk => sdk.HostPath.Equals(keep.HostPath, StringComparison.OrdinalIgnoreCase));
        chosen ??= DotnetSdk.Default(sdks);
        _sdks = sdks;
        _sdkIndex = chosen is null ? -1 : sdks.ToList().IndexOf(chosen);
    }

    /// <summary>K: the SDK manager. When it closes, the SDKs and the chosen JDK are read again.</summary>
    private async Task OpenSdkManagerAsync()
    {
        DotnetSdk? previous = SelectedSdk;
        var manager = new SdkManagerDialog(_sdkManager, ThemeService)
        {
            Closed = () => RequestSdkReload(previous),
        };
        await ShowDialogAsync(manager);
    }

    /// <summary>Reads the SDKs and the JDK choice on a background task; <see cref="ApplySdkReload"/> applies them on the UI thread.</summary>
    private void RequestSdkReload(DotnetSdk? keep)
    {
        _ = Task.Run(() =>
        {
            var sdks = ReadSdks();
            string? javaHome = _sdkManager.ActiveJavaHome;
            lock (_sdkGate)
            {
                _sdkReload = new SdkReload(keep, sdks, javaHome);
            }
        });
    }

    private void ApplySdkReload()
    {
        SdkReload? reload;
        lock (_sdkGate)
        {
            reload = _sdkReload;
            _sdkReload = null;
        }

        if (reload is not { } answer) return;
        ApplySdks(answer.Sdks, answer.Keep);
        _javaHome = answer.JavaHome;
        RebuildView();
    }

    /// <summary>A reload of the SDK list: the selection to keep, the list read and the JDK chosen at that time.</summary>
    private sealed record SdkReload(DotnetSdk? Keep, IReadOnlyList<DotnetSdk> Sdks, string? JavaHome);

    /// <summary>V: the next installed .NET SDK. A command already running keeps the SDK it started with.</summary>
    private void CycleSdk()
    {
        if (_sdks.Count == 0)
        {
            SetStatus(UiText.Get("Solution.NoSdk", "No .NET SDK was found; commands run with the dotnet on the PATH."), StatusSeverity.Info);
            return;
        }

        if (_sdks.Count == 1)
        {
            SetStatus(string.Format(UiText.Get("Solution.OnlySdk", "Only .NET SDK {0} is installed."), SelectedSdk!.Version), StatusSeverity.Info);
            return;
        }

        _sdkIndex = (_sdkIndex + 1) % _sdks.Count;
        var chosen = SelectedSdk!;
        string version = chosen.IsPrerelease ? string.Format(UiText.Get("Solution.SdkPreview", "{0} (preview)"), chosen.Version) : chosen.Version;
        SetStatus(string.Format(UiText.Get("Solution.UsingSdk", "Using .NET SDK {0}."), version), StatusSeverity.Info);
    }

    /// <summary>S: writes a CycloneDX SBOM of the solution (the cursor's, or the first in the list) beside it. Off the UI thread.</summary>
    private void WriteSbom()
    {
        var solution = SelectedItem is { Kind: DevelopmentProjectKind.Solution } selected ? selected : _items.FirstOrDefault(item => item.Kind == DevelopmentProjectKind.Solution);
        if (solution is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NoSolutionForSbom", "There is no solution here to write an SBOM for."), StatusSeverity.Info);
            return;
        }

        SetStatus(UiText.Get("DevelopmentStudio.SbomWriting", "Writing the SBOM..."), StatusSeverity.Info);
        var format = _sbomFormat;
        string solutionPath = solution.Path;
        _ = Task.Run(() =>
        {
            string message;
            var severity = StatusSeverity.Success;
            try
            {
                var result = _dotnet.GenerateSbom(_dotnet.ParseSolution(solutionPath), format);
                if (result.Path.Length == 0)
                {
                    message = UiText.Get("Solution.SbomNoAssets", "No project.assets.json found: build or restore the solution first.");
                    severity = StatusSeverity.Error;
                }
                else
                {
                    message = string.Format(UiText.Get("Solution.SbomWritten", "Wrote an SBOM of {0} packages to {1}."), result.ComponentCount, result.Path);
                    if (result.ProjectsWithoutAssets > 0)
                    {
                        message += " " + string.Format(UiText.Get("Solution.SbomMissingSome", "{0} projects had no assets file and are left out."), result.ProjectsWithoutAssets);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException or FormatException or InvalidOperationException)
            {
                message = string.Format(UiText.Get("Solution.SbomFailed", "SBOM failed: {0}"), exception.Message);
                severity = StatusSeverity.Error;
            }

            SetStatus(message, severity);
        }, CancellationToken.None);
    }

    /// <summary>F: switches the SBOM between XML and JSON. Takes effect on the next S.</summary>
    private void ToggleSbomFormat()
    {
        _sbomFormat = _sbomFormat == SbomFormat.Xml ? SbomFormat.Json : SbomFormat.Xml;
        SetStatus(string.Format(UiText.Get("Solution.SbomFormat", "SBOM format: {0}."), _sbomFormat == SbomFormat.Json ? "JSON" : "XML"), StatusSeverity.Info);
    }

    /// <summary>Clean of a script: deletes its cache folders (there is no clean command for Python).</summary>
    private void CleanCacheFolders(DevelopmentProject item, IReadOnlyList<string> folders)
    {
        _ = Task.Run(() =>
        {
            int removed = 0;
            foreach (string folder in folders)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    removed++;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    SetStatus(exception.Message, StatusSeverity.Error);
                    return;
                }
            }

            SetStatus(string.Format(UiText.Get("DevelopmentStudio.Cleaned", "Removed {0} cache folders of {1}."), removed, item.Name), StatusSeverity.Success);
        }, CancellationToken.None);
    }

    #endregion

    #region Debugging

    /// <summary>F5: starts debugging the Main program (or the selected item when none is set), or continues the program when it is stopped.</summary>
    private void StartOrContinueDebugging()
    {
        if (_session is not null)
        {
            if (_paused) DebugStep(session => session.ContinueAsync());
            else SetStatus(UiText.Get("DevelopmentStudio.AlreadyRunning", "The program is running. It stops at a breakpoint."), StatusSeverity.Info);
            return;
        }

        var item = MainItem ?? SelectedItem;
        if (item is null || item.Kind == DevelopmentProjectKind.Solution)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectToDebug", "Select a project or a script to debug. A solution cannot be debugged."), StatusSeverity.Info);
            return;
        }

        if (_busy)
        {
            SetStatus(UiText.Get("DevelopmentStudio.BusyDebug", "A command is still running; X cancels it before debugging."), StatusSeverity.Info);
            return;
        }

        _ = StartDebuggingAsync(item, debugTests: _explorerMode == ExplorerMode.Tests);
    }

    private async Task StartDebuggingAsync(DevelopmentProject item, bool debugTests)
    {
        _busy = true;
        var cts = new CancellationTokenSource();
        _runCts = cts;
        try
        {
            var toolchain = _toolchains.For(item);
            string? assembly = null;
            if (item.Language == DevelopmentLanguage.DotNet)
            {
                // A .NET program is built first, so the debugger starts the DLL that matches the source.
                var build = toolchain.Build(item)!;
                lock (_stateLock) ClearOutputLocked();
                RebuildErrorPanel();
                AppendOutput($"$ {build.Display}");
                int buildExit = await _runner.RunAsync(build, AppendOutput, cts.Token);
                if (buildExit != 0)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.BuildFailedNoDebug", "The build failed, so {0} was not started."), item.Name), StatusSeverity.Error);
                    _outputTab = OutputTab.Output;
                    return;
                }

                assembly = toolchain.FindBuiltAssembly(item);
            }

            var plan = toolchain.PlanDebug(item, assembly, debugTests);
            if (plan is null)
            {
                SetStatus(string.Format(UiText.Get("DevelopmentStudio.NoDebugger", "Debugging {0} is not available for {1} here."), item.Name, LanguageLabel(item.Language)), StatusSeverity.Error);
                return;
            }

            DebugSession session = DebugSession.Start(plan, item.Directory, OnDebugNotification);
            lock (_stateLock)
            {
                _session = session;
                _stack = [];
                _locals.Clear();
                _watchValues.Clear();
            }

            var breakpoints = _breakpoints.Files().ToDictionary(file => file, file => _breakpoints.LinesIn(file), StringComparer.OrdinalIgnoreCase);
            AppendOutput($"Debugging {item.Name} with {plan.FileName}");
            await session.LaunchAsync(plan.Launch, breakpoints, cts.Token);
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.Started", "Debugging {0}. F5 continues, F10 steps over."), item.Name), StatusSeverity.Success);
            _outputTab = OutputTab.Debug;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DapException or OperationCanceledException or IOException)
        {
            DebugSession? failed;
            lock (_stateLock)
            {
                failed = _session;
                _session = null;
            }

            if (failed is not null) await failed.DisposeAsync();
            SetStatus(exception.Message, StatusSeverity.Error);
        }
        finally
        {
            _busy = false;
            _runCts = null;
            RebuildView();
            StateHasChanged();
        }
    }

    /// <summary>Runs one step (or continue, pause) on the session, off the UI thread. A failure is shown on the status line.</summary>
    private void DebugStep(Func<DebugSession, Task> step)
    {
        DebugSession? session = _session;
        if (session is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NotDebugging", "Not debugging. F5 starts it."), StatusSeverity.Info);
            return;
        }

        if (!_paused)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NotStopped", "The program is running. It stops at a breakpoint."), StatusSeverity.Info);
            return;
        }

        _paused = false;
        _ = Task.Run(async () =>
        {
            try
            {
                await step(session);
            }
            catch (Exception exception) when (exception is DapException or OperationCanceledException or InvalidOperationException)
            {
                SetStatus(exception.Message, StatusSeverity.Error);
            }
        }, CancellationToken.None);
    }

    /// <summary>Shift+F5: ends the debugged program and the adapter.</summary>
    private void StopDebugging()
    {
        DebugSession? session;
        lock (_stateLock)
        {
            session = _session;
            _session = null;
            _paused = false;
            _stack = [];
            _locals.Clear();
            _watchValues.Clear();
        }

        if (session is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NotDebugging", "Not debugging. F5 starts it."), StatusSeverity.Info);
            return;
        }

        _ = Task.Run(async () =>
        {
            await session.TerminateAsync();
            await session.DisposeAsync();
        }, CancellationToken.None);
        SetStatus(UiText.Get("DevelopmentStudio.Stopped", "Debugging stopped."), StatusSeverity.Info);
        RebuildView();
        StateHasChanged();
    }

    /// <summary>Events from the adapter, on its reader thread. Stopping reads the stack in the background; the rest updates state.</summary>
    private void OnDebugNotification(IDebugNotification notification)
    {
        switch (notification)
        {
            case DebugStoppedNotification stopped:
                _paused = true;
                _ = Task.Run(() => RefreshStopAsync(stopped.Stop), CancellationToken.None);
                break;
            case DebugContinuedNotification:
                _paused = false;
                lock (_stateLock)
                {
                    _stack = [];
                    _locals.Clear();
                    _watchValues.Clear();
                }
                SetStatus(UiText.Get("DevelopmentStudio.RunningState", "Running."), StatusSeverity.Info);
                break;
            case DebugOutputNotification output:
                foreach (string line in output.Text.Replace("\r", string.Empty).Split('\n'))
                {
                    if (line.Length > 0) AppendOutput(line);
                }
                break;
            case DebugEndedNotification ended:
                DebugSession? session;
                lock (_stateLock)
                {
                    session = _session;
                    _session = null;
                    _paused = false;
                    _stack = [];
                    _locals.Clear();
                    _watchValues.Clear();
                }

                AppendOutput(ended.Reason);
                SetStatus(ended.Reason, StatusSeverity.Info);
                if (session is not null) _ = Task.Run(() => session.DisposeAsync().AsTask(), CancellationToken.None);
                RebuildView();
                break;
        }
    }

    /// <summary>Re-evaluates the watches of the stopped program, without moving the view. Used when a watch is added at a stop.</summary>
    private async Task RefreshWatchesAsync()
    {
        DebugSession? session = _session;
        if (session is null) return;
        try
        {
            var frames = await session.StackTraceAsync();
            int frameId = frames.FirstOrDefault()?.Id ?? 0;
            List<string> expressions;
            lock (_stateLock) expressions = [.. _watchExpressions];
            var values = new List<DebugVariable>();
            foreach (string expression in expressions)
            {
                values.Add(await session.EvaluateAsync(expression, frameId));
            }

            lock (_stateLock)
            {
                _watchValues.Clear();
                _watchValues.AddRange(values);
            }

            RebuildView();
        }
        catch (Exception exception) when (exception is DapException or OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
            SetStatus(exception.Message, StatusSeverity.Error);
        }
    }

    /// <summary>Reads the stack, the locals and the watches at a stop, then shows the stopped line in the source view.</summary>
    private async Task RefreshStopAsync(DebugStop stop)
    {
        DebugSession? session = _session;
        if (session is null) return;
        try
        {
            IReadOnlyList<DebugStackFrame> frames = await session.StackTraceAsync();
            DebugStackFrame? top = frames.FirstOrDefault();
            var locals = new List<DebugVariable>();
            var watchValues = new List<DebugVariable>();
            if (top is not null)
            {
                foreach (var scope in await session.ScopesAsync(top.Id))
                {
                    if (scope.Name.Contains("Local", StringComparison.OrdinalIgnoreCase) || scope.Name.Length == 0)
                    {
                        locals.AddRange(await session.VariablesAsync(scope.Reference));
                    }
                }
            }

            List<string> expressions;
            lock (_stateLock) expressions = [.. _watchExpressions];
            foreach (string expression in expressions)
            {
                watchValues.Add(await session.EvaluateAsync(expression, top?.Id ?? 0));
            }

            bool openFile = top is not null && top.Path.Length > 0 && File.Exists(top.Path)
                && !top.Path.Equals(_sourcePath, StringComparison.OrdinalIgnoreCase);
            lock (_stateLock)
            {
                _stack = frames;
                _locals.Clear();
                _locals.AddRange(locals);
                _watchValues.Clear();
                _watchValues.AddRange(watchValues);
                _stopPath = top?.Path ?? string.Empty;
                _stopLine = top?.Line ?? 0;
                _outputTab = OutputTab.Debug;
            }

            if (openFile) OpenSourceAndWait(top!.Path);
            RebuildView();
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.StoppedAt", "Stopped ({0}) at {1}:{2}."), stop.Reason, top is null ? "?" : Path.GetFileName(top.Path), top?.Line ?? 0), StatusSeverity.Info);
            if (_stopLine > 0) _editorListBox.ScrollableModel.CursorPosition.Y = _stopLine - 1;
        }
        catch (Exception exception) when (exception is DapException or OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.StackFailed", "Could not read the program's state: {0}"), exception.Message), StatusSeverity.Error);
        }
    }

    /// <summary>Loads the stopped file synchronously; a stop is not a key handler, so the short read is allowed here.</summary>
    private void OpenSourceAndWait(string path)
    {
        try
        {
            string[] lines = new FileInfo(path).Length > MaxSourceBytes ? [] : File.ReadAllLines(path);
            lock (_stateLock)
            {
                _sourceRequest = path;
                _sourcePath = path;
                _sourceLines.Clear();
                _sourceLines.AddRange(lines);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus(exception.Message, StatusSeverity.Error);
        }
    }

    #endregion

    #region Drawing

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty) return;

        // Rows and columns are the dialog's own (row 0 is the frame), so each one is the dialog's origin plus it.
        var colorScheme = ThemeService.CurrentColorScheme;
        int x0 = (int)AbsolutePosition.X;
        int y0 = (int)AbsolutePosition.Y;
        int innerWidth = (int)ViewSize.Width - 2;
        int footerRow = (int)ViewSize.Height - 2;

        DrawHeader(buffer, x0 + 1, y0 + 1, innerWidth, colorScheme);
        DrawFooter(buffer, x0 + 1, y0 + footerRow, innerWidth, colorScheme);
    }

    #region Panels and layout

    /// <summary>The Error List's band: its name and how many errors and warnings the commands printed.</summary>
    private string ErrorHeading()
    {
        List<BuildDiagnostic> merged = MergedDiagnostics();
        int errors = merged.Count(diagnostic => diagnostic.IsError);
        int warnings = merged.Count - errors;

        return UiText.Get("DevelopmentStudio.ErrorListHeading", "Error List") + "   "
            + string.Format(UiText.Get("DevelopmentStudio.ErrorCounts", "{0} errors, {1} warnings"), errors, warnings);
    }

    /// <summary>Rebuilds the Error List from the diagnostics the output holds, in the order they were printed.</summary>
    private void RebuildErrorPanel()
    {
        List<string> lines = MergedDiagnostics().Select(diagnostic => diagnostic.Text).ToList();
        _errorItems.ReplaceAll(lines);
        _errorListBox.UpdateModel();
    }

    /// <summary>Clears the output and the errors it held, for a new command. The caller holds <see cref="_stateLock"/>.</summary>
    private void ClearOutputLocked()
    {
        _output.Clear();
        _diagnostics.Clear();
        _diagnosticTexts.Clear();
    }

    /// <summary>Enter on an Error List row: opens the file the diagnostic names, at the line it names.</summary>
    private void OpenDiagnosticAtCursor()
    {
        int row = (int)_errorListBox.ScrollableModel.CursorPosition.Y;
        List<BuildDiagnostic> merged = MergedDiagnostics();
        if (row < 0 || row >= merged.Count) return;
        BuildDiagnostic diagnostic = merged[row];

        string folder = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        string path;
        try
        {
            // The tools print paths relative to the folder they ran in, which is the studio's folder or the solution's.
            // The base must be fully qualified: a rooted path such as "/src" is not, on Windows, and would throw.
            path = Path.GetFullPath(diagnostic.File, Path.GetFullPath(folder));
        }
        catch (ArgumentException)
        {
            return;
        }

        OpenSource(path, diagnostic.Line);
    }

    /// <summary>The panel whose list has the focus, or null when none has.</summary>
    private StudioPanelKind? FocusedPanelKind()
    {
        foreach (StudioPanel panel in _panels.Values)
        {
            if (panel.HasFocus) return panel.Kind;
        }

        return null;
    }

    /// <summary>
    /// Docks every panel again in the order <see cref="_layout"/> gives, then gives the focus back to the panel that had it.
    /// The dock lays its children out in the order they were added, so a changed arrangement means a rebuilt dock.
    /// </summary>
    private void RebuildDock()
    {
        StudioPanelKind? focused = FocusedPanelKind();
        foreach (StudioPanel panel in _panels.Values)
        {
            _dock.Remove(panel);
        }

        foreach ((StudioPanelKind kind, DockSide side, int percent) in _layout.Docked())
        {
            _dock.Add(_panels[kind], side, SizeRule.Percent(percent).WithMin(3));
        }

        LayoutPanelBodies();

        // A removed and re-added list keeps its focus flag, which would make SetFocus a no-op: clear the flags first.
        ResetFocus();
        if (focused is { } focusedKind) _panels[focusedKind].Body.SetFocus();
    }

    /// <summary>The dock sizes the panels; each panel then sizes its list to the rows under its band.</summary>
    private void LayoutPanelBodies()
    {
        foreach (StudioPanel panel in _panels.Values)
        {
            panel.LayoutBody();
        }
    }

    /// <summary>F8 starts or ends the panel layout mode.</summary>
    private void ToggleLayoutMode()
    {
        if (_layoutMode)
        {
            LeaveLayoutMode();
            return;
        }

        _layoutMode = true;
        _statusBeforeLayout = _status;
        _statusSeverityBeforeLayout = _statusSeverity;
        SetStatus(UiText.Get("DevelopmentStudio.LayoutOn", "Panel layout: arrows move the focused panel to that edge, Shift+arrows resize it, F8 or Esc ends."), StatusSeverity.Info);
    }

    private void LeaveLayoutMode()
    {
        _layoutMode = false;
        SetStatus(_statusBeforeLayout, _statusSeverityBeforeLayout);
    }

    /// <summary>
    /// The keys of the layout mode. Arrows move the focused panel to that edge; Shift and an arrow move its inner border.
    /// Returns false for the keys that keep their meaning in the mode (the commands, Tab and the arrows of a list).
    /// </summary>
    private bool HandleLayoutKey(KeyEvent keyEvent)
    {
        bool plain = !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        bool shift = keyEvent.Shift && !keyEvent.Alt && !keyEvent.Control;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
            case ConsoleKey.F8 when plain:
            case ConsoleKey.Enter when plain:
                LeaveLayoutMode();
                return true;
            case ConsoleKey.LeftArrow when plain:
                MovePanel(DockSide.Left);
                return true;
            case ConsoleKey.RightArrow when plain:
                MovePanel(DockSide.Right);
                return true;
            case ConsoleKey.UpArrow when plain:
                MovePanel(DockSide.Top);
                return true;
            case ConsoleKey.DownArrow when plain:
                MovePanel(DockSide.Bottom);
                return true;
            case ConsoleKey.LeftArrow when shift:
                ResizePanel(DockSide.Left);
                return true;
            case ConsoleKey.RightArrow when shift:
                ResizePanel(DockSide.Right);
                return true;
            case ConsoleKey.UpArrow when shift:
                ResizePanel(DockSide.Top);
                return true;
            case ConsoleKey.DownArrow when shift:
                ResizePanel(DockSide.Bottom);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Moves the focused panel to <paramref name="side"/>: onto an empty edge, or swapping with the panel there.</summary>
    private void MovePanel(DockSide side)
    {
        if (FocusedPanelKind() is not { } kind) return;

        switch (_layout.Move(kind, side))
        {
            case StudioPanelMove.Refused:
                SetStatus(UiText.Get("DevelopmentStudio.LayoutCentre", "The centre panel can only swap places with a panel: move the panel you want there into the centre."), StatusSeverity.Info);
                break;
            case StudioPanelMove.Moved:
            case StudioPanelMove.Swapped:
                RebuildDock();
                break;
        }
    }

    /// <summary>
    /// Shift and an arrow move the inner border of the focused panel: toward the panel's inside it grows the panel, away from it
    /// it shrinks. The arrow along the panel's own edge does nothing, and the centre panel has no border to move.
    /// </summary>
    private void ResizePanel(DockSide direction)
    {
        if (FocusedPanelKind() is not { } kind) return;

        DockSide side = _layout.SideOf(kind);
        if (side == DockSide.Fill)
        {
            SetStatus(UiText.Get("DevelopmentStudio.LayoutCentreResize", "The centre panel fills what the edges leave; resize an edge panel instead."), StatusSeverity.Info);
            return;
        }

        const int step = 5;
        int delta = (side, direction) switch
        {
            (DockSide.Left, DockSide.Right) or (DockSide.Right, DockSide.Left) or (DockSide.Top, DockSide.Bottom) or (DockSide.Bottom, DockSide.Top) => step,
            (DockSide.Left, DockSide.Left) or (DockSide.Right, DockSide.Right) or (DockSide.Top, DockSide.Top) or (DockSide.Bottom, DockSide.Bottom) => -step,
            _ => 0,
        };
        if (delta != 0 && _layout.Resize(kind, delta)) RebuildDock();
    }

    #endregion

    /// <summary>The editor's band: the open file's name and the cursor's line and column, or "Source" while no file is open.</summary>
    private string EditorTitle()
    {
        string path;
        lock (_stateLock) path = _sourcePath;
        if (path.Length == 0) return UiText.Get("DevelopmentStudio.SourceHeading", "Source");

        var cursor = _editorListBox.ScrollableModel.CursorPosition;
        int column = Math.Max(1, (int)cursor.X - GutterWidth + 1);
        string position = string.Format(UiText.Get("DevelopmentStudio.EditorPosition", "Ln {0}, Col {1}"), (int)cursor.Y + 1, column);
        return $"{Path.GetFileName(path)}   {position}";
    }

    /// <summary>The Solution Explorer's band: the heading of the mode F6 has shown.</summary>
    private string ExplorerHeading() => _explorerMode switch
    {
        ExplorerMode.Solution => UiText.Get("DevelopmentStudio.SolutionExplorerHeading", "Solution Explorer"),
        ExplorerMode.Tests => UiText.Get("DevelopmentStudio.TestExplorerHeading", "Test Explorer"),
        _ => UiText.Get("DevelopmentStudio.FileExplorerHeading", "File Explorer"),
    };

    /// <summary>The output panel's tab strip. The shown tab is drawn in reverse video, the others on the band.</summary>
    private void DrawOutputTabs(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme, bool focused)
    {
        var band = focused ? colorScheme.SelectedFocusedBackground : colorScheme.ButtonBackground;
        var bandText = focused ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
        WriteLine(buffer, x, y, w, string.Empty, bandText, band);

        int column = x;
        foreach (OutputTab tab in Enum.GetValues<OutputTab>())
        {
            string label = " " + OutputTabHeading(tab) + " ";
            int labelWidth = Math.Min(label.Length, x + w - column);
            if (labelWidth <= 0) break;

            bool shown = tab == _outputTab;
            WriteLine(buffer, column, y, labelWidth, label,
                shown ? colorScheme.Background : bandText,
                shown ? colorScheme.Foreground : band);
            column += label.Length + 1;
        }
    }

    private static string OutputTabHeading(OutputTab tab) => tab switch
    {
        OutputTab.Output => UiText.Get("DevelopmentStudio.OutputHeading", "Output"),
        OutputTab.Coverage => UiText.Get("DevelopmentStudio.CoverageHeading", "Coverage"),
        OutputTab.Profile => UiText.Get("DevelopmentStudio.ProfileHeading", "Profile"),
        _ => UiText.Get("DevelopmentStudio.DebugHeading", "Debug"),
    };

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        string folder = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        string debugState = _session is null ? string.Empty : _paused ? "   ● paused" : "   ▶ running";
        string busy = _busy ? "   *" : string.Empty;
        WriteLine(buffer, x, y, w, $"{folder}   ({_items.Count} items){debugState}{busy}", Color.Cyan, colorScheme.Background);
    }

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        string hints = UiText.Get("Hints.DevelopmentStudio", "F5:debug F9:breakpoint F10/F11:step Space:check B/C/T/R:build/clean/test/run V:SDK K:SDKs S/F:SBOM F6/F7:panes F8:layout Enter:open Esc:close");
        DrawStatusAndHints(buffer, x, y, w, _status, _statusSeverity switch
        {
            StatusSeverity.Error => colorScheme.Error,
            StatusSeverity.Success => Color.Green,
            _ => colorScheme.Foreground,
        }, hints, colorScheme);
    }

    #endregion
}
