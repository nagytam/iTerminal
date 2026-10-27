using iConsole.Buffers;
using iConsole.Commands;
using iConsole.Controls.Buttons;
using iConsole.Data;
using iConsole.Display;
using iConsole.Helper;
using iConsole.Layouts;
using iConsole.Services;

namespace iConsole.Controls.PopUpWindows;

public class PopUpWindow : Window
{
    private IThemeService _themeService { get; set; }
    public override string Title { get; set; }

    /// <summary>The window can be dragged by its title bar and moved with Ctrl+F7 and the arrow keys.</summary>
    public bool IsMovable { get; set; }

    /// <summary>The window can be resized by dragging its bottom right corner and with Ctrl+F8 and the arrow keys.</summary>
    public bool IsResizable { get; set; }

    /// <summary>Scrolls a title that is longer than the room in the top border (see <see cref="Marquee"/>) instead of cutting it.</summary>
    public bool ScrollTitle { get; set; }

    /// <summary>Milliseconds on any fixed clock; replaceable so a test can move the title without waiting.</summary>
    public Func<long> TickSource
    {
        get => _tickSource;
        set
        {
            _tickSource = value ?? (() => Environment.TickCount64);
            // The scroll counts from the moment the clock is chosen.
            _titleStarted = _tickSource();
        }
    }

    private Func<long> _tickSource = () => Environment.TickCount64;
    private long _titleStarted = Environment.TickCount64;

    // Where the user put the window, and how big they made it: layout keeps to them instead of centring again.
    private Position? _userPosition;
    private Size? _userSize;

    private enum WindowDrag
    {
        None,
        Move,
        Resize,
    }

    private WindowDrag _drag;
    private long _grabX;
    private long _grabY;
    private WindowDrag _keyMode;

    /// <summary>True while the window is being moved or resized by the mouse or by Ctrl+F7 or Ctrl+F8.</summary>
    public bool IsBeingMoved => _drag != WindowDrag.None || _keyMode != WindowDrag.None;

    /// <summary>Smallest size a window can be resized to.</summary>
    public Size MinimumSize { get; set; } = new Size(20, 5);

    private Position ClampPosition(long x, long y)
    {
        if (Parent is { ViewSize: { Width: > 0, Height: > 0 } parentSize })
        {
            x = Math.Min(x, Math.Max(0, parentSize.Width - ViewSize.Width));
            y = Math.Min(y, Math.Max(0, parentSize.Height - ViewSize.Height));
        }

        return new Position(Math.Max(0, x), Math.Max(0, y));
    }

    /// <summary>Moves the window to <paramref name="x"/>, <paramref name="y"/> (held on the screen) and keeps it there when the layout runs again.</summary>
    public void MoveTo(long x, long y)
    {
        _userPosition = ClampPosition(x, y);
        Position = _userPosition;
        GetRootControl()?.Refresh();
    }

    /// <summary>Gives the window a new size (held between <see cref="MinimumSize"/> and the room left on the screen) and lays its controls out again.</summary>
    public void ResizeTo(long width, long height)
    {
        long maxWidth = Parent is { ViewSize.Width: > 0 } parent ? Math.Max(MinimumSize.Width, parent.ViewSize.Width - Position.X) : long.MaxValue;
        long maxHeight = Parent is { ViewSize.Height: > 0 } parentHeight ? Math.Max(MinimumSize.Height, parentHeight.ViewSize.Height - Position.Y) : long.MaxValue;
        _userSize = new Size(Math.Clamp(width, MinimumSize.Width, maxWidth), Math.Clamp(height, MinimumSize.Height, maxHeight));
        _userPosition ??= Position;
        ViewSize = _userSize;

        // The layout of a dialog puts the focus on its first control; the control that had it keeps it.
        IControl? focused = GetFocusedControl();
        OnInitializeLayout();
        focused?.SetFocus();
        GetRootControl()?.Refresh();
    }

    /// <summary>
    /// Ctrl+F7 starts moving the window with the arrow keys (Shift takes five cells), Ctrl+F8 resizing it; Enter or Escape ends it.
    /// While one is on, every key belongs to it. Returns whether the key was used.
    /// </summary>
    public bool HandleWindowKey(KeyEvent keyEvent)
    {
        if (_keyMode != WindowDrag.None)
        {
            int step = keyEvent.Shift ? 5 : 1;
            switch (keyEvent.ConsoleKey)
            {
                case ConsoleKey.LeftArrow: Nudge(-step, 0); break;
                case ConsoleKey.RightArrow: Nudge(step, 0); break;
                case ConsoleKey.UpArrow: Nudge(0, -step); break;
                case ConsoleKey.DownArrow: Nudge(0, step); break;
                case ConsoleKey.Enter or ConsoleKey.Escape:
                    _keyMode = WindowDrag.None;
                    break;
            }

            return true;
        }

        if (keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            if (keyEvent.ConsoleKey == ConsoleKey.F7 && IsMovable)
            {
                _keyMode = WindowDrag.Move;
                return true;
            }

            if (keyEvent.ConsoleKey == ConsoleKey.F8 && IsResizable)
            {
                _keyMode = WindowDrag.Resize;
                return true;
            }
        }

        return false;
    }

    private void Nudge(int dx, int dy)
    {
        if (_keyMode == WindowDrag.Move)
        {
            MoveTo(Position.X + dx, Position.Y + dy);
        }
        else
        {
            ResizeTo(ViewSize.Width + dx, ViewSize.Height + dy);
        }
    }

    public PopUpWindow(string title, IThemeService themeService)
        : base(themeService)
    {
        _themeService = themeService;
        Title = title;
        IsVisible = false;
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();

        OnInitializeActionButtons();
        foreach (var popUpWindows in Controls.AsSnapshot()
            .Where(c => c is PopUpWindow)
            .Cast<PopUpWindow>())
        {
            popUpWindows.OnInitializeActionButtons();
        }
    }

    public virtual void OnInitializeActionButtons()
    {
        AddActionButton(new ActionButton("Hide", "[X]", new HideCommand(), _themeService));
    }

    protected void AddActionButton(ActionButton actionButton)
    {
        if (!Controls.AsSnapshot().Any(c => 
            c is ActionButton && 
            (c as ActionButton)?.Title == actionButton.Title))
        {
            AddControl(actionButton);
        }
    }

    public override void OnInitializeZOrder()
    {
        // A PopUpWindow should be above its parent when present. In unit tests
        // the dialog may not be added to a parent control, so avoid throwing
        // when Parent is null and default to ZOrder 1 in that case.
        ZOrder = (Parent?.ZOrder + 1) ?? 1;
        foreach (var control in Controls.AsSnapshot())
        {
            control.OnInitializeZOrder();
        }
        StateHasChanged();
    }

    /// <summary>
    /// Sets <see cref="Control.ViewSize"/> to <paramref name="size"/> and centers this window on
    /// <see cref="Control.Parent"/>, clamping to (0,0) when the window is larger than its parent
    /// (e.g. a fixed-size dialog opened in a narrower/shorter terminal) so Position's non-negative
    /// invariant is never violated.
    /// </summary>
    protected void CenterInParent(Size size)
    {
        // A dialog written for 84 columns lost its right border (and its last column of text) on an
        // 80-column terminal; no popup is ever wider or taller than the screen it sits on.
        if (Parent is { ViewSize: { Width: > 0, Height: > 0 } parentSize })
        {
            size = new Size(Math.Min(size.Width, parentSize.Width), Math.Min(size.Height, parentSize.Height));
        }

        if (_userSize is { } userSize)
        {
            size = new Size(Math.Min(userSize.Width, Parent is { ViewSize.Width: > 0 } p ? p.ViewSize.Width : userSize.Width),
                Math.Min(userSize.Height, Parent is { ViewSize.Height: > 0 } q ? q.ViewSize.Height : userSize.Height));
        }

        ViewSize = size;
        if (_userPosition is { } placed)
        {
            Position = ClampPosition(placed.X, placed.Y);
        }
        else
        {
            Layout.Center(this);
        }
    }

    protected string GetActionButtons()
    {
        var actionButtons = string.Empty;
        foreach (var actionButton in Controls.ToList().Where(c => c is ActionButton).Cast<ActionButton>())
        {
            actionButtons += actionButton.ActionView;
        }
        return actionButtons;
    }

    /// <summary>
    /// A left click on one of the title bar's action buttons (<c>[X]</c>) presses it. The buttons are drawn by the
    /// window, in the last cells of the top border, and have no place of their own, so the one under the pointer is
    /// put there first and then given the click. Returns false when the click is not on a button.
    /// </summary>
    protected async Task<bool> TryClickActionButtonAsync(MouseEvent mouseEvent)
    {
        if (!mouseEvent.LeftClicked || mouseEvent.Position.Y != AbsolutePosition.Y)
        {
            return false;
        }

        var buttons = Controls.AsSnapshot().OfType<ActionButton>().ToList();
        var start = (long)AbsolutePosition.X + (long)ViewSize.Width - 1 - buttons.Sum(b => b.ActionView.Length);
        foreach (var button in buttons)
        {
            var length = button.ActionView.Length;
            if (mouseEvent.Position.X >= start && mouseEvent.Position.X < start + length)
            {
                button.Position = new Position(start - (long)AbsolutePosition.X, 0);
                button.ViewSize = new Size(length, 1);
                return await button.HandleMouseEventAsync(mouseEvent);
            }

            start += length;
        }

        return false;
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (_drag != WindowDrag.None)
        {
            if (mouseEvent.IsRelease)
            {
                _drag = WindowDrag.None;
                return true;
            }

            if (mouseEvent.IsDrag)
            {
                if (_drag == WindowDrag.Move)
                {
                    MoveTo(mouseEvent.Position.X - _grabX, mouseEvent.Position.Y - _grabY);
                }
                else
                {
                    ResizeTo(mouseEvent.Position.X - AbsolutePosition.X + 1, mouseEvent.Position.Y - AbsolutePosition.Y + 1);
                }

                return true;
            }
        }

        if (await TryClickActionButtonAsync(mouseEvent))
        {
            return true;
        }

        if (mouseEvent.LeftClicked && !mouseEvent.IsRelease && !mouseEvent.IsDrag && mouseEvent.WheelDelta == 0)
        {
            long x = mouseEvent.Position.X - AbsolutePosition.X;
            long y = mouseEvent.Position.Y - AbsolutePosition.Y;
            if (IsResizable && x == ViewSize.Width - 1 && y == ViewSize.Height - 1)
            {
                _drag = WindowDrag.Resize;
                return true;
            }

            if (IsMovable && y == 0 && x >= 1 && x < ViewSize.Width - 1)
            {
                _drag = WindowDrag.Move;
                _grabX = x;
                _grabY = y;
                return true;
            }
        }

        return await base.HandleMouseEventAsync(mouseEvent);
    }

    protected override void DrawChrome(IBuffer buffer)
    {
        var colorScheme = _themeService.CurrentColorScheme;
        var visualState = GetVisualState();
        var headerLine = " " + Title + " ";
        var actionButtons = GetActionButtons();
        var titleRoom = (int)ViewSize.Width - 2 - actionButtons.Length;
        if (ScrollTitle && headerLine.Length > titleRoom && titleRoom > 2)
        {
            headerLine = " " + Marquee.Window(Title, titleRoom - 2, TimeSpan.FromMilliseconds(TickSource() - _titleStarted)) + " ";
        }

        headerLine = headerLine.Expand(Border.Horizontal, (int)ViewSize.Width - 2 - actionButtons.Length) + actionButtons;
        Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, headerLine, string.Empty, colorScheme, visualState);
    }
}