using iConsole.Localization;
using iTerminal.Core.Model.Passwords;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Network Browser: lists SSH/SMB hosts discovered on the local /24 subnet(s) alongside saved
/// connection profiles, and lets the user connect to either (prompting for credentials, with an
/// option to save the profile + credential for next time). Discovery runs on a background task;
/// the key handler and <see cref="Draw"/> never block on it.
/// </summary>
public class NetworkBrowserDialog : SplitPaneDialog
{
    internal enum HostRowKind { SavedProfile, DiscoveredSsh, DiscoveredSmbShare, DiscoveredSmbHost }

    internal sealed record HostRow(
        string DisplayName,
        HostRowKind Kind,
        string Host,
        string? ShareName,
        NetworkConnectionProfile? Profile);

    internal enum Mode { Browse, Connect }

    private sealed class HostListBox(IListBoxModel<HostRow> listModel, IThemeService themeService)
        : ListBox<HostRow>(listModel, themeService)
    {
        protected override Color? GetItemForegroundOverride(int index)
        {
            if (index < 0 || index >= ListModel.ListItems.Count)
            {
                return null;
            }
            return ListModel.ListItems[index].Item.Kind == HostRowKind.SavedProfile ? Color.Green : null;
        }
    }

    private readonly INetworkDiscoveryService _networkDiscoveryService;
    private readonly INetworkProfileService _networkProfileService;
    private readonly IPasswordManager _passwordManager;
    private readonly IContentModelService _contentModelService;
    private readonly IBeepService _beepService;
    private readonly IDateTimeProviderService _dateTimeProviderService;

    private readonly ConcurrentObservableCollection<HostRow> _hostItems = new();
    private readonly HostListBox _hostListBox;

    private readonly TextBox _usernameTextBox;
    private readonly TextBox _passwordTextBox;
    private readonly TextBox _shareTextBox;
    private readonly CheckBox _saveConnectionCheckBox;
    private readonly Button _connectButton;
    private readonly Button _backButton;

    private Mode _mode = Mode.Browse;
    private HostRow? _pendingRow;
    private bool _scanning;
    private string _status = string.Empty;
    private bool _statusIsError;
    private CancellationTokenSource? _scanCts;

    /// <summary>The saved profile a 'd' asked to delete, until y or n answers.</summary>
    private HostRow? _pendingDelete;

    public NetworkBrowserDialog(
        INetworkDiscoveryService networkDiscoveryService,
        INetworkProfileService networkProfileService,
        IPasswordManager passwordManager,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(NetworkBrowserDialog), themeService)
    {
        _networkDiscoveryService = networkDiscoveryService;
        _networkProfileService = networkProfileService;
        _passwordManager = passwordManager;
        _contentModelService = contentModelService;
        _beepService = beepService;
        _dateTimeProviderService = dateTimeProviderService;
        Title = iConsole.Localization.UiText.Get("Dialog.NetworkBrowser", "Network Browser");

        _hostListBox = new HostListBox(
            new ListBoxModel<HostRow>(_hostItems) { TransformItem = TransformHostRow },
            themeService);
        AddControl(_hostListBox);

        _usernameTextBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.Username", "Username"), IsVisible = false, CanFocus = false };
        _passwordTextBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.PasswordOrKey", "Password (or SSH key path if left blank)"), IsVisible = false, CanFocus = false };
        _shareTextBox = new TextBox(beepService, dateTimeProviderService, themeService) { Label = UiText.Get("Field.ShareName", "Share name"), IsVisible = false, CanFocus = false };
        _saveConnectionCheckBox = new CheckBox(themeService) { Label = UiText.Get("Field.SaveConnection", "Save this connection"), IsVisible = false, CanFocus = false };
        _connectButton = new Button(iConsole.Localization.UiText.Get("Button.Connect", "Connect"), new NetworkBrowserActionCommand(() => _ = ConnectPendingAsync()), themeService) { HotKeyChar = 'C', IsVisible = false, CanFocus = false };
        _backButton = new Button(iConsole.Localization.UiText.Get("Common.Back", "Back"), new NetworkBrowserActionCommand(BeginBrowse), themeService) { HotKeyChar = 'B', IsVisible = false, CanFocus = false };
        AddControl(_usernameTextBox);
        AddControl(_passwordTextBox);
        AddControl(_shareTextBox);
        AddControl(_saveConnectionCheckBox);
        AddControl(_connectButton);
        AddControl(_backButton);
    }

    private sealed class NetworkBrowserActionCommand(Action action) : iConsole.Commands.Command
    {
        public override Task ExecuteAsync(object? args)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private static ListItem<HostRow> TransformHostRow(HostRow row, CancellationToken cancellationToken) =>
        new(row) { Value = row.DisplayName, IsEnabled = true };

    public override async Task ShowAsync()
    {
        await base.ShowAsync();

        // Control.ShowAsync runs OnInitializeAsync (which enters browse mode and hides the connect
        // form) and only *then* makes every child visible — so the username, password and share
        // boxes and the Connect/Back buttons were all on screen over the host list from the moment
        // the dialog opened. Re-entering browse mode puts them back where they belong.
        if (_mode == Mode.Browse)
        {
            BeginBrowse();
        }
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        BeginBrowse();
        Rescan();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(Parent.ViewSize * 0.9);
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw > 0 && ih > 0)
        {
            var midY = 2;
            var midH = Math.Max(0, ih - 2);

            _hostListBox.Position = new Position(1, midY + 1);
            _hostListBox.ViewSize = new Size(iw, midH - 1);

            var fieldWidth = Math.Max(1, iw - 4);
            var y = midY + 1;
            _usernameTextBox.Position = new Position(2, y);
            _usernameTextBox.ViewSize = new Size(fieldWidth, 1);
            _usernameTextBox.ScrollableModel.ViewSize = _usernameTextBox.ViewSize;
            y += 2;
            _passwordTextBox.Position = new Position(2, y);
            _passwordTextBox.ViewSize = new Size(fieldWidth, 1);
            _passwordTextBox.ScrollableModel.ViewSize = _passwordTextBox.ViewSize;
            y += 2;
            _shareTextBox.Position = new Position(2, y);
            _shareTextBox.ViewSize = new Size(fieldWidth, 1);
            _shareTextBox.ScrollableModel.ViewSize = _shareTextBox.ViewSize;
            y += 2;
            _saveConnectionCheckBox.Position = new Position(2, y);
            _saveConnectionCheckBox.ViewSize = new Size(fieldWidth, 1);
            y += 2;
            _connectButton.Position = new Position(2, y);
            _connectButton.ViewSize = new Size(12, 1);
            _backButton.Position = new Position(16, y);
            _backButton.ViewSize = new Size(12, 1);
        }

        base.OnInitializeLayout();
    }

    #region Discovery

    private void Rescan()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        _scanning = true;
        _status = "Scanning local network...";
        _statusIsError = false;
        _hostListBox.EmptyText = UiText.Get("Empty.HostsLooking", UiText.Get("Empty.HostsLooking", " Looking for hosts on the local network…"));

        var savedRows = BuildSavedProfileRows();
        _hostItems.ReplaceAll(savedRows);
        _hostListBox.UpdateModel();
        StateHasChanged();

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var host in _networkDiscoveryService.ScanAsync(cts.Token))
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }
                    foreach (var row in BuildDiscoveredRows(host))
                    {
                        _hostItems.Add(row);
                    }
                    _hostListBox.UpdateModel();
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                _status = $"Scan error: {exception.Message}";
                _statusIsError = true;
            }
            finally
            {
                if (!cts.IsCancellationRequested)
                {
                    _scanning = false;
                    _hostListBox.EmptyText = UiText.Get("Empty.HostsNone", UiText.Get("Empty.HostsNone", " No hosts found — r scans again."));

                    // Only the progress note is cleared: a scan error was shown and then wiped by
                    // this same line a moment later, so a failed scan read as an empty network.
                    if (!_statusIsError)
                    {
                        _status = string.Empty;
                    }

                    StateHasChanged();
                }
            }
        }, cts.Token);
    }

    private List<HostRow> BuildSavedProfileRows()
    {
        return [.. _networkProfileService.GetAll().Select(profile =>
        {
            var label = profile.Protocol == NetworkProtocol.Smb
                ? $"* {profile.Username}@{profile.Host}/{profile.ShareName} (SMB, saved)"
                : $"* {profile.Username}@{profile.Host} (SSH, saved)";
            return new HostRow(label, HostRowKind.SavedProfile, profile.Host, profile.ShareName, profile);
        })];
    }

    private static List<HostRow> BuildDiscoveredRows(DiscoveredHost host)
    {
        var rows = new List<HostRow>();
        if (host.SshAvailable)
        {
            rows.Add(new HostRow($"{host.IPAddress} (SSH)", HostRowKind.DiscoveredSsh, host.IPAddress.ToString(), null, null));
        }
        if (host.SmbAvailable)
        {
            if (host.Shares.Count > 0)
            {
                foreach (var share in host.Shares)
                {
                    rows.Add(new HostRow($"{host.IPAddress}\\{share} (SMB)", HostRowKind.DiscoveredSmbShare, host.IPAddress.ToString(), share, null));
                }
            }
            else
            {
                rows.Add(new HostRow($"{host.IPAddress} (SMB, login required)", HostRowKind.DiscoveredSmbHost, host.IPAddress.ToString(), null, null));
            }
        }
        return rows;
    }

    #endregion

    #region Test seams

    internal IReadOnlyList<HostRow> HostRowsForTest => [.. _hostItems.AsSnapshot()];
    internal int ListedRowCountForTest => _hostListBox.ListModel.ListItems.Count;
    internal Mode ModeForTest => _mode;
    internal HostRow? PendingRowForTest => _pendingRow;
    internal TextBox UsernameTextBoxForTest => _usernameTextBox;
    internal TextBox PasswordTextBoxForTest => _passwordTextBox;
    internal TextBox ShareTextBoxForTest => _shareTextBox;
    internal CheckBox SaveConnectionCheckBoxForTest => _saveConnectionCheckBox;
    internal void SelectRowForTest(int index) => _hostListBox.ScrollableModel.CursorPosition.Y = index;
    internal Task SelectAsyncForTest() => SelectAsync();
    internal Task ConnectPendingAsyncForTest() => ConnectPendingAsync();

    #endregion

    #region Connect flow

    private HostRow? SelectedRow
    {
        get
        {
            var items = _hostListBox.ListModel.ListItems;
            var index = (int)_hostListBox.ScrollableModel.CursorPosition.Y;
            return index >= 0 && index < items.Count ? items[index].Item : null;
        }
    }

    private void BeginBrowse()
    {
        _mode = Mode.Browse;
        _pendingRow = null;

        _hostListBox.IsVisible = true;
        _hostListBox.CanFocus = true;
        _hostListBox.SetFocus();

        _usernameTextBox.IsVisible = false;
        _usernameTextBox.CanFocus = false;
        _passwordTextBox.IsVisible = false;
        _passwordTextBox.CanFocus = false;
        _shareTextBox.IsVisible = false;
        _shareTextBox.CanFocus = false;
        _saveConnectionCheckBox.IsVisible = false;
        _saveConnectionCheckBox.CanFocus = false;
        _connectButton.IsVisible = false;
        _connectButton.CanFocus = false;
        _backButton.IsVisible = false;
        _backButton.CanFocus = false;

        _status = string.Empty;
        _statusIsError = false;
        StateHasChanged();
    }

    private async Task SelectAsync()
    {
        var row = SelectedRow;
        if (row is null)
        {
            return;
        }

        if (row.Kind == HostRowKind.SavedProfile && row.Profile is not null)
        {
            await ConnectAsync(row.Profile, saveConnection: false);
            return;
        }

        BeginConnectForm(row);
    }

    private void BeginConnectForm(HostRow row)
    {
        _mode = Mode.Connect;
        _pendingRow = row;

        _hostListBox.CanFocus = false;

        _usernameTextBox.Text = string.Empty;
        _usernameTextBox.IsVisible = true;
        _usernameTextBox.CanFocus = true;
        _passwordTextBox.Text = string.Empty;
        _passwordTextBox.IsVisible = true;
        _passwordTextBox.CanFocus = true;

        var isSmb = row.Kind is HostRowKind.DiscoveredSmbHost or HostRowKind.DiscoveredSmbShare;
        _shareTextBox.Text = row.ShareName ?? string.Empty;
        _shareTextBox.IsVisible = isSmb;
        _shareTextBox.CanFocus = isSmb;

        _saveConnectionCheckBox.IsChecked = false;
        _saveConnectionCheckBox.IsVisible = true;
        _saveConnectionCheckBox.CanFocus = true;
        _connectButton.IsVisible = true;
        _connectButton.CanFocus = true;
        _backButton.IsVisible = true;
        _backButton.CanFocus = true;

        _usernameTextBox.SetFocus();
        _status = string.Empty;
        _statusIsError = false;
        StateHasChanged();
    }

    private async Task ConnectPendingAsync()
    {
        var row = _pendingRow;
        if (row is null)
        {
            return;
        }

        var username = _usernameTextBox.Text.Trim();
        var secret = _passwordTextBox.Text;
        var shareName = _shareTextBox.Text.Trim();
        var isSmb = row.Kind is HostRowKind.DiscoveredSmbHost or HostRowKind.DiscoveredSmbShare;

        if (isSmb && shareName.Length == 0)
        {
            _status = "A share name is required.";
            _statusIsError = true;
            StateHasChanged();
            return;
        }

        var profile = new NetworkConnectionProfile
        {
            Protocol = isSmb ? NetworkProtocol.Smb : NetworkProtocol.Ssh,
            Host = row.Host,
            Username = username,
            ShareName = isSmb ? shareName : null,
            SshAuthMethod = isSmb ? null : (string.IsNullOrEmpty(secret) ? SshAuthMethod.PrivateKey : SshAuthMethod.Password),
            PrivateKeyPath = null,
            HasSavedCredential = _saveConnectionCheckBox.IsChecked && !string.IsNullOrEmpty(secret),
        };

        await ConnectAsync(profile, saveConnection: _saveConnectionCheckBox.IsChecked, secret);
    }

    private async Task ConnectAsync(NetworkConnectionProfile profile, bool saveConnection, string? secretOverride = null)
    {
        _status = "Connecting...";
        _statusIsError = false;
        StateHasChanged();

        try
        {
            string? secret = secretOverride;
            if (secret is null && profile.HasSavedCredential)
            {
                secret = _passwordManager.GetSecret(profile.Id);
            }

            var userPart = string.IsNullOrEmpty(profile.Username) ? string.Empty : profile.Username + "@";
            var url = profile.Protocol == NetworkProtocol.Smb
                ? $"{Container.SMB}{userPart}{profile.Host}/{profile.ShareName}/"
                : $"{Container.SSH}{userPart}{profile.Host}:/";

            if (saveConnection)
            {
                if (!string.IsNullOrEmpty(secret))
                {
                    var protocol = profile.Protocol == NetworkProtocol.Smb ? "SMB" : "SSH";
                    _passwordManager.Save(new PasswordEntry
                    {
                        Id = profile.Id,
                        Title = $"{protocol} {profile.Username}@{profile.Host}" + (profile.ShareName is null ? string.Empty : $"/{profile.ShareName}"),
                        Username = profile.Username,
                        Category = PasswordCategory.Network,
                        Password = secret,
                    });
                    profile.HasSavedCredential = true;
                }
                _networkProfileService.Save(profile);
            }

            await _contentModelService.SetUrlAsync(new Url(url));
            Close();
        }
        catch (Exception exception)
        {
            _status = $"Connection failed: {exception.Message}";
            _statusIsError = true;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Asks before deleting a saved connection: 'd' used to remove the profile and its stored
    /// password on the spot, one keystroke away from Enter on the same row.
    /// </summary>
    private void BeginDeleteSelectedProfile()
    {
        var row = SelectedRow;
        if (row?.Kind != HostRowKind.SavedProfile || row.Profile is null)
        {
            return;
        }

        _pendingDelete = row;
        _status = $"Delete the saved connection {row.DisplayName} and its stored password?  y:yes  n:no";
        _statusIsError = true;
        StateHasChanged();
    }

    private void DeleteSelectedProfile()
    {
        var row = _pendingDelete;
        _pendingDelete = null;
        if (row?.Profile is null)
        {
            return;
        }

        _status = $"Deleted {row.DisplayName}.";
        _statusIsError = false;
        _networkProfileService.Delete(row.Profile.Id);
        _passwordManager.DeleteSecret(row.Profile.Id);
        _hostItems.Remove(row);
        _hostListBox.UpdateModel();
        StateHasChanged();
    }

    #endregion

    #region Input

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_pendingDelete is not null)
        {
            yield return ("Y", UiText.Get("DialogHint.DeleteSavedProfile", "Delete the saved profile"));
            yield return ("N / Esc", UiText.Get("DialogHint.KeepProfile", "Keep the profile"));
            yield break;
        }

        if (_mode == Mode.Connect)
        {
            yield return ("Enter", UiText.Get("DialogHint.Connect", "Connect"));
            yield return ("Esc", UiText.Get("DialogHint.BackToHostList", "Back to the host list"));
            yield break;
        }

        yield return ("Enter", UiText.Get("DialogHint.OpenChosenHost", "Open the chosen host"));
        yield return ("R", UiText.Get("DialogHint.SearchNetworkAgain", "Search the network again"));
        yield return ("D", UiText.Get("DialogHint.DeleteSavedProfileAsks", "Delete the saved profile (asks first)"));
        yield return ("Esc", iConsole.Localization.UiText.Get("Common.Close", "Close"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_pendingDelete is not null)
        {
            var answer = char.ToLowerInvariant(keyEvent.KeyChar);
            if (answer == 'y')
            {
                DeleteSelectedProfile();
            }
            else if (answer == 'n' || keyEvent.ConsoleKey == ConsoleKey.Escape)
            {
                _pendingDelete = null;
                _status = string.Empty;
                _statusIsError = false;
                StateHasChanged();
            }

            // Anything else is not an answer to "delete this?".
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt)
        {
            if (_mode == Mode.Connect)
            {
                BeginBrowse();
                return true;
            }
            _scanCts?.Cancel();
            Close();
            return true;
        }

        if (_mode == Mode.Browse)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Enter)
            {
                await SelectAsync();
                return true;
            }
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'r':
                    Rescan();
                    return true;
                case 'd':
                    BeginDeleteSelectedProfile();
                    return true;
            }
        }

        // In the connect form Enter connects from the text fields — the footer always said so, but
        // only a focused Connect button answered it. On the Back button Enter still goes back.
        if (_mode == Mode.Connect && keyEvent.ConsoleKey == ConsoleKey.Enter && !_backButton.IsFocused)
        {
            await ConnectPendingAsync();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
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

        var scanning = _scanning ? "  (scanning…)" : string.Empty;
        WriteLine(buffer, ix, iy, iw, $"Network Browser{scanning}", Color.Cyan, colorScheme.Background);
        DrawSectionHeader(buffer, ix, iy + 1, iw, SectionTitle(), true, colorScheme);
        DrawFooter(buffer, ix, iy + ih - 1, iw, colorScheme);
    }

    /// <summary>The band over the list or the form: which host the form connects to, and over what.</summary>
    internal string SectionTitle()
    {
        if (_mode == Mode.Browse || _pendingRow is null)
        {
            return $"Hosts ({_hostItems.Count})";
        }

        var protocol = _pendingRow.Kind is HostRowKind.DiscoveredSmbHost or HostRowKind.DiscoveredSmbShare ? "SMB" : "SSH";
        return $"Connect to {_pendingRow.Host} over {protocol}";
    }

    internal string KeyHints => _mode == Mode.Browse
        ? "Enter:connect  r:rescan  d:delete saved  Esc:close"
        : "Tab:move  Enter:connect  Ctrl+B:back  Esc:back";

    private void DrawFooter(IBuffer buffer, int x, int y, int w, ColorScheme colorScheme)
    {
        if (_pendingDelete is not null)
        {
            WriteLine(buffer, x, y, w, _status, colorScheme.Error, colorScheme.Background);
            return;
        }

        // The status beside the keys, not instead of them.
        DrawStatusAndHints(buffer, x, y, w, _status, _statusIsError ? colorScheme.Error : colorScheme.Foreground, KeyHints, colorScheme);
    }

    internal string StatusForTest => _status;
    internal bool IsConfirmingDeleteForTest => _pendingDelete is not null;

    #endregion
}
