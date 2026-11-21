using iConsole.Helper;
using iConsole.Localization;
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
    private const int MaxListRows = 14;

    /// <summary>All 14 rows on a full-height dialog, fewer on a short terminal so the details, the status and the hints stay inside the frame.</summary>
    private int ListRows => Math.Clamp((int)ViewSize.Height - 2 - 8, 3, MaxListRows);
    private const int MaxFieldLength = 500;
    private const string Mask = "••••••••";

    private readonly IPasswordManager _passwords;
    private readonly Lock _sync = new();

    /// <summary>The entries the list shows. Only the UI thread writes it, from Reload.</summary>
    private readonly ConcurrentObservableCollection<PasswordEntry> _rows = [];

    /// <summary>
    /// The list. Its cursor is the only cursor: the keys move it through <see cref="MoveCursor"/>, and a click or the
    /// wheel moves it through the table itself. Only columns that hold no secret are shown; the password is in the detail line.
    /// </summary>
    private readonly Table<PasswordEntry> _table;

    private Mode _mode = Mode.Unavailable;
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
        Title = iConsole.Localization.UiText.Get("Dialog.Passwords", "Passwords");
        _passwords = passwords;
        _strengthMeter = new StrengthMeter(themeService);
        _table = new Table<PasswordEntry>(_rows, themeService)
        {
            // Two sentences, wrapped by the table to its width, as the empty list used to show them on two lines.
            EmptyText = iConsole.Localization.UiText.Get("Passwords.NothingSaved", "Nothing saved yet. N adds a password;") + " "
                + iConsole.Localization.UiText.Get("Passwords.NothingSaved2", "network and mail passwords appear here as they are saved."),
        };
        _table.Columns.Add(new ColumnDefinition<PasswordEntry>(iConsole.Localization.UiText.Get("Passwords.ColCategory", "Category"), 10, entry => Clip(entry.Category.ToString(), 10)));
        _table.Columns.Add(new ColumnDefinition<PasswordEntry>(iConsole.Localization.UiText.Get("Passwords.ColTitle", "Title"), 30, entry => Clip(entry.Title, 30)));
        _table.Columns.Add(new ColumnDefinition<PasswordEntry>(iConsole.Localization.UiText.Get("Passwords.ColUsername", "Username"), 24, entry => entry.Username));
        AddControl(_table);
    }

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _reveal = false;
            _confirmDelete = false;
            if (!_passwords.IsUnlocked && !_passwords.Unlock())
            {
                SetMode(Mode.Unavailable);
            }
            else if (_mode == Mode.Unavailable)
            {
                SetMode(Mode.List);
                Reload();
            }
        }

        await base.ShowAsync();

        // ShowAsync makes every child visible, so the list is hidden again here when another mode is on.
        SyncListVisibility();

        // The list was read before the dialog had its size, so the cursor is placed again now that the view has one.
        lock (_sync)
        {
            SetCursor(Cursor);
        }
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            CenterInParent(new Size(DialogWidth, DialogHeight));
        }

        base.OnInitializeLayout();

        // The list sits under the count line and the header, and has the rows the dialog height leaves room for.
        _table.Position = new Position(1, 2);
        _table.ViewSize = new Size(Math.Max(0, ViewSize.Width - 4), ListRows + 1);
        _table.ScrollableModel.ViewSize = new Size(_table.ViewSize.Width, ListRows);
    }

    /// <summary>Changes the mode and shows the list only while it is the mode on screen.</summary>
    private void SetMode(Mode mode)
    {
        _mode = mode;
        SyncListVisibility();
    }

    private void SyncListVisibility() => _table.IsVisible = _mode == Mode.List;

    /// <summary>The entry under the cursor, or null when the list is empty.</summary>
    private PasswordEntry? Selected => _table.SelectedRow;

    private int Cursor => _table.CursorRow;

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_mode == Mode.Edit)
        {
            yield return ("Ctrl+S", UiText.Get("DialogHint.SaveEntry", "Save the entry"));
            yield return ("Tab / Up/Down", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
            yield return ("Esc", UiText.Get("DialogHint.BackToList", "Back to the list"));
            yield break;
        }

        if (_confirmDelete)
        {
            yield return ("Y / D / Delete", UiText.Get("DialogHint.DeleteEntry", "Delete the entry"));
            yield return ("Any other key", UiText.Get("DialogHint.KeepEntry", "Keep the entry"));
            yield break;
        }

        yield return ("Up/Down", UiText.Get("DialogHint.ChooseEntry", "Choose an entry"));
        yield return ("N", UiText.Get("DialogHint.AddEntry", "Add an entry"));
        yield return ("Enter/E", UiText.Get("DialogHint.EditEntry", "Edit the entry"));
        yield return ("Delete/D", UiText.Get("DialogHint.DeleteEntryAsks", "Delete the entry (asks first)"));
        yield return ("V", UiText.Get("DialogHint.ShowHidePasswords", "Show or hide the passwords"));
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
            SetMode(Mode.List);
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

    /// <summary>
    /// Reads the list again. The entry with <paramref name="keepId"/> is highlighted when it is there; otherwise the
    /// cursor keeps its row, clamped to the list.
    /// </summary>
    private void Reload(string? keepId = null)
    {
        var cursor = Cursor;
        var entries = _passwords.List();
        _rows.ReplaceAll(entries.ToList());
        var index = keepId is null ? -1 : entries.ToList().FindIndex(e => e.Id == keepId);
        SetCursor(index >= 0 ? index : Math.Clamp(cursor, 0, Math.Max(0, entries.Count - 1)));
    }

    private void SetCursor(int index)
    {
        _table.ScrollableModel.CursorPosition.Y = Math.Clamp(index, 0, Math.Max(0, _rows.Count - 1));
        _table.ScrollableModel.ScrollToVisible();
    }

    /// <summary>
    /// The wheel and a click are the table's own (three rows a notch, a click highlights the row under the pointer, and a
    /// second click on the highlighted row opens nothing, as before). While "delete?" is on screen the list does not answer.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode == Mode.List && _confirmDelete)
        {
            return true;
        }

        var before = Cursor;
        var handled = await base.HandleMouseEventAsync(mouseEvent);
        if (_mode == Mode.List && Cursor != before)
        {
            // A new highlight hides the password again, as a keyboard move does.
            _reveal = false;
            StateHasChanged();
        }

        return handled;
    }

    private bool MoveCursor(int delta)
    {
        _confirmDelete = false;
        if (_rows.Count > 0)
        {
            SetCursor(Cursor + delta);
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
        SetMode(Mode.Edit);
        _reveal = false;
        SetStatus(entry is null ? Say("PasswordManager.Status.NewEntry", "A new saved password. Ctrl+S saves it, Esc goes back.") : Say("PasswordManager.Status.Editing", "Ctrl+S saves, Ctrl+R shows the password, Esc goes back."), false);
        StateHasChanged();
    }

    private void SaveEdit()
    {
        var title = _fields[0].Text.Trim();
        if (title.Length == 0)
        {
            SetStatus(Say("PasswordManager.Status.NeedTitle", "Give the password a title, so it can be found in the list."), true);
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
            SetMode(Mode.List);
            Reload(_editingId);
            SetStatus(Say("PasswordManager.Status.Saved", "Saved \"{0}\".", title), false);
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
        SetStatus(Say("PasswordManager.Status.ConfirmDelete", "Delete \"{0}\"? Press y to delete, anything else cancels.", entry.Title), true);
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
            SetStatus(Say("PasswordManager.Status.Deleted", "Deleted \"{0}\".", entry.Title), false);
        }
        catch (InvalidOperationException exception)
        {
            SetStatus(exception.Message, true);
        }

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

            switch (_mode)
            {
                case Mode.Unavailable:
                    Put(buffer, 1, 1, iw - 2, iConsole.Localization.UiText.Get("Passwords.CannotOpen", "The saved passwords cannot be opened."), colorScheme.Foreground, colorScheme.Background);
                    Put(buffer, 1, 2, iw - 2, iConsole.Localization.UiText.Get("Passwords.KeyGone", "The key they were sealed with is gone from this account, so they cannot be read again."), colorScheme.DisabledForeground, colorScheme.Background);
                    break;
                case Mode.List:
                    DrawList(buffer, iw, colorScheme);
                    break;
                case Mode.Edit:
                    DrawForm(buffer, iw, iConsole.Localization.UiText.Get("Passwords.SavedPassword", "Saved password"), colorScheme);
                    break;
            }

            Put(buffer, 1, ih - 2, iw - 2, _status, _statusIsError ? colorScheme.Error : colorScheme.Foreground, colorScheme.Background);
            Put(buffer, 1, ih - 1, iw - 2, Hints().FitHints(iw - 2), colorScheme.Foreground, colorScheme.Background);
        }
    }

    private string Hints() => _mode switch
    {
        Mode.Unavailable => iConsole.Localization.UiText.Get("Passwords.HintUnavailable", "Esc close"),
        Mode.List => _confirmDelete ? iConsole.Localization.UiText.Get("Hint.ConfirmDelete", "y delete  any other key cancels")
            : iConsole.Localization.UiText.Get("Passwords.HintList", "Enter edit  N new  D delete  V show  Esc close"),
        _ => iConsole.Localization.UiText.Get("Passwords.HintEdit", "Tab field  Space category  Ctrl+R show password  Ctrl+S save  Esc back"),
    };

    private void DrawList(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        // The count line; the table below paints its own header, rows and empty text over its whole rectangle.
        Put(buffer, 1, 0, iw - 2, string.Format(UiText.Get("Passwords.ListHeader", "{0} saved  ·  unlocked"), _rows.Count), colorScheme.Foreground, colorScheme.Background);

        var selected = Selected;
        if (selected is not null)
        {
            var password = _reveal ? selected.Password : Mask;
            Put(buffer, 1, 2 + ListRows + 1, iw - 2, string.Format(iConsole.Localization.UiText.Get("Passwords.Password", "Password: {0}"), password), colorScheme.Foreground, colorScheme.Background);
            Put(buffer, 1, 2 + ListRows + 2, iw - 2, string.IsNullOrEmpty(selected.Notes) ? string.Empty : string.Format(iConsole.Localization.UiText.Get("Passwords.Notes", "Notes: {0}"), selected.Notes), colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawForm(IBuffer buffer, int iw, string heading, ColorScheme colorScheme)
    {
        Put(buffer, 1, 0, iw - 2, heading, colorScheme.Foreground, colorScheme.Background);
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
    internal int CursorForTest { get { lock (_sync) return Cursor; } }
    internal int ListCountForTest { get { lock (_sync) return _rows.Count; } }

    /// <summary>The list, so a test can draw it and read back its rows.</summary>
    internal Table<PasswordEntry> TableForTest => _table;
    internal PasswordStrength StrengthForTest => _strengthMeter.Strength;
    internal bool RevealForTest { get { lock (_sync) return _reveal; } }
    internal string FieldForTest(string label) { lock (_sync) return _fields.First(f => f.Label == label).Text; }

    #endregion
}
