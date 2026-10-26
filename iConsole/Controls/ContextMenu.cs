using iConsole.Data;
using iConsole.Display;
using iConsole.Services;
using IBuffer = iConsole.Buffers.IBuffer;

namespace iConsole.Controls;

/// <summary>
/// A menu that opens at the pointer, or at a control, over everything else. Set it as <see cref="Control.ContextMenu"/>
/// and a right-click on that control (or Shift+F10 / the Menu key while it has focus) opens it; or open it yourself with
/// <see cref="OpenAtAsync"/>.
/// <list type="bullet">
/// <item>Up, Down, Home and End move; Enter, Space or a click choose; Esc or a click outside closes it.</item>
/// <item>An entry with <see cref="ContextMenuItem.Items"/> is a submenu: Right, Enter or a click opens it beside the entry,
/// Left or Esc closes it again, and choosing an entry in it closes the whole chain.</item>
/// <item>An entry with an <c>&amp;</c> in its title has a hot key: pressing the letter chooses it. A letter that is nobody's hot
/// key jumps to the next entry that starts with it.</item>
/// <item>An entry with <see cref="ContextMenuItem.IsChecked"/> set is a switch, drawn with a check mark; choosing it turns it over.</item>
/// </list>
/// The entry's <see cref="ContextMenuItem.Execute"/> runs once the menu is closed and focus is back where it was, so it can open a dialog.
/// </summary>
public class ContextMenu : Popup
{
    private readonly IPopoverService _popovers;
    private readonly List<ContextMenuItem> _items = [];
    private int _selected = -1;
    private ContextMenu? _child;
    private int _childIndex = -1;

    public ContextMenu(IPopoverService popoverService, IThemeService themeService) : base(themeService)
    {
        _popovers = popoverService;
    }

    /// <summary>The entries, top to bottom. Change them before opening the menu.</summary>
    public IList<ContextMenuItem> Items => _items;

    /// <summary>Index of the highlighted entry, or -1 when there is none.</summary>
    public int SelectedIndex => _selected;

    /// <summary>The menu this one is the submenu of, or null for a menu opened on its own.</summary>
    public ContextMenu? ParentMenu { get; private set; }

    /// <summary>The submenu open from this menu, or null.</summary>
    public ContextMenu? OpenSubmenu => _child is { IsOpen: true } ? _child : null;

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

    /// <summary>
    /// Chooses the highlighted entry. A submenu is opened; any other entry closes the menu (the whole chain, for an entry of a
    /// submenu), turns it over if it is a switch, and runs it.
    /// </summary>
    public async Task ActivateSelectedAsync()
    {
        if (_selected < 0 || _selected >= _items.Count || !_items[_selected].CanSelect)
        {
            return;
        }

        ContextMenuItem item = _items[_selected];
        if (item.HasSubmenu)
        {
            await OpenSubmenuAsync(_selected);
            return;
        }

        if (item.IsChecked is { } isChecked)
        {
            item.IsChecked = !isChecked;
        }

        CloseChain();
        if (item.Execute is not null)
        {
            await item.Execute();
        }
    }

    /// <summary>Opens the submenu of the entry at <paramref name="index"/> beside it, if it has one and can be chosen.</summary>
    public async Task OpenSubmenuAsync(int index)
    {
        if (!IsOpen || Owner is null || index < 0 || index >= _items.Count || !_items[index].CanSelect || !_items[index].HasSubmenu)
        {
            return;
        }

        if (_child is { IsOpen: true })
        {
            if (_childIndex == index)
            {
                return;
            }

            _child.Close();
        }

        _child ??= new ContextMenu(_popovers, ThemeService) { ParentMenu = this };
        _child._items.Clear();
        _child._items.AddRange(_items[index].Items);
        _childIndex = index;
        _child.PrepareToOpen(Owner);

        // Beside the entry, with the submenu's first row level with it; on the left where the right has no room.
        Size screen = Owner.GetRootControl().ViewSize;
        Rectangle row = new((int)AbsolutePosition.X, (int)(AbsolutePosition.Y + ContentPosition.Y + index) - 1, (int)ViewSize.Width, 1);
        Position position = PopoverPlacement.Place(row, _child.ViewSize, screen, PopoverSide.Right);
        await _child.ShowAtAsync(Owner, position);
    }

    /// <summary>Closes the menu at the top of the chain of submenus this one belongs to, and with it every submenu below.</summary>
    public void CloseChain()
    {
        ContextMenu root = this;
        while (root.ParentMenu is not null)
        {
            root = root.ParentMenu;
        }

        root.Close();
    }

    public override void Close()
    {
        if (_child is { IsOpen: true })
        {
            _child.Close();
        }

        _childIndex = -1;
        base.Close();
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
            case ConsoleKey.RightArrow:
                if (_selected >= 0 && _selected < _items.Count && _items[_selected].HasSubmenu)
                {
                    await OpenSubmenuAsync(_selected);
                    return true;
                }

                break;
            case ConsoleKey.LeftArrow:
                if (ParentMenu is not null)
                {
                    Close();
                    return true;
                }

                break;
            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                await ActivateSelectedAsync();
                return true;
        }

        if (!keyEvent.Shift && char.IsLetterOrDigit(keyEvent.KeyChar)
            && (await ChooseByHotKeyAsync(keyEvent.KeyChar) || JumpToLetter(keyEvent.KeyChar)))
        {
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        // A submenu is modal, so it gets every event. One over its parent is the parent's; one anywhere else closes the chain.
        if (ParentMenu is { } parent && IsOpen && !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            if (parent.AbsoluteBoundRect.Contains(mouseEvent.Position))
            {
                return await parent.HandleMouseEventAsync(mouseEvent);
            }

            if (mouseEvent.LeftClicked || mouseEvent.RightClicked)
            {
                CloseChain();
            }

            return true;
        }

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
                if (_child is { IsOpen: true } && _childIndex != index)
                {
                    _child.Close();
                }

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
        (int checkWidth, int titleWidth, int rightWidth) = Measure();
        for (int i = 0; i < _items.Count && i < ContentSize.Height; i++)
        {
            ContextMenuItem item = _items[i];
            bool isSelected = i == _selected;
            Color foreground = isSelected ? colorScheme.SelectedFocusedForeground : colorScheme.Foreground;
            Color background = isSelected ? colorScheme.SelectedFocusedBackground : colorScheme.Background;

            string text = item.IsSeparator
                ? new string('─', inner)
                : FormatRow(item, checkWidth, titleWidth, rightWidth, inner);
            ConxelString line = new(text, foreground, background);
            if (!item.IsSeparator)
            {
                if (!item.IsEnabled)
                {
                    // The default scheme's disabled colour is the text colour, so the entry is made faint as well.
                    line.Edit(0, line.Count, conxel =>
                    {
                        conxel.IsDim = true;
                        conxel.ForegroundColor = colorScheme.DisabledForeground;
                        return conxel;
                    });
                }
                else if (item.HotKeyIndex >= 0)
                {
                    // The letter that chooses the entry, underlined.
                    int cell = 1 + checkWidth + item.HotKeyIndex;
                    line.Edit(cell, cell + 1, conxel =>
                    {
                        conxel.IsUnderline = true;
                        return conxel;
                    });
                }
            }

            Position position = new(AbsolutePosition.X + ContentPosition.X, AbsolutePosition.Y + ContentPosition.Y + i);
            buffer.Write(ZOrder, position, new Size(inner, 1), line);
        }
    }

    /// <summary>The widths of the check column, the titles and the right-hand column (a shortcut, or the arrow of a submenu).</summary>
    private (int Check, int Title, int Right) Measure()
    {
        List<ContextMenuItem> entries = [.. _items.Where(item => !item.IsSeparator)];
        int check = entries.Any(item => item.IsChecked is not null) ? 2 : 0;
        int title = entries.Select(item => item.Text.Length).DefaultIfEmpty(0).Max();
        int right = entries.Select(item => item.HasSubmenu ? 1 : item.Shortcut.Length).DefaultIfEmpty(0).Max();
        return (check, title, right);
    }

    private static string FormatRow(ContextMenuItem item, int checkWidth, int titleWidth, int rightWidth, int inner)
    {
        string check = checkWidth == 0 ? string.Empty : item.IsChecked == true ? "✓ " : "  ";
        string row = " " + check + item.Text.PadRight(titleWidth);
        string right = item.HasSubmenu ? "▶" : item.Shortcut;
        if (right.Length > 0)
        {
            int gap = Math.Max(2, inner - row.Length - right.Length - 1);
            row += new string(' ', gap) + right;
        }

        return row.Length > inner ? row[..inner] : row.PadRight(inner);
    }

    private void PrepareToOpen(IControl owner)
    {
        // Sized to the longest entry, but never wider or taller than the screen.
        Size screen = owner.GetRootControl().ViewSize;
        (int check, int title, int right) = Measure();
        long width = 1 + check + title + (right > 0 ? 2 + right : 0) + 1 + 2;
        long height = _items.Count + 2;
        ViewSize = new Size(Math.Min(Math.Max(width, 12), screen.Width), Math.Min(height, screen.Height));

        _selected = -1;
        _childIndex = -1;
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

    /// <summary>
    /// Chooses the entry whose hot key is <paramref name="letter"/>. When several share it, the first press moves to the next
    /// of them and does not choose, as a menu usually does. False when nobody has that hot key.
    /// </summary>
    private async Task<bool> ChooseByHotKeyAsync(char letter)
    {
        List<int> matches = [];
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].CanSelect && _items[i].HotKey is char hotKey
                && char.ToUpperInvariant(hotKey) == char.ToUpperInvariant(letter))
            {
                matches.Add(i);
            }
        }

        if (matches.Count == 0)
        {
            return false;
        }

        if (matches.Count == 1)
        {
            _selected = matches[0];
            await ActivateSelectedAsync();
            return true;
        }

        _selected = matches.FirstOrDefault(i => i > _selected, matches[0]);
        StateHasChanged();
        return true;
    }

    private bool JumpToLetter(char letter)
    {
        for (int offset = 1; offset <= _items.Count; offset++)
        {
            int i = (_selected + offset) % _items.Count;
            ContextMenuItem item = _items[i];
            string text = item.Text;
            if (item.CanSelect && text.Length > 0 && char.ToUpperInvariant(text[0]) == char.ToUpperInvariant(letter))
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
