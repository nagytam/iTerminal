using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.PackageManager;
using iTerminal.Core.Services.PackageManager;

namespace iTerminal.Core.Dialogs.PackageManager;

/// <summary>
/// Full-screen modal package manager client: the installed-packages list (or, after a search, the
/// search results) on the left, live install/uninstall output on the right. Backed by whichever
/// <see cref="IPackageManagerService"/> is registered for the host OS (winget on Windows, APT on
/// Debian/Ubuntu Linux, ...). All package manager work runs on background tasks; the key handler
/// and <see cref="Draw"/> never block on it.
/// </summary>
public class PackageManagerDialog : SplitPaneDialog
{
    private enum Mode { Installed, Search }

    /// <summary>Drives footer status coloring without relying on brittle substring matching of <see cref="_status"/>.</summary>
    private enum StatusSeverity { Info, Success, Error }

    private sealed class PackageListBox : ListBox<PackageInfo>
    {
        public PackageListBox(IListBoxModel<PackageInfo> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            ListModel.TransformItem = TransformPackage;
        }

        private ListItem<PackageInfo> TransformPackage(PackageInfo package, CancellationToken cancellationToken) =>
            new(package) { Value = $" {FormatRow(package)}", IsEnabled = true };

        private static string FormatRow(PackageInfo package)
        {
            var version = string.IsNullOrEmpty(package.AvailableVersion) || package.AvailableVersion == package.Version
                ? package.Version
                : $"{package.Version} -> {package.AvailableVersion}";
            return version.Length == 0
                ? $"{package.Name}  [{package.Id}]"
                : $"{package.Name}  [{package.Id}]  {version}";
        }

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            return ListModel.ListItems[index].Item.AvailableVersion is not null ? Color.Yellow : null;
        }
    }

    private sealed class PackageOutputListBox : ListBox<string>
    {
        public PackageOutputListBox(IListBoxModel<string> listModel, IThemeService themeService)
            : base(listModel, themeService)
        {
            CanFocus = false;
            ListModel.TransformItem = (line, ct) => new ListItem<string>(line) { Value = line, IsEnabled = true };
        }

        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }

            var line = ListModel.ListItems[index].Item;
            if (line.StartsWith("$ ", StringComparison.Ordinal)) return Color.Cyan;
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase)) return Color.Red;
            if (line.Contains("Successfully", StringComparison.OrdinalIgnoreCase)) return Color.Green;
            return null;
        }
    }

    private readonly IPackageManagerService _packageManagerService;

    private readonly ConcurrentObservableCollection<PackageInfo> _packageItems = new();
    private readonly ConcurrentObservableCollection<string> _outputItems = new();
    private readonly object _outputLock = new();
    private readonly List<string> _output = [];

    private readonly PackageListBox _packageListBox;
    private readonly PackageOutputListBox _outputListBox;
    private readonly TextBox _inputBox;

    private Mode _mode = Mode.Installed;
    private IReadOnlyList<PackageInfo> _installed = [];
    private IReadOnlyList<PackageInfo> _searchResults = [];
    private string _searchQuery = string.Empty;

    private bool _loading;
    private bool _busy;
    private string _message = string.Empty;
    private string _status = string.Empty;
    private StatusSeverity _statusSeverity = StatusSeverity.Info;
    private CancellationTokenSource? _runCts;

    private bool _followTail = true;
    private bool _syncingScroll;

    private Action<string>? _onInputSubmit;

    /// <summary>
    /// Whether the inline search box is up. Tracked here rather than read from the box's
    /// <c>IsVisible</c>, because <c>Control.ShowAsync</c> makes every child visible on the way in:
    /// read that way the box was "up" from the moment the dialog opened, so the footer — key hints,
    /// status, and the y/n prompt of every install and uninstall — was never drawn at all.
    /// </summary>
    private bool _inputActive;

    private string _confirmPrompt = string.Empty;
    private Action? _confirmAction;

    /// <summary>
    /// Bumped by every query. A query compares it against the value it started with before
    /// publishing, so a slow listing of installed packages cannot land on top of a search the user
    /// started after it (or two searches in the wrong order).
    /// </summary>
    private int _queryGeneration;

    public PackageManagerDialog(
        IPackageManagerService packageManagerService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(PackageManagerDialog), themeService)
    {
        _packageManagerService = packageManagerService;
        Title = "Packages";

        _packageListBox = new PackageListBox(new ListBoxModel<PackageInfo>(_packageItems), themeService)
        {
            EmptyText = " Loading…",
        };
        _outputListBox = new PackageOutputListBox(new ListBoxModel<string>(_outputItems), themeService);
        _inputBox = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            IsVisible = false,
            IsEnabled = false,
            CanFocus = false,
        };

        AddControl(_packageListBox);
        AddControl(_outputListBox);
        AddControl(_inputBox);

        _outputListBox.ScrollableModel.PropertyChanged += (_, e) =>
        {
            if (!_syncingScroll && e.PropertyName == nameof(IScrollableModel.CursorPosition))
            {
                _followTail = false;
            }
        };
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        _mode = Mode.Installed;
        _installed = [];
        _searchResults = [];
        _searchQuery = string.Empty;
        _output.Clear();
        _outputItems.ReplaceAll([]);
        _followTail = true;
        _busy = false;
        _status = string.Empty;
        _statusSeverity = StatusSeverity.Info;
        _runCts = null;

        StartRefreshInstalled();
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        ApplyInputVisibility();
    }

    private void ApplyInputVisibility()
    {
        _inputBox.CanFocus = _inputActive;
        _inputBox.IsVisible = _inputActive;
        _inputBox.IsEnabled = _inputActive;
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
                var leftW = Math.Max(24, iw * 40 / 100);
                var rightX = 1 + leftW + 1;
                var rightW = iw - leftW - 1;

                _packageListBox.Position = new Position(1, midY + 1); // +1 reserves the section-header band
                _packageListBox.ViewSize = new Size(leftW, Math.Max(0, midH - 1));

                _outputListBox.Position = new Position(rightX, midY + 1);
                _outputListBox.ViewSize = rightW > 0 ? new Size(rightW, Math.Max(0, midH - 1)) : new Size(0, 0);
            }

            _inputBox.Position = new Position(1, ih);
            _inputBox.ViewSize = new Size(iw, 1);
            _inputBox.ScrollableModel.ViewSize = _inputBox.ViewSize;
        }

        // Children must have their Position/ViewSize assigned before base.OnInitializeLayout()
        // recurses into them (each control's own OnInitializeLayout sizes its ScrollableModel from
        // its own ViewSize) and focuses the first focusable child.
        base.OnInitializeLayout();

        ResyncTailScroll();
    }

    #region Queries (background)

    private void StartRefreshInstalled() =>
        StartQuery(
            $"Loading installed packages from {_packageManagerService.DisplayName}…",
            ct => _packageManagerService.GetInstalledPackagesAsync(),
            packages =>
            {
                _installed = packages;
                _mode = Mode.Installed;
            });

    private void StartSearch(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return;
        }

        _searchQuery = query;
        StartQuery(
            $"Searching for \"{query}\"…",
            ct => _packageManagerService.SearchPackagesAsync(query),
            packages =>
            {
                _searchResults = packages;
                _mode = Mode.Search;
            });
    }

    /// <summary>
    /// Runs a listing query on a background task. The list says it is loading while it runs — an
    /// empty "Installed (0)" read as "nothing is installed" — and a query that throws (rather than
    /// returning a failed result) still ends the loading state instead of leaving it on for good.
    /// </summary>
    private void StartQuery(string loadingText, Func<CancellationToken, Task<PackageQueryResult>> query, Action<IReadOnlyList<PackageInfo>> apply)
    {
        var generation = Interlocked.Increment(ref _queryGeneration);
        _loading = true;
        _message = string.Empty;
        _packageListBox.EmptyText = " " + loadingText;
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            PackageQueryResult? result = null;
            string? error = null;
            try
            {
                result = await query(CancellationToken.None);
                if (!result.Success)
                {
                    error = result.Error;
                }
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            if (generation != Volatile.Read(ref _queryGeneration))
            {
                return;
            }

            if (error is not null)
            {
                _message = $"Error: {error}";
                _packageListBox.EmptyText = " Could not list packages — r retries.";
            }
            else
            {
                apply(result!.Packages);
                _packageListBox.EmptyText = _mode == Mode.Installed
                    ? " No packages installed."
                    : $" No package matches \"{_searchQuery}\" — s searches again.";
                SyncPackageList();
            }

            // Cleared only once the list is in place: "loaded" with the old (or no) rows still
            // showing is a moment in which Enter acts on a row that is about to be replaced.
            _loading = false;
            StateHasChanged();
        });
    }

    private void SyncPackageList()
    {
        var packages = _mode == Mode.Installed ? _installed : _searchResults;
        _packageItems.ReplaceAll(packages.ToList());
        _packageListBox.UpdateModel();
        var scrollableModel = _packageListBox.ScrollableModel;
        scrollableModel.CursorPosition.Y = packages.Count <= 0 ? 0 : Math.Clamp(scrollableModel.CursorPosition.Y, 0, packages.Count - 1);
        scrollableModel.ScrollPosition.Y = packages.Count <= 0 ? 0 : Math.Clamp(scrollableModel.ScrollPosition.Y, 0, packages.Count - 1);
    }

    private void ShowInstalledList()
    {
        if (_mode == Mode.Installed || _busy)
        {
            return;
        }

        _mode = Mode.Installed;
        SyncPackageList();
        StateHasChanged();
    }

    private new void Refresh()
    {
        if (_busy)
        {
            return;
        }

        if (_mode == Mode.Search)
        {
            StartSearch(_searchQuery);
        }
        else
        {
            StartRefreshInstalled();
        }
    }

    #endregion

    #region Install / uninstall (background)

    private PackageInfo? CurrentPackage()
    {
        var items = _packageListBox.ListModel.ListItems;
        var index = (int)_packageListBox.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < items.Count ? items[index].Item : null;
    }

    private void InstallSelected()
    {
        if (_mode != Mode.Search)
        {
            return;
        }

        var package = CurrentPackage();
        if (package is null)
        {
            return;
        }

        BeginConfirm($"Install {package.Name} ({package.Id})?  y:yes  any other key:no",
            () => RunPackageManager($"Installing {package.Name}...", ct => _packageManagerService.InstallAsync(package.Id, AppendOutput, ct)));
    }

    private void UninstallSelected()
    {
        if (_mode != Mode.Installed)
        {
            return;
        }

        var package = CurrentPackage();
        if (package is null)
        {
            return;
        }

        BeginConfirm($"Uninstall {package.Name} ({package.Id})?  y:yes  any other key:no",
            () => RunPackageManager($"Uninstalling {package.Name}...", ct => _packageManagerService.UninstallAsync(package.Id, AppendOutput, ct)));
    }

    /// <summary>Runs an install/uninstall in the background, streaming output, then refreshes the installed list.</summary>
    private void RunPackageManager(string busyMessage, Func<CancellationToken, Task<int>> action)
    {
        if (_busy)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _runCts = cts;
        _busy = true;
        _status = busyMessage;
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
            int exitCode;
            try
            {
                exitCode = await action(cts.Token);
            }
            catch (Exception exception)
            {
                AppendOutput($"Error: {exception.Message}");
                exitCode = -1;
            }

            _busy = false;
            var cancelled = cts.IsCancellationRequested;
            _status = cancelled ? "Cancelled." : exitCode == 0 ? "Done." : $"Failed (exit {exitCode}).";
            _statusSeverity = cancelled ? StatusSeverity.Info : exitCode == 0 ? StatusSeverity.Success : StatusSeverity.Error;
            StateHasChanged();

            if (!cancelled && exitCode == 0)
            {
                StartRefreshInstalled();
            }
        });
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

    private void AppendOutput(string line)
    {
        lock (_outputLock)
        {
            _output.Add(line);
        }

        SyncOutputItems();
        StateHasChanged();
    }

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

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_confirmAction is not null)
        {
            yield return ("Y", "Confirm");
            yield return ("Any other key", "Cancel");
            yield break;
        }

        if (_inputActive)
        {
            yield return ("Enter", "Submit");
            yield return ("Esc", "Cancel");
            yield break;
        }

        yield return ("Enter/Space", "Run the main action");
        yield return ("R", "Refresh the list");
        yield return ("S", "Search for packages");
        yield return ("I", "Show the installed packages");
        yield return ("X", "Cancel the running command");
        yield return ("PageUp/PageDown", "Scroll the output");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
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

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Spacebar:
            case ConsoleKey.Enter:
                PrimaryAction();
                return true;
            case ConsoleKey.PageUp:
                ScrollOutput(-10);
                return true;
            case ConsoleKey.PageDown:
                ScrollOutput(10);
                return true;
            case ConsoleKey.Escape:
                _runCts?.Cancel();
                Close();
                return true;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'r':
                Refresh();
                return true;
            case 's':
                BeginSearch();
                return true;
            case 'i':
                ShowInstalledList();
                return true;
            case 'x':
                Cancel();
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void PrimaryAction()
    {
        if (_busy)
        {
            _status = "Still working on the last request — x cancels it.";
            _statusSeverity = StatusSeverity.Info;
            StateHasChanged();
            return;
        }

        if (_mode == Mode.Installed)
        {
            UninstallSelected();
        }
        else
        {
            InstallSelected();
        }
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

    private void BeginSearch() => ShowInput("Search query", StartSearch);

    private void ShowInput(string label, Action<string> onSubmit)
    {
        _onInputSubmit = onSubmit;
        _inputActive = true;
        _inputBox.Label = label;
        _inputBox.Text = string.Empty;
        ApplyInputVisibility();
        _inputBox.SetFocus();
        StateHasChanged();
    }

    private void SubmitInput()
    {
        var text = _inputBox.Text;
        var callback = _onInputSubmit;
        HideInput();
        callback?.Invoke(text);
    }

    private void CancelInput() => HideInput();

    private void HideInput()
    {
        _inputBox.ResetFocus();
        _inputActive = false;
        ApplyInputVisibility();
        _inputBox.Text = string.Empty;
        _onInputSubmit = null;
        _packageListBox.SetFocus();
        StateHasChanged();
    }

    private bool HandleConfirmKey(KeyEvent keyEvent)
    {
        var action = _confirmAction;
        _confirmAction = null;
        _confirmPrompt = string.Empty;

        if (char.ToLowerInvariant(keyEvent.KeyChar) == 'y')
        {
            action?.Invoke();
        }
        else
        {
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

    #endregion

    #region Test seams

    internal string StatusForTest => _status;
    internal int OutputScrollForTest => (int)_outputListBox.ScrollableModel.ScrollPosition.Y;
    internal int OutputRowsForTest => (int)_outputListBox.ViewSize.Height;
    internal string MessageForTest => _message;
    internal bool IsBusyForTest => _busy;
    internal bool IsLoadingForTest => _loading;
    internal bool IsConfirmingForTest => _confirmAction is not null;
    internal bool IsInputActiveForTest => _inputActive;
    internal bool IsInputBoxVisibleForTest => _inputBox.IsVisible;
    internal int PackageCountForTest => _packageListBox.ListModel.ListItems.Count;
    internal string EmptyTextForTest => _packageListBox.EmptyText;

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
        if (!_inputActive)
        {
            // While the search TextBox occupies the footer row, don't overwrite its rendering.
            DrawFooter(buffer, ix, iy + ih - 1, iw, colorScheme);
        }

        var midY = iy + 1;
        var midH = ih - 2;
        if (midH <= 0)
        {
            return;
        }

        var leftW = Math.Max(24, iw * 40 / 100);
        var rightX = ix + leftW + 1;
        var rightW = iw - leftW - 1;

        var packages = _mode == Mode.Installed ? _installed : _searchResults;
        var leftTitle = _mode == Mode.Installed ? $"Installed ({packages.Count})" : $"Search: {_searchQuery} ({packages.Count})";
        DrawSectionHeader(buffer, ix, midY, leftW, leftTitle, true, colorScheme);

        if (rightW > 0)
        {
            DrawSectionHeader(buffer, rightX, midY, rightW, "Output", false, colorScheme);
        }
    }

    private void DrawHeader(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (!string.IsNullOrEmpty(_message))
        {
            WriteLine(buffer, x, y, w, _message, colorScheme.Error, colorScheme.Background);
            return;
        }

        var activity = _busy ? "  (working…)" : _loading ? "  (loading…)" : string.Empty;
        WriteLine(buffer, x, y, w, $"{_packageManagerService.DisplayName}{activity}", Color.Cyan, colorScheme.Background);
    }

    internal string KeyHints => _mode == Mode.Installed
        ? "Space/Enter:uninstall  s:search  r:refresh  x:cancel  PgUp/PgDn:output  Esc:close"
        : "Space/Enter:install  i:installed  s:new search  r:refresh  x:cancel  PgUp/PgDn:output  Esc:close";

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_confirmAction is not null)
        {
            WriteLine(buffer, x, y, w, _confirmPrompt, Color.Red, colorScheme.Background);
            return;
        }

        DrawStatusAndHints(buffer, x, y, w, _status, _statusSeverity switch
        {
            StatusSeverity.Error => colorScheme.Error,
            StatusSeverity.Success => Color.Green,
            _ => colorScheme.Foreground,
        }, KeyHints, colorScheme);
    }

    #endregion
}
