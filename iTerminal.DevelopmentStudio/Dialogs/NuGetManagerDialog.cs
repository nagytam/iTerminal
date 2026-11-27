using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Dialogs;
using iTerminal.DevelopmentStudio.Model.NuGet;
using iTerminal.DevelopmentStudio.Services.NuGet;

namespace iTerminal.DevelopmentStudio.Dialogs;

/// <summary>
/// The NuGet packages of a solution or a project. The left column is the scope: the whole solution, or one of its projects
/// (a project opened on its own has just that one). The right column lists the packages of the scope with the version each
/// asks for and the newest one when it is outdated; the output below shows every <c>dotnet</c> command the dialog ran.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Enter updates the package under the cursor to its newest version; Shift+U updates every outdated package in the scope
/// after a y/n; Del removes the package from the scope after a y/n; I installs a package (typed in a prompt) into the selected
/// project; F5 reads the packages again; X cancels a command that is running; Esc closes and cancels too.</item>
/// <item>Only top-level package references are listed and changed. Transitive packages are left to their owners.</item>
/// <item>Every command runs on a background task, one at a time. A worker never touches the controls: it queues the work for the
/// UI thread (<see cref="Post"/>), and <see cref="OnIdle"/> runs it.</item>
/// <item>The dialog has no text box of its own, so the letter keys are commands; the install prompt is a separate dialog.</item>
/// </list>
/// </remarks>
public class NuGetManagerDialog : SplitPaneDialog
{
    /// <summary>One row of the scope column. <see cref="ProjectPath"/> is null for the whole-solution row.</summary>
    private sealed record ScopeRow(string Name, string? ProjectPath);

    /// <summary>One row of the package column: a package id and the references to it in the scope.</summary>
    public sealed record PackageRow(string PackageId, IReadOnlyList<NuGetPackageUsage> Usages, bool ShowProjectCount);

    /// <summary>One <c>dotnet</c> command: an update (<see cref="Version"/>), an install (no version) or a removal.</summary>
    private sealed record Change(string ProjectPath, string PackageId, string? Version, bool Remove);

    /// <summary>A pending y/n question: the changes it applies, and the title their summary line starts with.</summary>
    private sealed record Confirmation(string Question, IReadOnlyList<Change> Changes, string Title);

    private sealed class ScopeListBox(IListBoxModel<ScopeRow> listModel, IThemeService themeService)
        : ListBox<ScopeRow>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            return ListModel.ListItems[index].Item.ProjectPath is null ? Color.Cyan : null;
        }
    }

    private sealed class PackageListBox(IListBoxModel<PackageRow> listModel, IThemeService themeService)
        : ListBox<PackageRow>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            return ListModel.ListItems[index].Item.Usages.Any(usage => usage.LatestVersion is not null) ? Color.Yellow : null;
        }
    }

    private sealed class OutputListBox(IListBoxModel<string> listModel, IThemeService themeService)
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
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase)) return Color.Red;
            if (line.Contains("warning", StringComparison.OrdinalIgnoreCase)) return Color.Yellow;
            return null;
        }
    }

    private readonly INuGetPackageService _packages;
    private readonly IPromptService _prompts;

    private readonly ConcurrentObservableCollection<ScopeRow> _scopeItems = new();
    private readonly ConcurrentObservableCollection<PackageRow> _packageItems = new();
    private readonly ConcurrentObservableCollection<string> _outputItems = new();
    private readonly ScopeListBox _scopeListBox;
    private readonly PackageListBox _packageListBox;
    private readonly OutputListBox _outputListBox;

    // Written by background tasks under _gate, run by OnIdle on the UI thread.
    private readonly object _gate = new();
    private readonly Queue<Action> _posted = new();

    /// <summary>Cancelled when the dialog closes: a command in progress stops with it.</summary>
    private readonly CancellationTokenSource _lifetime = new();

    // Touched on the UI thread only.
    private readonly List<string> _output = [];
    private CancellationTokenSource? _operation;
    private long _listRequest;
    private bool _busy;
    private bool _loaded;
    private bool _solutionScope;
    private string _solutionName = string.Empty;
    private IReadOnlyList<NuGetPackageUsage> _usages = [];
    private IReadOnlyList<string> _problems = [];
    private Confirmation? _awaiting;
    private int _shownScope = -2;
    private bool _initialScopeChosen;
    private string _status = string.Empty;
    private bool _statusIsError;

    /// <summary>The solution (.sln or .slnx) or the project (.csproj and the like) whose packages are shown. Set before the dialog is shown.</summary>
    public string TargetPath { get; set; } = string.Empty;

    /// <summary>The project the scope starts on, when the dialog opens on a project. Null starts on the whole solution.</summary>
    public string? InitialProjectPath { get; set; }

    /// <summary>The <c>dotnet</c> host the commands run with: the SDK the studio uses, or the one on the PATH.</summary>
    public string DotnetHost { get; set; } = "dotnet";

    public NuGetManagerDialog(INuGetPackageService packages, IPromptService prompts, IThemeService themeService)
        : base(nameof(NuGetManagerDialog), themeService)
    {
        _packages = packages;
        _prompts = prompts;
        Title = UiText.Get("NuGet.Title", "NuGet packages");

        _scopeListBox = new ScopeListBox(new ListBoxModel<ScopeRow>(_scopeItems) { TransformItem = TransformScopeRow }, themeService)
        {
            EmptyText = ReadingText,
        };
        _packageListBox = new PackageListBox(new ListBoxModel<PackageRow>(_packageItems) { TransformItem = TransformPackageRow }, themeService)
        {
            EmptyText = ReadingText,
        };
        _outputListBox = new OutputListBox(new ListBoxModel<string>(_outputItems) { TransformItem = TransformOutputLine }, themeService)
        {
            CanFocus = false,
        };

        AddControl(_scopeListBox);
        AddControl(_packageListBox);
        AddControl(_outputListBox);
    }

    private static string ReadingText => UiText.Get("NuGet.Reading", " Reading the packages…");

    private static ListItem<ScopeRow> TransformScopeRow(ScopeRow row, CancellationToken cancellationToken) =>
        new(row) { Value = " " + row.Name, IsEnabled = true };

    private static ListItem<string> TransformOutputLine(string line, CancellationToken cancellationToken) =>
        new(line) { Value = line, IsEnabled = true };

    private static ListItem<PackageRow> TransformPackageRow(PackageRow row, CancellationToken cancellationToken)
    {
        string requested = string.Join(", ", row.Usages.Select(usage => usage.RequestedVersion).Distinct(StringComparer.OrdinalIgnoreCase));
        string latest = string.Join(", ", row.Usages.Where(usage => usage.LatestVersion is not null).Select(usage => usage.LatestVersion!).Distinct(StringComparer.OrdinalIgnoreCase));
        string state = latest.Length == 0 ? UiText.Get("NuGet.UpToDate", "up to date") : latest;
        string projects = row.ShowProjectCount
            ? string.Format(UiText.Get("NuGet.RowProjects", "{0} project(s)"), row.Usages.Count)
            : string.Empty;
        return new(row) { Value = $" {row.PackageId,-40} {requested,-16} {state,-18} {projects}", IsEnabled = true };
    }

    /// <summary>The scope rows, for a test to read.</summary>
    public IReadOnlyList<string> ScopeNames() => _scopeItems.Select(row => row.Name).ToList();

    /// <summary>The name of the scope under the cursor, for a test to read.</summary>
    public string? SelectedScopeName => SelectedScope?.Name;

    /// <summary>The package rows of the scope under the cursor, for a test to read.</summary>
    public IReadOnlyList<PackageRow> PackageRows() => _packageItems.ToList();

    /// <summary>The output lines shown, for a test to read.</summary>
    public IReadOnlyList<string> OutputLines()
    {
        return _output.ToList();
    }

    /// <summary>The last status line.</summary>
    public string Status => _status;

    /// <summary>Whether a command or a listing is running.</summary>
    public bool IsBusy => _busy;

    /// <summary>Whether a y/n question is waiting for an answer.</summary>
    public bool IsConfirming => _awaiting is not null;

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        RequestListing();
    }

    /// <summary>Closing cancels a command in progress: the dialog is gone, so nothing would show its outcome.</summary>
    public override void Close()
    {
        _lifetime.Cancel();
        _operation?.Cancel();
        base.Close();
    }

    public override void OnIdle()
    {
        RunPosted();
        SyncScopeRows();
        base.OnIdle();
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
            var midH = ih - 2; // minus header row and footer row
            if (midH > 3)
            {
                var leftW = Math.Max(24, iw * 34 / 100);
                var rightX = 1 + leftW + 1;
                var rightW = iw - leftW - 1;
                var packagesH = Math.Max(2, (midH - 2) * 3 / 5);
                var outputH = Math.Max(0, midH - 2 - packagesH);

                _scopeListBox.Position = new Position(1, midY + 1);
                _scopeListBox.ViewSize = new Size(leftW, Math.Max(0, midH - 1));

                _packageListBox.Position = new Position(rightX, midY + 1);
                _packageListBox.ViewSize = rightW > 0 ? new Size(rightW, packagesH) : new Size(0, 0);

                _outputListBox.Position = new Position(rightX, midY + 2 + packagesH);
                _outputListBox.ViewSize = rightW > 0 ? new Size(rightW, outputH) : new Size(0, 0);
            }
        }

        base.OnInitializeLayout();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        yield return ("Enter", UiText.Get("NuGet.HintUpdate", "Update the package under the cursor to its newest version"));
        yield return ("Shift+U", UiText.Get("NuGet.HintUpdateAll", "Update every outdated package in the scope"));
        yield return ("Del", UiText.Get("NuGet.HintRemove", "Remove the package from the scope"));
        yield return ("I", UiText.Get("NuGet.HintInstall", "Install a package into the selected project"));
        yield return ("F5", UiText.Get("NuGet.HintRefresh", "Read the packages again"));
        yield return ("X", UiText.Get("NuGet.HintCancel", "Cancel the running command"));
        yield return ("Tab", UiText.Get("NuGet.HintPane", "Switch between the scope and the packages"));
        yield return ("Esc", UiText.Get("NuGet.HintClose", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_awaiting is { } pending && keyEvent.ConsoleKey != ConsoleKey.Escape)
        {
            _awaiting = null;
            if (!keyEvent.Alt && !keyEvent.Control && char.ToUpperInvariant(keyEvent.KeyChar) == 'Y')
            {
                Run(pending.Changes, pending.Title);
            }
            else
            {
                SetStatus(UiText.Get("NuGet.Cancelled", "Cancelled."), isError: false);
            }

            return true;
        }

        bool plain = !keyEvent.Alt && !keyEvent.Control && !keyEvent.Shift;
        if (plain)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.Enter:
                    UpdateSelected();
                    return true;
                case ConsoleKey.Delete:
                    RemoveSelected();
                    return true;
                case ConsoleKey.F5:
                    RequestListing();
                    return true;
            }
        }

        if (!keyEvent.Alt && !keyEvent.Control)
        {
            // Case matters for U: Shift+U updates everything, and u does the same after the same y/n.
            if (keyEvent.KeyChar == 'U' || (plain && keyEvent.KeyChar == 'u'))
            {
                UpdateAll();
                return true;
            }

            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'i' when plain:
                    InstallRequested();
                    return true;
                case 'x' when plain:
                    CancelOperation();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #region Scope and rows

    private ScopeRow? SelectedScope => Cursor(_scopeListBox);

    private PackageRow? SelectedPackage => Cursor(_packageListBox);

    private static T? Cursor<T>(ListBox<T> box) where T : class
    {
        var items = box.ListModel.ListItems;
        var index = (int)box.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    /// <summary>The references in the scope under the cursor: all of the solution's, or those of one project.</summary>
    private IEnumerable<NuGetPackageUsage> ScopeUsages()
    {
        var scope = SelectedScope;
        return scope?.ProjectPath is null
            ? _usages
            : _usages.Where(usage => SamePath(usage.ProjectPath, scope.ProjectPath));
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Rebuilds the package column when the cursor has moved to another scope, or the scopes were rebuilt.</summary>
    private void SyncScopeRows()
    {
        int index = (int)_scopeListBox.ScrollableModel.CursorPosition.Y;
        if (index == _shownScope) return;
        _shownScope = index;
        RebuildPackageRows();
    }

    /// <summary>The packages of the scope under the cursor: grouped by id for the whole solution, one row per reference for a project.</summary>
    private void RebuildPackageRows()
    {
        var scope = SelectedScope;
        var rows = new List<PackageRow>();
        if (scope is not null)
        {
            bool solution = scope.ProjectPath is null;
            var usages = ScopeUsages().ToList();
            if (solution)
            {
                rows.AddRange(usages
                    .GroupBy(usage => usage.PackageId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new PackageRow(group.Key, group.ToList(), ShowProjectCount: true)));
            }
            else
            {
                rows.AddRange(usages
                    .OrderBy(usage => usage.PackageId, StringComparer.OrdinalIgnoreCase)
                    .Select(usage => new PackageRow(usage.PackageId, [usage], ShowProjectCount: false)));
            }
        }

        _packageItems.ReplaceAll(rows);
        _packageListBox.UpdateModel();
        _packageListBox.EmptyText = _loaded
            ? " " + UiText.Get("NuGet.NoPackages", "No packages in this scope.")
            : ReadingText;
    }

    /// <summary>Rebuilds the scope column from the projects of the target. The cursor stays on the same scope, or starts on the initial project.</summary>
    private void ApplyProjects(IReadOnlyList<string> projects)
    {
        // The scope the cursor is on is found again by its project path (null for the whole solution), so a re-read keeps it.
        ScopeRow? previous = _scopeItems.Count > 0 ? SelectedScope : null;
        var rows = new List<ScopeRow>();
        if (_solutionScope)
        {
            rows.Add(new ScopeRow(string.Format(UiText.Get("NuGet.ScopeSolution", "All projects in {0}"), _solutionName), null));
        }

        rows.AddRange(projects.Select(project => new ScopeRow(Path.GetFileNameWithoutExtension(project), project)));
        _scopeItems.ReplaceAll(rows);
        _scopeListBox.UpdateModel();

        int index = 0;
        if (!_initialScopeChosen && InitialProjectPath is not null)
        {
            index = rows.FindIndex(row => row.ProjectPath is not null && SamePath(row.ProjectPath, InitialProjectPath));
        }
        else if (previous is not null)
        {
            index = rows.FindIndex(row => SameScope(row, previous));
        }

        _initialScopeChosen = true;
        _scopeListBox.ScrollableModel.CursorPosition.Y = Math.Clamp(index, 0, Math.Max(0, rows.Count - 1));
        _shownScope = -2;
    }

    private static bool SameScope(ScopeRow a, ScopeRow b) =>
        a.ProjectPath is null || b.ProjectPath is null
            ? a.ProjectPath is null && b.ProjectPath is null
            : SamePath(a.ProjectPath, b.ProjectPath);

    #endregion

    #region Background work

    /// <summary>Queues UI-thread work from a background task. <see cref="OnIdle"/> runs it, in the order it was queued.</summary>
    private void Post(Action action)
    {
        lock (_gate) _posted.Enqueue(action);
    }

    private void RunPosted()
    {
        List<Action> work;
        lock (_gate)
        {
            if (_posted.Count == 0) return;
            work = [.. _posted];
            _posted.Clear();
        }

        foreach (var action in work)
        {
            action();
        }
    }

    /// <summary>Marks the dialog busy and returns the token of the command that starts; false (with a status line) when one is running.</summary>
    private bool TryStartOperation(out CancellationToken token)
    {
        token = default;
        if (_busy)
        {
            SetStatus(UiText.Get("NuGet.Busy", "A command is still running; X cancels it."), isError: false);
            return false;
        }

        _busy = true;
        _operation?.Dispose();
        _operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        token = _operation.Token;
        return true;
    }

    private void EndOperation()
    {
        _busy = false;
        _operation?.Dispose();
        _operation = null;
    }

    /// <summary>Reads the projects and the packages of the target on a background task.</summary>
    private void RequestListing()
    {
        if (!TryStartOperation(out var token)) return;

        long request = ++_listRequest;
        SetStatus(UiText.Get("NuGet.Reading", "Reading the packages…"), isError: false);
        _solutionScope = IsSolutionFile(TargetPath);
        _solutionName = Path.GetFileNameWithoutExtension(TargetPath);
        string target = TargetPath;
        string host = DotnetHost;

        _ = Task.Run(async () =>
        {
            try
            {
                var projects = _packages.ProjectsOf(target);
                var listing = await _packages.ListAsync(target, host, token);
                Post(() => FinishListing(request, projects, listing, cancelled: false, error: null));
            }
            catch (OperationCanceledException)
            {
                Post(() => FinishListing(request, null, null, cancelled: true, error: null));
            }
            catch (Exception exception)
            {
                Post(() => FinishListing(request, null, null, cancelled: false, error: exception.Message));
            }
        }, CancellationToken.None);
    }

    private void FinishListing(long request, IReadOnlyList<string>? projects, NuGetListing? listing, bool cancelled, string? error)
    {
        if (request != _listRequest) return;
        EndOperation();

        if (cancelled)
        {
            SetStatus(UiText.Get("NuGet.Cancelled", "Cancelled."), isError: false);
            return;
        }

        if (error is not null)
        {
            _loaded = true;
            SetStatus(string.Format(UiText.Get("NuGet.ReadFailed", "Could not read the packages: {0}"), error), isError: true);
            return;
        }

        if (projects is not null)
        {
            ApplyProjects(projects);
        }

        if (listing is not null)
        {
            ApplyListing(listing);
            ShowListingStatus(listing);
        }
    }

    private void ApplyListing(NuGetListing listing)
    {
        _loaded = true;
        _usages = listing.Packages;
        _problems = listing.Problems;
        RebuildPackageRows();
    }

    private void ShowListingStatus(NuGetListing listing)
    {
        if (!listing.Succeeded)
        {
            SetStatus(listing.Problems.FirstOrDefault() ?? UiText.Get("NuGet.CouldNotRun", "Could not run dotnet to list the packages."), isError: true);
            return;
        }

        int projectCount = _usages.Select(usage => usage.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int outdated = _usages.Count(usage => usage.LatestVersion is not null);
        if (_problems.Count > 0)
        {
            SetStatus(_problems[0], isError: true);
            return;
        }

        SetStatus(string.Format(UiText.Get("NuGet.Summary", "{0} packages in {1} projects; {2} outdated."), _usages.Count, projectCount, outdated), isError: false);
    }

    /// <summary>Runs the changes one after another, then reads the packages again. The summary says how many succeeded.</summary>
    private void Run(IReadOnlyList<Change> changes, string title)
    {
        if (!TryStartOperation(out var token)) return;

        SetStatus(title + "...", isError: false);
        string host = DotnetHost;
        string target = TargetPath;
        _ = Task.Run(async () =>
        {
            int succeeded = 0;
            int failed = 0;
            NuGetListing? listing = null;
            bool cancelled = false;
            try
            {
                foreach (var change in changes)
                {
                    token.ThrowIfCancellationRequested();
                    string display = change.Remove
                        ? "dotnet " + string.Join(' ', NuGetCommands.RemoveArguments(change.ProjectPath, change.PackageId))
                        : "dotnet " + string.Join(' ', NuGetCommands.AddArguments(change.ProjectPath, change.PackageId, change.Version));
                    Post(() => AppendOutput("$ " + display));

                    void OnLine(string line) => Post(() => AppendOutput(line));
                    int exitCode = change.Remove
                        ? await _packages.RemoveAsync(change.ProjectPath, change.PackageId, host, OnLine, token)
                        : await _packages.AddAsync(change.ProjectPath, change.PackageId, change.Version, host, OnLine, token);
                    if (exitCode == 0) succeeded++; else failed++;
                }

                listing = await _packages.ListAsync(target, host, token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception exception)
            {
                Post(() => AppendOutput("Error: " + exception.Message));
                failed++;
            }

            int ok = succeeded;
            int bad = failed;
            bool wasCancelled = cancelled;
            NuGetListing? read = listing;
            Post(() => FinishRun(title, ok, bad, wasCancelled, read));
        }, CancellationToken.None);
    }

    private void FinishRun(string title, int succeeded, int failed, bool cancelled, NuGetListing? listing)
    {
        EndOperation();
        if (listing is not null)
        {
            ApplyListing(listing);
        }

        if (cancelled)
        {
            SetStatus(UiText.Get("NuGet.Cancelled", "Cancelled.") + " " + UiText.Get("NuGet.ReadAgain", "F5 reads the packages again."), isError: false);
            return;
        }

        SetStatus(string.Format(UiText.Get("NuGet.Done", "{0}: {1} succeeded, {2} failed."), title, succeeded, failed), isError: failed > 0);
    }

    private void AppendOutput(string line)
    {
        _output.Add(line);
        _outputItems.ReplaceAll(_output.ToArray());
        _outputListBox.UpdateModel();
        if (_outputListBox.ListModel.ListItems.Count > 0)
        {
            var rows = (int)_outputListBox.ViewSize.Height;
            var count = _outputListBox.ListModel.ListItems.Count;
            _outputListBox.ScrollableModel.ScrollPosition.Y = Math.Max(0, count - rows);
            _outputListBox.ScrollableModel.CursorPosition.Y = Math.Max(0, count - 1);
        }
    }

    private void CancelOperation()
    {
        if (!_busy || _operation is null)
        {
            return;
        }

        SetStatus(UiText.Get("Msg.Cancelling", "Cancelling..."), isError: false);
        _operation.Cancel();
    }

    #endregion

    #region Commands

    /// <summary>Enter: the package under the cursor goes to its newest version in every project of the scope that is outdated.</summary>
    private void UpdateSelected()
    {
        if (_busy) { SetStatus(UiText.Get("NuGet.Busy", "A command is still running; X cancels it."), isError: false); return; }
        if (SelectedPackage is not { } row)
        {
            SetStatus(UiText.Get("NuGet.SelectPackage", "Select a package first."), isError: true);
            return;
        }

        var changes = row.Usages
            .Where(usage => usage.LatestVersion is not null)
            .Select(usage => new Change(usage.ProjectPath, usage.PackageId, usage.LatestVersion, Remove: false))
            .ToList();
        if (changes.Count == 0)
        {
            SetStatus(string.Format(UiText.Get("NuGet.AlreadyNewest", "{0} is up to date."), row.PackageId), isError: false);
            return;
        }

        Run(changes, string.Format(UiText.Get("NuGet.TitleUpdate", "Update {0}"), row.PackageId));
    }

    /// <summary>Shift+U: every outdated reference in the scope goes to its newest version, after a y/n.</summary>
    private void UpdateAll()
    {
        if (_busy) { SetStatus(UiText.Get("NuGet.Busy", "A command is still running; X cancels it."), isError: false); return; }
        var changes = ScopeUsages()
            .Where(usage => usage.LatestVersion is not null)
            .Select(usage => new Change(usage.ProjectPath, usage.PackageId, usage.LatestVersion, Remove: false))
            .ToList();
        if (changes.Count == 0)
        {
            SetStatus(UiText.Get("NuGet.NothingOutdated", "Every package in this scope is up to date."), isError: false);
            return;
        }

        int projects = changes.Select(change => change.ProjectPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Ask(new Confirmation(
            string.Format(UiText.Get("NuGet.ConfirmUpdateAll", "Update {0} package references in {1} project(s)? (y/n)"), changes.Count, projects),
            changes,
            UiText.Get("NuGet.TitleUpdateAll", "Update all")));
    }

    /// <summary>Del: the package under the cursor is removed from every project of the scope that references it, after a y/n.</summary>
    private void RemoveSelected()
    {
        if (_busy) { SetStatus(UiText.Get("NuGet.Busy", "A command is still running; X cancels it."), isError: false); return; }
        if (SelectedPackage is not { } row)
        {
            SetStatus(UiText.Get("NuGet.SelectPackage", "Select a package first."), isError: true);
            return;
        }

        var changes = row.Usages.Select(usage => new Change(usage.ProjectPath, usage.PackageId, null, Remove: true)).ToList();
        Ask(new Confirmation(
            string.Format(UiText.Get("NuGet.ConfirmRemove", "Remove {0} from {1} project(s)? (y/n)"), row.PackageId, changes.Count),
            changes,
            string.Format(UiText.Get("NuGet.TitleRemove", "Remove {0}"), row.PackageId)));
    }

    private void Ask(Confirmation confirmation)
    {
        _awaiting = confirmation;
        SetStatus(confirmation.Question, isError: false);
    }

    /// <summary>I: a package is typed in a prompt and installed, at its newest version, into the selected project. The solution takes none.</summary>
    private void InstallRequested()
    {
        if (_busy) { SetStatus(UiText.Get("NuGet.Busy", "A command is still running; X cancels it."), isError: false); return; }
        if (SelectedScope?.ProjectPath is not { } project)
        {
            SetStatus(UiText.Get("NuGet.InstallNeedsProject", "Select a project to install into; a solution takes no package."), isError: true);
            return;
        }

        _ = AskForPackageAsync(project, Path.GetFileNameWithoutExtension(project));
    }

    /// <summary>Waits for the prompt on its own task; the install starts on the UI thread once an id was typed.</summary>
    private async Task AskForPackageAsync(string projectPath, string projectName)
    {
        string? typed = await _prompts.AskAsync(
            new TextPrompt<string>(string.Format(UiText.Get("NuGet.InstallAsk", "Package id to install into {0}"), projectName))
                .Validate(text => NuGetCommands.IsPackageId(text?.Trim())
                    ? null
                    : UiText.Get("NuGet.InstallInvalidId", "A package id has letters, digits, dots, dashes and underscores, and starts with a letter or digit.")),
            CancellationToken.None);
        if (string.IsNullOrWhiteSpace(typed))
        {
            return;
        }

        string id = typed.Trim();
        Post(() => Run([new Change(projectPath, id, null, Remove: false)], string.Format(UiText.Get("NuGet.TitleInstall", "Install {0}"), id)));
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

        DrawHeader(buffer, ix, iy, iw);
        DrawStatusAndHints(buffer, ix, iy + ih - 1, iw, _status, _statusIsError ? colorScheme.Error : colorScheme.Foreground, KeyHints, colorScheme);

        var midY = iy + 1;
        var midH = ih - 2;
        if (midH <= 3)
        {
            return;
        }

        var leftW = Math.Max(24, iw * 34 / 100);
        var rightX = ix + leftW + 1;
        var rightW = iw - leftW - 1;
        var packagesH = Math.Max(2, (midH - 2) * 3 / 5);

        DrawSectionHeader(buffer, ix, midY, leftW, UiText.Get("Solution.Projects", "Projects"), _scopeListBox.IsFocused, colorScheme);
        if (rightW > 0)
        {
            DrawSectionHeader(buffer, rightX, midY, rightW, UiText.Get("NuGet.Packages", "Packages"), _packageListBox.IsFocused, colorScheme);
            DrawSectionHeader(buffer, rightX, midY + 1 + packagesH, rightW, UiText.Get("Packages.Output", "Output"), false, colorScheme);
        }
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        int outdated = _usages.Count(usage => usage.LatestVersion is not null);
        string name = _solutionScope ? _solutionName : Path.GetFileNameWithoutExtension(TargetPath);
        string text = string.Format(UiText.Get("NuGet.Header", "{0}   ({1} packages, {2} outdated)"), name, _usages.Count, outdated);
        WriteLine(buffer, x, y, w, text, Color.Cyan, colorScheme.Background);
    }

    private static string KeyHints => UiText.Get("NuGet.Hints", "Enter:update  Shift+U:update all  Del:remove  I:install  F5:read again  X:cancel  Tab:switch  Esc:close");

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    #endregion

    private static bool IsSolutionFile(string path) =>
        path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
}
