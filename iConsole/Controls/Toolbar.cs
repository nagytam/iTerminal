using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>One entry on a <see cref="Toolbar"/>: a caption, an optional key hint and whether it can be used.</summary>
public sealed record ToolbarItem(string Text, string Hint = "", bool IsEnabled = true);

/// <summary>
/// A one-row strip of actions (<c> Open  Save  Close </c>) for a dialog that has several things to do and
/// no room for a button row. Left/Right (or Home/End) move the highlight over the enabled items, Enter or
/// Space invokes it and a click invokes the item under the pointer; <see cref="ItemInvoked"/> carries the
/// item's index. The key hint is drawn dim after the caption (<c> Open F3 </c>) when it fits.
/// </summary>
public class Toolbar : Control
{
    private List<ToolbarItem> _items = [];
    private int _active;

    public Toolbar(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(40, 1);
    }

    public event EventHandler<int>? ItemInvoked;

    public IReadOnlyList<ToolbarItem> Items
    {
        get => _items;
        set
        {
            _items = [.. value ?? []];
            _active = NextEnabled(-1, 1) is var first and >= 0 ? first : 0;
            StateHasChanged();
        }
    }

    public int ActiveIndex
    {
        get => _active;
        set
        {
            if (value >= 0 && value < _items.Count && _items[value].IsEnabled)
            {
                _active = value;
                StateHasChanged();
            }
        }
    }

    private int NextEnabled(int from, int direction)
    {
        for (int i = from + direction; i >= 0 && i < _items.Count; i += direction)
        {
            if (_items[i].IsEnabled)
            {
                return i;
            }
        }

        return -1;
    }

    private string Cell(int index)
    {
        ToolbarItem item = _items[index];
        return item.Hint.Length == 0 ? $" {item.Text} " : $" {item.Text} {item.Hint} ";
    }

    /// <summary>The item whose cell covers <paramref name="column"/> (relative to the toolbar), or -1.</summary>
    public int IndexAtColumn(int column)
    {
        int start = 0;
        for (int i = 0; i < _items.Count; i++)
        {
            int end = start + Cell(i).Length;
            if (column >= start && column < end)
            {
                return i;
            }

            start = end + 1;
        }

        return -1;
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (_items.Count == 0 || !IsEnabled)
        {
            yield break;
        }

        yield return ("Left/Right", "Move along the toolbar");
        yield return ("Home/End", "First or last item");
        yield return ("Enter/Space", "Run the item");
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        bool isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled || !IsFocused || !IsEnabled || _items.Count == 0)
        {
            return isHandled;
        }

        int target;
        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.RightArrow:
                target = NextEnabled(_active, 1);
                if (target >= 0)
                {
                    ActiveIndex = target;
                }

                return true;
            case ConsoleKey.LeftArrow:
                target = NextEnabled(_active, -1);
                if (target >= 0)
                {
                    ActiveIndex = target;
                }

                return true;
            case ConsoleKey.Home:
                target = NextEnabled(-1, 1);
                if (target >= 0)
                {
                    ActiveIndex = target;
                }

                return true;
            case ConsoleKey.End:
                target = NextEnabled(_items.Count, -1);
                if (target >= 0)
                {
                    ActiveIndex = target;
                }

                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                if (_active < _items.Count && _items[_active].IsEnabled)
                {
                    ItemInvoked?.Invoke(this, _active);
                }

                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        bool isHandled = await base.HandleMouseEventAsync(mouseEvent);
        if (mouseEvent.WheelDelta != 0 && IsEnabled && _items.Count > 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            int step = NextEnabled(_active, mouseEvent.WheelDelta > 0 ? -1 : 1);
            if (step >= 0)
            {
                ActiveIndex = step;
            }

            return true;
        }

        if (!mouseEvent.LeftClicked || !IsEnabled || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return isHandled;
        }

        int index = IndexAtColumn((int)mouseEvent.Position.X - (int)AbsolutePosition.X);
        if (index >= 0 && _items[index].IsEnabled)
        {
            if (!IsFocused)
            {
                SetFocus();
            }

            _active = index;
            ItemInvoked?.Invoke(this, index);
            StateHasChanged();
            return true;
        }

        return isHandled;
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
        ConxelString line = new(string.Empty, colorScheme.Foreground, colorScheme.Background);
        for (int i = 0; i < _items.Count && line.Count < width; i++)
        {
            ToolbarItem item = _items[i];
            bool active = i == _active && IsFocused && IsEnabled;
            Color foreground = !item.IsEnabled || !IsEnabled ? colorScheme.DisabledForeground
                : active ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            Color background = active ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
            line.Append(Cell(i), foreground, background);
            line.Append(" ", colorScheme.Foreground, colorScheme.Background);
        }

        line.Expand(width, colorScheme.Foreground, colorScheme.Background);
        buffer.Write(ZOrder, AbsolutePosition, ViewSize, line);
    }
}
