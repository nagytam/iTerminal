using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.Git;
using iTerminal.Core.Services.Git;

namespace iTerminal.Core.Dialogs.Git;

/// <summary>
/// Ctrl+Shift+G: the git synchronisation of the repository the focused panel is in, full screen. It shows the current
/// branch and its upstream (ahead/behind), the conflicted files, the local and remote branches, and the worktrees. It lets
/// the person pick how the pull reconciles (rebase, merge, or fast-forward only) and whether a push follows. Enter runs the
/// sync: pull, then push when that is ticked. A failed step stops there, and the conflicts it left are listed on reload.
///
/// <para>F6 cleans up what is merged into the default branch: first press lists the worktrees and branches it would remove,
/// the second removes them. Worktrees with changes in them, and locked ones, are left alone — git refuses to remove a dirty
/// worktree without force, and that is what makes the cleaning safe.</para>
///
/// <para>Every git call runs on a background task; the key handler and <see cref="Draw"/> never wait on git.</para>
/// </summary>
public class GitSyncDialog : Dialog
{
    /// <summary>What a clean-up would remove: the linked worktrees first, then the branches (merged into the default branch).</summary>
    public sealed record CleanPlan(IReadOnlyList<GitWorktree> Worktrees, IReadOnlyList<string> Branches);

    private readonly IGitService _gitService;
    private readonly IContentModelService _contentModelService;
    private readonly FormPanel _form;
    private readonly ComboBox<GitPullMode> _comboPull;
    private readonly CheckBox _checkBoxPush;
    private readonly SyncList _list;
    private string? _folder;
    private string? _root;
    private volatile string _status = string.Empty;
    private volatile bool _statusIsError;
    private GitRepositoryStatus _repository = GitRepositoryStatus.Empty;
    private IReadOnlyList<GitBranch> _branches = [];
    private IReadOnlyList<string> _conflicts = [];
    private IReadOnlyList<GitWorktree> _worktrees = [];
    private IReadOnlyList<string> _merged = [];
    private string? _defaultBranch;
    private CleanPlan? _pendingClean;

    public GitSyncDialog(
        IGitService gitService,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base(nameof(GitSyncDialog), themeService)
    {
        _gitService = gitService;
        _contentModelService = contentModelService;
        Title = Say("Sync.Title", "Git sync");

        _comboPull = new ComboBox<GitPullMode>(themeService) { DisplaySelector = DescribePull };
        _comboPull.SetItems([GitPullMode.Rebase, GitPullMode.Merge, GitPullMode.FastForwardOnly]);
        _comboPull.SelectedIndex = 0;
        _form = new FormPanel(themeService);
        _form.AddField(Say("Sync.FieldPull", "Pull"), _comboPull);
        _checkBoxPush = new CheckBox(themeService) { Label = Say("Sync.FieldPush", "Push after the pull"), IsChecked = true };
        _list = new SyncList(themeService);

        AddControl(_form);
        AddControl(_checkBoxPush);
        AddControl(_list);
        AddCommand(new SyncCommand());
        AddCommand(new CleanMergedCommand());
    }

    private static string DescribePull(GitPullMode mode) => mode switch
    {
        GitPullMode.Rebase => Say("Sync.Mode.Rebase", "Rebase the local commits onto the upstream"),
        GitPullMode.Merge => Say("Sync.Mode.Merge", "Merge the upstream in (a merge commit when needed)"),
        GitPullMode.FastForwardOnly => Say("Sync.Mode.FastForward", "Fast-forward only (fails when the branches diverged)"),
        _ => mode.ToString(),
    };

    /// <summary>How the pull reconciles the branches.</summary>
    public GitPullMode PullMode
    {
        get => _comboPull.SelectedItem;
        set => _comboPull.SelectedIndex = _comboPull.Items.ToList().IndexOf(value);
    }

    /// <summary>Whether a successful pull is followed by a push.</summary>
    public bool PushAfterPull
    {
        get => _checkBoxPush.IsChecked;
        set => _checkBoxPush.IsChecked = value;
    }

    /// <summary>The repository's current branch and its upstream, as the last reload saw them.</summary>
    public GitRepositoryStatus Repository => _repository;

    /// <summary>The conflicted paths the last reload saw; empty when the working tree is clean of them.</summary>
    public IReadOnlyList<string> Conflicts => _conflicts;

    /// <summary>Every worktree the last reload saw, the main one first.</summary>
    public IReadOnlyList<GitWorktree> Worktrees => _worktrees;

    /// <summary>The local branches the last reload found merged into the default branch.</summary>
    public IReadOnlyList<string> MergedBranches => _merged;

    /// <summary>The clean-up the first F6 proposed and the second F6 will carry out, if one is waiting.</summary>
    public CleanPlan? PendingClean => _pendingClean;

    public string Status => _status;

    /// <summary>The reload started last — nothing waits on it; it exists so a test can.</summary>
    public Task RefreshTask { get; private set; } = Task.CompletedTask;

    /// <summary>The sync started last — nothing waits on it; it exists so a test can.</summary>
    public Task SyncTask { get; private set; } = Task.CompletedTask;

    /// <summary>The clean-up started last — nothing waits on it; it exists so a test can.</summary>
    public Task CleanTask { get; private set; } = Task.CompletedTask;

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        _folder = GitPullRebaseCommand.ResolveFolder(_contentModelService);
        _status = Say("Sync.Status.Enter", "Enter syncs: pull, then push when it is ticked.");
        _statusIsError = false;
        RefreshTask = RefreshAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            // Full screen, like the Git client: the lists are the point of this dialog, so they get the room.
            CenterInParent(Parent.ViewSize * 0.95);
        }

        var inner = Math.Max(1, ViewSize.Width - 4);
        _form.Position = new Position(2, 4);
        _form.ViewSize = new Size(inner, 1);
        _checkBoxPush.Position = new Position(2, 5);
        _checkBoxPush.ViewSize = new Size(inner, 1);
        _list.Position = new Position(1, 8);
        _list.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 11));
        base.OnInitializeLayout();
    }

    /// <summary>Reloads the status, the branches, the worktrees and the conflicts. A running reload or sync is left to finish.</summary>
    public Task RefreshAsync()
    {
        if (!RefreshTask.IsCompleted)
        {
            return RefreshTask;
        }

        RefreshTask = Task.Run(async () =>
        {
            try
            {
                var root = await ResolveRootAsync();
                if (root is null)
                {
                    SetStatus(Say("Sync.Status.NotRepo", "This folder is not in a git repository."), isError: true);
                    return;
                }

                await LoadAsync(root);
                SetStatus(_repository.Upstream is null
                    ? Say("Sync.Status.NoUpstream", "The branch has no upstream branch to sync with.")
                    : Say("Sync.Status.Enter", "Enter syncs: pull, then push when it is ticked."), isError: _repository.Upstream is null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetStatus(exception.Message, isError: true);
            }
        });
        return RefreshTask;
    }

    /// <summary>Enter: pull with the chosen reconciliation, then push when ticked. A failed step stops the sync there.</summary>
    public Task SyncAsync()
    {
        if (!SyncTask.IsCompleted)
        {
            return SyncTask;
        }

        var mode = PullMode;
        var push = PushAfterPull;
        SetStatus(Say("Sync.Status.Syncing", "Syncing with the remote…"), isError: false);
        SyncTask = Task.Run(async () =>
        {
            try
            {
                // A reload still running is finished first, so the sync acts on what it found.
                await RefreshTask;

                var root = await ResolveRootAsync();
                if (root is null)
                {
                    SetStatus(Say("Sync.Status.NotRepo", "This folder is not in a git repository."), isError: true);
                    return;
                }

                // The status is read again here: the sync must act on the repository as it is now, not as the last reload left it.
                var status = await _gitService.GetStatusAsync(root);
                if (status.Upstream is null)
                {
                    SetStatus(Say("Sync.Status.NoUpstream", "The branch has no upstream branch to sync with."), isError: true);
                    return;
                }

                var pull = await _gitService.PullAsync(root, mode);
                if (!pull.Success)
                {
                    await LoadAsync(root);
                    SetStatus(Say("Sync.Status.PullFailed", "Pull failed: {0} (Nothing was pushed. If git started a rebase or merge, it is left unfinished.)", FirstLine(pull.Output)), isError: true);
                    return;
                }

                if (push)
                {
                    var pushed = await _gitService.PushAsync(root);
                    if (!pushed.Success)
                    {
                        await LoadAsync(root);
                        SetStatus(Say("Sync.Status.PushFailed", "Push failed: {0}", FirstLine(pushed.Output)), isError: true);
                        return;
                    }
                }

                await LoadAsync(root);
                SetStatus(Say("Sync.Status.Synced", "Synced with the remote."), isError: false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetStatus(exception.Message, isError: true);
            }
        });
        return SyncTask;
    }

    /// <summary>
    /// F6: the first press proposes the clean-up and says what it would remove; the second press carries it out. The
    /// proposal is worked out from the last reload, so nothing is removed that was not on screen.
    /// </summary>
    public Task CleanAsync()
    {
        if (!CleanTask.IsCompleted)
        {
            return CleanTask;
        }

        if (_pendingClean is { } plan)
        {
            _pendingClean = null;
            SetStatus(Say("Sync.Clean.Running", "Cleaning…"), isError: false);
            CleanTask = Task.Run(() => ExecuteCleanAsync(plan));
            return CleanTask;
        }

        if (_defaultBranch is null)
        {
            SetStatus(Say("Sync.Clean.NoBase", "No main or master branch to judge merges against."), isError: true);
            return Task.CompletedTask;
        }

        var proposal = PlanClean();
        if (proposal.Worktrees.Count == 0 && proposal.Branches.Count == 0)
        {
            SetStatus(Say("Sync.Clean.Nothing", "Nothing merged into {0} to clean.", _defaultBranch), isError: false);
            return Task.CompletedTask;
        }

        _pendingClean = proposal;
        var names = string.Join(", ", proposal.Worktrees.Select(w => w.Path).Concat(proposal.Branches));
        SetStatus(Say("Sync.Clean.Plan", "Will remove {0} worktree(s) and delete {1} branch(es): {2}. F6 again to do it, any other key cancels.", proposal.Worktrees.Count, proposal.Branches.Count, names), isError: false);
        return Task.CompletedTask;
    }

    /// <summary>What is merged into the default branch and not in use: linked worktrees that are clean-able, then the branches they hold and the rest.</summary>
    private CleanPlan PlanClean()
    {
        var current = _repository.Branch;
        var merged = _merged.Where(name => name != _defaultBranch && name != current).ToList();
        var mergedSet = merged.ToHashSet(StringComparer.Ordinal);

        var worktrees = _worktrees
            .Where(w => !w.IsMain && !w.IsLocked && w.Branch is { } branch && mergedSet.Contains(branch))
            .ToList();
        var removed = worktrees.Select(w => w.Path).ToHashSet(StringComparer.Ordinal);

        // A branch checked out in a worktree we keep is not deleted: git would refuse, and the worktree still needs it.
        var kept = _worktrees
            .Where(w => !removed.Contains(w.Path))
            .Select(w => w.Branch)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var branches = merged.Where(name => !kept.Contains(name)).ToList();
        return new CleanPlan(worktrees, branches);
    }

    private async Task ExecuteCleanAsync(CleanPlan plan)
    {
        try
        {
            var root = await ResolveRootAsync();
            if (root is null)
            {
                SetStatus(Say("Sync.Status.NotRepo", "This folder is not in a git repository."), isError: true);
                return;
            }

            var failures = new List<string>();
            var blocked = new HashSet<string>(StringComparer.Ordinal);
            var removedWorktrees = 0;
            var deletedBranches = 0;

            foreach (var worktree in plan.Worktrees)
            {
                var removal = await _gitService.RemoveWorktreeAsync(root, worktree.Path);
                if (removal.Success)
                {
                    removedWorktrees++;
                }
                else
                {
                    failures.Add(worktree.Path);
                    if (worktree.Branch is { } branch)
                    {
                        blocked.Add(branch);
                    }
                }
            }

            foreach (var branch in plan.Branches)
            {
                if (blocked.Contains(branch))
                {
                    continue;
                }

                var deletion = await _gitService.DeleteBranchAsync(root, branch, force: false);
                if (deletion.Success)
                {
                    deletedBranches++;
                }
                else
                {
                    failures.Add(branch);
                }
            }

            await _gitService.PruneWorktreesAsync(root);
            await LoadAsync(root);

            var done = Say("Sync.Clean.Done", "Cleaned: removed {0} worktree(s), deleted {1} branch(es).", removedWorktrees, deletedBranches);
            SetStatus(failures.Count == 0
                ? done
                : done + " " + Say("Sync.Clean.Failed", "Not cleaned: {0}", string.Join(", ", failures)), isError: failures.Count > 0);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetStatus(exception.Message, isError: true);
        }
    }

    /// <summary>The repository root for the folder the panel showed when the dialog opened, or null outside a working tree.</summary>
    private async Task<string?> ResolveRootAsync()
    {
        if (_folder is null)
        {
            return null;
        }

        _root ??= await _gitService.GetRepositoryRootAsync(_folder);
        return _root;
    }

    private async Task LoadAsync(string root)
    {
        _repository = await _gitService.GetStatusAsync(root);
        _branches = await _gitService.GetBranchesAsync(root, includeRemotes: true);
        _worktrees = await _gitService.GetWorktreesAsync(root);
        _defaultBranch = await _gitService.GetDefaultBranchAsync(root);
        _merged = _defaultBranch is null ? [] : await _gitService.GetMergedBranchesAsync(root, _defaultBranch);
        _conflicts = _repository.Unstaged.Where(file => file.IsConflicted).Select(file => file.Path).ToList();
        _pendingClean = null;
        _list.SetRows(BuildRows());
        StateHasChanged();
    }

    private IReadOnlyList<(string Text, Color Color)> BuildRows()
    {
        var rows = new List<(string Text, Color Color)>();
        var colorScheme = ThemeService.CurrentColorScheme;
        rows.Add((Say("Sync.HeaderConflicts", "Conflicts (resolve, stage, then sync again)"), colorScheme.Foreground));
        if (_conflicts.Count == 0)
        {
            rows.Add((" " + Say("Sync.NoConflicts", "No conflicts."), colorScheme.Foreground));
        }
        else
        {
            rows.AddRange(_conflicts.Select(path => ($" ! {path}", Color.Red)));
        }

        rows.Add((string.Empty, colorScheme.Foreground));
        rows.Add((Say("Sync.HeaderLocal", "Local branches"), colorScheme.Foreground));
        var merged = _merged.ToHashSet(StringComparer.Ordinal);
        foreach (var branch in _branches.Where(branch => !branch.IsRemote))
        {
            var tag = merged.Contains(branch.Name) && branch.Name != _defaultBranch && !branch.IsCurrent
                ? "  " + Say("Sync.Merged", "merged")
                : string.Empty;
            var upstream = branch.Upstream is null ? string.Empty : "  → " + branch.Upstream;
            rows.Add(($" {(branch.IsCurrent ? "*" : " ")} {branch.Name}{upstream}{tag}", branch.IsCurrent ? Color.Green : colorScheme.Foreground));
        }

        rows.Add((string.Empty, colorScheme.Foreground));
        rows.Add((Say("Sync.HeaderRemote", "Remote branches"), colorScheme.Foreground));
        rows.AddRange(_branches.Where(branch => branch.IsRemote).Select(branch => ($"   {branch.Name}", Color.DarkGray)));

        rows.Add((string.Empty, colorScheme.Foreground));
        rows.Add((Say("Sync.HeaderWorktrees", "Worktrees"), colorScheme.Foreground));
        foreach (var worktree in _worktrees)
        {
            var kind = worktree.IsMain ? Say("Sync.WorktreeMain", "main") : Say("Sync.WorktreeLinked", "linked");
            var locked = worktree.IsLocked ? "  " + Say("Sync.Locked", "locked") : string.Empty;
            rows.Add(($"   {kind,-6} {worktree.Path}  [{worktree.Branch ?? Say("Sync.Detached", "detached")}]{locked}", worktree.IsMain ? colorScheme.Foreground : Color.Cyan));
        }

        return rows;
    }

    private void SetStatus(string status, bool isError)
    {
        _status = status;
        _statusIsError = isError;
        StateHasChanged();
    }

    /// <summary>A toast-sized first line of git's output.</summary>
    private static string FirstLine(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        return line.Length == 0 ? "git returned an error." : line;
    }

    /// <summary>A text in the chosen language, with its arguments filled in.</summary>
    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("Sync.Hint.Sync", "Pull, then push: runs the sync"));
        yield return ("F5", UiText.Get("Sync.Hint.Reload", "Reload the branches, worktrees and conflicts"));
        yield return ("F6", UiText.Get("Sync.Hint.Clean", "Clean the branches and worktrees merged into main (press twice)"));
        yield return ("Up/Down", UiText.Get("Sync.Hint.Scroll", "Scroll the branches, worktrees and conflicts"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        // The second F6 is the confirmation; any other key, but Esc, withdraws the proposal, as in the folder comparison.
        if (_pendingClean is not null && keyEvent.ConsoleKey is not (ConsoleKey.Escape or ConsoleKey.F6))
        {
            _pendingClean = null;
            SetStatus(Say("Sync.Clean.Cancelled", "Cleaning cancelled — nothing was removed."), isError: false);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F5 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            _ = RefreshAsync();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift && !_comboPull.IsOpen)
        {
            _ = SyncAsync();
            return true;
        }

        if (!keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            var step = keyEvent.ConsoleKey switch
            {
                ConsoleKey.UpArrow when !_comboPull.IsFocused => -1,
                ConsoleKey.DownArrow when !_comboPull.IsFocused => 1,
                ConsoleKey.PageUp => -_list.PageSize,
                ConsoleKey.PageDown => _list.PageSize,
                _ => 0,
            };
            if (step != 0)
            {
                _list.Scroll(step);
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var inner = Math.Max(0, width - 4);

        void Line(int row, string text, Color color) =>
            buffer.Write(ZOrder, new Position(AbsolutePosition.X + 2, AbsolutePosition.Y + row), new Size(inner, 1),
                new ConxelString((text.Shrink(inner) ?? string.Empty).ExpandRight(inner), color, colorScheme.Background));

        var branch = _repository.Branch.Length == 0 ? "—" : _repository.Branch;
        var upstream = _repository.Upstream is null
            ? Say("Sync.NoUpstreamShort", "no upstream")
            : $"{_repository.Upstream}  ↑{_repository.Ahead} ↓{_repository.Behind}";
        Line(1, Say("Sync.RepositoryLine", "Repository: {0}", _folder ?? "—"), foreground);
        Line(2, Say("Sync.BranchLine", "Branch: {0}  ({1})", branch, upstream), foreground);

        var rule = new ConxelString(Border.Horizontal.Repeat(Math.Max(0, width - 2)), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 7), new Size(rule.Count, 1), rule);

        Line((int)ViewSize.Height - 3, _status, _statusIsError ? colorScheme.Error : foreground);
        Line((int)ViewSize.Height - 2, string.Empty, foreground);

        var hints = (" " + Say("Sync.Hints", "Enter:sync  F5:reload  F6:clean  ↑↓:scroll  Esc:close") + " ").FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, hints), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }

    /// <summary>Enter: sync, wherever focus is. Claimed by the dialog so the pull-mode list cannot answer Enter by opening itself.</summary>
    private sealed class SyncCommand : Command
    {
        public override string Title => "Sync";
        public override string Description => "Pull, then push: the git sync.";
        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            (Control as GitSyncDialog ?? Control?.Parent as GitSyncDialog)?.SyncAsync();
            return Task.CompletedTask;
        }
    }

    /// <summary>F6: clean the branches and worktrees merged into the default branch. Two presses: propose, then do it.</summary>
    private sealed class CleanMergedCommand : Command
    {
        public override string Title => "Clean merged";
        public override string Description => "Remove the worktrees and branches merged into the default branch (press twice to confirm).";
        public override Key? Key { get; init; } = new Key(ConsoleKey.F6, false, false, false);

        public override Task ExecuteAsync(object? args)
        {
            (Control as GitSyncDialog ?? Control?.Parent as GitSyncDialog)?.CleanAsync();
            return Task.CompletedTask;
        }
    }

    /// <summary>Read-only rows of the branches, worktrees and conflicts, each with its own colour, scrolled with the arrows and page keys.</summary>
    private sealed class SyncList(IThemeService themeService) : Control(themeService)
    {
        private readonly Lock _lock = new();
        private IReadOnlyList<(string Text, Color Color)> _rows = [];
        private int _top;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        public void SetRows(IReadOnlyList<(string Text, Color Color)> rows)
        {
            lock (_lock)
            {
                _rows = rows;
                _top = 0;
            }

            StateHasChanged();
        }

        public void Scroll(int step)
        {
            lock (_lock)
            {
                _top = Math.Clamp(_top + step, 0, Math.Max(0, _rows.Count - (int)ViewSize.Height));
            }
        }

        public override void Draw(IBuffer buffer)
        {
            IReadOnlyList<(string Text, Color Color)> rows;
            int top;
            lock (_lock)
            {
                rows = _rows;
                top = _top;
            }

            var colorScheme = ThemeService.CurrentColorScheme;
            var width = (int)ViewSize.Width;
            for (var row = 0; row < (int)ViewSize.Height; row++)
            {
                var index = top + row;
                var text = string.Empty;
                var color = colorScheme.Foreground;
                if (index < rows.Count)
                {
                    (text, color) = rows[index];
                }

                // Every cell of the rectangle, every time: a shorter list must not leave old rows behind.
                var line = new ConxelString(text.ExpandRight(width)[..width], color, colorScheme.Background);
                buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + row), new Size(width, 1), line);
            }
        }
    }
}
