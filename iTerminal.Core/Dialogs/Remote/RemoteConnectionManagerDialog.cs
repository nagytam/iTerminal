using iTerminal.Core.Helper;
using iConsole.Buffers;
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
/// The remote connections: SSH hosts and PowerShell remoting targets, saved by name. Enter hands the whole
/// terminal to the session (<see cref="IInteractiveShellService"/>) — a real <c>ssh</c> or
/// <c>Enter-PSSession</c> with its prompt, colours and Ctrl+C — and <c>exit</c> comes back here. B opens an
/// SSH host in the active panel as an SFTP drive. N adds, E edits and D removes (after a y).
///
/// <para>Passwords are never stored: the session asks for them on the terminal it was lent.</para>
/// </summary>
public class RemoteConnectionManagerDialog : Dialog
{
    private enum Mode
    {
        List,
        Edit,
    }

    private enum FieldKind
    {
        Text,
        Number,
        Protocol,
        Transport,
    }

    private sealed class Field(string label, FieldKind kind, string text = "")
    {
        public string Label { get; } = label;
        public FieldKind Kind { get; } = kind;
        public string Text { get; set; } = text;
    }

    private const int DialogWidth = 84;
    private const int DialogHeight = 24;
    private const int ListRows = 12;
    private const int MaxFieldLength = 300;

    private const int NameField = 0;
    private const int ProtocolField = 1;
    private const int HostField = 2;
    private const int PortField = 3;
    private const int UserField = 4;
    private const int KeyField = 5;
    private const int TransportField = 6;
    private const int NotesField = 7;

    private readonly IRemoteConnectionService _connections;
    private readonly IInteractiveShellService _shellService;
    private readonly IContentModelService _contentModelService;
    private readonly Lock _sync = new();

    private Mode _mode = Mode.List;
    private IReadOnlyList<RemoteConnection> _list = [];
    private int _cursor;
    private string? _selectedId;
    private bool _confirmDelete;

    private Field[] _fields = [];
    private int _field;
    private RemoteProtocol _editProtocol;
    private PowerShellTransport _editTransport;
    private RemoteConnection? _editing;

    private string _status = string.Empty;
    private bool _statusIsError;

    public RemoteConnectionManagerDialog(
        IRemoteConnectionService connections,
        IInteractiveShellService shellService,
        IContentModelService contentModelService,
        IThemeService themeService)
        : base(nameof(RemoteConnectionManagerDialog), themeService)
    {
        Title = "Remote connections";
        _connections = connections;
        _shellService = shellService;
        _contentModelService = contentModelService;
    }

    /// <summary>The session or panel navigation the last key started. Nothing waits on it; it exists so a test can.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _mode = Mode.List;
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

        base.OnInitializeLayout();
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
                    return HandleEditKey(keyEvent);
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
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.List;
            SetStatus(string.Empty, false);
            StateHasChanged();
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.S)
        {
            SaveEdit();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            SaveEdit();
            return true;
        }

        return EditField(keyEvent);
    }

    /// <summary>Tab and the arrows move between fields; the field under the cursor takes the typing.</summary>
    private bool EditField(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent) && _fields[_field].Kind is not (FieldKind.Protocol or FieldKind.Transport))
        {
            _fields[_field].Text = PasteHelper.DeleteLastWord(_fields[_field].Text);
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent) && _fields[_field].Kind is not (FieldKind.Protocol or FieldKind.Transport))
        {
            Field target = _fields[_field];
            PasteHelper.Paste(text =>
            {
                string line = PasteHelper.SingleLine(text);
                if (target.Kind == FieldKind.Number)
                {
                    line = new string([.. line.Where(char.IsAsciiDigit)]);
                }

                target.Text = (target.Text + line) is { Length: > MaxFieldLength } longer ? longer[..MaxFieldLength] : target.Text + line;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U && _fields[_field].Kind is not (FieldKind.Protocol or FieldKind.Transport))
        {
            _fields[_field].Text = string.Empty;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Tab when !keyEvent.Shift:
            case ConsoleKey.DownArrow:
                _field = (_field + 1) % _fields.Length;
                StateHasChanged();
                return true;
            case ConsoleKey.Tab:
            case ConsoleKey.UpArrow:
                _field = (_field - 1 + _fields.Length) % _fields.Length;
                StateHasChanged();
                return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return true;
        }

        var field = _fields[_field];
        switch (field.Kind)
        {
            case FieldKind.Protocol:
                if (IsSpace(keyEvent))
                {
                    // The other type's fields keep their text; they are just not used.
                    _editProtocol = _editProtocol == RemoteProtocol.Ssh ? RemoteProtocol.PowerShell : RemoteProtocol.Ssh;
                }

                break;
            case FieldKind.Transport:
                if (IsSpace(keyEvent) && _editProtocol == RemoteProtocol.PowerShell)
                {
                    _editTransport = _editTransport == PowerShellTransport.WinRm ? PowerShellTransport.Ssh : PowerShellTransport.WinRm;
                }

                break;
            default:
                if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
                {
                    if (field.Text.Length > 0)
                    {
                        field.Text = field.Text[..^1];
                    }
                }
                else if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && field.Text.Length < MaxFieldLength
                         && (field.Kind != FieldKind.Number || char.IsAsciiDigit(keyEvent.KeyChar)))
                {
                    field.Text += keyEvent.KeyChar;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    private static bool IsSpace(KeyEvent keyEvent) => keyEvent.ConsoleKey == ConsoleKey.Spacebar || keyEvent.KeyChar == ' ';

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
            SetStatus("Nothing to connect to yet. N adds a connection.", true);
            StateHasChanged();
            return;
        }

        if (!RemoteShellLaunch.TryCreate(connection, out var launch, out var error) || launch is null)
        {
            SetStatus(error ?? "This connection cannot be started.", true);
            StateHasChanged();
            return;
        }

        if (_shellService.IsRunning)
        {
            SetStatus("A shell is already running.", true);
            StateHasChanged();
            return;
        }

        SetStatus($"Connecting to {connection.Name}…", false);
        var id = connection.Id;
        var name = connection.Name;
        PendingOperation = Task.Run(async () =>
        {
            var outcome = await _shellService.RunAsync(launch);
            lock (_sync)
            {
                if (outcome.Started)
                {
                    _connections.MarkConnected(id);
                    Reload();
                    SetStatus($"Session with {name} ended (exit code {outcome.ExitCode}).", outcome.ExitCode != 0);
                }
                else
                {
                    SetStatus(outcome.Failure ?? "The session did not start.", true);
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

        if (connection.Protocol != RemoteProtocol.Ssh)
        {
            SetStatus("Only SSH connections can be browsed as a drive.", true);
        }
        else if (connection.Port is not null and not 22)
        {
            SetStatus("The panels' SSH drive connects on port 22 only.", true);
        }
        else
        {
            var url = Container.SSH + connection.Destination + ":/";
            SetStatus($"Opening {connection.Name} in the panel…", false);
            PendingOperation = Task.Run(() => _contentModelService.SetUrlAsync(url));
            Close();
        }

        StateHasChanged();
    }

    private void OpenEdit(RemoteConnection? connection)
    {
        _editing = connection;
        var source = connection ?? new RemoteConnection();
        _editProtocol = source.Protocol;
        _editTransport = source.Transport;
        _fields =
        [
            new Field("Name", FieldKind.Text, source.Name),
            new Field("Type", FieldKind.Protocol),
            new Field("Host", FieldKind.Text, source.Host),
            new Field("Port", FieldKind.Number, source.Port?.ToString() ?? string.Empty),
            new Field("User", FieldKind.Text, source.Username),
            new Field("Key file", FieldKind.Text, source.PrivateKeyPath ?? string.Empty),
            new Field("Transport", FieldKind.Transport),
            new Field("Notes", FieldKind.Text, source.Notes ?? string.Empty),
        ];
        _field = 0;
        _mode = Mode.Edit;
        SetStatus(connection is null ? "A new connection. Enter or Ctrl+S saves it, Esc goes back." : "Enter or Ctrl+S saves, Esc goes back.", false);
        StateHasChanged();
    }

    private void SaveEdit()
    {
        var connection = _editing?.Clone() ?? new RemoteConnection();
        connection.Name = _fields[NameField].Text.Trim();
        connection.Protocol = _editProtocol;
        connection.Host = _fields[HostField].Text.Trim();
        connection.Username = _fields[UserField].Text.Trim();
        connection.PrivateKeyPath = NullIfEmpty(_fields[KeyField].Text);
        connection.Transport = _editTransport;
        connection.Notes = NullIfEmpty(_fields[NotesField].Text);

        var portText = _fields[PortField].Text.Trim();
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
            SetStatus("The port must be between 1 and 65535.", true);
            _field = PortField;
            StateHasChanged();
            return;
        }

        try
        {
            _connections.Save(connection);
            _selectedId = connection.Id;
            _mode = Mode.List;
            Reload();
            SetStatus($"Saved \"{connection.Name}\".", false);
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, true);
            _field = FieldWithProblem(connection);
        }

        StateHasChanged();
    }

    private static int FieldWithProblem(RemoteConnection connection)
    {
        if (connection.Name.Length == 0)
        {
            return NameField;
        }

        if (!RemoteConnection.IsValidHost(connection.Host))
        {
            return HostField;
        }

        return connection.Port is null or (>= 1 and <= 65535) ? UserField : PortField;
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
        SetStatus($"Delete \"{connection.Name}\"? Press y to delete, anything else cancels.", true);
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
        SetStatus($"Deleted \"{connection.Name}\".", false);
        StateHasChanged();
    }

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

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colorScheme.Error : Color.Cyan, colorScheme.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints(), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.List => _confirmDelete ? "y delete  any other key cancels"
            : "Enter connect  B browse files  N new  E edit  D delete  Esc close",
        _ => "Tab field  Space changes type/transport  Enter save  Esc back",
    };

    private void DrawList(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, $"{_list.Count} saved  ·  passwords are asked for by the session, never stored", Color.Cyan, colorScheme.Background);
        Put(buffer, 1, 1, iw - 2, "  Type  Name                          Destination", colorScheme.DisabledForeground, colorScheme.Background);
        if (_list.Count == 0)
        {
            Put(buffer, 1, 2, iw - 2, "Nothing saved yet. N adds an SSH or PowerShell connection.", colorScheme.DisabledForeground, colorScheme.Background);
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
            Put(buffer, 1, y, iw - 2, $"Runs: {DescribeCommand(selected)}", colorScheme.Foreground, colorScheme.Background);
            var last = selected.LastConnectedUtc is { } at ? $"Last connected {at.ToLocalTime():yyyy-MM-dd HH:mm}" : "Never connected";
            Put(buffer, 1, y + 1, iw - 2, string.IsNullOrEmpty(selected.Notes) ? last : $"{last}  ·  {selected.Notes}", colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private static string TypeLabel(RemoteConnection connection) => connection.Protocol == RemoteProtocol.Ssh ? "SSH" : "PS";

    private static string DescribeCommand(RemoteConnection connection) =>
        RemoteShellLaunch.TryCreate(connection, out var launch, out var error) && launch is not null
            ? Path.GetFileNameWithoutExtension(launch.FileName) + " " + string.Join(' ', launch.Arguments.Select(a => a.Contains(' ') ? $"\"{a}\"" : a))
            : error ?? string.Empty;

    private void DrawForm(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, _editing is null ? "New connection" : "Edit connection", Color.Cyan, colorScheme.Background);
        for (var i = 0; i < _fields.Length; i++)
        {
            var field = _fields[i];
            var focused = i == _field;
            var unused = (i == TransportField && _editProtocol != RemoteProtocol.PowerShell)
                         || (i == KeyField && !(_editProtocol == RemoteProtocol.Ssh || _editTransport == PowerShellTransport.Ssh));
            var row = 2 + i * 2;
            Put(buffer, 1, row, 16, field.Label, focused ? Color.Cyan : colorScheme.DisabledForeground, colorScheme.Background);
            var value = field.Kind switch
            {
                FieldKind.Protocol => (_editProtocol == RemoteProtocol.Ssh ? "SSH" : "PowerShell remoting") + "  (Space changes)",
                FieldKind.Transport => _editProtocol == RemoteProtocol.PowerShell
                    ? (_editTransport == PowerShellTransport.WinRm ? "WinRM (Windows)" : "SSH (PowerShell 7)") + "  (Space changes)"
                    : "only for PowerShell",
                FieldKind.Number when field.Text.Length == 0 => $"(default {DefaultPortFor()})",
                _ => field.Text,
            };
            var editable = field.Kind is FieldKind.Text or FieldKind.Number;
            Put(buffer, 17, row, iw - 18, value + (focused && editable ? "_" : string.Empty),
                unused || (field.Kind == FieldKind.Number && field.Text.Length == 0) ? colorScheme.DisabledForeground : colorScheme.Foreground,
                focused ? colorScheme.SelectedBackground : colorScheme.Background);
        }
    }

    private int DefaultPortFor() =>
        new RemoteConnection { Protocol = _editProtocol, Transport = _editTransport }.DefaultPort;

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
