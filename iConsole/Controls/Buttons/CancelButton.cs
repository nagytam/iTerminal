using iConsole.Commands;
using iConsole.Services;

namespace iConsole.Controls.Buttons;

public class CancelButton : Button
{
    public CancelButton(CancellationTokenSource cancellationTokenSource, IThemeService themeService)
        : base(iConsole.Localization.UiText.Get("Common.Cancel", "Cancel"), new CancelCommand(cancellationTokenSource), themeService)
    {
        HotKeyChar = 'c';
    }
}
