using iTerminal.Core.Helper;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Services;
using iTerminal.Core.Model.ToDo;

namespace iTerminal.Core.Dialogs.ToDo;

/// <summary>
/// The task list, after Outlook's: subject, due date and priority, with done tasks kept below the open ones.
/// N (or Enter) types a new task (<c>!Subject @yyyy-MM-dd</c>); Space marks one done or open; P steps its
/// priority; D removes it; H hides or shows the done ones. A task that is open and past its due date is
/// drawn in the error colour. The highlight follows the task itself, so it stays on it when it moves.
/// </summary>
public class ToDoListDialog : Dialog
{
    private const int DialogWidth = 76;
    private const int DialogHeight = 24;
    private const int ListRows = 16;
    private const int MaxEntryLength = 120;
    private const string DoneColumn = "Done ";
    private const string PriorityColumn = "Priority ";
    private const string DueColumn = "Due        ";

    private readonly IDateTimeProviderService _dateTimeProviderService;
    private readonly ToDoStore _store;

    private DateOnly _today;
    private bool _showCompleted = true;

    /// <summary>The task the highlight is on. Kept by identity, not by row, so it survives a re-sort.</summary>
    private Guid? _selectedId;

    /// <summary>What is being typed for a new task, or null when no entry is open.</summary>
    private string? _entry;
    private string? _entryError;

    /// <summary>The task the entry line is changing, or null when it is typing a new one.</summary>
    private ToDoTask? _editing;

    /// <summary>True while "Delete this task? (y/n)" is up: the next key answers it.</summary>
    private bool _confirmingDelete;

    public ToDoListDialog(IDateTimeProviderService dateTimeProviderService, ToDoStore store, IThemeService themeService)
        : base(nameof(ToDoListDialog), themeService)
    {
        Title = "Tasks";
        _dateTimeProviderService = dateTimeProviderService;
        _store = store;
        _today = Today();
    }

    private DateOnly Today() => DateOnly.FromDateTime(_dateTimeProviderService.Now);

    /// <summary>The dialog is built once and opened many times, so the date is read again on each opening.</summary>
    public override async Task ShowAsync()
    {
        _today = Today();
        _entry = null;
        _entryError = null;
        _editing = null;
        _confirmingDelete = false;
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

    private IReadOnlyList<ToDoTask> Visible => _store.Visible(_showCompleted);

    private ToDoTask? Selected
    {
        get
        {
            var visible = Visible;
            return visible.FirstOrDefault(t => t.Id == _selectedId) ?? visible.FirstOrDefault();
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_entry is not null)
        {
            return HandleEntryKey(keyEvent);
        }

        if (_confirmingDelete)
        {
            // Esc is the dialog's own: it leaves the question as it closes the list. Any other key but y withdraws it.
            if (keyEvent.ConsoleKey != ConsoleKey.Escape)
            {
                _confirmingDelete = false;
                if (keyEvent.ConsoleKey == ConsoleKey.Y && !keyEvent.Control && !keyEvent.Alt)
                {
                    DeleteSelected();
                }

                StateHasChanged();
                return true;
            }

            _confirmingDelete = false;
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
            case ConsoleKey.Enter when plain:
            case ConsoleKey.Insert when plain:
                OpenEntry();
                return true;
            case ConsoleKey.Delete when plain:
                AskToDelete();
                return true;
            case ConsoleKey.F2 when plain:
                EditSelected();
                return true;
            case ConsoleKey.Spacebar when plain:
                ToggleSelected();
                return true;
        }

        if (plain)
        {
            switch (char.ToLowerInvariant(keyEvent.KeyChar))
            {
                case 'n':
                    OpenEntry();
                    return true;
                case 'd':
                    AskToDelete();
                    return true;
                case 'e':
                    EditSelected();
                    return true;
                case 'p':
                    CyclePriority();
                    return true;
                case 'h':
                    _showCompleted = !_showCompleted;
                    StateHasChanged();
                    return true;
                case ' ':
                    ToggleSelected();
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>While a task is being typed every key belongs to the entry; Esc abandons it rather than closing the list.</summary>
    private bool HandleEntryKey(KeyEvent keyEvent)
    {
        if (PasteHelper.IsDeleteWordKey(keyEvent))
        {
            _entry = PasteHelper.DeleteLastWord(_entry ?? string.Empty);
            _entryError = null;
            StateHasChanged();
            return true;
        }

        if (PasteHelper.IsPasteKey(keyEvent))
        {
            PasteHelper.Paste(text =>
            {
                _entry = ((_entry ?? string.Empty) + PasteHelper.SingleLine(text)) is { Length: > MaxEntryLength } longer ? longer[..MaxEntryLength] : (_entry ?? string.Empty) + PasteHelper.SingleLine(text);
                _entryError = null;
            }, StateHasChanged);
            return true;
        }

        if (keyEvent.Control && keyEvent.ConsoleKey == ConsoleKey.U)
        {
            _entry = string.Empty;
            _entryError = null;
            StateHasChanged();
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _entry = null;
                _entryError = null;
                _editing = null;
                break;
            case ConsoleKey.Enter:
                CommitEntry();
                break;
            case ConsoleKey.Backspace when _entry!.Length > 0:
                _entry = _entry[..^1];
                _entryError = null;
                break;
            default:
                var printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && _entry!.Length < MaxEntryLength)
                {
                    _entry += keyEvent.KeyChar;
                    _entryError = null;
                }
                break;
        }

        StateHasChanged();
        return true;
    }

    /// <summary>
    /// The wheel moves the highlight three tasks. A click highlights the task under the pointer, and a click on its
    /// [ ] box marks it done or open, as Space does. Nothing answers the mouse while a task is being typed or a
    /// deletion is being asked.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_entry is null && !_confirmingDelete)
        {
            if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return MoveSelection(mouseEvent.WheelDelta > 0 ? -3 : 3);
            }

            if (mouseEvent.LeftClicked && TaskAt(mouseEvent.Position, out ToDoTask? task, out bool onBox))
            {
                _selectedId = task!.Id;
                if (onBox)
                {
                    ToggleSelected();
                }
                else
                {
                    StateHasChanged();
                }

                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>The first listed task, which the list scrolls to keep the highlight in view.</summary>
    private int FirstVisibleIndex(IReadOnlyList<ToDoTask> visible)
    {
        var selectedIndex = visible.ToList().FindIndex(t => t.Id == Selected?.Id);
        return selectedIndex >= ListRows ? selectedIndex - ListRows + 1 : 0;
    }

    private bool TaskAt(Position position, out ToDoTask? task, out bool onBox)
    {
        task = null;
        onBox = false;
        long row = position.Y - (AbsolutePosition.Y + 2);
        long column = position.X - (AbsolutePosition.X + 2);
        var visible = Visible;
        if (row < 0 || row >= ListRows || column < 0 || column >= ViewSize.Width - 4)
        {
            return false;
        }

        int index = FirstVisibleIndex(visible) + (int)row;
        if (index >= visible.Count)
        {
            return false;
        }

        task = visible[index];
        onBox = column <= 2;
        return true;
    }

    /// <summary>E or F2: the highlighted task goes into the entry line as it would be typed, to be changed.</summary>
    private void EditSelected()
    {
        if (Selected is not { } task)
        {
            return;
        }

        _editing = task;
        _entry = (task.Priority == ToDoPriority.High ? "!" : string.Empty)
            + task.Subject
            + (task.DueDate is { } due ? " @" + due.ToString("yyyy-MM-dd") : string.Empty);
        _entryError = null;
        StateHasChanged();
    }

    private void AskToDelete()
    {
        if (Selected is not null)
        {
            _confirmingDelete = true;
            StateHasChanged();
        }
    }

    private void OpenEntry()
    {
        _entry = string.Empty;
        _entryError = null;
        StateHasChanged();
    }

    private void CommitEntry()
    {
        var id = _editing?.Id ?? Guid.NewGuid();
        if (ToDoTask.TryParse(_entry!, id, out var task, out var error) && task is not null)
        {
            if (_editing is { } original)
            {
                // Done stays done. The line can only say "!" (high): without it a high task becomes normal, and a low one stays low.
                var priority = task.Priority == ToDoPriority.High ? ToDoPriority.High
                    : original.Priority == ToDoPriority.High ? ToDoPriority.Normal
                    : original.Priority;
                _store.Update(task with { IsComplete = original.IsComplete, Priority = priority });
                _editing = null;
            }
            else
            {
                _store.Add(task);
            }

            _selectedId = id;
            _entry = null;
            _entryError = null;
        }
        else
        {
            _entryError = error;
        }

        StateHasChanged();
    }

    private void ToggleSelected()
    {
        var task = Selected;
        if (task is null)
        {
            return;
        }

        _store.Update(task with { IsComplete = !task.IsComplete });
        _selectedId = task.Id;
        StateHasChanged();
    }

    private void CyclePriority()
    {
        var task = Selected;
        if (task is null)
        {
            return;
        }

        _store.Update(task with { Priority = ToDoTask.NextPriority(task.Priority) });
        _selectedId = task.Id;
        StateHasChanged();
    }

    /// <summary>Removes the highlighted task and moves the highlight to the one that took its row.</summary>
    private void DeleteSelected()
    {
        var visible = Visible;
        var task = Selected;
        if (task is null)
        {
            return;
        }

        var index = visible.ToList().FindIndex(t => t.Id == task.Id);
        _store.Remove(task.Id);
        var remaining = Visible;
        var next = remaining.ElementAtOrDefault(index) ?? remaining.ElementAtOrDefault(index - 1);
        _selectedId = next?.Id;
        StateHasChanged();
    }

    private bool MoveSelection(int delta)
    {
        var visible = Visible;
        var index = visible.ToList().FindIndex(t => t.Id == Selected?.Id);
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
        DrawEntry(buffer, iw, colorScheme);
        Put(buffer, 1, ih - 1, iw - 2, "Space done  N new  E change  P priority  D delete  H show done  Esc close", colorScheme.Foreground, colorScheme.Background);
    }

    private void DrawHeader(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var open = _store.Visible(false).Count;
        var title = $"{open} open  ·  {_store.Visible(true).Count - open} done";
        Put(buffer, 1, 0, iw - 2, DoneColumn + PriorityColumn + DueColumn + "Subject", Color.Cyan, colorScheme.Background);
        Put(buffer, iw - title.Length - 1, 0, title.Length, title, colorScheme.DisabledForeground, colorScheme.Background);
    }

    private void DrawRows(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var visible = Visible;
        var selectedId = Selected?.Id;
        if (visible.Count == 0)
        {
            Put(buffer, 1, 1, iw - 2, "No tasks. Press N to add one, e.g. !Renew passport @2026-11-01", colorScheme.DisabledForeground, colorScheme.Background);
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

            var task = visible[index];
            var highlighted = task.Id == selectedId;
            var overdue = !task.IsComplete && task.DueDate is { } due && due < _today;
            var foreground = highlighted ? colorScheme.SelectedFocusedForeground
                : task.IsComplete ? colorScheme.DisabledForeground
                : overdue ? colorScheme.Error
                : colorScheme.Foreground;
            var background = highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background;

            var done = task.IsComplete ? "[x]  " : "[ ]  ";
            var priority = task.Priority switch
            {
                ToDoPriority.High => "High     ",
                ToDoPriority.Low => "Low      ",
                _ => "         ",
            };
            var dueText = task.DueDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            var line = done + priority + dueText.PadRight(DueColumn.Length) + task.Subject;
            Put(buffer, 1, y, iw - 2, line, foreground, background);
        }
    }

    private void DrawEntry(IBuffer buffer, int iw, ColorScheme colorScheme)
    {
        var y = 1 + ListRows + 1;
        if (_confirmingDelete && Selected is { } doomed)
        {
            Put(buffer, 1, y, iw - 2, $"Delete \"{doomed.Subject}\"? (y/n)", colorScheme.Error, colorScheme.Background);
        }
        else if (_entry is not null)
        {
            Put(buffer, 1, y, iw - 2, $"{(_editing is null ? "New" : "Change")} task: {_entry}_", colorScheme.SelectedFocusedForeground, colorScheme.Background);
            if (_entryError is not null)
            {
                Put(buffer, 1, y + 1, iw - 2, _entryError, colorScheme.Error, colorScheme.Background);
            }
        }
        else
        {
            Put(buffer, 1, y, iw - 2, "N: new task, e.g. !Renew passport @2026-11-01  (! = high priority)", colorScheme.DisabledForeground, colorScheme.Background);
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

    #region Test seams

    internal string? EntryForTest => _entry;
    internal bool ConfirmingDeleteForTest => _confirmingDelete;
    internal string? EntryErrorForTest => _entryError;
    internal ToDoTask? SelectedForTest => Selected;

    #endregion
}
