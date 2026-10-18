using iConsole.Buffers;
using iConsole.Layouts;
using iConsole.Data;
using iConsole.Services;

namespace iConsole.Controls.StatusBar;

public class StatusBar : Control
{
    public StatusBar(
        IThemeService themeService)
        : base(themeService)
    {
        Name = nameof(StatusBar);
        CanFocus = false;
        ViewSize = new Size(0, 1);
    }

    public override void OnInitializeLayout()
    {
        if (Parent != null)
        {
            ViewSize = new Size(Parent.ViewSize.Width, 1);
        }
        base.OnInitializeLayout();
    }

    /// <summary>
    /// Stacks the items left to right, in <paramref name="byPriority"/> order of importance, and hides
    /// the ones that no longer fit. The fixed-width items of the editor and viewer bars add up to more
    /// than 80 columns, so on a standard terminal the last of them was cut off mid-word.
    /// </summary>
    protected void StackFitting(IReadOnlyList<Control> inOrder, IReadOnlyList<Control> byPriority)
    {
        var room = (long)ViewSize.Width;
        var shown = new HashSet<Control>();
        foreach (var item in byPriority)
        {
            if (item.ViewSize.Width <= room)
            {
                room -= item.ViewSize.Width;
                shown.Add(item);
            }
        }

        var visible = new List<Control>();
        foreach (var item in inOrder)
        {
            var fits = shown.Contains(item);
            if (item.IsVisible != fits)
            {
                item.IsVisible = fits;
            }
            if (fits)
            {
                visible.Add(item);
            }
        }
        Layout.StackHorizontal(visible);
    }

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        base.Draw(buffer);
    }
}
