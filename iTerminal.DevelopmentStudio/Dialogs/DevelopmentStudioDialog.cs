using System.Diagnostics;
using System.Globalization;
using System.Text;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.Core.Services;
using iTerminal.DevelopmentStudio.Model;
using iTerminal.DevelopmentStudio.Services;
using iTerminal.Core.Model.Dotnet;
using iTerminal.DevelopmentStudio.Services.Analysis;
using iTerminal.DevelopmentStudio.Services.Dap;
using iTerminal.DevelopmentStudio.Services.Lsp;
using iTerminal.DevelopmentStudio.Services.NuGet;
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
public partial class DevelopmentStudioDialog : SplitPaneDialog
{
    /// <summary>What the left pane lists: the Solution Explorer, or the Test Explorer.</summary>
    public enum ExplorerMode { Solution, Tests, Files }

    /// <summary>The tabs of the bottom output panel: the output log, the error list, the code analysis, the debug view, the coverage and the profile.</summary>
    public enum OutputTab { Output, Errors, Analysis, Debug, Coverage, Profile }

    /// <summary>
    /// The focus the hot key bar follows: the panel that has the keys, and for the output panel its tab. <see cref="Default"/> is
    /// what no panel has the focus of.
    /// </summary>
    private enum HotKeyContext { Default, Layout, Editor, Explorer, Output, Errors, Analysis, Debug, Coverage, Profile }

    /// <summary>The result of the last code analysis: its counts, how long it took, and whether it was cancelled.</summary>
    private sealed record AnalysisSummary(string Target, int Errors, int Warnings, TimeSpan Elapsed, bool Cancelled, int ExitCode);

    private enum StatusSeverity { Info, Success, Error }

    /// <summary>One row of the explorer. <see cref="Item"/> is null for a heading row.</summary>
    /// <summary>One row of the explorer. A File Explorer row carries <see cref="FilePath"/> instead of a project.</summary>
    private sealed record ExplorerRow(string DisplayName, DevelopmentProject? Item, Color? Tint, string? FilePath = null, bool IsDirectory = false);

    /// <summary>A file or folder of the File Explorer tree. <see cref="Children"/> is empty for a file.</summary>
    private sealed record FileNode(string Path, bool IsDirectory, List<FileNode> Children);

    /// <summary>The result of the last test run of one test item.</summary>
    private sealed record TestOutcome(bool Passed, TestTotals Totals);

    /// <summary>A source file read in the background: its lines, its line ending, and why it could not be shown (null when it was read).</summary>
    private sealed record LoadedSource(string Path, string[] Lines, string LineEnding, string? Error);

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
        /// <summary>A left click on a row, after the list has moved the cursor there (the click opens a finding in the Analysis tab).</summary>
        public event EventHandler? RowClicked;

        public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
        {
            bool isHandled = await base.HandleMouseEventAsync(mouseEvent);

            // The base class moves the cursor on a click; only a click that landed on a row counts as a row click.
            double relativeY = mouseEvent.Position.Y - AbsolutePosition.Y;
            bool onRow = mouseEvent.Position.X >= AbsolutePosition.X && mouseEvent.Position.X < AbsolutePosition.X + ViewSize.Width
                && relativeY >= 0 && relativeY < ViewSize.Height && ScrollableModel.ScrollPosition.Y + relativeY < Count;
            if (mouseEvent.LeftClicked && IsEnabled && onRow) RowClicked?.Invoke(this, EventArgs.Empty);
            return isHandled;
        }

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
    }

    private readonly IDevelopmentWorkspaceScanner _scanner;
    private readonly IDevelopmentProcessRunner _runner;
    private readonly DevelopmentToolchains _toolchains;
    private readonly BreakpointStore _breakpoints;
    private readonly MainProgramStore _mainProgram;
    private readonly IDotnetSolutionService _dotnet;
    private readonly ISyntaxHighlightingService _highlighting;
    private readonly ISdkManager _sdkManager;
    private readonly INuGetPackageService _nuGetPackages;
    private readonly IPromptService _prompts;
    private readonly ISettingsService _settings;
    /// <summary>Shows the explorer's context menu (the Menu key). Null where no popover service is registered, as in the tests.</summary>
    private readonly IPopoverService? _popover;

    /// <summary>The row the explorer's cursor was on when it was saved, to be put back once that row is listed (null when there is none to wait for).</summary>
    private string? _pendingSelection;

    /// <summary>The cursor row last written to the settings, so a cursor that has not moved is not written again.</summary>
    private string? _savedSelection;

    /// <summary>The clock of the command running (or the last one run): its elapsed time is shown while it runs and in the status after it.</summary>
    private Stopwatch? _runClock;

    /// <summary>The whole second the header last showed for the running command; the dialog redraws when the second changes.</summary>
    private long _shownSeconds = -1;

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

    /// <summary>The build configuration of the .NET commands (D switches it). Release disables debugging.</summary>
    private BuildConfiguration _configuration = BuildConfiguration.Debug;

    /// <summary>The solution the dialog was opened on (F3), or empty for the whole folder.</summary>
    private string _solutionPath = string.Empty;

    private readonly ConcurrentObservableCollection<ExplorerRow> _explorerItems = new();
    private readonly ConcurrentObservableCollection<string> _outputItems = new();
    private readonly ExplorerListBox _explorerListBox;
    private readonly StudioSourceEditor _editor;
    private readonly ViewListBox _outputListBox;

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
    /// <summary>The file open in the editor, or empty when none is. Set on the UI thread, when a read is put in the editor.</summary>
    private string _sourcePath = string.Empty;
    /// <summary>The newest file asked for. A read that finishes for an older request is dropped.</summary>
    private string _sourceRequest = string.Empty;
    /// <summary>A file read in the background, waiting for the UI thread to put it in the editor (<see cref="ApplyLoadedSource"/>).</summary>
    private LoadedSource? _loadedSource;
    /// <summary>The line the editor shows once its file is the one in it: a stop's line, or the line an open asked for.</summary>
    private (string Path, int Line, int Column)? _reveal;
    /// <summary>What the last save wrote, applied on the UI thread: when the editor still holds that text, it is clean again.</summary>
    private (string Path, string Text)? _savedWrite;
    /// <summary>Set by a background change to the marks in the editor's gutter; the UI thread redraws them on its idle pass.</summary>
    private volatile bool _marksStale;
    private static readonly UTF8Encoding Utf8NoBom = new(false);
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
    private StudioPanelLayout _layout;
    private bool _layoutMode;

    /// <summary>The rows the hot key bar takes under the panels: the most any focus needs at the current width, so the panels do not move when the focus does.</summary>
    private int _hotKeyRows = 1;
    private string _statusBeforeLayout = string.Empty;
    private StatusSeverity _statusSeverityBeforeLayout = StatusSeverity.Info;
    /// <summary>The tab the bottom output panel shows (F7 cycles it). The editor always shows the source.</summary>
    private OutputTab _outputTab = OutputTab.Output;

    private DebugSession? _session;

    /// <summary>Coverage of the last coverage run, and the file each Coverage row names (row 1 is the first file).</summary>
    private CoverageReport? _coverage;
    private readonly List<CoverageFile> _coverageFiles = [];

    /// <summary>The findings of the last code analysis, in the order the Analysis tab lists them. Guarded by <see cref="_stateLock"/>.</summary>
    private readonly List<BuildDiagnostic> _findings = [];

    /// <summary>The last code analysis, or null before the first one. Guarded by <see cref="_stateLock"/>.</summary>
    private AnalysisSummary? _analysis;

    /// <summary>The name of the item the running code analysis is measuring. Guarded by <see cref="_stateLock"/>.</summary>
    private string _analysisTarget = string.Empty;

    /// <summary>The estimate of the running code analysis: how long the item's last one took, or null when it has none. Guarded by <see cref="_stateLock"/>.</summary>
    private TimeSpan? _analysisEstimate;

    /// <summary>How long each item's last code analysis took, in seconds: the estimate the running one shows. Guarded by <see cref="_stateLock"/>.</summary>
    private readonly Dictionary<string, double> _analysisDurations;


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
        ISdkManager sdkManager,
        ISettingsService settings,
        INuGetPackageService nuGetPackages,
        IPromptService prompts,
        IPopoverService? popoverService = null)
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
        _nuGetPackages = nuGetPackages;
        _prompts = prompts;
        _settings = settings;
        _popover = popoverService;

        // The settings the studio keeps between openings; each one is written again when the user changes it.
        _layout = StudioPanelLayout.Parse(settings.Get(StudioSettings.LayoutKey));
        _sbomFormat = ParseSetting(settings.Get(StudioSettings.SbomFormatKey), SbomFormat.Xml);
        _configuration = ParseSetting(settings.Get(StudioSettings.BuildConfigurationKey), BuildConfiguration.Debug);
        _explorerMode = ParseSetting(settings.Get(StudioSettings.ExplorerModeKey), ExplorerMode.Solution);
        _outputTab = ParseSetting(settings.Get(StudioSettings.OutputTabKey), OutputTab.Output);
        _analysisDurations = new Dictionary<string, double>(StudioSettings.DecodeDurations(settings.Get(StudioSettings.AnalysisDurationsKey)));
        foreach (string folder in StudioSettings.DecodeList(settings.Get(StudioSettings.ExpandedFoldersKey))) _expandedFolders.Add(folder);
        Title = UiText.Get("DevelopmentStudio.Title", "Development studio");

        _explorerListBox = new ExplorerListBox(
            new ListBoxModel<ExplorerRow>(_explorerItems) { TransformItem = (row, _) => new ListItem<ExplorerRow>(row) { Value = row.DisplayName, IsEnabled = true } },
            themeService);
        _editor = new StudioSourceEditor(themeService, highlighting);
        _outputListBox = new ViewListBox(
            new ListBoxModel<string>(_outputItems) { TransformItem = (line, _) => new ListItem<string>(line) { Value = line, IsEnabled = true } },
            themeService);
        _outputListBox.RowClicked += (_, _) =>
        {
            if (_outputTab == OutputTab.Analysis) OpenAnalysisAtCursor();
        };

        // Each panel is a component of its own; the dock places them in the order _layout gives.
        _dock = new DockPanel(themeService) { Gap = 1 };
        _panels[StudioPanelKind.Editor] = new StudioPanel(StudioPanelKind.Editor, _editor, EditorTitle, themeService);
        _panels[StudioPanelKind.Explorer] = new StudioPanel(StudioPanelKind.Explorer, _explorerListBox, ExplorerHeading, themeService, _explorerListBox.ScrollableModel);
        _panels[StudioPanelKind.Output] = new StudioPanel(StudioPanelKind.Output, _outputListBox, () => string.Empty, themeService)
        {
            BandPainter = (buffer, x, y, width, colorScheme, focused) => DrawOutputTabs(buffer, x, y, width, colorScheme, focused),
        };
        ShowNoSource();
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
    public IReadOnlyList<string> EditorLines => [.. _editor.TextModel.Lines.AsSnapshot()];

    /// <summary>The gutter mark of a line of the editor (1-based), such as the breakpoint, the stop line or an error; null for none.</summary>
    public string? EditorMark(int lineNumber) => _editor.MarkAt(lineNumber - 1);

    /// <summary>The lines of the bottom output panel as they are drawn, in order.</summary>
    public IReadOnlyList<string> OutputLines => [.. _outputItems];

    /// <summary>The Error List rows as they are drawn, in order.</summary>
    public IReadOnlyList<string> ErrorLines => [.. MergedDiagnostics().Select(diagnostic => diagnostic.Text)];

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
        string? savedSdk = _settings.Get(StudioSettings.SdkHostKey);
        var sdks = await Task.Run(ReadSdks, CancellationToken.None);
        ApplySdks(sdks, sdks.FirstOrDefault(sdk => sdk.HostPath.Equals(savedSdk, StringComparison.OrdinalIgnoreCase)));
        _javaHome = _sdkManager.ActiveJavaHome;

        // The explorer keeps the expansion and the cursor the user left it with. A studio that has never saved them opens its solutions.
        if (_settings.Get(StudioSettings.ExpandedFoldersKey) is null) OpenSolutionsInTree();
        _savedSelection = string.IsNullOrEmpty(_solutionPath) ? SavedSelection() : null;
        _pendingSelection = _savedSelection;
        _status = _items.Count == 0
            ? UiText.Get("DevelopmentStudio.NothingFound", "No solutions, projects or scripts were found in this folder.")
            : string.Format(UiText.Get("DevelopmentStudio.Found", "Found {0} items. Enter opens a file; F5 starts debugging."), _items.Count);
        _statusSeverity = StatusSeverity.Info;
        RebuildExplorer();
        if (_pendingSelection is null) SelectSolutionRow();
        if (_explorerMode == ExplorerMode.Files && !_fileTreeBuilt) _ = LoadFileTreeAsync();
        RebuildView();
        StateHasChanged();
    }

    /// <summary>The row the explorer's cursor was on when the studio last saved it, or null.</summary>
    private string? SavedSelection()
    {
        string? path = _settings.Get(StudioSettings.SelectedPathKey);
        return string.IsNullOrEmpty(path) ? null : path;
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

        // The panels fill the frame between the header row (row 1) and the footer: the status row and the hot key rows, which are
        // the most rows any focus needs at this width (the bottom row is the frame's).
        int innerWidth = Math.Max(0, (int)ViewSize.Width - 2);
        _hotKeyRows = Enum.GetValues<HotKeyContext>().Max(context => WrapHotKeys(HotKeyText(context), innerWidth).Count);
        _hotKeyRows = Math.Max(1, _hotKeyRows);
        int footerRows = 1 + _hotKeyRows;
        _dock.Position = new Position(1, 2);
        _dock.ViewSize = new Size(innerWidth, Math.Max(0, (int)ViewSize.Height - 3 - footerRows));
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

    /// <summary>
    /// Ctrl+W: stops a running command and the debugged program, then closes the studio. Esc does not close it: a menu or a
    /// popup open on the studio takes Esc, and otherwise Esc does nothing.
    /// </summary>
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

        SaveExplorerSelection();
        Close();
    }

    #endregion

    #region Keys

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("F12", UiText.Get("DevelopmentStudio.HintMenuBar", "The menu bar: File, Build, Debug, View and Tools (a click on a title opens its menu too)"));
        yield return ("F5", UiText.Get("DevelopmentStudio.HintStart", "Start debugging, or continue"));
        yield return ("D", UiText.Get("DevelopmentStudio.HintConfiguration", "Switch the build configuration: Debug or Release (Release disables debugging)"));
        yield return ("Ctrl+F5", UiText.Get("DevelopmentStudio.HintRunNoDebug", "Run without debugging"));
        yield return ("Shift+F5", UiText.Get("DevelopmentStudio.HintStop", "Stop debugging"));
        yield return ("F9", UiText.Get("DevelopmentStudio.HintBreakpoint", "Toggle a breakpoint on the cursor line"));
        yield return ("F10 / F11", UiText.Get("DevelopmentStudio.HintStep", "Step over / step into"));
        yield return ("Shift+F11", UiText.Get("DevelopmentStudio.HintStepOut", "Step out"));
        yield return ("B / C / T / R", UiText.Get("DevelopmentStudio.HintCommands", "Build, clean, test, run the selected item"));
        yield return ("W", UiText.Get("DevelopmentStudio.HintWatch", "Watch the word under the cursor (in the debug view, remove it)"));
        yield return ("F6 / F7", UiText.Get("DevelopmentStudio.HintPanes", "Explorer tab (Solution, Tests, Files) / output panel tab (Output, Errors, Analysis, Debug, Coverage, Profile)"));
        yield return ("F8", UiText.Get("DevelopmentStudio.HintLayout", "Move and resize the panels: arrows move the focused one to that edge, Shift+arrows resize it"));
        yield return ("Enter", UiText.Get("DevelopmentStudio.HintOpen", "Open the file (or run the test)"));
        yield return ("Left / Right", UiText.Get("DevelopmentStudio.HintExpand", "Collapse or expand the solution, project or folder under the cursor"));
        yield return ("M", UiText.Get("DevelopmentStudio.HintMain", "Make the item under the cursor the Main program: F5 and Ctrl+F5 start it"));
        yield return ("Menu", UiText.Get("DevelopmentStudio.HintMenu", "The item's menu: its commands, and Set as main project"));
        yield return ("Space", UiText.Get("DevelopmentStudio.HintCheck", "Check or uncheck the .NET project; B, C, T and R then run on the checked ones"));
        yield return ("A / Shift+T", UiText.Get("DevelopmentStudio.HintCheckAll", "Check all projects / all test projects"));
        yield return ("V", UiText.Get("DevelopmentStudio.HintSdk", "Switch to the next .NET SDK"));
        yield return ("K", UiText.Get("DevelopmentStudio.HintSdkManager", "Install .NET SDKs and JDKs, and choose the JDK that Java builds and runs use"));
        yield return ("U", UiText.Get("DevelopmentStudio.HintNuGet", "Manage the NuGet packages of the selected solution or project"));
        yield return ("S / F", UiText.Get("DevelopmentStudio.HintSbom", "Write a CycloneDX SBOM of the solution / switch it between XML and JSON"));
        yield return ("O / P", UiText.Get("DevelopmentStudio.HintCoverage", "Measure the coverage of the selected item / profile the selected Python script"));
        yield return ("N", UiText.Get("DevelopmentStudio.HintAnalysis", "Analyze the code of the selected item: the Analysis tab lists the findings, and Enter or a click opens one"));
        yield return ("G", UiText.Get("DevelopmentStudio.HintGoToFile", "In the coverage view, open the file under the cursor"));
        yield return ("X", UiText.Get("DevelopmentStudio.HintCancel", "Cancel the running command"));
        yield return ("Ctrl+,", UiText.Get("DevelopmentStudio.HintSettings", "Settings: the start-up tabs, the SDK and SBOM format, the layout and the analysis estimates"));
        yield return ("Ctrl+W", UiText.Get("DevelopmentStudio.HintClose", "Close the studio. Esc does not close it."));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // In the layout mode Esc ends the mode rather than closing the dialog.
        if (_layoutMode && HandleLayoutKey(keyEvent)) return true;

        // While the editor has the focus and a file is open, the editor takes the keys: its letters are text, not commands, and the
        // cursor, selection, undo and clipboard keys are its own. A key it declines goes on to the studio's commands.
        if (IsEditorKey(keyEvent) && await _editor.HandleKeyEventAsync(keyEvent)) return true;

        bool plain = !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        bool shift = keyEvent.Shift && !keyEvent.Alt && !keyEvent.Control;
        bool ctrl = keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                // Esc never closes the studio (Ctrl+W does). It closes one open overlay at a time, and is otherwise swallowed here so
                // the dialog's own Esc, which closes it, never runs.
                if (ITransientOverlayHost.TryCloseInnermostOverlay(this)) StateHasChanged();
                return true;
            case ConsoleKey.W when ctrl:
                await CloseAsync();
                return true;
            case ConsoleKey.OemComma when ctrl:
                await OpenSettingsAsync();
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
            case ConsoleKey.F12 when plain:
                await OpenMenuBarAsync(MenuBarTitle.File);
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
            case ConsoleKey.S when ctrl:
                SaveSource();
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
            case ConsoleKey.D when plain:
                ToggleBuildConfiguration();
                return true;
            case ConsoleKey.O when plain:
                RunCoverage();
                return true;
            case ConsoleKey.P when plain:
                RunProfile();
                return true;
            case ConsoleKey.N when plain:
                RunCodeAnalysis();
                return true;
            case ConsoleKey.G when plain:
                OpenCoverageFileAtCursor();
                return true;
            case ConsoleKey.Applications when plain:
                await ShowExplorerMenuAsync();
                return true;
            case ConsoleKey.K when plain:
                await OpenSdkManagerAsync();
                return true;
            case ConsoleKey.U when plain:
                await OpenNuGetManagerAsync();
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
        if (FocusedPanelKind() == StudioPanelKind.Output && _outputTab == OutputTab.Errors)
        {
            OpenDiagnosticAtCursor();
            return;
        }

        if (FocusedPanelKind() == StudioPanelKind.Output && _outputTab == OutputTab.Analysis)
        {
            OpenAnalysisAtCursor();
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
        _settings.Set(StudioSettings.ExpandedFoldersKey, StudioSettings.EncodeList(_expandedFolders));
        RebuildExplorer();
        StateHasChanged();
    }

    /// <summary>
    /// Writes the row under the explorer's cursor to the settings, when it has moved since the last write. It runs on the UI
    /// thread's idle pass, so a cursor move costs a key press and no more. While a saved row is still waiting to be listed, the
    /// cursor is not the user's, so nothing is written.
    /// </summary>
    private void SaveExplorerSelection()
    {
        if (_pendingSelection is not null) return;
        var row = SelectedRow;
        string? path = row?.Item?.Path ?? row?.FilePath;
        if (path is null || path == _savedSelection) return;
        _savedSelection = path;
        _settings.Set(StudioSettings.SelectedPathKey, path);
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

    /// <summary>
    /// The Menu key on the Solution Explorer: a menu of what can be done with the item under the cursor. Its first entry sets the
    /// item as the main project (or clears it), the way M does; the others are the build, run, debug, test and clean commands.
    /// Does nothing without a popover service, with the Explorer not focused, or with no item under the cursor.
    /// </summary>
    private async Task ShowExplorerMenuAsync()
    {
        var item = SelectedItem;
        if (_popover is null || item is null || FocusedPanelKind() != StudioPanelKind.Explorer) return;

        var menu = new ContextMenu(_popover, ThemeService);
        if (item.Kind != DevelopmentProjectKind.Solution)
        {
            bool main = IsMain(item);
            menu.Items.Add(new ContextMenuItem(main
                ? UiText.Get("DevelopmentStudio.MenuClearMain", "Clear the main project")
                : UiText.Get("DevelopmentStudio.MenuSetMain", "Set as main project"))
            {
                Shortcut = "M",
                IsEnabled = main || item.IsStartable,
                Execute = () =>
                {
                    ToggleMainProgramAtCursor();
                    return Task.CompletedTask;
                },
            });
        }

        menu.Items.Add(CommandMenuItem(string.Format(UiText.Get("DevelopmentStudio.MenuConfiguration", "Configuration: {0}"), _configuration), "D", ToggleBuildConfiguration));
        menu.Items.Add(CommandMenuItem(UiText.Get("DevelopmentStudio.MenuBuild", "Build"), "B", () => RunSelected("build")));
        menu.Items.Add(CommandMenuItem(UiText.Get("DevelopmentStudio.MenuRun", "Run"), "R", () => RunSelected("run")));
        menu.Items.Add(CommandMenuItem(UiText.Get("DevelopmentStudio.MenuDebug", "Debug"), "F5", StartOrContinueDebugging));
        menu.Items.Add(CommandMenuItem(UiText.Get("DevelopmentStudio.MenuTest", "Test"), "T", () => RunSelected("test")));
        menu.Items.Add(CommandMenuItem(UiText.Get("DevelopmentStudio.MenuClean", "Clean"), "C", () => RunSelected("clean")));
        menu.Items.Add(CommandMenuItem(UiText.Get("DevelopmentStudio.MenuAnalyze", "Analyze code"), "N", RunCodeAnalysis));

        // The menu opens beside the cursor row, as a click on that row would.
        var list = _explorerListBox.ScrollableModel;
        var point = new Position(_explorerListBox.AbsolutePosition.X + 2, _explorerListBox.AbsolutePosition.Y + list.CursorPosition.Y - list.ScrollPosition.Y + 1);
        await menu.OpenAtAsync(this, point);
    }

    private static ContextMenuItem CommandMenuItem(string title, string shortcut, Action run, bool enabled = true) =>
        new(title)
        {
            Shortcut = shortcut,
            IsEnabled = enabled,
            Execute = () =>
            {
                run();
                return Task.CompletedTask;
            },
        };

    /// <summary>F6 cycles the left pane: Solution Explorer, Test Explorer, File Explorer.</summary>
    private void ToggleExplorerMode()
    {
        _explorerMode = _explorerMode switch
        {
            ExplorerMode.Solution => ExplorerMode.Tests,
            ExplorerMode.Tests => ExplorerMode.Files,
            _ => ExplorerMode.Solution,
        };
        _settings.Set(StudioSettings.ExplorerModeKey, _explorerMode.ToString());
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

    /// <summary>F7 cycles the output panel's tabs: Output, Errors, Analysis, Debug, Coverage, Profile. The editor is not affected.</summary>
    private void CycleOutputTab()
    {
        _outputTab = _outputTab switch
        {
            OutputTab.Output => OutputTab.Errors,
            OutputTab.Errors => OutputTab.Analysis,
            OutputTab.Analysis => OutputTab.Debug,
            OutputTab.Debug => OutputTab.Coverage,
            OutputTab.Coverage => OutputTab.Profile,
            _ => OutputTab.Output,
        };
        _settings.Set(StudioSettings.OutputTabKey, _outputTab.ToString());
        RebuildOutputPanel();

        // The cursor row belongs to the list it was on; a new tab starts at its first row, so Enter reads the row shown.
        _outputListBox.ScrollableModel.CursorPosition.Y = 0;
        _outputListBox.ScrollableModel.ScrollPosition.Y = 0;
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
        ApplyPendingSelection();
    }

    /// <summary>
    /// Puts the cursor back on the saved row once it is listed. A project's files and the File Explorer's tree are read in the
    /// background, so their rows can appear after the first listing: the wait ends with the row, or when nothing is being read.
    /// </summary>
    private void ApplyPendingSelection()
    {
        if (_pendingSelection is not { } path) return;
        int index = _explorerItems.ToList().FindIndex(row => string.Equals(row.Item?.Path ?? row.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            _explorerListBox.ScrollableModel.CursorPosition.Y = index;
            _pendingSelection = null;
            return;
        }

        if (_loadingProjects.Count == 0 && (_explorerMode != ExplorerMode.Files || _fileTreeBuilt)) _pendingSelection = null;
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
        MarkEditorStale();
        RebuildOutputPanel();
    }

    /// <summary>
    /// Marks the editor's gutter for a redraw on the UI thread's next idle pass. A background task uses this, since it must not
    /// touch the editor; a change made on the UI thread calls <see cref="RefreshEditorMarks"/> directly.
    /// </summary>
    private void MarkEditorStale() => _marksStale = true;

    /// <summary>
    /// Rebuilds the gutter marks of the open file, in order of precedence (the last one wins): the coverage, the server's
    /// diagnostics, the breakpoints and the stop line. Runs on the UI thread only.
    /// </summary>
    private void RefreshEditorMarks()
    {
        _marksStale = false;
        string path;
        lock (_stateLock) path = _sourcePath;

        var marks = new Dictionary<int, string>();
        if (path.Length > 0)
        {
            CoverageFile? covered = _coverage?.Files.FirstOrDefault(file => file.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (covered is not null)
            {
                foreach ((int number, int hits) in covered.Hits) marks[number - 1] = hits > 0 ? "+" : "-";
            }

            foreach ((int number, string mark) in LiveMarks(path)) marks[number - 1] = mark;
            foreach (int number in _breakpoints.LinesIn(path)) marks[number - 1] = "●";

            lock (_stateLock)
            {
                if (_stopLine > 0 && _stopPath.Equals(path, StringComparison.OrdinalIgnoreCase)) marks[_stopLine - 1] = "→";
            }
        }

        _editor.SetMarks(marks);
    }

    /// <summary>The hint shown while no file is open: a read-only line that no key can change.</summary>
    private void ShowNoSource()
    {
        lock (_stateLock) _sourcePath = string.Empty;
        _editor.IsReadOnly = false;
        _editor.Syntax = null;
        _editor.ReplaceAllText([UiText.Get("DevelopmentStudio.NoSource", "Select a file in the explorer and press Enter to open it here.")], "\n", Utf8NoBom);
        _editor.IsReadOnly = true;
        RefreshEditorMarks();
    }

    /// <summary>Puts the file read in the background into the editor. Runs on the UI thread, from the idle pass.</summary>
    private void ApplyLoadedSource()
    {
        LoadedSource? loaded;
        lock (_stateLock)
        {
            loaded = _loadedSource;
            _loadedSource = null;
        }

        if (loaded is null) return;
        if (IsSourceDirty)
        {
            // Edits the file would replace are kept, and the file is not opened over them (a debugger stop in another file, say).
            _editor.IsReadOnly = false;
            SetStatus(UiText.Get("DevelopmentStudio.UnsavedSource", "Unsaved changes in the editor: Ctrl+S saves them first."), StatusSeverity.Error);
            return;
        }

        if (loaded.Error is not null)
        {
            ShowNoSource();
            SetStatus(loaded.Error, StatusSeverity.Error);
            RebuildView();
            return;
        }

        lock (_stateLock) _sourcePath = loaded.Path;
        _editor.IsReadOnly = false;
        _editor.Syntax = _highlighting.FindByFile(loaded.Path);
        _editor.ReplaceAllText(loaded.Lines.Length == 0 ? new[] { string.Empty } : loaded.Lines, loaded.LineEnding, Utf8NoBom);
        RefreshEditorMarks();
        RevealPending();
        SetStatus(Path.GetFileName(loaded.Path), StatusSeverity.Info);
        RebuildView();
    }

    /// <summary>Moves the editor's cursor to the line a stop or an open asked for, once that file is the one in the editor.</summary>
    private void RevealPending()
    {
        (string Path, int Line, int Column)? reveal;
        string path;
        lock (_stateLock)
        {
            reveal = _reveal;
            path = _sourcePath;
        }

        if (reveal is not { } target || !path.Equals(target.Path, StringComparison.OrdinalIgnoreCase)) return;
        lock (_stateLock) _reveal = null;

        // Lines and columns count from 1 in the tools' output; a column of 0 means the tool printed none, so the line's start.
        _editor.ShowLine(Math.Max(0, target.Line - 1), Math.Max(0, target.Column - 1));
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
                case OutputTab.Errors:
                    lines.AddRange(MergedDiagnostics().Select(diagnostic => diagnostic.Text));
                    break;
                case OutputTab.Analysis:
                    lines.AddRange(AnalysisLines());
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
            lines.Add(_outputTab switch
            {
                OutputTab.Errors => UiText.Get("DevelopmentStudio.NoErrors", "No errors or warnings. Build, run and test report theirs here."),
                _ => string.Empty,
            });
        }

        _outputItems.ReplaceAll(lines);
        _outputListBox.UpdateModel();
    }

    /// <summary>
    /// The Analysis view. While the analysis runs it shows the item and the time so far against the estimate (the last analysis
    /// of the same item). Once it has ended it shows the counts, then one row per finding: the rows <see cref="OpenAnalysisAtCursor"/>
    /// reads, after the summary row. Caller holds the lock.
    /// </summary>
    private List<string> AnalysisLines()
    {
        var lines = new List<string>();
        if (_busy && _runClock is { IsRunning: true } clock)
        {
            string elapsed = FormatElapsed(clock.Elapsed);
            lines.Add(_analysisEstimate is { } estimate
                ? string.Format(UiText.Get("DevelopmentStudio.AnalysisRunning", "Analyzing {0}: {1} elapsed, about {2} expected."), _analysisTarget, elapsed, FormatElapsed(estimate))
                : string.Format(UiText.Get("DevelopmentStudio.AnalysisRunningNoEstimate", "Analyzing {0}: {1} elapsed."), _analysisTarget, elapsed));
            return lines;
        }

        if (_analysis is not { } summary)
        {
            lines.Add(UiText.Get("DevelopmentStudio.NoAnalysis", "No code analysis yet. N analyzes the selected item, and its findings are listed here."));
            return lines;
        }

        if (summary.Cancelled)
        {
            lines.Add(string.Format(UiText.Get("DevelopmentStudio.AnalysisCancelled", "Code analysis of {0} cancelled."), summary.Target));
        }
        else if (summary.ExitCode != 0 && summary.Errors == 0)
        {
            lines.Add(string.Format(UiText.Get("DevelopmentStudio.AnalysisFailed", "Code analysis of {0} failed (exit {1}). The output says why."), summary.Target, summary.ExitCode));
        }
        else
        {
            lines.Add(string.Format(UiText.Get("DevelopmentStudio.AnalysisSummary", "Code analysis of {0}: {1} error(s), {2} warning(s). Elapsed {3}."),
                summary.Target, summary.Errors, summary.Warnings, FormatElapsed(summary.Elapsed)));
        }

        lines.AddRange(_findings.Select(finding => finding.Text));
        return lines;
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
        ApplyLoadedSource();
        ApplySavedText();
        if (_marksStale) RefreshEditorMarks();
        RevealPending();
        RedrawRunningClock();
        SaveExplorerSelection();
        long version = _languageServer.Version;
        if (version == _seenServerVersion) return;
        _seenServerVersion = version;
        RefreshEditorMarks();
        RebuildErrorPanel();
        ShowServerStatus();
    }

    /// <summary>The elapsed time of the running command as the header shows it (m:ss, or h:mm:ss from an hour), or null when none is running.</summary>
    private string? RunningElapsedText()
    {
        TimeSpan? elapsed;
        lock (_stateLock) elapsed = _runClock is { IsRunning: true } clock ? clock.Elapsed : null;
        return elapsed is { } time ? FormatElapsed(time) : null;
    }

    /// <summary>The time a command took, as the status and the output show it: m:ss, or h:mm:ss from an hour.</summary>
    private static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? elapsed.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : elapsed.ToString(@"m\:ss", CultureInfo.InvariantCulture);

    /// <summary>The header's clock moves while a command runs, so the dialog redraws when the second on screen changes.</summary>
    private void RedrawRunningClock()
    {
        if (!_busy) return;
        long seconds;
        lock (_stateLock) seconds = _runClock is { IsRunning: true } clock ? (long)clock.Elapsed.TotalSeconds : -1;
        if (seconds < 0 || seconds == _shownSeconds) return;
        _shownSeconds = seconds;

        // The Analysis tab shows the time too, against its estimate.
        if (_outputTab == OutputTab.Analysis) RebuildOutputPanel();
        StateHasChanged();
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

    /// <summary>
    /// Opens a source file in the editor and gives the editor the focus. The read runs on a background task and the newest request
    /// wins; the text is put in the editor on the UI thread's idle pass (<see cref="ApplyLoadedSource"/>), with the cursor on <paramref name="line"/>.
    /// </summary>
    private void OpenSource(string path, int line = 1, int column = 0)
    {
        if (IsSourceDirty)
        {
            // Opening another file, or reloading this one, would throw the unsaved edits away.
            SetStatus(UiText.Get("DevelopmentStudio.UnsavedSource", "Unsaved changes in the editor: Ctrl+S saves them first."), StatusSeverity.Error);
            return;
        }

        lock (_stateLock)
        {
            _sourceRequest = path;
            _reveal = (path, line, column);
        }

        // Read-only until the file is in the editor, so a key typed in the meantime cannot be lost with the old text.
        _editor.IsReadOnly = true;
        _editor.SetFocus();
        _ = Task.Run(() =>
        {
            LoadedSource loaded = ReadSource(path);
            if (loaded.Error is null && path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                _languageServer.OpenFile(ServerFolder, path, string.Join('\n', loaded.Lines));
            }

            lock (_stateLock)
            {
                if (_sourceRequest != path) return;
                _loadedSource = loaded;
            }
        }, CancellationToken.None);
    }

    /// <summary>Reads a source file for the editor. Runs on a background task: it never touches the controls.</summary>
    private static LoadedSource ReadSource(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxSourceBytes)
            {
                return new LoadedSource(path, [], "\n", UiText.Get("DevelopmentStudio.TooLarge", "The file is too large to show here."));
            }

            string[] lines = File.ReadAllLines(path);
            string lineEnding = File.ReadAllText(path).Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            return new LoadedSource(path, lines, lineEnding, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LoadedSource(path, [], "\n", exception.Message);
        }
    }

    /// <summary>The open file's text as the editor holds it, with its line ending after the last line, as a save writes it.</summary>
    private string TextOfSource()
    {
        string lineEnding = _editor.TextModel.LineEnding;
        return string.Join(lineEnding, _editor.TextModel.Lines.AsSnapshot()) + lineEnding;
    }

    /// <summary>The 1-based line under the editor's cursor, or 0 when no file is open.</summary>
    private int CursorLine => SourceOpen ? (int)_editor.ScrollableModel.CursorPosition.Y + 1 : 0;

    #endregion

    #region Breakpoints and watches

    private void ToggleBreakpointAtCursor()
    {
        string path;
        lock (_stateLock) path = _sourcePath;
        int line = CursorLine;
        int sourceCount = _editor.TextModel.Lines.Count;

        if (path.Length == 0 || line < 1 || line > sourceCount)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NoBreakpointTarget", "Open a file (Enter) and put the cursor on a line to set a breakpoint."), StatusSeverity.Info);
            return;
        }

        bool set = _breakpoints.Toggle(path, line);
        RefreshEditorMarks();
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
        int line = CursorLine;
        if (line < 1 || line > _editor.TextModel.Lines.Count) return null;
        string text = _editor.TextModel.Lines[line - 1];
        // The editor's cursor column is the text's own column: the editor draws its gutter itself.
        int column = Math.Clamp((int)_editor.ScrollableModel.CursorPosition.X, 0, text.Length);
        int start = column, end = column;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return end > start ? text[start..end] : null;
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
    /// N: analyzes the code of the cursor item with the toolchain's analyzers. The build runs on a background task while the
    /// Analysis tab takes the focus and shows the time so far against the estimate (the last analysis of the same item). When it
    /// ends, the tab lists the findings: Enter, or a click on one, opens its file at the line and column it names.
    /// </summary>
    private void RunCodeAnalysis()
    {
        var item = SelectedItem;
        if (item is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectItem", "Select an item first."), StatusSeverity.Info);
            return;
        }

        if (_busy)
        {
            SetStatus(UiText.Get("DevelopmentStudio.BusyAnalysis", "A command is still running; X cancels it before analyzing."), StatusSeverity.Info);
            return;
        }

        ProcessCommand? command = ToolchainFor(item).Analyze(item);
        if (command is null)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.NoAnalysisFor", "{0} has no code analysis here."), LanguageLabel(item.Language)), StatusSeverity.Info);
            return;
        }

        var cts = new CancellationTokenSource();
        _runCts = cts;
        _busy = true;
        var clock = Stopwatch.StartNew();
        lock (_stateLock)
        {
            ClearOutputLocked();
            _findings.Clear();
            _analysis = null;
            _analysisTarget = item.Name;
            _analysisEstimate = _analysisDurations.TryGetValue(item.Path, out double seconds) ? TimeSpan.FromSeconds(seconds) : null;
            _runClock = clock;
            _outputTab = OutputTab.Analysis;
        }

        SetStatus(string.Format(UiText.Get("DevelopmentStudio.RunningAnalysis", "Analyzing {0}..."), item.Name), StatusSeverity.Info);
        _outputListBox.SetFocus();
        RebuildView();

        _ = Task.Run(async () =>
        {
            try
            {
                AppendOutput($"$ {command.Display}");
                int exitCode = await _runner.RunAsync(command, AppendOutput, cts.Token);

                TimeSpan elapsed;
                List<BuildDiagnostic> findings;
                lock (_stateLock)
                {
                    clock.Stop();
                    elapsed = clock.Elapsed;

                    // The analyzers report the same finding once per target framework: it is listed once.
                    findings = _output.Select(BuildDiagnosticParser.Parse).OfType<BuildDiagnostic>().Distinct()
                        .OrderBy(finding => finding.IsError ? 0 : 1).ThenBy(finding => finding.File, StringComparer.OrdinalIgnoreCase).ThenBy(finding => finding.Line)
                        .ToList();
                }

                int errors = findings.Count(finding => finding.IsError);
                bool cancelled = cts.IsCancellationRequested;
                var summary = new AnalysisSummary(item.Name, errors, findings.Count - errors, elapsed, cancelled, exitCode);
                string tookText = string.Format(UiText.Get("DevelopmentStudio.Elapsed", "Elapsed {0}"), FormatElapsed(elapsed));
                lock (_stateLock)
                {
                    _findings.AddRange(findings);
                    _analysis = summary;
                    _outputTab = OutputTab.Analysis;
                    if (!cancelled)
                    {
                        _analysisDurations[item.Path] = elapsed.TotalSeconds;
                        _settings.Set(StudioSettings.AnalysisDurationsKey, StudioSettings.EncodeDurations(_analysisDurations));
                    }
                }

                AppendOutput(tookText);
                if (cancelled)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.AnalysisCancelled", "Code analysis of {0} cancelled."), item.Name) + " " + tookText, StatusSeverity.Info);
                }
                else if (exitCode != 0 && errors == 0)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.AnalysisFailed", "Code analysis of {0} failed (exit {1}). The output says why."), item.Name, exitCode) + " " + tookText, StatusSeverity.Error);
                }
                else
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.AnalysisSummary", "Code analysis of {0}: {1} error(s), {2} warning(s). Elapsed {3}."),
                        item.Name, errors, findings.Count - errors, FormatElapsed(elapsed)), errors > 0 ? StatusSeverity.Error : findings.Count > 0 ? StatusSeverity.Info : StatusSeverity.Success);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or IOException)
            {
                SetStatus(exception.Message, StatusSeverity.Error);
            }
            finally
            {
                // The view is rebuilt before the run is marked finished, so anyone who waits for it sees the result.
                lock (_stateLock) clock.Stop();
                RebuildView();
                _busy = false;
                _runCts = null;
                StateHasChanged();
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// Enter, or a click, in the Analysis tab: opens the file of the finding under the cursor at its line and column. Row 0 is the
    /// summary, so the finding is the row less one.
    /// </summary>
    private void OpenAnalysisAtCursor()
    {
        if (_outputTab != OutputTab.Analysis) return;
        int row = (int)_outputListBox.ScrollableModel.CursorPosition.Y;
        BuildDiagnostic? finding;
        lock (_stateLock) finding = row >= 1 && row - 1 < _findings.Count ? _findings[row - 1] : null;
        if (finding is not null) OpenDiagnostic(finding);
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
        if (item.Language == DevelopmentLanguage.DotNet) return new DotNetToolchain { Host = SelectedSdk?.HostPath ?? "dotnet", Configuration = _configuration };
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
        var clock = Stopwatch.StartNew();
        lock (_stateLock)
        {
            ClearOutputLocked();
            _runClock = clock;
        }

        SetStatus(string.Format(UiText.Get("DevelopmentStudio.Running", "Running {0} {1}..."), verb, targetLabel), StatusSeverity.Info);
        _outputTab = OutputTab.Output;
        _outputListBox.SetFocus();
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

                // The run's time stops with its last command; the output and the status both say how long it took.
                TimeSpan elapsed;
                lock (_stateLock)
                {
                    clock.Stop();
                    elapsed = clock.Elapsed;
                }

                string tookText = string.Format(UiText.Get("DevelopmentStudio.Elapsed", "Elapsed {0}"), FormatElapsed(elapsed));
                AppendOutput(tookText);

                if (cts.IsCancellationRequested)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.Cancelled", "{0} cancelled."), verb) + " " + tookText, StatusSeverity.Info);
                }
                else if (verb == "test" && runTotals.Total > 0)
                {
                    string summary = string.Format(UiText.Get("DevelopmentStudio.TestTotals", "Tests: {0} passed, {1} failed, {2} skipped."), runTotals.Passed, runTotals.Failed, runTotals.Skipped);
                    AppendOutput(summary);
                    SetStatus(summary + " " + tookText, runTotals.Failed == 0 && firstFailure == 0 ? StatusSeverity.Success : StatusSeverity.Error);
                }
                else if (firstFailure == 0)
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.Succeeded", "{0} succeeded: {1}."), verb, targetLabel) + " " + tookText, StatusSeverity.Success);
                }
                else
                {
                    SetStatus(string.Format(UiText.Get("DevelopmentStudio.FailedExit", "{0} failed (exit {1})."), verb, firstFailure) + " " + tookText, StatusSeverity.Error);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or InvalidOperationException or IOException)
            {
                SetStatus(exception.Message, StatusSeverity.Error);
            }
            finally
            {
                lock (_stateLock) clock.Stop();
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

    /// <summary>
    /// N: the NuGet packages of the selected solution or .NET project. A project opens with its own scope chosen, inside its
    /// solution when it has one.
    /// </summary>
    private async Task OpenNuGetManagerAsync()
    {
        var item = SelectedItem;
        string? target = item?.Kind switch
        {
            DevelopmentProjectKind.Solution => item.Path,
            DevelopmentProjectKind.DotNetProject => item.SolutionPath ?? item.Path,
            _ => null,
        };
        if (item is null || target is null)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NuGetNoTarget", "Select a .NET solution or project to manage its NuGet packages."), StatusSeverity.Info);
            return;
        }

        var manager = new NuGetManagerDialog(_nuGetPackages, _prompts, ThemeService)
        {
            TargetPath = target,
            InitialProjectPath = item.Kind == DevelopmentProjectKind.DotNetProject ? item.Path : null,
            DotnetHost = SelectedSdk?.HostPath ?? "dotnet",
        };
        await ShowDialogAsync(manager);
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

    /// <summary>Ctrl+,: the studio's settings, in tabs. Each change is saved as it is made; when the dialog closes the studio reads them again.</summary>
    private async Task OpenSettingsAsync()
    {
        var settings = new DevelopmentStudioSettingsDialog(_settings, _sdks, ThemeService)
        {
            Closed = ReloadSettings,
        };
        await ShowDialogAsync(settings);
    }

    /// <summary>
    /// Applies what the settings dialog saved to the open studio: the SDK, the SBOM format, the build configuration, the explorer
    /// and output tabs, the layout and the analysis estimates. The studio keeps its own copies of these, so each one is read again here.
    /// </summary>
    private void ReloadSettings()
    {
        _sbomFormat = ParseSetting(_settings.Get(StudioSettings.SbomFormatKey), SbomFormat.Xml);
        _configuration = ParseSetting(_settings.Get(StudioSettings.BuildConfigurationKey), _configuration);

        string? sdkHost = _settings.Get(StudioSettings.SdkHostKey);
        int sdkIndex = sdkHost is null ? -1 : _sdks.ToList().FindIndex(sdk => sdk.HostPath.Equals(sdkHost, StringComparison.OrdinalIgnoreCase));
        if (sdkIndex >= 0) _sdkIndex = sdkIndex;

        var explorerMode = ParseSetting(_settings.Get(StudioSettings.ExplorerModeKey), _explorerMode);
        if (explorerMode != _explorerMode)
        {
            _explorerMode = explorerMode;
            RebuildExplorer();
            if (_explorerMode == ExplorerMode.Files && !_fileTreeBuilt) _ = LoadFileTreeAsync();
        }

        var outputTab = ParseSetting(_settings.Get(StudioSettings.OutputTabKey), _outputTab);
        if (outputTab != _outputTab)
        {
            _outputTab = outputTab;
            RebuildOutputPanel();
            _outputListBox.ScrollableModel.CursorPosition.Y = 0;
            _outputListBox.ScrollableModel.ScrollPosition.Y = 0;
        }

        _layout = StudioPanelLayout.Parse(_settings.Get(StudioSettings.LayoutKey));
        RebuildDock();

        lock (_stateLock)
        {
            _analysisDurations.Clear();
            foreach ((string path, double seconds) in StudioSettings.DecodeDurations(_settings.Get(StudioSettings.AnalysisDurationsKey)))
            {
                _analysisDurations[path] = seconds;
            }
        }

        RebuildView();
        StateHasChanged();
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
        _settings.Set(StudioSettings.SdkHostKey, chosen.HostPath);
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
        _settings.Set(StudioSettings.SbomFormatKey, _sbomFormat.ToString());
        SetStatus(string.Format(UiText.Get("Solution.SbomFormat", "SBOM format: {0}."), _sbomFormat == SbomFormat.Json ? "JSON" : "XML"), StatusSeverity.Info);
    }

    /// <summary>
    /// D: switches the build configuration between Debug and Release. It applies to the next .NET command; a command already
    /// running keeps the configuration it started with (the toolchain is made per command, see <see cref="ToolchainFor"/>).
    /// </summary>
    private void ToggleBuildConfiguration()
    {
        _configuration = _configuration == BuildConfiguration.Debug ? BuildConfiguration.Release : BuildConfiguration.Debug;
        _settings.Set(StudioSettings.BuildConfigurationKey, _configuration.ToString());
        SetStatus(string.Format(UiText.Get("DevelopmentStudio.ConfigurationSet", "Build configuration: {0}."), _configuration), StatusSeverity.Info);
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

        if (_configuration == BuildConfiguration.Release)
        {
            SetStatus(UiText.Get("DevelopmentStudio.ReleaseNoDebug", "Debugging is disabled in the Release configuration. D switches to Debug; Ctrl+F5 runs without debugging."), StatusSeverity.Info);
            return;
        }

        var item = MainItem ?? SelectedItem;
        if (item is null || item.Kind == DevelopmentProjectKind.Solution)
        {
            SetStatus(UiText.Get("DevelopmentStudio.SelectToDebug","Select a project or a script to debug. A solution cannot be debugged."), StatusSeverity.Info);
            return;
        }

        if (_busy)
        {
            SetStatus(UiText.Get("DevelopmentStudio.BusyDebug", "A command is still running; X cancels it before debugging."), StatusSeverity.Info);
            return;
        }

        // The build's output is what the user looks at first: the Output tab shows it, and the panel takes the focus.
        _outputTab = OutputTab.Output;
        _outputListBox.SetFocus();
        _ = StartDebuggingAsync(item, ToolchainFor(item), debugTests: _explorerMode == ExplorerMode.Tests);
    }

    private async Task StartDebuggingAsync(DevelopmentProject item, IDevelopmentToolchain toolchain, bool debugTests)
    {
        _busy = true;
        var cts = new CancellationTokenSource();
        _runCts = cts;
        try
        {
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
                if (top is { Path.Length: > 0 } stopFrame) _reveal = (stopFrame.Path, stopFrame.Line, 0);
            }

            if (openFile) OpenSourceAndWait(top!.Path);
            RebuildView();
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.StoppedAt", "Stopped ({0}) at {1}:{2}."), stop.Reason, top is null ? "?" : Path.GetFileName(top.Path), top?.Line ?? 0), StatusSeverity.Info);
        }
        catch (Exception exception) when (exception is DapException or OperationCanceledException or InvalidOperationException or ObjectDisposedException)
        {
            SetStatus(string.Format(UiText.Get("DevelopmentStudio.StackFailed", "Could not read the program's state: {0}"), exception.Message), StatusSeverity.Error);
        }
    }

    /// <summary>Loads the stopped file for the editor. A stop is not a key handler, so the short read is allowed here; the editor takes the file on its idle pass.</summary>
    private void OpenSourceAndWait(string path)
    {
        LoadedSource loaded = ReadSource(path);
        lock (_stateLock)
        {
            _sourceRequest = path;
            _loadedSource = loaded;
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
        int footerRow = (int)ViewSize.Height - 2 - _hotKeyRows;

        DrawHeader(buffer, x0 + 1, y0 + 1, innerWidth, colorScheme);
        DrawFooter(buffer, x0 + 1, y0 + footerRow, innerWidth, colorScheme);
    }

    #region Editing

    /// <summary>A stored setting as its enum value, or the fallback when it is missing or names no value of the enum.</summary>
    private static T ParseSetting<T>(string? text, T fallback) where T : struct, Enum =>
        Enum.TryParse(text, out T value) && Enum.IsDefined(value) ? value : fallback;

    /// <summary>Whether a file is open in the editor.</summary>
    private bool SourceOpen
    {
        get
        {
            lock (_stateLock) return _sourcePath.Length > 0;
        }
    }

    /// <summary>Whether the open file has edits not yet saved.</summary>
    private bool IsSourceDirty => _editor.IsDirty;

    /// <summary>
    /// Whether a key goes to the editor: it has the focus and a file is open. The function keys and Esc stay the studio's, so the
    /// debugger, the panels and the close key work from the editor; a key the editor declines goes on to the studio's commands.
    /// </summary>
    private bool IsEditorKey(KeyEvent keyEvent) =>
        FocusedPanelKind() == StudioPanelKind.Editor && SourceOpen
        && keyEvent.ConsoleKey is not (ConsoleKey.Escape or ConsoleKey.F5 or ConsoleKey.F6 or ConsoleKey.F7 or ConsoleKey.F8
            or ConsoleKey.F9 or ConsoleKey.F10 or ConsoleKey.F11);

    /// <summary>
    /// Ctrl+S: writes the open file on a background task. The editor is clean again on the idle pass, if its text is still the text
    /// that was written; typing after the save leaves it dirty.
    /// </summary>
    private void SaveSource()
    {
        string path;
        lock (_stateLock) path = _sourcePath;
        if (path.Length == 0) return;
        if (!IsSourceDirty)
        {
            SetStatus(UiText.Get("DevelopmentStudio.NothingToSave", "No unsaved changes."), StatusSeverity.Info);
            return;
        }

        string text = TextOfSource();
        _ = Task.Run(() =>
        {
            string? error = null;
            try
            {
                File.WriteAllText(path, text);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                error = exception.Message;
            }

            if (error is null)
            {
                lock (_stateLock) _savedWrite = (path, text);
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) _languageServer.SaveFile(ServerFolder, path, text);
            }

            SetStatus(error is null
                ? string.Format(UiText.Get("DevelopmentStudio.SavedFile", "Saved {0}."), Path.GetFileName(path))
                : string.Format(UiText.Get("DevelopmentStudio.SaveFailed", "Save failed: {0}"), error),
                error is null ? StatusSeverity.Success : StatusSeverity.Error);
        }, CancellationToken.None);
    }

    /// <summary>On the idle pass: clears the editor's dirty flag when its text is still the text the last save wrote.</summary>
    private void ApplySavedText()
    {
        (string Path, string Text)? saved;
        string path;
        lock (_stateLock)
        {
            saved = _savedWrite;
            _savedWrite = null;
            path = _sourcePath;
        }

        if (saved is not { } written || !written.Path.Equals(path, StringComparison.OrdinalIgnoreCase)) return;
        if (IsSourceDirty && TextOfSource() == written.Text) _editor.IsDirty = false;
    }

    #endregion

    #region Panels and layout

    /// <summary>Rebuilds what shows the errors: the Errors tab of the output panel, when it is the one shown.</summary>
    private void RebuildErrorPanel() => RebuildOutputPanel();

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
        int row = (int)_outputListBox.ScrollableModel.CursorPosition.Y;
        List<BuildDiagnostic> merged = MergedDiagnostics();
        if (row < 0 || row >= merged.Count) return;
        OpenDiagnostic(merged[row]);
    }

    /// <summary>Opens the file a diagnostic names, at its line and column.</summary>
    private void OpenDiagnostic(BuildDiagnostic diagnostic)
    {
        string folder = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        string path;
        try
        {
            // The tools print paths relative to the folder they ran in, which is the studio's folder or the solution's.
            // A diagnostic's path is usually absolute already; a base path is only for a relative one, and it must be absolute itself.
            path = Path.IsPathRooted(diagnostic.File) || !Path.IsPathRooted(folder)
                ? Path.GetFullPath(diagnostic.File)
                : Path.GetFullPath(diagnostic.File, folder);
        }
        catch (ArgumentException)
        {
            return;
        }

        OpenSource(path, diagnostic.Line, diagnostic.Column);
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
                SaveLayout();
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
        if (delta == 0 || !_layout.Resize(kind, delta)) return;
        SaveLayout();
        RebuildDock();
    }

    /// <summary>Writes the panel arrangement to the settings, so the next studio opens with it.</summary>
    private void SaveLayout() => _settings.Set(StudioSettings.LayoutKey, _layout.Serialize());

    #endregion

    /// <summary>The editor's band: the open file's name and the cursor's line and column, or "Source" while no file is open.</summary>
    private string EditorTitle()
    {
        string path;
        lock (_stateLock) path = _sourcePath;
        if (path.Length == 0) return UiText.Get("DevelopmentStudio.SourceHeading", "Source");

        var cursor = _editor.ScrollableModel.CursorPosition;
        string position = string.Format(UiText.Get("DevelopmentStudio.EditorPosition", "Ln {0}, Col {1}"), (int)cursor.Y + 1, (int)cursor.X + 1);
        return $"{Path.GetFileName(path)}{(IsSourceDirty ? " *" : string.Empty)}   {position}";
    }

    /// <summary>The Solution Explorer's band: the heading of the mode F6 has shown.</summary>
    private string ExplorerHeading() => ExplorerModeHeading(_explorerMode);

    /// <summary>The name of an explorer mode as the studio shows it.</summary>
    public static string ExplorerModeHeading(ExplorerMode mode) => mode switch
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

    /// <summary>The name of an output tab as the studio shows it.</summary>
    public static string OutputTabHeading(OutputTab tab) => tab switch
    {
        OutputTab.Output => UiText.Get("DevelopmentStudio.OutputHeading", "Output"),
        OutputTab.Errors => UiText.Get("DevelopmentStudio.ErrorsTab", "Errors"),
        OutputTab.Analysis => UiText.Get("DevelopmentStudio.AnalysisTab", "Analysis"),
        OutputTab.Coverage => UiText.Get("DevelopmentStudio.CoverageHeading", "Coverage"),
        OutputTab.Profile => UiText.Get("DevelopmentStudio.ProfileHeading", "Profile"),
        _ => UiText.Get("DevelopmentStudio.DebugHeading", "Debug"),
    };

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        // The menu titles lead the header, each in its own cell (see MenuBar.cs); the folder and its state follow them.
        int column = 0;
        foreach (MenuBarTitle title in Enum.GetValues<MenuBarTitle>())
        {
            string cell = MenuTitleCell(title);
            WriteLine(buffer, x + column, y, w - column, cell, colorScheme.Foreground, colorScheme.Background);
            column += cell.Length;
        }

        string folder = string.IsNullOrEmpty(Folder) ? Environment.CurrentDirectory : Folder;
        string debugState = _session is null ? string.Empty : _paused ? "   ● paused" : "   ▶ running";
        string busy = _busy ? "   * " + (RunningElapsedText() ?? string.Empty) : string.Empty;
        WriteLine(buffer, x + column, y, w - column, $"   {folder}   ({_items.Count} items){debugState}{busy}", Color.Cyan, colorScheme.Background);
    }

    /// <summary>
    /// The context the hot key bar follows: the layout mode, the editor while a file is open, the output tab shown while the
    /// output panel has the focus, or the explorer. Anything else (the default line) lists the studio's main commands.
    /// </summary>
    private HotKeyContext CurrentHotKeyContext()
    {
        if (_layoutMode) return HotKeyContext.Layout;
        return FocusedPanelKind() switch
        {
            StudioPanelKind.Editor when SourceOpen => HotKeyContext.Editor,
            StudioPanelKind.Output => _outputTab switch
            {
                OutputTab.Errors => HotKeyContext.Errors,
                OutputTab.Analysis => HotKeyContext.Analysis,
                OutputTab.Debug => HotKeyContext.Debug,
                OutputTab.Coverage => HotKeyContext.Coverage,
                OutputTab.Profile => HotKeyContext.Profile,
                _ => HotKeyContext.Output,
            },
            StudioPanelKind.Explorer => HotKeyContext.Explorer,
            _ => HotKeyContext.Default,
        };
    }

    /// <summary>The hot key bar's text for a context: its commands as "Key:action", two spaces between them.</summary>
    private static string HotKeyText(HotKeyContext context) => context switch
    {
        HotKeyContext.Layout => UiText.Get("Hints.DevelopmentStudioLayout", "Arrows:move the panel to that edge  Shift+arrows:resize it  Esc or F8:end the layout mode"),
        HotKeyContext.Editor => UiText.Get("Hints.DevelopmentStudioEditor", "Type to edit  Ctrl+S:save  Ctrl+R:refresh  F5:debug  Shift+F5:stop  F9:breakpoint  F10/F11:step  Shift+F11:step out  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Explorer => UiText.Get("Hints.DevelopmentStudioExplorer", "Up/Down:move  Enter:open, run a test or expand  Left/Right:collapse or expand  Space:check  A:check all  Shift+T:check tests  B/C/T/R:build/clean/test/run  Ctrl+F5:run  F5:debug  M:main  V:SDK  K:SDKs  N:analyze  U:NuGet  S/F:SBOM  O/P:coverage/profile  F6:explorer tab  F7:output tab  F8:layout  Ctrl+R:refresh  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Output => UiText.Get("Hints.DevelopmentStudioOutput", "Up/Down/PgUp/PgDn:scroll  B/C/T/R:build/clean/test/run  F5:debug  Shift+F5:stop  X:cancel  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Errors => UiText.Get("Hints.DevelopmentStudioErrors", "Up/Down/PgUp/PgDn:scroll  Enter:open the error at its line  X:cancel  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Analysis => UiText.Get("Hints.DevelopmentStudioAnalysis", "Up/Down/PgUp/PgDn:scroll  Enter or click:open the finding at its line  N:analyze  X:cancel  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Debug => UiText.Get("Hints.DevelopmentStudioDebug", "Up/Down/PgUp/PgDn:scroll  W:watch the word under the cursor  Delete:remove the watch  F5:start or continue  Shift+F5:stop  F9:breakpoint  F10/F11:step  Shift+F11:step out  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Coverage => UiText.Get("Hints.DevelopmentStudioCoverage", "Up/Down/PgUp/PgDn:scroll  G:open the file under the cursor  O:measure coverage  X:cancel  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        HotKeyContext.Profile => UiText.Get("Hints.DevelopmentStudioProfile", "Up/Down/PgUp/PgDn:scroll  P:profile the selected script  X:cancel  F6:explorer tab  F7:output tab  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
        _ => UiText.Get("Hints.DevelopmentStudio", "F5:debug  F9:breakpoint  F10/F11:step  Shift+F5:stop  Space:check  B/C/T/R:build/clean/test/run  V:SDK  K:SDKs  N:analyze  U:NuGet  S/F:SBOM  O/P:coverage/profile  F6/F7:tabs  F8:layout  Ctrl+,:settings  Ctrl+W:close"),
    };

    /// <summary>The commands of the hot key bar now: the ones of the focused panel, in the order they are listed.</summary>
    public IReadOnlyList<string> HotKeyCommands => SplitHotKeys(HotKeyText(CurrentHotKeyContext()));

    /// <summary>Splits a hot key text into its commands: they are separated by two spaces, and a command has single spaces only.</summary>
    private static List<string> SplitHotKeys(string text) => text.Split("  ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>
    /// The rows a hot key text takes at a width. The text is cut between its commands, so no command is split across two rows;
    /// a command wider than the row is cut by the row.
    /// </summary>
    private static List<string> WrapHotKeys(string text, int width)
    {
        var rows = new List<string>();
        var row = new StringBuilder();
        foreach (string command in SplitHotKeys(text))
        {
            if (row.Length > 0 && row.Length + 2 + command.Length > width)
            {
                rows.Add(row.ToString());
                row.Clear();
            }

            if (row.Length > 0) row.Append("  ");
            row.Append(command);
        }

        if (row.Length > 0) rows.Add(row.ToString());
        return rows;
    }

    /// <summary>
    /// Draws the footer: the status on its own row, then the hot key rows of the focused panel's commands. The rows the bar
    /// takes are the same whatever the focus, and a command that does not fit the rows is cut at the edge.
    /// </summary>
    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        Color statusForeground = _statusSeverity switch
        {
            StatusSeverity.Error => colorScheme.Error,
            StatusSeverity.Success => Color.Green,
            _ => colorScheme.Foreground,
        };
        WriteLine(buffer, x, y, w, _status, statusForeground, colorScheme.Background);

        // The status row and the hot key rows end on the frame's second to last row, as Draw placed them.
        List<string> rows = WrapHotKeys(HotKeyText(CurrentHotKeyContext()), w);
        for (int row = 0; row < _hotKeyRows; row++)
        {
            WriteLine(buffer, x, y + 1 + row, w, row < rows.Count ? rows[row] : string.Empty, colorScheme.Foreground, colorScheme.Background);
        }
    }

    #endregion
}
