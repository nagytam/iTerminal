using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Dialogs.Git;

/// <summary>
/// Full-screen modal git client: changed-files (unstaged), staging-area (staged), branches,
/// commit-history and stash panels on the left, a live diff view on the right. All git work runs
/// on background tasks; the key handler and <see cref="Draw"/> never block on git.
/// </summary>
public class GitClientDialog : SplitPaneDialog
{
    /// <summary>The five left-hand list sections, in the order they are stacked and tabbed through.</summary>
    private enum Section
    {
        Unstaged = 0,
        Staged = 1,
        Branches = 2,
        Commits = 3,
        Stashes = 4,
    }

    /// <summary>Drives footer coloring without matching substrings of the message text.</summary>
    private enum MessageSeverity { Info, Success, Error }

    private sealed class GitFileListBox : ListBox<GitFileStatus>
    {
        private readonly bool _staged;

        public GitFileListBox(IListBoxModel<GitFileStatus> listModel, IThemeService themeService, bool staged)
            : base(listModel, themeService)
        {
            _staged = staged;
            ListModel.TransformItem = TransformFile;
        }

        private ListItem<GitFileStatus> TransformFile(GitFileStatus file, CancellationToken cancellationToken) =>
            new(file) { Value = $" {file.Describe(_staged)} {file.Path}".Shrink(Math.Max(1, (int)ViewSize.Width)) ?? string.Empty, IsEnabled = true };

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var file = ListModel.ListItems[index].Item;
            if (file.IsConflicted)
            {
                return Color.Red;
            }

            if (file.IsUntracked)
            {
                return Color.Green;
            }

            var status = _staged ? file.IndexStatus : file.WorkTreeStatus;
            return status switch
            {
                'A' => Color.Green,
                'M' => Color.Yellow,
                'D' => Color.Red,
                'R' or 'C' => Color.Magenta,
                'U' => Color.Red,
                _ => null,
            };
        }
    }

    private sealed class GitBranchListBox : ListBox<GitBranch>
    {
        public GitBranchListBox(IListBoxModel<GitBranch> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = TransformBranch;
        }

        private ListItem<GitBranch> TransformBranch(GitBranch branch, CancellationToken cancellationToken) =>
            new(branch) { Value = $" {(branch.IsCurrent ? "*" : " ")} {branch.Name}".Shrink(Math.Max(1, (int)ViewSize.Width)) ?? string.Empty, IsEnabled = true };

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var branch = ListModel.ListItems[index].Item;
            if (branch.IsCurrent) return Color.Green;
            // Remote-tracking rows are recessive: checking one out creates a local branch, which is a
            // bigger step than switching to one that already exists.
            return branch.IsRemote ? Color.DarkGray : null;
        }
    }

    /// <summary>
    /// Read-only diff view. It takes focus so a long patch can be read with the arrow keys, but it
    /// never shows a cursor row — it is a colored text pane, not a picker, and a highlighted line
    /// would read as a selection the user could act on.
    /// </summary>
    private sealed class GitDiffListBox : ListBox<string>
    {
        public GitDiffListBox(IListBoxModel<string> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = (line, ct) => new ListItem<string>(line) { Value = line, IsEnabled = true };
        }

        public override VisualState GetVisualState(int index) => VisualState.Normal;

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var line = ListModel.ListItems[index].Item;
            if (line.StartsWith("@@", StringComparison.Ordinal)) return Color.Cyan;
            if (line.StartsWith("+++", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal)) return Color.DarkGray;
            if (line.StartsWith("diff ", StringComparison.Ordinal) || line.StartsWith("index ", StringComparison.Ordinal)) return Color.DarkGray;
            if (line.StartsWith('+')) return Color.Green;
            if (line.StartsWith('-')) return Color.Red;
            return null;
        }
    }

    private const int MaxCommits = 100;
    private const int SectionCount = 5;

    /// <summary>Rows the history asks for before the remaining space is shared out. It absorbs the slack afterwards.</summary>
    private const int PreferredCommitRows = 10;

    /// <summary>Rows the section being worked in is guaranteed before anything else is shared out.</summary>
    private const int MinActiveSectionRows = 3;

    private readonly IGitService _gitService;
    private readonly IContentModelService _contentModelService;
    private readonly IDateTimeProviderService _dateTimeProviderService;

    private readonly ConcurrentObservableCollection<GitFileStatus> _unstagedItems = new();
    private readonly ConcurrentObservableCollection<GitFileStatus> _stagedItems = new();
    private readonly ConcurrentObservableCollection<GitBranch> _branchItems = new();
    private readonly ConcurrentObservableCollection<GitCommit> _commitItems = new();
    private readonly ConcurrentObservableCollection<GitStash> _stashItems = new();
    private readonly ConcurrentObservableCollection<string> _diffItems = new();

    private readonly GitFileListBox _unstagedListBox;
    private readonly GitFileListBox _stagedListBox;
    private readonly GitBranchListBox _branchesListBox;
    private readonly ListBox<GitCommit> _commitsListBox;
    private readonly ListBox<GitStash> _stashesListBox;
    private readonly GitDiffListBox _diffListBox;
    private readonly TextBox _inputBox;

    /// <summary>The five sections by <see cref="Section"/> index, for focus and layout bookkeeping.</summary>
    private readonly IControl[] _sections;

    /// <summary>Dialog-relative row of each section's header band, filled in by <see cref="ApplySectionLayout"/> and read back by <see cref="Draw"/>.</summary>
    private readonly int[] _sectionTops = new int[SectionCount];
    private readonly int[] _sectionRows = new int[SectionCount];

    private string _startDirectory = string.Empty;
    private string? _root;
    private bool _loading;
    private bool _busy;
    private bool _mergeInProgress;
    private string _message = string.Empty;
    private MessageSeverity _messageSeverity = MessageSeverity.Info;

    /// <summary>
    /// Which list the detail pane follows. Not simply "the focused list": the diff pane is itself
    /// focusable, and tabbing into it to scroll a patch must not blank the patch being read.
    /// </summary>
    private Section _activeSection = Section.Unstaged;

    private GitRepositoryStatus _status = GitRepositoryStatus.Empty;
    private IReadOnlyList<GitCommit> _commits = [];
    private IReadOnlyList<GitBranch> _branches = [];
    private IReadOnlyList<GitStash> _stashes = [];

    private CancellationTokenSource? _diffCts;

    /// <summary>What the detail pane is currently showing, for its header band.</summary>
    private string _detailTitle = "Details";

    /// <summary>
    /// Set while the detail pane holds a command's failure output rather than a diff. Git's errors
    /// are routinely several lines (rejected pushes, conflicted merges) and the one-line footer can
    /// only ever show the first of them.
    /// </summary>
    private bool _detailShowsError;

    private Action<string>? _onInputSubmit;
    private IControl? _restoreFocusTo;

    /// <summary>
    /// True while the inline commit/branch/stash input owns the footer row. Kept separately from the
    /// text box's own <see cref="IControl.IsVisible"/> because <see cref="Control.ShowAsync"/> makes
    /// every child visible on the way in — a footer that thinks the input is up never draws its hints.
    /// </summary>
    private bool _inputActive;

    private string _confirmPrompt = string.Empty;
    private Action? _confirmAction;

    private bool _helpVisible;
    private int _helpScroll;

    /// <summary>
    /// The background task started by the most recent refresh or command, completing once the panels
    /// have been repainted from it. Nothing in the dialog awaits this — the whole point is that key
    /// handlers return immediately — but a test has to be able to wait for the work it just asked for.
    /// </summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public GitClientDialog(
        IGitService gitService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(GitClientDialog), themeService)
    {
        _gitService = gitService;
        _contentModelService = contentModelService;
        _dateTimeProviderService = dateTimeProviderService;
        Title = "Git";

        _unstagedListBox = new GitFileListBox(new ListBoxModel<GitFileStatus>(_unstagedItems), themeService, staged: false);
        _stagedListBox = new GitFileListBox(new ListBoxModel<GitFileStatus>(_stagedItems), themeService, staged: true);
        _branchesListBox = new GitBranchListBox(new ListBoxModel<GitBranch>(_branchItems), themeService);
        _commitsListBox = new ListBox<GitCommit>(
            new ListBoxModel<GitCommit>(_commitItems)
            {
                TransformItem = (commit, ct) => new ListItem<GitCommit>(commit) { Value = $" {commit.ShortHash} {commit.Subject}", IsEnabled = true },
            },
            themeService);
        _stashesListBox = new ListBox<GitStash>(
            new ListBoxModel<GitStash>(_stashItems)
            {
                TransformItem = (stash, ct) => new ListItem<GitStash>(stash) { Value = $" {{{stash.Index}}} {stash.Message}", IsEnabled = true },
            },
            themeService);
        _diffListBox = new GitDiffListBox(new ListBoxModel<string>(_diffItems), themeService);
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };

        _sections = [_unstagedListBox, _stagedListBox, _branchesListBox, _commitsListBox, _stashesListBox];

        // An empty section says so from inside its own list: the list paints every one of its rows,
        // so a message drawn over it by the dialog lasted only until the list next repainted alone.
        _unstagedListBox.EmptyText = CleanWorkingTreeText;
        _stagedListBox.EmptyText = "  (nothing staged)";
        _branchesListBox.EmptyText = "  (no branches)";
        _commitsListBox.EmptyText = "  (no commits yet)";
        _stashesListBox.EmptyText = "  (no stashes)";

        AddControl(_unstagedListBox);
        AddControl(_stagedListBox);
        AddControl(_branchesListBox);
        AddControl(_commitsListBox);
        AddControl(_stashesListBox);
        AddControl(_diffListBox);
        AddControl(_inputBox);

        WireCursorChanged(_unstagedListBox);
        WireCursorChanged(_stagedListBox);
        WireCursorChanged(_branchesListBox);
        WireCursorChanged(_commitsListBox);
        WireCursorChanged(_stashesListBox);
    }

    /// <summary>Reloads the detail pane whenever the cursor moves within the section it is following.</summary>
    private void WireCursorChanged(IControl section)
    {
        var scrollableModel = section switch
        {
            ListBox<GitFileStatus> box => box.ScrollableModel,
            ListBox<GitBranch> box => box.ScrollableModel,
            ListBox<GitCommit> box => box.ScrollableModel,
            ListBox<GitStash> box => box.ScrollableModel,
            _ => null,
        };

        if (scrollableModel is null)
        {
            return;
        }

        scrollableModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IScrollableModel.CursorPosition) && ReferenceEquals(section, _sections[(int)_activeSection]))
            {
                StartLoadDiff();
            }
        };
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _startDirectory = _contentModelService.Url?.Path ?? Directory.GetCurrentDirectory();
        StartRefresh(reloadLog: true);
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();

        // Control.ShowAsync makes every child visible on its way in. The inline input owns the
        // footer row and is only ever shown on demand, so put it back where it belongs.
        ApplyInputVisibility();
    }

    public override void OnIdle()
    {
        base.OnIdle();

        // Focus can move without a key this dialog saw (mouse, a control taking it back), and the
        // detail pane follows the section that has it — so notice the move rather than assume Tab.
        SyncActiveSection();
    }

    #region Layout

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.95);
        }

        ApplySectionLayout();

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them (each control's own OnInitializeLayout sizes its ScrollableModel from
        // its own ViewSize) and focuses the first focusable child.
        base.OnInitializeLayout();
    }

    private int LeftColumnWidth(int interiorWidth) => Math.Max(24, interiorWidth * 42 / 100);

    /// <summary>
    /// Sizes the five sections from what they actually hold, rather than giving each a fixed fifth of
    /// the column. A clean staging area or an empty stash list then costs one header row instead of a
    /// fifth of the screen, which is what makes the file lists usable in a repository with more than a
    /// handful of changes.
    /// </summary>
    private void ApplySectionLayout()
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var midH = ih - 2; // minus the header row and the footer row
        if (midH > 0)
        {
            var leftW = LeftColumnWidth(iw);
            var rightX = 1 + leftW + 1;
            var rightW = iw - leftW - 1;

            DistributeSectionRows(midH);

            var y = 2; // interior offset (1) + header row (1)
            _sectionTops[(int)Section.Unstaged] = y;
            y = PlaceSection(_unstagedListBox, leftW, y, _sectionRows[(int)Section.Unstaged]);
            _sectionTops[(int)Section.Staged] = y;
            y = PlaceSection(_stagedListBox, leftW, y, _sectionRows[(int)Section.Staged]);
            _sectionTops[(int)Section.Branches] = y;
            y = PlaceSection(_branchesListBox, leftW, y, _sectionRows[(int)Section.Branches]);
            _sectionTops[(int)Section.Commits] = y;
            y = PlaceSection(_commitsListBox, leftW, y, _sectionRows[(int)Section.Commits]);
            _sectionTops[(int)Section.Stashes] = y;
            PlaceSection(_stashesListBox, leftW, y, _sectionRows[(int)Section.Stashes]);

            _diffListBox.Position = new Position(rightX, 3);
            _diffListBox.ViewSize = rightW > 0 ? new Size(rightW, Math.Max(0, midH - 1)) : new Size(0, 0);
            _diffListBox.ScrollableModel.ViewSize = _diffListBox.ViewSize;
            _diffListBox.ScrollableModel.ScrollableSize = new Size(_diffListBox.ViewSize.Width, _diffListBox.ListModel.ListItems.Count);
        }

        _inputBox.Position = new Position(1, ih);
        _inputBox.ViewSize = new Size(iw, 1);
        _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;
    }

    /// <summary>Positions one section's list under its header band and returns the row the next header goes on.</summary>
    private static int PlaceSection<T>(ListBox<T> listBox, int width, int headerY, int rows)
    {
        listBox.Position = new Position(1, headerY + 1);
        listBox.ViewSize = new Size(Math.Max(0, width), Math.Max(0, rows));
        listBox.ScrollableModel.ViewSize = listBox.ViewSize;
        listBox.ScrollableModel.ScrollableSize = new Size(listBox.ViewSize.Width, listBox.ListModel.ListItems.Count);
        listBox.ScrollableModel.ScrollToVisible();
        return headerY + rows + 1;
    }

    /// <summary>
    /// Shares <paramref name="midH"/> rows between the five sections: one header row each, then a
    /// row at a time round-robin to every section that still wants one, so a long list cannot crowd
    /// out a short one. The section being worked in is served first, and the history takes whatever
    /// is left over.
    /// </summary>
    private void DistributeSectionRows(int midH)
    {
        Array.Clear(_sectionRows);

        var available = midH - SectionCount;
        if (available <= 0)
        {
            // Too short for five headers: fall back to an even split so every band is still reachable.
            for (var i = 0; i < SectionCount; i++)
            {
                _sectionRows[i] = Math.Max(0, midH / SectionCount - 1);
            }

            return;
        }

        var want = new[]
        {
            _status.Unstaged.Count,
            _status.Staged.Count,
            _branches.Count,
            Math.Min(_commits.Count, PreferredCommitRows),
            _stashes.Count,
        };

        // The section being worked in is served first — including when it is empty, so it has a row
        // to say so in rather than collapsing to a bare header the user reads as a drawing glitch.
        var active = (int)_activeSection;
        _sectionRows[active] = Math.Min(want[active] > 0 ? Math.Min(want[active], MinActiveSectionRows) : 1, available);
        available -= _sectionRows[active];

        var granted = true;
        while (available > 0 && granted)
        {
            granted = false;
            for (var i = 0; i < SectionCount && available > 0; i++)
            {
                if (_sectionRows[i] >= want[i])
                {
                    continue;
                }

                _sectionRows[i]++;
                available--;
                granted = true;
            }
        }

        // Whatever nothing asked for goes to the history, which is never really "full".
        var slackSection = _commits.Count > 0 ? (int)Section.Commits : (int)Section.Unstaged;
        _sectionRows[slackSection] += available;
    }

    #endregion

    #region Refresh (background)

    private const string CleanWorkingTreeText = "  (working tree clean)";
    private const string NoRepositoryText = "  Open a folder inside a repository in the panel, then press Ctrl+G again.";
    private void StartRefresh(bool reloadLog) => PendingOperation = Task.Run(() => RefreshAllAsync(reloadLog));

    private async Task RefreshAllAsync(bool reloadLog)
    {
        _loading = true;
        StateHasChanged();

        try
        {
            var root = await _gitService.GetRepositoryRootAsync(_startDirectory);
            if (root is null)
            {
                _root = null;
                // "(working tree clean)" under "Not a git repository" contradicted the line above it.
                _unstagedListBox.EmptyText = NoRepositoryText;
                SetMessage($"Not a git repository: {_startDirectory}", MessageSeverity.Error);
                _loading = false;
                StateHasChanged();
                return;
            }

            _root = root;
            _unstagedListBox.EmptyText = CleanWorkingTreeText;
            _status = await _gitService.GetStatusAsync(root);
            _branches = await _gitService.GetBranchesAsync(root, includeRemotes: true);
            _stashes = await _gitService.GetStashesAsync(root);
            _mergeInProgress = await _gitService.IsMergeInProgressAsync(root);
            if (reloadLog)
            {
                _commits = await _gitService.GetLogAsync(root, MaxCommits);
            }

            SyncListModels(reloadLog);
            _loading = false;
            StateHasChanged();
            await LoadDiffAsync();
        }
        catch (Exception exception)
        {
            SetMessage($"Error: {exception.Message}", MessageSeverity.Error);
            _loading = false;
            StateHasChanged();
        }
    }

    /// <summary>Pushes the freshly-loaded git snapshot into the list box models and clamps each cursor to the new counts.</summary>
    private void SyncListModels(bool reloadLog)
    {
        _unstagedItems.ReplaceAll(_status.Unstaged.ToList());
        _unstagedListBox.UpdateModel();
        ClampCursor(_unstagedListBox.ScrollableModel, _status.Unstaged.Count);

        _stagedItems.ReplaceAll(_status.Staged.ToList());
        _stagedListBox.UpdateModel();
        ClampCursor(_stagedListBox.ScrollableModel, _status.Staged.Count);

        _branchItems.ReplaceAll(_branches.ToList());
        _branchesListBox.UpdateModel();
        ClampCursor(_branchesListBox.ScrollableModel, _branches.Count);

        _stashItems.ReplaceAll(_stashes.ToList());
        _stashesListBox.UpdateModel();
        ClampCursor(_stashesListBox.ScrollableModel, _stashes.Count);

        if (reloadLog)
        {
            _commitItems.ReplaceAll(_commits.ToList());
            _commitsListBox.UpdateModel();
            ClampCursor(_commitsListBox.ScrollableModel, _commits.Count);
        }

        // The counts drive the section heights, so the listing and the layout are refreshed together.
        ApplySectionLayout();
    }

    private static void ClampCursor(IScrollableModel scrollableModel, int count)
    {
        scrollableModel.CursorPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.CursorPosition.Y, 0, count - 1);
        scrollableModel.ScrollPosition.Y = count <= 0 ? 0 : Math.Clamp(scrollableModel.ScrollPosition.Y, 0, count - 1);
    }

    /// <summary>
    /// Loads the detail pane off the calling thread. Every caller is a key handler, a focus change or
    /// the render loop's idle pass, none of which may pay for starting a git process.
    /// </summary>
    private void StartLoadDiff() => _ = Task.Run(LoadDiffAsync);

    private async Task LoadDiffAsync()
    {
        if (_root is null)
        {
            return;
        }

        _detailShowsError = false;
        _diffCts?.Cancel();
        var cts = new CancellationTokenSource();
        _diffCts = cts;

        var file = CurrentFile(out var staged);
        var commit = _activeSection == Section.Commits ? CursorItem(_commitsListBox) : null;
        if (file is null && commit is null)
        {
            SetDetailLines(DetailTitleFor(null, false, null), RightDetailLines());
            return;
        }

        var title = DetailTitleFor(file, staged, commit);
        SetDetailLines(title, ["Loading diff..."]);

        try
        {
            var diff = file is not null
                ? await _gitService.GetDiffAsync(_root, file.Path, staged, file.IsUntracked, cts.Token)
                : await _gitService.GetCommitDiffAsync(_root, commit!.ShortHash, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            SetDetailLines(title, diff.Replace("\r", string.Empty).Split('\n').ToList());
        }
        catch
        {
            // Stale/cancelled diff load — ignore.
        }
    }

    private string DetailTitleFor(GitFileStatus? file, bool staged, GitCommit? commit)
    {
        if (file is not null)
        {
            return $"{(staged ? "Staged" : "Working tree")}: {file.Path}";
        }

        if (commit is not null)
        {
            return $"Commit {commit.ShortHash}";
        }

        return _activeSection switch
        {
            Section.Branches => "Branch",
            Section.Stashes => "Stash",
            _ => "Details",
        };
    }

    private void SetDetailLines(string title, List<string> lines)
    {
        _detailTitle = title;
        _diffItems.ReplaceAll(lines);
        _diffListBox.UpdateModel();
        _diffListBox.ScrollableModel.ScrollPosition.Y = 0;
        _diffListBox.ScrollableModel.CursorPosition.Y = 0;
        StateHasChanged();
    }

    private List<string> RightDetailLines()
    {
        if (_activeSection == Section.Stashes)
        {
            var stash = CursorItem(_stashesListBox);
            return stash is null
                ? ["(no stashes)", string.Empty, "S stashes every change, untracked files included."]
                :
                [
                    $"stash@{{{stash.Index}}}",
                    stash.Message,
                    string.Empty,
                    "Space/Enter pop   a apply   x drop",
                ];
        }

        if (_activeSection == Section.Branches)
        {
            var branch = CursorItem(_branchesListBox);
            if (branch is null)
            {
                return [];
            }

            return branch.IsRemote
                ?
                [
                    $"remote branch {branch.Name}",
                    string.Empty,
                    "Space/Enter creates a local branch tracking it.",
                ]
                :
                [
                    $"branch {branch.Name}{(branch.IsCurrent ? "  (current)" : string.Empty)}",
                    branch.Upstream is null ? "upstream: (none)" : $"upstream: {branch.Upstream}",
                    string.Empty,
                    "Space/Enter checkout   m merge   D delete   n new",
                ];
        }

        var head = CursorItem(_commitsListBox);
        return head is null
            ? []
            :
            [
                $"commit {head.ShortHash}",
                $"Author: {head.Author}",
                $"Date:   {head.RelativeDate}",
                string.Empty,
                $"    {head.Subject}",
            ];
    }

    /// <summary>Puts a failed command's whole output in the detail pane — git's errors rarely fit the footer's one line.</summary>
    private void ShowErrorDetail(string title, string output)
    {
        var lines = output.Replace("\r", string.Empty).Split('\n').ToList();
        SetDetailLines(title, lines);
        _detailShowsError = true;
    }

    #endregion

    #region Mutating actions (background)

    private void ToggleStage()
    {
        if (_root is null || _busy)
        {
            return;
        }

        var file = CurrentFile(out var staged);
        if (file is null)
        {
            return;
        }

        if (file.IsConflicted)
        {
            // Staging a conflicted file is how git spells "I resolved this", so say so rather than
            // letting it look like an ordinary stage.
            RunAction($"Marking {file.Path} resolved", $"Resolved {file.Path}", root => _gitService.StageAsync(root, file.Path), reloadLog: false);
            return;
        }

        // Staging moves a row between two panels and leaves the history alone, so it does not pay
        // for a `git log` — this is the key pressed most often in the dialog.
        RunAction(
            staged ? $"Unstaging {file.Path}" : $"Staging {file.Path}",
            staged ? $"Unstaged {file.Path}" : $"Staged {file.Path}",
            root => staged ? _gitService.UnstageAsync(root, file.Path) : _gitService.StageAsync(root, file.Path),
            reloadLog: false);
    }

    private void ToggleStageAll()
    {
        if (_activeSection == Section.Staged)
        {
            if (_status.Staged.Count > 0)
            {
                RunAction("Unstaging everything...", "Unstaged everything.", root => _gitService.UnstageAllAsync(root), reloadLog: false);
            }

            return;
        }

        if (_activeSection == Section.Unstaged && _status.Unstaged.Count > 0)
        {
            RunAction("Staging everything...", $"Staged {_status.Unstaged.Count} file(s).", root => _gitService.StageAllAsync(root), reloadLog: false);
        }
    }

    private void CommitStagedFromInput(string message, bool amend)
    {
        message = message.Trim();
        if (message.Length == 0)
        {
            SetMessage("Commit cancelled — an empty message commits nothing.", MessageSeverity.Info);
            StateHasChanged();
            return;
        }

        RunAction(
            amend ? "Amending..." : "Committing...",
            amend ? "Amended." : "Committed.",
            root => _gitService.CommitAsync(root, message, amend));
    }

    /// <summary>
    /// Runs a mutating/remote git action in the background, then refreshes everything. A failure
    /// keeps its whole output: the footer shows the first line and the detail pane the rest.
    /// </summary>
    private void RunAction(string busyMessage, string successMessage, Func<string, Task<GitResult>> action, bool reloadLog = true)
    {
        if (_root is null || _busy)
        {
            return;
        }

        var root = _root;
        _busy = true;
        SetMessage(busyMessage, MessageSeverity.Info);
        StateHasChanged();

        PendingOperation = Task.Run(async () =>
        {
            GitResult result;
            try
            {
                result = await action(root);
            }
            catch (Exception exception)
            {
                result = GitResult.Fail(exception.Message);
            }

            _busy = false;
            await RefreshAllAsync(reloadLog);

            if (result.Success)
            {
                SetMessage(successMessage, MessageSeverity.Success);
            }
            else
            {
                var output = string.IsNullOrWhiteSpace(result.Output) ? "the command failed." : result.Output.Trim();
                SetMessage($"Error: {FirstLine(output)}", MessageSeverity.Error);
                ShowErrorDetail(busyMessage.TrimEnd('.'), output);
            }

            StateHasChanged();
        });
    }

    private static string FirstLine(string text)
    {
        var index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text : text[..index];
    }

    private void SetMessage(string message, MessageSeverity severity)
    {
        _message = message;
        _messageSeverity = severity;
    }

    private void CheckoutSelected()
    {
        var branch = CursorItem(_branchesListBox);
        if (branch is null || branch.IsCurrent)
        {
            return;
        }

        if (branch.IsRemote)
        {
            RunAction(
                $"Checking out {branch.Name}",
                $"Now on a local branch tracking {branch.Name}.",
                root => _gitService.CheckoutRemoteBranchAsync(root, branch.Name));
            return;
        }

        RunAction($"Checking out {branch.Name}", $"On {branch.Name}.", root => _gitService.CheckoutBranchAsync(root, branch.Name));
    }

    private void CreateBranchFromInputText(string name)
    {
        name = name.Trim();
        if (name.Length == 0)
        {
            return;
        }

        RunAction($"Creating {name}", $"Created and checked out {name}.", root => _gitService.CreateBranchAsync(root, name));
    }

    private void MergeSelected()
    {
        var branch = CursorItem(_branchesListBox);
        if (branch is null || branch.IsCurrent)
        {
            return;
        }

        RunAction($"Merging {branch.Name}", $"Merged {branch.Name}.", root => _gitService.MergeBranchAsync(root, branch.Name));
    }

    private void BeginDeleteBranch()
    {
        var branch = CursorItem(_branchesListBox);
        if (branch is null || branch.IsCurrent || branch.IsRemote)
        {
            return;
        }

        BeginConfirm($"Delete branch {branch.Name}, merged or not? (y/n)",
            () => RunAction($"Deleting {branch.Name}", $"Deleted {branch.Name}.", root => _gitService.DeleteBranchAsync(root, branch.Name, force: true)));
    }

    private void AbortMerge() => RunAction("Aborting merge...", "Merge aborted.", root => _gitService.AbortMergeAsync(root));

    private void Fetch() => RunAction("Fetching...", "Fetched.", root => _gitService.FetchAsync(root));

    private void Pull() => RunAction("Pulling...", "Pulled.", root => _gitService.PullAsync(root));

    /// <summary>
    /// Pushes the current branch, publishing it first when it has no upstream — which is the case a
    /// plain <c>git push</c> answers with a paragraph of advice rather than doing what was asked.
    /// </summary>
    private void Push()
    {
        var needsUpstream = _status.Upstream is null;
        RunAction(
            needsUpstream ? "Publishing branch..." : "Pushing...",
            needsUpstream ? $"Published {_status.Branch} and set its upstream." : "Pushed.",
            root => _gitService.PushAsync(root, setUpstream: needsUpstream));
    }

    private void BeginStashPush()
    {
        if (_status.Unstaged.Count == 0 && _status.Staged.Count == 0)
        {
            SetMessage("Nothing to stash — the working tree is clean.", MessageSeverity.Info);
            StateHasChanged();
            return;
        }

        ShowInput("Stash message (Enter for none)", string.Empty, StashPushFromInput);
    }

    private void StashPushFromInput(string message) =>
        RunAction("Stashing changes...", "Stashed.", root => _gitService.StashPushAsync(root, message));

    private void StashPop()
    {
        var stash = CursorItem(_stashesListBox);
        if (stash is not null)
        {
            RunAction($"Popping stash@{{{stash.Index}}}", $"Popped stash@{{{stash.Index}}}.", root => _gitService.StashPopAsync(root, stash.Index));
        }
    }

    private void StashApply()
    {
        var stash = CursorItem(_stashesListBox);
        if (stash is not null)
        {
            RunAction($"Applying stash@{{{stash.Index}}}", $"Applied stash@{{{stash.Index}}}.", root => _gitService.StashApplyAsync(root, stash.Index));
        }
    }

    private void BeginDropStash()
    {
        var stash = CursorItem(_stashesListBox);
        if (stash is null)
        {
            return;
        }

        BeginConfirm($"Drop stash@{{{stash.Index}}} for good? (y/n)",
            () => RunAction($"Dropping stash@{{{stash.Index}}}", $"Dropped stash@{{{stash.Index}}}.", root => _gitService.StashDropAsync(root, stash.Index)));
    }

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_helpVisible)
        {
            yield return ("Up/Down", UiText.Get("DialogHint.ScrollHelp", "Scroll the help"));
            yield return ("Esc", UiText.Get("DialogHint.BackToRepository", "Back to the repository"));
            yield break;
        }

        if (_confirmAction is not null)
        {
            yield return ("Y", iConsole.Localization.UiText.Get("Common.Confirm", "Confirm"));
            yield return ("N / Esc", iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"));
            yield break;
        }

        if (_inputActive)
        {
            yield return ("Enter", UiText.Get("DialogHint.UseTheText", "Use the text"));
            yield return ("Esc", iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"));
            yield break;
        }

        yield return ("Tab", UiText.Get("DialogHint.NextSection", "Next section (changes, branches, log, stashes, detail)"));
        yield return ("Enter/Space", UiText.Get("DialogHint.StageCheckoutShow", "Stage or unstage, check out, or show the commit"));
        yield return ("S/U", UiText.Get("DialogHint.StageUnstageFile", "Stage or unstage the file"));
        yield return ("A", UiText.Get("DialogHint.StageAll", "Stage all (apply, in the stashes)"));
        yield return ("D", UiText.Get("DialogHint.DiscardFileChangesAsks", "Discard the changes of the file (asks first)"));
        yield return ("C", UiText.Get("DialogHint.Commit", "Commit"));
        yield return ("Shift+C", UiText.Get("DialogHint.AmendCommit", "Amend the last commit"));
        yield return ("N", UiText.Get("DialogHint.NewBranch", "New branch"));
        yield return ("M", UiText.Get("DialogHint.MergeBranch", "Merge the branch"));
        yield return ("Shift+D", UiText.Get("DialogHint.DeleteBranch", "Delete the branch"));
        yield return ("F", UiText.Get("DialogHint.Fetch", "Fetch"));
        yield return ("P / Shift+P", UiText.Get("DialogHint.PullPush", "Pull / push"));
        yield return ("Shift+S", UiText.Get("DialogHint.StashChanges", "Stash the changes"));
        yield return ("X", UiText.Get("DialogHint.DropStash", "Drop the stash"));
        yield return ("R", UiText.Get("DialogHint.Refresh", "Refresh"));
        yield return ("PageUp/PageDown", UiText.Get("DialogHint.ScrollPatch", "Scroll the patch"));
        yield return ("F1, ?", iConsole.Localization.UiText.Get("Common.Help", "Help"));
        yield return ("Esc", iConsole.Localization.UiText.Get("Common.Close", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_helpVisible)
        {
            return HandleHelpKey(keyEvent);
        }

        if (_confirmAction is not null)
        {
            return HandleConfirmKey(keyEvent);
        }

        if (_inputActive)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    SubmitInput();
                    return true;
                case ConsoleKey.Escape:
                    CancelInput();
                    return true;
            }

            return await base.HandleKeyEventAsync(keyEvent);
        }

        // With the detail pane focused the arrows belong to the patch, not to a list.
        if (_diffListBox.IsFocused && HandleDiffKey(keyEvent))
        {
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Spacebar:
            case ConsoleKey.Enter:
                PrimaryAction();
                return true;
            case ConsoleKey.PageUp:
                ScrollDiff(-DiffPageRows());
                return true;
            case ConsoleKey.PageDown:
                ScrollDiff(DiffPageRows());
                return true;
            case ConsoleKey.F1:
                ShowHelp();
                return true;
            case ConsoleKey.Escape:
                Close();
                return true;
        }

        // Case-sensitive single keys (pull vs push, stage vs stash) — check before lower-casing.
        switch (keyEvent.KeyChar)
        {
            case '?':
                ShowHelp();
                return true;
            case 'p':
                Pull();
                return true;
            case 'P':
                Push();
                return true;
            case 'S':
                BeginStashPush();
                return true;
            case 'C':
                BeginAmend();
                return true;
            case 'D':
                if (_activeSection == Section.Branches)
                {
                    BeginDeleteBranch();
                }
                return true;
            case 'A':
                if (_mergeInProgress)
                {
                    AbortMerge();
                }
                return true;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'r':
                StartRefresh(reloadLog: true);
                return true;
            case 's':
            case 'u':
                ToggleStage();
                return true;
            case 'd':
                BeginDiscard();
                return true;
            case 'c':
                BeginCommit();
                return true;
            case 'f':
                Fetch();
                return true;
            case 'n':
                BeginNewBranch();
                return true;
            case 'm':
                if (_activeSection == Section.Branches)
                {
                    MergeSelected();
                    return true;
                }
                break;
            case 'a':
                if (_activeSection == Section.Stashes)
                {
                    StashApply();
                    return true;
                }

                ToggleStageAll();
                return true;
            case 'x':
                if (_activeSection == Section.Stashes)
                {
                    BeginDropStash();
                    return true;
                }
                break;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Tab)
        {
            var handled = await base.HandleKeyEventAsync(keyEvent);
            if (handled)
            {
                // Tab/Shift+Tab moved focus — follow it, and reload the detail pane if it landed on
                // a different section.
                SyncActiveSection();
            }

            return handled;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// Adopts whichever section now holds focus. Focus landing on the detail pane changes nothing:
    /// the pane is there to be read, and blanking it on the way in would defeat the point.
    /// </summary>
    private void SyncActiveSection()
    {
        for (var i = 0; i < SectionCount; i++)
        {
            if (!_sections[i].IsFocused || (int)_activeSection == i)
            {
                continue;
            }

            _activeSection = (Section)i;
            ApplySectionLayout();
            StartLoadDiff();
            StateHasChanged();
            return;
        }
    }

    /// <summary>Scrolling keys for the focused detail pane. Returns false for anything it does not own.</summary>
    private bool HandleDiffKey(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                ScrollDiff(-1);
                return true;
            case ConsoleKey.DownArrow:
                ScrollDiff(1);
                return true;
            case ConsoleKey.PageUp:
                ScrollDiff(-DiffPageRows());
                return true;
            case ConsoleKey.PageDown:
                ScrollDiff(DiffPageRows());
                return true;
            case ConsoleKey.Home:
                ScrollDiff(int.MinValue / 2);
                return true;
            case ConsoleKey.End:
                ScrollDiff(int.MaxValue / 2);
                return true;
            default:
                return false;
        }
    }

    private int DiffPageRows() => Math.Max(1, (int)_diffListBox.ViewSize.Height - 1);

    /// <summary>The panel-specific primary action bound to Space/Enter.</summary>
    private void PrimaryAction()
    {
        switch (_activeSection)
        {
            case Section.Unstaged:
            case Section.Staged:
                ToggleStage();
                break;
            case Section.Branches:
                CheckoutSelected();
                break;
            case Section.Stashes:
                StashPop();
                break;
        }
    }

    private void ScrollDiff(int delta)
    {
        var count = _diffListBox.ListModel.ListItems.Count;
        var rows = (int)_diffListBox.ViewSize.Height;
        var maxScroll = Math.Max(0, count - rows);
        var scrollableModel = _diffListBox.ScrollableModel;
        var target = delta switch
        {
            <= int.MinValue / 2 => 0,
            >= int.MaxValue / 2 => maxScroll,
            _ => (int)scrollableModel.ScrollPosition.Y + delta,
        };

        scrollableModel.ScrollPosition.Y = Math.Clamp(target, 0, maxScroll);
        // The pane draws no cursor row, so the cursor rides along with the viewport rather than
        // wandering off-screen and making the next key look like it did nothing.
        scrollableModel.CursorPosition.Y = scrollableModel.ScrollPosition.Y;
        StateHasChanged();
    }

    private void BeginCommit()
    {
        if (_status.Staged.Count == 0)
        {
            SetMessage(
                _status.Unstaged.Count > 0
                    ? "Nothing staged — stage a file with Space, or everything with a."
                    : "Nothing staged to commit.",
                MessageSeverity.Info);
            StateHasChanged();
            return;
        }

        ShowInput($"Commit {_status.Staged.Count} staged file(s)", string.Empty, message => CommitStagedFromInput(message, amend: false));
    }

    /// <summary>
    /// Amend pre-fills the previous message, because amending to fix a typo in it is most of what
    /// amend is for and retyping it from memory is how the rest of it gets lost.
    /// </summary>
    private void BeginAmend()
    {
        if (_root is null || _busy)
        {
            return;
        }

        var root = _root;
        PendingOperation = Task.Run(async () =>
        {
            var previous = await _gitService.GetLastCommitMessageAsync(root);
            if (previous.Length == 0)
            {
                SetMessage("Nothing to amend — there is no commit yet.", MessageSeverity.Info);
                StateHasChanged();
                return;
            }

            // Keep the subject: the input is one line, and pre-filling a body would silently drop it.
            ShowInput("Amend last commit", FirstLine(previous), message => CommitStagedFromInput(message, amend: true));
        });
    }

    private void BeginNewBranch() => ShowInput("New branch", string.Empty, CreateBranchFromInputText);

    private void ShowInput(string label, string initialText, Action<string> onSubmit)
    {
        _restoreFocusTo = _sections.FirstOrDefault(section => section.IsFocused) ?? _sections[(int)_activeSection];
        _onInputSubmit = onSubmit;
        _inputBox.Label = label;
        _inputBox.Text = initialText;
        _inputBox.ScrollableModel.CursorPosition.X = initialText.Length;
        _inputActive = true;
        ApplyInputVisibility();
        _inputBox.SetFocus();
        StateHasChanged();
    }

    private void ApplyInputVisibility()
    {
        _inputBox.CanFocus = _inputActive;
        _inputBox.IsVisible = _inputActive;
        _inputBox.IsEnabled = _inputActive;
    }

    private void SubmitInput()
    {
        var text = _inputBox.Text;
        var callback = _onInputSubmit;
        HideInput();
        callback?.Invoke(text);
    }

    private void CancelInput()
    {
        HideInput();
        SetMessage("Cancelled.", MessageSeverity.Info);
        StateHasChanged();
    }

    private void HideInput()
    {
        _inputBox.ResetFocus();
        _inputActive = false;
        ApplyInputVisibility();
        _inputBox.Text = string.Empty;
        _onInputSubmit = null;
        _restoreFocusTo?.SetFocus();
        _restoreFocusTo = null;
        StateHasChanged();
    }

    /// <summary>
    /// y confirms, n/Esc cancels, and anything else is ignored. A stray key must not stand for an
    /// answer either way when the question is "delete this branch".
    /// </summary>
    private bool HandleConfirmKey(KeyEvent keyEvent)
    {
        var answer = char.ToLowerInvariant(keyEvent.KeyChar);
        if (answer != 'y' && answer != 'n' && keyEvent.ConsoleKey != ConsoleKey.Escape)
        {
            return true;
        }

        var action = _confirmAction;
        _confirmAction = null;
        _confirmPrompt = string.Empty;

        if (answer == 'y')
        {
            action?.Invoke();
        }
        else
        {
            SetMessage("Cancelled.", MessageSeverity.Info);
            StateHasChanged();
        }

        return true;
    }

    private void BeginConfirm(string prompt, Action onConfirm)
    {
        _confirmPrompt = prompt;
        _confirmAction = onConfirm;
        StateHasChanged();
    }

    private void BeginDiscard()
    {
        if (_activeSection != Section.Unstaged)
        {
            return;
        }

        var file = CursorItem(_unstagedListBox);
        if (file is null)
        {
            return;
        }

        var prompt = file.IsUntracked
            ? $"Delete untracked file {file.Path}? (y/n)"
            : $"Throw away your changes to {file.Path}? (y/n)";
        BeginConfirm(prompt,
            () => RunAction($"Discarding {file.Path}", $"Discarded {file.Path}.", root => _gitService.DiscardAsync(root, file.Path, file.IsUntracked)));
    }

    /// <summary>The item under the cursor in <paramref name="box"/>, or null if the cursor is out of range.</summary>
    private static T? CursorItem<T>(ListBox<T> box) where T : class
    {
        var items = box.ListModel.ListItems;
        var index = (int)box.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    private GitFileStatus? CurrentFile(out bool staged)
    {
        if (_activeSection == Section.Unstaged)
        {
            staged = false;
            return CursorItem(_unstagedListBox);
        }

        if (_activeSection == Section.Staged)
        {
            staged = true;
            return CursorItem(_stagedListBox);
        }

        staged = false;
        return null;
    }

    #endregion

    #region Help overlay

    /// <summary>
    /// The key reference. The dialog binds three dozen keys that no menu bar advertises, and the
    /// footer can only ever carry the handful that apply where the cursor is standing.
    /// </summary>
    private static readonly string[] HelpLines =
    [
        "MOVING AROUND",
        "  Tab / Shift+Tab      Next / previous panel (the diff pane is one of them)",
        "  Up / Down            Move within the focused panel",
        "  PgUp / PgDn          Scroll the diff pane, from any panel",
        "  Home / End           Top / bottom of the diff, with the diff pane focused",
        "  ? or F1              This help          Esc  Close it, then close the dialog",
        "",
        "ANY PANEL",
        "  r                    Reload everything from disk",
        "  f                    Fetch all remotes and prune",
        "  p / P                Pull / Push  (a branch with no upstream is published and tracked)",
        "  c                    Commit what is staged",
        "  C                    Amend the last commit, its message pre-filled",
        "  S                    Stash everything, untracked files included",
        "  n                    New branch from HEAD",
        "  A                    Abort the merge in progress (shown only while one is)",
        "",
        "UNSTAGED — the working tree",
        "  Space / Enter / s    Stage the file under the cursor",
        "  a                    Stage everything, untracked files included",
        "  d                    Discard the file's changes; deletes it if untracked. Asks first.",
        "  Conflicted files are marked ! and shown here only. Staging one marks it resolved.",
        "",
        "STAGED — what the next commit will contain",
        "  Space / Enter / u    Unstage the file under the cursor",
        "  a                    Unstage everything",
        "",
        "BRANCHES",
        "  Space / Enter        Check out. On a remote branch, create a local one tracking it.",
        "  m                    Merge into the current branch",
        "  D                    Delete, merged or not. Asks first.",
        "  Grey rows are remote-tracking branches.",
        "",
        "COMMITS",
        "  Up / Down            Show that commit's full patch in the diff pane",
        "",
        "STASHES",
        "  Space / Enter        Pop (apply and drop)",
        "  a                    Apply, keeping the stash",
        "  x                    Drop it. Asks first.",
    ];

    private void ShowHelp()
    {
        _helpVisible = true;
        _helpScroll = 0;
        SetContentVisible(false);
        StateHasChanged();
    }

    private void HideHelp()
    {
        _helpVisible = false;
        SetContentVisible(true);
        StateHasChanged();
    }

    /// <summary>
    /// The overlay is drawn by the dialog, but the panels are separate controls rendered in no
    /// guaranteed order, so they are hidden rather than drawn over.
    /// </summary>
    private void SetContentVisible(bool visible)
    {
        foreach (var section in _sections)
        {
            section.IsVisible = visible;
        }

        _diffListBox.IsVisible = visible;
    }

    private bool HandleHelpKey(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                ScrollHelp(-1);
                return true;
            case ConsoleKey.DownArrow:
                ScrollHelp(1);
                return true;
            case ConsoleKey.PageUp:
                ScrollHelp(-10);
                return true;
            case ConsoleKey.PageDown:
                ScrollHelp(10);
                return true;
            case ConsoleKey.Home:
                _helpScroll = 0;
                StateHasChanged();
                return true;
        }

        HideHelp();
        return true;
    }

    private void ScrollHelp(int delta)
    {
        var rows = Math.Max(1, (int)ViewSize.Height - 4);
        _helpScroll = Math.Clamp(_helpScroll + delta, 0, Math.Max(0, HelpLines.Length - rows));
        StateHasChanged();
    }

    private void DrawHelp(IBuffer buffer, int x, int y, int w, int h, ColorScheme colorScheme)
    {
        var rows = Math.Max(0, h - 1);
        for (var row = 0; row < rows; row++)
        {
            var index = _helpScroll + row;
            var line = index < HelpLines.Length ? HelpLines[index] : string.Empty;
            // Captions are the only unindented lines; colouring them is what turns the wall of keys
            // into something skimmable.
            var isCaption = line.Length > 0 && line[0] != ' ';
            WriteLine(buffer, x, y + row, w, " " + line, isCaption ? Color.Cyan : colorScheme.Foreground, colorScheme.Background);
        }

        var more = _helpScroll + rows < HelpLines.Length ? "  more below (Down/PgDn)" : string.Empty;
        WriteLine(buffer, x, y + rows, w, $" Any other key closes this help.{more}", Color.DarkGray, colorScheme.Background);
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

        if (_helpVisible)
        {
            DrawSectionHeader(buffer, ix, iy, iw, "Git — keys", true, colorScheme);
            DrawHelp(buffer, ix, iy + 1, iw, ih - 1, colorScheme);
            return;
        }

        DrawHeader(buffer, ix, iy, iw, colorScheme);
        if (!_inputActive)
        {
            // While the commit/new-branch TextBox occupies the footer row, don't overwrite its rendering.
            DrawFooter(buffer, ix, iy + ih - 1, iw, colorScheme);
        }

        var midY = iy + 1;
        var midH = ih - 2;
        if (midH <= 0)
        {
            return;
        }

        if (_root is null)
        {
            WriteLine(buffer, ix, midY, iw, _loading ? "Loading..." : _message, colorScheme.Foreground, colorScheme.Background);
            return;
        }

        var leftW = LeftColumnWidth(iw);
        var rightX = ix + leftW + 1;
        var rightW = iw - leftW - 1;
        var baseY = (int)AbsolutePosition.Y;

        DrawSection(buffer, ix, baseY, leftW, Section.Unstaged, UnstagedTitle(), colorScheme);
        DrawSection(buffer, ix, baseY, leftW, Section.Staged, $"Staged ({_status.Staged.Count})", colorScheme);
        DrawSection(buffer, ix, baseY, leftW, Section.Branches, $"Branches ({_branches.Count(branch => !branch.IsRemote)})", colorScheme);
        DrawSection(buffer, ix, baseY, leftW, Section.Commits, $"Commits ({_commits.Count})", colorScheme);
        DrawSection(buffer, ix, baseY, leftW, Section.Stashes, $"Stashes ({_stashes.Count})", colorScheme);

        if (rightW > 0)
        {
            DrawSectionHeader(buffer, rightX, midY, rightW, DetailHeaderText(rightW), _diffListBox.IsFocused, colorScheme);
        }
    }

    private string UnstagedTitle()
    {
        var conflicts = _status.Unstaged.Count(file => file.IsConflicted);
        return conflicts > 0
            ? $"Unstaged ({_status.Unstaged.Count}, {conflicts} conflicted)"
            : $"Unstaged ({_status.Unstaged.Count})";
    }

    /// <summary>Draws a section's header band, and its empty-state line when it has rows but nothing to put in them.</summary>
    private void DrawSection(IBuffer buffer, int x, int baseY, int width, Section section, string title, ColorScheme colorScheme)
    {
        var index = (int)section;
        var headerY = baseY + _sectionTops[index];
        DrawSectionHeader(buffer, x, headerY, width, title, _sections[index].IsFocused, colorScheme);
    }

    /// <summary>Names what the detail pane holds and how far down it the reader is — the pane draws no cursor to say so.</summary>
    private string DetailHeaderText(int width)
    {
        var total = _diffListBox.ListModel.ListItems.Count;
        var rows = Math.Max(1, (int)_diffListBox.ViewSize.Height);
        var position = total > rows
            ? $"  {Math.Min(total, (int)_diffListBox.ScrollableModel.ScrollPosition.Y + rows)}/{total}"
            : string.Empty;
        var title = _detailShowsError ? $"Failed: {_detailTitle}" : _detailTitle;
        return (title.Shrink(Math.Max(1, width - position.Length - 2)) ?? string.Empty) + position;
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_root is null)
        {
            WriteLine(buffer, x, y, w, _loading ? "Loading..." : "No repository", Color.Cyan, colorScheme.Background);
            return;
        }

        var parts = new List<string> { $"[{_status.Branch}]" };
        if (_status.Upstream is null)
        {
            parts.Add("(no upstream)");
        }
        else
        {
            parts.Add($"-> {_status.Upstream}");
            if (_status.Ahead > 0) parts.Add($"ahead {_status.Ahead}");
            if (_status.Behind > 0) parts.Add($"behind {_status.Behind}");
        }

        if (_mergeInProgress)
        {
            parts.Add("[MERGING]");
        }

        if (_loading || _busy)
        {
            // A real spinner, not a dot: fetch and push can take seconds, and a still marker looks
            // the same as a hung one.
            parts.Add(Spinner().ToString());
        }

        var prefix = string.Join("  ", parts);
        // The path is shrunk rather than the status: which branch you are on is what the line is for.
        var room = w - prefix.Length - 3;
        var text = room > 4 ? $"{prefix}   {_root.Shrink(room)}" : prefix;
        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    private char Spinner()
    {
        const string frames = @"|/-\";
        var now = _dateTimeProviderService.Now;
        return frames[(int)(now.Ticks / TimeSpan.TicksPerMillisecond / 150) % frames.Length];
    }

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        string text;
        Color fg;
        if (_confirmAction is not null)
        {
            text = _confirmPrompt;
            fg = Color.Red;
        }
        else if (string.IsNullOrEmpty(_message))
        {
            text = NormalFooterHint();
            fg = colorScheme.Foreground;
        }
        else
        {
            text = _message;
            fg = _messageSeverity switch
            {
                MessageSeverity.Error => colorScheme.Error,
                MessageSeverity.Success => Color.Green,
                _ => colorScheme.Foreground,
            };
        }

        WriteLine(buffer, x, y, w, text.Shrink(w) ?? string.Empty, fg, colorScheme.Background);
    }

    /// <summary>
    /// Key hints for the focused panel plus the few globals worth a permanent line. Everything else
    /// lives behind ?, which the hint always names — a footer long enough to list every binding is a
    /// footer that gets dropped on a narrow terminal.
    /// </summary>
    private string NormalFooterHint()
    {
        if (_diffListBox.IsFocused)
        {
            return "Up/Dn:scroll  Home/End:top/bottom   Tab:panel  r:refresh  ?:keys  Esc:close";
        }

        var panelKeys = _activeSection switch
        {
            Section.Unstaged => "Space:stage  a:stage-all  d:discard  c:commit",
            Section.Staged => "Space:unstage  a:unstage-all  c:commit",
            Section.Branches => "Space:checkout  n:new  m:merge  D:delete",
            Section.Commits => "Up/Dn:show patch",
            _ => "Space:pop  a:apply  x:drop",
        };

        var mergeHint = _mergeInProgress ? "  A:abort-merge" : string.Empty;
        return $"{panelKeys}   Tab:panel  p/P:pull/push  ?:keys  Esc:close{mergeHint}";
    }

    #endregion
}
