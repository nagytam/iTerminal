using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services.Remote;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// N on the 8-FTP/SFTP page of the folder selector: adds an FTP or SFTP server and opens it in the panel, without
/// a detour through Remote connections. The entry is saved the way the Remote connections dialog saves one (so it is
/// listed on that page from then on), the password goes to the password safe, and the selector closes once the
/// panel has been pointed at the new server.
/// </summary>
public class FolderSelectorAddServerDialog : FormDialog
{
    private const int ErrorRows = 2;
    private const int DefaultFtpPort = 21;
    private const int DefaultSftpPort = 22;

    private readonly IRemoteConnectionService _connections;
    private readonly RadioGroup<RemoteProtocol> _protocol;
    private readonly TextBox _textBoxHost;
    private readonly TextBox _textBoxPort;
    private readonly TextBox _textBoxUser;
    private readonly PasswordBox _passwordBox;
    private readonly TextBox _textBoxFolder;
    private readonly TextBox _textBoxName;
    private readonly Label _labelHint;
    private readonly Label[] _errorLabels;
    private readonly Button _addButton;
    private readonly Button _cancelButton;

    public FolderSelectorAddServerDialog(
        IRemoteConnectionService connections,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(FolderSelectorAddServerDialog), themeService)
    {
        _connections = connections;
        Title = UiText.Get("Dialog.AddServer", "Add a server");
        var addCommand = new SubmitCommand();
        AddCommand(addCommand);

        _protocol = new RadioGroup<RemoteProtocol>(themeService)
        {
            DisplaySelector = protocol => protocol == RemoteProtocol.Ftp
                ? UiText.Get("AddServer.Type.Ftp", "FTP")
                : UiText.Get("AddServer.Type.Sftp", "SFTP (SSH)"),
        };
        _protocol.SetItems([RemoteProtocol.Ftp, RemoteProtocol.Ssh]);
        _protocol.SelectedIndex = 0;
        _protocol.SelectionChanged += (_, _) => OnProtocolChanged();

        _textBoxHost = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("AddServer.Host", "Host: "),
            Placeholder = "files.example.com",
        };
        _textBoxPort = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("AddServer.Port", "Port: "),
        };
        _textBoxUser = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("AddServer.User", "User: "),
        };
        _passwordBox = new PasswordBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("AddServer.Password", "Password: "),
        };
        _textBoxFolder = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("AddServer.Folder", "Folder: "),
            Placeholder = "/",
        };
        _textBoxName = new TextBox(beepService, dateTimeProviderService, themeService)
        {
            Label = UiText.Get("AddServer.Name", "Name: "),
        };
        _labelHint = new Label(string.Empty, themeService);
        _errorLabels = [.. Enumerable.Range(0, ErrorRows).Select(_ => new Label(string.Empty, themeService))];
        _addButton = new Button(UiText.Get("AddServer.Button.Add", "Add and open"), addCommand, themeService);
        _cancelButton = new CancelDialogButton(themeService);

        // The type comes first: it decides which of the other fields apply, and it is where the cursor starts.
        AddControl(_protocol);
        AddControl(_textBoxHost);
        AddControl(_textBoxPort);
        AddControl(_textBoxUser);
        AddControl(_passwordBox);
        AddControl(_textBoxFolder);
        AddControl(_textBoxName);
        AddControl(_labelHint);
        foreach (var label in _errorLabels)
        {
            AddControl(label);
        }

        AddControl(_addButton);
        AddControl(_cancelButton);
        OnProtocolChanged();
    }

    /// <summary>
    /// Opens the panel at the new server: called with its drive URL once the entry is saved, before the dialog closes.
    /// The folder selector sets it, so that it can close with this dialog.
    /// </summary>
    public Func<string, Task>? OpenInPanelAsync { get; set; }

    /// <summary>The entry the dialog saved; null until it has saved one.</summary>
    public RemoteConnection? Added { get; private set; }

    internal RadioGroup<RemoteProtocol> ProtocolForTest => _protocol;
    internal TextBox HostForTest => _textBoxHost;
    internal TextBox PortForTest => _textBoxPort;
    internal TextBox UserForTest => _textBoxUser;
    internal PasswordBox PasswordForTest => _passwordBox;
    internal TextBox FolderForTest => _textBoxFolder;
    internal TextBox NameForTest => _textBoxName;
    internal string ErrorForTest => string.Join(" ", _errorLabels.Select(l => l.Text).Where(t => t.Length > 0));

    private RemoteProtocol SelectedProtocol => _protocol.SelectedItem;

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Clamp((int)Parent.ViewSize.Width - 8, 56, 80), 15));
        }

        var row = 1;
        var fields = new List<IControl> { _protocol, _textBoxHost, _textBoxPort, _textBoxUser, _passwordBox, _textBoxFolder, _textBoxName, _labelHint };
        fields.AddRange(_errorLabels);
        foreach (var control in fields)
        {
            control.Position = new Position(FieldX, row++);
            control.ViewSize = new Size(FieldWidth, 1);
        }

        LayoutButtonRow(_addButton, _cancelButton);
        base.OnInitializeLayout();
    }

    /// <summary>
    /// The type decides the rest: an FTP server has a port and a folder, an SFTP server is always on port 22 (the SSH
    /// drive connects nowhere else) and opens at its root, so those two fields are off for it.
    /// </summary>
    private void OnProtocolChanged()
    {
        var ftp = SelectedProtocol == RemoteProtocol.Ftp;
        _textBoxPort.IsEnabled = ftp;
        _textBoxFolder.IsEnabled = ftp;
        _textBoxPort.Placeholder = string.Format(
            UiText.Get("AddServer.PortDefault", "default {0}"),
            ftp ? DefaultFtpPort : DefaultSftpPort);
        _labelHint.Text = ftp
            ? UiText.Get("AddServer.Hint.Ftp", "FTP: a blank password logs in anonymously; the password is kept in the password safe.")
            : UiText.Get("AddServer.Hint.Sftp", "SFTP: port 22; a blank password uses your key or SSH agent; the password is kept in the password safe.");
        StateHasChanged();
    }

    protected override void ClearError() => SetError(string.Empty);

    /// <summary>A failure is a sentence: wrapped over the rows kept for it, not cut at the border.</summary>
    private void SetError(string message)
    {
        var lines = message.Length == 0 ? [] : string.Format(UiText.Get("Msg.ErrorPrefix", "Error: {0}"), message).WrapWords((int)Math.Max(10, FieldWidth));
        for (var i = 0; i < _errorLabels.Length; i++)
        {
            _errorLabels[i].Text = i < lines.Count ? lines[i] : string.Empty;
        }

        StateHasChanged();
    }

    /// <summary>
    /// Saves the server, points the panel at it and closes. A problem stays in the dialog, in words. Returns whether
    /// the server was added.
    /// </summary>
    public async Task<bool> AddAsync()
    {
        var host = _textBoxHost.Text.Trim();
        if (host.Length == 0)
        {
            SetError(UiText.Get("AddServer.HostNeeded", "Type the host name or IP address first."));
            return false;
        }

        var ftp = SelectedProtocol == RemoteProtocol.Ftp;
        int? port = null;
        var portText = _textBoxPort.Text.Trim();
        if (ftp && portText.Length > 0)
        {
            if (!int.TryParse(portText, out var typedPort))
            {
                SetError(UiText.Get("Remote.PortInvalid", "The port must be between 1 and 65535."));
                return false;
            }

            port = typedPort;
        }

        var connection = new RemoteConnection
        {
            Name = _textBoxName.Text.Trim().Length > 0 ? _textBoxName.Text.Trim() : host,
            Protocol = SelectedProtocol,
            Host = host,
            Port = port,
            Username = _textBoxUser.Text.Trim(),
            RemotePath = ftp ? NullIfEmpty(_textBoxFolder.Text) : null,
        };
        var password = _passwordBox.Text;
        if (connection.Validate() is { } problem)
        {
            SetError(problem);
            return false;
        }

        try
        {
            _connections.Save(connection, string.IsNullOrEmpty(password) ? null : password);
        }
        catch (InvalidOperationException exception)
        {
            SetError(exception.Message);
            return false;
        }

        Added = connection;
        _connections.MarkConnected(connection.Id);
        var url = ftp ? connection.DriveUrl : Container.SSH + (connection.Username.Length > 0 ? connection.Username + "@" : string.Empty) + connection.Host + ":/";
        if (OpenInPanelAsync is { } open)
        {
            await open(url);
        }

        Close();
        return true;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Enter, from any field: adds the server.</summary>
    private sealed class SubmitCommand : Command
    {
        public override string Title => "Add and open";
        public override string Description => "Save the server and open it in the panel.";

        public override Key? Key { get; init; } =
            new Key(ConsoleKey.Enter, false, false, false);

        public override async Task ExecuteAsync(object? args)
        {
            var dialog = Control as FolderSelectorAddServerDialog ?? Control?.Parent as FolderSelectorAddServerDialog;
            if (dialog is not null)
            {
                await dialog.AddAsync();
            }
        }
    }
}
