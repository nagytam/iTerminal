using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
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
/// Ctrl+Shift+G: the git synchronisation of the repository the focused panel is in. It shows the current branch and
/// its upstream, the local and remote branches, and any conflicted files, and lets the person pick how the pull
/// reconciles (rebase, merge, or fast-forward only) and whether a push follows. Enter runs the sync: pull, then push
/// when that is ticked. A failed pull or push stops there, and the conflicts it left are listed on the next reload.
///
/// <para>Every git call runs on a background task; the key handler and <see cref="Draw"/> never wait on git.</para>
/// </summary>
public class GitSyncDialog : Dialog
{
    private readonly IGitService _gitService;
    private readonly IContentModelService _contentModelService;
    private readonly INotificationService? _notifications;
    private readonly FormPanel _form;
    private readonly ComboBox<GitPullMode> _comboPull;
    private readonly CheckBox _checkBoxPush;
    private readonly Button _cleanupButton;
    private readonly SyncList _list;
    private string? _folder;
    private string? _root;
    private volatile string _status = string.Empty;
    private volatile bool _statusIsError;
    private volatile string _statusDetail = string.Empty;
    private GitRepositoryStatus _repository = GitRepositoryStatus.Empty;
    private IReadOnlyList<GitBranch> _branches = [];
    private IReadOnlyList<string> _conflicts = [];
    private string? _mainBranch;
    private IReadOnlySet<string> _mergedRefs = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The local branches the cleanup asked about and waits on a y/n for; empty when no cleanup is waiting.</summary>
    private IReadOnlyList<string> _pendingCleanup = [];

    public GitSyncDialog(
        IGitService gitService,
        IContentModelService contentModelService,
        IThemeService themeService,
        INotificationService? notifications = null)
        : base(nameof(GitSyncDialog), themeService)
    {
        _gitService = gitService;
        _contentModelService = contentModelService;
        _notifications = notifications;
        Title = Say("Sync.Title", "Git sync");

        _comboPull = new ComboBox<GitPullMode>(themeService) { DisplaySelector = DescribePull };
        _comboPull.SetItems([GitPullMode.Rebase, GitPullMode.Merge, GitPullMode.FastForwardOnly]);
        _comboPull.SelectedIndex = 0;
        _form = new FormPanel(themeService);
        _form.AddField(Say("Sync.FieldPull", "Pull"), _comboPull);
        _checkBoxPush = new CheckBox(themeService) { Label = Say("Sync.FieldPush", "Push after the pull"), IsChecked = true };
        _cleanupButton = new Button(Say("Sync.Cleanup.Button", "Clean up merged (Alt+C)"), new CleanupMergedCommand(), themeService);
        _list = new SyncList(themeService);

        AddControl(_form);
        AddControl(_checkBoxPush);
        AddControl(_cleanupButton);
        AddControl(_list);
        AddCommand(new SyncCommand());
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

    public string Status => _status;

    /// <summary>The second status line: guidance under a failed step, empty otherwise.</summary>
    public string StatusDetail => _statusDetail;

    /// <summary>The reload started last — nothing waits on it; it exists so a test can.</summary>
    public Task RefreshTask { get; private set; } = Task.CompletedTask;

    /// <summary>The sync started last — nothing waits on it; it exists so a test can.</summary>
    public Task SyncTask { get; private set; } = Task.CompletedTask;

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
            // Opens maximized: the branch and conflict lists get the whole screen.
            CenterInParent(Parent.ViewSize);
        }

        var inner = Math.Max(1, ViewSize.Width - 4);
        _form.Position = new Position(2, 4);
        _form.ViewSize = new Size(inner, 1);
        _checkBoxPush.Position = new Position(2, 5);
        _checkBoxPush.ViewSize = new Size(inner, 1);
        _cleanupButton.Position = new Position(2, 6);
        _cleanupButton.ViewSize = new Size(Math.Min(inner, _cleanupButton.Title.Length + 4), 1);
        _list.Position = new Position(1, 8);
        _list.ViewSize = new Size(Math.Max(1, ViewSize.Width - 2), Math.Max(1, ViewSize.Height - 11));
        base.OnInitializeLayout();
    }

    /// <summary>Reloads the status, the branches and the conflicts. A running reload or sync is left to finish.</summary>
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

    /// <summary>F6: checks out the local main branch (main, else master), then reloads. Waits for a running sync to finish.</summary>
    public Task SwitchToMainAsync()
    {
        if (!SyncTask.IsCompleted)
        {
            SetStatus(Say("Sync.Status.Busy", "A sync or branch switch is still running."), isError: false);
            return SyncTask;
        }

        SetStatus(Say("Sync.Status.SwitchingToMain", "Switching to the main branch…"), isError: false);
        SyncTask = Task.Run(async () =>
        {
            try
            {
                var root = await ResolveRootAsync();
                if (root is null)
                {
                    SetStatus(Say("Sync.Status.NotRepo", "This folder is not in a git repository."), isError: true);
                    return;
                }

                await RefreshTask;
                var main = await _gitService.GetMainBranchAsync(root);
                if (main is null)
                {
                    SetStatus(Say("Sync.Status.NoMain", "This repository has no local main or master branch."), isError: true);
                    return;
                }

                var checkout = await _gitService.CheckoutBranchAsync(root, main);
                await LoadAsync(root);
                SetStatus(checkout.Success
                    ? Say("Sync.Status.OnMain", "On {0}.", main)
                    : Say("Sync.Status.SwitchFailed", "Switch failed: {0}", FirstLine(checkout.Output)), isError: !checkout.Success);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetStatus(exception.Message, isError: true);
            }
        });
        return SyncTask;
    }

    /// <summary>Enter: pull with the chosen reconciliation, then push when ticked. A failed step stops the sync there.</summary>
    public Task SyncAsync()
    {
        if (!SyncTask.IsCompleted)
        {
            SetStatus(Say("Sync.Status.Busy", "A sync or branch switch is still running."), isError: false);
            return SyncTask;
        }

        var mode = PullMode;
        var push = PushAfterPull;
        SetStatus(Say("Sync.Status.Syncing", "Syncing with the remote…"), isError: false);
        SyncTask = Task.Run(async () =>
        {
            try
            {
                var root = await ResolveRootAsync();
                if (root is null)
                {
                    ReportSyncResult(Say("Sync.Status.NotRepo", "This folder is not in a git repository."), isError: true);
                    return;
                }

                // A reload still running is finished first, so the sync acts on what it found.
                await RefreshTask;

                // The status is read again here: the sync must act on the repository as it is now, not as the last reload left it.
                var status = await _gitService.GetStatusAsync(root);
                if (status.Upstream is null)
                {
                    ReportSyncResult(Say("Sync.Status.NoUpstream", "The branch has no upstream branch to sync with."), isError: true);
                    return;
                }

                var pull = await _gitService.PullAsync(root, mode);
                if (!pull.Success)
                {
                    await LoadAsync(root);
                    ReportSyncResult(Say("Sync.Status.PullFailed", "Pull failed: {0}", FirstLine(pull.Output)), isError: true,
                        detail: Say("Sync.Status.PullFailedDetail", "Nothing was pushed. If git started a rebase or merge, it is left unfinished."));
                    return;
                }

                if (push)
                {
                    var pushed = await _gitService.PushAsync(root);
                    if (!pushed.Success)
                    {
                        await LoadAsync(root);
                        ReportSyncResult(Say("Sync.Status.PushFailed", "Push failed: {0}", FirstLine(pushed.Output)), isError: true);
                        return;
                    }
                }

                await LoadAsync(root);
                ReportSyncResult(Say("Sync.Status.Synced", "Synced with the remote."), isError: false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ReportSyncResult(exception.Message, isError: true);
            }
        });
        return SyncTask;
    }

    /// <summary>
    /// The Clean up button: asks which local branches are merged into the main branch and waits for a y/n. Nothing is deleted
    /// until the answer is y. Uses the merged list the last reload read, and the run checks it again before deleting.
    /// </summary>
    public void BeginCleanup()
    {
        if (!SyncTask.IsCompleted)
        {
            SetStatus(Say("Sync.Status.Busy", "A sync or branch switch is still running."), isError: false);
            return;
        }

        if (_mainBranch is null)
        {
            SetStatus(Say("Sync.Status.NoMain", "This repository has no local main or master branch."), isError: true);
            return;
        }

        var names = MergedCleanupCandidates(_branches, _mergedRefs, _mainBranch, currentBranch: _repository.Branch);
        if (names.Count == 0)
        {
            SetStatus(Say("Sync.Cleanup.None", "No local branch is merged into {0}, so there is nothing to clean up.", _mainBranch), isError: false);
            return;
        }

        _pendingCleanup = names;
        SetStatus(Say("Sync.Cleanup.Confirm", "Delete {0} local branch(es) merged into {1}? (y/n)", names.Count, _mainBranch), isError: true,
            detail: string.Join(", ", names));
    }

    /// <summary>
    /// The local branches that may be deleted as merged: every local branch whose tip is already in the main branch,
    /// except the main branch itself and the one checked out.
    /// </summary>
    internal static IReadOnlyList<string> MergedCleanupCandidates(IEnumerable<GitBranch> branches, IReadOnlySet<string> mergedRefs, string mainBranch, string currentBranch) =>
        branches
            .Where(branch => !branch.IsRemote && !branch.IsCurrent)
            .Select(branch => branch.Name)
            .Where(name => name != mainBranch && name != currentBranch && mergedRefs.Contains("refs/heads/" + name))
            .ToList();

    /// <summary>Deletes the branches the person confirmed, after checking again that each is still merged and not checked out.</summary>
    private void RunCleanup(IReadOnlyList<string> names, string mainBranch)
    {
        SetStatus(Say("Sync.Cleanup.Deleting", "Deleting the merged branches…"), isError: false);
        SyncTask = Task.Run(async () =>
        {
            try
            {
                var root = await ResolveRootAsync();
                if (root is null)
                {
                    ReportSyncResult(Say("Sync.Status.NotRepo", "This folder is not in a git repository."), isError: true);
                    return;
                }

                await RefreshTask;
                var status = await _gitService.GetStatusAsync(root);
                var merged = await _gitService.GetMergedRefsAsync(root, mainBranch);

                var deleted = 0;
                var failed = new List<string>();
                var firstError = string.Empty;
                foreach (var name in names)
                {
                    if (name == status.Branch || name == mainBranch || !merged.Contains("refs/heads/" + name))
                    {
                        continue;
                    }

                    // Force: each tip was just checked to be contained in the main branch, so no commit is lost. The safe delete
                    // would compare against the checked-out branch instead, which is not necessarily main.
                    var result = await _gitService.DeleteBranchAsync(root, name, force: true);
                    if (result.Success)
                    {
                        deleted++;
                    }
                    else
                    {
                        failed.Add(name);
                        firstError = string.IsNullOrEmpty(firstError) ? FirstLine(result.Output) : firstError;
                    }
                }

                await LoadAsync(root);
                if (failed.Count == 0)
                {
                    ReportSyncResult(Say("Sync.Cleanup.Done", "Deleted {0} merged branch(es).", deleted), isError: false);
                }
                else
                {
                    ReportSyncResult(Say("Sync.Cleanup.Failed", "Deleted {0}; {1} could not be deleted: {2}", deleted, failed.Count, firstError), isError: true,
                        detail: string.Join(", ", failed));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                ReportSyncResult(exception.Message, isError: true);
            }
        });
    }

    /// <summary>
    /// The outcome of an Enter sync: the status line in the dialog, and a toast so the result is seen even when the
    /// dialog is closed while the sync runs. The toast carries the status line's text and, on failure, its guidance.
    /// </summary>
    private void ReportSyncResult(string status, bool isError, string detail = "")
    {
        SetStatus(status, isError, detail);
        _notifications?.Notify(
            Say("Sync.Notify.Title", "Git sync"),
            string.IsNullOrEmpty(detail) ? status : status + " " + detail,
            isError ? NotificationSeverity.Error : NotificationSeverity.Success);
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
        _mainBranch = await _gitService.GetMainBranchAsync(root);
        _mergedRefs = _mainBranch is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : await _gitService.GetMergedRefsAsync(root, _mainBranch) ?? new HashSet<string>(StringComparer.Ordinal);
        _conflicts = _repository.Unstaged.Where(file => file.IsConflicted).Select(file => file.Path).ToList();
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
            rows.AddRange(_conflicts.Select(path => ($" ! {path}", colorScheme.Error)));
        }

        rows.Add((string.Empty, colorScheme.Foreground));
        rows.Add((_mainBranch is null
            ? Say("Sync.HeaderLocal", "Local branches")
            : Say("Sync.HeaderLocalMerged", "Local branches  (merged = already in {0})", _mainBranch), colorScheme.Foreground));
        rows.AddRange(_branches.Where(branch => !branch.IsRemote)
            .Select(branch => ($" {(branch.IsCurrent ? "*" : " ")} {branch.Name}{(branch.Upstream is null ? string.Empty : "  → " + branch.Upstream)}{MergedTag(branch)}", branch.IsCurrent ? Color.Green : colorScheme.Foreground)));
        rows.Add((string.Empty, colorScheme.Foreground));
        rows.Add((Say("Sync.HeaderRemote", "Remote branches"), colorScheme.Foreground));
        rows.AddRange(_branches.Where(branch => branch.IsRemote).Select(branch => ($"   {branch.Name}{MergedTag(branch)}", colorScheme.DisabledForeground)));
        return rows;
    }

    /// <summary>"  (merged)" when the branch's tip is already in the main branch; empty otherwise.</summary>
    private string MergedTag(GitBranch branch)
    {
        var fullName = branch.IsRemote ? "refs/remotes/" + branch.Name : "refs/heads/" + branch.Name;
        return _mergedRefs.Contains(fullName) ? "  " + Say("Sync.Merged", "(merged)") : string.Empty;
    }

    /// <summary>Sets the one-line status; <paramref name="detail"/> is an optional second line of guidance under it.</summary>
    private void SetStatus(string status, bool isError, string detail = "")
    {
        _status = status;
        _statusIsError = isError;
        _statusDetail = detail;
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
        if (_pendingCleanup.Count > 0)
        {
            yield return ("Y", iConsole.Localization.UiText.Get("Common.Confirm", "Confirm"));
            yield return ("N / Esc", iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"));
            yield break;
        }

        yield return ("Enter", UiText.Get("Sync.Hint.Sync", "Pull, then push: runs the sync"));
        yield return ("Alt+C", UiText.Get("Sync.Hint.Cleanup", "Delete the local branches merged into main (asks first)"));
        yield return ("Tab", UiText.Get("Sync.Hint.Tab", "Move between the pull mode, the push option and the branch list"));
        yield return ("Space", UiText.Get("Sync.Hint.Push", "Tick or untick the push after the pull"));
        yield return ("F5", UiText.Get("Sync.Hint.Reload", "Reload the branches and conflicts"));
        yield return ("F6", UiText.Get("Sync.Hint.Main", "Switch to the main branch"));
        yield return ("Up/Down", UiText.Get("Sync.Hint.Scroll", "Scroll the branches and conflicts (chooses the pull mode when it has focus)"));
        yield return ("PgUp/PgDn", UiText.Get("Sync.Hint.Page", "Scroll the branches and conflicts a page at a time"));
        yield return ("Esc", UiText.Get("Sync.Hint.Close", "Close the dialog"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_pendingCleanup.Count > 0)
        {
            return HandleCleanupAnswer(keyEvent);
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F5 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            _ = RefreshAsync();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.F6 && !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            _ = SwitchToMainAsync();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.C && keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift)
        {
            BeginCleanup();
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

    /// <summary>While the cleanup waits for its answer: y deletes, n or Esc keeps the branches, and every other key is held.</summary>
    private bool HandleCleanupAnswer(KeyEvent keyEvent)
    {
        var answer = char.ToLowerInvariant(keyEvent.KeyChar);
        if (answer != 'y' && answer != 'n' && keyEvent.ConsoleKey != ConsoleKey.Escape)
        {
            return true;
        }

        var names = _pendingCleanup;
        _pendingCleanup = [];
        if (answer == 'y')
        {
            RunCleanup(names, _mainBranch ?? string.Empty);
        }
        else
        {
            SetStatus(UiText.Get("Msg.Cancelled", "Cancelled."), isError: false);
        }

        return true;
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
        // The root is what the sync acts on; the panel's folder may be a sub-folder of it.
        Line(1, Say("Sync.RepositoryLine", "Repository: {0}", _root ?? _folder ?? "—"), foreground);
        Line(2, Say("Sync.BranchLine", "Branch: {0}  ({1})", branch, upstream), foreground);

        var rule = new ConxelString(Border.Horizontal.Repeat(Math.Max(0, width - 2)), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1, AbsolutePosition.Y + 7), new Size(rule.Count, 1), rule);

        // When the list scrolls, its visible range sits at the right end of the rule.
        var window = _list.Window;
        if (window.Total > window.Visible)
        {
            var label = " " + Say("Sync.Position", "{0}–{1} of {2}", window.First, window.Last, window.Total) + " ";
            if (label.Length <= rule.Count)
            {
                var labelOffset = rule.Count - label.Length;
                buffer.Write(ZOrder, new Position(AbsolutePosition.X + 1 + labelOffset, AbsolutePosition.Y + 7), new Size(label.Length, 1),
                    new ConxelString(label, foreground, colorScheme.Background));
            }
        }

        Line((int)ViewSize.Height - 3, _status, _statusIsError ? colorScheme.Error : foreground);
        Line((int)ViewSize.Height - 2, _statusDetail, foreground);

        var hints = (" " + Say("Sync.Hints", "Enter:sync  F5:reload  ↑↓:scroll  Esc:close") + " ").FitHints(Math.Max(0, width - 4));
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

    /// <summary>The Clean up button: asks about the merged branches. The dialog does the work, so the question stays with it.</summary>
    private sealed class CleanupMergedCommand : Command
    {
        public override string Title => "Clean up merged";
        public override string Description => "Delete the local branches merged into the main branch (asks first).";

        public override Task ExecuteAsync(object? args)
        {
            (Control as GitSyncDialog ?? Control?.Parent as GitSyncDialog)?.BeginCleanup();
            return Task.CompletedTask;
        }
    }

    /// <summary>Read-only rows of the branches and conflicts, each with its own colour, scrolled with the arrows and page keys.</summary>
    private sealed class SyncList(IThemeService themeService) : Control(themeService)
    {
        private readonly Lock _lock = new();
        private IReadOnlyList<(string Text, Color Color)> _rows = [];
        private int _top;

        public int PageSize => Math.Max(1, (int)ViewSize.Height - 1);

        /// <summary>The rows on screen as 1-based numbers (First..Last) out of Total, with the number the screen holds (Visible).</summary>
        public (int First, int Last, int Total, int Visible) Window
        {
            get
            {
                lock (_lock)
                {
                    var visible = Math.Max(1, (int)ViewSize.Height);
                    var total = _rows.Count;
                    var first = total == 0 ? 0 : _top + 1;
                    var last = Math.Min(_top + visible, total);
                    return (first, last, total, visible);
                }
            }
        }

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
