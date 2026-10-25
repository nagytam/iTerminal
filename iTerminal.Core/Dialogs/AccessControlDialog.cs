using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.AccessControl;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Who may do what to a file or folder: the access control list, its owner, and whether the parent's entries
/// flow down. Opened from the Properties dialog (P). Windows shows NTFS entries — allow and deny, each as Full
/// control, Modify, Read &amp; execute, Read, Write or "Special" — and POSIX systems show the owner, the group, named
/// users and groups, the mask and a folder's default entries.
///
/// <para>Keys: ↑/↓ choose an entry; <b>A</b> or Insert adds one; <b>Enter</b> or <b>E</b> changes the chosen one's rights
/// (Space or ←/→ cycles them); <b>Del</b> or <b>D</b> removes it after a y; <b>I</b> turns inheritance on or off
/// (Windows); <b>O</b> changes the owner (and group); <b>R</b> reads the list again.</para>
///
/// <para>Every read and write runs on a background task: a directory security read or <c>setfacl</c> can take a
/// while on a network share, and a key handler gets 200 ms. A result is written only if it still answers the
/// latest request (<see cref="_generation"/>).</para>
/// </summary>
public class AccessControlDialog : Dialog
{
    private enum Mode
    {
        List,
        Edit,
        Owner,
    }

    private const int DialogWidth = 96;
    private const int DialogHeight = 24;
    private const int ListRows = 10;
    private const int MaxFieldLength = 128;

    private readonly IAccessControlService _service;
    private readonly Lock _sync = new();

    private string _path = string.Empty;
    private AclInfo? _info;
    private int _generation;
    private int _cursor;
    private Mode _mode = Mode.List;
    private bool _confirmDelete;
    private bool _busy;
    private string _status = string.Empty;
    private bool _statusIsError;

    // Edit form
    private string _principal = string.Empty;
    private AclKind _kind;
    private int _preset;
    private bool _inheritToChildren = true;
    private bool _adding;
    private int _field;

    private enum EditField
    {
        Who,
        Type,
        Rights,
        Applies,
    }

    /// <summary>The fields of the form: Type only where an entry can refuse, Applies to only for a folder.</summary>
    private List<EditField> EditFields()
    {
        List<EditField> fields = [EditField.Who];
        if (_info is { SupportsDeny: true })
        {
            fields.Add(EditField.Type);
        }

        fields.Add(EditField.Rights);
        if (_info is { IsFolder: true })
        {
            fields.Add(EditField.Applies);
        }

        return fields;
    }

    // Owner form
    private string _ownerText = string.Empty;
    private string _groupText = string.Empty;
    private int _ownerField;

    public AccessControlDialog(IAccessControlService service, IThemeService themeService)
        : base(nameof(AccessControlDialog), themeService)
    {
        Title = "Permissions";
        _service = service;
    }

    /// <summary>The read or change the last key started. Nothing waits on it; it exists so a test can.</summary>
    public Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>Sets the local file or folder; call before showing the dialog.</summary>
    public void Load(string path)
    {
        lock (_sync)
        {
            _path = path;
            _info = null;
            _cursor = 0;
            _mode = Mode.List;
            _confirmDelete = false;
            SetStatus(string.Empty, false);
            StartRead("Reading the permissions…");
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(Math.Min(DialogWidth, Math.Max(50, (int)Parent.ViewSize.Width - 4)), DialogHeight));
        }

        base.OnInitializeLayout();
    }

    #region Work

    /// <summary>Reads the list on a worker; the dialog says so until it lands.</summary>
    private void StartRead(string message)
    {
        var generation = ++_generation;
        var path = _path;
        _busy = true;
        SetStatus(message, false);
        PendingOperation = Task.Run(() =>
        {
            AclInfo? info = null;
            string? failure = null;
            try
            {
                info = _service.Read(path);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                failure = exception.Message;
            }

            lock (_sync)
            {
                if (generation != _generation)
                {
                    return;
                }

                _busy = false;
                if (info is not null)
                {
                    _info = info;
                    _cursor = Math.Clamp(_cursor, 0, Math.Max(0, info.Entries.Count - 1));
                    SetStatus(info.Note ?? string.Empty, info.Note is not null);
                }
                else
                {
                    SetStatus("Error: " + failure, true);
                }
            }

            StateHasChanged();
        });
        StateHasChanged();
    }

    /// <summary>Runs one change on a worker, then reads the list again so what is shown is what the disk says.</summary>
    private void StartChange(Action<IAccessControlService> change, string done)
    {
        var generation = ++_generation;
        var path = _path;
        _busy = true;
        SetStatus("Working…", false);
        PendingOperation = Task.Run(() =>
        {
            string? failure = null;
            try
            {
                change(_service);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                failure = exception.Message;
            }

            AclInfo? info = null;
            string? readFailure = null;
            try
            {
                info = _service.Read(path);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                readFailure = exception.Message;
            }

            lock (_sync)
            {
                if (generation != _generation)
                {
                    return;
                }

                _busy = false;
                if (info is not null)
                {
                    _info = info;
                    _cursor = Math.Clamp(_cursor, 0, Math.Max(0, info.Entries.Count - 1));
                }

                if (failure is not null)
                {
                    SetStatus("Error: " + failure, true);
                }
                else if (readFailure is not null)
                {
                    SetStatus("Changed, but the list could not be read again: " + readFailure, true);
                }
                else
                {
                    SetStatus(done, false);
                }
            }

            StateHasChanged();
        });
        StateHasChanged();
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException;

    #endregion

    #region Keys

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
                case Mode.Owner:
                    return HandleOwnerKey(keyEvent);
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private AclEntry? Selected => _info is { } info && _cursor >= 0 && _cursor < info.Entries.Count ? info.Entries[_cursor] : null;

    private bool HandleListKey(KeyEvent keyEvent)
    {
        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        var answersDelete = keyEvent.ConsoleKey == ConsoleKey.Delete || char.ToLowerInvariant(keyEvent.KeyChar) is 'd' or 'y';
        if (!answersDelete)
        {
            _confirmDelete = false;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                return Move(-1);
            case ConsoleKey.DownArrow when plain:
                return Move(1);
            case ConsoleKey.PageUp when plain:
                return Move(-ListRows);
            case ConsoleKey.PageDown when plain:
                return Move(ListRows);
            case ConsoleKey.Home when plain:
                return Move(-int.MaxValue / 2);
            case ConsoleKey.End when plain:
                return Move(int.MaxValue / 2);
            case ConsoleKey.Insert when plain:
                OpenEdit(adding: true);
                return true;
            case ConsoleKey.Enter when plain:
                OpenEdit(adding: false);
                return true;
            case ConsoleKey.Delete when plain:
                AskDelete();
                return true;
        }

        if (!plain || _info is null)
        {
            return false;
        }

        switch (char.ToLowerInvariant(keyEvent.KeyChar))
        {
            case 'a':
                OpenEdit(adding: true);
                return true;
            case 'e':
                OpenEdit(adding: false);
                return true;
            case 'd':
                AskDelete();
                return true;
            case 'y' when _confirmDelete:
                DeleteSelected();
                return true;
            case 'i':
                ToggleInheritance();
                return true;
            case 'o':
                OpenOwner();
                return true;
            case 'r':
                StartRead("Reading the permissions…");
                return true;
        }

        return false;
    }

    private bool Move(int delta)
    {
        _confirmDelete = false;
        if (_info is { Entries.Count: > 0 } info)
        {
            _cursor = Math.Clamp(_cursor + delta, 0, info.Entries.Count - 1);
        }

        StateHasChanged();
        return true;
    }

    private void OpenEdit(bool adding)
    {
        if (_info is not { } info || _busy)
        {
            return;
        }

        var entry = Selected;
        if (!adding)
        {
            if (entry is null)
            {
                SetStatus("Nothing to change. A adds an entry.", true);
                StateHasChanged();
                return;
            }

            if (entry.Inherited)
            {
                SetStatus("That entry is inherited from the parent folder. Change it there, or press I to stop inheriting.", true);
                StateHasChanged();
                return;
            }

            if (!entry.IsPreset)
            {
                SetStatus("These are special rights; they can be removed but not edited here. Remove it and add a preset.", true);
                StateHasChanged();
                return;
            }
        }

        _adding = adding;
        _principal = adding ? string.Empty : entry!.Principal;
        _kind = adding ? AclKind.Allow : entry!.Kind;
        _preset = adding ? Math.Max(0, info.Presets.ToList().FindIndex(p => p == "Read" || p == "r--")) : Math.Max(0, PresetIndex(info, entry!));
        _inheritToChildren = info.IsFolder;
        _field = adding ? 0 : EditFields().IndexOf(EditField.Rights);
        _mode = Mode.Edit;
        SetStatus(adding
            ? (info.SupportsDeny ? "Type a user or group, such as Users or DOMAIN\\name." : "A user, or group:name. Bare owner, group, other and mask work too.")
            : "Space or ←/→ changes the rights. Enter saves, Esc goes back.", false);
        StateHasChanged();
    }

    private static int PresetIndex(AclInfo info, AclEntry entry)
    {
        // The Unix list adds "(effective …)" after the triple; the longest preset the rights start with is the preset.
        var best = -1;
        for (var i = 0; i < info.Presets.Count; i++)
        {
            var preset = info.Presets[i];
            if ((entry.Rights == preset || entry.Rights.StartsWith(preset + " ", StringComparison.Ordinal))
                && (best < 0 || preset.Length > info.Presets[best].Length))
            {
                best = i;
            }
        }

        return best;
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

        if (keyEvent.ConsoleKey == ConsoleKey.Enter || (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.S))
        {
            SaveEdit();
            return true;
        }

        var fields = EditFields();
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Tab when !keyEvent.Shift:
            case ConsoleKey.DownArrow:
                _field = (_field + 1) % fields.Count;
                StateHasChanged();
                return true;
            case ConsoleKey.Tab:
            case ConsoleKey.UpArrow:
                _field = (_field - 1 + fields.Count) % fields.Count;
                StateHasChanged();
                return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return true;
        }

        var info = _info!;
        var toggles = IsSpace(keyEvent) || keyEvent.ConsoleKey is ConsoleKey.LeftArrow or ConsoleKey.RightArrow;
        switch (fields[_field])
        {
            case EditField.Who:
                // An existing entry keeps its principal: the name is what identifies it.
                if (_adding)
                {
                    _principal = EditText(_principal, keyEvent);
                }

                break;
            case EditField.Type:
                if (toggles)
                {
                    _kind = _kind == AclKind.Allow ? AclKind.Deny : AclKind.Allow;
                }

                break;
            case EditField.Rights:
                CyclePreset(keyEvent, info);
                break;
            case EditField.Applies:
                if (toggles)
                {
                    _inheritToChildren = !_inheritToChildren;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    private void CyclePreset(KeyEvent keyEvent, AclInfo info)
    {
        var step = keyEvent.ConsoleKey == ConsoleKey.LeftArrow ? -1 : IsSpace(keyEvent) || keyEvent.ConsoleKey == ConsoleKey.RightArrow ? 1 : 0;
        if (step != 0)
        {
            _preset = (_preset + step + info.Presets.Count) % info.Presets.Count;
        }
    }

    private static bool IsSpace(KeyEvent keyEvent) => keyEvent.ConsoleKey == ConsoleKey.Spacebar || keyEvent.KeyChar == ' ';

    /// <summary>Typing, Backspace, Ctrl+U, Ctrl+W and a paste, for the text field under the cursor.</summary>
    private string EditText(string text, KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            return text.Length > 0 ? text[..^1] : text;
        }

        if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && text.Length < MaxFieldLength)
        {
            return text + keyEvent.KeyChar;
        }

        return text;
    }

    private void SaveEdit()
    {
        if (_info is not { } info)
        {
            return;
        }

        var principal = _principal.Trim();
        if (principal.Length == 0)
        {
            SetStatus("Give the user or group first.", true);
            _field = 0;
            StateHasChanged();
            return;
        }

        var preset = info.Presets[_preset];
        var kind = info.SupportsDeny ? _kind : AclKind.Allow;
        var inherit = info.IsFolder && _inheritToChildren;
        var path = _path;
        _mode = Mode.List;
        StartChange(service => service.Set(path, principal, kind, preset, inherit), $"{principal}: {preset}.");
    }

    private void AskDelete()
    {
        if (Selected is not { } entry)
        {
            return;
        }

        if (entry.Inherited)
        {
            SetStatus("That entry is inherited from the parent folder; remove it there.", true);
            StateHasChanged();
            return;
        }

        _confirmDelete = true;
        SetStatus($"Remove the entry for {entry.Principal}? Press y to remove, anything else cancels.", true);
        StateHasChanged();
    }

    private void DeleteSelected()
    {
        _confirmDelete = false;
        if (Selected is not { } entry || _busy)
        {
            return;
        }

        var path = _path;
        StartChange(service => service.Remove(path, entry.Principal, entry.Kind), $"Removed the entry for {entry.Principal}.");
    }

    private void ToggleInheritance()
    {
        if (_info is not { } info || _busy)
        {
            return;
        }

        if (!info.CanChangeInheritance)
        {
            SetStatus("This system's permissions have no inheritance switch.", true);
            StateHasChanged();
            return;
        }

        var path = _path;
        var inherit = !info.Inherits;
        StartChange(service => service.SetInheritance(path, inherit),
            inherit ? "Entries from the parent folder apply again." : "Stopped inheriting; the parent's entries were kept as explicit copies.");
    }

    private void OpenOwner()
    {
        if (_info is not { } info || _busy)
        {
            return;
        }

        if (!info.CanChangeOwner)
        {
            SetStatus("The owner cannot be changed here.", true);
            StateHasChanged();
            return;
        }

        _ownerText = info.Owner is "(unknown)" ? string.Empty : info.Owner;
        _groupText = info.Group is null or "(unknown)" ? string.Empty : info.Group;
        _ownerField = 0;
        _mode = Mode.Owner;
        SetStatus(info.Group is null
            ? "Type the new owner. Changing it needs the right to take ownership (an administrator)."
            : "The new owner and/or group. Changing the owner needs root; blank leaves one alone.", false);
        StateHasChanged();
    }

    private bool HandleOwnerKey(KeyEvent keyEvent)
    {
        var hasGroup = _info?.Group is not null;
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.List;
            SetStatus(string.Empty, false);
            StateHasChanged();
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter || (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.S))
        {
            var path = _path;
            var owner = _ownerText.Trim();
            var group = hasGroup ? _groupText.Trim() : null;
            // Unchanged is left out, so the tool is not asked to chown to what it already is.
            var currentOwner = _info!.Owner;
            var currentGroup = _info.Group;
            var newOwner = owner == currentOwner ? null : owner;
            var newGroup = group == currentGroup ? null : group;
            _mode = Mode.List;
            if (string.IsNullOrEmpty(newOwner) && string.IsNullOrEmpty(newGroup))
            {
                SetStatus("The owner is unchanged.", false);
                StateHasChanged();
                return true;
            }

            StartChange(service => service.SetOwner(path, newOwner, newGroup), "Owner changed.");
            return true;
        }

        if ((keyEvent.ConsoleKey is ConsoleKey.Tab or ConsoleKey.DownArrow or ConsoleKey.UpArrow) && hasGroup)
        {
            _ownerField = 1 - _ownerField;
            StateHasChanged();
            return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return true;
        }

        if (_ownerField == 0)
        {
            _ownerText = EditText(_ownerText, keyEvent);
        }
        else
        {
            _groupText = EditText(_groupText, keyEvent);
        }

        StateHasChanged();
        return true;
    }

    #endregion

    #region Mouse

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode == Mode.List && !_confirmDelete && _info is { Entries.Count: > 0 })
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                lock (_sync)
                {
                    return Move(mouseEvent.WheelDelta > 0 ? -3 : 3);
                }
            }

            if (mouseEvent.LeftClicked)
            {
                long row = mouseEvent.Position.Y - (AbsolutePosition.Y + ListTop + 2);
                long column = mouseEvent.Position.X - (AbsolutePosition.X + 2);
                lock (_sync)
                {
                    var first = FirstRow();
                    if (row >= 0 && row < ListRows && column >= 0 && column < ViewSize.Width - 4 && first + (int)row < _info.Entries.Count)
                    {
                        return Move(first + (int)row - _cursor);
                    }
                }
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    #endregion

    #region Drawing

    /// <summary>Rows above the column header: path, owner, inheritance.</summary>
    private const int ListTop = 3;

    private int FirstRow() => _cursor >= ListRows ? _cursor - ListRows + 1 : 0;

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
    }

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

        var colors = ThemeService.CurrentColorScheme;
        lock (_sync)
        {
            for (var row = 0; row < ih; row++)
            {
                Put(buffer, 0, row, iw, string.Empty, colors.Foreground, colors.Background);
            }

            switch (_mode)
            {
                case Mode.List:
                    DrawList(buffer, iw, colors);
                    break;
                case Mode.Edit:
                    DrawEdit(buffer, iw, colors);
                    break;
                default:
                    DrawOwner(buffer, iw, colors);
                    break;
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colors.Error : Color.Cyan, colors.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints(), colors.Foreground, colors.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.List => _confirmDelete ? "y remove  any other key cancels"
            : "A add  Enter change  Del remove  O owner" + (_info is { CanChangeInheritance: true } ? "  I inherit" : string.Empty) + "  R reread  Esc close",
        Mode.Edit => "Tab field  Space/←→ change  Enter save  Esc back",
        _ => "Tab field  Enter save  Esc back",
    };

    private void DrawList(IBuffer buffer, int iw, ColorScheme colors)
    {
        Put(buffer, 1, 0, iw - 2, _path.Shrink(iw - 2) ?? string.Empty, Color.Cyan, colors.Background);
        if (_info is not { } info)
        {
            Put(buffer, 1, 2, iw - 2, _busy ? "Reading…" : string.Empty, colors.DisabledForeground, colors.Background);
            return;
        }

        var ownerLine = info.Group is null ? $"Owner: {info.Owner}" : $"Owner: {info.Owner}   Group: {info.Group}";
        Put(buffer, 1, 1, iw - 2, ownerLine, colors.Foreground, colors.Background);
        Put(buffer, 1, 2, iw - 2,
            info.CanChangeInheritance
                ? (info.Inherits ? "Inheriting entries from the parent folder" : "Not inheriting: only the entries below apply")
                : (info.IsFolder ? "A folder's default entries are what new items start with" : string.Empty),
            colors.DisabledForeground, colors.Background);
        Put(buffer, 1, ListTop, iw - 2, "  " + (info.SupportsDeny ? "Type  " : string.Empty) + "Who".PadRight(26) + "Rights".PadRight(22) + "Applies to", colors.DisabledForeground, colors.Background);
        if (info.Entries.Count == 0)
        {
            Put(buffer, 1, ListTop + 1, iw - 2, "No entries: nobody but the owner and administrators has access.", colors.DisabledForeground, colors.Background);
        }

        var first = FirstRow();
        for (var i = 0; i < ListRows; i++)
        {
            var index = first + i;
            if (index >= info.Entries.Count)
            {
                continue;
            }

            var entry = info.Entries[index];
            var highlighted = index == _cursor;
            var scope = entry.Inherited ? "inherited" + (entry.Scope is null ? string.Empty : ", " + entry.Scope) : entry.Scope ?? string.Empty;
            var line = (highlighted ? "> " : "  ")
                + (info.SupportsDeny ? (entry.Kind == AclKind.Allow ? "Allow " : "Deny  ") : string.Empty)
                + Clip(entry.Principal, 24).PadRight(26)
                + Clip(entry.Rights, 20).PadRight(22)
                + scope;
            var foreground = highlighted ? colors.SelectedFocusedForeground
                : entry.Kind == AclKind.Deny ? colors.Error
                : entry.Inherited ? colors.DisabledForeground
                : colors.Foreground;
            Put(buffer, 1, ListTop + 1 + i, iw - 2, line, foreground, highlighted ? colors.SelectedFocusedBackground : colors.Background);
        }
    }

    private void DrawEdit(IBuffer buffer, int iw, ColorScheme colors)
    {
        var info = _info!;
        Put(buffer, 1, 0, iw - 2, _adding ? "Add an entry" : "Change an entry", Color.Cyan, colors.Background);
        var fields = EditFields();
        for (var i = 0; i < fields.Count; i++)
        {
            var focused = i == _field;
            var (label, value, editable) = fields[i] switch
            {
                EditField.Who => ("Who", _principal, _adding),
                EditField.Type => ("Type", (_kind == AclKind.Allow ? "Allow" : "Deny") + "  (Space changes)", false),
                EditField.Rights => ("Rights", info.Presets[_preset] + "  (Space or ←/→ changes)", false),
                _ => ("Applies to",
                    (_inheritToChildren
                        ? (info.SupportsDeny ? "this folder, subfolders and files" : "this folder, and new items (default entry)")
                        : "this folder only") + "  (Space changes)", false),
            };
            var row = 2 + i * 2;
            Put(buffer, 1, row, 14, label, focused ? Color.Cyan : colors.DisabledForeground, colors.Background);
            Put(buffer, 15, row, iw - 16, value + (focused && editable ? "_" : string.Empty),
                colors.Foreground, focused ? colors.SelectedBackground : colors.Background);
        }
    }

    private void DrawOwner(IBuffer buffer, int iw, ColorScheme colors)
    {
        Put(buffer, 1, 0, iw - 2, "Change the owner", Color.Cyan, colors.Background);
        Put(buffer, 1, 2, 14, "Owner", _ownerField == 0 ? Color.Cyan : colors.DisabledForeground, colors.Background);
        Put(buffer, 15, 2, iw - 16, _ownerText + (_ownerField == 0 ? "_" : string.Empty), colors.Foreground, _ownerField == 0 ? colors.SelectedBackground : colors.Background);
        if (_info?.Group is not null)
        {
            Put(buffer, 1, 4, 14, "Group", _ownerField == 1 ? Color.Cyan : colors.DisabledForeground, colors.Background);
            Put(buffer, 15, 4, iw - 16, _groupText + (_ownerField == 1 ? "_" : string.Empty), colors.Foreground, _ownerField == 1 ? colors.SelectedBackground : colors.Background);
        }
    }

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

    internal AclInfo? InfoForTest { get { lock (_sync) return _info; } }
    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal string ModeForTest { get { lock (_sync) return _mode.ToString(); } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }

    #endregion
}
