using iConsole.Buffers;
using iConsole.Data;
using iConsole.Display;
using iConsole.Services;

namespace iConsole.Controls;

public class Window(IThemeService themeService)
    : Control(themeService)
{
    public virtual string Title { get; set; } = string.Empty;

    /// <summary>Draws a drop shadow down the right and along the bottom of the window (see <see cref="Shadow"/>).</summary>
    public bool HasShadow { get; set; }

    public override void Draw(IBuffer buffer)
    {
        if (ViewSize.IsEmpty)
        {
            return;
        }

        if (HasShadow)
        {
            Shadow.Draw(buffer, ZOrder, AbsolutePosition, ViewSize, ThemeService.CurrentColorScheme);
        }

        DrawChrome(buffer);
        base.Draw(buffer);
    }

    /// <summary>
    /// Draws the border/title chrome. Overridden by <see cref="PopUpWindows.PopUpWindow"/> to add
    /// action buttons instead of being re-drawn wholesale, so a subclass's border (and the title in
    /// it) isn't immediately overwritten by this base implementation running again afterwards.
    /// </summary>
    protected virtual void DrawChrome(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        var visualState = GetVisualState();
        var header = string.IsNullOrEmpty(Title) ? string.Empty : " " + Title + " ";
        DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, header, string.Empty, colorScheme, visualState,
            HighlightFocusedBorder);
    }

    /// <summary>
    /// Whether the border takes <see cref="ColorScheme.FocusedBorder"/> while the window has focus. Off by default;
    /// the file panels and the bottom bars turn it on.
    /// </summary>
    protected virtual bool HighlightFocusedBorder => false;

    /// <summary>
    /// The cells a frame takes on each side of the window: 1 when the window is large enough to hold one, else 0 (a
    /// one-row bar has no room for a frame, so its content keeps the whole area).
    /// </summary>
    protected int FrameInset => ViewSize.Width >= 3 && ViewSize.Height >= 3 ? 1 : 0;

    public override void GetColor(ColorScheme colorScheme, VisualState visualState, out Color foreground, out Color background)
    {
        switch (visualState)
        {
            case VisualState.Focused:
                foreground = colorScheme.SelectedFocusedForeground;
                background = colorScheme.Background;
                break;
            case VisualState.Disabled:
                foreground = colorScheme.DisabledForeground;
                background = colorScheme.DisabledBackground;
                break;
            default:
                foreground = colorScheme.Foreground;
                background = colorScheme.Background;
                break;
        }
    }
}
