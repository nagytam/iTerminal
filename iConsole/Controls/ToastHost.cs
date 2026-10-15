using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Shows the <see cref="INotificationService"/>'s active notifications as a stack of
/// <see cref="Toast"/>s in the top right corner of its parent. Add one to the screen's root and
/// notifications posted anywhere in the application appear, over whatever is on screen — panels,
/// a dialog, an open menu — without taking focus or a key from it.
///
/// <list type="bullet">
/// <item><b>The toasts are synchronised from <see cref="OnIdle"/></b>, on the render thread, by
/// comparing <see cref="INotificationService.Version"/>. A notification is posted from whichever
/// thread finished the work — usually a task worker — and attaching a control there would race the
/// renderer walking the tree.</item>
/// <item><b>Each toast is a control of its own, attached and removed</b> rather than one rectangle
/// redrawn: a layer is never cleared between frames, so a stack that only drew fewer rows would
/// leave the expired toast on screen. Attaching and removing moves <see cref="Control.TreeVersion"/>,
/// which is what makes the renderer repaint from scratch.</item>
/// <item><b>The stack sits one layer above everything else</b> (<see cref="ApplyZOrder"/>), worked
/// out from what is on screen rather than fixed high: the buffer allocates a layer for every
/// Z-order up to the highest in use.</item>
/// </list>
/// </summary>
public class ToastHost : Control
{
    private readonly INotificationService _notificationService;
    private long _syncedVersion = -1;

    public ToastHost(INotificationService notificationService, IThemeService themeService) : base(themeService)
    {
        _notificationService = notificationService;
    }

    /// <summary>The widest a toast gets; a narrower screen gets narrower toasts.</summary>
    public int MaxToastWidth { get; set; } = 50;

    /// <summary>Rows left free above the first toast — the menu bar's row, by default.</summary>
    public int TopOffset { get; set; } = 1;

    /// <summary>Columns left free between the toasts and the right edge.</summary>
    public int RightMargin { get; set; } = 1;

    public override void OnIdle()
    {
        Synchronize();
        base.OnIdle();
    }

    /// <summary>
    /// Brings the toasts in line with the service's active notifications. Runs on the render thread
    /// (see the class remarks); public so a test can do in one call what an idle pass does.
    /// </summary>
    public void Synchronize()
    {
        if (_notificationService is null)
        {
            return;
        }

        var version = _notificationService.Version;
        if (version != _syncedVersion)
        {
            _syncedVersion = version;

            var active = _notificationService.Active;
            var toasts = Controls.AsSnapshot().OfType<Toast>().ToList();
            foreach (var toast in toasts.Where(t => active.All(n => n.Id != t.Notification.Id)))
            {
                toast.Clicked -= OnToastClicked;
                RemoveControl(toast);
            }

            foreach (var notification in active.Where(n => toasts.All(t => t.Notification.Id != n.Id)))
            {
                var toast = new Toast(notification, ThemeService);
                toast.Clicked += OnToastClicked;
                AddControl(toast);
            }

            Arrange();
        }

        if (Controls.Count > 0)
        {
            ApplyZOrder();
        }
    }

    public override void OnInitializeLayout()
    {
        Arrange();
        base.OnInitializeLayout();
    }

    /// <summary>
    /// The stack keeps its own layer: the base implementation would put it on its parent's, under
    /// every dialog.
    /// </summary>
    public override void OnInitializeZOrder()
    {
        ApplyZOrder();
    }

    /// <summary>Stacks the toasts downwards from the top right corner, oldest first, a blank row between.</summary>
    private void Arrange()
    {
        if (Parent is not null)
        {
            Position = new Position(0, 0);
            ViewSize = Parent.ViewSize;
        }

        var available = (int)ViewSize.Width - RightMargin;
        var width = Math.Min(MaxToastWidth, available);
        var y = TopOffset;
        foreach (var toast in Controls.AsSnapshot().OfType<Toast>())
        {
            var height = Toast.MeasureHeight(toast.Notification, width);

            // A toast that does not fit under the others is not drawn rather than drawn over the
            // status lines; it is still counted down, and still in the history.
            var fits = width > 0 && y + height <= ViewSize.Height;
            toast.Position = new Position(Math.Max(0, available - width), y);
            toast.ViewSize = fits ? new Size(width, height) : new Size(0, 0);
            toast.IsVisible = fits;
            y += height + 1;
        }
    }

    /// <summary>
    /// Puts the toasts one layer above the highest one anything else on screen draws on. Checked on
    /// every idle pass while a toast is up, since a dialog can open over the panels at any moment.
    /// </summary>
    private void ApplyZOrder()
    {
        var zOrder = 1;
        if (Parent is not null)
        {
            zOrder = Parent.ZOrder + 1;
            foreach (var sibling in Parent.Controls.AsSnapshot())
            {
                if (!ReferenceEquals(sibling, this) && sibling.IsVisible)
                {
                    zOrder = Math.Max(zOrder, sibling.GetMaxZOrder() + 1);
                }
            }
        }

        var changed = false;
        foreach (var toast in Controls.AsSnapshot())
        {
            if (toast.ZOrder != zOrder)
            {
                toast.ZOrder = zOrder;
                changed = true;
            }
        }

        if (changed)
        {
            // The toasts were drawn into another layer: without a repaint from scratch that
            // picture stays there, under or over the one drawn next.
            InvalidateTree();
        }
    }

    private void OnToastClicked(object? sender, EventArgs e)
    {
        if (sender is Toast toast)
        {
            _notificationService.Dismiss(toast.Notification.Id);
        }
    }
}
