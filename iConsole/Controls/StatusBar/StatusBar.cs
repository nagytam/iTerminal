using iConsole.Buffers;
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

    public override void Draw(IBuffer buffer)
    {
        var colorScheme = ThemeService.CurrentColorScheme;
        Buffers.Buffer.Clear(buffer, ZOrder, AbsolutePosition, ViewSize, colorScheme);
        base.Draw(buffer);
    }
}
