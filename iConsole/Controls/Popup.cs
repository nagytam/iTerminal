using iConsole.Data;
using iConsole.Display;
using IBuffer = iConsole.Buffers.IBuffer;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>Where a <see cref="Popup"/> is placed relative to the control that opened it.</summary>
public enum PopupPlacement
{
    Below,
    Above,
    Left,
    Right,
    Manual,
}

/// <summary>
/// A transient, focus-capturing overlay anchored to an owning control — the reusable primitive
/// behind dropdowns, menus and small confirmations. It attaches itself to the root above everything
/// else, captures input while open, dismisses on an outside click or Escape, and restores focus to
/// the owner on close (the same focus hand-off <see cref="iConsole.Dialogs.Dialog"/> uses). Host
/// arbitrary content with <see cref="SetContent"/> or <see cref="Control.AddControl"/>.
/// </summary>
public class Popup : Window
{
    private int _desiredZOrder = 1;
    private IControl? _content;
    private bool _pushedFocus;
    private int _isShowing;

    public Popup(IThemeService themeService) : base(themeService)
    {
        CanFocus = true;
        IsVisible = false;
        ViewSize = new Size(20, 6);
    }

    public IControl? Owner { get; private set; }
    public bool IsOpen { get; private set; }
    public PopupPlacement Placement { get; set; } = PopupPlacement.Below;

    /// <summary>
    /// Where a <see cref="PopupPlacement.Manual"/> popup's top-left corner goes, in the root's
    /// coordinates — a context menu opens at the pointer. Kept on screen like every other placement.
    /// Without it a manual popup is left wherever it was last put.
    /// </summary>
    public Position? Anchor { get; set; }
    public bool HasBorder { get; set; } = true;

    /// <summary>Draws a drop shadow down the right and along the bottom of the popup (see <see cref="Shadow"/>).</summary>
    public bool HasShadow { get; set; }
    public bool CloseOnOutsideClick { get; set; } = true;
    public bool CloseOnEscape { get; set; } = true;

    /// <summary>
    /// When true (the default) the overlay captures input and focus while open — the right model for
    /// dropdowns and menus. When false it floats and elevates above siblings but leaves focus and
    /// input with the owner, which keeps driving it (e.g. an inline autocompletion list shown while
    /// the user keeps typing in the owning text box).
    /// </summary>
    public bool Modal { get; set; } = true;

    public event EventHandler? Closed;

    public Position ContentPosition => HasBorder ? new Position(1, 1) : new Position(0, 0);

    public Size ContentSize => HasBorder
        ? new Size(Math.Max(0, (int)ViewSize.Width - 2), Math.Max(0, (int)ViewSize.Height - 2))
        : new Size(ViewSize.Width, ViewSize.Height);

    /// <summary>Hosts a single control sized to fill the overlay's interior.</summary>
    public void SetContent(IControl content)
    {
        if (_content is not null)
        {
            RemoveControl(_content);
        }

        _content = content;
        AddControl(content);
        LayoutContent();
        StateHasChanged();
    }

    /// <summary>
    /// Opens the overlay with its top-left corner at <paramref name="anchor"/> in the root's coordinates, not beside the
    /// owner. Used where the thing it points at is a position, such as a context menu at the pointer or a tooltip.
    /// </summary>
    public Task ShowAtAsync(IControl owner, Position anchor)
    {
        Placement = PopupPlacement.Manual;
        Anchor = anchor;
        return ShowAsync(owner);
    }

    /// <summary>Opens the overlay anchored to <paramref name="owner"/>, capturing focus and input.</summary>
    public async Task ShowAsync(IControl owner)
    {
        // The claim has to be taken before the first await, not left to IsOpen, which is only set
        // once the whole show has finished. An autocompletion list is opened from two places at
        // once as a matter of course — the keystroke that shows the owning text box, and the
        // background match update that keystroke triggers — and both used to get past the IsOpen
        // check and call AddControl, attaching the same overlay to the root twice. Closing then
        // detached one of them, leaving an invisible orphan on the root for the rest of the run:
        // one more every time the dialog was opened, each raising the root's maximum Z-order and
        // so the number of buffer layers the renderer allocates.
        if (Interlocked.Exchange(ref _isShowing, 1) == 1)
        {
            return;
        }

        Owner = owner;
        var root = owner.GetRootControl();

        // Captured before we attach: GetFocusedControl falls back to the root, and the popup itself
        // must not be the thing found.
        var focused = Modal ? root.GetFocusedControl() : null;

        // Sit above everything currently on screen. Computed before we attach so this popup
        // isn't counted in the maximum.
        _desiredZOrder = root.GetMaxZOrder() + 1;

        Parent = root;

        // A modal overlay remembers whoever had focus so it can be restored on close (mirrors
        // Dialog). A non-modal overlay leaves focus with the owner and so captures nothing. Pushed
        // only once the popup has its parent: the stack is the root's, and before that this popup
        // *is* its own root — the entry went onto a stack nobody would ever pop. Remember that we
        // pushed so Close pops exactly our entry and never a sibling overlay's.
        if (focused is not null && focused.IsFocused)
        {
            FocusedStack.Push(focused);
            _pushedFocus = true;
        }

        IsModal = Modal;
        IsEnabled = true;
        IsVisible = true;
        root.AddControl(this);

        await OnInitializeAsync();

        // A Close that lands while this show is still initialising wins. It has already detached the
        // overlay and released the claim above, so carrying on here would mark a popup that is no
        // longer on the screen as open — and `IsOpen` is what every later Close and every idempotence
        // check reads, so it would stay that way for the rest of the run: closed to look at, open to
        // ask. An autocompletion list overlaps the two as a matter of course, being shown from a
        // background match update and dismissed from a key handler.
        if (Volatile.Read(ref _isShowing) == 0)
        {
            IsVisible = false;
            IsModal = false;
            Parent?.RemoveControl(this);
            if (_pushedFocus && FocusedStack.TryPop(out var interrupted))
            {
                _pushedFocus = false;
                interrupted?.SetFocus();
            }
            return;
        }

        PlaceRelativeToOwner();
        IsOpen = true;

        if (Modal)
        {
            if (HasFocusableControl(out var focusable) && focusable is not null)
            {
                focusable.SetFocus();
            }
            else
            {
                SetFocus();
            }
        }

        StateHasChanged();
    }

    public override void OnInitializeZOrder()
    {
        // Keep the elevated z computed in ShowAsync (the base would reset us to the root's layer),
        // and put our content on the same layer.
        ZOrder = _desiredZOrder;
        foreach (var control in Controls.AsSnapshot())
        {
            control.OnInitializeZOrder();
        }
        StateHasChanged();
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();
        LayoutContent();
    }

    public override void Close()
    {
        // Released first: a close that lands while ShowAsync is still awaiting its initialisation
        // has to leave the overlay openable again, and has to detach it — by then it is already a
        // child of the root even though IsOpen is still false.
        Interlocked.Exchange(ref _isShowing, 0);

        if (!IsOpen)
        {
            base.Close();
            Parent?.RemoveControl(this);
            return;
        }

        IsOpen = false;
        IsModal = false;
        IsVisible = false;

        var parent = Parent;
        parent?.RemoveControl(this);

        if (_pushedFocus && FocusedStack.TryPop(out var previous))
        {
            _pushedFocus = false;
            previous?.SetFocus();
        }

        // Repaint every layer so the cells we overlaid are restored (mirrors Control.Hide()).
        (parent ?? Owner)?.GetRootControl().Refresh(0);
        Closed?.Invoke(this, EventArgs.Empty);
    }

    public override IEnumerable<(string Key, string Description)> GetKeyHints()
    {
        if (IsOpen && CloseOnEscape)
        {
            yield return ("Esc", "Close the popup");
        }
    }

    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (IsOpen && CloseOnEscape
            && keyEvent.ConsoleKey == ConsoleKey.Escape
            && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            Close();
            return true;
        }

        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task<bool> HandleMouseEventAsync(MouseEvent mouseEvent)
    {
        if (IsOpen && CloseOnOutsideClick
            && (mouseEvent.LeftClicked || mouseEvent.RightClicked)
            && !AbsoluteBoundRect.Contains(mouseEvent.Position))
        {
            Close();
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

        var colorScheme = ThemeService.CurrentColorScheme;
        if (HasShadow)
        {
            Shadow.Draw(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        }

        iConsole.Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);

        if (HasBorder)
        {
            var header = string.IsNullOrEmpty(Title) ? string.Empty : " " + Title + " ";
            DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, header, string.Empty, colorScheme, GetVisualState());
        }

        foreach (var control in Controls.AsSnapshot().Where(control => control is not null && control.IsVisible))
        {
            try
            {
                control.Draw(buffer);
            }
            catch
            {
                control.Refresh();
            }
        }
    }

    private void LayoutContent()
    {
        if (_content is null)
        {
            return;
        }

        _content.Position = ContentPosition;
        _content.ViewSize = ContentSize;
    }

    private void PlaceRelativeToOwner()
    {
        if (Owner is null || (Placement == PopupPlacement.Manual && Anchor is null))
        {
            return;
        }

        var ownerX = (int)Owner.AbsolutePosition.X;
        var ownerY = (int)Owner.AbsolutePosition.Y;
        var ownerWidth = (int)Owner.ViewSize.Width;
        var ownerHeight = (int)Owner.ViewSize.Height;

        var (x, y) = Placement switch
        {
            PopupPlacement.Below => (ownerX, ownerY + ownerHeight),
            PopupPlacement.Above => (ownerX, ownerY - (int)ViewSize.Height),
            PopupPlacement.Right => (ownerX + ownerWidth, ownerY),
            PopupPlacement.Left => (ownerX - (int)ViewSize.Width, ownerY),
            PopupPlacement.Manual => ((int)Anchor!.X, (int)Anchor.Y),
            _ => (ownerX, ownerY),
        };

        // Keep the overlay on screen.
        if (Parent is not null)
        {
            var maxX = Math.Max(0, (int)Parent.ViewSize.Width - (int)ViewSize.Width);
            var maxY = Math.Max(0, (int)Parent.ViewSize.Height - (int)ViewSize.Height);
            x = Math.Clamp(x, 0, maxX);
            y = Math.Clamp(y, 0, maxY);
        }

        Position = new Position(Math.Max(0, x), Math.Max(0, y));
    }
}
