namespace iConsole.Controls;

/// <summary>
/// A control that can put a transient sub-state of its own on screen — a suggestion list, a drop-down
/// — which <b>Esc dismisses before Esc means "close the dialog"</b>. That order is the whole point of
/// the interface: <see cref="iConsole.Dialogs.Dialog"/> claims Esc ahead of its children on purpose,
/// so that no text box or list can leave the user with a dialog they cannot get out of, and without a
/// declared exception an open suggestion list had no way of being dismissed on its own — the first Esc
/// took the whole dialog with it.
///
/// It is an interface rather than a check for <see cref="AutoCompletion"/> because the dialog has no
/// business knowing which controls float things over it, and because the overlay is not a child of the
/// dialog at all: a <see cref="Popup"/> attaches itself to the root, so nothing in the dialog's own
/// tree can be closed to make it go away.
/// </summary>
public interface ITransientOverlayHost
{
    /// <summary>True while this control has an overlay on screen that Esc should take down.</summary>
    bool IsOverlayOpen { get; }

    /// <summary>
    /// Takes the overlay off the screen, leaving the control itself alone and usable — dismissing a
    /// suggestion list is not the same as being finished with the box it belongs to.
    /// </summary>
    void CloseOverlay();

    /// <summary>
    /// Closes the innermost open overlay anywhere under <paramref name="control"/> (itself included)
    /// and reports whether there was one. Children are searched before the control that holds them,
    /// and in reverse order, for the same reason <see cref="IControl.GetModalChildControl"/> does it
    /// that way: what is nested deepest and attached last is what the user is looking at, so it is
    /// what a single Esc has to peel off first.
    /// </summary>
    static bool TryCloseInnermostOverlay(IControl control)
    {
        foreach (var child in control.Controls.AsSnapshot().Reverse())
        {
            if (TryCloseInnermostOverlay(child))
            {
                return true;
            }
        }

        if (control is ITransientOverlayHost host && host.IsOverlayOpen)
        {
            host.CloseOverlay();
            return true;
        }
        return false;
    }
}
