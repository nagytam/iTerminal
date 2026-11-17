using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// 7-SMB: the network neighbourhood for SMB. The list starts with the hosts that answered on port 445
/// during a scan and the saved SMB connections; Enter on a host lists its shares, Enter on a share goes
/// to <c>smb://[user@]host/share/</c> and closes the selector, and Backspace goes back to the hosts.
///
/// A share that needs a login is reached through a saved connection (the Network Browser, Ctrl+N, saves
/// one with its password), so a host that lists no shares without one says so instead of asking here.
/// The scan runs on a background task and parks its results; <see cref="OnIdle"/> applies them, so the
/// list is only ever rebuilt on the UI thread.
/// </summary>
public class FolderSelectorSmbTabPage : FolderSelectorTabPage
{
    /// <summary>One row of the page: a host (<see cref="Share"/> null) or a share on a host.</summary>
    internal sealed record SmbEntry(string Host, string? Share, string? Username, string Label);

    /// <summary>The scan is a sweep of the local /24s; it is stopped after this even if it has not finished.</summary>
    internal static readonly TimeSpan ScanLimit = TimeSpan.FromSeconds(30);

    private readonly INetworkDiscoveryService _networkDiscoveryService;
    private readonly INetworkProfileService _networkProfileService;
    private readonly IContentModelService _contentModelService;

    private readonly object _foundLock = new();
    private readonly List<DiscoveredHost> _found = [];
    private volatile bool _dirty;
    private volatile bool _scanning;
    private string? _scanError;
    private CancellationTokenSource? _scanCts;

    private readonly ConcurrentObservableCollection<SmbEntry> _items = new();
    private readonly ListBox<SmbEntry> _listBox;
    private readonly Label _statusLabel;
    private readonly Button _selectButton;

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
        IThemeService themeService)
        : base(UiText.Get("FolderSelector.Tab7", "7-SMB"), ConsoleKey.D7, themeService)
    {
        _networkDiscoveryService = networkDiscoveryService;
        _networkProfileService = networkProfileService;
        _contentModelService = contentModelService;

        _listBox = new ListBox<SmbEntry>(
            new ListBoxModel<SmbEntry>(_items) { TransformItem = TransformEntry },
            themeService)
        {
            EmptyText = UiText.Get("FolderSelector.Smb.Looking", " Looking for SMB hosts on the local network…"),
        };
        _statusLabel = new Label(string.Empty, themeService);
        _selectCommand = new SelectSmbCommand(_contentModelService);
        _selectButton = new Button(UiText.Get("Button.SelectFolder", "Select folder"), _selectCommand, themeService)
        {
            HotKeyChar = 'S'
        };

        AddControl(_listBox);
        AddControl(_statusLabel);
        AddControl(_selectButton);
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
        _listBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 3));
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _statusLabel.Position = new Position(1, Math.Max(0, ViewSize.Height - 2));
        _statusLabel.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        _selectButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }

    public override void OnIdle()
    {
        base.OnIdle();
        if (!_dirty)
        {
            return;
        }
        _dirty = false;
        ApplyRows();
    }

    #region Scan

    private void Rescan()
    {
        _scanCts?.Cancel();
        var cts = new CancellationTokenSource(ScanLimit);
        _scanCts = cts;
        lock (_foundLock)
        {
            _found.Clear();
        }
        _scanning = true;
        _scanError = null;
        _dirty = true;

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

    private void ApplyRows()
    {
        List<DiscoveredHost> found;
        lock (_foundLock)
        {
            found = [.. _found];
        }
        var profiles = _networkProfileService.GetAll().ToList();

        var rows = _hostFilter is null
            ? SmbBrowserRows.Hosts(found, profiles)
            : SmbBrowserRows.Shares(_hostFilter, found, profiles);
        _items.ReplaceAll(rows);
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

        var hasShares = SmbBrowserRows.Shares(entry.Host, SnapshotFound(), _networkProfileService.GetAll().ToList()).Count > 0;
        if (!hasShares)
        {
            _status = string.Format(UiText.Get("FolderSelector.Smb.NoShares", "No shares are listed on {0} without a login. Save a connection in the Network Browser (Ctrl+N) first."), entry.Host);
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

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Control && !keyEvent.Shift && !keyEvent.Alt;
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
}

/// <summary>
/// The rows the 7-SMB page lists, built from a scan and the saved connections. Kept apart from the page
/// so the rules (which hosts appear, how a saved login overrides a discovered share, the address a share
/// goes to) are tested without a screen or a network.
/// </summary>
internal static class SmbBrowserRows
{
    public static List<FolderSelectorSmbTabPage.SmbEntry> Hosts(IEnumerable<DiscoveredHost> found, IEnumerable<NetworkConnectionProfile> profiles)
    {
        var savedProfiles = profiles.Where(p => p.Protocol == NetworkProtocol.Smb && !string.IsNullOrWhiteSpace(p.Host)).ToList();
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
            var label = saved
                ? $"{address} (SMB, " + UiText.Get("FolderSelector.Smb.Saved", "saved") + ")"
                : $"{address} (SMB)";
            rows.Add(new(address, null, null, label));
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

    public static List<FolderSelectorSmbTabPage.SmbEntry> Shares(string host, IEnumerable<DiscoveredHost> found, IEnumerable<NetworkConnectionProfile> profiles)
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

        foreach (var profile in profiles.Where(p => p.Protocol == NetworkProtocol.Smb && string.Equals(p.Host, host, StringComparison.OrdinalIgnoreCase)))
        {
            if (string.IsNullOrWhiteSpace(profile.ShareName))
            {
                continue;
            }
            // A saved connection carries the login a discovered share lacks, so it replaces that row.
            var username = string.IsNullOrEmpty(profile.Username) ? null : profile.Username;
            var entry = new FolderSelectorSmbTabPage.SmbEntry(host, profile.ShareName, username, $"{host}\\{profile.ShareName} (SMB, " + UiText.Get("FolderSelector.Smb.Saved", "saved") + ")");
            var existing = rows.FindIndex(r => string.Equals(r.Share, profile.ShareName, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                rows[existing] = entry;
            }
            else
            {
                rows.Add(entry);
            }
        }

        return rows;
    }

    /// <summary>The address a share goes to: <c>smb://[user@]host/share/</c>, the same form the Network Browser opens.</summary>
    public static string UrlFor(FolderSelectorSmbTabPage.SmbEntry entry)
    {
        var userPart = string.IsNullOrEmpty(entry.Username) ? string.Empty : entry.Username + "@";
        return Container.SMB + userPart + entry.Host + "/" + entry.Share + "/";
    }
}
