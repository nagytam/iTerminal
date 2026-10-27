using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Data;
using iConsole.Display;
using iConsole.Model;
using iConsole.Services;
using System.Text;

namespace iConsole.Controls;

/// <summary>
/// Displays <see cref="Data"/> as a classic hex dump:
/// <code>
/// 00000000  48 65 6C 6C 6F 2C 20 57  6F 72 6C 64 21 0A 00 00  |Hello, World!...|
/// </code>
/// Up/Down/PgUp/PgDn/Home/End scroll by row; <see cref="SelectedOffset"/> reflects the
/// first byte of the cursor row.
/// </summary>
public class HexViewer : Control
{
    private byte[] _data = [];
    private int _bytesPerRow = 16;

    public HexViewer(IThemeService themeService) : base(themeService)
    {
        ScrollableModel = new ScrollableModel();
        CanFocus = true;

        AddCommand(new ScrollDownCommand(ScrollableModel));
        AddCommand(new ScrollUpCommand(ScrollableModel));
        AddCommand(new ScrollPageDownCommand(ScrollableModel));
        AddCommand(new ScrollPageUpCommand(ScrollableModel));
        AddCommand(new ScrollToHomeVerticalCommand(ScrollableModel));
        AddCommand(new ScrollToEndVerticalCommand(ScrollableModel));
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled || RowCount == 0)
        {
            yield break;
        }

        yield return ("Up/Down", "Scroll a row");
        yield return ("PageUp/PageDown", "Scroll a page");
        yield return ("Home/End", "First or last row");
    }

    /// <summary>The bytes to display. Setting this resets the cursor to row 0.</summary>
    public byte[] Data
    {
        get => _data;
        set
        {
            _data = value ?? [];
            ScrollableModel.CursorPosition.Y = 0;
            ScrollableModel.ScrollPosition.Y = 0;
            ScrollableModel.ScrollableSize = new Size(ViewSize.Width, RowCount);
            StateHasChanged();
        }
    }

    /// <summary>Number of bytes shown per row. Default is 16.</summary>
    public int BytesPerRow
    {
        get => _bytesPerRow;
        set
        {
            _bytesPerRow = Math.Max(1, value);
            ScrollableModel.ScrollableSize = new Size(ViewSize.Width, RowCount);
            StateHasChanged();
        }
    }

    public ScrollableModel ScrollableModel { get; }

    /// <summary>
    /// Puts the cursor on the row of a byte and scrolls so that row sits a third of the way down the view. An offset past
    /// the end goes to the last byte; an empty dump has nothing to go to.
    /// </summary>
    public void ShowOffset(long offset)
    {
        if (_data.Length == 0)
        {
            return;
        }

        long clamped = Math.Clamp(offset, 0, _data.Length - 1);
        long row = clamped / BytesPerRow;
        int visibleRows = Math.Max(1, (int)ViewSize.Height);
        long maxScroll = Math.Max(0, RowCount - visibleRows);
        ScrollableModel.CursorPosition.Y = row;
        ScrollableModel.ScrollPosition.Y = Math.Clamp(row - visibleRows / 3, 0, maxScroll);
        StateHasChanged();
    }

    /// <summary>The size of the dump in bytes, for a go-to-offset to check its target against.</summary>
    public int Length => _data.Length;

    /// <summary>
    /// Reads an offset typed by a person: a decimal number, or a hexadecimal one written with a <c>0x</c> prefix or an
    /// <c>h</c> suffix (<c>0x1F</c>, <c>1Fh</c>). Returns false for anything else.
    /// </summary>
    public static bool TryParseOffset(string? text, out long offset)
    {
        offset = 0;
        string value = text?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return false;
        }

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(value[2..], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out offset)
                && offset >= 0;
        }

        if (value.EndsWith('h') || value.EndsWith('H'))
        {
            return long.TryParse(value[..^1], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out offset)
                && offset >= 0;
        }

        return long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out offset)
            && offset >= 0;
    }

    /// <summary>Byte offset of the first byte in the cursor row.</summary>
    public int SelectedOffset => (int)ScrollableModel.CursorPosition.Y * BytesPerRow;

    private int RowCount => _data.Length == 0 ? 0 : (_data.Length + BytesPerRow - 1) / BytesPerRow;

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        ScrollableModel.ViewSize = ViewSize;
        ScrollableModel.ScrollableSize = new Size(ViewSize.Width, RowCount);
    }

    /// <summary>Rows the wheel moves the cursor by in one notch.</summary>
    private const int WheelRows = 3;

    /// <summary>A click on a row moves the cursor there and takes the focus; the wheel moves it three rows.</summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        long relativeX = (long)mouseEvent.Position.X - (long)AbsolutePosition.X;
        long relativeY = (long)mouseEvent.Position.Y - (long)AbsolutePosition.Y;
        if (!IsEnabled || RowCount == 0 || relativeX < 0 || relativeX >= ViewSize.Width || relativeY < 0 || relativeY >= ViewSize.Height)
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            long target = ScrollableModel.CursorPosition.Y + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows);
            ScrollableModel.CursorPosition.Y = Math.Clamp(target, 0, RowCount - 1);
            ScrollableModel.ScrollToVisible();
            StateHasChanged();
            return true;
        }

        if (mouseEvent.LeftClicked)
        {
            long row = (long)ScrollableModel.ScrollPosition.Y + relativeY;
            if (row < RowCount)
            {
                if (!IsFocused)
                {
                    SetFocus();
                }

                ScrollableModel.CursorPosition.Y = row;
                ScrollableModel.ScrollToVisible();
                StateHasChanged();
                return true;
            }
        }

        return isHandled;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);

        var colorScheme = ThemeService.CurrentColorScheme;
        var scrollOffset = (int)ScrollableModel.ScrollPosition.Y;
        var cursorRow = (int)ScrollableModel.CursorPosition.Y;

        for (int y = 0; y < (int)ViewSize.Height; y++)
        {
            var rowIndex = scrollOffset + y;
            if (rowIndex >= RowCount)
            {
                // Every row is painted, the ones past the data blank: a layer is not cleared
                // between frames, so a shorter dump would keep showing the rows it no longer has.
                buffer.Write(ZOrder,
                    new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + y)),
                    ViewSize,
                    new ConxelString(string.Empty.PadRight((int)ViewSize.Width), colorScheme.Foreground, colorScheme.Background));
                continue;
            }

            var rowText = FormatRow(rowIndex);
            var isCursor = rowIndex == cursorRow;

            Color fg, bg;
            if (!IsEnabled)
            {
                // A disabled viewer is greyed like every other disabled control, the cursor row included.
                fg = colorScheme.DisabledForeground;
                bg = colorScheme.DisabledBackground;
            }
            else if (isCursor)
            {
                fg = IsFocused ? colorScheme.SelectedFocusedForeground : colorScheme.SelectedForeground;
                bg = IsFocused ? colorScheme.SelectedFocusedBackground : colorScheme.SelectedBackground;
            }
            else
            {
                fg = colorScheme.Foreground;
                bg = colorScheme.Background;
            }

            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X, (uint)(AbsolutePosition.Y + y)),
                ViewSize,
                new ConxelString(rowText, fg, bg));
        }
    }

    private string FormatRow(int rowIndex)
    {
        var offset = rowIndex * BytesPerRow;
        var sb = new StringBuilder();

        // Offset column
        sb.Append(offset.ToString("X8"));
        sb.Append("  ");

        // Hex columns — two groups of BytesPerRow/2, separated by an extra space
        int half = BytesPerRow / 2;
        for (int i = 0; i < BytesPerRow; i++)
        {
            if (i > 0) sb.Append(' ');
            if (i == half) sb.Append(' '); // mid-row gap
            var byteOffset = offset + i;
            sb.Append(byteOffset < _data.Length ? _data[byteOffset].ToString("X2") : "  ");
        }

        // ASCII column
        sb.Append("  |");
        for (int i = 0; i < BytesPerRow; i++)
        {
            var byteOffset = offset + i;
            if (byteOffset < _data.Length)
            {
                var b = _data[byteOffset];
                sb.Append(b >= 32 && b < 127 ? (char)b : '.');
            }
            else
            {
                sb.Append(' ');
            }
        }
        sb.Append('|');

        return sb.ToString().PadRight((int)ViewSize.Width);
    }
}
