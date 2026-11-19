using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Controls.ScrollBar;
using iConsole.Data;
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
/// Nothing is typed here and nothing is scanned: a new connection is made in the Remote connections dialog
/// (Alt+F2), and an SSH login is kept by the Network Browser (Ctrl+N).
/// </summary>
public class FolderSelectorRemoteFilesTabPage : FolderSelectorTabPage
{
    /// <summary>One row of the page: what it shows, and the drive URL Enter goes to.</summary>
    internal sealed record RemoteFilesEntry(string Label, string Url);

    private readonly INetworkProfileService _networkProfileService;
    private readonly IRemoteConnectionService _remoteConnectionService;
    private readonly ConcurrentObservableCollection<RemoteFilesEntry> _items = new();
    private readonly ListBox<RemoteFilesEntry> _listBox;
    private readonly ScrollBar _scrollBar;
    private readonly Button _selectButton;

    private readonly ICommand _selectCommand;
    protected override ICommand? SelectCommand => _selectCommand;
    protected override string SelectDescription => UiText.Get("FolderSelector.Select.Remote", "Open the saved FTP or SFTP connection under the cursor, in its folder.");

    public FolderSelectorRemoteFilesTabPage(
        INetworkProfileService networkProfileService,
        IRemoteConnectionService remoteConnectionService,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base(UiText.Get("FolderSelector.Tab8", "8-FTP/SFTP"), ConsoleKey.D8, themeService)
    {
        _networkProfileService = networkProfileService;
        _remoteConnectionService = remoteConnectionService;

        _listBox = new ListBox<RemoteFilesEntry>(
            new ListBoxModel<RemoteFilesEntry>(_items) { TransformItem = TransformEntry },
            themeService)
        {
            EmptyText = UiText.Get("FolderSelector.Remote.Empty", " No FTP or SFTP connections are saved. Add one in Remote connections (Alt+F2)."),
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

        AddControl(_listBox);
        AddControl(_scrollBar);
        AddControl(_selectButton);
    }

    private sealed class SelectRemoteFilesCommand(IContentModelService contentModelService) : Command
    {
        public override string Title => "Open FTP or SFTP connection";

        public override async Task ExecuteAsync(object? arg)
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

        _selectButton.Position = new Position(1, Math.Max(0, ViewSize.Height - 1));
        _selectButton.ViewSize = new Size(Math.Max(0, ViewSize.Width - 2), 1);

        base.OnInitializeLayout();
    }

    /// <summary>Rebuilds the list from the saved connections. Both reads are from the settings cache, not I/O.</summary>
    private void Reload()
    {
        var rows = RemoteFilesBrowserRows.Build(_networkProfileService.GetAll(), _remoteConnectionService.GetAll());
        _items.ReplaceAll(rows);
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
