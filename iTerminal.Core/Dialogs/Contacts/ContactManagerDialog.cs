using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Helper;
using iTerminal.Core.Model.Contacts;

namespace iTerminal.Core.Dialogs.Contacts;

/// <summary>
/// The contact manager: name, email, phone, company and notes for the people the person keeps in touch with.
/// N (or Insert) adds a contact and E (F2 or Enter) changes the highlighted one, each field in turn with the
/// whole contact shown under the list; D removes one after a y/n; / filters the list as it is typed (every word
/// must appear in some field). The highlight follows the contact itself, so it stays on it when the list is
/// re-sorted or filtered.
/// </summary>
public class ContactManagerDialog : Dialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 24;
    private const int ListRows = 12;
    private const int MaxFieldLength = 120;
    private const int FieldCount = 5;
    private const int FieldsTop = 14;
    private const int NameColumn = 20;
    private const int EmailColumn = 24;
    private const int PhoneColumn = 15;
    private const int LabelWidth = 10;

    private static readonly string[] FieldLabels = ["Name", "Email", "Phone", "Company", "Notes"];

    private enum Mode
    {
        Browse,
        Form,
        Filter,
        ConfirmDelete,
    }

    private readonly ContactStore _store;

    private Mode _mode = Mode.Browse;

    /// <summary>The contact the highlight is on. Kept by identity, not by row, so it survives a re-sort.</summary>
    private Guid? _selectedId;

    private string _filter = string.Empty;

    /// <summary>The five fields being typed while the form is open.</summary>
    private readonly string[] _values = new string[FieldCount];
    private int _field;
    private string? _error;

    /// <summary>The contact the form is changing, or null when it is typing a new one.</summary>
    private Contact? _editing;

    public ContactManagerDialog(ContactStore store, IThemeService themeService)
        : base(nameof(ContactManagerDialog), themeService)
    {
        Title = UiText.Get("Dialog.ContactsTitle", "Contacts");
        _store = store;
    }

    /// <summary>The dialog is built once and opened many times, so it starts from the plain list each time.</summary>
    public override async Task ShowAsync()
    {
        _mode = Mode.Browse;
        _filter = string.Empty;
        _error = null;
        _editing = null;
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

    private IReadOnlyList<Contact> Visible => _store.Find(_filter);

    private Contact? Selected
    {
        get
        {
            var visible = Visible;
            return visible.FirstOrDefault(c => c.Id == _selectedId) ?? visible.FirstOrDefault();
        }
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        switch (_mode)
        {
            case Mode.Form:
                yield return ("Enter/Tab", UiText.Get("DialogHint.NextFieldEnterSaves", "Next field; Enter on the last one saves"));
                yield return ("Shift+Tab", UiText.Get("DialogHint.PreviousField", "Previous field"));
                yield return ("Up/Down", UiText.Get("DialogHint.MoveBetweenFields", "Move between the fields"));
                yield return ("Esc", UiText.Get("Hint.DiscardChanges", "Discard the changes"));
                yield break;
            case Mode.Filter:
                yield return ("Enter", UiText.Get("DialogHint.KeepFilterBackToList", "Keep the filter and go back to the list"));
                yield return ("Esc", UiText.Get("DialogHint.ClearFilter", "Clear the filter"));
                yield break;
            case Mode.ConfirmDelete:
                yield return ("Y", UiText.Get("DialogHint.DeleteContact", "Delete the contact"));
                yield return ("Any other key", UiText.Get("DialogHint.KeepContact", "Keep the contact"));
                yield break;
        }

        yield return ("Up/Down", UiText.Get("DialogHint.MoveOverContacts", "Move over the contacts"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MovePageAtATime", "Move a page at a time"));
        yield return ("Home/End", UiText.Get("DialogHint.JumpFirstLastContact", "Jump to the first or last contact"));
        yield return ("Insert/N", UiText.Get("DialogHint.AddContact", "Add a contact"));
        yield return ("Enter/F2/E", UiText.Get("DialogHint.ChangeContact", "Change the contact"));
        yield return ("Delete/D", UiText.Get("DialogHint.DeleteContactAsks", "Delete the contact (asks first)"));
        yield return ("/", UiText.Get("DialogHint.FilterList", "Filter the list"));
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        switch (_mode)
        {
            case Mode.Form:
                return HandleFormKey(keyEvent);
            case Mode.Filter:
                return HandleFilterKey(keyEvent);
            case Mode.ConfirmDelete:
                // Esc is the dialog's own: it leaves the question as it closes the list. Any other key but y withdraws it.
                if (keyEvent.ConsoleKey != ConsoleKey.Escape)
                {
                    _mode = Mode.Browse;
                    if (keyEvent.ConsoleKey == ConsoleKey.Y && !keyEvent.Control && !keyEvent.Alt)
                    {
                        DeleteSelected();
                    }

                    StateHasChanged();
                    return true;
                }

                _mode = Mode.Browse;
                break;
        }

        // The slash is Shift+7 on some layouts, so it is read by its character, not as a plain key.
        if (keyEvent.KeyChar == '/' && !keyEvent.Control && !keyEvent.Alt)
        {
            _mode = Mode.Filter;
            StateHasChanged();
            return true;
        }

        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.PageUp when plain:
                return MoveSelection(-ListRows);
            case ConsoleKey.PageDown when plain:
                return MoveSelection(ListRows);
            case ConsoleKey.UpArrow when plain:
                return MoveSelection(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveSelection(1);
            case ConsoleKey.Home when plain:
                return SelectAt(0);
            case ConsoleKey.End when plain:
                return SelectAt(Visible.Count - 1);
            case ConsoleKey.Insert when plain:
                OpenForm(null);
                return true;
            case ConsoleKey.Enter when plain:
            case ConsoleKey.F2 when plain:
                OpenForm(Selected);
                return true;
            case ConsoleKey.Delete when plain:
                AskToDelete();
                return true;
            case ConsoleKey.Escape when plain && _filter.Length > 0:
                // With a filter up, Esc first puts the whole list back; the next Esc closes the dialog.
                _filter = string.Empty;
                StateHasChanged();
                return true;
        }

        if (plain)
        {
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'n':
                    OpenForm(null);
                    return true;
                case 'e':
                    OpenForm(Selected);
                    return true;
                case 'd':
                    AskToDelete();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>While the form is up every key belongs to it; Esc abandons the changes rather than closing the list.</summary>
    private bool HandleFormKey(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            _values[_field] = PasteHelper.DeleteLastWord(_values[_field]);
            _error = null;
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                var joined = _values[_field] + PasteHelper.SingleLine(text);
                _values[_field] = joined.Length > MaxFieldLength ? joined[..MaxFieldLength] : joined;
                _error = null;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            _values[_field] = string.Empty;
            _error = null;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                _error = null;
                _editing = null;
                break;
            case ConsoleKey.Enter:
                if (_field == FieldCount - 1)
                {
                    CommitForm();
                }
                else
                {
                    _field++;
                }

                break;
            case ConsoleKey.Tab when keyEvent.Shift:
            case ConsoleKey.UpArrow:
                _field = (_field + FieldCount - 1) % FieldCount;
                break;
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                _field = (_field + 1) % FieldCount;
                break;
            case ConsoleKey.Backspace when _values[_field].Length > 0:
                _values[_field] = _values[_field][..^1];
                _error = null;
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && _values[_field].Length < MaxFieldLength)
                {
                    _values[_field] += keyEvent.KeyChar;
                    _error = null;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>The filter narrows the list with every key typed, and Enter keeps it while the list is walked.</summary>
    private bool HandleFilterKey(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            _filter = PasteHelper.DeleteLastWord(_filter);
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                var joined = _filter + PasteHelper.SingleLine(text);
                _filter = joined.Length > MaxFieldLength ? joined[..MaxFieldLength] : joined;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            _filter = string.Empty;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _filter = string.Empty;
                _mode = Mode.Browse;
                break;
            case ConsoleKey.Enter:
                _mode = Mode.Browse;
                break;
            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                break;
            case ConsoleKey.DownArrow:
                MoveSelection(1);
                break;
            case ConsoleKey.Backspace when _filter.Length > 0:
                _filter = _filter[..^1];
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && _filter.Length < MaxFieldLength)
                {
                    _filter += keyEvent.KeyChar;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>
    /// The wheel moves the highlight three contacts and a click highlights the contact under the pointer. Nothing
    /// answers the mouse while the form is up or a deletion is being asked.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode is Mode.Browse or Mode.Filter)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return MoveSelection(mouseEvent.WheelDelta > 0 ? -3 : 3);
            }

            if (mouseEvent.LeftClicked && ContactAt(mouseEvent.Position) is { } contact)
            {
                _selectedId = contact.Id;
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>The first listed contact, which the list scrolls to keep the highlight in view.</summary>
    private int FirstVisibleIndex(IReadOnlyList<Contact> visible)
    {
        var selectedIndex = visible.ToList().FindIndex(c => c.Id == Selected?.Id);
        return selectedIndex >= ListRows ? selectedIndex - ListRows + 1 : 0;
    }

    private Contact? ContactAt(Position position)
    {
        long row = position.Y - (AbsolutePosition.Y + 2);
        long column = position.X - (AbsolutePosition.X + 2);
        if (row < 0 || row >= ListRows || column < 0 || column >= ViewSize.Width - 4)
        {
            return null;
        }

        var visible = Visible;
        int index = FirstVisibleIndex(visible) + (int)row;
        return index < visible.Count ? visible[index] : null;
    }

    /// <summary>Opens the form on <paramref name="contact"/>, or empty for a new one. A change with nothing highlighted does nothing.</summary>
    private void OpenForm(Contact? contact)
    {
        _editing = contact;
        _values[0] = contact?.Name ?? string.Empty;
        _values[1] = contact?.Email ?? string.Empty;
        _values[2] = contact?.Phone ?? string.Empty;
        _values[3] = contact?.Company ?? string.Empty;
        _values[4] = contact?.Notes ?? string.Empty;
        _field = 0;
        _error = null;
        _mode = Mode.Form;
        StateHasChanged();
    }

    private void CommitForm()
    {
        var id = _editing?.Id ?? Guid.NewGuid();
        var contact = new Contact(id, _values[0], _values[1], _values[2], _values[3], _values[4]).Trimmed();
        if (!contact.Validate(out var error))
        {
            _error = error;
            _field = error.Contains("email", StringComparison.OrdinalIgnoreCase) ? 1
                : error.Contains("phone", StringComparison.OrdinalIgnoreCase) ? 2
                : 0;
            return;
        }

        if (_editing is not null)
        {
            _store.Update(contact);
        }
        else
        {
            _store.Add(contact);
        }

        // A new contact the filter would hide is shown anyway: the person just typed it and expects to see it.
        if (!contact.Matches(_filter))
        {
            _filter = string.Empty;
        }

        _selectedId = id;
        _editing = null;
        _error = null;
        _mode = Mode.Browse;
    }

    private void AskToDelete()
    {
        if (Selected is not null)
        {
            _mode = Mode.ConfirmDelete;
            StateHasChanged();
        }
    }

    /// <summary>Removes the highlighted contact and moves the highlight to the one that took its row.</summary>
    private void DeleteSelected()
    {
        var visible = Visible;
        var contact = Selected;
        if (contact is null)
        {
            return;
        }

        var index = visible.ToList().FindIndex(c => c.Id == contact.Id);
        _store.Remove(contact.Id);
        var remaining = Visible;
        _selectedId = (remaining.ElementAtOrDefault(index) ?? remaining.ElementAtOrDefault(index - 1))?.Id;
        StateHasChanged();
    }

    private bool MoveSelection(int delta)
    {
        var index = Visible.ToList().FindIndex(c => c.Id == Selected?.Id);
        return SelectAt(index + delta);
    }

    private bool SelectAt(int index)
    {
        var visible = Visible;
        if (visible.Count == 0)
        {
            return true;
        }

        _selectedId = visible[Math.Clamp(index, 0, visible.Count - 1)].Id;
        StateHasChanged();
        return true;
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

        var colorScheme = ThemeService.CurrentColorScheme;
        for (var row = 0; row < ih; row++)
        {
            Put(buffer, 0, row, iw, string.Empty, colorScheme.Foreground, colorScheme.Background);
        }

        DrawHeader(buffer, iw, colorScheme);
        DrawRows(buffer, iw, colorScheme);
        DrawFields(buffer, iw, colorScheme);
        DrawMessage(buffer, iw, colorScheme);
        Put(buffer, 1, ih - 1, iw - 2, FooterText(), colorScheme.Foreground, colorScheme.Background);
    }

    private string FooterText() => _mode switch
    {
        Mode.Form => "Enter next field  Tab/Shift+Tab move  Esc discard",
        Mode.Filter => "Type to filter  Enter keep  Esc clear",
        _ => "N new  E change  D delete  / filter  Esc close",
    };

    private void DrawHeader(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var total = _store.Count;
        var shown = Visible.Count;
        var count = _filter.Length > 0 ? $"{shown} of {total} contacts" : $"{total} contacts";
        string header = Fit("Name", NameColumn) + Fit("Email", EmailColumn) + Fit("Phone", PhoneColumn) + "Company";
        Put(buffer, 1, 0, iw - 2, header, Color.Cyan, colorScheme.Background);

        // The count sits at the right edge and must not paint over the last column title: the short form
        // ("3/10", "10") takes over where the words do not fit beside it.
        string compact = _filter.Length > 0 ? $"{shown}/{total}" : total.ToString();
        string? fitting = new[] { count, compact }.FirstOrDefault(text => iw - text.Length - 1 > 1 + header.Length);
        if (fitting is not null)
        {
            Put(buffer, iw - fitting.Length - 1, 0, fitting.Length, fitting, colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawRows(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var visible = Visible;
        var selectedId = Selected?.Id;
        if (visible.Count == 0)
        {
            var empty = _filter.Length > 0 ? $"No contact matches \"{_filter}\"." : "No contacts. Press N to add one.";
            Put(buffer, 1, 1, iw - 2, empty, colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        var first = FirstVisibleIndex(visible);
        for (var i = 0; i < ListRows; i++)
        {
            var y = 1 + i;
            var index = first + i;
            if (index >= visible.Count)
            {
                Put(buffer, 1, y, iw - 2, string.Empty, colorScheme.Foreground, colorScheme.Background);
                continue;
            }

            var contact = visible[index];
            var highlighted = contact.Id == selectedId;
            var foreground = highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            var background = highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
            var line = Fit(contact.Name, NameColumn) + Fit(contact.Email, EmailColumn) + Fit(contact.Phone, PhoneColumn) + contact.Company;
            Put(buffer, 1, y, iw - 2, line, foreground, background);
        }
    }

    /// <summary>The five fields under the list: the highlighted contact's, or the ones being typed with a cursor on the active one.</summary>
    private void DrawFields(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var contact = Selected;
        for (var i = 0; i < FieldCount; i++)
        {
            var label = Fit(FieldLabels[i] + ":", LabelWidth);
            var inForm = _mode == Mode.Form;
            var value = inForm
                ? _values[i] + (i == _field ? "_" : string.Empty)
                : contact is null ? string.Empty : FieldOf(contact, i);
            var active = inForm && i == _field;
            Put(buffer, 1, FieldsTop + i, LabelWidth, label, colorScheme.DisabledForeground, colorScheme.Background);
            Put(buffer, 1 + LabelWidth, FieldsTop + i, iw - 2 - LabelWidth, value,
                active ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground, colorScheme.Background);
        }
    }

    private void DrawMessage(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var y = FieldsTop + FieldCount;
        if (_mode == Mode.ConfirmDelete && Selected is { } doomed)
        {
            Put(buffer, 1, y, iw - 2, string.Format(iConsole.Localization.UiText.Get("Prompt.DeleteNamed", "Delete \"{0}\"? (y/n)"), doomed.Name), colorScheme.Error, colorScheme.Background);
        }
        else if (_mode == Mode.Form)
        {
            Put(buffer, 1, y, iw - 2, _error ?? $"{(_editing is null ? "New" : "Change")} contact: only the name is needed", _error is null ? colorScheme.DisabledForeground : colorScheme.Error, colorScheme.Background);
        }
        else if (_mode == Mode.Filter)
        {
            Put(buffer, 1, y, iw - 2, string.Format(iConsole.Localization.UiText.Get("Contacts.FilterInput", "Filter: {0}_"), _filter), colorScheme.SelectedFocusedForeground, colorScheme.Background);
        }
        else if (_filter.Length > 0)
        {
            Put(buffer, 1, y, iw - 2, string.Format(iConsole.Localization.UiText.Get("Contacts.FilterHint", "Filter: {0}  (Esc shows every contact)"), _filter), colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private static string FieldOf(Contact contact, int field) => field switch
    {
        0 => contact.Name,
        1 => contact.Email,
        2 => contact.Phone,
        3 => contact.Company,
        _ => contact.Notes,
    };

    /// <summary>Clips to <paramref name="width"/> - 1 and pads to <paramref name="width"/>, so neighbouring columns never touch.</summary>
    private static string Fit(string text, int width) =>
        (text.Length > width - 1 ? text[..(width - 1)] : text).PadRight(width);

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

    #region Test seams

    internal bool IsFormOpenForTest => _mode == Mode.Form;
    internal bool ConfirmingDeleteForTest => _mode == Mode.ConfirmDelete;
    internal bool IsFilteringForTest => _mode == Mode.Filter;
    internal string? ErrorForTest => _error;
    internal string FilterForTest => _filter;
    internal Contact? SelectedForTest => Selected;

    #endregion
}
