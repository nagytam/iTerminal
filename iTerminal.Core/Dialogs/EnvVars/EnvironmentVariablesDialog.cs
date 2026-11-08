using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Localization;
using iConsole.Services;
using iTerminal.Core.Helper;
using iTerminal.Core.Services.SystemSettings;

namespace iTerminal.Core.Dialogs.EnvVars;

/// <summary>
/// The environment variables editor (Ctrl+Shift+E). Three scopes, cycled with S: <b>Session</b> is the
/// environment of this running iTerminal (what the programs it starts inherit; gone when it closes),
/// <b>User</b> is the signed-in user's saved environment and <b>Machine</b> is every user's (administrator
/// rights; Windows and Linux). Where the user's environment lives depends on the system — the registry on
/// Windows, systemd's <c>environment.d</c> on Linux, <c>~/.zprofile</c> on a Mac — and
/// <see cref="ISystemSettingsService"/> owns that.
///
/// <para>N or Insert adds a variable (name, then value), E, F2 or Enter changes the value of the highlighted one,
/// D or Delete removes it after a y/n, / filters by name or value, R reads the list again.</para>
///
/// <para>Nothing is read or written from a key handler or from <c>Draw</c>. A read or a change runs on a task and
/// leaves its outcome for <see cref="OnIdle"/>, which takes it on the UI thread; only the newest read may land,
/// and one change runs at a time. The form checks the name and value first, so a refusal keeps it open.</para>
/// </summary>
public class EnvironmentVariablesDialog : Dialog
{
    public enum EditScope
    {
        Session,
        User,
        Machine,
    }

    private const int DialogWidth = 84;
    private const int DialogHeight = 26;
    private const int ListRows = 15;
    private const int MaxFieldLength = 4000;
    private const int NameColumn = 30;
    private const int LabelWidth = 8;
    private const int NoteRows = 2;
    private const int HeaderRow = 1 + NoteRows;
    private const int ListTop = HeaderRow + 1;

    private enum Mode
    {
        Browse,
        Form,
        Filter,
        ConfirmDelete,
    }

    /// <summary>What a task leaves behind: new rows, a status line, and whether the list must be read again.</summary>
    private sealed record Outcome(int Generation, IReadOnlyList<SystemEnvironmentVariable>? Rows, string? Status, bool IsError, bool Reload);

    private readonly ISystemSettingsService _settings;
    private readonly ISessionEnvironment _session;
    private readonly object _outcomeLock = new();

    private Mode _mode = Mode.Browse;
    private EditScope _scope = EditScope.Session;
    private IReadOnlyList<SystemEnvironmentVariable> _rows = [];
    private bool _loaded;
    private string _filter = string.Empty;
    private string? _selectedName;

    private string _formName = string.Empty;
    private string _formValue = string.Empty;
    private int _field;
    private string? _editingName;
    private string? _formError;

    private string _status = string.Empty;
    private bool _statusIsError;

    private List<SystemEnvironmentVariable>? _visible;
    private IReadOnlyList<SystemEnvironmentVariable>? _visibleRows;
    private string _visibleFilter = string.Empty;

    private Outcome? _parked;
    private int _generation;
    private volatile bool _busy;

    public EnvironmentVariablesDialog(ISystemSettingsService settings, ISessionEnvironment session, IThemeService themeService)
        : base(nameof(EnvironmentVariablesDialog), themeService)
    {
        _settings = settings;
        _session = session;
        Title = UiText.Get("Dialog.EnvVarsTitle", "Environment Variables");
    }

    /// <summary>The read the dialog last started. Nothing awaits it — a test can.</summary>
    internal Task LoadTask { get; private set; } = Task.CompletedTask;

    /// <summary>The change last started. Nothing awaits it — a test can.</summary>
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;

    /// <summary>The dialog is built once and opened many times, so every opening starts from a fresh read.</summary>
    public override async Task ShowAsync()
    {
        _mode = Mode.Browse;
        _filter = string.Empty;
        _selectedName = null;
        _rows = [];
        _formError = null;
        _editingName = null;
        _status = string.Empty;
        _statusIsError = false;
        _scope = _settings.SupportsEnvironment(EnvironmentScope.User) ? EditScope.User : EditScope.Session;
        lock (_outcomeLock)
        {
            // What a task of the previous opening left behind belongs to that opening.
            _parked = null;
        }

        StartLoad();
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

    public override void OnIdle()
    {
        Outcome? outcome;
        lock (_outcomeLock)
        {
            outcome = _parked;
            _parked = null;
        }

        if (outcome is not null)
        {
            Apply(outcome);
        }

        base.OnIdle();
    }

    private void Apply(Outcome outcome)
    {
        // A read that was overtaken by a newer one (the scope changed, a change finished) is dropped; its status
        // and reload request still count.
        if (outcome.Rows is not null && outcome.Generation == Volatile.Read(ref _generation))
        {
            _rows = outcome.Rows;
            _loaded = true;
        }

        if (outcome.Status is not null)
        {
            _status = outcome.Status;
            _statusIsError = outcome.IsError;
        }

        if (outcome.Reload)
        {
            StartLoad();
        }

        StateHasChanged();
    }

    private void Park(Outcome outcome)
    {
        lock (_outcomeLock)
        {
            // A read that was overtaken must not replace the one that overtook it. Two quick scope switches start two reads;
            // when the older finished last it used to overwrite the newer, Apply then dropped it as stale, and the list stayed empty.
            if (_parked is { Rows: not null } newer && outcome.Rows is not null && outcome.Generation < newer.Generation)
            {
                return;
            }

            // A change's outcome (status, reload) must not be lost to a read landing right after it.
            _parked = _parked is { Rows: null } older && outcome.Rows is not null
                ? outcome with { Status = outcome.Status ?? older.Status, IsError = outcome.Status is null ? older.IsError : outcome.IsError, Reload = outcome.Reload || older.Reload }
                : outcome;
        }
    }

    private void StartLoad()
    {
        var generation = Interlocked.Increment(ref _generation);
        var scope = _scope;
        _loaded = false;
        LoadTask = Task.Run(async () =>
        {
            try
            {
                var rows = scope == EditScope.Session
                    ? _session.Read()
                    : await _settings.GetEnvironmentAsync(ToSystemScope(scope)).ConfigureAwait(false);
                Park(new Outcome(generation, rows, null, false, false));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Park(new Outcome(generation, [], string.Format(UiText.Get("EnvVars.ReadFailed", "Could not read the variables: {0}"), exception.Message), true, false));
            }
        });
    }

    private static EnvironmentScope ToSystemScope(EditScope scope) => scope == EditScope.Machine ? EnvironmentScope.Machine : EnvironmentScope.User;

    private void StartChange(Func<SystemEnvironmentVariable?, Task<SystemSettingResult>> change, SystemEnvironmentVariable? variable)
    {
        if (_busy)
        {
            _status = UiText.Get("EnvVars.Busy", "Another change is still running.");
            _statusIsError = true;
            return;
        }

        _busy = true;
        _status = UiText.Get("EnvVars.Working", "Working…");
        _statusIsError = false;
        PendingOperation = Task.Run(async () =>
        {
            SystemSettingResult result;
            try
            {
                result = await change(variable).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                result = SystemSettingResult.Fail(exception.Message);
            }
            finally
            {
                _busy = false;
            }

            Park(new Outcome(Volatile.Read(ref _generation), null, result.Message, !result.Success, Reload: true));
        });
    }

    /// <summary>The rows the filter lets through. Rebuilt only when the rows or the filter changed since the last call.</summary>
    private List<SystemEnvironmentVariable> VisibleList()
    {
        if (_visible is null || !ReferenceEquals(_visibleRows, _rows) || _visibleFilter != _filter)
        {
            var words = _filter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            _visible = [.. _rows.Where(v => words.All(w =>
                v.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || v.Value.Contains(w, StringComparison.OrdinalIgnoreCase)))];
            _visibleRows = _rows;
            _visibleFilter = _filter;
        }

        return _visible;
    }

    private SystemEnvironmentVariable? Selected(IReadOnlyList<SystemEnvironmentVariable> visible) =>
        visible.FirstOrDefault(v => SameName(v.Name, _selectedName)) ?? visible.FirstOrDefault();

    private static bool SameName(string a, string? b) =>
        b is not null && string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private IReadOnlyList<EditScope> AvailableScopes()
    {
        var scopes = new List<EditScope> { EditScope.Session };
        if (_settings.SupportsEnvironment(EnvironmentScope.User))
        {
            scopes.Add(EditScope.User);
        }

        if (_settings.SupportsEnvironment(EnvironmentScope.Machine))
        {
            scopes.Add(EditScope.Machine);
        }

        return scopes;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        switch (_mode)
        {
            case Mode.Form:
                yield return ("Enter/Tab", UiText.Get("DialogHint.EnvVars.NextFieldEnterSaves", "Next field; Enter on the value saves"));
                yield return ("Shift+Tab", UiText.Get("DialogHint.PreviousField", "Previous field"));
                yield return ("Esc", UiText.Get("Hint.DiscardChanges", "Discard the changes"));
                yield break;
            case Mode.Filter:
                yield return ("Enter", UiText.Get("DialogHint.KeepFilterBackToList", "Keep the filter and go back to the list"));
                yield return ("Esc", UiText.Get("DialogHint.ClearFilter", "Clear the filter"));
                yield break;
            case Mode.ConfirmDelete:
                yield return ("Y", UiText.Get("DialogHint.EnvVars.Delete", "Delete the variable"));
                yield return ("Any other key", UiText.Get("DialogHint.EnvVars.Keep", "Keep the variable"));
                yield break;
        }

        yield return ("Up/Down", UiText.Get("DialogHint.EnvVars.Move", "Move over the variables"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MovePageAtATime", "Move a page at a time"));
        yield return ("Insert/N", UiText.Get("DialogHint.EnvVars.Add", "Add a variable"));
        yield return ("Enter/F2/E", UiText.Get("DialogHint.EnvVars.Change", "Change the value"));
        yield return ("Delete/D", UiText.Get("DialogHint.EnvVars.DeleteAsks", "Delete the variable (asks first)"));
        yield return ("S", UiText.Get("DialogHint.EnvVars.Scope", "Switch between session, user and machine"));
        yield return ("R", UiText.Get("DialogHint.EnvVars.Reload", "Read the variables again"));
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
                // Esc is the dialog's own: it withdraws the question as it closes the dialog.
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
                return SelectAt(int.MaxValue);
            case ConsoleKey.Insert when plain:
                OpenForm(null);
                return true;
            case ConsoleKey.Enter when plain:
            case ConsoleKey.F2 when plain:
                OpenForm(Selected(VisibleList()));
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
                    OpenForm(Selected(VisibleList()));
                    return true;
                case 'd':
                    AskToDelete();
                    return true;
                case 's':
                    NextScope();
                    return true;
                case 'r':
                    StartLoad();
                    StateHasChanged();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private void NextScope()
    {
        var scopes = AvailableScopes();
        _scope = scopes[(scopes.ToList().IndexOf(_scope) + 1) % scopes.Count];
        _selectedName = null;
        _rows = [];
        StartLoad();
        StateHasChanged();
    }

    /// <summary>While the form is up every key belongs to it; Esc abandons the changes rather than closing the dialog.</summary>
    private bool HandleFormKey(KeyEvent keyEvent)
    {
        var nameLocked = _editingName is not null;
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            SetField(PasteHelper.DeleteLastWord(CurrentField));
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            if (_field == 0 && nameLocked)
            {
                return true;
            }

            PasteHelper.Paste(text =>
            {
                var joined = CurrentField + PasteHelper.SingleLine(text);
                SetField(joined.Length > MaxFieldLength ? joined[..MaxFieldLength] : joined);
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            SetField(string.Empty);
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _mode = Mode.Browse;
                _formError = null;
                _editingName = null;
                break;
            case ConsoleKey.Enter:
                if (_field == 1)
                {
                    CommitForm();
                }
                else
                {
                    _field = 1;
                }

                break;
            case ConsoleKey.Tab when keyEvent.Shift:
            case ConsoleKey.UpArrow:
                _field = nameLocked ? 1 : 0;
                break;
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                _field = 1;
                break;
            case ConsoleKey.Backspace when CurrentField.Length > 0 && !(_field == 0 && nameLocked):
                SetField(CurrentField[..^1]);
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && CurrentField.Length < MaxFieldLength && !(_field == 0 && nameLocked))
                {
                    SetField(CurrentField + keyEvent.KeyChar);
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    private string CurrentField => _field == 0 ? _formName : _formValue;

    private void SetField(string text)
    {
        if (_field == 0)
        {
            _formName = text;
        }
        else
        {
            _formValue = text;
        }

        _formError = null;
        StateHasChanged();
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
            PasteHelper.Paste(text => _filter += PasteHelper.SingleLine(text), StateHasChanged);
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
                if (printable && _filter.Length < 200)
                {
                    _filter += keyEvent.KeyChar;
                }

                break;
        }

        StateHasChanged();
        return true;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode is Mode.Browse or Mode.Filter)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return MoveSelection(mouseEvent.WheelDelta > 0 ? -3 : 3);
            }

            if (mouseEvent.LeftClicked && VariableAt(mouseEvent.Position) is { } variable)
            {
                _selectedName = variable.Name;
                StateHasChanged();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    private int FirstVisibleIndex(IReadOnlyList<SystemEnvironmentVariable> visible)
    {
        var selected = Selected(visible);
        var selectedIndex = selected is null ? 0 : visible.ToList().IndexOf(selected);
        return selectedIndex >= ListRows ? selectedIndex - ListRows + 1 : 0;
    }

    private SystemEnvironmentVariable? VariableAt(Position position)
    {
        long row = position.Y - (AbsolutePosition.Y + 1 + ListTop);
        long column = position.X - (AbsolutePosition.X + 2);
        if (row < 0 || row >= ListRows || column < 0 || column >= ViewSize.Width - 4)
        {
            return null;
        }

        var visible = VisibleList();
        var index = FirstVisibleIndex(visible) + (int)row;
        return index < visible.Count ? visible[index] : null;
    }

    private void OpenForm(SystemEnvironmentVariable? variable)
    {
        // Changing needs a highlighted variable; with an empty list E does nothing, N adds.
        _editingName = variable?.Name;
        _formName = variable?.Name ?? string.Empty;
        _formValue = variable?.Value ?? string.Empty;
        _field = variable is null ? 0 : 1;
        _formError = null;
        _mode = Mode.Form;
        StateHasChanged();
    }

    private void CommitForm()
    {
        var name = _formName;
        var value = _formValue;
        var windows = OperatingSystem.IsWindows();
        var scope = _scope;

        // A name that is already there is not being changed, so only a new one is checked here (a process may
        // hold names the saved stores would refuse); the service still refuses a bad one in words.
        var nameProblem = _editingName is not null ? null
            : scope == EditScope.Session ? SessionEnvironment.ValidateName(name)
            : SystemSettingsValidator.ValidateVariableName(name, windows);
        var problem = nameProblem ?? SystemSettingsValidator.ValidateVariableValue(value, windows);
        if (problem is not null)
        {
            _formError = problem;
            _field = nameProblem is null ? 1 : 0;
            return;
        }

        _selectedName = name;
        _editingName = null;
        _formError = null;
        _mode = Mode.Browse;
        StartChange(
            v => scope == EditScope.Session
                ? Task.FromResult(_session.Set(v!.Name, v.Value))
                : _settings.SetEnvironmentVariableAsync(ToSystemScope(scope), v!.Name, v.Value),
            new SystemEnvironmentVariable(name, value));
    }

    private void AskToDelete()
    {
        if (Selected(VisibleList()) is not null)
        {
            _mode = Mode.ConfirmDelete;
            StateHasChanged();
        }
    }

    private void DeleteSelected()
    {
        var visible = VisibleList();
        var variable = Selected(visible);
        if (variable is null)
        {
            return;
        }

        // The highlight moves to the variable that takes the removed one's row.
        var index = visible.IndexOf(variable);
        _selectedName = (visible.ElementAtOrDefault(index + 1) ?? visible.ElementAtOrDefault(index - 1))?.Name;
        var scope = _scope;
        StartChange(
            v => scope == EditScope.Session
                ? Task.FromResult(_session.Remove(v!.Name))
                : _settings.RemoveEnvironmentVariableAsync(ToSystemScope(scope), v!.Name),
            variable);
    }

    private bool MoveSelection(int delta)
    {
        var visible = VisibleList();
        var selected = Selected(visible);
        var index = selected is null ? 0 : visible.IndexOf(selected);
        return SelectAt(index + delta);
    }

    private bool SelectAt(int index)
    {
        var visible = VisibleList();
        if (visible.Count > 0)
        {
            _selectedName = visible[Math.Clamp(index, 0, visible.Count - 1)].Name;
            StateHasChanged();
        }

        return true;
    }

    private string ScopeName(EditScope scope) => scope switch
    {
        EditScope.Session => UiText.Get("EnvVars.Session", "Session"),
        EditScope.User => UiText.Get("EnvVars.User", "User"),
        _ => UiText.Get("EnvVars.Machine", "Machine"),
    };

    /// <summary>One line on how far the current scope reaches, which differs by system and is easy to get wrong.</summary>
    private string ScopeNote()
    {
        if (_scope == EditScope.Session)
        {
            return UiText.Get("EnvVars.NoteSession", "Seen by iTerminal and the programs it starts; gone when it closes.");
        }

        if (OperatingSystem.IsWindows())
        {
            return UiText.Get("EnvVars.NoteWindows", "Saved in the registry; programs started from now on see it.");
        }

        if (OperatingSystem.IsMacOS())
        {
            return UiText.Get("EnvVars.NoteMac", "Saved in ~/.zprofile; new login shells read it, running ones and non-login shells do not.");
        }

        return UiText.Get("EnvVars.NoteLinux", "Saved for systemd user sessions at the next login; terminal shells may not read it.");
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

        var visible = VisibleList();
        DrawScopes(buffer, iw, colorScheme, visible.Count);
        DrawHeader(buffer, iw, colorScheme);
        DrawRows(buffer, iw, colorScheme, visible);
        DrawForm(buffer, iw, colorScheme, Selected(visible));
        Put(buffer, 1, ih - 1, iw - 2, FooterText(), colorScheme.Foreground, colorScheme.Background);
    }

    private string FooterText() => _mode switch
    {
        Mode.Form => UiText.Get("EnvVars.FooterForm", "Enter next field  Tab/Shift+Tab move  Esc discard"),
        Mode.Filter => UiText.Get("EnvVars.FooterFilter", "Type to filter  Enter keep  Esc clear"),
        _ => UiText.Get("EnvVars.FooterBrowse", "N new  E change  D delete  S scope  R reload  / filter  Esc close"),
    };

    private void DrawScopes(IBuffer buffer, int iw, ColorScheme colorScheme, int shown)
    {
        var x = 1;
        foreach (var scope in AvailableScopes())
        {
            var label = $" {ScopeName(scope)} ";
            var active = scope == _scope;
            Put(buffer, x, 0, label.Length, label,
                active ? colorScheme.SelectedFocusedForeground : colorScheme.DisabledForeground,
                active ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
            x += label.Length + 1;
        }

        var count = !_loaded ? UiText.Get("EnvVars.Reading", "reading…")
            : _filter.Length > 0 ? $"{shown}/{_rows.Count}" : _rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (iw - count.Length - 1 > x)
        {
            Put(buffer, iw - count.Length - 1, 0, count.Length, count, colorScheme.DisabledForeground, colorScheme.Background);
        }

        var note = Wrap(ScopeNote(), iw - 2, NoteRows);
        for (var i = 0; i < NoteRows; i++)
        {
            Put(buffer, 1, 1 + i, iw - 2, i < note.Count ? note[i] : string.Empty, colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawHeader(IBuffer buffer, int iw, ColorScheme colorScheme) =>
        Put(buffer, 1, HeaderRow, iw - 2, Fit(UiText.Get("EnvVars.ColumnName", "Name"), NameColumn) + UiText.Get("EnvVars.ColumnValue", "Value"), Color.Cyan, colorScheme.Background);

    private void DrawRows(IBuffer buffer, int iw, ColorScheme colorScheme, List<SystemEnvironmentVariable> visible)
    {
        if (visible.Count == 0)
        {
            var empty = !_loaded ? UiText.Get("EnvVars.Reading", "reading…")
                : _filter.Length > 0 ? string.Format(UiText.Get("EnvVars.NoMatch", "No variable matches \"{0}\"."), _filter)
                : UiText.Get("EnvVars.Empty", "No variables here. Press N to add one.");
            Put(buffer, 1, ListTop, iw - 2, empty, colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        var selected = Selected(visible);
        var first = FirstVisibleIndex(visible);
        for (var i = 0; i < ListRows; i++)
        {
            var y = ListTop + i;
            var index = first + i;
            if (index >= visible.Count)
            {
                Put(buffer, 1, y, iw - 2, string.Empty, colorScheme.Foreground, colorScheme.Background);
                continue;
            }

            var variable = visible[index];
            var highlighted = ReferenceEquals(variable, selected);
            Put(buffer, 1, y, iw - 2, Fit(variable.Name, NameColumn) + variable.Value,
                highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }
    }

    /// <summary>Under the list: the form while it is open, otherwise the question, the status or the highlighted value in full.</summary>
    private void DrawForm(IBuffer buffer, int iw, ColorScheme colorScheme, SystemEnvironmentVariable? selected)
    {
        var top = ListTop + ListRows + 1;
        if (_mode == Mode.Form)
        {
            DrawField(buffer, iw, colorScheme, top, UiText.Get("EnvVars.ColumnName", "Name"), _formName, _field == 0);
            DrawField(buffer, iw, colorScheme, top + 1, UiText.Get("EnvVars.ColumnValue", "Value"), _formValue, _field == 1);
            var line = _formError
                ?? (_editingName is null
                    ? string.Format(UiText.Get("EnvVars.NewHint", "New variable in the {0} scope"), ScopeName(_scope))
                    : UiText.Get("EnvVars.ChangeHint", "Change the value; the name stays"));
            Put(buffer, 1, top + 2, iw - 2, line, _formError is null ? colorScheme.DisabledForeground : colorScheme.Error, colorScheme.Background);
            return;
        }

        if (_mode == Mode.ConfirmDelete && selected is not null)
        {
            Put(buffer, 1, top + 2, iw - 2, string.Format(UiText.Get("Prompt.DeleteNamed", "Delete \"{0}\"? (y/n)"), selected.Name), colorScheme.Error, colorScheme.Background);
            return;
        }

        if (_mode == Mode.Filter)
        {
            Put(buffer, 1, top + 2, iw - 2, string.Format(UiText.Get("EnvVars.FilterInput", "Filter: {0}_"), _filter), colorScheme.SelectedFocusedForeground, colorScheme.Background);
            return;
        }

        if (selected is not null)
        {
            DrawField(buffer, iw, colorScheme, top, UiText.Get("EnvVars.ColumnName", "Name"), selected.Name, false);
            DrawField(buffer, iw, colorScheme, top + 1, UiText.Get("EnvVars.ColumnValue", "Value"), selected.Value, false);
        }

        if (_status.Length > 0)
        {
            Put(buffer, 1, top + 2, iw - 2, _status, _statusIsError ? colorScheme.Error : colorScheme.DisabledForeground, colorScheme.Background);
        }
        else if (_filter.Length > 0)
        {
            Put(buffer, 1, top + 2, iw - 2, string.Format(UiText.Get("EnvVars.FilterHint", "Filter: {0}  (Esc shows every variable)"), _filter), colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private void DrawField(IBuffer buffer, int iw, ColorScheme colorScheme, int y, string label, string value, bool active)
    {
        Put(buffer, 1, y, LabelWidth, Fit(label + ":", LabelWidth), colorScheme.DisabledForeground, colorScheme.Background);
        Put(buffer, 1 + LabelWidth, y, iw - 2 - LabelWidth, active ? value + "_" : value,
            active ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground, colorScheme.Background);
    }

    /// <summary>Breaks <paramref name="text"/> at spaces into at most <paramref name="maxLines"/> lines of <paramref name="width"/>; the last is clipped.</summary>
    internal static List<string> Wrap(string text, int width, int maxLines)
    {
        var lines = new List<string>();
        var rest = text;
        while (rest.Length > 0 && lines.Count < maxLines)
        {
            if (rest.Length <= width || lines.Count == maxLines - 1)
            {
                lines.Add(rest.Length > width ? rest[..width] : rest);
                break;
            }

            var cut = rest.LastIndexOf(' ', width);
            cut = cut <= 0 ? width : cut;
            lines.Add(rest[..cut]);
            rest = rest[cut..].TrimStart();
        }

        return lines;
    }

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

    internal EditScope ScopeForTest => _scope;
    internal bool IsFormOpenForTest => _mode == Mode.Form;
    internal bool ConfirmingDeleteForTest => _mode == Mode.ConfirmDelete;
    internal bool IsFilteringForTest => _mode == Mode.Filter;
    internal string? FormErrorForTest => _formError;
    internal string StatusForTest => _status;
    internal string? SelectedNameForTest => Selected(VisibleList())?.Name;
    internal int RowCountForTest => _rows.Count;

    /// <summary>Takes a parked outcome now, as the next idle pass would.</summary>
    internal void ApplyParkedForTest() => OnIdle();

    /// <summary>Lands a finished read as the background load does, so a test can choose the order two reads arrive in.</summary>
    internal void ParkReadForTest(int generation, IReadOnlyList<SystemEnvironmentVariable> rows) => Park(new Outcome(generation, rows, null, false, false));

    /// <summary>The generation of the read waiting to be applied, or null.</summary>
    internal int? ParkedGenerationForTest { get { lock (_outcomeLock) { return _parked?.Generation; } } }

    #endregion
}
