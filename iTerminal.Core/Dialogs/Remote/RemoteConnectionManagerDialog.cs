using iConsole.Helper;
using iConsole.Localization;
using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Model.Remote;
using iTerminal.Core.Services.Remote;
using iTerminal.Core.Services.Shell;

namespace iTerminal.Core.Dialogs.Remote;

/// <summary>
/// The remote connections: SSH hosts, PowerShell remoting targets, FTP servers and WebDAV servers, saved by
/// name. Enter hands the whole terminal to an SSH or PowerShell session (<see cref="IInteractiveShellService"/>) —
/// a real <c>ssh</c> or <c>Enter-PSSession</c> with its prompt, colours and Ctrl+C — and <c>exit</c> comes back
/// here. B opens an SSH host in the active panel as an SFTP drive; for an FTP or WebDAV entry, which has no
/// shell, Enter and B both open its drive. N adds, E edits and D removes (after a y).
///
/// <para>A shell's password is never stored: the session asks for it on the terminal it was lent. An FTP or
/// WebDAV password has no such terminal, so it goes to the password manager (see
/// <see cref="IRemoteConnectionService.Save(RemoteConnection, string?)"/>) under the entry's id — where the drives find it.</para>
/// </summary>
public class RemoteConnectionManagerDialog : Dialog
{
    private enum Mode
    {
        List,
        Edit,
    }

    /// <summary>The Type choices, in the order the segmented control shows them: WebDAV comes twice, over HTTP and over HTTPS.</summary>
    private static readonly (RemoteProtocol Protocol, bool Secure, string Label)[] ProtocolChoices =
    [
        (RemoteProtocol.Ssh, false, "SSH"),
        (RemoteProtocol.PowerShell, false, "PowerShell"),
        (RemoteProtocol.Ftp, false, "FTP"),
        (RemoteProtocol.WebDav, false, "WebDAV http"),
        (RemoteProtocol.WebDav, true, "WebDAV https"),
    ];

    /// <summary>A box that takes digits only, so a pasted "port 2200!" becomes 2200.</summary>
    private sealed class PortBox(IBeepService beepService, IDateTimeProviderService dateTimeProviderService, IThemeService themeService)
        : TextBox(beepService, dateTimeProviderService, themeService)
    {
        protected override bool IsAllowedCharacter(char c) => char.IsAsciiDigit(c);
    }

    private const int DialogWidth = 84;
    private const int DialogHeight = 24;
    private const int ListRows = 12;
    private const int MaxFieldLength = 300;

    private const int FormRows = 10;

    private readonly IRemoteConnectionService _connections;
    private readonly IInteractiveShellService _shellService;
    private readonly IContentModelService _contentModelService;
    private readonly Lock _sync = new();

    private Mode _mode = Mode.List;
    private IReadOnlyList<RemoteConnection> _list = [];
    private int _cursor;
    private string? _selectedId;
    private bool _confirmDelete;

    private readonly FormPanel _form;
    private readonly TextBox _name;
    private readonly SegmentedControl _type;
    private readonly TextBox _host;
    private readonly TextBox _port;
    private readonly TextBox _user;
    private readonly TextBox _keyFile;
    private readonly SegmentedControl _transport;
    private readonly TextBox _notes;
    private readonly PasswordBox _password;
    private readonly TextBox _folder;
    private RemoteConnection? _editing;
    private string _baseline = string.Empty;
    private bool _confirmDiscard;

    private string _status = string.Empty;
    private bool _statusIsError;

    public RemoteConnectionManagerDialog(
        IRemoteConnectionService connections,
        IInteractiveShellService shellService,
        IContentModelService contentModelService,
        IBeepService beepService,
        IDateTimeProviderService dateTimeProviderService,
        IThemeService themeService)
        : base(nameof(RemoteConnectionManagerDialog), themeService)
    {
        Title = iConsole.Localization.UiText.Get("Dialog.RemoteConnections", "Remote connections");
        _connections = connections;
        _shellService = shellService;
        _contentModelService = contentModelService;

        // The edit form is built once, from the same controls every other form uses; it is attached to the dialog
        // while an entry is being edited and taken off again for the list.
        TextBox Box() => new(beepService, dateTimeProviderService, themeService) { MaxLength = MaxFieldLength };
        _name = Box();
        _type = new SegmentedControl(themeService) { Options = [.. ProtocolChoices.Select(c => c.Label)] };
        _host = Box();
        _port = new PortBox(beepService, dateTimeProviderService, themeService) { MaxLength = 5 };
        _user = Box();
        _keyFile = Box();
        _transport = new SegmentedControl(themeService) { Options = ["WinRM (Windows)", "SSH (PowerShell 7)"] };
        _notes = Box();
        _password = new PasswordBox(beepService, dateTimeProviderService, themeService) { MaxLength = MaxFieldLength };
        _folder = Box();
        _type.SelectionChanged += (_, _) => UpdateFormFields();
        _transport.SelectionChanged += (_, _) => UpdateFormFields();

        _form = new FormPanel(themeService);
        _form.AddField(UiText.Get("Remote.FieldName", "Name"), _name);
        _form.AddField(UiText.Get("Remote.FieldType", "Type"), _type);
        _form.AddField(UiText.Get("Remote.FieldHost", "Host"), _host);
        _form.AddField(UiText.Get("Remote.FieldPort", "Port"), _port);
        _form.AddField(UiText.Get("Remote.FieldUser", "User"), _user);
        _form.AddField(UiText.Get("Remote.FieldKeyFile", "Key file"), _keyFile);
        _form.AddField(UiText.Get("Remote.FieldTransport", "Transport"), _transport);
        _form.AddField(UiText.Get("Remote.FieldNotes", "Notes"), _notes);
        _form.AddField(UiText.Get("Remote.FieldPassword", "Password"), _password);
        _form.AddField(UiText.Get("Remote.FieldFolder", "Folder"), _folder);
    }

    /// <summary>The session or panel navigation the last key started. Nothing waits on it; it exists so a test can.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            LeaveEdit();
            _confirmDelete = false;
            Reload();
        }

        await base.ShowAsync();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        LayoutForm();
        base.OnInitializeLayout();
    }

    /// <summary>The form sits under the heading row, inside the border, and leaves the two footer rows alone.</summary>
    private void LayoutForm()
    {
        _form.Position = new Position(2, 3);
        _form.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), FormRows);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_mode == Mode.Edit)
        {
            if (_confirmDiscard)
            {
                yield return ("Y", UiText.Get("DialogHint.DiscardChanges", "Discard the changes"));
                yield return ("Any other key", UiText.Get("DialogHint.KeepEditing", "Keep editing"));
                yield break;
            }

            yield return ("Ctrl+S / Enter", UiText.Get("DialogHint.SaveConnection", "Save the connection"));
            yield return ("Tab / Shift+Tab", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
            yield return ("Left/Right", UiText.Get("Hint.ChoosePreviousNextSegment", "Choose the previous or next segment"));
            yield return ("Esc", UiText.Get("DialogHint.BackToList", "Back to the list"));
            yield break;
        }

        if (_confirmDelete)
        {
            yield return ("Y / D / Delete", UiText.Get("DialogHint.DeleteConnection", "Delete the connection"));
            yield return ("Any other key", UiText.Get("DialogHint.KeepConnection", "Keep the connection"));
            yield break;
        }

        yield return ("Up/Down", UiText.Get("DialogHint.ChooseConnection", "Choose a connection"));
        yield return ("Enter", UiText.Get("DialogHint.ConnectLentTerminal", "Connect on the lent terminal"));
        yield return ("N", UiText.Get("DialogHint.AddConnection", "Add a connection"));
        yield return ("E", UiText.Get("DialogHint.EditConnection", "Edit the connection"));
        yield return ("D/Delete", UiText.Get("DialogHint.DeleteConnectionAsks", "Delete the connection (asks first)"));
        yield return ("B", UiText.Get("DialogHint.BrowseConnection", "Browse the connection in a panel"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        lock (_sync)
        {
            switch (_mode)
            {
                case Mode.List:
                    if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
                    {
                        break;
                    }

                    if (HandleListKey(keyEvent))
                    {
                        return true;
                    }

                    break;
                case Mode.Edit:
                    // Enter, Ctrl+S and Esc are the dialog's; every other key goes to the field that has the focus.
                    if (HandleEditKey(keyEvent))
                    {
                        return true;
                    }

                    break;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    #region Keys

    private bool HandleListKey(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        // Only the keys that continue or answer the question keep it open; any other key cancels it.
        var answersDelete = keyEvent.ConsoleKey == ConsoleKey.Delete || char.ToLowerInvariant(keyEvent.KeyChar) is 'd' or 'y';
        if (!answersDelete)
        {
            _confirmDelete = false;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                return MoveCursor(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveCursor(1);
            case ConsoleKey.PageUp when plain:
                return MoveCursor(-ListRows);
            case ConsoleKey.PageDown when plain:
                return MoveCursor(ListRows);
            case ConsoleKey.Home:
                return MoveCursor(-int.MaxValue / 2);
            case ConsoleKey.End:
                return MoveCursor(int.MaxValue / 2);
            case ConsoleKey.Enter when plain:
                Connect();
                return true;
            case ConsoleKey.Delete when plain:
                AskDelete();
                return true;
        }

        if (!plain)
        {
            return false;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'n':
                OpenEdit(null);
                return true;
            case 'e':
                OpenEdit(Selected);
                return true;
            case 'd':
                AskDelete();
                return true;
            case 'y' when _confirmDelete:
                DeleteSelected();
                return true;
            case 'b':
                Browse();
                return true;
        }

        return false;
    }

    private bool HandleEditKey(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        if (_confirmDiscard)
        {
            // Only y throws the edit away; any other key is the answer "no" and is not typed into a field.
            _confirmDiscard = false;
            if (plain && char.ToLowerInvariant(keyEvent.KeyChar) == 'y')
            {
                LeaveEdit();
                SetStatus(string.Empty, false);
            }
            else
            {
                SetStatus(EditHint(), false);
            }

            StateHasChanged();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Escape && plain)
        {
            if (IsEditDirty())
            {
                _confirmDiscard = true;
                SetStatus(Say("Remote.Status.ConfirmDiscard", "Discard the changes? Press y to discard, anything else keeps editing."), true);
            }
            else
            {
                LeaveEdit();
                SetStatus(string.Empty, false);
            }

            StateHasChanged();
            return true;
        }

        if ((keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.S)
            || (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt))
        {
            SaveEdit();
            return true;
        }

        return false;
    }

    private string EditHint() => _editing is null
        ? iConsole.Localization.UiText.Get("Remote.EditHintNew", "A new connection. Enter or Ctrl+S saves it, Esc goes back.")
        : iConsole.Localization.UiText.Get("Remote.EditHint", "Enter or Ctrl+S saves, Esc goes back.");

    private RemoteProtocol EditProtocol => ProtocolChoices[Math.Clamp(_type.SelectedIndex, 0, ProtocolChoices.Length - 1)].Protocol;

    private bool EditSecure => ProtocolChoices[Math.Clamp(_type.SelectedIndex, 0, ProtocolChoices.Length - 1)].Secure;

    private PowerShellTransport EditTransport => _transport.SelectedIndex == 0 ? PowerShellTransport.WinRm : PowerShellTransport.Ssh;

    /// <summary>
    /// What the type decides: the transport is for PowerShell, the key file for an SSH login, the password and the
    /// folder for the file drives. A field that does not apply is dimmed and skipped by Tab; it keeps its text.
    /// </summary>
    private void UpdateFormFields()
    {
        var fileDrive = EditProtocol is RemoteProtocol.Ftp or RemoteProtocol.WebDav;
        _transport.IsEnabled = EditProtocol == RemoteProtocol.PowerShell;
        _keyFile.IsEnabled = !fileDrive && (EditProtocol == RemoteProtocol.Ssh || EditTransport == PowerShellTransport.Ssh);
        _password.IsEnabled = fileDrive;
        _folder.IsEnabled = fileDrive;

        var onlyFileDrives = iConsole.Localization.UiText.Get("Remote.OnlyFileDrives", "only for FTP and WebDAV");
        if (!fileDrive) _password.Placeholder = onlyFileDrives;
        else if (_editing?.HasSavedCredential == true) _password.Placeholder = iConsole.Localization.UiText.Get("Remote.PasswordSaved", "(saved - type to replace)");
        else _password.Placeholder = iConsole.Localization.UiText.Get("Remote.PasswordNone", "(none: anonymous or asks nothing)");
        _folder.Placeholder = fileDrive ? string.Empty : onlyFileDrives;
        _port.Placeholder = string.Format(iConsole.Localization.UiText.Get("Remote.DefaultPort", "(default {0})"), DefaultPortFor());
        StateHasChanged();
    }

    /// <summary>Every value the form holds, as one string: Esc asks only when this differs from what the form opened with.</summary>
    private string FormSnapshot() => string.Join('\u001f',
        _name.Text, _type.SelectedIndex, _host.Text, _port.Text, _user.Text, _keyFile.Text,
        _transport.SelectedIndex, _notes.Text, _password.Text, _folder.Text);

    private bool IsEditDirty() => FormSnapshot() != _baseline;

    /// <summary>Takes the form off the dialog and returns to the list. Safe to call when the form is not up.</summary>
    private void LeaveEdit()
    {
        _mode = Mode.List;
        _confirmDiscard = false;
        if (_form.Parent is not null)
        {
            RemoveControl(_form);
        }

        SetFocus();
    }

    #endregion

    #region Actions

    private RemoteConnection? Selected => _list.FirstOrDefault(c => c.Id == _selectedId) ?? _list.FirstOrDefault();

    private void Reload()
    {
        _list = _connections.GetAll();
        var index = _list.ToList().FindIndex(c => c.Id == _selectedId);
        _cursor = index >= 0 ? index : Math.Clamp(_cursor, 0, Math.Max(0, _list.Count - 1));
        _selectedId = _list.ElementAtOrDefault(_cursor)?.Id;
    }

    /// <summary>
    /// The wheel moves the highlight three connections; a click highlights the one under the pointer. Only the list
    /// answers, and not while "delete?" is on screen.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode == Mode.List && !_confirmDelete && _list.Count > 0)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return MoveCursor(mouseEvent.WheelDelta > 0 ? -3 : 3);
            }

            if (mouseEvent.LeftClicked)
            {
                long row = mouseEvent.Position.Y - (AbsolutePosition.Y + 3);
                long column = mouseEvent.Position.X - (AbsolutePosition.X + 2);
                int first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
                if (row >= 0 && row < ListRows && column >= 0 && column < ViewSize.Width - 4 && first + (int)row < _list.Count)
                {
                    return MoveCursor(first + (int)row - _cursor);
                }
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    private bool MoveCursor(int delta)
    {
        _confirmDelete = false;
        if (_list.Count > 0)
        {
            _cursor = Math.Clamp(_cursor + delta, 0, _list.Count - 1);
            _selectedId = _list[_cursor].Id;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>Lends the terminal to the selected connection on a thread of its own; the key handler only starts it.</summary>
    private void Connect()
    {
        var connection = Selected;
        if (connection is null)
        {
            SetStatus(Say("Remote.Status.NoConnection", "Nothing to connect to yet. N adds a connection."), true);
            StateHasChanged();
            return;
        }

        if (connection.IsFileDrive)
        {
            // No shell behind it: Enter does what B does.
            Browse();
            return;
        }

        if (!RemoteShellLaunch.TryCreate(connection, out var launch, out var error) || launch is null)
        {
            SetStatus(error ?? Say("Remote.Status.CannotStart", "This connection cannot be started."), true);
            StateHasChanged();
            return;
        }

        if (_shellService.IsRunning)
        {
            SetStatus(Say("Remote.Status.ShellRunning", "A shell is already running."), true);
            StateHasChanged();
            return;
        }

        SetStatus(Say("Remote.Status.Connecting", "Connecting to {0}…", connection.Name), false);
        var id = connection.Id;
        var name = connection.Name;
        PendingOperation = Task.Run(async () =>
        {
            ShellOutcome outcome;
            try
            {
                outcome = await _shellService.RunAsync(launch);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Said on the status line. Left alone, "Connecting to …" stayed up for ever.
                outcome = new ShellOutcome(-1, exception.Message);
            }

            lock (_sync)
            {
                if (outcome.Started)
                {
                    _connections.MarkConnected(id);
                    Reload();
                    SetStatus(Say("Remote.Status.SessionEnded", "Session with {0} ended (exit code {1}).", name, outcome.ExitCode), outcome.ExitCode != 0);
                }
                else
                {
                    SetStatus(outcome.Failure ?? iConsole.Localization.UiText.Get("Remote.SessionFailed", "The session did not start."), true);
                }
            }

            StateHasChanged();
        });
        StateHasChanged();
    }

    /// <summary>Opens an SSH connection in the active panel, as the SFTP drive the panels already have.</summary>
    private void Browse()
    {
        var connection = Selected;
        if (connection is null)
        {
            return;
        }

        if (connection.IsFileDrive)
        {
            var url = connection.DriveUrl;
            var id = connection.Id;
            if (connection.Validate() is { } problem)
            {
                SetStatus(problem, true);
            }
            else
            {
                _connections.MarkConnected(id);
                Reload();
                SetStatus(Say("Remote.Status.OpeningInPanel", "Opening {0} in the panel…", connection.Name), false);
                PendingOperation = Task.Run(() => _contentModelService.SetUrlAsync(url));
                Close();
            }
        }
        else if (connection.Protocol != RemoteProtocol.Ssh)
        {
            SetStatus(Say("Remote.Status.BrowseProtocols", "Only SSH, FTP and WebDAV connections can be browsed as a drive."), true);
        }
        else if (connection.Port is not null and not 22)
        {
            SetStatus(Say("Remote.Status.SshPort22", "The panels' SSH drive connects on port 22 only."), true);
        }
        else
        {
            var url = Container.SSH + connection.Destination + ":/";
            SetStatus(Say("Remote.Status.OpeningInPanel", "Opening {0} in the panel…", connection.Name), false);
            PendingOperation = Task.Run(() => _contentModelService.SetUrlAsync(url));
            Close();
        }

        StateHasChanged();
    }

    private void OpenEdit(RemoteConnection? connection)
    {
        _editing = connection;
        var source = connection ?? new RemoteConnection();
        SetBox(_name, source.Name);
        _type.SelectedIndex = Math.Max(0, Array.FindIndex(ProtocolChoices, c => c.Protocol == source.Protocol && c.Secure == (source.Protocol == RemoteProtocol.WebDav && source.Secure)));
        SetBox(_host, source.Host);
        SetBox(_port, source.Port?.ToString() ?? string.Empty);
        SetBox(_user, source.Username);
        SetBox(_keyFile, source.PrivateKeyPath ?? string.Empty);
        _transport.SelectedIndex = source.Transport == PowerShellTransport.WinRm ? 0 : 1;
        SetBox(_notes, source.Notes ?? string.Empty);
        SetBox(_password, string.Empty);
        SetBox(_folder, source.RemotePath ?? string.Empty);
        UpdateFormFields();
        _baseline = FormSnapshot();
        _confirmDiscard = false;
        _mode = Mode.Edit;
        LayoutForm();
        if (_form.Parent is null)
        {
            AddControl(_form);
        }

        // ShowAsync did these for the controls that were there when the dialog opened; a form attached later has
        // to be given its layer, its layout and its visibility by hand, or the dialog's own fill covers it.
        _form.OnInitializeZOrder();
        _form.OnInitializeLayout();
        _form.IsVisible = true;
        foreach (var field in _form.Controls.AsArray())
        {
            field.IsVisible = true;
        }

        _name.SetFocus();
        SetStatus(EditHint(), false);
        StateHasChanged();
    }

    private static void SetBox(TextBox box, string text)
    {
        box.Text = text;
        box.Select(text.Length, 0);
    }

    private void SaveEdit()
    {
        var connection = _editing?.Clone() ?? new RemoteConnection();
        connection.Name = _name.Text.Trim();
        connection.Protocol = EditProtocol;
        connection.Host = _host.Text.Trim();
        connection.Username = _user.Text.Trim();
        connection.PrivateKeyPath = NullIfEmpty(_keyFile.Text);
        connection.Transport = EditTransport;
        connection.Notes = NullIfEmpty(_notes.Text);
        connection.Secure = EditProtocol == RemoteProtocol.WebDav && EditSecure;
        connection.RemotePath = NullIfEmpty(_folder.Text);
        // Only an FTP or WebDAV entry has somewhere to keep a password; the others are asked on the terminal.
        var password = EditProtocol is RemoteProtocol.Ftp or RemoteProtocol.WebDav ? _password.Text : null;

        var portText = _port.Text.Trim();
        if (portText.Length == 0)
        {
            connection.Port = null;
        }
        else if (int.TryParse(portText, out var port))
        {
            connection.Port = port;
        }
        else
        {
            SetStatus(Say("Remote.Status.PortRange", "The port must be between 1 and 65535."), true);
            _port.SetFocus();
            StateHasChanged();
            return;
        }

        try
        {
            _connections.Save(connection, password);
            _selectedId = connection.Id;
            LeaveEdit();
            Reload();
            SetStatus(Say("Remote.Status.Saved", "Saved \"{0}\".", connection.Name), false);
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, true);
            FieldWithProblem(connection).SetFocus();
        }

        StateHasChanged();
    }

    private TextBox FieldWithProblem(RemoteConnection connection)
    {
        if (connection.Name.Length == 0)
        {
            return _name;
        }

        if (!RemoteConnection.IsValidHost(connection.Host))
        {
            return _host;
        }

        return connection.Port is null or (>= 1 and <= 65535) ? _user : _port;
    }

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private void AskDelete()
    {
        var connection = Selected;
        if (connection is null)
        {
            return;
        }

        _confirmDelete = true;
        SetStatus(Say("Remote.Status.ConfirmDelete", "Delete \"{0}\"? Press y to delete, anything else cancels.", connection.Name), true);
        StateHasChanged();
    }

    private void DeleteSelected()
    {
        var connection = Selected;
        _confirmDelete = false;
        if (connection is null)
        {
            return;
        }

        _connections.Delete(connection.Id);
        Reload();
        SetStatus(Say("Remote.Status.Deleted", "Deleted \"{0}\".", connection.Name), false);
        StateHasChanged();
    }

    /// <summary>A text in the chosen language, with its arguments filled in.</summary>
    private static string Say(string key, string english, params object[] args) => string.Format(UiText.Get(key, english), args);

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

    #endregion

    #region Drawing

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = (int)ViewSize.Height - 2;
        if (iw <= 0 || ih <= 0)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        lock (_sync)
        {
            for (var row = 0; row < ih; row++)
            {
                Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
            }

            if (_mode == Mode.List)
            {
                DrawList(buffer, iw, colorScheme);
            }
            else
            {
                DrawForm(buffer, iw, colorScheme);
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colorScheme.Error : colorScheme.Foreground, colorScheme.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints().FitHints(iw - 2), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.List => _confirmDelete ? iConsole.Localization.UiText.Get("Hint.ConfirmDelete", "y delete  any other key cancels")
            : iConsole.Localization.UiText.Get("Remote.HintList", "Enter connect  B browse files  N new  E edit  D delete  Esc close"),
        _ => iConsole.Localization.UiText.Get("Remote.HintForm", "Tab field  Left/Right type  Enter/Ctrl+S save  Esc:back"),
    };

    private void DrawList(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, Ellipsize(string.Format(iConsole.Localization.UiText.Get("Remote.Summary", "{0} saved  ·  shells ask for a password; FTP/WebDAV use the password manager"), _list.Count), iw - 2), colorScheme.Foreground, colorScheme.Background);
        Put(buffer, 1, 1, iw - 2, iConsole.Localization.UiText.Get("Remote.ListHeader", "  Type  Name                          Destination"), colorScheme.DisabledForeground, colorScheme.Background);
        if (_list.Count == 0)
        {
            // The only guidance on an empty list: wrapped over the free rows, so a narrow terminal does not cut it off.
            var emptyRow = 2;
            foreach (var part in iConsole.Localization.UiText.Get("Remote.Empty", "Nothing saved yet. N adds an SSH, PowerShell, FTP or WebDAV connection.").WrapWords(iw - 2).Take(ListRows))
            {
                Put(buffer, 1, emptyRow++, iw - 2, part, colorScheme.DisabledForeground, colorScheme.Background);
            }
        }

        var first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
        for (var i = 0; i < ListRows; i++)
        {
            var index = first + i;
            var y = 2 + i;
            if (index >= _list.Count)
            {
                continue;
            }

            var connection = _list[index];
            var highlighted = index == _cursor;
            var line = $"{TypeLabel(connection),-5} {Clip(connection.Name, 28),-28}  {connection.Destination}";
            Put(buffer, 1, y, iw - 2, (highlighted ? "> " : "  ") + line,
                highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }

        if (Selected is { } selected)
        {
            var y = 2 + ListRows + 1;
            Put(buffer, 1, y, iw - 2, string.Format(iConsole.Localization.UiText.Get("Remote.Runs", "Runs: {0}"), DescribeCommand(selected)), colorScheme.Foreground, colorScheme.Background);
            var last = selected.LastConnectedUtc is { } at ? string.Format(iConsole.Localization.UiText.Get("Remote.LastConnected", "Last connected {0:yyyy-MM-dd HH:mm}"), at.ToLocalTime()) : iConsole.Localization.UiText.Get("Remote.NeverConnected", "Never connected");
            Put(buffer, 1, y + 1, iw - 2, string.IsNullOrEmpty(selected.Notes) ? last : $"{last}  ·  {selected.Notes}", colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private static string TypeLabel(RemoteConnection connection) => connection.Protocol switch
    {
        RemoteProtocol.Ssh => "SSH",
        RemoteProtocol.Ftp => "FTP",
        RemoteProtocol.WebDav => connection.Secure ? "DAVS" : "DAV",
        _ => "PS",
    };

    private static string DescribeCommand(RemoteConnection connection)
    {
        if (connection.IsFileDrive)
            return string.Format(UiText.Get("Remote.Opens", "opens {0}"), connection.DriveUrl) + (connection.HasSavedCredential ? "  " + UiText.Get("Remote.PasswordSaved", "(password saved)") : string.Empty);
        if (!RemoteShellLaunch.TryCreate(connection, out var launch, out var error) || launch is null) return error ?? string.Empty;
        return Path.GetFileNameWithoutExtension(launch.FileName) + " " + string.Join(' ', launch.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
    }

    private void DrawForm(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        // The fields are controls of their own; the dialog draws only the heading over them.
        Put(buffer, 1, 0, iw - 2, _editing is null ? iConsole.Localization.UiText.Get("Remote.NewConnection", "New connection") : iConsole.Localization.UiText.Get("Remote.EditConnection", "Edit connection"), colorScheme.Foreground, colorScheme.Background);
    }

    private int DefaultPortFor() =>
        new RemoteConnection { Protocol = EditProtocol, Transport = EditTransport, Secure = EditSecure }.DefaultPort;

    private static string Ellipsize(string text, int width) => width > 1 && text.Length > width ? text[..(width - 1)] + "…" : text;

    private void Put(IBuffer buffer, int x, int y, int width, string text, Color foreground, Color background)
    {
        if (width <= 0)
        {
            return;
        }

        var clipped = text.Length > width ? text[..width] : text.PadRight(width);
        buffer.Write(
            ZOrder,
            new Position(AbsolutePosition.X + 1 + x, AbsolutePosition.Y + 1 + y),
            new Size(width, 1),
            new ConxelString(clipped, foreground, background));
    }

    private static string Clip(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    #endregion

    #region Test seams

    internal string ModeForTest { get { lock (_sync) return _mode.ToString(); } }
    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }
    internal int ListCountForTest { get { lock (_sync) return _list.Count; } }

    #endregion
}
