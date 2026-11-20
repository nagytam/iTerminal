using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Prompts;
using iConsole.Services;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;
using System.Collections.Concurrent;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// 7-SMB: the network neighbourhood for SMB. The hosts are browsed anonymously: the list starts with the
/// hosts that answered on port 445 during a scan, the saved SMB connections and the hosts with a login
/// kept in the password safe. Enter on a host lists its shares, Enter on a share goes to
/// <c>smb://[user@]host/share/</c> and closes the selector, and Backspace goes back to the hosts.
///
/// A host that lists no shares anonymously can be logged in to with L: the username and password are asked
/// for, the host's shares are listed with them, and the login is kept in the password safe so the shares are
/// listed again at every scan and opening one as that user finds the password. A saved connection (the
/// Network Browser, Ctrl+N) still works as before.
///
/// The scan and the logins run on background tasks and park their results; <see cref="OnIdle"/> applies
/// them, so the list is only ever rebuilt on the UI thread.
/// </summary>
public class FolderSelectorSmbTabPage : FolderSelectorTabPage
{
    /// <summary>One row of the page: a host (<see cref="Share"/> null) or a share on a host.</summary>
    internal sealed record SmbEntry(string Host, string? Share, string? Username, string Label);

    /// <summary>The shares a host lists for one login: a login kept in the password safe, listed at each scan, or one typed on this page.</summary>
    internal sealed record LoginShares(string Host, string Username, IReadOnlyList<string> Shares, int SmbVersion = 0);

    /// <summary>A login listing that has finished on a background task. <see cref="OnIdle"/> applies it on the UI thread.</summary>
    private sealed record LoginOutcome(int Generation, string Host, string Username, string Password, IReadOnlyList<string>? Shares, bool Typed, int SmbVersion = 0);

    /// <summary>The scan is a sweep of the local /24s; it is stopped after this even if it has not finished.</summary>
    internal static readonly TimeSpan ScanLimit = TimeSpan.FromSeconds(30);

    private readonly INetworkDiscoveryService _networkDiscoveryService;
    private readonly INetworkProfileService _networkProfileService;
    private readonly IContentModelService _contentModelService;
    private readonly IPasswordManager _passwordManager;
    private readonly IPromptService? _promptService;

    private readonly object _foundLock = new();
    private readonly List<DiscoveredHost> _found = [];
    private volatile bool _dirty;
    private volatile bool _scanning;
    private string? _scanError;
    private CancellationTokenSource? _scanCts;

    /// <summary>Bumped by each rescan, so a listing that finishes after it is dropped rather than applied.</summary>
    private int _generation;
    private readonly ConcurrentQueue<LoginOutcome> _outcomes = new();
    /// <summary>The logins whose shares the list shows. Only the UI thread touches it.</summary>
    private readonly List<LoginShares> _logins = [];
    /// <summary>1 while a login is being asked for or listed, so a second L does not open a second prompt.</summary>
    private int _loginBusy;

    private readonly ConcurrentObservableCollection<SmbEntry> _items = new();
    private readonly ListBox<SmbEntry> _listBox;
    private readonly Label _statusLabel;
    private readonly Label _signedInLabel;
    private readonly Button _selectButton;
    private readonly Button _loginButton;
    private readonly Button _logoutButton;

    /// <summary>The host whose logins are waiting for a y or n before they are removed, or null.</summary>
    private string? _pendingLogoutHost;

    /// <summary>The host whose shares are listed, or null while the list shows hosts.</summary>
    private string? _hostFilter;
    private string _status = string.Empty;

    private readonly ICommand _selectCommand;
    protected override ICommand? SelectCommand => _selectCommand;
    protected override string SelectDescription => UiText.Get("FolderSelector.Select.Smb", "Open the host under the cursor, or go to the share under the cursor.");

    public FolderSelectorSmbTabPage(
        INetworkDiscoveryService networkDiscoveryService,
        INetworkProfileService networkProfileService,
        IContentModelService contentModelService,
        IPasswordManager passwordManager,
        IPromptService? promptService,
        IThemeService themeService)
        : base(UiText.Get("FolderSelector.Tab7", "7-SMB"), ConsoleKey.D7, themeService)
    {
        _networkDiscoveryService = networkDiscoveryService;
        _networkProfileService = networkProfileService;
        _contentModelService = contentModelService;
        _passwordManager = passwordManager;
        _promptService = promptService;

        _listBox = new ListBox<SmbEntry>(
            new ListBoxModel<SmbEntry>(_items) { TransformItem = TransformEntry },
            themeService)
        {
            EmptyText = UiText.Get("FolderSelector.Smb.Looking", " Looking for SMB hosts on the local network…"),
        };
        _statusLabel = new Label(string.Empty, themeService);
        _signedInLabel = new Label(string.Empty, themeService);
        _selectCommand = new SelectSmbCommand(_contentModelService);
        _selectButton = new Button(UiText.Get("Button.SelectFolder", "Select folder"), _selectCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        _loginButton = new Button(UiText.Get("FolderSelector.Smb.Login", "Log in…"), new SmbLoginCommand(), themeService)
        {
            HotKeyChar = 'L'
        };
        _logoutButton = new Button(UiText.Get("FolderSelector.Smb.Logout", "Log out…"), new SmbLogoutCommand(), themeService)
        {
            HotKeyChar = 'O'
        };

        AddControl(_listBox);
        AddControl(_signedInLabel);
        AddControl(_statusLabel);
        AddControl(_selectButton);
        AddControl(_loginButton);
        AddControl(_logoutButton);
    }

    private sealed class SmbLogoutCommand : Command
    {
        public override string Title => "Log out of an SMB host";

        public override Task ExecuteAsync(object? arg)
        {
            if (Control?.Parent is FolderSelectorSmbTabPage page)
            {
                page.BeginLogout();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class SmbLoginCommand : Command
    {
        public override string Title => "Log in to an SMB host";

        public override Task ExecuteAsync(object? arg)
        {
            if (Control?.Parent is FolderSelectorSmbTabPage page)
            {
                page.StartLogin();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class SelectSmbCommand(IContentModelService contentModelService) : Command
    {
        public override Key? Key { get; init; } = new Key(ConsoleKey.Enter, false, false, false);
        public override string Title => "Select SMB host or share";

        public override async Task ExecuteAsync(object? arg)
        {
            if (Control?.Parent is not FolderSelectorSmbTabPage page)
            {
                return;
            }

            var url = page.ActivateSelected();
            if (url is null)
            {
                return;
            }

            await contentModelService.SetUrlAsync(new Url(url));
            CloseContainingDialog();
        }
    }

    private static ListItem<SmbEntry> TransformEntry(SmbEntry entry, CancellationToken cancellationToken) =>
        new(entry) { Value = entry.Label, IsEnabled = true };

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        Rescan();
    }

    public override void OnInitializeLayout()
    {
        _listBox.Position = new Position(1, 0);
        _listBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 4));
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        // Rows from the bottom: buttons, status, who is signed in.
        _signedInLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 3));
        _signedInLabel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        _statusLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        // The three buttons share the bottom row: Select, Log in, Log out.
        var buttonsWidth = Math.Max(0, ViewSize.Width - 2);
        var buttonWidth = Math.Max(0, (buttonsWidth - 2) / 3);
        var row = Math.Max(0, ViewSize.Height - 1);
        _selectButton.Position = new Position(1, row);
        _selectButton.ViewSize = new Size(buttonWidth, 1);
        _loginButton.Position = new Position(1 + buttonWidth + 1, row);
        _loginButton.ViewSize = new Size(buttonWidth, 1);
        _logoutButton.Position = new Position(1 + 2 * (buttonWidth + 1), row);
        _logoutButton.ViewSize = new Size(Math.Max(0, buttonsWidth - 2 * (buttonWidth + 1)), 1);

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        var changed = false;
        while (_outcomes.TryDequeue(out var outcome))
        {
            ApplyOutcome(outcome);
            changed = true;
        }
        if (_dirty || changed)
        {
            _dirty = false;
            ApplyRows();
        }
        // After the rows, so the host under the cursor is the one in the list.
        RefreshSignedIn();
    }

    /// <summary>The line above the status: who is logged in to the host under the cursor, or that it is browsed anonymously.</summary>
    private void RefreshSignedIn()
    {
        var host = SelectedEntry?.Host ?? _hostFilter;
        var text = host is null
            ? string.Empty
            : LoginsFor(host) is { Count: > 0 } logins
                ? string.Format(UiText.Get("FolderSelector.Smb.SignedIn", "Logged in to {0} as {1}."), host, UsersOf(logins))
                : string.Format(UiText.Get("FolderSelector.Smb.Anonymous", "Browsing {0} anonymously. L logs in."), host);
        if (host is not null && SmbVersionOf(host) is > 0 and var version)
        {
            text += "  " + string.Format(UiText.Get("FolderSelector.Smb.Version", "SMB {0} in use."), version);
        }
        if (_signedInLabel.Text != text)
        {
            _signedInLabel.Text = text;
            StateHasChanged();
        }
    }

    /// <summary>The SMB version the host was listed with: a login's when there is one, else the anonymous listing's. 0 when neither listed it.</summary>
    private int SmbVersionOf(string host)
    {
        var fromLogin = LoginsFor(host).Select(l => l.SmbVersion).FirstOrDefault(v => v > 0);
        if (fromLogin > 0)
        {
            return fromLogin;
        }
        lock (_foundLock)
        {
            return _found.FirstOrDefault(h => h.SmbAvailable && string.Equals(h.IPAddress.ToString(), host, StringComparison.OrdinalIgnoreCase))?.SmbVersion ?? 0;
        }
    }

    private List<LoginShares> LoginsFor(string host) =>
        _logins.Where(l => string.Equals(l.Host, host, StringComparison.OrdinalIgnoreCase)).ToList();

    private static string UsersOf(IEnumerable<LoginShares> logins) => string.Join(", ", logins.Select(l => l.Username));

    #region Scan

    private void Rescan()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource(ScanLimit);
        _scanCts = cts;
        _generation++;
        var generation = _generation;
        lock (_foundLock)
        {
            _found.Clear();
        }
        _logins.Clear();
        _scanning = true;
        _scanError = null;
        _dirty = true;

        // The saved logins are read here, on the UI thread, and each one lists its host's shares in the background.
        foreach (var (host, username, password) in SavedLogins())
        {
            _ = Task.Run(async () =>
            {
                SmbListing? listing = null;
                try
                {
                    listing = await _networkDiscoveryService.ListSharesWithLoginAsync(host, username, password, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // Left as no listing: the status line says the saved login did not list the host.
                }
                _outcomes.Enqueue(new LoginOutcome(generation, host, username, string.Empty, listing?.Shares, Typed: false, listing?.SmbVersion ?? 0));
                _dirty = true;
            }, cts.Token);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var host in _networkDiscoveryService.ScanAsync(cts.Token))
                {
                    if (host.SmbAvailable)
                    {
                        lock (_foundLock)
                        {
                            _found.Add(host);
                        }
                        _dirty = true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _scanError = exception.Message;
            }
            finally
            {
                if (ReferenceEquals(_scanCts, cts))
                {
                    _scanning = false;
                }
                _dirty = true;
            }
        }, cts.Token);
    }

    /// <summary>The logins in the password safe that name a host and user, read in full on the calling thread.</summary>
    private List<(string Host, string Username, string Password)> SavedLogins()
    {
        var saved = new List<(string Host, string Username, string Password)>();
        foreach (var entry in _passwordManager.List())
        {
            if (SmbLoginKey.TryParse(entry.Id, out var host, out var username))
            {
                saved.Add((host, username, entry.Password));
            }
        }
        return saved;
    }

    private void ApplyOutcome(LoginOutcome outcome)
    {
        if (outcome.Typed)
        {
            Interlocked.Exchange(ref _loginBusy, 0);
        }
        else if (outcome.Generation != _generation)
        {
            return;
        }

        if (outcome.Shares is null)
        {
            _status = outcome.Typed
                ? string.Format(UiText.Get("FolderSelector.Smb.LoginFailed", "Could not log in to {0} as {1}. Nothing was saved."), outcome.Host, outcome.Username)
                : string.Format(UiText.Get("FolderSelector.Smb.SavedLoginFailed", "Could not list the shares of {0} with the saved login of {1}."), outcome.Host, outcome.Username);
            return;
        }

        _logins.RemoveAll(login => string.Equals(login.Host, outcome.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(login.Username, outcome.Username, StringComparison.OrdinalIgnoreCase));
        _logins.Add(new LoginShares(outcome.Host, outcome.Username, outcome.Shares, outcome.SmbVersion));
        if (!outcome.Typed)
        {
            return;
        }

        var saved = SaveLogin(outcome.Host, outcome.Username, outcome.Password);
        _status = string.Format(
            saved
                ? UiText.Get("FolderSelector.Smb.LoggedIn", "Logged in to {0} as {1}. The login is saved in the password safe.")
                : UiText.Get("FolderSelector.Smb.LoggedInUnsaved", "Logged in to {0} as {1}. The password safe could not be opened, so the login was not saved."),
            outcome.Host, outcome.Username);

        // The shares are what the login was for, so the list opens on them.
        _hostFilter = outcome.Host;
        _listBox.ScrollableModel.CursorPosition.Y = 0;
    }

    /// <summary>Keeps the password in the password safe under the host and user. False when the safe cannot be opened.</summary>
    private bool SaveLogin(string host, string username, string password)
    {
        try
        {
            if (!_passwordManager.IsUnlocked && !_passwordManager.Unlock())
            {
                return false;
            }
            _passwordManager.Save(new PasswordEntry
            {
                Id = SmbLoginKey.Id(host, username),
                Title = $"SMB {username}@{host}",
                Username = username,
                Category = PasswordCategory.Network,
                Password = password,
            });
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void ApplyRows()
    {
        List<DiscoveredHost> found;
        lock (_foundLock)
        {
            found = [.. _found];
        }
        var profiles = _networkProfileService.GetAll().ToList();

        var rows = _hostFilter is null
            ? SmbBrowserRows.Hosts(found, profiles, _logins)
            : SmbBrowserRows.Shares(_hostFilter, found, profiles, _logins);
        _items.ReplaceAll(rows);
        // "Looking for SMB hosts" is true only while the scan runs; once it has finished with nothing, the
        // status line says how many were found and the list stays empty rather than still claiming a search.
        _listBox.EmptyText = _scanning && _hostFilter is null
            ? UiText.Get("FolderSelector.Smb.Looking", " Looking for SMB hosts on the local network…")
            : string.Empty;
        _listBox.UpdateModel();
        RefreshStatus(rows.Count);
        StateHasChanged();
    }

    private void RefreshStatus(int rowCount)
    {
        var band = _hostFilter is null
            ? string.Format(UiText.Get("FolderSelector.Smb.Hosts", "SMB hosts ({0})"), rowCount)
            : string.Format(UiText.Get("FolderSelector.Smb.Shares", "Shares on {0} ({1}) — Backspace: hosts"), _hostFilter, rowCount);
        var progress = _scanning && _hostFilter is null
            ? UiText.Get("FolderSelector.Smb.Scanning", "scanning…")
            : null;
        var parts = new List<string> { band };
        if (progress is not null)
        {
            parts.Add(progress);
        }
        if (_scanError is not null)
        {
            parts.Add(string.Format(UiText.Get("Net.ScanError", "Scan error: {0}"), _scanError));
        }
        if (!string.IsNullOrEmpty(_status))
        {
            parts.Add(_status);
        }
        _statusLabel.Text = string.Join("  ", parts);
    }

    #endregion

    #region Navigation

    private SmbEntry? SelectedEntry
    {
        get
        {
            var items = _listBox.ListModel.ListItems;
            var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < items.Count ? items[index].Item : null;
        }
    }

    /// <summary>
    /// What Enter does on the page. A host opens its shares (or says why it has none); a share returns the
    /// <c>smb://</c> address to go to, and the command navigates there and closes the selector.
    /// </summary>
    internal string? ActivateSelected()
    {
        var entry = SelectedEntry;
        if (entry is null)
        {
            return null;
        }

        if (entry.Share is not null)
        {
            _status = string.Empty;
            return SmbBrowserRows.UrlFor(entry);
        }

        var hasShares = SmbBrowserRows.Shares(entry.Host, SnapshotFound(), _networkProfileService.GetAll().ToList(), _logins).Count > 0;
        if (!hasShares)
        {
            _status = string.Format(UiText.Get("FolderSelector.Smb.NoShares", "No shares are listed on {0} without a login. Press L to log in."), entry.Host);
            RefreshStatus(_items.Count);
            StateHasChanged();
            return null;
        }

        _hostFilter = entry.Host;
        _status = string.Empty;
        ApplyRows();
        _listBox.ScrollableModel.CursorPosition.Y = 0;
        return null;
    }

    private void BackToHosts()
    {
        _hostFilter = null;
        _status = string.Empty;
        ApplyRows();
        _listBox.ScrollableModel.CursorPosition.Y = 0;
    }

    private List<DiscoveredHost> SnapshotFound()
    {
        lock (_foundLock)
        {
            return [.. _found];
        }
    }

    #endregion

    #region Login

    /// <summary>
    /// L: asks for a username and a password for the host under the cursor and lists its shares with them.
    /// The prompts are awaited here, so this is started with <see cref="StartLogin"/> rather than from a key
    /// handler that must return at once.
    /// </summary>
    internal async Task LoginAsync()
    {
        if (Interlocked.CompareExchange(ref _loginBusy, 1, 0) != 0)
        {
            return;
        }

        var host = SelectedEntry?.Host ?? _hostFilter;
        if (host is null)
        {
            Interlocked.Exchange(ref _loginBusy, 0);
            _status = UiText.Get("FolderSelector.Smb.LoginSelectHost", "Select a host to log in to first.");
            _dirty = true;
            return;
        }
        if (_promptService is null)
        {
            Interlocked.Exchange(ref _loginBusy, 0);
            return;
        }

        // The prompts are answered on the UI thread; nothing below touches the control tree after an await.
        var username = await _promptService.AskAsync(UsernamePrompt(host));
        if (string.IsNullOrWhiteSpace(username))
        {
            Interlocked.Exchange(ref _loginBusy, 0);
            return;
        }
        var password = await _promptService.AskAsync(PasswordPrompt(username, host));
        if (password is null)
        {
            Interlocked.Exchange(ref _loginBusy, 0);
            return;
        }

        username = username.Trim();
        var generation = _generation;
        _status = string.Format(UiText.Get("FolderSelector.Smb.LoggingIn", "Logging in to {0} as {1}…"), host, username);
        _dirty = true;

        SmbListing? listing = null;
        try
        {
            listing = await _networkDiscoveryService.ListSharesWithLoginAsync(host, username, password);
        }
        catch (Exception)
        {
            // Reported as a failed login below.
        }
        _outcomes.Enqueue(new LoginOutcome(generation, host, username, password, listing?.Shares, Typed: true, listing?.SmbVersion ?? 0));
        _dirty = true;
    }

    /// <summary>Starts <see cref="LoginAsync"/> without waiting for its prompts, for the key and the button.</summary>
    internal void StartLogin() => _ = LoginAsync();

    /// <summary>
    /// O: asks before removing every login to the host under the cursor, and the saved passwords for them. Nothing
    /// is removed until y; the list then falls back to the shares the host lists anonymously.
    /// </summary>
    internal void BeginLogout()
    {
        var host = SelectedEntry?.Host ?? _hostFilter;
        if (host is null)
        {
            _status = UiText.Get("FolderSelector.Smb.LogoutSelectHost", "Select a host to log out of first.");
        }
        else if (LoginsFor(host) is not { Count: > 0 } logins)
        {
            _status = string.Format(UiText.Get("FolderSelector.Smb.LogoutNotHere", "Not logged in to {0}."), host);
        }
        else
        {
            _pendingLogoutHost = host;
            _status = string.Format(UiText.Get("FolderSelector.Smb.LogoutConfirm", "Log out of {0} and remove the saved password for {1}?  y:yes  n:no"), host, UsersOf(logins));
        }
        _dirty = true;
    }

    /// <summary>Answers the logout question: y removes the logins, anything else leaves them.</summary>
    private void AnswerLogout(bool yes)
    {
        var host = _pendingLogoutHost;
        _pendingLogoutHost = null;
        _status = string.Empty;
        _dirty = true;
        if (host is null || !yes)
        {
            return;
        }

        try
        {
            if (!_passwordManager.IsUnlocked && !_passwordManager.Unlock())
            {
                _status = UiText.Get("FolderSelector.Smb.LogoutUnsafe", "The password safe could not be opened, so the login was not removed.");
                return;
            }
            foreach (var login in LoginsFor(host))
            {
                _passwordManager.Delete(SmbLoginKey.Id(login.Host, login.Username));
                _logins.Remove(login);
            }
        }
        catch (InvalidOperationException)
        {
            _status = UiText.Get("FolderSelector.Smb.LogoutUnsafe", "The password safe could not be opened, so the login was not removed.");
            return;
        }

        _status = string.Format(UiText.Get("FolderSelector.Smb.LoggedOut", "Logged out of {0}; the saved password is removed."), host);
        // A host that lists nothing without the login has no shares left to show, so the list goes back to the hosts.
        if (_hostFilter is not null && SmbBrowserRows.Shares(host, SnapshotFound(), _networkProfileService.GetAll().ToList(), _logins).Count == 0)
        {
            _hostFilter = null;
            _listBox.ScrollableModel.CursorPosition.Y = 0;
        }
    }

    private static TextPrompt<string> UsernamePrompt(string host)
    {
        TextPrompt<string> prompt = new(string.Format(UiText.Get("FolderSelector.Smb.LoginUser", "Username for {0}"), host));
        prompt.Validate(value => value.Any(char.IsControl)
            ? UiText.Get("FolderSelector.Smb.LoginInvalid", "Control characters are not allowed.")
            : null);
        return prompt;
    }

    private static TextPrompt<string> PasswordPrompt(string username, string host) =>
        new(string.Format(UiText.Get("FolderSelector.Smb.LoginPassword", "Password for {0} on {1}"), username.Trim(), host)) { IsSecret = true };

    #endregion

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt;
        if (_pendingLogoutHost is not null && plain)
        {
            // Only y removes the logins; n leaves them. Any other key also leaves them, and then does what it does.
            var answer = char.ToLowerInvariant(keyEvent.KeyChar);
            if (answer is 'y' or 'n')
            {
                AnswerLogout(answer == 'y');
                return true;
            }
            AnswerLogout(false);
        }
        if (plain && keyEvent.ConsoleKey == ConsoleKey.Backspace && _hostFilter is not null)
        {
            BackToHosts();
            return true;
        }
        if (plain && keyEvent.ConsoleKey != ConsoleKey.Enter && char.ToLowerInvariant(keyEvent.KeyChar) == 'r' && GetFocusedControl() is not Button)
        {
            _status = string.Empty;
            Rescan();
            return true;
        }
        if (plain && keyEvent.ConsoleKey != ConsoleKey.Enter && char.ToLowerInvariant(keyEvent.KeyChar) == 'l' && GetFocusedControl() is not Button)
        {
            StartLogin();
            return true;
        }
        if (plain && keyEvent.ConsoleKey != ConsoleKey.Enter && char.ToLowerInvariant(keyEvent.KeyChar) == 'o' && GetFocusedControl() is not Button)
        {
            BeginLogout();
            return true;
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }
        yield return ("Backspace", UiText.Get("FolderSelector.Hint.SmbBack", "Back from a host's shares to the list of hosts."));
        yield return ("R", UiText.Get("FolderSelector.Hint.SmbRescan", "Search the network for SMB hosts again."));
        yield return ("L", UiText.Get("FolderSelector.Hint.SmbLogin", "Log in to the host under the cursor with a username and password; the login is saved in the password safe."));
        yield return ("O", UiText.Get("FolderSelector.Hint.SmbLogout", "Log out of the host under the cursor and remove its saved password (y confirms)."));
    }

    // Test seams.
    internal IReadOnlyList<SmbEntry> RowsForTest => [.. _items.AsSnapshot()];
    internal string? HostFilterForTest => _hostFilter;
    internal string StatusForTest => _statusLabel.Text;
    internal void SelectRowForTest(int index) => _listBox.ScrollableModel.CursorPosition.Y = index;
    internal void ApplyFoundForTest(IEnumerable<DiscoveredHost> hosts)
    {
        lock (_foundLock)
        {
            _found.Clear();
            _found.AddRange(hosts);
        }
        _dirty = true;
        OnIdle();
    }
    internal Task PressSelectForTestAsync() => _selectCommand.RunAsync(null);
    internal Task LoginForTestAsync() => LoginAsync();
    internal string SignedInForTest => _signedInLabel.Text;
    internal string? PendingLogoutHostForTest => _pendingLogoutHost;
    internal IReadOnlyList<LoginShares> LoginsForTest => _logins;
}

/// <summary>
/// The rows the 7-SMB page lists, built from a scan and the saved connections. Kept apart from the page
/// so the rules (which hosts appear, how a saved login overrides a discovered share, the address a share
/// goes to) are tested without a screen or a network.
/// </summary>
internal static class SmbBrowserRows
{
    public static List<FolderSelectorSmbTabPage.SmbEntry> Hosts(
        IEnumerable<DiscoveredHost> found,
        IEnumerable<NetworkConnectionProfile> profiles,
        IEnumerable<FolderSelectorSmbTabPage.LoginShares>? logins = null)
    {
        var savedProfiles = profiles.Where(p => p.Protocol == NetworkProtocol.Smb && !string.IsNullOrWhiteSpace(p.Host)).ToList();
        var loginList = (logins ?? []).ToList();
        var rows = new List<FolderSelectorSmbTabPage.SmbEntry>();
        var listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var host in found.Where(h => h.SmbAvailable))
        {
            var address = host.IPAddress.ToString();
            if (!listed.Add(address))
            {
                continue;
            }
            var saved = savedProfiles.Any(p => string.Equals(p.Host, address, StringComparison.OrdinalIgnoreCase));
            var login = LoginFor(address, loginList);
            var thisMachine = NetworkDiscoveryService.IsLocalAddress(host.IPAddress)
                ? ", " + UiText.Get("FolderSelector.Smb.ThisMachine", "this machine")
                : string.Empty;
            var label = saved
                ? $"{address} (SMB{thisMachine}, " + UiText.Get("FolderSelector.Smb.Saved", "saved") + ")"
                : login is not null
                    ? $"{address} (SMB{thisMachine}, {LoggedIn(login.Username)})"
                    : $"{address} (SMB{thisMachine})";
            rows.Add(new(address, null, null, label));
        }

        foreach (var login in loginList)
        {
            if (listed.Add(login.Host))
            {
                rows.Add(new(login.Host, null, null, $"{login.Host} (SMB, {LoggedIn(login.Username)})"));
            }
        }

        foreach (var profile in savedProfiles)
        {
            if (!listed.Add(profile.Host))
            {
                continue;
            }
            rows.Add(new(profile.Host, null, null, $"{profile.Host} (SMB, " + UiText.Get("FolderSelector.Smb.Saved", "saved") + ")"));
        }

        return rows;
    }

    public static List<FolderSelectorSmbTabPage.SmbEntry> Shares(
        string host,
        IEnumerable<DiscoveredHost> found,
        IEnumerable<NetworkConnectionProfile> profiles,
        IEnumerable<FolderSelectorSmbTabPage.LoginShares>? logins = null)
    {
        var rows = new List<FolderSelectorSmbTabPage.SmbEntry>();

        foreach (var discovered in found.Where(h => h.SmbAvailable && string.Equals(h.IPAddress.ToString(), host, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var share in discovered.Shares)
            {
                if (rows.Any(r => string.Equals(r.Share, share, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                rows.Add(new(host, share, null, $"{host}\\{share} (SMB)"));
            }
        }

        // A login lists shares an anonymous browse may not, and it is the one the share opens with, so it replaces a row of the same name.
        foreach (var login in (logins ?? []).Where(l => string.Equals(l.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var share in login.Shares)
            {
                Put(rows, new(host, share, login.Username, $"{host}\\{share} (SMB, {LoggedIn(login.Username)})"));
            }
        }

        foreach (var profile in profiles.Where(p => p.Protocol == NetworkProtocol.Smb && string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(profile.ShareName))
            {
                continue;
            }
            // A saved connection carries the login a discovered share lacks, so it replaces that row.
            var username = string.IsNullOrEmpty(profile.Username) ? null : profile.Username;
            Put(rows, new(host, profile.ShareName, username, $"{host}\\{profile.ShareName} (SMB, " + UiText.Get("FolderSelector.Smb.Saved", "saved") + ")"));
        }

        return rows;
    }

    /// <summary>The address a share goes to: <c>smb://[user@]host/share/</c>, the same form the Network Browser opens.</summary>
    public static string UrlFor(FolderSelectorSmbTabPage.SmbEntry entry)
    {
        var userPart = string.IsNullOrEmpty(entry.Username) ? string.Empty : entry.Username + "@";
        return Container.SMB + userPart + entry.Host + "/" + entry.Share + "/";
    }

    private static void Put(List<FolderSelectorSmbTabPage.SmbEntry> rows, FolderSelectorSmbTabPage.SmbEntry entry)
    {
        var existing = rows.FindIndex(r => string.Equals(r.Share, entry.Share, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            rows[existing] = entry;
        }
        else
        {
            rows.Add(entry);
        }
    }

    private static FolderSelectorSmbTabPage.LoginShares? LoginFor(string host, List<FolderSelectorSmbTabPage.LoginShares> logins) =>
        logins.FirstOrDefault(l => string.Equals(l.Host, host, StringComparison.OrdinalIgnoreCase));

    private static string LoggedIn(string username) =>
        string.Format(UiText.Get("FolderSelector.Smb.LoggedInAs", "logged in as {0}"), username);
}
