using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Services;
using System.ComponentModel;
using System.Diagnostics;

namespace iConsole.Controls;

[DebuggerDisplay("{Text}")]
public class MenuItem : PopUpWindow
{
    public string Text { get; set; } = string.Empty;
    protected ConcurrentList<MenuItem> MenuItems { get; private set; }

    public bool IsOpened { get; private set; } = false;

    public bool IsSelected { get; private set; } = false;

    /// <summary>The entry's name: <see cref="Text"/> up to the tab that separates its shortcut.</summary>
    public string Label
    {
        get
        {
            var tab = Text.IndexOf('\t');
            return tab < 0 ? Text : Text[..tab];
        }
    }

    /// <summary>
    /// The shortcut drawn in the entry's right-hand column: the one written after the tab in
    /// <see cref="Text"/> ("Create File\tShift+F4"), else the one its command is bound to. Never both —
    /// the column used to append the command's bare <see cref="ConsoleKey"/> to a label that already
    /// named the chord, so the File menu read "Create File	Shift+F4 F4", tab character and all.
    /// </summary>
    public string HotKeyText
    {
        get
        {
            var tab = Text.IndexOf('\t');
            if (tab >= 0)
            {
                return Text[(tab + 1)..];
            }
            var command = Commands.FirstOrDefault();
            if (command is null)
            {
                return string.Empty;
            }
            var display = command.KeyDisplayText;
            return string.IsNullOrEmpty(display) ? Command.FormatKey(command.Key) : display;
        }
    }

    public bool IsSeparator => Text == "-";

    /// <summary>The entry as one line: the label, then the shortcut right-aligned in its column.</summary>
    public string TextWithHotKey => FormatEntry(Label.Length, HotKeyText.Length);

    internal string FormatEntry(int labelWidth, int hotKeyWidth)
    {
        var hotKey = HotKeyText;
        if (hotKeyWidth == 0)
        {
            return Label.PadRight(labelWidth);
        }
        return Label.PadRight(labelWidth) + "  " + hotKey.PadLeft(hotKeyWidth);
    }

    private (int LabelWidth, int HotKeyWidth) GetEntryColumns()
    {
        var entries = MenuItems.Where(m => !m.IsSeparator).ToList();
        return entries.Count == 0
            ? (0, 0)
            : (entries.Max(m => m.Label.Length), entries.Max(m => m.HotKeyText.Length));
    }

    public MenuItem(string text, ICommand? command, IEnumerable<MenuItem> menuItems, IThemeService themeService)
        : base(text, themeService)
    {
        Text = text;
        MenuItems = [.. menuItems];
        IsVisible = false;
        if (command != null)
        {
            AddCommand(command);
        }
        foreach (var menuItem in MenuItems)
        {
            AddControl(menuItem);
        }

        if (this is not MenuBar)
        {
            OnInitializeActionButtons();
        }
    }

    public override void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton("CloseMenu", "[X]", new CloseMenuCommand(), ThemeService));
    }

    public override void OnInitializeZOrder()
    {
        base.OnInitializeZOrder();
        ZOrder = 0;
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            ViewSize = new Size(Parent.ViewSize.Width, 1);
        }
        base.OnInitializeLayout();
        var subMenuItems = Controls
            .AsSnapshot()
            .Where(c => c is MenuItem)
            .Cast<MenuItem>();
        long width = 0;
        if (this is MenuBar)
        {
            width = Parent?.ViewSize.Width ?? 0;
        }
        else if (subMenuItems.Any())
        {
            var (labelWidth, hotKeyWidth) = GetEntryColumns();
            width = labelWidth + (hotKeyWidth > 0 ? hotKeyWidth + 2 : 0) + 4;
            // Wide enough for the title in the top border too.
            width = Math.Max(width, Text.Length + 4);
        }
        else
        {
            width = (uint)Text.Length + 1;
        }
        var height = IsOpened ? (uint)(VisibleRowCount(subMenuItems.Count()) + 2) : 1;
        ViewSize = new Size(width, height);
        Position = new Position(0, 0);
        if (Parent is MenuBar menuBar)
        {
            Position = new Position(menuBar.GetDropDownColumn(this, width), 1);
        }
    }

    /// <summary>First entry drawn when the drop-down is taller than the screen allows.</summary>
    private int _firstVisibleEntry;

    /// <summary>
    /// How many entries the drop-down shows: all of them, unless the screen is too short — the Tools
    /// menu has more entries than an 80x24 terminal has rows, and its tail (and bottom border) was
    /// drawn off screen. Then it shows a window of the list that follows the selection.
    /// </summary>
    private int VisibleRowCount(int entryCount)
    {
        if (this is MenuBar || entryCount == 0)
        {
            return entryCount;
        }
        var rootHeight = (int)GetRootControl().ViewSize.Height;
        if (rootHeight <= 0)
        {
            return entryCount;
        }
        // Row 0 is the menu bar, then the drop-down's two border rows.
        var room = Math.Max(3, rootHeight - 1 - 2);
        return Math.Min(entryCount, room);
    }

    /// <summary>Moves the window so the selected entry is inside it.</summary>
    private void KeepSelectionVisible(int visibleRows)
    {
        var selected = MenuItems.AsSnapshot().ToList().FindIndex(m => m.IsSelected);
        var maxFirst = Math.Max(0, MenuItems.Count - visibleRows);
        if (selected >= 0)
        {
            if (selected < _firstVisibleEntry)
            {
                _firstVisibleEntry = selected;
            }
            else if (selected >= _firstVisibleEntry + visibleRows)
            {
                _firstVisibleEntry = selected - visibleRows + 1;
            }
        }
        _firstVisibleEntry = Math.Clamp(_firstVisibleEntry, 0, maxFirst);
    }

    /// <summary>
    /// The column a top-level menu's drop-down opens at: under its own name in the bar, pulled back
    /// left only as far as it takes to stay on screen. Every drop-down used to open at column 1, so
    /// the Tools menu appeared under "File".
    /// </summary>
    internal long GetDropDownColumn(MenuItem topLevel, long dropDownWidth)
    {
        long column = 0;
        foreach (var menuItem in MenuItems)
        {
            if (ReferenceEquals(menuItem, topLevel))
            {
                break;
            }
            column += menuItem.Text.Length + 1;
        }
        var screenWidth = (long)ViewSize.Width;
        if (screenWidth > 0 && column + dropDownWidth > screenWidth)
        {
            column = Math.Max(0, screenWidth - dropDownWidth);
        }
        return column;
    }

    protected void AddMenuItem(MenuItem menuItem)
    {
        menuItem.Parent = this;
        MenuItems.Add(menuItem);
        Controls.Add(menuItem);
    }

    /// <summary>
    /// The layer an open drop-down draws on: one above whatever opened it. Every menu item sits on
    /// layer 0 so the bar itself is part of the screen — and an open drop-down used to stay there
    /// too, on the same layer as the file panels, which repaint at least once a second and painted
    /// straight over it. Ctrl+F gave the keyboard to a File menu nobody could see.
    /// </summary>
    private void LiftAboveTheScreen()
    {
        if (this is MenuBar)
        {
            return;
        }
        ZOrder = Parent is MenuItem parentMenu and not MenuBar ? parentMenu.ZOrder + 1 : 1;
    }

    public virtual void OpenMenu()
    {
        Title = Text;
        IsOpened = true;
        // Ensure this menu is visible when opened so child visibility
        // checks that depend on parent visibility succeed.
        IsVisible = true;
        if (Parent is not null)
        {
            var parent = Parent as MenuItem;
            if (parent is not null && !parent.IsOpened)
            {
                parent.IsOpened = true;
                parent.LiftAboveTheScreen();
                // Also ensure the parent is visible when opening a child menu.
                parent.IsVisible = true;
            }
        }
        // After the parent: a submenu goes one layer above the menu it opened from.
        LiftAboveTheScreen();
        var menuItem = MenuItems.FirstOrDefault();
        if (menuItem is not null)
        {
            menuItem.IsVisible = true;
            menuItem.SetSelected();
        }
        OnInitializeLayout();
        StateHasChanged();
        FindMenuBar()?.SyncModalState();
    }

    public virtual void CloseMenu()
    {
        IsOpened = false;
        if (this is not MenuBar)
        {
            ZOrder = 0;
        }
        foreach (var menuItem in MenuItems)
        {
            menuItem.IsVisible = false;
            menuItem.ClearSelected();
            if (menuItem.IsOpened)
            {
                menuItem.CloseMenu();
            }
        }
        GetRootControl().Refresh();
        OnInitializeLayout();
        StateHasChanged();
        FindMenuBar()?.SyncModalState();
    }

    // Walks up to the owning MenuBar so opening/closing any item in the tree can
    // keep MenuBar.IsModal in sync (it drives whether the desktop routes all key
    // events straight into the menu instead of the focused panel).
    private MenuBar? FindMenuBar()
    {
        IControl? current = this;
        while (current is not null)
        {
            if (current is MenuBar menuBar)
            {
                return menuBar;
            }
            current = current.Parent;
        }
        return null;
    }

    // The direct child of the MenuBar that owns the currently open chain — used to switch
    // between top-level menus with Left/Right without having to walk back up from scratch.
    private MenuItem GetTopLevelMenu()
    {
        var current = this;
        while (current.Parent is MenuItem parentItem && parentItem is not MenuBar)
        {
            current = parentItem;
        }
        return current;
    }

    // Closes the entire open menu chain. Prefers closing from the owning MenuBar (so its
    // IsOpened/IsModal reset correctly) and falls back to the top-level item when this tree
    // has no MenuBar ancestor (e.g. a MenuItem used standalone in tests).
    private void CloseWholeMenu()
    {
        MenuItem closer = FindMenuBar() ?? GetTopLevelMenu();
        closer.CloseMenu();
    }

    private List<MenuItem> SelectableMenuItems => MenuItems.Where(m => m.Text != "-").ToList();

    private void MoveSelection(int direction)
    {
        var siblings = SelectableMenuItems;
        if (siblings.Count == 0)
        {
            return;
        }
        var current = siblings.FirstOrDefault(m => m.IsSelected);
        var index = current is null ? 0 : siblings.IndexOf(current);
        var nextIndex = ((index + direction) % siblings.Count + siblings.Count) % siblings.Count;
        siblings[nextIndex].SetSelected();
    }

    private void SwitchTopLevelMenu(int direction)
    {
        if (Parent is not MenuBar menuBar)
        {
            return;
        }
        var siblings = menuBar.MenuItems.Where(m => m.Text != "-").ToList();
        var index = siblings.IndexOf(this);
        if (index < 0 || siblings.Count == 0)
        {
            return;
        }
        var nextIndex = ((index + direction) % siblings.Count + siblings.Count) % siblings.Count;
        CloseMenu();
        siblings[nextIndex].OpenMenu();
    }

    private async Task ActivateSelectedAsync()
    {
        var selected = MenuItems.FirstOrDefault(m => m.IsSelected);
        if (selected is null)
        {
            return;
        }
        if (selected.MenuItems.Count > 0)
        {
            selected.OpenMenu();
            return;
        }
        var command = selected.Commands.FirstOrDefault();
        if (command is not null)
        {
            await command.RunAsync(null);
        }
        CloseWholeMenu();
    }

    /// <summary>The submenu of this item that is currently open, if any.</summary>
    internal MenuItem? OpenChild => MenuItems.FirstOrDefault(m => m.IsOpened);

    /// <summary>
    /// A left click inside this open drop-down: the entry under the pointer is selected and activated, as
    /// Enter would do — a leaf runs its command and closes the menu, a branch opens its submenu. A click on
    /// a separator, the border or the empty rows is swallowed. Returns false when the click is not on this
    /// drop-down at all.
    /// </summary>
    internal async Task<bool> HandleDropDownClickAsync(MouseEvent mouseEvent)
    {
        if (this is MenuBar || !IsOpened || !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            return false;
        }

        var row = (int)(mouseEvent.Position.Y - AbsolutePosition.Y);
        var column = (int)(mouseEvent.Position.X - AbsolutePosition.X);
        var entryIndex = _firstVisibleEntry + row - 1;
        if (row >= 1 && row <= (int)ViewSize.Height - 2 && column >= 1 && column <= (int)ViewSize.Width - 2
            && entryIndex >= 0 && entryIndex < MenuItems.Count && !MenuItems[entryIndex].IsSeparator)
        {
            MenuItems[entryIndex].SetSelected();
            await ActivateSelectedAsync();
        }

        return true;
    }

    private async Task OpenOrAdvanceAsync()
    {
        var selected = MenuItems.FirstOrDefault(m => m.IsSelected);
        if (selected is not null && selected.MenuItems.Count > 0)
        {
            selected.OpenMenu();
            return;
        }
        if (Parent is MenuBar)
        {
            SwitchTopLevelMenu(1);
        }
        await Task.CompletedTask;
    }

    private void CloseOrRetreat()
    {
        if (Parent is MenuBar)
        {
            SwitchTopLevelMenu(-1);
            return;
        }
        CloseMenu();
    }

    public virtual void SetSelected()
    {
        if (Parent != null)
        {
            if (Parent is MenuItem parentMenu)
            {
                foreach (var menuItem in parentMenu.MenuItems.Where(m => m.IsSelected))
                {
                    menuItem.ClearSelected();
                }
            }
            IsSelected = true;
            Parent.Refresh();
        }
        else
        {
            IsSelected = true;
            StateHasChanged();
        }
    }

    public virtual void ClearSelected()
    {
        if (Parent != null)
        {
            IsSelected = false;
            if (Parent is MenuItem parentMenu)
            {
                foreach (var menuItem in parentMenu.MenuItems.Where(m => m.IsSelected))
                {
                    menuItem.ClearSelected();
                }
            }
            Parent.Refresh();
        }
        else
        {
            IsSelected = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// A menu's Ctrl+letter mnemonic gives way to a command bound to that same chord anywhere on the
    /// screen. The menu bar is offered keys ahead of the controls below it, so the Edit menu's Ctrl+E
    /// used to take the key from the unit converter — whose own button, and the Tools menu entry
    /// for it, both told the user to press Ctrl+E. A mnemonic is a shortcut to a menu that ←/→
    /// also reach once any menu is open; the command has no other key.
    /// </summary>
    public override Task<bool> HandleHotKeyEventAsync(KeyEvent keyEvent)
    {
        if (HotKeyChar is not null && keyEvent.Control && IsBoundToACommand(GetRootControl(), keyEvent))
        {
            return Task.FromResult(false);
        }
        return base.HandleHotKeyEventAsync(keyEvent);
    }

    private static bool IsBoundToACommand(IControl control, KeyEvent keyEvent)
    {
        // A menu's own entries name their commands' keys, but run them only from the menu.
        if (control is MenuItem || control is not Control concrete)
        {
            return false;
        }

        foreach (var command in concrete.Commands.AsSnapshot())
        {
            if (command.Key is not null && command.Key.IsIdentical(keyEvent))
            {
                return true;
            }
        }

        foreach (var child in concrete.Controls.AsSnapshot())
        {
            if (IsBoundToACommand(child, keyEvent))
            {
                return true;
            }
        }
        return false;
    }

    public override Task TriggerHotKeyAsync()
    {
        // base.TriggerHotKey();

        if (!IsOpened)
        {
            OpenMenu();
        }
        else
        {
            CloseMenu();
        }
        return Task.CompletedTask;
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (IsOpened)
        {
            // Always defer to the deepest open submenu — MenuBar and every open ancestor
            // along the chain all report IsOpened simultaneously, but only the innermost
            // one holds the selection the user is currently navigating.
            var openChild = MenuItems.FirstOrDefault(m => m.IsOpened);
            if (openChild is not null)
            {
                return await openChild.HandleKeyEventAsync(keyEvent);
            }

            if (!keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
            {
                switch (keyEvent.ConsoleKey)
                {
                    case ConsoleKey.UpArrow:
                        MoveSelection(-1);
                        return true;
                    case ConsoleKey.DownArrow:
                        MoveSelection(1);
                        return true;
                    case ConsoleKey.Enter:
                        await ActivateSelectedAsync();
                        return true;
                    case ConsoleKey.RightArrow:
                        await OpenOrAdvanceAsync();
                        return true;
                    case ConsoleKey.LeftArrow:
                        CloseOrRetreat();
                        return true;
                    case ConsoleKey.Escape:
                        CloseWholeMenu();
                        return true;
                }
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override void Draw(IBuffer buffer)
    {
        if (!IsOpened || this is MenuBar)
        {
            var position = AbsolutePosition;
            var colorScheme = ThemeService.CurrentColorScheme;
            var parentIsOpened = (Parent as MenuItem)?.IsOpened ?? false;
            var width = (int)ViewSize.Width;
            if (parentIsOpened && Parent is not null)
            {
                width = (int)Parent.ViewSize.Width - 2;
            }

            ConxelString s;
            if (this is MenuBar)
            {
                // Highlight each top-level item's Ctrl-hotkey letter (e.g. the 'F' in "File")
                // so the menu advertises how it's opened from the keyboard.
                s = new ConxelString();
                var isFirst = true;
                foreach (var menuItem in MenuItems)
                {
                    if (!isFirst)
                    {
                        s.Append(" ", colorScheme.Foreground, colorScheme.Background);
                    }
                    isFirst = false;
                    var segment = new ConxelString(menuItem.Text, colorScheme.Foreground, colorScheme.Background);
                    segment.HighlightHotKey(menuItem.HotConsoleKey, menuItem.HotKeyChar,
                        colorScheme.SelectedFocusedForeground, colorScheme.Background);
                    s.Append(segment);
                }
                s.Expand(width, colorScheme.Foreground, colorScheme.Background);
            }
            else
            {
                var text = string.Empty;
                foreach (var menuItem in MenuItems)
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        text += " ";
                    }
                    text += menuItem.Text;
                }
                text = text.ExpandRight((int)ViewSize.Width);
                s = new ConxelString(text, colorScheme.Foreground, colorScheme.Background);
            }
            buffer.Write(ZOrder, position, ViewSize, s);
        }
        else
        {
            var colorScheme = ThemeService.CurrentColorScheme;
            var visualState = GetVisualState();
            var headerLine = " " + Title + " ";

            var actionButtons = GetActionButtons();
            var line = Border.TopLeft +
                        Border.Horizontal.Repeat((int)ViewSize.Width - actionButtons.Length - 2) +
                        actionButtons +
                        Border.TopRight;
            line = StringHelper.AlignCenter(line, headerLine);
            var s = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder, AbsolutePosition, new Size(s.Count, 1), s);
            var (labelWidth, hotKeyWidth) = GetEntryColumns();
            var innerWidth = (int)ViewSize.Width - 2;
            var visibleRows = (int)ViewSize.Height - 2;
            KeepSelectionVisible(visibleRows);
            var hiddenAbove = _firstVisibleEntry > 0;
            var hiddenBelow = _firstVisibleEntry + visibleRows < MenuItems.Count;
            for (uint y = 1; y < ViewSize.Height - 1; y++)
            {
                s = new ConxelString(Border.Vertical, colorScheme.Foreground, colorScheme.Background);
                buffer.Write(ZOrder,
                    new Position(AbsolutePosition.X, y + AbsolutePosition.Y),
                    new Size(s.Count, 1), s
                    );
                var entryIndex = _firstVisibleEntry + (int)y - 1;
                if (entryIndex < MenuItems.Count)
                {
                    var item = MenuItems[entryIndex];
                    if (item.IsSeparator)
                    {
                        s = new ConxelString(Border.Horizontal.Repeat(innerWidth), colorScheme.Foreground, colorScheme.Background);
                    }
                    else
                    {
                        // The selected entry keeps its "<…>" marks (readable on a monochrome terminal)
                        // and is drawn in the selection colours, like a focused list row.
                        var entry = item.FormatEntry(labelWidth, hotKeyWidth);
                        line = (item.IsSelected ? "<" + entry + ">" : " " + entry + " ").ExpandRight(innerWidth);
                        s = item.IsSelected
                            ? new ConxelString(line, colorScheme.SelectedFocusedForeground, colorScheme.SelectedFocusedBackground)
                            : new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
                    }
                    buffer.Write(ZOrder,
                        new Position(AbsolutePosition.X + 1, y + AbsolutePosition.Y),
                        new Size(s.Count, 1), s
                        );
                }
                // A "▲" / "▼" in the right border says the list goes on beyond the screen.
                var rightEdge = y == 1 && hiddenAbove ? "▲" : y == ViewSize.Height - 2 && hiddenBelow ? "▼" : Border.Vertical;
                s = new ConxelString(rightEdge, colorScheme.Foreground, colorScheme.Background);
                buffer.Write(ZOrder,
                    new Position(AbsolutePosition.X + ViewSize.Width - 1, y + AbsolutePosition.Y), 
                    new Size(s.Count, 1), s
                    );
            }
            line = Border.BottomLeft +
                        Border.Horizontal.Repeat((int)ViewSize.Width - 2) +
                        Border.BottomRight;
            var lineStr = new ConxelString(line, colorScheme.Foreground, colorScheme.Background);
            buffer.Write(ZOrder,
                new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), 
                new Size(lineStr.Count, 1), lineStr
                );
        }

        var openedSubMenu = MenuItems.FirstOrDefault(m => m.IsOpened);
        openedSubMenu?.Draw(buffer);        
    }
}
