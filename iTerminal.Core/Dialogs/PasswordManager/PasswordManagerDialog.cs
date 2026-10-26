using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Controls;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Model.Passwords;
using iTerminal.Core.Services;

namespace iTerminal.Core.Dialogs.PasswordManager;

/// <summary>
/// The saved passwords: every login a network connection, a mail account or a service keeps, listed by title,
/// with the password hidden until V shows it. N adds one, Enter edits and D removes (after a y). The store is
/// unlocked when the app starts, with a key the app generated; the dialog only says so when that key is lost.
/// </summary>
public class PasswordManagerDialog : Dialog
{
    private enum Mode
    {
        Unavailable,
        List,
        Edit,
    }

    private enum FieldKind
    {
        Text,
        Password,
        Category,
    }

    private sealed class Field(string label, FieldKind kind, string text = "")
    {
        public string Label { get; } = label;
        public FieldKind Kind { get; } = kind;
        public string Text { get; set; } = text;
        public PasswordCategory Category { get; set; } = PasswordCategory.Other;
    }

    private const int DialogWidth = 80;
    private const int DialogHeight = 24;
    private const int ListRows = 14;
    private const int MaxFieldLength = 500;
    private const string Mask = "••••••••";

    private readonly IPasswordManager _passwords;
    private readonly Lock _sync = new();

    private Mode _mode = Mode.Unavailable;
    private IReadOnlyList<PasswordEntry> _list = [];
    private int _cursor;
    private string? _selectedId;
    private bool _reveal;
    private bool _confirmDelete;

    private Field[] _fields = [];
    private int _field;
    private string? _editingId;

    private string _status = string.Empty;
    private bool _statusIsError;

    /// <summary>
    /// How strong the password being typed is. Not in the control tree: this dialog paints its whole
    /// interior itself, after its children, so the meter is drawn by <see cref="DrawForm"/> at the
    /// position it computes.
    /// </summary>
    private readonly StrengthMeter _strengthMeter;

    public PasswordManagerDialog(IPasswordManager passwords, IThemeService themeService)
        : base(nameof(PasswordManagerDialog), themeService)
    {
        Title = "Passwords";
        _passwords = passwords;
        _strengthMeter = new StrengthMeter(themeService);
    }

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _reveal = false;
            _confirmDelete = false;
            if (!_passwords.IsUnlocked && !_passwords.Unlock())
            {
                _mode = Mode.Unavailable;
            }
            else if (_mode == Mode.Unavailable)
            {
                _mode = Mode.List;
                Reload();
            }
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

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_mode == Mode.Edit)
        {
            yield return ("Ctrl+S", "Save the entry");
            yield return ("Tab / Up/Down", "Move between the fields");
            yield return ("Esc", "Back to the list");
            yield break;
        }

        if (_confirmDelete)
        {
            yield return ("Y / D / Delete", "Delete the entry");
            yield return ("Any other key", "Keep the entry");
            yield break;
        }

        yield return ("Up/Down", "Choose an entry");
        yield return ("N", "Add an entry");
        yield return ("Enter/E", "Edit the entry");
        yield return ("Delete/D", "Delete the entry (asks first)");
        yield return ("V", "Show or hide the passwords");
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

                    return HandleListKey(keyEvent);
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
                OpenEdit(Selected);
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
            case 'v':
                _reveal = !_reveal;
                StateHasChanged();
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

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.R)
        {
            _reveal = !_reveal;
            StateHasChanged();
            return true;
        }

        return EditField(keyEvent);
    }

    /// <summary>Tab and the arrows move between fields; the field under the cursor takes the typing.</summary>
    private bool EditField(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent) && _fields[_field].Kind != FieldKind.Category)
        {
            _fields[_field].Text = PasteHelper.DeleteLastWord(_fields[_field].Text);
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            Field target = _fields[_field];
            if (target.Kind != FieldKind.Category)
            {
                PasteHelper.Paste(text =>
                {
                    string line = PasteHelper.SingleLine(text);
                    target.Text = (target.Text + line) is { Length: > MaxFieldLength } longer ? longer[..MaxFieldLength] : target.Text + line;
                }, StateHasChanged);
            }

            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U && _fields[_field].Kind != FieldKind.Category)
        {
            _fields[_field].Text = string.Empty;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Tab when keyEvent.Shift:
                _field = (_field - 1 + _fields.Length) % _fields.Length;
                StateHasChanged();
                return true;
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                _field = (_field + 1) % _fields.Length;
                StateHasChanged();
                return true;
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
        if (field.Kind == FieldKind.Category)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.Spacebar || keyEvent.KeyChar == ' ')
            {
                field.Category = (PasswordCategory)(((int)field.Category + 1) % Enum.GetValues<PasswordCategory>().Length);
                StateHasChanged();
            }

            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            TrimLast(field);
        }
        else
        {
            AppendPrintable(field, keyEvent);
        }

        StateHasChanged();
        return true;
    }

    private static void TrimLast(Field field)
    {
        if (field.Text.Length > 0)
        {
            field.Text = field.Text[..^1];
        }
    }

    private static void AppendPrintable(Field field, KeyEvent keyEvent)
    {
        if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && field.Text.Length < MaxFieldLength)
        {
            field.Text += keyEvent.KeyChar;
        }
    }

    #endregion

    #region Actions

    private PasswordEntry? Selected => _list.FirstOrDefault(e => e.Id == _selectedId) ?? _list.FirstOrDefault();

    private void Reload()
    {
        _list = _passwords.List();
        var index = _list.ToList().FindIndex(e => e.Id == _selectedId);
        _cursor = index >= 0 ? index : Math.Clamp(_cursor, 0, Math.Max(0, _list.Count - 1));
        _selectedId = _list.ElementAtOrDefault(_cursor)?.Id;
    }

    /// <summary>
    /// The wheel moves the highlight three entries; a click highlights the entry under the pointer. Only the list
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
            _reveal = false;
        }

        StateHasChanged();
        return true;
    }

    private void OpenEdit(PasswordEntry? entry)
    {
        _editingId = entry?.Id;
        _fields =
        [
            new Field("Title", FieldKind.Text, entry?.Title ?? string.Empty),
            new Field("Username", FieldKind.Text, entry?.Username ?? string.Empty),
            new Field("Category", FieldKind.Category) { Category = entry?.Category ?? PasswordCategory.Other },
            new Field("Password", FieldKind.Password, entry?.Password ?? string.Empty),
            new Field("Notes", FieldKind.Text, entry?.Notes ?? string.Empty),
        ];
        _field = 0;
        _mode = Mode.Edit;
        _reveal = false;
        SetStatus(entry is null ? "A new saved password. Ctrl+S saves it, Esc goes back." : "Ctrl+S saves, Ctrl+R shows the password, Esc goes back.", false);
        StateHasChanged();
    }

    private void SaveEdit()
    {
        var title = _fields[0].Text.Trim();
        if (title.Length == 0)
        {
            SetStatus("Give the password a title, so it can be found in the list.", true);
            _field = 0;
            StateHasChanged();
            return;
        }

        try
        {
            _passwords.Save(new PasswordEntry
            {
                Id = _editingId ?? Guid.NewGuid().ToString("N"),
                Title = title,
                Username = _fields[1].Text.Trim(),
                Category = _fields[2].Category,
                Password = _fields[3].Text,
                Notes = _fields[4].Text,
            });
            _selectedId = _editingId;
            _mode = Mode.List;
            Reload();
            SetStatus($"Saved \"{title}\".", false);
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, true);
        }

        StateHasChanged();
    }

    private void AskDelete()
    {
        var entry = Selected;
        if (entry is null)
        {
            return;
        }

        _confirmDelete = true;
        SetStatus($"Delete \"{entry.Title}\"? Press y to delete, anything else cancels.", true);
        StateHasChanged();
    }

    private void DeleteSelected()
    {
        var entry = Selected;
        _confirmDelete = false;
        if (entry is null)
        {
            return;
        }

        try
        {
            _passwords.Delete(entry.Id);
            Reload();
            SetStatus($"Deleted \"{entry.Title}\".", false);
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, true);
        }

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

            switch (_mode)
            {
                case Mode.Unavailable:
                    Put(buffer, 1, 1, iw - 2, "The saved passwords cannot be opened.", colorScheme.Foreground, colorScheme.Background);
                    Put(buffer, 1, 2, iw - 2, "The key they were sealed with is gone from this account, so they cannot be read again.", colorScheme.DisabledForeground, colorScheme.Background);
                    break;
                case Mode.List:
                    DrawList(buffer, iw, colorScheme);
                    break;
                case Mode.Edit:
                    DrawForm(buffer, iw, "Saved password", colorScheme);
                    break;
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colorScheme.Error : Color.Cyan, colorScheme.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints(), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.Unavailable => "Esc close",
        Mode.List => _confirmDelete ? "y delete  any other key cancels"
            : "Enter edit  N new  D delete  V show  Esc close",
        _ => "Tab field  Space category  Ctrl+R show password  Ctrl+S save  Esc back",
    };

    private void DrawList(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, $"{_list.Count} saved  ·  unlocked", Color.Cyan, colorScheme.Background);
        Put(buffer, 1, 1, iw - 2, "  Category    Title                            Username", colorScheme.DisabledForeground, colorScheme.Background);
        if (_list.Count == 0)
        {
            Put(buffer, 1, 2, iw - 2, "Nothing saved yet. N adds a password; network and mail passwords appear here as they are saved.", colorScheme.DisabledForeground, colorScheme.Background);
        }

        var first = _cursor >= ListRows ? _cursor - ListRows + 1 : 0;
        for (var i = 0; i < ListRows; i++)
        {
            var index = first + i;
            var y = 2 + i;
            if (index >= _list.Count)
            {
                Put(buffer, 1, y, iw - 2, string.Empty, colorScheme.Foreground, colorScheme.Background);
                continue;
            }

            var entry = _list[index];
            var highlighted = index == _cursor;
            var line = $"{Clip(entry.Category.ToString(), 10),-10}  {Clip(entry.Title, 30),-30}  {entry.Username}";
            Put(buffer, 1, y, iw - 2, (highlighted ? "> " : "  ") + line,
                highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }

        var selected = Selected;
        if (selected is not null)
        {
            var password = _reveal ? selected.Password : Mask;
            Put(buffer, 1, 2 + ListRows + 1, iw - 2, $"Password: {password}", colorScheme.Foreground, colorScheme.Background);
            Put(buffer, 1, 2 + ListRows + 2, iw - 2, string.IsNullOrEmpty(selected.Notes) ? string.Empty : $"Notes: {selected.Notes}", colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawForm(IBuffer buffer, int iw, string heading, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, heading, Color.Cyan, colorScheme.Background);
        for (var i = 0; i < _fields.Length; i++)
        {
            DrawField(buffer, iw, 2 + i * 2, _fields[i], i == _field, colorScheme);
        }

        // The rating sits on the blank row under the password, as wide as the value column.
        var passwordIndex = Array.FindIndex(_fields, f => f.Kind == FieldKind.Password);
        if (passwordIndex >= 0 && iw > 18)
        {
            _strengthMeter.Password = _fields[passwordIndex].Text;
            _strengthMeter.ZOrder = ZOrder;
            _strengthMeter.Position = new Position(AbsolutePosition.X + 1 + 17, AbsolutePosition.Y + 1 + 2 + passwordIndex * 2 + 1);
            _strengthMeter.ViewSize = new Size(iw - 18, 1);
            _strengthMeter.Draw(buffer);
        }
    }

    private void DrawField(IBuffer buffer, int iw, int row, Field field, bool focused, ColorScheme colorScheme)
    {
        Put(buffer, 1, row, 16, field.Label, focused ? Color.Cyan : colorScheme.DisabledForeground, colorScheme.Background);
        var value = field.Kind switch
        {
            FieldKind.Category => field.Category.ToString() + "  (Space changes)",
            FieldKind.Password when !_reveal => new string('*', field.Text.Length),
            _ => field.Text,
        };
        Put(buffer, 17, row, iw - 18, value + (focused && field.Kind != FieldKind.Category ? "_" : string.Empty), colorScheme.Foreground,
            focused ? colorScheme.SelectedBackground : colorScheme.Background);
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

    internal string ModeForTest { get { lock (_sync) return _mode.ToString(); } }
    internal string StatusForTest { get { lock (_sync) return _status; } }
    internal int CursorForTest { get { lock (_sync) return _cursor; } }
    internal int ListCountForTest { get { lock (_sync) return _list.Count; } }
    internal PasswordStrength StrengthForTest => _strengthMeter.Strength;
    internal bool RevealForTest { get { lock (_sync) return _reveal; } }
    internal string FieldForTest(string label) { lock (_sync) return _fields.First(f => f.Label == label).Text; }

    #endregion
}
