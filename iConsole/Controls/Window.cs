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
        DrawBorder(buffer, ZOrder, AbsolutePosition, ViewSize, header, string.Empty, colorScheme, visualState);
    }

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
