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
    private readonly List<Control> _leftOut = [];

    /// <summary>
    /// <see cref="Control.ShowAsync"/> makes every child visible after the layout pass, which put back
    /// the items the layout had just left out — stacked on top of the first one.
    /// </summary>
    public override async Task ShowAsync()
    {
        await base.ShowAsync();
        foreach (var item in _leftOut.ToList())
        {
            item.IsVisible = false;
        }
    }

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
        _leftOut.Clear();
        foreach (var item in inOrder)
        {
            var fits = shown.Contains(item);
            // Assigned unconditionally: the getter also reports a hidden parent, and comparing against
            // it left an item that did not fit visible, stacked over the first one, whenever the bar was
            // laid out before its dialog was shown. The setter ignores a value it already holds.
            item.IsVisible = fits;
            if (fits)
            {
                visible.Add(item);
            }
            else
            {
                _leftOut.Add(item);
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
