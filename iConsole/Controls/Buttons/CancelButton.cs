using iConsole.Commands;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class CancelButton : Button
{
    public CancelButton(CancellationTokenSource cancellationTokenSource, IThemeService themeService)
        : base("Cancel", new CancelCommand(cancellationTokenSource), themeService)
    {
        HotKeyChar = 'c';
    }
}
