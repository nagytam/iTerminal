using iConsole.Commands;
using iConsole.Controls;
using iConsole.Controls.PopUpWindows;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Dialogs;

public class Dialog : PopUpWindow
{
    public Dialog(string title, IThemeService themeService) 
        : base(title, themeService)
    {
        IsModal = true;
        IsVisible = false;
        AddCommand(new CloseCommand());
        AddCommand(new TabForwardCommand());
        AddCommand(new TabBackwardCommand());
    }

    public override async Task OnInitializeAsync()
    {
        await base.OnInitializeAsync();
    }

    public override void OnInitializeZOrder()
    {
        var zOrder = (Parent?.ZOrder + 1) ?? 1;
        base.OnInitializeZOrder();
    }

    public override void OnInitializeActionButtons()
    {
        // AddActionButton(new ActionButton("Close", "[X]", new CloseCommand(), ThemeService));
    }

    public override void OnInitializeLayout()
    {
        base.OnInitializeLayout();

        if (Controls.Any(c => c.CanFocus))
        {
            Controls.First(c => c.CanFocus).SetFocus();
        }
    }

    /// <summary>
    /// Esc is claimed here, <b>ahead of the children</b>, rather than offered to the focused control
    /// first. That is what makes it the one key that always means the same thing: a dialog holding a
    /// text box, a list with its own Esc binding or a page that has grown a sub-state cannot swallow
    /// the key and leave the user with a dialog they cannot dismiss. Whether the dialog on screen
    /// is the one this key reaches at all is <c>Desktop.GetModalChildControl</c>'s job — the topmost
    /// modal wins, so an overlay opened over a dialog closes before the dialog underneath it.
    ///
    /// The one declared exception is an <see cref="ITransientOverlayHost"/> with something on screen —
    /// a suggestion list, a drop-down. Esc peels that off first and leaves the dialog standing, which
    /// is what Esc means everywhere else and what the key is reached for while typing. It is not a
    /// child swallowing the key: the overlay is closed from here, one layer per press, so the next Esc
    /// closes the dialog whether or not the control that owns the list would have cooperated.
    /// </summary>
    public override async Task<bool> HandleKeyEventAsync(KeyEvent keyEvent)
    {
        if (keyEvent.ConsoleKey == ConsoleKey.Escape && !keyEvent.Control && !keyEvent.Alt && !keyEvent.Shift)
        {
            if (ITransientOverlayHost.TryCloseInnermostOverlay(this))
            {
                StateHasChanged();
                return true;
            }
            Close();
            return true;
        }
        return await base.HandleKeyEventAsync(keyEvent);
    }

    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        IsEnabled = true;
        IsVisible = true;
        IsModal = true;
        SetFocus();
    }

    /// <summary>How often an open dialog repaints on its own, for what background work changed.</summary>
    internal static readonly TimeSpan IdleRepaintInterval = TimeSpan.FromMilliseconds(50);
    private long _lastIdleRepaint;

    /// <summary>
    /// Repaints the dialog for whatever background work changed — a search that landed, a progress
    /// bar, a git command's output — at most 20 times a second. It used to repaint on every idle
    /// pass, and an idle pass is what follows a frame in which nothing was drawn: so the dialog's
    /// repaint ended the idle, the next pass drew it again, and an open dialog redrew itself
    /// hundreds of times a second for as long as it stayed open.
    /// </summary>
    public override void OnIdle()
    {
        base.OnIdle();
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastIdleRepaint) >= IdleRepaintInterval.TotalMilliseconds)
        {
            Interlocked.Exchange(ref _lastIdleRepaint, now);
            StateHasChanged();
        }
    }

    public override void Close()
    {
        base.Close();

        Parent?.RemoveControl(this);
        if (!FocusedStack.IsEmpty)
        {
            FocusedStack.TryPop(out IControl? focusedControl);
            focusedControl?.SetFocus();
        }

        GetRootControl()?.Refresh();
    }
}
