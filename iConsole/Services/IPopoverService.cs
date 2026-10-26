using iConsole.Controls;
using iConsole.Data;

namespace iConsole.Services;

/// <summary>
/// Opens popovers — transient overlays such as context menus and drop-downs — and sees to it that only one is open at a
/// time: opening a second one closes the first, and the one on screen can be closed from anywhere (a dialog about to open,
/// a window resize). It also places them, turning one to the other side of its target where it would not fit.
/// </summary>
public interface IPopoverService
{
    /// <summary>The popover on screen, or null.</summary>
    Popup? Current { get; }

    bool IsOpen { get; }

    /// <summary>
    /// Opens <paramref name="popover"/> with its corner at <paramref name="point"/> (the pointer, for a context menu), or
    /// against the screen's edges where it does not fit. Whatever was open is closed first.
    /// </summary>
    Task ShowAtPointAsync(Popup popover, IControl owner, Position point);

    /// <summary>
    /// Opens <paramref name="popover"/> beside <paramref name="target"/> on <paramref name="side"/>, or the opposite side
    /// where it does not fit. Whatever was open is closed first.
    /// </summary>
    Task ShowBesideAsync(Popup popover, IControl owner, IControl target, PopoverSide side = PopoverSide.Below);

    /// <summary>Closes the popover on screen, if any.</summary>
    void CloseCurrent();
}
