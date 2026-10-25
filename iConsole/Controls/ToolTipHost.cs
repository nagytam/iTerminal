using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls;

/// <summary>
/// Shows the <see cref="Control.ToolTipText"/> of the control under the pointer once the pointer has rested on it for
/// <see cref="Delay"/>. Add one to the screen's root, last, so its tooltip is drawn above everything else.
///
/// <list type="bullet">
/// <item><b>It reads the pointer from <see cref="IToolTipService"/></b>, which the input consumer fills in for every
/// pointer event. It never takes focus or input: the tooltip is a non-modal <see cref="Popup"/> that closes on the next
/// click or key, not on an outside click.</item>
/// <item><b>It works on the render thread, from <see cref="OnIdle"/></b>, like <see cref="ToastHost"/>, and it measures
/// the delay on <see cref="ITimeProvider"/>, so a test can move past the delay without waiting for it.</item>
/// <item><b>The innermost control with text wins,</b> so a button inside a panel with its own tooltip shows the button's.</item>
/// </list>
/// </summary>
public class ToolTipHost : Control
{
    private readonly IToolTipService _toolTipService;
    private readonly ITimeProvider _timeProvider;
    private long _syncedVersion = -1;
    private Control? _target;
    private long _targetSinceMilliseconds;
    private Popup? _tip;

    // The text the open tip was built from, so a change to the target's text can be seen and the tip rebuilt.
    private string? _shownText;

    public ToolTipHost(IToolTipService toolTipService, ITimeProvider timeProvider, IThemeService themeService) : base(themeService)
    {
        _toolTipService = toolTipService;
        _timeProvider = timeProvider;
    }

    /// <summary>How long the pointer must rest on a control before its tooltip appears.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.FromMilliseconds(600);

    /// <summary>The control whose tooltip is shown, or waiting to be shown.</summary>
    public Control? Target => _target;

    /// <summary>True while a tooltip is on screen.</summary>
    public bool IsShowing => _tip is { IsOpen: true };

    public override void OnIdle()
    {
        Synchronize();
        base.OnIdle();
    }

    /// <summary>
    /// Brings the tooltip in line with the pointer. Runs on the render thread (see the class remarks); public so a test
    /// can do in one call what an idle pass does.
    /// </summary>
    public void Synchronize()
    {
        var version = _toolTipService.Version;
        if (version != _syncedVersion)
        {
            _syncedVersion = version;
            var pointer = _toolTipService.PointerPosition;
            var target = pointer is null ? null : FindTarget(GetRootControl() as Control, pointer);
            if (target != _target)
            {
                HideTip();
                _target = target;
                _targetSinceMilliseconds = _timeProvider.MonotonicMilliseconds;
            }
        }

        // The text can change while its tip is on screen: the stale tip comes down and the delay starts again for the new text.
        if (_tip is not null && _target is not null && _shownText != _target.ToolTipText)
        {
            HideTip();
            _targetSinceMilliseconds = _timeProvider.MonotonicMilliseconds;
        }

        if (_target is null || _tip is not null || string.IsNullOrEmpty(_target.ToolTipText) || !_target.IsParentVisible())
        {
            return;
        }

        var restedFor = _timeProvider.MonotonicMilliseconds - _targetSinceMilliseconds;
        if (restedFor >= Delay.TotalMilliseconds && _toolTipService.PointerPosition is { } pointerPosition)
        {
            ShowTip(_target, pointerPosition);
        }
    }

    /// <summary>The innermost visible control under <paramref name="position"/> that has tooltip text, or null.</summary>
    public static Control? FindTarget(Control? control, Position position)
    {
        if (control is null)
        {
            return null;
        }

        var children = control.Controls.AsSnapshot();
        for (var i = children.Count - 1; i >= 0; i--)
        {
            if (children[i] is Control child && child.IsVisible && child.AbsoluteBoundRect.Contains(position))
            {
                var deeper = FindTarget(child, position);
                if (deeper is not null)
                {
                    return deeper;
                }
            }
        }

        return !string.IsNullOrEmpty(control.ToolTipText) && control.AbsoluteBoundRect.Contains(position)
            ? control
            : null;
    }

    private void ShowTip(Control target, Position pointer)
    {
        var text = " " + target.ToolTipText + " ";
        var root = GetRootControl();
        // One line of text inside a border; wider than the screen, it is cut at the edge rather than wrapped.
        var width = (long)Math.Min(text.Length + 2, Math.Max(3, root.ViewSize.Width));
        const long height = 3;
        var tip = new Popup(ThemeService)
        {
            HasBorder = true,
            Modal = false,
            CloseOnOutsideClick = false,
            CloseOnEscape = false,
            ViewSize = new Size(width, height),
        };
        tip.SetContent(new Label(text, ThemeService));
        _tip = tip;
        _shownText = target.ToolTipText;

        // Just below and to the right of the pointer, so the pointer never covers the text. Where that runs off the screen the
        // tip turns to the left of the pointer or above it: Popup would only pull it back on screen, over the pointer.
        long x = pointer.X + 1;
        if (x + width > root.ViewSize.Width)
        {
            x = pointer.X - width;
        }

        long y = pointer.Y + 1;
        if (y + height > root.ViewSize.Height)
        {
            y = pointer.Y - height;
        }

        _ = tip.ShowAtAsync(target, new Position(Math.Max(0, x), Math.Max(0, y)));
    }

    private void HideTip()
    {
        if (_tip is null)
        {
            return;
        }

        var tip = _tip;
        _tip = null;
        _shownText = null;
        tip.Close();
    }
}
