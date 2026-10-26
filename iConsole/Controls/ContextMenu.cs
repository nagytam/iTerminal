using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A menu that opens at the pointer, or at a control, over everything else. Set it as <see cref="Control.ContextMenu"/>
/// and a right-click on that control (or Shift+F10 / the Menu key while it has focus) opens it; or open it yourself with
/// <see cref="OpenAtAsync"/>. Up, Down, Home and End move, Enter and Space choose, a letter jumps to the next entry that
/// starts with it, Esc or a click outside closes it. The entry's <see cref="ContextMenuItem.Execute"/> runs once the menu
/// is closed and focus is back where it was, so it can open a dialog.
/// </summary>
public class ContextMenu : Popup
{
    private readonly IPopoverService _popovers;
    private readonly List<ContextMenuItem> _items = [];
    private int _selected = -1;

    public ContextMenu(IPopoverService popoverService, IThemeService themeService) : base(themeService)
    {
        _popovers = popoverService;
    }

    /// <summary>The entries, top to bottom. Change them before opening the menu.</summary>
    public IList<ContextMenuItem> Items => _items;

    /// <summary>Index of the highlighted entry, or -1 when there is none.</summary>
    public int SelectedIndex => _selected;

    /// <summary>Opens the menu with its corner at <paramref name="point"/>, in the root's coordinates.</summary>
    public async Task OpenAtAsync(IControl owner, Position point)
    {
        PrepareToOpen(owner);
        await _popovers.ShowAtPointAsync(this, owner, point);
    }

    /// <summary>Opens the menu beside <paramref name="target"/>, as a drop-down opens below its button.</summary>
    public async Task OpenBesideAsync(IControl owner, IControl target, PopoverSide side = PopoverSide.Below)
    {
        PrepareToOpen(owner);
        await _popovers.ShowBesideAsync(this, owner, target, side);
    }

    /// <summary>Chooses the highlighted entry: closes the menu, then runs the entry.</summary>
    public async Task ActivateSelectedAsync()
    {
        if (_selected < 0 || _selected >= _items.Count || !_items[_selected].CanSelect)
        {
            return;
        }

        ContextMenuItem item = _items[_selected];
        Close();
        if (item.Execute is not null)
        {
            await item.Execute();
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (!IsOpen || keyEvent.Control || keyEvent.Alt)
        {
            return await base.HandleKeyEventAsync(keyEvent);
        }

        switch (keyEvent.ConsoleKey)
        {
            case ConsoleKey.UpArrow:
                Move(-1);
                return true;
            case ConsoleKey.DownArrow:
                Move(1);
                return true;
            case ConsoleKey.Home:
                _selected = -1;
                Move(1);
                return true;
            case ConsoleKey.End:
                _selected = _items.Count;
                Move(-1);
                return true;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                await ActivateSelectedAsync();
                return true;
        }

        if (!keyEvent.Shift && char.IsLetterOrDigit(keyEvent.KeyChar) && JumpToLetter(keyEvent.KeyChar))
        {
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (!IsOpen || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return await base.HandleMouseEventAsync(mouseEvent);
        }

        int index = IndexAt(mouseEvent.Position);
        if (mouseEvent.IsHover || mouseEvent.IsDrag)
        {
            if (index >= 0 && _items[index].CanSelect && index != _selected)
            {
                _selected = index;
                StateHasChanged();
            }

            return true;
        }

        if (mouseEvent.LeftClicked && !mouseEvent.IsRelease)
        {
            if (index >= 0 && _items[index].CanSelect)
            {
                _selected = index;
                await ActivateSelectedAsync();
            }

            return true;
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        base.Draw(buffer);

        ColorScheme colorScheme = ThemeService.CurrentColorScheme;
        int inner = (int)ContentSize.Width;
        int titleWidth = _items.Where(item => !item.IsSeparator).Select(item => item.Title.Length).DefaultIfEmpty(0).Max();
        for (int i = 0; i < _items.Count && i < ContentSize.Height; i++)
        {
            ContextMenuItem item = _items[i];
            bool isSelected = i == _selected;
            Color foreground = isSelected ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            Color background = isSelected ? colorScheme.SelectedFocusedBackground : colorScheme.Background;

            string text = item.IsSeparator
                ? new string('─', inner)
                : FormatRow(item, titleWidth, inner);
            ConxelString line = new(text, foreground, background, isBold: false);
            if (!item.IsEnabled && !item.IsSeparator)
            {
                // The default scheme's disabled colour is the text colour, so the entry is made faint as well.
                line.Edit(0, line.Count, conxel =>
                {
                    conxel.IsDim = true;
                    conxel.ForegroundColor = colorScheme.DisabledForeground;
                    return conxel;
                });
            }

            Position position = new(AbsolutePosition.X + ContentPosition.X, AbsolutePosition.Y + ContentPosition.Y + i);
            buffer.Write(ZOrder, position, new Size(inner, 1), line);
        }
    }

    private static string FormatRow(ContextMenuItem item, int titleWidth, int inner)
    {
        string row = " " + item.Title.PadRight(titleWidth);
        if (item.Shortcut.Length > 0)
        {
            int gap = Math.Max(2, inner - row.Length - item.Shortcut.Length - 1);
            row += new string(' ', gap) + item.Shortcut;
        }

        return row.Length > inner ? row[..inner] : row.PadRight(inner);
    }

    private void PrepareToOpen(IControl owner)
    {
        // Sized to the longest entry, but never wider or taller than the screen.
        Size screen = owner.GetRootControl().ViewSize;
        int titleWidth = _items.Where(item => !item.IsSeparator).Select(item => item.Title.Length).DefaultIfEmpty(0).Max();
        int shortcutWidth = _items.Select(item => item.Shortcut.Length).DefaultIfEmpty(0).Max();
        long width = 1 + titleWidth + (shortcutWidth > 0 ? 2 + shortcutWidth : 0) + 1 + 2;
        long height = _items.Count + 2;
        ViewSize = new Size(Math.Min(Math.Max(width, 12), screen.Width), Math.Min(height, screen.Height));

        _selected = -1;
        Move(1);
    }

    /// <summary>Moves to the next entry the keyboard can stop on in <paramref name="step"/>'s direction, without wrapping.</summary>
    private void Move(int step)
    {
        for (int i = _selected + step; i >= 0 && i < _items.Count; i += step)
        {
            if (_items[i].CanSelect)
            {
                _selected = i;
                StateHasChanged();
                return;
            }
        }
    }

    private bool JumpToLetter(char letter)
    {
        for (int offset = 1; offset <= _items.Count; offset++)
        {
            int i = (_selected + offset) % _items.Count;
            ContextMenuItem item = _items[i];
            if (item.CanSelect && item.Title.Length > 0 && char.ToUpperInvariant(item.Title[0]) == char.ToUpperInvariant(letter))
            {
                _selected = i;
                StateHasChanged();
                return true;
            }
        }

        return false;
    }

    private int IndexAt(Position position)
    {
        long row = position.Y - AbsolutePosition.Y - ContentPosition.Y;
        return row >= 0 && row < _items.Count ? (int)row : -1;
    }
}
