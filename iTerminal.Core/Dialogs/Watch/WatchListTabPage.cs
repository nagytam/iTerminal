using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Localization;
using iConsole.Services;

namespace iTerminal.Core.Dialogs.Watch;

/// <summary>
/// A page that lists items with a cursor: the alarms, the world-clock zones and the timers. The page's last row is the
/// message line, which carries the entry being typed (N or Insert opens it, Enter adds, Esc cancels), its error, or the y/n
/// question that Delete asks. Concrete pages say what an item looks like and what adding, toggling and deleting it does.
/// </summary>
public abstract class WatchListTabPage(string name, IThemeService themeService) : WatchTabPage(name, themeService)
{
    private const int MaxEntryLength = 80;

    private int _cursor;
    private int _top;
    private string? _entry;
    private string? _entryError;
    private bool _confirmingDelete;

    /// <summary>How many items the list holds right now.</summary>
    protected abstract int ItemCount { get; }

    /// <summary>The text of one row, without the cursor.</summary>
    protected abstract string RowText(int index);

    /// <summary>The line shown when the list is empty.</summary>
    protected abstract string EmptyText { get; }

    /// <summary>What the entry line asks for, e.g. "New alarm (HH:mm label)".</summary>
    protected abstract string EntryPrompt { get; }

    /// <summary>The row above the list, or empty for none.</summary>
    protected virtual string HeadingText => string.Empty;

    /// <summary>The row the heading is drawn on; the list starts on the row under it.</summary>
    protected virtual int HeadingRow => 0;

    /// <summary>The colour of a row, or null for the page's ordinary colour.</summary>
    protected virtual Color? RowForeground(int index) => null;

    /// <summary>Whether Delete may remove this item. The world clock keeps the computer's own zone.</summary>
    protected virtual bool CanDelete(int index) => true;

    /// <summary>Adds the item typed into the entry line. Returns null when it was added, or the error to show.</summary>
    protected abstract string? AddItem(string text);

    protected virtual void DeleteItem(int index)
    {
    }

    protected virtual void ToggleItem(int index)
    {
    }

    /// <summary>Keys the page has beyond the list's own (the list keys are handled first). Returns false to leave the key unhandled.</summary>
    protected virtual Task<bool> HandleOtherKeyAsync(KeyEvent keyEvent) => Task.FromResult(false);

    /// <summary>The item the cursor is on, clamped to the list; -1 when the list is empty.</summary>
    protected int Cursor => ItemCount == 0 ? -1 : Math.Clamp(_cursor, 0, ItemCount - 1);

    protected int ListTop => HeadingRow + 1;

    /// <summary>The rows the list can show: everything from the list's top to the message line, which is the page's last row.</summary>
    protected int VisibleRows => Math.Max(0, (int)ViewSize.Height - 1 - ListTop);

    public override bool HasPendingInput => _entry is not null || _confirmingDelete;

    public override void CancelPendingInput()
    {
        _entry = null;
        _entryError = null;
        _confirmingDelete = false;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (_entry is not null)
        {
            HandleEntryKey(keyEvent);
            return true;
        }

        if (_confirmingDelete)
        {
            // Only y deletes; any other key withdraws the question (Esc is the dialog's, and leaves the question as the dialog closes).
            _confirmingDelete = false;
            if (keyEvent.ConsoleKey == ConsoleKey.Y && IsPlain(keyEvent) && Cursor >= 0)
            {
                DeleteItem(Cursor);
                _cursor = Cursor;
            }

            StateHasChanged();
            return true;
        }

        bool plain = IsPlain(keyEvent);
        int step = Math.Max(1, VisibleRows - 1);
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow when plain:
                return MoveCursor(-1);
            case ConsoleKey.DownArrow when plain:
                return MoveCursor(1);
            case ConsoleKey.PageUp when plain:
                return MoveCursor(-step);
            case ConsoleKey.PageDown when plain:
                return MoveCursor(step);
            case ConsoleKey.Home when plain:
                return MoveTo(0);
            case ConsoleKey.End when plain:
                return MoveTo(ItemCount - 1);
            case ConsoleKey.N when plain:
            case ConsoleKey.Insert when plain:
            case ConsoleKey.Enter when plain:
                _entry = string.Empty;
                _entryError = null;
                StateHasChanged();
                return true;
            case ConsoleKey.Delete when plain:
                if (Cursor >= 0 && CanDelete(Cursor))
                {
                    _confirmingDelete = true;
                    StateHasChanged();
                }

                return true;
            case ConsoleKey.Spacebar when plain:
                if (Cursor >= 0)
                {
                    ToggleItem(Cursor);
                    StateHasChanged();
                }

                return true;
        }

        if (await HandleOtherKeyAsync(keyEvent))
        {
            StateHasChanged();
            return true;
        }

        return false;
    }

    /// <summary>Paints what sits above the list (a clock face, say). It has to write every row above <see cref="HeadingRow"/>.</summary>
    protected virtual void PaintAboveList(IBuffer buffer)
    {
    }

    protected override void PaintPage(IBuffer buffer)
    {
        PaintAboveList(buffer);
        int rows = VisibleRows;
        int cursor = Cursor;
        if (cursor >= 0)
        {
            // Keep the cursor on screen: scroll down until it is the last visible row, or up until it is the first.
            if (cursor < _top)
            {
                _top = cursor;
            }
            else if (rows > 0 && cursor >= _top + rows)
            {
                _top = cursor - rows + 1;
            }
        }

        _top = Math.Clamp(_top, 0, Math.Max(0, ItemCount - rows));

        var (foreground, background) = RowColours(highlighted: false);
        WriteRow(buffer, HeadingRow, HeadingText, foreground, background);

        for (int line = 0; line < rows; line++)
        {
            int index = _top + line;
            int row = ListTop + line;
            if (index >= ItemCount)
            {
                WriteRow(buffer, row, string.Empty, foreground, background);
                continue;
            }

            bool highlighted = index == cursor;
            var (rowForeground, rowBackground) = RowColours(highlighted);
            if (RowForeground(index) is { } colour && !highlighted)
            {
                rowForeground = colour;
            }

            WriteRow(buffer, row, " " + RowText(index), rowForeground, rowBackground);
        }

        if (ItemCount == 0 && rows > 0)
        {
            WriteRow(buffer, ListTop, " " + EmptyText, DimColour, background);
        }

        WriteRow(buffer, (int)ViewSize.Height - 1, MessageLine(), MessageColour(), background);
    }

    private string MessageLine()
    {
        // An error takes the whole line: after the prompt and the typed text it is cut off by the dialog's width.
        if (_entry is not null)
        {
            return _entryError is null ? $" {EntryPrompt}: {_entry}_" : " " + _entryError;
        }

        if (_confirmingDelete && Cursor >= 0)
        {
            return " " + UiText.Get("Watch.ConfirmDelete", "Delete the highlighted one? (y/n)");
        }

        return string.Empty;
    }

    private Color MessageColour() => _entryError is not null ? ErrorColour : ThemeService.CurrentColorScheme.Foreground;

    private void HandleEntryKey(KeyEvent keyEvent)
    {
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Escape:
                _entry = null;
                _entryError = null;
                break;
            case ConsoleKey.Enter:
                var error = AddItem((_entry ?? string.Empty).Trim());
                if (error is null)
                {
                    _entry = null;
                    _entryError = null;
                }
                else
                {
                    _entryError = error;
                }

                break;
            case ConsoleKey.Backspace when _entry is { Length: > 0 }:
                _entry = _entry[..^1];
                _entryError = null;
                break;
            default:
                // Printable text only: an arrow or a function key carries no character and is consumed without typing one.
                bool printable = keyEvent.KeyChar != '\0' && !char.IsControl(keyEvent.KeyChar) && !keyEvent.Control && !keyEvent.Alt;
                if (printable && (_entry ?? string.Empty).Length < MaxEntryLength)
                {
                    _entry = (_entry ?? string.Empty) + keyEvent.KeyChar;
                    _entryError = null;
                }

                break;
        }

        StateHasChanged();
    }

    private bool MoveCursor(int delta)
    {
        return MoveTo(Cursor + delta);
    }

    private bool MoveTo(int index)
    {
        if (ItemCount > 0)
        {
            _cursor = Math.Clamp(index, 0, ItemCount - 1);
        }

        StateHasChanged();
        return true;
    }

    private static bool IsPlain(KeyEvent keyEvent) => !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift;
}
