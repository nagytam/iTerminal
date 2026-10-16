using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A single-line selector that drops down a scrollable list of options. Closed, it shows the
/// selected item in a <see cref="TextBox"/>-styled well with a ▾ indicator. Opening it raises a
/// modal <see cref="Popup"/> hosting a <see cref="PopupListView"/>, which captures input until the
/// user commits a choice or dismisses it; styling, focus and read-only behaviour follow the other
/// interactive controls.
///
/// <para>Keys (closed): Enter / Space / F4 / Alt+Down open the list, Up/Down cycle the selection
/// without opening, and the wheel cycles it too. Once open, navigation lives in the dropdown
/// (Up/Down/PageUp/PageDown/Home/End move, Enter / Space / click commit, Esc / outside-click
/// dismiss).</para>
/// </summary>
public class ComboBox<T> : Control, IReadOnly
{
    private const string DropDownGlyph = "▾";

    private readonly List<T> _items = [];
    private int _selectedIndex = -1;
    private Popup? _popup;

    public ComboBox(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(20, 1);
    }

    public string Label { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public int MaxDropDownRows { get; set; } = 6;
    public Func<T, string> DisplaySelector { get; set; } = item => item?.ToString() ?? string.Empty;

    public event EventHandler? SelectionChanged;

    public IReadOnlyList<T> Items => _items;

    public bool IsOpen => _popup?.IsOpen ?? false;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            var clamped = _items.Count == 0 ? -1 : Math.Clamp(value, 0, _items.Count - 1);
            if (_selectedIndex != clamped)
            {
                _selectedIndex = clamped;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                StateHasChanged();
            }
        }
    }

    public T? SelectedItem =>
        _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : default;

    public void SetItems(IEnumerable<T> items)
    {
        _items.Clear();
        _items.AddRange(items);
        _selectedIndex = _items.Count == 0
            ? -1
            : Math.Clamp(_selectedIndex < 0 ? 0 : _selectedIndex, 0, _items.Count - 1);
        StateHasChanged();
    }

    /// <summary>Opens the dropdown as a modal overlay anchored below the control.</summary>
    public async Task OpenAsync()
    {
        if (IsReadOnly || _items.Count == 0 || IsOpen)
        {
            return;
        }

        var rows = _items.Select(item => DisplaySelector(item)).ToList();
        var visibleRows = Math.Max(1, Math.Min(MaxDropDownRows, _items.Count));

        var listView = new PopupListView(ThemeService)
        {
            Committed = index =>
            {
                SelectedIndex = index;
                _popup?.Close();
            },
            Cancelled = () => _popup?.Close(),
        };
        listView.SetItems(rows, Math.Max(0, _selectedIndex));

        var popup = new Popup(ThemeService)
        {
            Placement = PopupPlacement.Below,
            HasBorder = true,
            ViewSize = new Size((int)ViewSize.Width, visibleRows + 2),
        };
        popup.SetContent(listView);
        popup.Closed += (_, _) =>
        {
            _popup = null;
            StateHasChanged();
        };
        _popup = popup;

        await popup.ShowAsync(this);
        StateHasChanged();
    }

    public new void Close() => _popup?.Close();

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);

        // While open the modal popup owns input; only the closed control reacts here. And only
        // when it has focus: a key the focused control declines is offered to every control, so
        // an unfocused combo answering Enter opened its list over the dialog's own Enter action.
        if (isHandled || IsReadOnly || IsOpen || !IsFocused || !IsEnabled)
        {
            return isHandled;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
            case ConsoleKey.F4:
                await OpenAsync();
                return true;
            case ConsoleKey.DownArrow:
                if (keyEvent.Alt)
                {
                    await OpenAsync();
                }
                else
                {
                    MoveSelection(1);
                }
                return true;
            case ConsoleKey.UpArrow:
                MoveSelection(-1);
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (IsReadOnly || IsOpen)
        {
            return isHandled;
        }

        var relativeX = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
        var relativeY = (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y;
        var onClosedLine = relativeY == 0 && relativeX >= 0 && relativeX < (int)ViewSize.Width;

        if (mouseEvent.WheelDelta != 0 && onClosedLine)
        {
            MoveSelection(mouseEvent.WheelDelta > 0 ? -1 : 1);
            return true;
        }

        if ((mouseEvent.LeftClicked || mouseEvent.RightClicked) && onClosedLine)
        {
            await OpenAsync();
            return true;
        }

        return isHandled;
    }

    private void MoveSelection(int delta)
    {
        if (_items.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp((_selectedIndex < 0 ? 0 : _selectedIndex) + delta, 0, _items.Count - 1);
    }

    public override void Draw(IBuffer buffer)
    {
        base.Draw(buffer);
        if (ViewSize.IsEmpty)
        {
            return;
        }

        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out var background);
        DrawClosedLine(buffer, colorScheme, foreground, background, (int)ViewSize.Width);
    }

    private void DrawClosedLine(IBuffer buffer, ColorScheme colorScheme, Color foreground, Color background, int width)
    {
        var prefix = string.IsNullOrEmpty(Label) ? string.Empty : Label + ": ";
        var line = new ConxelString(prefix, foreground, background);
        line.HighlightHotKey(HotConsoleKey, HotKeyChar, colorScheme.SelectedFocusedForeground, background);

        var wellForeground = Color.Green;
        var wellBackground = IsReadOnly ? Color.DarkYellow : Color.Yellow;

        // Reserve the leading "[", trailing "]" and the ▾ glyph when sizing the visible text.
        var innerWidth = Math.Max(0, width - line.Count - 3);
        var selectedText = SelectedItem is not null ? DisplaySelector(SelectedItem) : string.Empty;

        line.Append(Border.Start, wellForeground, wellBackground);
        line.Append(Fit(selectedText, innerWidth), wellForeground, wellBackground);
        line.Append(Border.End, wellForeground, wellBackground);

        var glyphForeground = IsReadOnly
            ? colorScheme.DisabledForeground
            : IsFocused ? colorScheme.SelectedFocusedForeground : foreground;
        line.Append(DropDownGlyph, glyphForeground, background);

        if (line.Count < width)
        {
            line.Expand(width, foreground, background);
        }

        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }

    private static string Fit(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        return text.Length > width ? text[..width] : text.PadRight(width);
    }

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foregroundColor, out Color backgroundColor)
    {
        if (IsReadOnly)
        {
            visualState = VisualState.Disabled;
        }

        switch (visualState)
        {
            case VisualState.Disabled:
                foregroundColor = colorScheme.DisabledForeground;
                backgroundColor = colorScheme.DisabledBackground;
                break;
            case VisualState.Focused:
                foregroundColor = colorScheme.SelectedFocusedForeground;
                backgroundColor = colorScheme.Background;
                break;
            case VisualState.Normal:
            default:
                foregroundColor = colorScheme.Foreground;
                backgroundColor = colorScheme.Background;
                break;
        }
    }
}
