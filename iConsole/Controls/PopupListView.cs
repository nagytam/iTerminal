using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A focusable, scrollable list of string rows designed to live inside a <see cref="Popup"/> — the
/// content behind a <see cref="ComboBox{T}"/> dropdown (and reusable for menus). It owns its
/// highlight + scroll and reports the user's choice through <see cref="Committed"/> (Enter / Space /
/// click) or <see cref="Cancelled"/> (Esc), leaving the host to decide what that means.
/// </summary>
public class PopupListView : Control
{
    private IReadOnlyList<string> _items = [];
    private int _highlight;
    private int _scroll;

    public PopupListView(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        ViewSize = new Size(20, 6);
    }

    /// <summary>Invoked with the chosen row index when the user commits a selection.</summary>
    public Action<int>? Committed { get; set; }

    /// <summary>Invoked when the user dismisses the list without choosing (Esc).</summary>
    public Action? Cancelled { get; set; }

    public IReadOnlyList<string> Items => _items;

    public int Highlight => _highlight;

    public void SetItems(IReadOnlyList<string> items, int highlight = 0)
    {
        _items = items;
        _highlight = items.Count == 0 ? 0 : Math.Clamp(highlight, 0, items.Count - 1);
        _scroll = 0;
        EnsureHighlightVisible();
        StateHasChanged();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        var isHandled = await base.HandleKeyEventAsync(keyEvent);
        if (isHandled)
        {
            return true;
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                MoveHighlight(-1);
                return true;
            case ConsoleKey.DownArrow:
                MoveHighlight(1);
                return true;
            case ConsoleKey.PageUp:
                MoveHighlight(-Rows());
                return true;
            case ConsoleKey.PageDown:
                MoveHighlight(Rows());
                return true;
            case ConsoleKey.Home:
                MoveHighlightTo(0);
                return true;
            case ConsoleKey.End:
                MoveHighlightTo(_items.Count - 1);
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                Commit(_highlight);
                return true;
            case ConsoleKey.Escape:
                Cancelled?.Invoke();
                return true;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        var isHandled = await base.HandleMouseEventAsync(mouseEvent);

        var relativeX = (int)mouseEvent.Position.X - (int)AbsolutePosition.X;
        var relativeY = (int)mouseEvent.Position.Y - (int)AbsolutePosition.Y;
        var withinX = relativeX >= 0 && relativeX < (int)ViewSize.Width;

        if (mouseEvent.WheelDelta != 0 && withinX && relativeY >= 0 && relativeY < Rows())
        {
            MoveHighlight(mouseEvent.WheelDelta > 0 ? -1 : 1);
            return true;
        }

        if ((mouseEvent.LeftClicked || mouseEvent.RightClicked) && withinX && relativeY >= 0 && relativeY < Rows())
        {
            var index = _scroll + relativeY;
            if (index >= 0 && index < _items.Count)
            {
                Commit(index);
                return true;
            }
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

        var colorScheme = ThemeService.CurrentColorScheme;
        var width = (int)ViewSize.Width;
        var rows = Rows();

        EnsureHighlightVisible();
        for (var i = 0; i < rows; i++)
        {
            var index = _scroll + i;
            Color foreground;
            Color background;
            string text;

            if (index >= 0 && index < _items.Count)
            {
                var highlighted = index == _highlight;
                foreground = highlighted ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
                background = highlighted ? colorScheme.SelectedFocusedBackground : colorScheme.Background;
                text = " " + _items[index];
            }
            else
            {
                foreground = colorScheme.Foreground;
                background = colorScheme.Background;
                text = string.Empty;
            }

            var rowString = new ConxelString(Fit(text, width), foreground, background);
            buffer.Write(ZOrder, new Position((int)AbsolutePosition.X, (int)AbsolutePosition.Y + i),
                new Size(width, 1), rowString);
        }
    }

    private void Commit(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            Committed?.Invoke(index);
        }
    }

    private void MoveHighlight(int delta) => MoveHighlightTo(_highlight + delta);

    private void MoveHighlightTo(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }

        _highlight = Math.Clamp(index, 0, _items.Count - 1);
        EnsureHighlightVisible();
        StateHasChanged();
    }

    private int Rows() => Math.Max(1, (int)ViewSize.Height);

    private void EnsureHighlightVisible()
    {
        var rows = Rows();
        if (_highlight < _scroll)
        {
            _scroll = _highlight;
        }
        else if (_highlight >= _scroll + rows)
        {
            _scroll = _highlight - rows + 1;
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _items.Count - rows));
    }

    private static string Fit(string text, int width) =>
        width <= 0 ? string.Empty : text.Length > width ? text[..width] : text.PadRight(width);
}
