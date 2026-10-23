using iConsole.Buffers;
using iConsole.Data;
using iConsole.Dialogs;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Interfaces;
using iConsole.Services;
using iTerminal.Core.Controls;
using iTerminal.Core.Services.FileAssociations;
using iTerminal.Core.Services.UserMenu;

namespace iTerminal.Core.Dialogs;

/// <summary>
/// Shift+F10: the user menu, mc's F2. The entries come from the menu file in the data folder
/// (<see cref="IUserMenuService.FilePath"/>), limited to those that apply to the file under the
/// cursor. ↑/↓ and Enter choose, an entry's own key chooses it at once, F4 opens the file for
/// editing. A chosen entry runs on the whole terminal, as Ctrl+O does, and the panels are read
/// again when it is over.
/// </summary>
public class UserMenuDialog : Dialog
{
    private readonly IUserMenuService _menuService;
    private readonly IFileLauncher _launcher;
    private readonly INotificationService? _notifications;
    private readonly InfoLines _lines;
    private IReadOnlyList<UserMenuItem> _items = [];
    private UserMenuContext _context = new(string.Empty, string.Empty, string.Empty, []);
    private int _cursor;
    private int _top;

    public UserMenuDialog(
        IUserMenuService menuService,
        IFileLauncher launcher,
        IThemeService themeService,
        INotificationService? notifications = null)
        : base(nameof(UserMenuDialog), themeService)
    {
        _menuService = menuService;
        _launcher = launcher;
        _notifications = notifications;
        Title = "User menu";
        _lines = new InfoLines(themeService);
        AddControl(_lines);
    }

    private string EmptyMessage => "  (no entries - F4 edits " + Path.GetFileName(_menuService.FilePath) + ")";

    /// <summary>The entries on offer, for a test.</summary>
    public IReadOnlyList<UserMenuItem> Items => _items;

    /// <summary>The entry under the cursor, or null when there are none.</summary>
    public UserMenuItem? Selected => _cursor >= 0 && _cursor < _items.Count ? _items[_cursor] : null;

    /// <summary>The run started last; nothing waits on it, a test can.</summary>
    public Task LastRun { get; private set; } = Task.CompletedTask;

    /// <summary>Sets what the entries apply to; call before showing the dialog.</summary>
    public void Load(UserMenuContext context)
    {
        _context = context;
        _items = _menuService.GetItems(context.CurrentFile);
        _cursor = 0;
        _top = 0;
        UpdateLines();
    }

    public override void OnInitializeLayout()
    {
        if (Parent is not null)
        {
            var rows = Math.Max(3, _items.Count + 1);
            // Wide enough for the longest title — or, with no entries, for the message that says where to add some,
            // which was cut off in a dialog sized for nothing.
            var width = Math.Max(Math.Max(40, EmptyMessage.Length + 6), _items.Select(i => i.Title.Length).DefaultIfEmpty(0).Max() + 12);
            CenterInParent(new Size(
                Math.Min(width, Math.Max(20, (int)Parent.ViewSize.Width - 4)),
                Math.Min(rows + 4, Math.Max(6, (int)Parent.ViewSize.Height - 2))));
        }

        _lines.Position = new Position(2, 1);
        _lines.ViewSize = new Size(Math.Max(1, ViewSize.Width - 4), Math.Max(1, ViewSize.Height - 3));
        base.OnInitializeLayout();
        UpdateLines();
    }

    private void UpdateLines()
    {
        var lines = new List<InfoLines.Line>();
        if (_items.Count == 0)
        {
            lines.Add(new(EmptyMessage, null));
        }

        var visible = Math.Max(1, (int)_lines.ViewSize.Height);

        // The window moves only when the cursor leaves it: it used to be placed so the cursor was on its last row, so
        // every Up in a long menu scrolled the list a row instead of moving the cursor.
        if (_cursor < _top)
        {
            _top = _cursor;
        }
        else if (_cursor >= _top + visible)
        {
            _top = _cursor - visible + 1;
        }

        _top = Math.Clamp(_top, 0, Math.Max(0, _items.Count - visible));
        for (var i = _top; i < _items.Count && lines.Count < visible; i++)
        {
            var item = _items[i];
            var key = item.Hotkey == ' ' ? " " : item.Hotkey.ToString();
            lines.Add(new($"{(i == _cursor ? "▶" : " ")} {key}  {item.Title}", i == _cursor ? Color.Yellow : null));
        }

        _lines.Lines = lines;
        StateHasChanged();
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.KeyPressType == KeyPressType.Down && !keyEvent.Alt && !keyEvent.Control)
        {
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.UpArrow:
                    _cursor = Math.Max(0, _cursor - 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.DownArrow:
                    _cursor = Math.Min(Math.Max(0, _items.Count - 1), _cursor + 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.Home:
                    _cursor = 0;
                    UpdateLines();
                    return true;
                case ConsoleKey.End:
                    _cursor = Math.Max(0, _items.Count - 1);
                    UpdateLines();
                    return true;
                case ConsoleKey.Enter:
                    RunSelected();
                    return true;
                case ConsoleKey.F4:
                    Close();
                    LastRun = _launcher.OpenWithSystemDefaultAsync(_menuService.FilePath);
                    return true;
            }

            if (!keyEvent.Shift && keyEvent.KeyChar != '\0' && !char.IsWhiteSpace(keyEvent.KeyChar))
            {
                var index = _items.ToList().FindIndex(i => i.Hotkey == keyEvent.KeyChar);
                if (index >= 0)
                {
                    _cursor = index;
                    RunSelected();
                    return true;
                }
            }
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    /// <summary>
    /// The wheel moves the cursor a row; a click on an entry runs it, as its key or Enter would. Before this the menu
    /// could only be driven from the keyboard.
    /// </summary>
    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (mouseEvent.WheelDelta != 0 && AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            _cursor = Math.Clamp(_cursor + (mouseEvent.WheelDelta > 0 ? -1 : 1), 0, Math.Max(0, _items.Count - 1));
            UpdateLines();
            return true;
        }

        if (mouseEvent.LeftClicked
            && (long)mouseEvent.Position.X >= (long)_lines.AbsolutePosition.X
            && (long)mouseEvent.Position.X < (long)_lines.AbsolutePosition.X + _lines.ViewSize.Width)
        {
            var row = (int)((long)mouseEvent.Position.Y - (long)_lines.AbsolutePosition.Y);
            var index = _top + row;
            if (row >= 0 && row < _lines.ViewSize.Height && index < _items.Count)
            {
                _cursor = index;
                RunSelected();
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    /// <summary>Closes the menu and runs the entry under the cursor.</summary>
    public void RunSelected()
    {
        if (Selected is not { } item)
        {
            return;
        }

        var context = _context;
        Close();
        LastRun = Task.Run(async () =>
        {
            var failure = await _menuService.RunAsync(item, context);
            if (failure is not null)
            {
                _notifications?.Notify(item.Title, failure, NotificationSeverity.Error);
            }
        });
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        base.DrawChrome(buffer);
        var colorScheme = ThemeService.CurrentColorScheme;
        GetColor(colorScheme, GetVisualState(), out var foreground, out _);
        var width = (int)ViewSize.Width;
        var footer = " Enter:run  F4:edit  Esc:close ".FitHints(Math.Max(0, width - 4));
        var bottom = Border.BottomLeft + Border.Horizontal.Repeat(width - 2) + Border.BottomRight;
        var bottomLine = new ConxelString(StringHelper.AlignCenter(bottom, footer), foreground, colorScheme.Background);
        buffer.Write(ZOrder, new Position(AbsolutePosition.X, AbsolutePosition.Y + ViewSize.Height - 1), new Size(bottomLine.Count, 1), bottomLine);
    }
}
