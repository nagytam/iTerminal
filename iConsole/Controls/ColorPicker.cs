using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// A grid of the sixteen console colours as swatches (4 × 4, three cells wide each) for picking a colour
/// without knowing its name. The arrows move the cursor, Enter or Space picks it, a click picks the swatch
/// under the pointer; <see cref="SelectedColor"/> is marked with a ✓ and the cursor with brackets.
/// </summary>
public class ColorPicker : Control, IReadOnly
{
    public const int GridColumns = 4;
    public const int SwatchWidth = 3;

    private static readonly Color[] Colors = Enum.GetValues<Color>();

    private int _cursor;
    private Color _selected = Color.White;

    public ColorPicker(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(GridColumns * SwatchWidth, (Colors.Length + GridColumns - 1) / GridColumns);
    }

    public bool IsReadOnly { get; set; }

    public event EventHandler? SelectedColorChanged;

    public int CursorIndex => _cursor;

    public Color SelectedColor
    {
        get => _selected;
        set
        {
            if (_selected != value)
            {
                _selected = value;
                int index = Array.IndexOf(Colors, value);
                if (index >= 0)
                {
                    _cursor = index;
                }
                SelectedColorChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    /// <summary>The cursor index after moving by (<paramref name="dx"/>, <paramref name="dy"/>), stopping at the edges.</summary>
    public static int Move(int index, int dx, int dy, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        index = Math.Clamp(index, 0, count - 1);
        int column = Math.Clamp(index % GridColumns + dx, 0, GridColumns - 1);
        int row = Math.Max(0, index / GridColumns + dy);
        return Math.Min(row * GridColumns + column, count - 1);
    }

    private void MoveCursor(int dx, int dy)
    {
        _cursor = Move(_cursor, dx, dy, Colors.Length);
        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Arrows", "Move the cursor over the palette");
        yield return ("Home/End", "First or last colour");
        if (!IsReadOnly)
        {
            yield return ("Enter/Space", "Choose the colour under the cursor");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.LeftArrow:
                MoveCursor(-1, 0);
                return true;
            case ConsoleKey.RightArrow:
                MoveCursor(1, 0);
                return true;
            case ConsoleKey.UpArrow:
                MoveCursor(0, -1);
                return true;
            case ConsoleKey.DownArrow:
                MoveCursor(0, 1);
                return true;
            case ConsoleKey.Home:
                _cursor = keyEvent.Control ? 0 : _cursor / GridColumns * GridColumns;
                StateHasChanged();
                return true;
            case ConsoleKey.End:
                _cursor = keyEvent.Control
                    ? Colors.Length - 1
                    : Math.Min(_cursor / GridColumns * GridColumns + GridColumns - 1, Colors.Length - 1);
                StateHasChanged();
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                if (!IsReadOnly)
                {
                    SelectedColor = Colors[_cursor];
                }

                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (!IsReadOnly && IsEnabled && mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            // A row up or down, as the arrow keys move; choosing is still a click, Enter or Space.
            MoveCursor(0, mouseEvent.WheelDelta > 0 ? -1 : 1);
            return true;
        }

        if (IsReadOnly || !IsEnabled || !mouseEvent.LeftClicked || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        int column = ((int)mouseEvent.Position.X - (int)AbsolutePosition.X) / SwatchWidth;
        int row = (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y;
        int index = row * GridColumns + column;
        // A stretched control has cells right of the grid: they must not wrap into the next row's swatches.
        if (column < GridColumns && index >= 0 && index < Colors.Length)
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            _cursor = index;
            SelectedColor = Colors[index];
            StateHasChanged();
        }

        return true;
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        for (int row = 0; row < (int)ViewSize.Height; row++)
        {
            ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
            for (int column = 0; column < GridColumns; column++)
            {
                int index = row * GridColumns + column;
                if (index >= Colors.Length)
                {
                    break;
                }

                Color swatch = Colors[index];
                // Black ink on light swatches, white on dark, so the marks stay readable on every colour.
                Color ink = swatch is Color.Black or Color.DarkBlue or Color.DarkRed or Color.DarkMagenta or Color.Blue or Color.DarkGreen or Color.DarkGray
                    ? Color.White
                    : Color.Black;
                bool atCursor = index == _cursor && IsFocused;
                string cell = atCursor
                    ? "[" + (swatch == _selected ? "✓" : " ") + "]"
                    : " " + (swatch == _selected ? "✓" : " ") + " ";
                line.Append(cell, ink, swatch);
            }

            line.Expand(width, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + (uint)row), new Size(width, 1), line);
        }
    }
}
