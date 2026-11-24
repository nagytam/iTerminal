using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Model;
using iConsole.Services;
using iTerminal.Core.Commands;
using iTerminal.Core.Model.Network;
using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// 8-FTP/SFTP: the saved FTP and SFTP connections, as folders to go to. The rows are the saved network
/// profiles of those two protocols, because they are what the drives log in with (the password of an FTP
/// profile, the key of an SFTP one). An FTP entry from the Remote connections dialog opens at the folder it
/// was saved with. Enter goes to the connection under the cursor and closes the selector.
///
/// The SSH entries of the Remote connections dialog are listed too, as SFTP. Nothing is scanned here. N adds a server
/// through <see cref="FolderSelectorAddServerDialog"/>, which saves it and opens it; the other connections are edited
/// in the Remote connections dialog (Alt+F2), and an SSH login is kept by the Network Browser (Ctrl+N).
/// </summary>
public class FolderSelectorRemoteFilesTabPage : FolderSelectorTabPage
{
    /// <summary>One row of the page: what it shows, and the drive URL Enter goes to.</summary>
    internal sealed record RemoteFilesEntry(string Label, string Url);

    private readonly INetworkProfileService _networkProfileService;
    private readonly IRemoteConnectionService _remoteConnectionService;
    private readonly IBeepService _beepService;
    private readonly IDateTimeProviderService _dateTimeProviderService;
    private readonly IThemeService _themeService;
    private readonly IContentModelService _contentModelService;
    private readonly ConcurrentObservableCollection<RemoteFilesEntry> _items = new();
    private readonly ListBox<RemoteFilesEntry> _listBox;
    private readonly ScrollBar _scrollBar;
    private readonly Button _selectButton;
    private readonly Button _addServerButton;

    private readonly ICommand _selectCommand;
    protected override ICommand? SelectCommand => _selectCommand;
    protected override string SelectDescription => UiText.Get("FolderSelector.Select.Remote", "Open the saved FTP or SFTP connection under the cursor, in its folder.");

    public FolderSelectorRemoteFilesTabPage(
        INetworkProfileService networkProfileService,
        IRemoteConnectionService remoteConnectionService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(UiText.Get("FolderSelector.Tab8", "8-FTP/SFTP"), ConsoleKey.D8, themeService)
    {
        _networkProfileService = networkProfileService;
        _remoteConnectionService = remoteConnectionService;
        _beepService = beepService;
        _dateTimeProviderService = dateTimeProviderService;
        _themeService = themeService;
        _contentModelService = contentModelService;

        _listBox = new ListBox<RemoteFilesEntry>(
            new ListBoxModel<RemoteFilesEntry>(_items) { TransformItem = TransformEntry },
            themeService)
        {
            EmptyText = UiText.Get("FolderSelector.Remote.Empty", " No FTP or SFTP servers are saved. Press N to add one."),
        };
        _scrollBar = new ScrollBar(themeService)
        {
            ScrollableModel = _listBox.ScrollableModel
        };
        _selectCommand = new SelectRemoteFilesCommand(contentModelService);
        _selectButton = new Button(UiText.Get("Button.SelectFolder", "Select folder"), _selectCommand, themeService)
        {
            HotKeyChar = 'S'
        };
        // No HotKeyChar: a button's hot key is Ctrl+letter, and Ctrl+N is the Network Browser's. N on the page is the key.
        _addServerButton = new Button(UiText.Get("FolderSelector.Remote.New", "New server…"), new AddServerCommand(), themeService);

        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_selectButton);
        AddControl(_addServerButton);
    }

    /// <summary>
    /// N, on the list or on the page's own buttons: adds a server. Claimed here, ahead of the children, for the same
    /// reason Enter is (see <see cref="FolderSelectorTabPage"/>): the list would otherwise have the key first.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent is { Control: false, Alt: false } && char.ToLowerInvariant(keyEvent.KeyChar) == 'n')
        {
            await AddServerAsync();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private sealed class AddServerCommand : Command
    {
        public override string Title => "New server";
        public override string Description => "Add an FTP or SFTP server and open it.";

        public override async Task ExecuteAsync(object? args)
        {
            if (Control?.Parent is FolderSelectorRemoteFilesTabPage page)
            {
                await page.AddServerAsync();
            }
        }
    }

    /// <summary>
    /// Shows the add-a-server form over the selector. Once the server is saved the panel goes to it, and the selector
    /// closes with the form, as it does after any other choice.
    /// </summary>
    internal async Task AddServerAsync()
    {
        var selector = FindFolderSelector();
        var dialog = new FolderSelectorAddServerDialog(_remoteConnectionService, _beepService, _dateTimeProviderService, _themeService)
        {
            OpenInPanelAsync = async url =>
            {
                await _contentModelService.SetUrlAsync(new Url(url));
                selector?.Close();
            },
        };
        await ShowDialogAsync(dialog);
    }

    /// <summary>The selector this page is on: the nearest dialog above it.</summary>
    private FolderSelectorDialog? FindFolderSelector()
    {
        IControl? control = this;
        while (control is not null && control is not Dialog)
        {
            control = control.Parent;
        }

        return control as FolderSelectorDialog;
    }

    private sealed class SelectRemoteFilesCommand(IContentModelService contentModelService) : Command
    {
        public override string Title => "Open FTP or SFTP connection";

        public override async Task ExecuteAsync(object? args)
        {
            if (Control?.Parent is not FolderSelectorRemoteFilesTabPage page)
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

    private static ListItem<RemoteFilesEntry> TransformEntry(RemoteFilesEntry entry, CancellationToken cancellationToken) =>
        new(entry) { Value = entry.Label, IsEnabled = true };

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
        Reload();
    }

    public override void OnInitializeLayout()
    {
        _listBox.Position = new Position(1, 1);
        _listBox.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), Math.Max(0, ViewSize.Height - 3));
        _listBox.ScrollableModel.ViewSize = _listBox.ViewSize;

        _scrollBar.Position = new Position(Math.Max(0, ViewSize.Width - 1), 1);
        _scrollBar.ViewSize = new Size(1, Math.Max(0, ViewSize.Height - 3));

        // The two buttons share the bottom row: Select folder takes the left half, New server the right.
        var half = Math.Max(0, (ViewSize.Width - 3) / 2);
        _selectButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectButton.ViewSize = new Size(half, 1);
        _addServerButton.Position = new Position(1 + half + 1, Math.Max(0, ViewSize.Height - 1));
        _addServerButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - half - 3), 1);

        base.OnInitializeLayout();
    }

    /// <summary>Rebuilds the list from the saved connections. Both reads are from the settings cache, not I/O.</summary>
    private void Reload()
    {
        var connections = _remoteConnectionService.GetAll();
        var profiles = _networkProfileService.GetAll();

        // An SSH entry saved before it was listed here has no profile yet. Saving it again writes the profile, so it
        // is done once, here; an entry that cannot be saved is left out rather than failing the page.
        var missing = connections
            .Where(c => c.Protocol == RemoteProtocol.Ssh && c.EffectivePort == c.DefaultPort && profiles.All(p => p.Id != c.Id))
            .ToList();
        if (missing.Count > 0)
        {
            foreach (var connection in missing)
            {
                try
                {
                    _remoteConnectionService.Save(connection);
                }
                catch (InvalidOperationException)
                {
                    // Not a sound entry any more; it stays a shell entry only.
                }
            }
            profiles = _networkProfileService.GetAll();
        }

        _items.ReplaceAll(RemoteFilesBrowserRows.Build(profiles, connections));
    }

    /// <summary>The URL of the row under the cursor, or null when the list is empty.</summary>
    internal string? ActivateSelected()
    {
        // Read from the rows rather than the list box's transformed items, which are only filled once the list
        // has been drawn. The list has no filter or sort of its own, so the cursor is an index into the rows.
        var index = (int)_listBox.ScrollableModel.CursorPosition.Y;
        return index >= 0 && index < _items.Count ? _items[index].Url : null;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        foreach (var hint in base.GetKeyHints())
        {
            yield return hint;
        }
        yield return ("N", UiText.Get("AddServer.Hint.Page", "Add an FTP or SFTP server, and open it."));
        yield return ("Alt+F2", UiText.Get("FolderSelector.Hint.RemoteConnections", "Add and edit the connections listed here, in Remote connections."));
    }
}

/// <summary>
/// The rows the 8-FTP/SFTP page lists. Kept apart from the page so the rules are pinned without a screen.
/// </summary>
internal static class RemoteFilesBrowserRows
{
    /// <summary>
    /// The FTP and SFTP profiles, one row each, sorted by host and then user. An FTP profile takes its folder
    /// from the saved entry with the same id; an SFTP profile has no folder to remember and opens at the root.
    /// Other protocols are left out.
    /// </summary>
    public static List<FolderSelectorRemoteFilesTabPage.RemoteFilesEntry> Build(
        IEnumerable<NetworkConnectionProfile> profiles,
        IEnumerable<RemoteConnection> connections)
    {
        var connectionsById = connections
            .Where(c => c.Protocol == RemoteProtocol.Ftp)
            .GroupBy(c => c.Id)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<(NetworkConnectionProfile Profile, FolderSelectorRemoteFilesTabPage.RemoteFilesEntry Entry)>();
        foreach (var profile in profiles)
        {
            if (profile.Protocol is not (NetworkProtocol.Ftp or NetworkProtocol.Ssh) || string.IsNullOrEmpty(profile.Host))
            {
                continue;
            }

            rows.Add((profile, profile.Protocol == NetworkProtocol.Ftp ? FtpRow(profile, connectionsById) : SftpRow(profile)));
        }

        return [.. rows
            .OrderBy(r => r.Profile.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Profile.Username, StringComparer.OrdinalIgnoreCase)
            .Select(r => r.Entry)];
    }

    private static FolderSelectorRemoteFilesTabPage.RemoteFilesEntry FtpRow(
        NetworkConnectionProfile profile,
        Dictionary<string, RemoteConnection> connectionsById)
    {
        connectionsById.TryGetValue(profile.Id, out var connection);
        var user = UserPart(profile.Username);
        var folder = (connection?.RemotePath ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/');
        var url = connection?.DriveUrl ?? Container.FTP + user + profile.Host + "/";
        return new(
            $"FTP   {user}{profile.Host}/{folder}",
            url);
    }

    private static FolderSelectorRemoteFilesTabPage.RemoteFilesEntry SftpRow(NetworkConnectionProfile profile)
    {
        // The form the Network Browser opens an SSH login with: the host, then an empty port, then the root.
        var user = UserPart(profile.Username);
        return new(
            $"SFTP  {user}{profile.Host}/",
            Container.SSH + user + profile.Host + ":/");
    }

    private static string UserPart(string username) => string.IsNullOrEmpty(username) ? string.Empty : username + "@";
}
