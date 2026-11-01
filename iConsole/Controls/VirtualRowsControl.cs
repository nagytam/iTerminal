using iConsole.Localization;
using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Model;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// The shared part of <see cref="VirtualListBox{T}"/> and <see cref="VirtualTable{T}"/>: a cursor and a scroll position
/// over an <see cref="IVirtualDataSource{T}"/>. Only the rows on screen are ever read from the source, so the cost of a key
/// press or a frame depends on the height of the control, not on how many rows there are, and a source that loads rows in
/// the background shows a placeholder for a row until it arrives.
/// </summary>
/// <remarks>
/// The cursor and the top row are plain row numbers kept here rather than in a <see cref="ScrollableModel"/>, whose scroll
/// commands move the cursor and the viewport by separate rules. Enter and a second click on the cursor row raise
/// <see cref="ItemActivated"/>. Unlike <see cref="ListBox{T}"/> there is no marking and no per-row projection.
/// </remarks>
public abstract class VirtualRowsControl<T> : Control
{
    /// <summary>Rows the wheel moves the cursor by in one notch.</summary>
    private const int WheelRows = 3;

    private int _cursor;
    private int _top;

    /// <summary>False after <see cref="ScrollTo"/>: the viewport may then leave the cursor off screen until the cursor moves.</summary>
    private bool _followCursor = true;

    protected VirtualRowsControl(IVirtualDataSource<T> source, IThemeService themeService) : base(themeService)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        CanFocus = true;
        Source.Changed += OnSourceChanged;
    }

    public IVirtualDataSource<T> Source { get; }

    /// <summary>The number of rows in the source.</summary>
    public int Count => Source.Count;

    /// <summary>The row number of the highlighted row.</summary>
    public int CursorIndex => _cursor;

    /// <summary>The row number of the first row on screen.</summary>
    public int TopIndex => _top;

    /// <summary>The highlighted item, or default when there is none or it has not loaded yet.</summary>
    public T? SelectedItem => Source.TryGet(_cursor, out T? item) ? item : default;

    /// <summary>Drawn on the first row, in the disabled colours, while the source has no rows.</summary>
    public string EmptyText { get; set; } = string.Empty;

    /// <summary>Drawn in the disabled colours for a row the source has not loaded yet.</summary>
    public string LoadingText { get; set; } = "…";

    /// <summary>Raised with the row number when the user presses Enter on a row, or clicks the cursor row again.</summary>
    public event EventHandler<int>? ItemActivated;

    /// <summary>Raised when the cursor moves to another row.</summary>
    public event EventHandler<int>? CursorChanged;

    /// <summary>Rows above the body (a table's header).</summary>
    protected virtual int HeaderRows => 0;

    /// <summary>The number of rows the body can show.</summary>
    protected int BodyHeight => Math.Max(0, (int)ViewSize.Height - HeaderRows);

    /// <summary>The text of <paramref name="item"/> for the row <paramref name="index"/>, padded or cut to the control's width by the caller.</summary>
    protected abstract string RowText(T item, int index);

    /// <summary>Draws the rows above the body.</summary>
    protected virtual void DrawHeader(IBuffer buffer, ColorScheme colorScheme)
    {
    }

    /// <summary>Moves the cursor to <paramref name="index"/> (kept inside the rows) and scrolls it into view.</summary>
    public void MoveCursorTo(int index)
    {
        int count = Source.Count;
        int target = count == 0 ? 0 : Math.Clamp(index, 0, count - 1);
        bool moved = target != _cursor;
        _cursor = target;
        _followCursor = true;
        Normalize();
        if (moved)
        {
            CursorChanged?.Invoke(this, _cursor);
        }

        StateHasChanged();
    }

    /// <summary>
    /// Scrolls so that row <paramref name="index"/> is the first on screen, without moving the cursor. The cursor may stay
    /// off screen; the next cursor move brings the viewport back to it.
    /// </summary>
    public void ScrollTo(int index)
    {
        _top = index;
        _followCursor = false;
        Normalize();
        StateHasChanged();
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (!IsEnabled)
        {
            yield break;
        }

        yield return ("Up/Down", UiText.Get("Hint.MoveCursor", "Move the cursor"));
        yield return ("PageUp/PageDown", UiText.Get("Hint.MoveCursorPage", "Move the cursor a page"));
        yield return ("Home/End", UiText.Get("Hint.FirstOrLastRow", "First or last row"));
        if (Source.Count > 0)
        {
            yield return ("Enter", UiText.Get("Hint.OpenHighlightedRow", "Open the highlighted row"));
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (IsFocused && IsEnabled && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift && Source.Count > 0)
        {
            int page = Math.Max(1, BodyHeight - 1);
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    MoveCursorTo(_cursor - 1);
                    return true;
                case ConsoleKey.DownArrow:
                    MoveCursorTo(_cursor + 1);
                    return true;
                case ConsoleKey.PageUp:
                    MoveCursorTo(_cursor - page);
                    return true;
                case ConsoleKey.PageDown:
                    MoveCursorTo(_cursor + page);
                    return true;
                case ConsoleKey.Home:
                    MoveCursorTo(0);
                    return true;
                case ConsoleKey.End:
                    MoveCursorTo(Source.Count - 1);
                    return true;
                case ConsoleKey.Enter:
                    ItemActivated?.Invoke(this, _cursor);
                    return true;
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// A click on a row moves the cursor there and takes the focus; a second click on the cursor row activates it. The wheel
    /// moves the cursor. A click above the body goes to <see cref="OnHeaderClick"/>.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        long relativeX = mouseEvent.Position.X - AbsolutePosition.X;
        long relativeY = mouseEvent.Position.Y - AbsolutePosition.Y;
        bool isInside = relativeX >= 0 && relativeX < ViewSize.Width && relativeY >= 0 && relativeY < ViewSize.Height;
        if (!isInside || !IsEnabled)
        {
            return isHandled;
        }

        if (relativeY < HeaderRows)
        {
            return mouseEvent.LeftClicked && OnHeaderClick((int)relativeX) ? true : isHandled;
        }

        if (Source.Count == 0)
        {
            return isHandled;
        }

        if (mouseEvent.WheelDelta != 0)
        {
            MoveCursorTo(_cursor + (mouseEvent.WheelDelta > 0 ? -WheelRows : WheelRows));
            return true;
        }

        if (mouseEvent.LeftClicked && !mouseEvent.IsRelease)
        {
            long index = (long)_top + relativeY - HeaderRows;
            if (index < Source.Count)
            {
                if (!IsFocused)
                {
                    SetFocus();
                }

                bool wasCursor = index == _cursor;
                MoveCursorTo((int)index);
                if (wasCursor)
                {
                    ItemActivated?.Invoke(this, _cursor);
                }

                return true;
            }
        }

        return isHandled;
    }

    /// <summary>A click on the header row at <paramref name="x"/>, relative to the control. Returns true when it was used.</summary>
    protected virtual bool OnHeaderClick(int x) => false;

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int width = (int)ViewSize.Width;
        int count = Source.Count;
        Normalize();

        DrawHeader(buffer, colorScheme);

        // Say which rows are about to be needed; a source that loads in the background starts on them now.
        int body = BodyHeight;
        Source.Prefetch(_top, body);

        // Every body row is painted, the ones below the last included: nothing clears a layer between frames.
        for (int y = 0; y < body; y++)
        {
            int index = _top + y;
            Position position = new(AbsolutePosition.X, AbsolutePosition.Y + HeaderRows + y);
            string text;
            Color foreground;
            Color background;
            if (index >= count)
            {
                text = count == 0 && y == 0 ? EmptyText : string.Empty;
                foreground = text.Length > 0 ? colorScheme.DisabledForeground : colorScheme.Foreground;
                background = colorScheme.Background;
            }
            else
            {
                bool isCursor = index == _cursor;
                if (Source.TryGet(index, out T? item) && item is not null)
                {
                    text = RowText(item, index);
                    // A disabled list greys its rows, the cursor row included, like every other disabled control.
                    VisualState state = !IsEnabled ? VisualState.Disabled
                        : isCursor ? (IsFocused ? VisualState.Focused : VisualState.Selected)
                        : VisualState.Normal;
                    GetColor(colorScheme, state, out foreground, out background);
                }
                else
                {
                    text = LoadingText;
                    foreground = colorScheme.DisabledForeground;
                    background = isCursor ? colorScheme.SelectedBackground : colorScheme.Background;
                }
            }

            buffer.Write(ZOrder, position, ViewSize, new ConxelString(text.Cut(width).PadRight(width), foreground, background));
        }
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        switch (visualState)
        {
            case VisualState.Focused:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.SelectedFocusedBackground;
                break;
            case VisualState.Selected:
                foregroundColor = colorScheme.SelectedForeground;
                backgroundColor = colorScheme.SelectedBackground;
                break;
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }

    private void OnSourceChanged(object? sender, EventArgs e)
    {
        // May come from a worker thread: only ask for a repaint here. The cursor is brought back inside the rows the next
        // time something reads it, on the thread that draws.
        StateHasChanged();
    }

    /// <summary>
    /// Keeps the cursor inside the rows, the top row inside the last full page, and, while the viewport follows the cursor,
    /// the cursor on screen.
    /// </summary>
    private void Normalize()
    {
        int count = Source.Count;
        int body = BodyHeight;
        _cursor = count == 0 ? 0 : Math.Clamp(_cursor, 0, count - 1);
        int maxTop = Math.Max(0, count - body);
        // A viewport moved by ScrollTo keeps its place: only the range limits below apply to it.
        if (_followCursor)
        {
            if (_cursor < _top)
            {
                _top = _cursor;
            }
            else if (body > 0 && _cursor >= _top + body)
            {
                _top = _cursor - body + 1;
            }
        }

        _top = Math.Clamp(_top, 0, maxTop);
    }
}
