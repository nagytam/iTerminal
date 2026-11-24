using iConsole.Buffers;
using iConsole.Data;
using iConsole.Localization;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A <see cref="HexViewer"/> whose bytes can be typed over. A hex digit replaces the high nibble of the byte under the
/// cursor, the next digit the low one, and the cursor then moves on. Only bytes are overwritten: the length never changes,
/// so an edit can never shift the bytes after it. Arrows move by byte and by row; Home and End go to the ends of the row,
/// Ctrl+Home and Ctrl+End to the ends of the file.
/// </summary>
public class HexEditorControl : HexViewer
{
    private int _column;
    private bool _lowNibble;

    public HexEditorControl(IThemeService themeService) : base(themeService)
    {
    }

    /// <summary>Raised when a byte changes value. Retyping the same value is not an edit.</summary>
    public event Action? Edited;

    /// <summary>
    /// Asked before each change: when it answers true the bytes are shown but not typed over. The editor passes the text
    /// editor's read-only state, so the hex view and the text view stay read-only together.
    /// </summary>
    public Func<bool>? ReadOnlyCheck { get; set; }

    /// <summary>The offset of the byte under the cursor, or 0 for an empty dump.</summary>
    public long CursorIndex
    {
        get
        {
            if (Length == 0)
            {
                return 0;
            }

            long index = ScrollableModel.CursorPosition.Y * BytesPerRow + _column;
            return Math.Min(index, Length - 1);
        }
    }

    /// <summary>Shows <paramref name="bytes"/> from the start and puts the cursor on the first byte, in the high nibble.</summary>
    public void Load(byte[] bytes)
    {
        Data = bytes;
        _column = 0;
        _lowNibble = false;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType != KeyPressType.Down || !IsEnabled || Length == 0)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        if (!keyEvent.Alt && !keyEvent.Control && TryHexValue(keyEvent.KeyChar, out int value))
        {
            TypeDigit(value);
            return true;
        }

        if (keyEvent.Alt)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        int row = BytesPerRow;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow:
                MoveTo(CursorIndex - 1);
                return true;
            case ConsoleKey.RightArrow:
                MoveTo(CursorIndex + 1);
                return true;
            case ConsoleKey.UpArrow:
                MoveTo(CursorIndex - row);
                return true;
            case ConsoleKey.DownArrow:
                MoveTo(CursorIndex + row);
                return true;
            case ConsoleKey.PageUp:
                MoveTo(CursorIndex - row * Math.Max(1, (int)ViewSize.Height));
                return true;
            case ConsoleKey.PageDown:
                MoveTo(CursorIndex + row * Math.Max(1, (int)ViewSize.Height));
                return true;
            case ConsoleKey.Home when keyEvent.Control:
                MoveTo(0);
                return true;
            case ConsoleKey.End when keyEvent.Control:
                MoveTo(Length - 1);
                return true;
            case ConsoleKey.Home:
                MoveTo(CursorIndex - _column);
                return true;
            case ConsoleKey.End:
                MoveTo(Math.Min(CursorIndex - _column + row - 1, Length - 1));
                return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>A click moves the cursor to its row and keeps the column; the nibble starts over on a new row.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        long rowBefore = ScrollableModel.CursorPosition.Y;
        bool handled = await base.HandleMouseEventAsync(mouseEvent);
        if (ScrollableModel.CursorPosition.Y != rowBefore)
        {
            _lowNibble = false;
        }

        return handled;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || Length == 0)
        {
            yield break;
        }

        yield return ("0-9 A-F", UiText.Get("Editor.HintHexDigit", "Type a hex digit"));
        yield return ("Arrows", UiText.Get("Editor.HintHexMove", "Move the cursor"));
        yield return ("Home/End", UiText.Get("Editor.HintHexRow", "Start or end of the row"));
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (Length == 0)
        {
            return;
        }

        long index = CursorIndex;
        long visibleRow = index / BytesPerRow - ScrollableModel.ScrollPosition.Y;
        if (visibleRow < 0 || visibleRow >= ViewSize.Height)
        {
            return;
        }

        int column = (int)(index % BytesPerRow);
        byte value = Bytes[index];
        var colorScheme = ThemeService.CurrentColorScheme;
        Color fg, bg;
        if (!IsEnabled)
        {
            fg = colorScheme.DisabledForeground;
            bg = colorScheme.DisabledBackground;
        }
        else
        {
            fg = IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
            bg = IsFocused ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
        }

        // The byte under the cursor is drawn again over the row, in the cursor colours: its two hex digits, then its ASCII form.
        string pair = $"{value:X2}";
        long y = AbsolutePosition.Y + visibleRow;
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + HexColumn(column, BytesPerRow), (uint)y),
            new Size(2, 1),
            new ConxelString(pair, fg, bg));
        char ascii = value is >= 32 and < 127 ? (char)value : '.';
        buffer.Write(ZOrder,
            new Position(AbsolutePosition.X + AsciiColumn(column, BytesPerRow), (uint)y),
            new Size(1, 1),
            new ConxelString(ascii.ToString(), fg, bg));
    }

    private void TypeDigit(int value)
    {
        if (ReadOnlyCheck?.Invoke() == true)
        {
            return;
        }

        long index = CursorIndex;
        byte current = Bytes[index];
        byte updated = _lowNibble
            ? (byte)((current & 0xF0) | value)
            : (byte)((value << 4) | (current & 0x0F));
        if (updated != current)
        {
            Bytes[index] = updated;
            Edited?.Invoke();
        }

        if (_lowNibble)
        {
            _lowNibble = false;
            MoveTo(index + 1);
        }
        else
        {
            _lowNibble = true;
            StateHasChanged();
        }
    }

    /// <summary>Puts the cursor on byte <paramref name="index"/> (clamped to the file), with the high nibble next.</summary>
    private void MoveTo(long index)
    {
        long target = Math.Clamp(index, 0, Length - 1);
        ScrollableModel.CursorPosition.Y = target / BytesPerRow;
        _column = (int)(target % BytesPerRow);
        _lowNibble = false;
        ScrollableModel.ScrollToVisible();
        StateHasChanged();
    }

    private static bool TryHexValue(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
        return value >= 0;
    }
}
