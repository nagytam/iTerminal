using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using iTerminal.Core.Model;
using iTerminal.Core.Model.Workspaces;
using iTerminal.Core.Services.Workspaces;

namespace iTerminal.Core.Dialogs.Workspaces;

/// <summary>
/// The workspaces: named screen layouts. A workspace remembers where both panels are, how each is
/// ordered, and what is under them (the AI bar, the command line or the full-screen terminal), so one
/// Enter puts a whole working context back — the project folders and the build terminal, then the
/// downloads and the AI bar.
///
/// <para>The list is in the order the workspaces were saved, and the first nine are on the 1-9 keys. A
/// mark in front of a row says where the screen stands against it: ● it is what is on screen now,
/// ○ it was the last one used but the screen has moved on since. S saves the screen as a new workspace
/// (a name that exists is replaced, after a y), U overwrites the selected one with the screen as it is
/// now, R renames and D deletes, each asking first.</para>
/// </summary>
public class WorkspaceManagerDialog : Dialog
{
    private enum Mode
    {
        List,
        Name,
    }

    private enum Pending
    {
        None,
        Delete,
        Update,
        Replace,
    }

    private const int DialogWidth = 80;
    private const int DialogHeight = 24;
    private const int DetailRows = 5;

    private readonly IWorkspaceService _workspaces;
    private readonly INotificationService? _notifications;
    private readonly Lock _sync = new();

    private Mode _mode = Mode.List;
    private Pending _pending = Pending.None;
    private IReadOnlyList<Workspace> _list = [];
    private int _cursor;
    private string? _selectedName;
    private string? _activeName;
    private WorkspaceLayout? _now;
    private string _text = string.Empty;

    /// <summary>True while the prefilled name is "selected": the first key replaces it, the way a rename field does.</summary>
    private bool _textIsSelected;
    private string? _renaming;
    private string _status = string.Empty;
    private bool _statusIsError;

    public WorkspaceManagerDialog(
        IWorkspaceService workspaces,
        IThemeService themeService,
        INotificationService? notifications = null)
        : base(nameof(WorkspaceManagerDialog), themeService)
    {
        Title = "Workspaces";
        _workspaces = workspaces;
        _notifications = notifications;
    }

    /// <summary>The names in the list, in order. Exposed for tests.</summary>
    internal int CursorForTest => _cursor;
    internal IReadOnlyList<string> NamesForTest => [.. _list.Select(w => w.Name)];

    /// <summary>The name the cursor is on. Exposed for tests.</summary>
    internal string? SelectedName => Selected?.Name;

    /// <summary>The restore the last key started, so a test can wait for it.</summary>
    internal Task PendingOperation { get; private set; } = Task.CompletedTask;

    private Workspace? Selected => _list.FirstOrDefault(w => w.Name == _selectedName) ?? _list.FirstOrDefault();

    public override async Task ShowAsync()
    {
        lock (_sync)
        {
            _mode = Mode.List;
            _pending = Pending.None;
            _status = string.Empty;
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

    private void Reload()
    {
        _list = _workspaces.GetAll();
        _activeName = _workspaces.ActiveName;
        _now = _workspaces.CaptureCurrent();
        var index = _list.ToList().FindIndex(w => w.Name == _selectedName);
        if (index < 0 && _activeName is not null)
        {
            index = _list.ToList().FindIndex(w => w.Name == _activeName);
        }

        _cursor = index >= 0 ? index : Math.Clamp(_cursor, 0, Math.Max(0, _list.Count - 1));
        _selectedName = _list.ElementAtOrDefault(_cursor)?.Name;
    }

    #region Keys

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_mode == Mode.Name)
        {
            yield return ("Enter", "Save the workspace under this name");
            yield return ("Esc", "Back to the list");
            yield break;
        }

        if (_pending != Pending.None)
        {
            yield return ("Y", "Confirm");
            yield return ("Any other key", "Cancel");
            yield break;
        }

        yield return ("Up/Down", "Choose a workspace");
        yield return ("Enter", "Switch to the chosen workspace");
        yield return ("1-9", "Switch to a workspace by its number");
        yield return ("S/N", "Save the screen as a new workspace");
        yield return ("U", "Update the chosen workspace from the screen");
        yield return ("R", "Rename the chosen workspace");
        yield return ("D/Delete", "Delete the chosen workspace (asks first)");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        Workspace? toRestore = null;
        lock (_sync)
        {
            if (_mode == Mode.Name)
            {
                return HandleNameKey(keyEvent);
            }

            if (!(keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift))
            {
                if (HandleListKey(keyEvent, out toRestore))
                {
                    if (toRestore is null)
                    {
                        return true;
                    }
                }
            }
            else if (_pending != Pending.None)
            {
                // Esc withdraws a question before it closes the dialog.
                _pending = Pending.None;
                StateHasChanged();
                return true;
            }
        }

        if (toRestore is not null)
        {
            await RestoreAsync(toRestore);
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    private bool HandleListKey(KeyEvent keyEvent, out Workspace? toRestore)
    {
        toRestore = null;
        var plain = !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
        var letter = char.ToLowerInvariant(keyEvent.KeyChar);

        // A pending question is answered by y; anything else withdraws it (and then acts as itself).
        if (_pending != Pending.None)
        {
            var pending = _pending;
            _pending = Pending.None;
            if (plain && letter == 'y')
            {
                Confirm(pending);
                return true;
            }

            if (plain && keyEvent.ConsoleKey is ConsoleKey.Enter)
            {
                StateHasChanged();
                return true;
            }
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
            case ConsoleKey.Home when plain:
                return MoveCursor(-_list.Count);
            case ConsoleKey.End when plain:
                return MoveCursor(_list.Count);
            case ConsoleKey.Enter when plain:
                toRestore = Selected;
                if (toRestore is null)
                {
                    SetStatus("Nothing to switch to yet. S saves the screen as it is now.", false);
                }

                return true;
            case ConsoleKey.Delete when plain:
                AskDelete();
                return true;
        }

        if (!plain)
        {
            return false;
        }

        if (letter is >= '1' and <= '9')
        {
            var index = letter - '1';
            if (index < _list.Count)
            {
                toRestore = _list[index];
                return true;
            }

            SetStatus($"There is no workspace {letter}.", true);
            return true;
        }

        switch (letter)
        {
            case 's':
            case 'n':
                OpenName(null);
                return true;
            case 'u':
                AskUpdate();
                return true;
            case 'r':
                if (Selected is { } selected)
                {
                    OpenName(selected.Name);
                }
                else
                {
                    SetStatus("Nothing to rename yet.", true);
                }

                return true;
            case 'd':
                AskDelete();
                return true;
        }

        return false;
    }

    private bool HandleNameKey(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape)
        {
            _mode = Mode.List;
            _pending = Pending.None;
            SetStatus(string.Empty, false);
            return true;
        }

        if (keyEvent.ConsoleKey == ConsoleKey.Enter && !keyEvent.Control && !keyEvent.Alt)
        {
            CommitName();
            return true;
        }

        if (PasteHelper.IsDeleteWordKey(keyEvent) || PasteHelper.IsClearKey(keyEvent))
        {
            _text = _textIsSelected || PasteHelper.IsClearKey(keyEvent) ? string.Empty : PasteHelper.DeleteLastWord(_text);
            _textIsSelected = false;
            _pending = Pending.None;
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                string line = PasteHelper.SingleLine(text);
                if (_textIsSelected)
                {
                    _text = string.Empty;
                    _textIsSelected = false;
                }

                _pending = Pending.None;
                _text = (_text + line) is { Length: > IWorkspaceService.MaxNameLength } longer ? longer[..IWorkspaceService.MaxNameLength] : _text + line;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control || keyEvent.Alt)
        {
            return true;
        }

        // Typing again withdraws the "replace it?" question: the name is no longer the one that was asked about.
        if (keyEvent.ConsoleKey is ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.End or ConsoleKey.Home)
        {
            // Moving in the text keeps it, to be edited rather than replaced.
            _textIsSelected = false;
        }
        else if (keyEvent.ConsoleKey == ConsoleKey.Backspace)
        {
            _text = _textIsSelected || _text.Length == 0 ? string.Empty : _text[..^1];
            _textIsSelected = false;
            _pending = Pending.None;
        }
        else if (keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar))
        {
            if (_textIsSelected)
            {
                _text = string.Empty;
                _textIsSelected = false;
            }

            if (_text.Length < IWorkspaceService.MaxNameLength)
            {
                _text += keyEvent.KeyChar;
            }

            _pending = Pending.None;
        }

        SetStatus(string.Empty, false);
        return true;
    }

    /// <summary>
    /// The wheel moves the highlight three workspaces; a click highlights the one under the pointer (Enter switches to
    /// it). Only the list answers, and not while a question or a name is being asked.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_mode == Mode.List && _pending == Pending.None && _list.Count > 0)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return MoveCursor(mouseEvent.WheelDelta > 0 ? -3 : 3);
            }

            if (mouseEvent.LeftClicked)
            {
                long row = mouseEvent.Position.Y - (AbsolutePosition.Y + 3);
                long column = mouseEvent.Position.X - (AbsolutePosition.X + 2);
                int rows = ListRows;
                int first = _cursor >= rows ? _cursor - rows + 1 : 0;
                if (row >= 0 && row < rows && column >= 0 && column < ViewSize.Width - 4 && first + (int)row < _list.Count)
                {
                    return MoveCursor(first + (int)row - _cursor);
                }
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    private bool MoveCursor(int delta)
    {
        if (_list.Count > 0)
        {
            _cursor = Math.Clamp(_cursor + delta, 0, _list.Count - 1);
            _selectedName = _list[_cursor].Name;
        }

        SetStatus(string.Empty, false);
        return true;
    }

    #endregion

    #region Actions

    private void OpenName(string? renaming)
    {
        _renaming = renaming;
        _text = renaming ?? _workspaces.SuggestName();
        _textIsSelected = true;
        _pending = Pending.None;
        _mode = Mode.Name;
        _now = _workspaces.CaptureCurrent();
        SetStatus(string.Empty, false);
    }

    private void CommitName()
    {
        var name = _text.Trim();
        var exists = _list.Any(w => string.Equals(w.Name, name, StringComparison.CurrentCultureIgnoreCase));

        if (_renaming is not null)
        {
            if (_workspaces.ValidateName(name, _renaming) is { } renameProblem)
            {
                SetStatus(renameProblem, true);
                return;
            }

            _workspaces.Rename(_renaming, name);
            _selectedName = name;
            _mode = Mode.List;
            Reload();
            SetStatus($"Renamed to '{name}'.", false);
            return;
        }

        // A taken name is a replace, not an error — but only after a second Enter: it is the one way
        // here to lose a saved layout by typing.
        if (exists && _pending != Pending.Replace)
        {
            _pending = Pending.Replace;
            SetStatus($"'{name}' already exists. Enter again replaces it with the screen as it is now.", true);
            return;
        }

        if (!exists && _workspaces.ValidateName(name) is { } problem)
        {
            SetStatus(problem, true);
            return;
        }

        var saved = _workspaces.Save(name);
        _pending = Pending.None;
        _selectedName = saved.Name;
        _mode = Mode.List;
        Reload();
        SetStatus(exists ? $"Replaced '{saved.Name}'." : $"Saved '{saved.Name}'.", false);
    }

    private void AskUpdate()
    {
        if (Selected is not { } selected)
        {
            SetStatus("Nothing to update yet. S saves the screen as a new workspace.", true);
            return;
        }

        _pending = Pending.Update;
        SetStatus(string.Empty, false);
    }

    private void AskDelete()
    {
        if (Selected is null)
        {
            SetStatus("Nothing to delete.", true);
            return;
        }

        _pending = Pending.Delete;
        SetStatus(string.Empty, false);
    }

    private void Confirm(Pending pending)
    {
        if (Selected is not { } selected)
        {
            return;
        }

        switch (pending)
        {
            case Pending.Delete:
                _workspaces.Delete(selected.Name);
                Reload();
                SetStatus($"Deleted '{selected.Name}'.", false);
                break;
            case Pending.Update:
                _workspaces.Save(selected.Name);
                Reload();
                SetStatus($"'{selected.Name}' now holds the screen as it is.", false);
                break;
        }
    }

    private async Task RestoreAsync(Workspace workspace)
    {
        // Closed first: the layout puts the cursor back in a panel, which a dialog still on top would take.
        Close();
        var problems = await _workspaces.RestoreAsync(workspace.Name);
        if (_notifications is null)
        {
            return;
        }

        if (problems.Count == 0)
        {
            _notifications.Notify($"Workspace '{workspace.Name}'");
        }
        else
        {
            _notifications.Notify(
                $"Workspace '{workspace.Name}' is back, except:",
                string.Join(Environment.NewLine, problems),
                NotificationSeverity.Warning);
        }
    }

    private void SetStatus(string text, bool isError)
    {
        _status = text;
        _statusIsError = isError;
        StateHasChanged();
    }

    #endregion

    #region Drawing

    private int InnerHeight => (int)ViewSize.Height - 2;

    /// <summary>Rows for the list: what is left under the summary and header and above the detail, status and hints.</summary>
    private int ListRows => Math.Max(3, InnerHeight - 3 - DetailRows - 2);

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        if (ViewSize.IsEmpty)
        {
            return;
        }

        var iw = (int)ViewSize.Width - 2;
        var ih = InnerHeight;
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

            DrawList(buffer, iw, colorScheme);
            if (_mode == Mode.Name)
            {
                DrawName(buffer, iw, colorScheme);
            }
            else
            {
                DrawDetail(buffer, iw, colorScheme);
            }

            if (ih >= 2)
            {
                Put(buffer, 1, ih - 2, iw - 2, StatusText(), _statusIsError || _pending != Pending.None ? colorScheme.Error : Color.Cyan, colorScheme.Background);
            }

            Put(buffer, 1, ih - 1, iw - 2, Hints().FitHints(iw - 2), colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private string StatusText()
    {
        var name = Selected?.Name ?? string.Empty;
        return _pending switch
        {
            Pending.Delete => $"Delete '{name}'? y deletes, any other key keeps it.",
            Pending.Update => $"Replace '{name}' with the screen as it is now? y replaces, any other key keeps it.",
            _ => _status,
        };
    }

    private string Hints() => _mode == Mode.Name
        ? (_renaming is null ? "Enter save  Esc cancel" : "Enter rename  Esc cancel")
        : _pending != Pending.None
            ? "y confirm  Esc keep"
            : "Enter switch  1-9 jump  S save  U update  R rename  D delete  Esc close";

    private void DrawList(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var summary = _workspaces.ActiveName is null
            ? "The screen is not saved as a workspace."
            : Selected is not null && _activeName is not null && _list.FirstOrDefault(w => w.Name == _activeName) is { } active
                ? $"On screen: {active.Name}" + (_now is not null && active.Layout.SameScreenAs(_now) ? string.Empty : "  (the screen has changed since)")
                : "The screen is not saved as a workspace.";
        Put(buffer, 1, 0, iw - 2, $"{_list.Count} saved  ·  {summary}", Color.Cyan, colorScheme.Background);
        Put(buffer, 1, 1, iw - 2, "     # Name                      Left panel  →  Right panel", colorScheme.DisabledForeground, colorScheme.Background);

        if (_list.Count == 0)
        {
            Put(buffer, 1, 2, iw - 2, "No workspaces yet. S saves the screen as it is now: both panels,", colorScheme.DisabledForeground, colorScheme.Background);
            Put(buffer, 1, 3, iw - 2, "their sort order and what is under them. Enter brings one back.", colorScheme.DisabledForeground, colorScheme.Background);
            return;
        }

        var rows = ListRows;
        var first = _cursor >= rows ? _cursor - rows + 1 : 0;
        var folderWidth = Math.Max(8, (iw - 2 - 5 - 2 - 26 - 5) / 2);
        for (var i = 0; i < rows; i++)
        {
            var index = first + i;
            if (index >= _list.Count)
            {
                break;
            }

            var workspace = _list[index];
            var highlighted = index == _cursor;
            var mark = workspace.Name == _activeName
                ? (_now is not null && workspace.Layout.SameScreenAs(_now) ? "●" : "○")
                : " ";
            var number = index < 9 ? (index + 1).ToString() : " ";
            var line = $"{(highlighted ? ">" : " ")} {mark}  {number} {Clip(workspace.Name, 25),-25} {Clip(Short(workspace.Layout.LeftUrl), folderWidth).PadRight(folderWidth)} → {Clip(Short(workspace.Layout.RightUrl), folderWidth)}";
            Put(buffer, 1, 2 + i, iw - 2, line,
                highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
                highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background);
        }
    }

    private void DrawDetail(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var top = 2 + ListRows + 1;
        if (Selected is not { } selected)
        {
            return;
        }

        var layout = selected.Layout;
        Put(buffer, 1, top, iw - 2, $"Left   {layout.LeftUrl}{DescribeSort(layout.LeftSort)}", colorScheme.Foreground, colorScheme.Background);
        Put(buffer, 1, top + 1, iw - 2, $"Right  {layout.RightUrl}{DescribeSort(layout.RightSort)}", colorScheme.Foreground, colorScheme.Background);
        Put(buffer, 1, top + 2, iw - 2,
            $"Below  {DescribeBar(layout.Bar)}  ·  cursor: {(layout.FocusedPanel == "RightPanel" ? "right" : "left")}  ·  saved {selected.SavedUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
            colorScheme.DisabledForeground, colorScheme.Background);

        if (selected.Name == _activeName)
        {
            var same = _now is not null && layout.SameScreenAs(_now);
            Put(buffer, 1, top + 3, iw - 2,
                same ? "● This is what is on screen now." : "○ The screen has changed since. U updates this, Enter puts it back.",
                same ? Color.DarkGreen : Color.DarkYellow, colorScheme.Background);
        }
    }

    private void DrawName(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var top = 2 + ListRows + 1;
        Put(buffer, 1, top, iw - 2, _renaming is null ? "Save the screen as a workspace:" : $"Rename '{_renaming}':", Color.Cyan, colorScheme.Background);
        Put(buffer, 1, top + 1, 6, "Name", colorScheme.DisabledForeground, colorScheme.Background);
        Put(buffer, 8, top + 1, iw - 10, _textIsSelected ? _text : _text + "_",
            _textIsSelected ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground,
            _textIsSelected ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground);
        if (_renaming is null && _now is not null)
        {
            Put(buffer, 1, top + 3, iw - 2, $"Saves: {Short(_now.LeftUrl)} → {Short(_now.RightUrl)}  ·  {DescribeBar(_now.Bar)}", colorScheme.DisabledForeground, colorScheme.Background);
        }
    }

    private static string DescribeBar(WorkspaceBar bar) => bar switch
    {
        WorkspaceBar.Terminal => "command line",
        WorkspaceBar.FullTerminal => "full-screen terminal",
        _ => "AI bar",
    };

    private static string DescribeSort(string sort)
    {
        var parsed = PanelSort.Parse(sort);
        return parsed.IsAsListed ? string.Empty : $"   [{parsed.Describe()}]";
    }

    /// <summary>A location as a short label: the last folder name, or the scheme when there is none ("musiccatalog:").</summary>
    internal static string Short(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return "-";
        }

        var parsed = new Url(url);
        var path = parsed.Path.TrimEnd('/', '\\');
        var last = path[(Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1)..];
        if (last.Length > 0)
        {
            return last;
        }

        return path.Length > 0 ? path : url;
    }

    private static string Clip(string text, int width) =>
        width <= 0 ? string.Empty : text.Length <= width ? text : width == 1 ? text[..1] : text[..(width - 1)] + "…";

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

    #endregion
}
